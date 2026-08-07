using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record InitiateSubscriptionInvoicePaymentResponse(
    Guid SubscriptionInvoicePaymentId,
    string CheckoutUrl,
    string? ProviderToken,
    DateTimeOffset ExpiresAt,
    long AmountMinor);

public sealed record InitiateSubscriptionInvoicePaymentCommand(Guid OwnerUserId, Guid InvoiceId, string? IdempotencyKey)
    : ICommand<InitiateSubscriptionInvoicePaymentResponse>;

public sealed class InitiateSubscriptionInvoicePaymentCommandValidator : AbstractValidator<InitiateSubscriptionInvoicePaymentCommand>
{
    public InitiateSubscriptionInvoicePaymentCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
    }
}

/// <summary>
/// RFC-012 madde 5b (RFC-014 ile abonelik faturasına taşındı) — PayTR online ödeme İSKELETİ.
/// Provider bilerek DAİMA Mock'a sabitlenir (gerçek PSP aktivasyonu founder'ın PayTR hesabına
/// bloklu — registry.DefaultProvider production'da PayTR olabilir, o yüzden burada registry'den
/// değil doğrudan Mock istenir).
///
/// CheckoutRequest (RFC-006'nın booking-payment sözleşmesi) alanları burada bilinçli yeniden
/// kullanılıyor: BookingId slotu SubscriptionInvoice.Id'yi taşıyor, CommissionMinor=0 (bkz.
/// IPaymentProvider.CheckoutRequest.CommissionMinor XML doc — platformun PSP split'inde payı
/// yok). Bu RFC'nin kendi ifadesiyle şema kesin DEĞİL — gerçek PSP entegrasyonunda
/// (Seçenek A/B implementasyon kararı) IPaymentProvider sözleşmesi muhtemelen ayrıştırılacak;
/// bu iskelet yalnız Mock-ile-test edilebilirliği kanıtlıyor, kalıcı bir kontrat değil.
/// </summary>
public sealed class InitiateSubscriptionInvoicePaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IRequestHandler<InitiateSubscriptionInvoicePaymentCommand, Result<InitiateSubscriptionInvoicePaymentResponse>>
{
    public async Task<Result<InitiateSubscriptionInvoicePaymentResponse>> Handle(InitiateSubscriptionInvoicePaymentCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.NotFound("BarberShop"));

        var invoice = await ctx.Set<SubscriptionInvoice>()
            .FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.NotFound("SubscriptionInvoice"));
        if (invoice.BarberShopId != shop.Id)
            return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.Forbidden("Bu fatura size ait değil."));
        if (invoice.Status is not (SubscriptionInvoiceStatus.Notified or SubscriptionInvoiceStatus.Overdue or SubscriptionInvoiceStatus.SuspensionApplied))
            return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.Conflict("Bu fatura online ödemeye uygun durumda değil."));

        var outstanding = await ctx.Set<SubscriptionInvoicePayment>()
            .AnyAsync(p => p.SubscriptionInvoiceId == invoice.Id && p.Status == SubscriptionInvoicePaymentStatus.Initiated, ct);
        if (outstanding)
            return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.Conflict("Bu fatura için zaten devam eden bir ödeme var."));

        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            var byKey = await ctx.Set<SubscriptionInvoicePayment>().AnyAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);
            if (byKey) return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.Conflict("Bu işlem anahtarı daha önce kullanılmış."));
        }

        var owner = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.OwnerUserId, ct);
        if (owner is null) return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(Error.NotFound("Owner"));

        var paymentResult = SubscriptionInvoicePayment.Initiate(
            shop.Id, invoice.Id, invoice.AmountMinor, PaymentProviderName.Mock, request.IdempotencyKey);
        if (paymentResult.IsFailure) return Result.Failure<InitiateSubscriptionInvoicePaymentResponse>(paymentResult.Error);
        var invoicePayment = paymentResult.Value;

        var mockProvider = providerRegistry.GetProvider(PaymentProviderName.Mock);
        var checkoutSession = await mockProvider.InitiateAsync(new CheckoutRequest(
            PaymentId: invoicePayment.Id,
            BookingId: invoice.Id, // bkz. sınıf doc-comment — SubscriptionInvoice referansı taşıyor
            CustomerId: owner.Id,
            CustomerPhone: owner.PhoneE164 ?? string.Empty,
            CustomerEmail: owner.Email,
            GrossAmountMinor: invoice.AmountMinor,
            CommissionMinor: 0, // platformun PSP split'inde payı yok — bkz. CheckoutRequest XML doc
            SubMerchantKey: shop.SubMerchantKey ?? "PLATFORM",
            Currency: "TRY",
            ReturnUrl: "kuaforum://billing/success",
            CancelUrl: "kuaforum://billing/cancel"), ct);

        ctx.Set<SubscriptionInvoicePayment>().Add(invoicePayment);

        await audit.RecordAsync(
            "owner.subscription_invoice_payment.initiated",
            nameof(SubscriptionInvoicePayment),
            invoicePayment.Id.ToString(),
            actorUserId: request.OwnerUserId,
            payload: new { invoiceId = invoice.Id, invoicePayment.AmountMinor },
            ct: ct);

        await db.SaveChangesAsync(ct);

        return Result.Success(new InitiateSubscriptionInvoicePaymentResponse(
            invoicePayment.Id, checkoutSession.CheckoutUrl, checkoutSession.ProviderToken,
            checkoutSession.ExpiresAt, invoicePayment.AmountMinor));
    }
}

/// <summary>Mock provider için manuel tamamlama — CompletePaymentCommand (RFC-006) ile aynı desen.</summary>
public sealed record CompleteSubscriptionInvoicePaymentCommand(string ProviderToken, PaymentProviderName Provider) : ICommand<Guid>;

public sealed class CompleteSubscriptionInvoicePaymentCommandValidator : AbstractValidator<CompleteSubscriptionInvoicePaymentCommand>
{
    public CompleteSubscriptionInvoicePaymentCommandValidator()
    {
        RuleFor(x => x.ProviderToken).NotEmpty();
    }
}

public sealed class CompleteSubscriptionInvoicePaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IRequestHandler<CompleteSubscriptionInvoicePaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CompleteSubscriptionInvoicePaymentCommand request, CancellationToken ct)
    {
        var provider = providerRegistry.GetProvider(request.Provider);
        var verification = await provider.VerifyAsync(request.ProviderToken, ct);

        var ctx = (DbContext)db;
        var paymentIdGuess = ParseMockToken(request.ProviderToken);
        var invoicePayment = paymentIdGuess.HasValue
            ? await ctx.Set<SubscriptionInvoicePayment>().FirstOrDefaultAsync(p => p.Id == paymentIdGuess.Value, ct)
            : null;
        if (invoicePayment is null) return Result.Failure<Guid>(Error.NotFound("SubscriptionInvoicePayment"));

        if (invoicePayment.Provider != PaymentProviderName.Mock || request.Provider != PaymentProviderName.Mock)
            return Result.Failure<Guid>(Error.Validation("Bu ödeme mock sağlayıcıya ait değil."));

        if (!verification.Succeeded)
        {
            invoicePayment.Fail(verification.FailureReason ?? "Sağlayıcı doğrulaması başarısız oldu.");
            await audit.RecordAsync("owner.subscription_invoice_payment.failed", nameof(SubscriptionInvoicePayment), invoicePayment.Id.ToString(),
                payload: new { reason = verification.FailureReason }, ct: ct);
            await db.SaveChangesAsync(ct);
            return Result.Failure<Guid>(Error.Validation(verification.FailureReason ?? "Ödeme başarısız oldu."));
        }

        var invoice = await ctx.Set<SubscriptionInvoice>().FirstOrDefaultAsync(s => s.Id == invoicePayment.SubscriptionInvoiceId, ct);
        if (invoice is null) return Result.Failure<Guid>(Error.NotFound("SubscriptionInvoice"));

        // Review fix (2026-08-07): stale-session tutar koruması — RFC-006'nın
        // ProcessPaytrWebhookCommand'ındaki "webhook tutarı payment.GrossAmountMinor'la
        // uyuşmuyorsa capture etme" desenin aynısı, burada invoice tarafı için. invoicePayment.
        // AmountMinor Initiate anında invoice.AmountMinor'dan SNAPSHOT'landı (bkz.
        // InitiateSubscriptionInvoicePaymentCommandHandler); checkout açıkken admin
        // AdjustSubscriptionInvoiceAmountCommand ile tutarı değiştirmiş olabilir. PSP zaten o
        // ESKİ snapshot tutarını doğruladı (verification.Succeeded) — ama invoice bu arada
        // değiştiyse capture'ı invoice'a Paid olarak yazmak, tahsil edilen tutarın invoice'un
        // GÜNCEL beklentisiyle eşleştiği yanılsamasını yaratır. Fail-closed: tamamlama reddedilir.
        if (invoicePayment.AmountMinor != invoice.AmountMinor)
        {
            invoicePayment.Fail("Fatura tutarı ödeme başlatıldıktan sonra değişti — stale-session koruması.");
            await audit.RecordAsync("owner.subscription_invoice_payment.stale_amount_rejected", nameof(SubscriptionInvoicePayment), invoicePayment.Id.ToString(),
                payload: new { invoiceId = invoice.Id, paymentAmountMinor = invoicePayment.AmountMinor, currentInvoiceAmountMinor = invoice.AmountMinor },
                ct: ct);
            await db.SaveChangesAsync(ct);
            return Result.Failure<Guid>(Error.Conflict("Fatura tutarı ödeme sırasında değişti, bu ödeme tamamlanamadı. Lütfen yeniden başlatın."));
        }

        var captureResult = invoicePayment.Capture(verification.ProviderTransactionId);
        if (captureResult.IsFailure) return Result.Failure<Guid>(captureResult.Error);

        // RFC-012 madde 4: online tahsilat PSP tarafından doğrulandığı için fatura Paid'e
        // geçer — ama BarberShop.Reactivate() burada da ÇAĞRILMAZ, admin ayrı aksiyon alır
        // (mark-paid ile aynı invariant, kanal fark etmez).
        var markPaidResult = invoice.MarkPaid();
        if (markPaidResult.IsFailure) return Result.Failure<Guid>(markPaidResult.Error);

        await audit.RecordAsync(
            "owner.subscription_invoice_payment.captured",
            nameof(SubscriptionInvoicePayment),
            invoicePayment.Id.ToString(),
            actorUserId: null,
            payload: new { invoiceId = invoice.Id, invoicePayment.AmountMinor, providerTransactionId = verification.ProviderTransactionId },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success(invoicePayment.Id);
    }

    private static Guid? ParseMockToken(string token)
    {
        if (!token.StartsWith("mock-")) return null;
        var hex = token[5..];
        return Guid.TryParseExact(hex, "N", out var g) ? g : null;
    }
}
