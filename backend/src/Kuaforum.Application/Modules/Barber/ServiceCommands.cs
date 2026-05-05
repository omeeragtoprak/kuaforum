using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
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
            return Result.Failure<Guid>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var createResult = ServiceCatalog.Create(
            request.BarberShopId, request.Name, request.Gender,
            request.DurationMinutes, request.PriceMinor, request.BufferMinutes, request.Description);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        ctx.Set<ServiceCatalog>().Add(createResult.Value);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(createResult.Value.Id);
    }
}

public sealed record UpdateServiceCommand(
    Guid OwnerId,
    Guid BarberShopId,
    Guid ServiceId,
    string? Name,
    string? Description,
    int? DurationMinutes,
    long? PriceMinor,
    int? BufferMinutes) : ICommand;

public sealed class UpdateServiceCommandValidator : AbstractValidator<UpdateServiceCommand>
{
    public UpdateServiceCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Name!).MaximumLength(80).When(x => x.Name is not null);
        RuleFor(x => x.DurationMinutes!.Value).InclusiveBetween(5, 600).When(x => x.DurationMinutes.HasValue);
        RuleFor(x => x.PriceMinor!.Value).GreaterThanOrEqualTo(0).When(x => x.PriceMinor.HasValue);
        RuleFor(x => x.BufferMinutes!.Value).InclusiveBetween(0, 60).When(x => x.BufferMinutes.HasValue);
    }
}

public sealed class UpdateServiceCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<UpdateServiceCommand, Result>
{
    public async Task<Result> Handle(UpdateServiceCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BarberShopId, ct);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var service = await ctx.Set<ServiceCatalog>()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.BarberShopId == request.BarberShopId, ct);
        if (service is null) return Result.Failure(Error.NotFound("Hizmet bulunamadı."));

        var update = service.Update(request.Name, request.Description, request.DurationMinutes, request.PriceMinor, request.BufferMinutes);
        if (update.IsFailure) return update;

        await audit.RecordAsync(
            "barber.service.updated",
            nameof(ServiceCatalog),
            service.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new { barberShopId = request.BarberShopId, name = service.Name, priceMinor = service.PriceMinor, durationMinutes = service.DurationMinutes },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record DeleteServiceCommand(
    Guid OwnerId,
    Guid BarberShopId,
    Guid ServiceId) : ICommand;

public sealed class DeleteServiceCommandValidator : AbstractValidator<DeleteServiceCommand>
{
    public DeleteServiceCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.BarberShopId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
    }
}

public sealed class DeleteServiceCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<DeleteServiceCommand, Result>
{
    public async Task<Result> Handle(DeleteServiceCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.BarberShopId, ct);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var service = await ctx.Set<ServiceCatalog>()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.BarberShopId == request.BarberShopId, ct);
        if (service is null) return Result.Failure(Error.NotFound("Hizmet bulunamadı."));

        service.SetActive(false);

        await audit.RecordAsync(
            "barber.service.deactivated",
            nameof(ServiceCatalog),
            service.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new { barberShopId = request.BarberShopId, name = service.Name },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetWorkingHoursCommand(
    Guid OwnerId,
    Guid StylistId,
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    bool IsClosed) : ICommand;

public sealed class SetWorkingHoursCommandValidator : AbstractValidator<SetWorkingHoursCommand>
{
    public SetWorkingHoursCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.DayOfWeek).IsInEnum();
        RuleFor(x => x.OpenTime).LessThan(x => x.CloseTime).When(x => !x.IsClosed)
            .WithMessage("Açılış saati kapanış saatinden önce olmalı (kapalı günler hariç).");
    }
}

public sealed class SetWorkingHoursCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<SetWorkingHoursCommand, Result>
{
    public async Task<Result> Handle(SetWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

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

        await audit.RecordAsync(
            "barber.workinghours.set",
            nameof(WorkingHours),
            $"{request.StylistId}:{request.DayOfWeek}",
            actorUserId: request.OwnerId,
            payload: new { stylistId = request.StylistId, dayOfWeek = request.DayOfWeek.ToString(), openTime = request.OpenTime.ToString(), closeTime = request.CloseTime.ToString(), isClosed = request.IsClosed },
            ct: cancellationToken);

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

public sealed class AddTimeOffCommandValidator : AbstractValidator<AddTimeOffCommand>
{
    public AddTimeOffCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.StartsAt).LessThan(x => x.EndsAt)
            .WithMessage("Başlangıç zamanı bitiş zamanından önce olmalı.");
        RuleFor(x => x.Reason).MaximumLength(200);
    }
}

public sealed class AddTimeOffCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<AddTimeOffCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AddTimeOffCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<Guid>(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<Guid>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var blockingStatuses = new[] { BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.Started };
        var conflictCount = await ctx.Set<BookingAggregate>().AsNoTracking()
            .CountAsync(b => b.StylistId == request.StylistId
                             && blockingStatuses.Contains(b.Status)
                             && b.ScheduledAt < request.EndsAt
                             && b.ScheduledEndsAt > request.StartsAt, cancellationToken);
        if (conflictCount > 0)
            return Result.Failure<Guid>(Error.Conflict($"Bu zaman aralığında {conflictCount} aktif randevu var. Önce onları iptal etmelisin."));

        var createResult = TimeOff.Create(request.StylistId, request.StartsAt, request.EndsAt, request.Reason);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        ctx.Set<TimeOff>().Add(createResult.Value);

        await audit.RecordAsync(
            "barber.timeoff.added",
            nameof(TimeOff),
            createResult.Value.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new { stylistId = request.StylistId, startsAt = request.StartsAt, endsAt = request.EndsAt, reason = request.Reason },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(createResult.Value.Id);
    }
}
