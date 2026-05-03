using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record CreateServiceCommand(
    Guid OwnerId,
    Guid BarberShopId,
    string Name,
    ServiceGender Gender,
    int DurationMinutes,
    long PriceMinor,
    int BufferMinutes,
    string? Description) : ICommand<Guid>;

public sealed class CreateServiceCommandValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(5, 600);
        RuleFor(x => x.PriceMinor).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BufferMinutes).InclusiveBetween(0, 60);
    }
}

public sealed class CreateServiceCommandHandler(IApplicationDbContext db) : IRequestHandler<CreateServiceCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == request.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<Guid>(Error.Forbidden("Not owner."));

        var createResult = ServiceCatalog.Create(
            request.BarberShopId, request.Name, request.Gender,
            request.DurationMinutes, request.PriceMinor, request.BufferMinutes, request.Description);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        ctx.Set<ServiceCatalog>().Add(createResult.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(createResult.Value.Id);
    }
}

public sealed record SetWorkingHoursCommand(
    Guid OwnerId,
    Guid StylistId,
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    bool IsClosed) : ICommand;

public sealed class SetWorkingHoursCommandHandler(IApplicationDbContext db) : IRequestHandler<SetWorkingHoursCommand, Result>
{
    public async Task<Result> Handle(SetWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Not owner."));

        var existing = await ctx.Set<WorkingHours>()
            .FirstOrDefaultAsync(w => w.StylistId == request.StylistId && w.DayOfWeek == request.DayOfWeek, cancellationToken);

        if (existing is null)
        {
            var createResult = WorkingHours.Create(request.StylistId, request.DayOfWeek, request.OpenTime, request.CloseTime, request.IsClosed);
            if (createResult.IsFailure) return createResult;
            ctx.Set<WorkingHours>().Add(createResult.Value);
        }
        else
        {
            var update = existing.Update(request.OpenTime, request.CloseTime, request.IsClosed);
            if (update.IsFailure) return update;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record AddTimeOffCommand(
    Guid OwnerId,
    Guid StylistId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason) : ICommand<Guid>;

public sealed class AddTimeOffCommandHandler(IApplicationDbContext db) : IRequestHandler<AddTimeOffCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddTimeOffCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<Guid>(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<Guid>(Error.Forbidden("Not owner."));

        var createResult = TimeOff.Create(request.StylistId, request.StartsAt, request.EndsAt, request.Reason);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        ctx.Set<TimeOff>().Add(createResult.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(createResult.Value.Id);
    }
}
