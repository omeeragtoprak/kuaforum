using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
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

public sealed record GetPendingReviewsQuery(int Page, int PageSize) : IQuery<IReadOnlyList<ReviewDto>>;

public sealed class GetPendingReviewsQueryHandler(IApplicationDbContext db) : IRequestHandler<GetPendingReviewsQuery, Result<IReadOnlyList<ReviewDto>>>
{
    public async Task<Result<IReadOnlyList<ReviewDto>>> Handle(GetPendingReviewsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var items = await ctx.Set<ReviewAggregate>().AsNoTracking()
            .Where(r => r.ModerationStatus == ReviewModerationStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(r => new ReviewDto(
                r.Id, r.CustomerId, r.StylistId, r.Rating, r.Body, r.CreatedAt, null, null))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ReviewDto>>(items);
    }
}
