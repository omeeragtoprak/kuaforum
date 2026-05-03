using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Geography;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record UpdateBarberShopCommand(
    Guid OwnerId,
    Guid BarberShopId,
    string? Name,
    string? Description,
    SalonGenderType? GenderType,
    string? Phone) : ICommand;

public sealed class UpdateBarberShopCommandValidator : AbstractValidator<UpdateBarberShopCommand>
{
    public UpdateBarberShopCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(120).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class UpdateBarberShopCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<UpdateBarberShopCommand, Result>
{
    public async Task<Result> Handle(UpdateBarberShopCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));
        if (shop.OwnerId != request.OwnerId) return Result.Failure(Error.Forbidden("Not owner."));

        var update = shop.UpdateBasics(request.Name, request.Description, request.GenderType, request.Phone);
        if (update.IsFailure) return update;

        await audit.RecordAsync("barber.shop.updated", nameof(BarberShop), shop.Id.ToString(),
            actorUserId: request.OwnerId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record SetBarberShopAddressCommand(
    Guid OwnerId,
    Guid BarberShopId,
    Guid CityId,
    Guid DistrictId,
    Guid NeighborhoodId,
    string Line1,
    string? Line2,
    string? PostalCode,
    double Latitude,
    double Longitude) : ICommand;

public sealed class SetBarberShopAddressCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<SetBarberShopAddressCommand, Result>
{
    public async Task<Result> Handle(SetBarberShopAddressCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null) return Result.Failure(Error.NotFound("BarberShop"));
        if (shop.OwnerId != request.OwnerId) return Result.Failure(Error.Forbidden("Not owner."));

        var coordResult = Coordinate.TryCreate(request.Latitude, request.Longitude);
        if (coordResult.IsFailure) return coordResult;

        var addressResult = Address.Create(
            request.CityId, request.DistrictId, request.NeighborhoodId,
            request.Line1, coordResult.Value, request.Line2, request.PostalCode);
        if (addressResult.IsFailure) return addressResult;

        var setResult = shop.SetAddress(addressResult.Value);
        if (setResult.IsFailure) return setResult;

        await audit.RecordAsync("barber.shop.address.set", nameof(BarberShop), shop.Id.ToString(),
            actorUserId: request.OwnerId, ct: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
