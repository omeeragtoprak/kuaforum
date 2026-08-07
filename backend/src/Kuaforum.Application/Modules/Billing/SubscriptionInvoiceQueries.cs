using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record SubscriptionInvoiceSummaryDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PlanCode,
    long PlanPriceMinor,
    long AmountMinor,
    DateOnly DueDate,
    SubscriptionInvoiceStatus Status,
    bool IsDisputed,
    string? DisputeNote);

internal static class SubscriptionInvoiceMapping
{
    public static SubscriptionInvoiceSummaryDto ToSummaryDto(this SubscriptionInvoice s) => new(
        s.Id, s.PeriodStart, s.PeriodEnd, s.PlanCode, s.PlanPriceMinor, s.AmountMinor,
        s.DueDate, s.Status, s.IsDisputed, s.DisputeNote);
}

public sealed record OwnerSubscriptionInvoicesPage(
    IReadOnlyList<SubscriptionInvoiceSummaryDto> Items,
    string? CommissionPaymentIban,
    string? CommissionPaymentBankName,
    string? CommissionPaymentAccountHolderName);

public sealed record GetOwnerSubscriptionInvoicesQuery(Guid OwnerUserId) : IQuery<OwnerSubscriptionInvoicesPage>;

public sealed class GetOwnerSubscriptionInvoicesQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerSubscriptionInvoicesQuery, Result<OwnerSubscriptionInvoicesPage>>
{
    public async Task<Result<OwnerSubscriptionInvoicesPage>> Handle(GetOwnerSubscriptionInvoicesQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<OwnerSubscriptionInvoicesPage>(Error.NotFound("BarberShop"));

        var invoices = await ctx.Set<SubscriptionInvoice>().AsNoTracking()
            .Where(s => s.BarberShopId == shop.Id)
            .OrderByDescending(s => s.PeriodStart)
            .ToListAsync(ct);

        var settings = await ctx.Set<PlatformSettings>().AsNoTracking().FirstOrDefaultAsync(ct);

        return Result.Success(new OwnerSubscriptionInvoicesPage(
            invoices.Select(s => s.ToSummaryDto()).ToList(),
            settings?.CommissionPaymentIban,
            settings?.CommissionPaymentBankName,
            settings?.CommissionPaymentAccountHolderName));
    }
}

public sealed record GetOwnerSubscriptionInvoiceDetailQuery(Guid OwnerUserId, Guid InvoiceId) : IQuery<SubscriptionInvoiceSummaryDto>;

public sealed class GetOwnerSubscriptionInvoiceDetailQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetOwnerSubscriptionInvoiceDetailQuery, Result<SubscriptionInvoiceSummaryDto>>
{
    public async Task<Result<SubscriptionInvoiceSummaryDto>> Handle(GetOwnerSubscriptionInvoiceDetailQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure<SubscriptionInvoiceSummaryDto>(Error.NotFound("BarberShop"));

        var invoice = await ctx.Set<SubscriptionInvoice>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure<SubscriptionInvoiceSummaryDto>(Error.NotFound("SubscriptionInvoice"));
        if (invoice.BarberShopId != shop.Id) return Result.Failure<SubscriptionInvoiceSummaryDto>(Error.Forbidden("Bu fatura size ait değil."));

        return Result.Success(invoice.ToSummaryDto());
    }
}

public sealed record AdminSubscriptionInvoiceItemDto(
    Guid Id,
    Guid BarberShopId,
    string BarberShopName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PlanCode,
    long PlanPriceMinor,
    long AmountMinor,
    DateOnly DueDate,
    SubscriptionInvoiceStatus Status,
    bool IsDisputed);

public sealed record AdminSubscriptionInvoicesPage(
    IReadOnlyList<AdminSubscriptionInvoiceItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>
/// Follow-up (review): PeriodStart eşitlik filtresi — web-admin tahsilat ekranının
/// "şu ayın faturaları" görünümü için. Yalnız TAM eşleşme (fatura'ların PeriodStart'ı
/// her zaman ayın 1'i — CloseMonthlySubscriptionInvoicesJob'un ürettiği tek biçim); aralık
/// sorgusu (BETWEEN) şu an YAGNI, ileride "son N ay" gibi bir ihtiyaç çıkarsa eklenir.
/// </summary>
public sealed record GetAdminSubscriptionInvoicesQueueQuery(
    SubscriptionInvoiceStatus? StatusFilter,
    bool? DisputedOnly,
    DateOnly? PeriodStart,
    int Page,
    int PageSize) : IQuery<AdminSubscriptionInvoicesPage>;

public sealed class GetAdminSubscriptionInvoicesQueueQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminSubscriptionInvoicesQueueQuery, Result<AdminSubscriptionInvoicesPage>>
{
    public async Task<Result<AdminSubscriptionInvoicesPage>> Handle(GetAdminSubscriptionInvoicesQueueQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var query = ctx.Set<SubscriptionInvoice>().AsNoTracking().AsQueryable();
        if (request.StatusFilter.HasValue) query = query.Where(s => s.Status == request.StatusFilter.Value);
        if (request.DisputedOnly == true) query = query.Where(s => s.IsDisputed);
        if (request.PeriodStart.HasValue) query = query.Where(s => s.PeriodStart == request.PeriodStart.Value);

        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 50);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(s => s.DueDate)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(s => new
            {
                Invoice = s,
                ShopName = ctx.Set<BarberShop>().Where(shop => shop.Id == s.BarberShopId).Select(shop => shop.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var dtos = items.Select(x => new AdminSubscriptionInvoiceItemDto(
            x.Invoice.Id, x.Invoice.BarberShopId, x.ShopName ?? "—",
            x.Invoice.PeriodStart, x.Invoice.PeriodEnd,
            x.Invoice.PlanCode, x.Invoice.PlanPriceMinor, x.Invoice.AmountMinor,
            x.Invoice.DueDate, x.Invoice.Status, x.Invoice.IsDisputed))
            .ToList();

        return Result.Success(new AdminSubscriptionInvoicesPage(dtos, page, size, total));
    }
}
