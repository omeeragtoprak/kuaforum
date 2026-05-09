using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using ReviewAggregate = Kuaforum.Domain.Modules.Review.Review;
using Kuaforum.Domain.Modules.Review;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Review;

public sealed record ReviewDto(
    Guid Id,
    Guid CustomerId,
    Guid StylistId,
    int Rating,
    string? Body,
    DateTimeOffset CreatedAt,
    string? ReplyBody,
    DateTimeOffset? ReplyAt);

/// Admin moderation queue için zenginleştirilmiş — anonim ID yerine şehir/salon/müşteri adı.
public sealed record PendingReviewDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerName,
    Guid BarberShopId,
    string? BarberShopName,
    Guid StylistId,
    string? StylistName,
    int Rating,
    string? Body,
    DateTimeOffset CreatedAt);

public sealed record ShopRatingSummary(double Average, int Count);

public sealed record GetReviewsByShopQuery(Guid BarberShopId, int Page, int PageSize) : IQuery<GetReviewsByShopResponse>;

public sealed record GetReviewsByShopResponse(IReadOnlyList<ReviewDto> Items, ShopRatingSummary Summary, int TotalCount);

public sealed class GetReviewsByShopQueryHandler(IApplicationDbContext db) : IRequestHandler<GetReviewsByShopQuery, Result<GetReviewsByShopResponse>>
{
    public async Task<Result<GetReviewsByShopResponse>> Handle(GetReviewsByShopQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var baseQuery = ctx.Set<ReviewAggregate>().AsNoTracking()
            .Where(r => r.BarberShopId == request.BarberShopId
                        && r.ModerationStatus == ReviewModerationStatus.Approved);

        var total = await baseQuery.CountAsync(ct);
        var avg = total == 0 ? 0 : await baseQuery.AverageAsync(r => (double)r.Rating, ct);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var items = await baseQuery
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(r => new ReviewDto(
                r.Id, r.CustomerId, r.StylistId, r.Rating, r.Body, r.CreatedAt,
                r.Reply != null ? r.Reply.Body : null,
                r.Reply != null ? (DateTimeOffset?)r.Reply.CreatedAt : null))
            .ToListAsync(ct);

        return Result.Success(new GetReviewsByShopResponse(items, new ShopRatingSummary(avg, total), total));
    }
}

public sealed record GetPendingReviewsQuery(int Page, int PageSize) : IQuery<IReadOnlyList<PendingReviewDto>>;

public sealed class GetPendingReviewsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetPendingReviewsQuery, Result<IReadOnlyList<PendingReviewDto>>>
{
    public async Task<Result<IReadOnlyList<PendingReviewDto>>> Handle(GetPendingReviewsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var rows = await ctx.Set<ReviewAggregate>().AsNoTracking()
            .Where(r => r.ModerationStatus == ReviewModerationStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var customerIds = rows.Select(r => r.CustomerId).Distinct().ToList();
        var customers = await ctx.Set<User>().AsNoTracking()
            .Where(u => customerIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, ct);

        var shopIds = rows.Select(r => r.BarberShopId).Distinct().ToList();
        var shops = await ctx.Set<BarberShop>().AsNoTracking()
            .Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, ct);

        var stylistIds = rows.Select(r => r.StylistId).Distinct().ToList();
        var stylists = await ctx.Set<Stylist>().AsNoTracking()
            .Where(s => stylistIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName })
            .ToDictionaryAsync(s => s.Id, ct);

        var dtos = rows.Select(r =>
        {
            customers.TryGetValue(r.CustomerId, out var c);
            shops.TryGetValue(r.BarberShopId, out var sh);
            stylists.TryGetValue(r.StylistId, out var st);
            return new PendingReviewDto(
                r.Id, r.CustomerId, c?.DisplayName,
                r.BarberShopId, sh?.Name,
                r.StylistId, st?.DisplayName,
                r.Rating, r.Body, r.CreatedAt);
        }).ToList();

        return Result.Success<IReadOnlyList<PendingReviewDto>>(dtos);
    }
}
