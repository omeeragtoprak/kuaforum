using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;

namespace Kuaforum.Application.Modules.Identity.Users;

/// <summary>
/// KVKK m.11.1.b: ilgili kişi, kişisel verilerinin işlenip işlenmediğini
/// öğrenme ve işlenen veriler hakkında bilgi talep etme hakkına sahiptir.
/// Bu query, kullanıcının kendi tüm verilerini JSON paketine paketler.
/// </summary>
public sealed record ExportMyDataQuery(Guid UserId) : IQuery<UserDataExportDto>;

public sealed record UserDataExportDto(
    DateTimeOffset ExportedAt,
    UserProfileExportDto Profile,
    IReadOnlyList<BookingExportDto> Bookings,
    IReadOnlyList<ReviewExportDto> Reviews,
    IReadOnlyList<ConsentExportDto> Consents,
    IReadOnlyList<SessionExportDto> Sessions);

public sealed record UserProfileExportDto(
    Guid Id,
    string? DisplayName,
    string? Email,
    string? PhoneE164,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record BookingExportDto(
    Guid Id,
    DateTimeOffset ScheduledAt,
    string Status,
    long TotalAmountMinor,
    string Currency,
    Guid BarberShopId,
    Guid StylistId,
    DateTimeOffset CreatedAt);

public sealed record ReviewExportDto(
    Guid Id,
    int Rating,
    string? Body,
    string ModerationStatus,
    DateTimeOffset CreatedAt);

public sealed record ConsentExportDto(
    string Purpose,
    bool Granted,
    string Version,
    DateTimeOffset GrantedAt,
    DateTimeOffset? RevokedAt);

public sealed record SessionExportDto(
    Guid Id,
    string? DeviceName,
    string DeviceFingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed class ExportMyDataQueryHandler(IApplicationDbContext db)
    : IRequestHandler<ExportMyDataQuery, Result<UserDataExportDto>>
{
    public async Task<Result<UserDataExportDto>> Handle(ExportMyDataQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var user = await ctx.Set<User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null) return Result.Failure<UserDataExportDto>(Error.NotFound("User"));

        var bookings = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.CustomerId == request.UserId)
            .OrderByDescending(b => b.ScheduledAt)
            .Select(b => new BookingExportDto(
                b.Id, b.ScheduledAt, b.Status.ToString(),
                b.TotalAmountMinor, b.Currency,
                b.BarberShopId, b.StylistId, b.CreatedAt))
            .ToListAsync(ct);

        var reviews = await ctx.Set<ReviewAggregate>().AsNoTracking()
            .Where(r => r.CustomerId == request.UserId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReviewExportDto(
                r.Id, r.Rating, r.Body, r.ModerationStatus.ToString(), r.CreatedAt))
            .ToListAsync(ct);

        var consents = await ctx.Set<Consent>().AsNoTracking()
            .Where(c => c.UserId == request.UserId)
            .OrderByDescending(c => c.GrantedAt)
            .Select(c => new ConsentExportDto(
                c.Purpose.ToString(), c.Granted, c.Version, c.GrantedAt, c.RevokedAt))
            .ToListAsync(ct);

        var sessions = await ctx.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.UserId == request.UserId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SessionExportDto(
                t.Id, t.DeviceName, t.DeviceFingerprint, t.CreatedAt, t.LastUsedAt))
            .ToListAsync(ct);

        var profile = new UserProfileExportDto(
            user.Id,
            user.DisplayName,
            user.Email,
            user.PhoneE164,
            user.Role.ToString(),
            user.Status.ToString(),
            user.CreatedAt,
            user.LastLoginAt);

        return Result.Success(new UserDataExportDto(
            DateTimeOffset.UtcNow, profile, bookings, reviews, consents, sessions));
    }
}
