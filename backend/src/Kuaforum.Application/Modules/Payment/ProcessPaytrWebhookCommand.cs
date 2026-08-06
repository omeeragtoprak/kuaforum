using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using PaymentAggregate = Kuaforum.Domain.Modules.Payment.Payment;
using Kuaforum.Domain.Modules.Payment;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Payment;

/// <summary>
/// PayTR bildirim (callback) işleme — RFC-006 T4. HMAC doğrulaması API katmanında
/// (WebhookEndpoints); burada saf iş mantığı: idempotent capture / fail + ReinitiateCheckout
/// stale-session tutar koruması. merchant_oid = paymentId "N" formatı.
/// Endpoint NotFound sonuçlarını da 200 "OK"a çevirir — PayTR retry fırtınası önlenir.
/// </summary>
public sealed record ProcessPaytrWebhookCommand(
    string MerchantOid,
    bool Succeeded,
    long TotalAmountMinor) : ICommand<Guid>;

public sealed class ProcessPaytrWebhookCommandValidator : AbstractValidator<ProcessPaytrWebhookCommand>
{
    public ProcessPaytrWebhookCommandValidator()
    {
        RuleFor(x => x.MerchantOid).NotEmpty();
    }
}

public sealed class ProcessPaytrWebhookCommandHandler(
    IApplicationDbContext db,
    PaymentCompletionService completionService,
    IAuditLogger audit) : IRequestHandler<ProcessPaytrWebhookCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(ProcessPaytrWebhookCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(request.MerchantOid, "N", out var paymentId))
            return Result.Failure<Guid>(Error.NotFound("Payment"));

        var ctx = (DbContext)db;
        var payment = await ctx.Set<PaymentAggregate>()
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
            return Result.Failure<Guid>(Error.NotFound("Payment"));

        // İdempotent no-op: para hareketi zaten kayıtlı — PayTR retry'ı hiçbir şey yazmadan OK'lenir.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return Result.Success(payment.Id);
        }

        if (request.Succeeded && request.TotalAmountMinor != payment.GrossAmountMinor)
        {
            // ReinitiateCheckout stale-session koruması: müşteri ödeme türünü değiştirdiyse
            // (Deposit ⇄ FullOnline) satırın Gross'u değişmiştir; ESKİ checkout session'ının
            // callback'i eski tutarla gelirse capture ETME — tutar uyuşmazlığı audit'e yazılır.
            // Not: gerçek PayTR'da total_amount taksit/vade farkıyla payment_amount'tan
            // BÜYÜK olabilir; canlı entegrasyonda bu kural payment_amount üzerinden gözden
            // geçirilmeli (RFC-006 canlıya alma checklist'i).
            await audit.RecordAsync("payment.webhook_amount_mismatch", nameof(PaymentAggregate),
                payment.Id.ToString(),
                payload: new
                {
                    merchantOid = request.MerchantOid,
                    expectedMinor = payment.GrossAmountMinor,
                    receivedMinor = request.TotalAmountMinor,
                },
                ct: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(payment.Id);
        }

        if (request.Succeeded)
        {
            // RFC-013 C1: booking bu arada Cancelled/NoShow olmuş olsa bile capture ARTIK sessizce
            // yutulmuyor — orphan capture olarak booking'e bağlanır + refund job enqueue edilir
            // (bkz. PaymentCompletionService XML doc, terminal durum matrisi).
            var completion = await completionService.CaptureAndConfirmAsync(
                payment, $"paytr-{request.MerchantOid}", pspMdrMinor: 0, ct: cancellationToken);
            if (completion.IsFailure) return Result.Failure<Guid>(completion.Error);
        }
        else if (payment.Status is PaymentStatus.Initiated or PaymentStatus.Authorized)
        {
            var fail = payment.Fail("PayTR bildirimi: ödeme başarısız");
            if (fail.IsFailure) return Result.Failure<Guid>(fail.Error);
        }
        // else: Failed/Cancelled durumundaki satıra gelen failed bildirimi — no-op (idempotent).

        await audit.RecordAsync("payment.webhook_processed", nameof(PaymentAggregate), payment.Id.ToString(),
            payload: new
            {
                merchantOid = request.MerchantOid,
                succeeded = request.Succeeded,
                amountMinor = request.TotalAmountMinor,
            },
            ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(payment.Id);
    }
}
