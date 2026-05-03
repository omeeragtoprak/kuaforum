using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

public sealed record RefundPaymentCommand(
    Guid ActorUserId,
    Guid PaymentId,
    long AmountMinor,
    RefundReason Reason) : ICommand<Guid>;

public sealed class RefundPaymentCommandValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentCommandValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.AmountMinor).GreaterThan(0);
    }
}

public sealed class RefundPaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit) : IRequestHandler<RefundPaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(RefundPaymentCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var payment = await ctx.Set<PaymentAggregate>()
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);
        if (payment is null) return Result.Failure<Guid>(Error.NotFound("Payment"));

        var provider = providerRegistry.GetProvider(payment.Provider);
        var pspResult = await provider.RefundAsync(new RefundRequest(
            ProviderTransactionId: payment.ProviderTransactionId ?? string.Empty,
            AmountMinor: request.AmountMinor,
            Reason: request.Reason), cancellationToken);

        if (!pspResult.Succeeded)
            return Result.Failure<Guid>(Error.Validation(pspResult.FailureReason ?? "Refund failed at PSP."));

        var refundResult = payment.AddRefund(request.AmountMinor, request.Reason, pspResult.ProviderRefundId);
        if (refundResult.IsFailure) return Result.Failure<Guid>(refundResult.Error);

        await audit.RecordAsync("payment.refunded", nameof(PaymentAggregate), payment.Id.ToString(),
            actorUserId: request.ActorUserId,
            payload: new { request.AmountMinor, reason = request.Reason.ToString(), pspResult.ProviderRefundId },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(refundResult.Value.Id);
    }
}
