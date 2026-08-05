using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record InitiateCommissionPaymentResponse(
    Guid CommissionPaymentId,
    string CheckoutUrl,
    string? ProviderToken,
    DateTimeOffset ExpiresAt,
    long AmountMinor);

public sealed record InitiateCommissionPaymentCommand(Guid OwnerUserId, Guid StatementId, string? IdempotencyKey)
    : ICommand<InitiateCommissionPaymentResponse>;

public sealed class InitiateCommissionPaymentCommandValidator : AbstractValidator<InitiateCommissionPaymentCommand>
{
    public InitiateCommissionPaymentCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.StatementId).NotEmpty();
    }
}

/// <summary>
/// RFC-012 madde 5b — PayTR online komisyon ödemesi İSKELETİ. Provider bilerek DAİMA Mock'a
/// sabitlenir (gerçek PSP aktivasyonu founder'ın PayTR hesabına bloklu — registry.DefaultProvider
/// production'da PayTR olabilir, o yüzden burada registry'den değil doğrudan Mock istenir).
///
/// CheckoutRequest (RFC-006'nın booking-payment sözleşmesi) alanları burada bilinçli yeniden
/// kullanılıyor: BookingId slotu CommissionStatement.Id'yi taşıyor, CommissionMinor=0 (platform
/// içi bölüşüm yok). Bu RFC'nin kendi ifadesiyle şema kesin DEĞİL — gerçek PSP entegrasyonunda
/// (Seçenek A/B implementasyon kararı) IPaymentProvider sözleşmesi muhtemelen ayrıştırılacak;
/// bu iskelet yalnız Mock-ile-test edilebilirliği kanıtlıyor, kalıcı bir kontrat değil.
/// </summary>
public sealed class InitiateCommissionPaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IRequestHandler<InitiateCommissionPaymentCommand, Result<InitiateCommissionPaymentResponse>>
{
    public async Task<Result<InitiateCommissionPaymentResponse>> Handle(InitiateCommissionPaymentCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<InitiateCommissionPaymentResponse>(Error.NotFound("BarberShop"));

        var statement = await ctx.Set<CommissionStatement>()
            .FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure<InitiateCommissionPaymentResponse>(Error.NotFound("CommissionStatement"));
        if (statement.BarberShopId != shop.Id)
            return Result.Failure<InitiateCommissionPaymentResponse>(Error.Forbidden("Bu statement size ait değil."));
        if (statement.Status is not (CommissionStatementStatus.Notified or CommissionStatementStatus.Overdue or CommissionStatementStatus.SuspensionApplied))
            return Result.Failure<InitiateCommissionPaymentResponse>(Error.Conflict("Bu statement online ödemeye uygun durumda değil."));

        var outstanding = await ctx.Set<CommissionPayment>()
            .AnyAsync(p => p.CommissionStatementId == statement.Id && p.Status == CommissionPaymentStatus.Initiated, ct);
        if (outstanding)
            return Result.Failure<InitiateCommissionPaymentResponse>(Error.Conflict("Bu statement için zaten devam eden bir ödeme var."));

        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            var byKey = await ctx.Set<CommissionPayment>().AnyAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);
            if (byKey) return Result.Failure<InitiateCommissionPaymentResponse>(Error.Conflict("Bu işlem anahtarı daha önce kullanılmış."));
        }

        var owner = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.OwnerUserId, ct);
        if (owner is null) return Result.Failure<InitiateCommissionPaymentResponse>(Error.NotFound("Owner"));

        var paymentResult = CommissionPayment.Initiate(
            shop.Id, statement.Id, statement.AmountDueMinor, PaymentProviderName.Mock, request.IdempotencyKey);
        if (paymentResult.IsFailure) return Result.Failure<InitiateCommissionPaymentResponse>(paymentResult.Error);
        var commissionPayment = paymentResult.Value;

        var mockProvider = providerRegistry.GetProvider(PaymentProviderName.Mock);
        var checkoutSession = await mockProvider.InitiateAsync(new CheckoutRequest(
            PaymentId: commissionPayment.Id,
            BookingId: statement.Id, // bkz. sınıf doc-comment — CommissionStatement referansı taşıyor
            CustomerId: owner.Id,
            CustomerPhone: owner.PhoneE164 ?? string.Empty,
            CustomerEmail: owner.Email,
            GrossAmountMinor: statement.AmountDueMinor,
            CommissionMinor: 0,
            SubMerchantKey: shop.SubMerchantKey ?? "PLATFORM",
            Currency: "TRY",
            ReturnUrl: "kuaforum://billing/success",
            CancelUrl: "kuaforum://billing/cancel"), ct);

        ctx.Set<CommissionPayment>().Add(commissionPayment);

        await audit.RecordAsync(
            "owner.commission_payment.initiated",
            nameof(CommissionPayment),
            commissionPayment.Id.ToString(),
            actorUserId: request.OwnerUserId,
            payload: new { statementId = statement.Id, commissionPayment.AmountMinor },
            ct: ct);

        await db.SaveChangesAsync(ct);

        return Result.Success(new InitiateCommissionPaymentResponse(
            commissionPayment.Id, checkoutSession.CheckoutUrl, checkoutSession.ProviderToken,
            checkoutSession.ExpiresAt, commissionPayment.AmountMinor));
    }
}

/// <summary>Mock provider için manuel tamamlama — CompletePaymentCommand (RFC-006) ile aynı desen.</summary>
public sealed record CompleteCommissionPaymentCommand(string ProviderToken, PaymentProviderName Provider) : ICommand<Guid>;

public sealed class CompleteCommissionPaymentCommandValidator : AbstractValidator<CompleteCommissionPaymentCommand>
{
    public CompleteCommissionPaymentCommandValidator()
    {
        RuleFor(x => x.ProviderToken).NotEmpty();
    }
}

public sealed class CompleteCommissionPaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IRequestHandler<CompleteCommissionPaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CompleteCommissionPaymentCommand request, CancellationToken ct)
    {
        var provider = providerRegistry.GetProvider(request.Provider);
        var verification = await provider.VerifyAsync(request.ProviderToken, ct);

        var ctx = (DbContext)db;
        var paymentIdGuess = ParseMockToken(request.ProviderToken);
        var commissionPayment = paymentIdGuess.HasValue
            ? await ctx.Set<CommissionPayment>().FirstOrDefaultAsync(p => p.Id == paymentIdGuess.Value, ct)
            : null;
        if (commissionPayment is null) return Result.Failure<Guid>(Error.NotFound("CommissionPayment"));

        if (commissionPayment.Provider != PaymentProviderName.Mock || request.Provider != PaymentProviderName.Mock)
            return Result.Failure<Guid>(Error.Validation("Bu ödeme mock sağlayıcıya ait değil."));

        if (!verification.Succeeded)
        {
            commissionPayment.Fail(verification.FailureReason ?? "Sağlayıcı doğrulaması başarısız oldu.");
            await audit.RecordAsync("owner.commission_payment.failed", nameof(CommissionPayment), commissionPayment.Id.ToString(),
                payload: new { reason = verification.FailureReason }, ct: ct);
            await db.SaveChangesAsync(ct);
            return Result.Failure<Guid>(Error.Validation(verification.FailureReason ?? "Ödeme başarısız oldu."));
        }

        var captureResult = commissionPayment.Capture(verification.ProviderTransactionId);
        if (captureResult.IsFailure) return Result.Failure<Guid>(captureResult.Error);

        var statement = await ctx.Set<CommissionStatement>().FirstOrDefaultAsync(s => s.Id == commissionPayment.CommissionStatementId, ct);
        if (statement is null) return Result.Failure<Guid>(Error.NotFound("CommissionStatement"));

        // RFC-012 madde 4: online tahsilat PSP tarafından doğrulandığı için statement Paid'e
        // geçer — ama BarberShop.Reactivate() burada da ÇAĞRILMAZ, admin ayrı aksiyon alır
        // (mark-paid ile aynı invariant, kanal fark etmez).
        var markPaidResult = statement.MarkPaid();
        if (markPaidResult.IsFailure) return Result.Failure<Guid>(markPaidResult.Error);

        await audit.RecordAsync(
            "owner.commission_payment.captured",
            nameof(CommissionPayment),
            commissionPayment.Id.ToString(),
            actorUserId: null,
            payload: new { statementId = statement.Id, commissionPayment.AmountMinor, providerTransactionId = verification.ProviderTransactionId },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success(commissionPayment.Id);
    }

    private static Guid? ParseMockToken(string token)
    {
        if (!token.StartsWith("mock-")) return null;
        var hex = token[5..];
        return Guid.TryParseExact(hex, "N", out var g) ? g : null;
    }
}
