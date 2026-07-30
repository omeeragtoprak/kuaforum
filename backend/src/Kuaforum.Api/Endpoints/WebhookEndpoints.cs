using System.Security.Cryptography;
using System.Text;
using Kuaforum.Application.Modules.Payment;
using Kuaforum.Infrastructure.Payments;
using MediatR;
using Microsoft.Extensions.Options;

namespace Kuaforum.Api.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        // PayTR sunucudan sunucuya çağırır — auth YOK, güvenlik HMAC imzasıyla (RFC-006 T4).
        app.MapPost("/v1/webhooks/paytr", HandlePaytrAsync)
            .WithTags("Webhooks").WithName("PaytrWebhook")
            .AllowAnonymous().DisableAntiforgery();
        return app;
    }

    /// <summary>
    /// PayTR bildirim şeması (https://dev.paytr.com/en/iframe-api/iframe-api-2-adim ile
    /// doğrulandı, 2026-07-30): form-urlencoded merchant_oid + status ("success"|"failed")
    /// + total_amount + hash; yanıt gövdesi literal "OK" olmalı, aksi halde PayTR bildirimi
    /// tekrar eder.
    /// </summary>
    private static async Task<IResult> HandlePaytrAsync(
        HttpRequest req, IMediator m, IOptions<PayTrOptions> payTrOptions, ILoggerFactory lf, CancellationToken ct)
    {
        var log = lf.CreateLogger("PaytrWebhook");

        // Fail-closed: key/salt yapılandırılmamışsa imza doğrulamasına HİÇ girme. Boş key ile
        // HMAC deterministik hesaplanabilir olur — saldırgan kendi paymentId'siyle ödemesiz
        // booking confirm forge edebilirdi. 503: konfigürasyon hatası, istek hatası değil.
        var opts = payTrOptions.Value;
        if (string.IsNullOrWhiteSpace(opts.MerchantKey) || string.IsNullOrWhiteSpace(opts.MerchantSalt))
        {
            log.LogError("PayTR webhook reddedildi: MerchantKey/MerchantSalt yapılandırılmamış.");
            return Results.Problem("Webhook yapılandırılmamış.", statusCode: 503);
        }

        if (!req.HasFormContentType)
            return Results.Problem("Form verisi bekleniyor.", statusCode: 400);

        var form = await req.ReadFormAsync(ct);
        string oid = form["merchant_oid"].ToString(), status = form["status"].ToString(),
               amount = form["total_amount"].ToString(), hash = form["hash"].ToString();

        var expected = PaytrWebhookSignature.Compute(oid, opts.MerchantSalt, status, amount, opts.MerchantKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(hash)))
        {
            log.LogWarning("PayTR webhook geçersiz imza (oid {Oid})", oid);
            return Results.Problem("Geçersiz imza.", statusCode: 400);
        }

        // İmza total_amount'u kapsıyor — parse hatası imzalı ama bozuk veri demektir; davranış
        // değişmez (0 ile devam → handler'da tutar uyuşmazlığı yolu), ama gözlemlenebilir olsun.
        if (!long.TryParse(amount, out var amountMinor))
            log.LogWarning("PayTR webhook total_amount parse edilemedi (oid {Oid}, raw: {Amount}) — 0 ile devam ediliyor", oid, amount);

        var r = await m.Send(new ProcessPaytrWebhookCommand(oid, status == "success", amountMinor), ct);
        if (r.IsFailure)
            log.LogWarning("PayTR webhook işlenemedi (oid {Oid}): {Error}", oid, r.Error.Message);
        return Results.Text("OK"); // PayTR "OK" görmezse bildirimi tekrar eder — bilinmeyen oid dahil OK dönülür.
    }
}
