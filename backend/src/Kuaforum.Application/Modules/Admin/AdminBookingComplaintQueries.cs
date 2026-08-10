using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using ComplaintAggregate = Kuaforum.Domain.Modules.Booking.BookingComplaint;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

/// <summary>RFC-015 §2 D3 madde 3: admin kuyruğu — KYC kuyruğuyla (<see cref="GetKycQueueQuery"/>) AYNI
/// sayfalama kalıbı (status filtresi + Page/PageSize/TotalCount).</summary>
public sealed record AdminBookingComplaintDto(
    Guid ComplaintId,
    Guid BookingId,
    Guid BarberShopId,
    string BarberShopName,
    Guid CustomerId,
    string? CustomerName,
    string? Note,
    BookingComplaintStatus Status,
    DateTimeOffset ReportedAt,
    DateTimeOffset BookingScheduledAt);

public sealed record AdminBookingComplaintsPage(
    IReadOnlyList<AdminBookingComplaintDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminBookingComplaintsQuery(
    BookingComplaintStatus? StatusFilter,
    int Page,
    int PageSize) : IQuery<AdminBookingComplaintsPage>;

public sealed class GetAdminBookingComplaintsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminBookingComplaintsQuery, Result<AdminBookingComplaintsPage>>
{
    public async Task<Result<AdminBookingComplaintsPage>> Handle(GetAdminBookingComplaintsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<ComplaintAggregate>().AsNoTracking().AsQueryable();
        if (request.StatusFilter.HasValue)
            query = query.Where(c => c.Status == request.StatusFilter.Value);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.ReportedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new
            {
                Complaint = c,
                ShopName = ctx.Set<BarberShop>().Where(s => s.Id == c.BarberShopId).Select(s => s.Name).FirstOrDefault(),
                CustomerName = ctx.Set<User>().Where(u => u.Id == c.CustomerId).Select(u => u.DisplayName).FirstOrDefault(),
                BookingScheduledAt = ctx.Set<BookingAggregate>().Where(b => b.Id == c.BookingId).Select(b => b.ScheduledAt).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var dtos = items.Select(x => new AdminBookingComplaintDto(
            x.Complaint.Id, x.Complaint.BookingId, x.Complaint.BarberShopId, x.ShopName ?? "—",
            x.Complaint.CustomerId, x.CustomerName, x.Complaint.Note, x.Complaint.Status,
            x.Complaint.ReportedAt, x.BookingScheduledAt))
            .ToList();

        return Result.Success(new AdminBookingComplaintsPage(dtos, page, size, total));
    }
}
