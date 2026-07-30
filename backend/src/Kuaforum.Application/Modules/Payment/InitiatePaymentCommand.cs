using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kuaforum.Application.Modules.Payment;

public sealed record InitiatePaymentCommand(
    Guid CustomerId,
    Guid BookingId,
    PaymentKind Kind,
    string? IdempotencyKey) : ICommand<InitiatePaymentResponse>;

/// <summary>
/// RFC-006: AmountMinor = online tahsil edilen tutar (FullOnline'da booking toplamı,
/// Deposit'te ceil(toplam × DepositRate) kapora); RemainingAtShopMinor = salonda ödenecek kalan.
/// </summary>
public sealed record InitiatePaymentResponse(
    Guid PaymentId,
    string CheckoutUrl,
    string? ProviderToken,
    DateTimeOffset ExpiresAt,
    long AmountMinor,
    long RemainingAtShopMinor,
    string Kind);

public sealed class InitiatePaymentCommandValidator : AbstractValidator<InitiatePaymentCommand>
{
    public InitiatePaymentCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
    }
}

public sealed class InitiatePaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit,
    IOptions<PaymentOptions> paymentOptions) : IRequestHandler<InitiatePaymentCommand, Result<InitiatePaymentResponse>>
{
    public async Task<Result<InitiatePaymentResponse>> Handle(InitiatePaymentCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;

        // Idempotency check
        if (!string.IsNullOrEmpty(request.IdempotencyKey))
        {
            var existingByKey = await ctx.Set<PaymentAggregate>()
                .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existingByKey is not null)
                return Result.Failure<InitiatePaymentResponse>(Error.Conflict("Bu işlem anahtarı daha önce kullanılmış."));
        }

        var booking = await ctx.Set<BookingAggregate>()
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, cancellationToken);
        if (booking is null) return Result.Failure<InitiatePaymentResponse>(Error.NotFound("Booking"));
        if (booking.CustomerId != request.CustomerId)
            return Result.Failure<InitiatePaymentResponse>(Error.Forbidden("Bu randevu size ait değil."));

        // Already paid?
        var existingPayment = await ctx.Set<PaymentAggregate>()
            .FirstOrDefaultAsync(p => p.BookingId == booking.Id, cancellationToken);
        if (existingPayment is not null)
        {
            if (existingPayment.Status is PaymentStatus.Captured or PaymentStatus.Settled)
                return Result.Failure<InitiatePaymentResponse>(Error.Conflict("Bu randevunun ödemesi zaten yapılmış."));
        }

        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == booking.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure<InitiatePaymentResponse>(Error.NotFound("BarberShop"));

        var customer = await ctx.Set<Domain.Modules.Identity.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.CustomerId, cancellationToken);
        if (customer is null) return Result.Failure<InitiatePaymentResponse>(Error.NotFound("Customer"));

        var providerName = providerRegistry.DefaultProvider;
        var provider = providerRegistry.GetProvider(providerName);

        // RFC-006: Deposit'te online tahsilat kapora — ceil garantisi + DepositRate >= CommissionRate
        // invariantı (startup validation) sayesinde kapora platform komisyonunu her zaman karşılar.
        var opts = paymentOptions.Value;
        var onlineAmountMinor = request.Kind == PaymentKind.Deposit
            ? PaymentAmounts.CalculateDepositMinor(booking.TotalAmountMinor, opts.DepositRate)
            : booking.TotalAmountMinor;

        var paymentResult = PaymentAggregate.Initiate(
            booking.Id,
            booking.CustomerId,
            booking.BarberShopId,
            onlineAmountMinor,
            booking.PlatformCommissionMinor,
            providerName,
            kind: request.Kind,
            idempotencyKey: request.IdempotencyKey);
        if (paymentResult.IsFailure) return Result.Failure<InitiatePaymentResponse>(paymentResult.Error);

        var payment = paymentResult.Value;
        ctx.Set<PaymentAggregate>().Add(payment);

        var checkoutSession = await provider.InitiateAsync(new CheckoutRequest(
            PaymentId: payment.Id,
            BookingId: booking.Id,
            CustomerId: customer.Id,
            CustomerPhone: customer.PhoneE164 ?? string.Empty,
            CustomerEmail: customer.Email,
            GrossAmountMinor: onlineAmountMinor,
            CommissionMinor: booking.PlatformCommissionMinor,
            SubMerchantKey: shop.SubMerchantKey ?? "MOCK-SUB",
            Currency: booking.Currency,
            ReturnUrl: "kuaforum://payment/success",
            CancelUrl: "kuaforum://payment/cancel"), cancellationToken);

        await audit.RecordAsync(
            "payment.initiated",
            nameof(PaymentAggregate),
            payment.Id.ToString(),
            actorUserId: request.CustomerId,
            payload: new { booking.Id, providerName = providerName.ToString(), payment.GrossAmountMinor, kind = request.Kind.ToString() },
            ct: cancellationToken);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var constraint))
        {
            // Concurrent initiate raced past idempotency/booking check; unique constraint caught it.
            return Result.Failure<InitiatePaymentResponse>(Error.Conflict(
                constraint == "ux_payments_idempotency"
                    ? "Bu işlem anahtarı daha önce kullanılmış."
                    : "Bu randevu için zaten bir ödeme işlemi devam ediyor."));
        }

        return Result.Success(new InitiatePaymentResponse(
            payment.Id, checkoutSession.CheckoutUrl, checkoutSession.ProviderToken, checkoutSession.ExpiresAt,
            onlineAmountMinor, booking.TotalAmountMinor - onlineAmountMinor, request.Kind.ToString()));
    }

    /// <summary>
    /// Detects PostgreSQL unique-violation (SQLSTATE 23505) without taking a hard dependency on Npgsql.
    /// Application layer stays provider-agnostic; Infrastructure references the driver.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex, out string? constraintName)
    {
        constraintName = null;
        var inner = ex.InnerException;
        if (inner is null) return false;
        var t = inner.GetType();
        if (t.Name != "PostgresException") return false;
        var sqlState = t.GetProperty("SqlState")?.GetValue(inner) as string;
        if (sqlState != "23505") return false;
        constraintName = t.GetProperty("ConstraintName")?.GetValue(inner) as string;
        return true;
    }
}
