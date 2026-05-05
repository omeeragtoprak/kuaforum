using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record CreateBarberShopCommand(
    Guid OwnerId,
    string Name,
    SalonGenderType GenderType,
    string? Description,
    string? Phone) : ICommand<Guid>;

public sealed class CreateBarberShopCommandValidator : AbstractValidator<CreateBarberShopCommand>
{
    public CreateBarberShopCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.GenderType).IsInEnum();
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class CreateBarberShopCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit) : IRequestHandler<CreateBarberShopCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateBarberShopCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var owner = await ctx.Set<User>().FirstOrDefaultAsync(u => u.Id == request.OwnerId, cancellationToken);
        if (owner is null) return Result.Failure<Guid>(Error.NotFound("Owner"));

        var existing = await ctx.Set<BarberShop>().AnyAsync(s => s.OwnerId == request.OwnerId, cancellationToken);
        if (existing) return Result.Failure<Guid>(Error.Conflict("Bu kullanıcının zaten bir işletmesi var. Çoklu işletme desteği V1.5'te gelecek."));

        var createResult = BarberShop.Create(request.OwnerId, request.Name, request.GenderType);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        var shop = createResult.Value;
        var update = shop.UpdateBasics(name: null, description: request.Description, genderType: null, phone: request.Phone);
        if (update.IsFailure) return Result.Failure<Guid>(update.Error);

        ctx.Set<BarberShop>().Add(shop);

        await audit.RecordAsync("barber.shop.created", nameof(BarberShop), shop.Id.ToString(),
            actorUserId: request.OwnerId, payload: new { shop.Name, shop.GenderType }, ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(shop.Id);
    }
}
