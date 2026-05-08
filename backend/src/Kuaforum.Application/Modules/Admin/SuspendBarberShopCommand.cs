using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Admin;

public sealed record SuspendBarberShopCommand(Guid AdminUserId, Guid BarberShopId, string Reason) : ICommand;

public sealed class SuspendBarberShopCommandValidator : AbstractValidator<SuspendBarberShopCommand>
{
    public SuspendBarberShopCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(8).MaximumLength(500)
            .WithMessage("Askıya alma sebebi en az 8 karakter olmalı, kalıcı audit log'a yazılır.");
    }
}

public sealed class SuspendBarberShopCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<SuspendBarberShopCommand, Result>
{
    public async Task<Result> Handle(SuspendBarberShopCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, ct);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        var result = shop.Suspend(request.Reason);
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.barbershop.suspended",
            nameof(BarberShop),
            shop.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { reason = request.Reason, shopName = shop.Name },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ReactivateBarberShopCommand(Guid AdminUserId, Guid BarberShopId) : ICommand;

public sealed class ReactivateBarberShopCommandValidator : AbstractValidator<ReactivateBarberShopCommand>
{
    public ReactivateBarberShopCommandValidator()
    {
        RuleFor(x => x.AdminUserId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
    }
}

public sealed class ReactivateBarberShopCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<ReactivateBarberShopCommand, Result>
{
    public async Task<Result> Handle(ReactivateBarberShopCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, ct);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));

        var result = shop.Reactivate();
        if (result.IsFailure) return result;

        await audit.RecordAsync(
            "admin.barbershop.reactivated",
            nameof(BarberShop),
            shop.Id.ToString(),
            actorUserId: request.AdminUserId,
            payload: new { shopName = shop.Name },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
