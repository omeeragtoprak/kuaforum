using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;

namespace Kuaforum.Application.Modules.Admin;

public sealed record AdminBookingDto(
    Guid Id,
    Guid CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    Guid BarberShopId,
    string ShopName,
    Guid StylistId,
    string StylistName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ScheduledEndsAt,
    BookingStatus Status,
    long TotalAmountMinor,
    string Currency);

public sealed record AdminBookingsPage(
    IReadOnlyList<AdminBookingDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminBookingsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    BookingStatus? Status,
    Guid? BarberShopId,
    string? Search,
    int Page,
    int PageSize) : IQuery<AdminBookingsPage>;

public sealed class GetAdminBookingsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminBookingsQuery, Result<AdminBookingsPage>>
{
    public async Task<Result<AdminBookingsPage>> Handle(GetAdminBookingsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize is <= 0 or > 100 ? 25 : request.PageSize;

        var query = ctx.Set<BookingAggregate>().AsNoTracking().AsQueryable();

        if (request.From is not null) query = query.Where(b => b.ScheduledAt >= request.From.Value);
        if (request.To is not null) query = query.Where(b => b.ScheduledAt < request.To.Value);
        if (request.Status is not null) query = query.Where(b => b.Status == request.Status.Value);
        if (request.BarberShopId is not null) query = query.Where(b => b.BarberShopId == request.BarberShopId.Value);

        var total = await query.CountAsync(ct);

        var rows = await (
            from b in query
            join u in ctx.Set<Domain.Modules.Identity.User>().AsNoTracking() on b.CustomerId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            join s in ctx.Set<Domain.Modules.Barber.BarberShop>().AsNoTracking() on b.BarberShopId equals s.Id
            join st in ctx.Set<Domain.Modules.Barber.Stylist>().AsNoTracking() on b.StylistId equals st.Id
            orderby b.ScheduledAt descending
            select new AdminBookingDto(
                b.Id, b.CustomerId,
                u != null ? u.DisplayName : null,
                u != null ? u.PhoneE164 : null,
                b.BarberShopId, s.Name,
                b.StylistId, st.DisplayName,
                b.ScheduledAt, b.ScheduledEndsAt,
                b.Status, b.TotalAmountMinor, b.Currency))
            .Skip((page - 1) * size).Take(size)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLowerInvariant();
            rows = rows.Where(r =>
                (r.CustomerName?.ToLowerInvariant().Contains(s) ?? false)
                || (r.CustomerPhone?.Contains(s) ?? false)
                || r.ShopName.ToLowerInvariant().Contains(s)
                || r.StylistName.ToLowerInvariant().Contains(s)).ToList();
        }

        return Result.Success(new AdminBookingsPage(rows, page, size, total));
    }
}
