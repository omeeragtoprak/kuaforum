using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Notifications;

public sealed class FcmOptions
{
    public const string SectionName = "Fcm";
    /// <summary>
    /// Path to Firebase service account JSON. If empty, FcmPushChannel is not registered (mock used instead).
    /// </summary>
    public string? ServiceAccountJsonPath { get; set; }
    /// <summary>Inline JSON content for the service account (alternative to file path; useful for env-driven deploys).</summary>
    public string? ServiceAccountJson { get; set; }
}

/// <summary>
/// Production push channel: looks up user's active DeviceTokens and sends a multicast via Firebase Cloud Messaging.
/// If a token is invalid (UNREGISTERED / SENDER_ID_MISMATCH), it is deactivated.
/// Falls back to a single warning + success-no-op if user has no active tokens (this is normal — they may be web-only).
/// </summary>
public sealed class FcmPushChannel : INotificationChannelHandler
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<FcmPushChannel> _logger;
    private readonly FirebaseMessaging _messaging;

    public NotificationChannel Channel => NotificationChannel.Push;

    public FcmPushChannel(IApplicationDbContext db, ILogger<FcmPushChannel> logger, FirebaseApp app)
    {
        _db = db;
        _logger = logger;
        _messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<NotificationDeliveryResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        if (request.UserId is null)
        {
            return new NotificationDeliveryResult(false, null, "Push requires UserId.");
        }

        var ctx = (DbContext)_db;
        var tokens = await ctx.Set<DeviceToken>().AsNoTracking()
            .Where(t => t.UserId == request.UserId && t.IsActive)
            .Select(t => new { t.Id, t.FcmToken })
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            _logger.LogDebug("FCM: user {UserId} has no active device tokens; skipping push.", request.UserId);
            return new NotificationDeliveryResult(true, null, null);
        }

        var message = new MulticastMessage
        {
            Tokens = tokens.Select(t => t.FcmToken).ToArray(),
            Notification = new FirebaseAdmin.Messaging.Notification
            {
                Title = request.Subject,
                Body = request.Body,
            },
            Data = BuildDataPayload(request),
            Android = new AndroidConfig { Priority = Priority.High },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string> { ["apns-priority"] = "10" },
                Aps = new Aps { Sound = "default" },
            },
        };

        try
        {
            var resp = await _messaging.SendEachForMulticastAsync(message, ct);
            await DeactivateInvalidTokensAsync(ctx, tokens.Select(t => (t.Id, t.FcmToken)).ToList(), resp, ct);
            return new NotificationDeliveryResult(
                resp.SuccessCount > 0,
                $"fcm-multicast-{resp.SuccessCount}/{resp.Responses.Count}",
                resp.FailureCount > 0 ? $"{resp.FailureCount} failed" : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM dispatch failed for user {UserId}", request.UserId);
            return new NotificationDeliveryResult(false, null, ex.Message);
        }
    }

    private static Dictionary<string, string> BuildDataPayload(NotificationRequest request)
    {
        var data = new Dictionary<string, string>
        {
            ["templateKey"] = request.TemplateKey,
        };
        if (request.RelatedBookingId is { } bookingId)
            data["bookingId"] = bookingId.ToString();
        return data;
    }

    private async Task DeactivateInvalidTokensAsync(
        DbContext ctx,
        List<(Guid Id, string FcmToken)> tokens,
        BatchResponse resp,
        CancellationToken ct)
    {
        if (resp.FailureCount == 0) return;

        var toDeactivate = new List<Guid>();
        for (int i = 0; i < resp.Responses.Count; i++)
        {
            var r = resp.Responses[i];
            if (r.IsSuccess) continue;
            var code = r.Exception?.MessagingErrorCode;
            if (code == MessagingErrorCode.Unregistered || code == MessagingErrorCode.SenderIdMismatch)
            {
                toDeactivate.Add(tokens[i].Id);
            }
        }

        if (toDeactivate.Count == 0) return;

        var entities = await ctx.Set<DeviceToken>()
            .Where(t => toDeactivate.Contains(t.Id))
            .ToListAsync(ct);
        foreach (var e in entities)
            e.Deactivate("fcm.unregistered");
        await ctx.SaveChangesAsync(ct);
        _logger.LogInformation("FCM: deactivated {Count} stale tokens", toDeactivate.Count);
    }
}

internal static class FirebaseAppFactory
{
    public static FirebaseApp? CreateOrNull(FcmOptions options, ILogger logger)
    {
        if (FirebaseApp.DefaultInstance is not null) return FirebaseApp.DefaultInstance;

        try
        {
            GoogleCredential cred;
            if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
            {
                cred = GoogleCredential.FromJson(options.ServiceAccountJson);
            }
            else if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath) && File.Exists(options.ServiceAccountJsonPath))
            {
                cred = GoogleCredential.FromFile(options.ServiceAccountJsonPath);
            }
            else
            {
                return null;
            }

            return FirebaseApp.Create(new AppOptions { Credential = cred });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize Firebase; falling back to mock push.");
            return null;
        }
    }
}
