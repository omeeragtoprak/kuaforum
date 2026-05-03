using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record AddStylistCommand(
    Guid OwnerId,
    Guid BarberShopId,
    string DisplayName,
    Guid? UserId) : ICommand<Guid>;

public sealed class AddStylistCommandValidator : AbstractValidator<AddStylistCommand>
{
    public AddStylistCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(80);
    }
}

public sealed class AddStylistCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<AddStylistCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddStylistCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure<Guid>(Error.NotFound("BarberShop"));
        if (shop.OwnerId != request.OwnerId) return Result.Failure<Guid>(Error.Forbidden("Not owner."));

        var createResult = Stylist.Create(request.BarberShopId, request.DisplayName, request.UserId);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        ctx.Set<Stylist>().Add(createResult.Value);
        await audit.RecordAsync("barber.stylist.added", nameof(Stylist), createResult.Value.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { request.DisplayName }, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(createResult.Value.Id);
    }
}

public sealed record UpdateStylistCommand(
    Guid OwnerId,
    Guid StylistId,
    string? DisplayName,
    string? PhotoUrl,
    string? Bio,
    bool? IsActive) : ICommand;

public sealed class UpdateStylistCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateStylistCommand, Result>
{
    public async Task<Result> Handle(UpdateStylistCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>()
            .Include(s => EF.Property<BarberShop>(s, "_shop"))
            .FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);

        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Not owner of this stylist's shop."));

        var update = stylist.UpdateProfile(request.DisplayName, request.PhotoUrl, request.Bio);
        if (update.IsFailure) return update;
        if (request.IsActive.HasValue) stylist.SetActive(request.IsActive.Value);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ToggleStylistWalkInCommand(Guid OwnerId, Guid StylistId, bool Available) : ICommand;

public sealed class ToggleStylistWalkInCommandHandler(IApplicationDbContext db) : IRequestHandler<ToggleStylistWalkInCommand, Result>
{
    public async Task<Result> Handle(ToggleStylistWalkInCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Not owner."));

        var toggle = stylist.ToggleWalkIn(request.Available);
        if (toggle.IsFailure) return toggle;
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
