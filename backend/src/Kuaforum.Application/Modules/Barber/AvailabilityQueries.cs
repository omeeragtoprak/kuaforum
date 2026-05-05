using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Barber;

public sealed record WorkingHoursDto(
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    bool IsClosed);

public sealed record GetWorkingHoursQuery(Guid StylistId) : IQuery<IReadOnlyList<WorkingHoursDto>>;

public sealed class GetWorkingHoursQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetWorkingHoursQuery, Result<IReadOnlyList<WorkingHoursDto>>>
{
    public async Task<Result<IReadOnlyList<WorkingHoursDto>>> Handle(GetWorkingHoursQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var stylistExists = await ctx.Set<Stylist>().AnyAsync(s => s.Id == request.StylistId, ct);
        if (!stylistExists) return Result.Failure<IReadOnlyList<WorkingHoursDto>>(Error.NotFound("Stylist"));

        var rows = await ctx.Set<WorkingHours>().AsNoTracking()
            .Where(w => w.StylistId == request.StylistId)
            .ToListAsync(ct);

        var byDay = rows.ToDictionary(w => w.DayOfWeek);

        var ordered = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        };

        var result = ordered.Select(d => byDay.TryGetValue(d, out var w)
            ? new WorkingHoursDto(d, w.OpenTime, w.CloseTime, w.IsClosed)
            : new WorkingHoursDto(d, new TimeOnly(9, 0), new TimeOnly(19, 0), true))
            .ToList();

        return Result.Success<IReadOnlyList<WorkingHoursDto>>(result);
    }
}

public sealed record TimeOffDto(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string? Reason);

public sealed record GetTimeOffsQuery(Guid StylistId) : IQuery<IReadOnlyList<TimeOffDto>>;

public sealed class GetTimeOffsQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetTimeOffsQuery, Result<IReadOnlyList<TimeOffDto>>>
{
    public async Task<Result<IReadOnlyList<TimeOffDto>>> Handle(GetTimeOffsQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var stylistExists = await ctx.Set<Stylist>().AnyAsync(s => s.Id == request.StylistId, ct);
        if (!stylistExists) return Result.Failure<IReadOnlyList<TimeOffDto>>(Error.NotFound("Stylist"));

        var now = DateTimeOffset.UtcNow;
        var rows = await ctx.Set<TimeOff>().AsNoTracking()
            .Where(t => t.StylistId == request.StylistId && t.EndsAt > now)
            .OrderBy(t => t.StartsAt)
            .Select(t => new TimeOffDto(t.Id, t.StartsAt, t.EndsAt, t.Reason))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TimeOffDto>>(rows);
    }
}

public sealed record DeleteTimeOffCommand(
    Guid OwnerId,
    Guid StylistId,
    Guid TimeOffId) : ICommand;

public sealed class DeleteTimeOffCommandValidator : AbstractValidator<DeleteTimeOffCommand>
{
    public DeleteTimeOffCommandValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.TimeOffId).NotEmpty();
    }
}

public sealed class DeleteTimeOffCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<DeleteTimeOffCommand, Result>
{
    public async Task<Result> Handle(DeleteTimeOffCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, ct);
        if (stylist is null) return Result.Failure(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, ct);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var timeOff = await ctx.Set<TimeOff>()
            .FirstOrDefaultAsync(t => t.Id == request.TimeOffId && t.StylistId == request.StylistId, ct);
        if (timeOff is null) return Result.Failure(Error.NotFound("TimeOff"));

        ctx.Set<TimeOff>().Remove(timeOff);

        await audit.RecordAsync(
            "barber.timeoff.deleted",
            nameof(TimeOff),
            timeOff.Id.ToString(),
            actorUserId: request.OwnerId,
            payload: new { stylistId = request.StylistId, startsAt = timeOff.StartsAt, endsAt = timeOff.EndsAt },
            ct: ct);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
