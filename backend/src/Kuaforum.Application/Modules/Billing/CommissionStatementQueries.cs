using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record CommissionStatementSummaryDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long BasisAmountMinor,
    decimal RateApplied,
    long AmountDueMinor,
    DateOnly DueDate,
    CommissionStatementStatus Status,
    bool IsDisputed,
    string? DisputeNote);

internal static class CommissionStatementMapping
{
    public static CommissionStatementSummaryDto ToSummaryDto(this CommissionStatement s) => new(
        s.Id, s.PeriodStart, s.PeriodEnd, s.BasisAmountMinor, s.RateApplied, s.AmountDueMinor,
        s.DueDate, s.Status, s.IsDisputed, s.DisputeNote);

    /// <summary>
    /// Dönem sınırlarını UTC gün sınırına çevirir — CloseMonthlyCommissionStatementsJob'un
    /// matrah sorgusuyla AYNI kural (Europe/Istanbul yerel gün DEĞİL, bkz. job doc-comment).
    /// </summary>
    public static (DateTimeOffset StartUtc, DateTimeOffset EndExclusiveUtc) ToUtcBounds(this CommissionStatement s) =>
        (new DateTimeOffset(s.PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
         new DateTimeOffset(s.PeriodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
}

public sealed record OwnerCommissionStatementsPage(
    IReadOnlyList<CommissionStatementSummaryDto> Items,
    string? CommissionPaymentIban,
    string? CommissionPaymentBankName,
    string? CommissionPaymentAccountHolderName);

public sealed record GetOwnerCommissionStatementsQuery(Guid OwnerUserId) : IQuery<OwnerCommissionStatementsPage>;

public sealed class GetOwnerCommissionStatementsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerCommissionStatementsQuery, Result<OwnerCommissionStatementsPage>>
{
    public async Task<Result<OwnerCommissionStatementsPage>> Handle(GetOwnerCommissionStatementsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<OwnerCommissionStatementsPage>(Error.NotFound("BarberShop"));

        var statements = await ctx.Set<CommissionStatement>().AsNoTracking()
            .Where(s => s.BarberShopId == shop.Id)
            .OrderByDescending(s => s.PeriodStart)
            .ToListAsync(ct);

        var settings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);

        return Result.Success(new OwnerCommissionStatementsPage(
            statements.Select(s => s.ToSummaryDto()).ToList(),
            settings?.CommissionPaymentIban,
            settings?.CommissionPaymentBankName,
            settings?.CommissionPaymentAccountHolderName));
    }
}

public sealed record CommissionStatementBookingLineDto(Guid BookingId, DateTimeOffset CompletedAt, long TotalAmountMinor);

public sealed record CommissionStatementDetailDto(
    CommissionStatementSummaryDto Statement,
    IReadOnlyList<CommissionStatementBookingLineDto> Bookings);

/// <summary>
/// RFC-012: ayrı bir line-item tablosu YOK (YAGNI) — booking dökümü Booking tablosundan
/// canlı türetilir (immutable, Completed sonrası bir booking hiçbir zaman değişmez).
/// </summary>
public sealed record GetOwnerCommissionStatementDetailQuery(Guid OwnerUserId, Guid StatementId) : IQuery<CommissionStatementDetailDto>;

public sealed class GetOwnerCommissionStatementDetailQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerCommissionStatementDetailQuery, Result<CommissionStatementDetailDto>>
{
    public async Task<Result<CommissionStatementDetailDto>> Handle(GetOwnerCommissionStatementDetailQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<CommissionStatementDetailDto>(Error.NotFound("BarberShop"));

        var statement = await ctx.Set<CommissionStatement>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure<CommissionStatementDetailDto>(Error.NotFound("CommissionStatement"));
        if (statement.BarberShopId != shop.Id) return Result.Failure<CommissionStatementDetailDto>(Error.Forbidden("Bu statement size ait değil."));

        var (startUtc, endExclusiveUtc) = statement.ToUtcBounds();
        var bookings = await ctx.Set<BookingAggregate>().AsNoTracking()
            .Where(b => b.BarberShopId == shop.Id
                        && b.Status == BookingStatus.Completed
                        && b.CompletedAt != null
                        && b.CompletedAt >= startUtc && b.CompletedAt < endExclusiveUtc)
            .OrderBy(b => b.CompletedAt)
            .Select(b => new CommissionStatementBookingLineDto(b.Id, b.CompletedAt!.Value, b.TotalAmountMinor))
            .ToListAsync(ct);

        return Result.Success(new CommissionStatementDetailDto(statement.ToSummaryDto(), bookings));
    }
}

public sealed record AdminCommissionStatementItemDto(
    Guid Id,
    Guid BarberShopId,
    string BarberShopName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    long AmountDueMinor,
    DateOnly DueDate,
    CommissionStatementStatus Status,
    bool IsDisputed);

public sealed record AdminCommissionStatementsPage(
    IReadOnlyList<AdminCommissionStatementItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminCommissionStatementsQueueQuery(
    CommissionStatementStatus? StatusFilter,
    bool? DisputedOnly,
    int Page,
    int PageSize) : IQuery<AdminCommissionStatementsPage>;

public sealed class GetAdminCommissionStatementsQueueQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminCommissionStatementsQueueQuery, Result<AdminCommissionStatementsPage>>
{
    public async Task<Result<AdminCommissionStatementsPage>> Handle(GetAdminCommissionStatementsQueueQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<CommissionStatement>().AsNoTracking().AsQueryable();
        if (request.StatusFilter.HasValue) query = query.Where(s => s.Status == request.StatusFilter.Value);
        if (request.DisputedOnly == true) query = query.Where(s => s.IsDisputed);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(s => s.DueDate)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(s => new
            {
                Statement = s,
                ShopName = ctx.Set<BarberShop>().Where(shop => shop.Id == s.BarberShopId).Select(shop => shop.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var dtos = items.Select(x => new AdminCommissionStatementItemDto(
            x.Statement.Id, x.Statement.BarberShopId, x.ShopName ?? "—",
            x.Statement.PeriodStart, x.Statement.PeriodEnd, x.Statement.AmountDueMinor,
            x.Statement.DueDate, x.Statement.Status, x.Statement.IsDisputed))
            .ToList();

        return Result.Success(new AdminCommissionStatementsPage(dtos, page, size, total));
    }
}
