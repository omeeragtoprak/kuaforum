using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record DisputeSubscriptionInvoiceCommand(Guid OwnerUserId, Guid InvoiceId, string Note) : ICommand;

public sealed class DisputeSubscriptionInvoiceCommandValidator : AbstractValidator<DisputeSubscriptionInvoiceCommand>
{
    public DisputeSubscriptionInvoiceCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MinimumLength(5).MaximumLength(1000);
    }
}

/// <summary>
/// Owner itirazı — yalnız bir bayrak koyar, RFC-012 kasıtlı olarak vade sayacını DURDURMAZ
/// (gerekçe: SubscriptionInvoice.Dispute XML doc'u). Admin dispute kuyruğunda görür.
/// </summary>
public sealed class DisputeSubscriptionInvoiceCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<DisputeSubscriptionInvoiceCommand, Result>
{
    public async Task<Result> Handle(DisputeSubscriptionInvoiceCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        var invoice = await ctx.Set<SubscriptionInvoice>().FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure(Error.NotFound("SubscriptionInvoice"));
        if (invoice.BarberShopId != shop.Id) return Result.Failure(Error.Forbidden("Bu fatura size ait değil."));

        var result = invoice.Dispute(request.Note);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "owner.subscription_invoice.disputed",
            nameof(SubscriptionInvoice),
            invoice.Id.ToString(),
            actorUserId: request.OwnerUserId,
            payload: new { note = request.Note },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record MarkSubscriptionInvoicePaidCommand(Guid AdminUserId, Guid InvoiceId, string Reason) : ICommand;

public sealed class MarkSubscriptionInvoicePaidCommandValidator : AbstractValidator<MarkSubscriptionInvoicePaidCommand>
{
    public MarkSubscriptionInvoicePaidCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5).MaximumLength(500);
    }
}

/// <summary>
/// Havale/EFT onayı — admin dekontu elle doğruladıktan sonra çağırır. RFC-012 madde 4:
/// bu OTOMATİK olarak BarberShop.Reactivate() ÇAĞIRMAZ — hesap Suspended ise admin AYRI
/// bir "reactivate" aksiyonuyla açar (regresyon testiyle korunan davranış).
/// </summary>
public sealed class MarkSubscriptionInvoicePaidCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<MarkSubscriptionInvoicePaidCommand, Result>
{
    public async Task<Result> Handle(MarkSubscriptionInvoicePaidCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var invoice = await ctx.Set<SubscriptionInvoice>().FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure(Error.NotFound("SubscriptionInvoice"));

        var result = invoice.MarkPaid();
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.subscription_invoice.marked_paid",
            nameof(SubscriptionInvoice),
            invoice.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { reason = request.Reason, invoice.AmountMinor },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AdjustSubscriptionInvoiceAmountCommand(Guid AdminUserId, Guid InvoiceId, long NewAmountMinor, string Reason) : ICommand;

public sealed class AdjustSubscriptionInvoiceAmountCommandValidator : AbstractValidator<AdjustSubscriptionInvoiceAmountCommand>
{
    public AdjustSubscriptionInvoiceAmountCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.NewAmountMinor).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(8).MaximumLength(500);
    }
}

public sealed class AdjustSubscriptionInvoiceAmountCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<AdjustSubscriptionInvoiceAmountCommand, Result>
{
    public async Task<Result> Handle(AdjustSubscriptionInvoiceAmountCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var invoice = await ctx.Set<SubscriptionInvoice>().FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure(Error.NotFound("SubscriptionInvoice"));

        var previousAmount = invoice.AmountMinor;
        var result = invoice.AdjustAmount(request.NewAmountMinor, request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.subscription_invoice.amount_adjusted",
            nameof(SubscriptionInvoice),
            invoice.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { previousAmount, newAmount = request.NewAmountMinor, reason = request.Reason },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ExtendSubscriptionInvoiceDueDateCommand(Guid AdminUserId, Guid InvoiceId, DateOnly NewDueDate, string Reason) : ICommand;

public sealed class ExtendSubscriptionInvoiceDueDateCommandValidator : AbstractValidator<ExtendSubscriptionInvoiceDueDateCommand>
{
    public ExtendSubscriptionInvoiceDueDateCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(8).MaximumLength(500);
    }
}

/// <summary>RFC-012'de vade sayacını durdurabilen TEK admin mekanizması (dispute otomatik durdurmaz).</summary>
public sealed class ExtendSubscriptionInvoiceDueDateCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<ExtendSubscriptionInvoiceDueDateCommand, Result>
{
    public async Task<Result> Handle(ExtendSubscriptionInvoiceDueDateCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var invoice = await ctx.Set<SubscriptionInvoice>().FirstOrDefaultAsync(s => s.Id == request.InvoiceId, ct);
        if (invoice is null) return Result.Failure(Error.NotFound("SubscriptionInvoice"));

        var previousDueDate = invoice.DueDate;
        var result = invoice.ExtendDueDate(request.NewDueDate, request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.subscription_invoice.due_date_extended",
            nameof(SubscriptionInvoice),
            invoice.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { previousDueDate, newDueDate = request.NewDueDate, reason = request.Reason },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
