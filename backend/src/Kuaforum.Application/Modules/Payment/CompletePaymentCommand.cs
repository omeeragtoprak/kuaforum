using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// Mock provider için manual completion endpoint. Production'da gerçek PSP webhook'u kullanılır.
/// </summary>
public sealed record CompletePaymentCommand(
    string ProviderToken,
    PaymentProviderName Provider) : ICommand<Guid>;

public sealed class CompletePaymentCommandValidator : AbstractValidator<CompletePaymentCommand>
{
    public CompletePaymentCommandValidator()
    {
        RuleFor(x => x.ProviderToken).NotEmpty();
    }
}

public sealed class CompletePaymentCommandHandler(
    IApplicationDbContext db,
    IPaymentProviderRegistry providerRegistry,
    IAuditLogger audit,
    PaymentCompletionService completionService) : IRequestHandler<CompletePaymentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CompletePaymentCommand request, CancellationToken cancellationToken)
    {
        var provider = providerRegistry.GetProvider(request.Provider);
        var verification = await provider.VerifyAsync(request.ProviderToken, cancellationToken);

        var ctx = (DbContext)db;

        // Provider token convention: payments table providerPaymentToken column should match
        // For Mock: token format is "mock-{paymentId without dashes}"
        var paymentIdGuess = ParseMockToken(request.ProviderToken);
        var payment = paymentIdGuess.HasValue
            ? await ctx.Set<PaymentAggregate>().FirstOrDefaultAsync(p => p.Id == paymentIdGuess.Value, cancellationToken)
            : null;

        if (payment is null)
            return Result.Failure<Guid>(Error.NotFound("Payment"));

        // Defense-in-depth: mock-only completion path. Even though the token format is
        // Mock-specific (mock-{paymentId:N}), guard against a guessed/collided token being
        // replayed against a payment that was actually initiated with a real PSP.
        if (payment.Provider != PaymentProviderName.Mock || request.Provider != PaymentProviderName.Mock)
            return Result.Failure<Guid>(Error.Validation("Bu ödeme mock sağlayıcıya ait değil."));

        if (!verification.Succeeded)
        {
            payment.Fail(verification.FailureReason ?? "Sağlayıcı doğrulaması başarısız oldu.");
            await audit.RecordAsync("payment.failed", nameof(PaymentAggregate), payment.Id.ToString(),
                payload: new { reason = verification.FailureReason }, ct: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<Guid>(Error.Validation(verification.FailureReason ?? "Ödeme başarısız oldu."));
        }

        // Capture → booking Confirm → audit ortak bloğu PaymentCompletionService'te —
        // PayTR webhook handler'ı ile paylaşılır (RFC-006 T4, kopya bırakma). RFC-013 C1: booking
        // Pending değilse artık komut düşmez — orphan capture olarak bağlanır + refund enqueue
        // edilir (bkz. PaymentCompletionService XML doc, terminal durum matrisi).
        var completion = await completionService.CaptureAndConfirmAsync(
            payment, verification.ProviderTransactionId, verification.PspMdrMinor, ct: cancellationToken);
        if (completion.IsFailure) return Result.Failure<Guid>(completion.Error);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(payment.Id);
    }

    private static Guid? ParseMockToken(string token)
    {
        if (!token.StartsWith("mock-")) return null;
        var hex = token[5..];
        return Guid.TryParseExact(hex, "N", out var g) ? g : null;
    }
}
