using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// Yalnızca Admin rolü çağırabilir (bkz. PaymentEndpoints.MapPaymentEndpoints — "Admin" policy).
/// İade tutarı client'tan alınmaz: bağlı booking'in Cancel/MarkNoShow sırasında domain'in
/// hesapladığı RefundPercentage'dan türetilir. Bu, hem IDOR'u (kim çağırabilir) hem de tutar
/// manipülasyonunu (ne kadar iade edilir) tek noktada kapatır.
/// </summary>
public sealed record RefundPaymentCommand(
    Guid ActorUserId,
    Guid PaymentId) : ICommand<Guid>;

public sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}

public sealed class RefundPaymentCommandHandler(
    IApplicationDbContext db,
    IBookingRefundService refundService) : IRequestHandler<RefundPaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var payment = await ctx.Set<PaymentAggregate>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);
        if (payment is null) return Result.Failure<Guid>(Error.NotFound("Payment"));

        // Shared with the automatic post-cancellation flow (BookingCancelledRefundHandler →
        // ProcessBookingRefundJob) — see BookingRefundService for the amount derivation +
        // idempotency logic. This admin path just translates a non-Refunded outcome into a
        // concrete validation error, since here (unlike the background job) "nothing to do"
        // is unexpected — the admin explicitly asked for a refund.
        var result = await refundService.RefundIfEligibleAsync(payment.BookingId, request.ActorUserId, cancellationToken);
        if (result.IsFailure) return Result.Failure<Guid>(result.Error);

        var outcome = result.Value;
        if (outcome.Kind != BookingRefundOutcomeKind.Refunded || outcome.Refund is null)
            return Result.Failure<Guid>(MapOutcomeToError(outcome.Kind));

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(outcome.Refund.Id);
    }

    private static Error MapOutcomeToError(BookingRefundOutcomeKind kind) => kind switch
    {
        BookingRefundOutcomeKind.BookingNotCancelled => Error.Validation(
            "İade yalnızca iptal edilmiş veya gelmedi işaretlenmiş randevular için yapılabilir."),
        BookingRefundOutcomeKind.NoRefundOwed => Error.Validation("Randevu için iade oranı hesaplanmamış."),
        BookingRefundOutcomeKind.NoPayment => Error.NotFound("Payment"),
        BookingRefundOutcomeKind.PaymentNotRefundable => Error.Validation("Bu ödeme durumunda iade yapılamaz."),
        BookingRefundOutcomeKind.AlreadyFullyRefunded => Error.Validation("İade edilecek tutar kalmadı."),
        _ => Error.Validation("İade yapılamadı."),
    };
}
