using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Billing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Billing;

public sealed record DisputeCommissionStatementCommand(Guid OwnerUserId, Guid StatementId, string Note) : ICommand;

public sealed class DisputeCommissionStatementCommandValidator : AbstractValidator<DisputeCommissionStatementCommand>
{
    public DisputeCommissionStatementCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.StatementId).NotEmpty();
        RuleFor(x => x.Note).NotEmpty().MinimumLength(5).MaximumLength(1000);
    }
}

/// <summary>
/// Owner itirazı — yalnız bir bayrak koyar, RFC-012 kasıtlı olarak vade sayacını DURDURMAZ
/// (gerekçe: CommissionStatement.Dispute XML doc'u). Admin dispute kuyruğunda görür.
/// </summary>
public sealed class DisputeCommissionStatementCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<DisputeCommissionStatementCommand, Result>
{
    public async Task<Result> Handle(DisputeCommissionStatementCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerId == request.OwnerUserId, ct);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        var statement = await ctx.Set<CommissionStatement>().FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure(Error.NotFound("CommissionStatement"));
        if (statement.BarberShopId != shop.Id) return Result.Failure(Error.Forbidden("Bu statement size ait değil."));

        var result = statement.Dispute(request.Note);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "owner.commission_statement.disputed",
            nameof(CommissionStatement),
            statement.Id.ToString(),
            actorUserId: request.OwnerUserId,
            payload: new { note = request.Note },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record MarkCommissionStatementPaidCommand(Guid AdminUserId, Guid StatementId, string Reason) : ICommand;

public sealed class MarkCommissionStatementPaidCommandValidator : AbstractValidator<MarkCommissionStatementPaidCommand>
{
    public MarkCommissionStatementPaidCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.StatementId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(5).MaximumLength(500);
    }
}

/// <summary>
/// Havale/EFT onayı — admin dekontu elle doğruladıktan sonra çağırır. RFC-012 madde 4:
/// bu OTOMATİK olarak BarberShop.Reactivate() ÇAĞIRMAZ — hesap Suspended ise admin AYRI
/// bir "reactivate" aksiyonuyla açar (regresyon testiyle korunan davranış).
/// </summary>
public sealed class MarkCommissionStatementPaidCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<MarkCommissionStatementPaidCommand, Result>
{
    public async Task<Result> Handle(MarkCommissionStatementPaidCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var statement = await ctx.Set<CommissionStatement>().FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure(Error.NotFound("CommissionStatement"));

        var result = statement.MarkPaid();
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.commission_statement.marked_paid",
            nameof(CommissionStatement),
            statement.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { reason = request.Reason, statement.AmountDueMinor },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AdjustCommissionStatementAmountCommand(Guid AdminUserId, Guid StatementId, long NewAmountDueMinor, string Reason) : ICommand;

public sealed class AdjustCommissionStatementAmountCommandValidator : AbstractValidator<AdjustCommissionStatementAmountCommand>
{
    public AdjustCommissionStatementAmountCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.StatementId).NotEmpty();
        RuleFor(x => x.NewAmountDueMinor).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(8).MaximumLength(500);
    }
}

public sealed class AdjustCommissionStatementAmountCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<AdjustCommissionStatementAmountCommand, Result>
{
    public async Task<Result> Handle(AdjustCommissionStatementAmountCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var statement = await ctx.Set<CommissionStatement>().FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure(Error.NotFound("CommissionStatement"));

        var previousAmount = statement.AmountDueMinor;
        var result = statement.AdjustAmount(request.NewAmountDueMinor, request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.commission_statement.amount_adjusted",
            nameof(CommissionStatement),
            statement.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { previousAmount, newAmount = request.NewAmountDueMinor, reason = request.Reason },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ExtendCommissionStatementDueDateCommand(Guid AdminUserId, Guid StatementId, DateOnly NewDueDate, string Reason) : ICommand;

public sealed class ExtendCommissionStatementDueDateCommandValidator : AbstractValidator<ExtendCommissionStatementDueDateCommand>
{
    public ExtendCommissionStatementDueDateCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.StatementId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(8).MaximumLength(500);
    }
}

/// <summary>RFC-012'de vade sayacını durdurabilen TEK admin mekanizması (dispute otomatik durdurmaz).</summary>
public sealed class ExtendCommissionStatementDueDateCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<ExtendCommissionStatementDueDateCommand, Result>
{
    public async Task<Result> Handle(ExtendCommissionStatementDueDateCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var statement = await ctx.Set<CommissionStatement>().FirstOrDefaultAsync(s => s.Id == request.StatementId, ct);
        if (statement is null) return Result.Failure(Error.NotFound("CommissionStatement"));

        var previousDueDate = statement.DueDate;
        var result = statement.ExtendDueDate(request.NewDueDate, request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.commission_statement.due_date_extended",
            nameof(CommissionStatement),
            statement.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { previousDueDate, newDueDate = request.NewDueDate, reason = request.Reason },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
