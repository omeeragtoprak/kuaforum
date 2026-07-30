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
    Guid? NeighborhoodId,
    string Line1,
    string? Line2,
    string? PostalCode,
    double Latitude,
    double Longitude) : ICommand;

public sealed class SetBarberShopAddressCommandValidator : AbstractValidator<SetBarberShopAddressCommand>
{
    public SetBarberShopAddressCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.CityId).NotEmpty();
        RuleFor(x => x.DistrictId).NotEmpty();
        // RFC-007 kapanış: mahalle OPSİYONEL (TR mahalle verisi Erzurum-pilot).
        // Gönderildiyse boş Guid olamaz; varlık kontrolü handler'da (DB gerekli).
        RuleFor(x => x.NeighborhoodId)
            .NotEqual(Guid.Empty)
            .When(x => x.NeighborhoodId.HasValue)
            .WithMessage("Mahalle seçimi geçersiz.");
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200);
        RuleFor(x => x.PostalCode).MaximumLength(10);
        RuleFor(x => x.Latitude).InclusiveBetween(35.5, 42.5).WithMessage("Türkiye sınırları dışı.");
        RuleFor(x => x.Longitude).InclusiveBetween(25.5, 45.0).WithMessage("Türkiye sınırları dışı.");
    }
}

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

        // Mahalle opsiyonel; ama VERİLDİYSE gerçekten var olmalı ve seçilen
        // ilçeye ait olmalı — aksi halde keşfet/filtre tarafında çöp FK kalır.
        if (request.NeighborhoodId.HasValue)
        {
            var neighborhood = await ctx.Set<Neighborhood>().AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == request.NeighborhoodId.Value, cancellationToken);
            if (neighborhood is null)
                return Result.Failure(Error.Validation("Mahalle bulunamadı."));
            if (neighborhood.DistrictId != request.DistrictId)
                return Result.Failure(Error.Validation("Mahalle seçilen ilçeye ait değil."));
        }

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
