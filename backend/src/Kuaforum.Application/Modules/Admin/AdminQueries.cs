using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using BookingStatus = Kuaforum.Domain.Modules.Booking.BookingStatus;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using Kuaforum.Domain.Modules.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record DashboardStatsDto(
    int TotalUsers,
    int CustomerCount,
    int BarberOwnerCount,
    int ActiveBarberShops,
    int PendingKycCount,
    int PendingReviewsCount,
    int TodaysBookings,
    long TodaysGmvMinor);

public sealed record GetDashboardStatsQuery() : IQuery<DashboardStatsDto>;

public sealed class GetDashboardStatsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetDashboardStatsQuery, Result<DashboardStatsDto>>
{
    public async Task<Result<DashboardStatsDto>> Handle(GetDashboardStatsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var today = DateTimeOffset.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var totalUsers = await ctx.Set<User>().CountAsync(u => u.DeletedAt == null, ct);
        var customers = await ctx.Set<User>().CountAsync(u => u.Role == UserRole.Customer && u.DeletedAt == null, ct);
        var barberOwners = await ctx.Set<User>().CountAsync(u => u.Role == UserRole.BarberOwner && u.DeletedAt == null, ct);
        var activeShops = await ctx.Set<BarberShop>().CountAsync(s => s.Status == BarberShopStatus.Active, ct);
        var pendingKyc = await ctx.Set<KycSubmission>()
            .CountAsync(k => k.Status == KycStatus.Pending || k.Status == KycStatus.UnderReview, ct);
        var pendingReviews = await ctx.Set<ReviewAggregate>()
            .CountAsync(r => r.ModerationStatus == ReviewModerationStatus.Pending, ct);

        var todayBookings = await ctx.Set<BookingAggregate>()
            .Where(b => b.ScheduledAt >= today && b.ScheduledAt < tomorrow)
            .ToListAsync(ct);
        var todayGmv = todayBookings
            .Where(b => b.Status is BookingStatus.Confirmed or BookingStatus.Started or BookingStatus.Completed)
            .Sum(b => b.TotalAmountMinor);

        return Result.Success(new DashboardStatsDto(
            TotalUsers: totalUsers,
            CustomerCount: customers,
            BarberOwnerCount: barberOwners,
            ActiveBarberShops: activeShops,
            PendingKycCount: pendingKyc,
            PendingReviewsCount: pendingReviews,
            TodaysBookings: todayBookings.Count,
            TodaysGmvMinor: todayGmv));
    }
}

public sealed record KycQueueItemDto(
    Guid SubmissionId,
    Guid BarberShopId,
    string BarberShopName,
    Guid SubmittedByUserId,
    string? SubmitterName,
    DateTimeOffset SubmittedAt,
    int DocumentCount,
    KycStatus Status);

public sealed record KycQueuePage(
    IReadOnlyList<KycQueueItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetKycQueueQuery(KycStatus? StatusFilter, int Page, int PageSize) : IQuery<KycQueuePage>;

public sealed class GetKycQueueQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetKycQueueQuery, Result<KycQueuePage>>
{
    public async Task<Result<KycQueuePage>> Handle(GetKycQueueQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<KycSubmission>().AsNoTracking().AsQueryable();
        if (request.StatusFilter.HasValue)
            query = query.Where(k => k.Status == request.StatusFilter.Value);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(k => k.SubmittedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(k => new
            {
                Submission = k,
                Shop = ctx.Set<BarberShop>().Where(s => s.Id == k.BarberShopId).Select(s => s.Name).FirstOrDefault(),
                Submitter = ctx.Set<User>().Where(u => u.Id == k.SubmittedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                DocCount = ctx.Set<KycDocument>().Count(d => d.SubmissionId == k.Id),
            })
            .ToListAsync(ct);

        var dtos = items.Select(x => new KycQueueItemDto(
            x.Submission.Id, x.Submission.BarberShopId, x.Shop ?? "—",
            x.Submission.SubmittedByUserId, x.Submitter,
            x.Submission.SubmittedAt, x.DocCount, x.Submission.Status))
            .ToList();
        return Result.Success(new KycQueuePage(dtos, page, size, total));
    }
}
