using System.Net.Http.Json;
using System.Text.Json;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Notifications;

/// <summary>
/// Resend email provider — free tier 3000 emails/month (100/day).
/// Replaces MockEmailChannel in production. Set Resend:ApiKey + Resend:From in config.
/// Falls back to logging if API key is missing (dev-friendly).
/// </summary>
public sealed class ResendEmailChannel(
    IHttpClientFactory httpClientFactory,
    IOptions<ResendOptions> options,
    ILogger<ResendEmailChannel> logger) : INotificationChannelHandler
{
    public NotificationChannel Channel => NotificationChannel.Email;

    private readonly ResendOptions _options = options.Value;

    public async Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
        {
            logger.LogWarning("Resend not configured — falling back to log-only. Set Resend:ApiKey to enable.");
            logger.LogInformation("[FALLBACK EMAIL] to={To} subject='{Subject}' body='{Body}'",
                request.Recipient, request.Subject, request.Body);
            return new NotificationDeliveryResult(true, $"fallback-{Guid.NewGuid():N}", null);
        }

        try
        {
            var http = httpClientFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new("Bearer", _options.ApiKey);

            var payload = new
            {
                from = _options.From,
                to = new[] { request.Recipient },
                subject = request.Subject,
                text = request.Body,
            };

            var res = await http.PostAsJsonAsync("https://api.resend.com/emails", payload, ct);
            if (!res.IsSuccessStatusCode)
            {
                var errBody = await res.Content.ReadAsStringAsync(ct);
                return new NotificationDeliveryResult(false, null,
                    $"Resend HTTP {(int)res.StatusCode}: {errBody}");
            }

            var body = await res.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var id = body.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            return new NotificationDeliveryResult(true, id, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Resend email dispatch failed.");
            return new NotificationDeliveryResult(false, null, ex.Message);
        }
    }
}

public sealed class ResendOptions
{
    public const string SectionName = "Resend";
    public string ApiKey { get; set; } = string.Empty;
    public string From { get; set; } = "Kuaforum <noreply@kuaforum.com>";
}
