using FluentValidation;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Application.Modules.Notification.EventHandlers;
using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Barber;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Notification;
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

/// <summary>RFC-015 §2 D1: kaydetme yanıtı — o günün YENİ aralığı dışında kalan gelecekteki
/// randevuları taşır (varsa müşterilere zaten bildirim gitmiştir, bkz. handler). Mobil bunu
/// kaydetme sonrası "3 randevu etkilendi, müşterilere bildirim gönderildi" onayı için kullanır;
/// önceden aynı sonucu <see cref="GetWorkingHoursChangeImpactQuery"/> ile KAYDETMEDEN görebilir
/// (aşağıdaki önizleme ucu — iki endpoint'in neden ayrı var olduğu için o kaydın XML doc'una bkz.).</summary>
public sealed record WorkingHoursSaveResultDto(IReadOnlyList<AffectedBookingDto> AffectedBookings);

public sealed record SetWorkingHoursCommand(
    Guid OwnerId,
    Guid StylistId,
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    bool IsClosed) : ICommand<WorkingHoursSaveResultDto>;

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

/// <summary>
/// RFC-015 §2 D1: engellemez — kuaför her zaman kaydedebilir (hastalık, cenaze, acil kapanış
/// gibi durumlarda ayarını değiştirebilmesi gerekiyor). Kayıttan sonra o günün yeni aralığı
/// dışında kalan gelecekteki Pending/Confirmed randevular varsa etkilenen müşterilere bildirim
/// gider — randevu OTOMATİK İPTAL EDİLMEZ, iptal/erteleme kararı taraflara ait.
///
/// İdempotency (RFC madde 4 — "aynı değişiklik aynı müşteriye tekrar tekrar gitmesin"): bildirim
/// yalnız STORE EDİLMİŞ değerler (open/close/isClosed) gerçekten değişmişse gönderilir. Owner aynı
/// değerlerle tekrar kaydederse (autosave/double-submit/gerçek bir değişiklik yapmadan "Kaydet"e
/// tekrar basmak) <c>isActualChange</c> false olur, sorgu bile çalışmaz — aynı randevu için ikinci
/// bir bildirim üretilmez. Gerçekten FARKLI bir değere tekrar tekrar kaydetmek (ör. önce 18:00'e,
/// sonra 17:00'ye kapatmak) her seferinde YENİ bir bildirim üretir — bu bilinçli: "aynı değişiklik"
/// literal olarak aynı değerler anlamına gelir, "bu güne bir daha dokunma" değil.
///
/// Bildirim domain-event üzerinden DEĞİL, doğrudan <see cref="INotificationDispatcher"/> ile
/// gönderiliyor — codebase'in yerleşik kalıbından (aggregate.RaiseDomainEvent → Notification
/// modülünde ayrı handler) bilinçli bir sapma: bu bildirim ne WorkingHours'a ne de tek bir
/// Booking'e ait bir olay, ikisinin KESİŞİMİ (N farklı booking, hiçbiri state değiştirmiyor).
/// Olayı taşımak için N adet "hiçbir şeyi değiştirmeyen" Booking güncellemesi üretmek (yalnız
/// RaiseDomainEvent çağırmak için) gereksiz karmaşıklık olurdu.
/// </summary>
public sealed class SetWorkingHoursCommandHandler(
    IApplicationDbContext db,
    IAuditLogger audit,
    INotificationDispatcher dispatcher) : IRequestHandler<SetWorkingHoursCommand, Result<WorkingHoursSaveResultDto>>
{
    public async Task<Result<WorkingHoursSaveResultDto>> Handle(SetWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().FirstOrDefaultAsync(s => s.Id == request.StylistId, cancellationToken);
        if (stylist is null) return Result.Failure<WorkingHoursSaveResultDto>(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, cancellationToken);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<WorkingHoursSaveResultDto>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var existing = await ctx.Set<WorkingHours>()
            .FirstOrDefaultAsync(w => w.StylistId == request.StylistId && w.DayOfWeek == request.DayOfWeek, cancellationToken);

        var isActualChange = existing is null
            || existing.OpenTime != request.OpenTime
            || existing.CloseTime != request.CloseTime
            || existing.IsClosed != request.IsClosed;

        if (existing is null)
        {
            var createResult = WorkingHours.Create(request.StylistId, request.DayOfWeek, request.OpenTime, request.CloseTime, request.IsClosed);
            if (createResult.IsFailure) return Result.Failure<WorkingHoursSaveResultDto>(createResult.Error);
            ctx.Set<WorkingHours>().Add(createResult.Value);
        }
        else
        {
            var update = existing.Update(request.OpenTime, request.CloseTime, request.IsClosed);
            if (update.IsFailure) return Result.Failure<WorkingHoursSaveResultDto>(update.Error);
        }

        await audit.RecordAsync(
            "barber.workinghours.set",
            nameof(WorkingHours),
            $"{request.StylistId}:{request.DayOfWeek}",
            actorUserId: request.OwnerId,
            payload: new { stylistId = request.StylistId, dayOfWeek = request.DayOfWeek.ToString(), openTime = request.OpenTime.ToString(), closeTime = request.CloseTime.ToString(), isClosed = request.IsClosed },
            ct: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        if (!isActualChange)
            return Result.Success(new WorkingHoursSaveResultDto(Array.Empty<AffectedBookingDto>()));

        var affected = await WorkingHoursImpactFinder.FindOutsideNewRangeAsync(
            ctx, request.StylistId, request.DayOfWeek, request.OpenTime, request.CloseTime, request.IsClosed, cancellationToken);

        foreach (var b in affected)
        {
            var localTime = NotificationHelpers.LocalTr(b.ScheduledAt);
            const string subject = "Salonun çalışma saati değişti";
            var body = $"{shop.Name}, çalışma saatlerini güncelledi. {localTime} randevun artık bu saatlerin dışında kalıyor — lütfen salonla iletişime geç.";

            await NotificationHelpers.DispatchInAppAsync(dispatcher, b.CustomerId,
                "barber.working_hours.changed", subject, body, cancellationToken, relatedBookingId: b.BookingId);

            await dispatcher.DispatchAsync(new NotificationRequest(
                UserId: b.CustomerId,
                Channel: NotificationChannel.Push,
                TemplateKey: "barber.working_hours.changed",
                Recipient: $"fcm-token-{b.CustomerId:N}",
                Subject: subject,
                Body: body,
                RelatedBookingId: b.BookingId), cancellationToken);
        }

        return Result.Success(new WorkingHoursSaveResultDto(affected));
    }
}

/// <summary>
/// RFC-015 §2 D1 madde 2: "kaydetmeden önce liste gösterilir" — bu ayrı, salt-okunur önizleme
/// ucu tam olarak bu adımı karşılar. Karar (ayrı uç mu, kaydetme yanıtında mı): İKİSİ BİRDEN,
/// farklı anlar için: bu uç mobil'in KAYDETMEDEN ÖNCE gösterdiği onay diyaloğunu besler ("Bu
/// değişiklik 3 randevunu etkiliyor" — "Emin misiniz?" değil, ne olacağını söyler); kaydetme
/// endpoint'inin yanıtındaki <see cref="WorkingHoursSaveResultDto.AffectedBookings"/> ise
/// kayıttan SONRAKİ kesin sonucu taşır (bildirim fiilen kime gitti). Owner bu önizlemeyi hiç
/// çağırmadan doğrudan kaydedebilir (örn. API entegrasyonu) — save endpoint'i o durumda da
/// kendi payload'ını üretir, önizlemeye bağımlı değil. Hiçbir persistans/audit/bildirim YAPMAZ.
/// </summary>
public sealed record GetWorkingHoursChangeImpactQuery(
    Guid OwnerId,
    Guid StylistId,
    DayOfWeek DayOfWeek,
    TimeOnly OpenTime,
    TimeOnly CloseTime,
    bool IsClosed) : IQuery<IReadOnlyList<AffectedBookingDto>>;

public sealed class GetWorkingHoursChangeImpactQueryValidator : AbstractValidator<GetWorkingHoursChangeImpactQuery>
{
    public GetWorkingHoursChangeImpactQueryValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
        RuleFor(x => x.StylistId).NotEmpty();
        RuleFor(x => x.DayOfWeek).IsInEnum();
        RuleFor(x => x.OpenTime).LessThan(x => x.CloseTime).When(x => !x.IsClosed)
            .WithMessage("Açılış saati kapanış saatinden önce olmalı (kapalı günler hariç).");
    }
}

public sealed class GetWorkingHoursChangeImpactQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetWorkingHoursChangeImpactQuery, Result<IReadOnlyList<AffectedBookingDto>>>
{
    public async Task<Result<IReadOnlyList<AffectedBookingDto>>> Handle(GetWorkingHoursChangeImpactQuery request, CancellationToken ct)
    {
        var ctx = (DbContext)db;
        var stylist = await ctx.Set<Stylist>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StylistId, ct);
        if (stylist is null) return Result.Failure<IReadOnlyList<AffectedBookingDto>>(Error.NotFound("Stylist"));

        var shop = await ctx.Set<BarberShop>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == stylist.BarberShopId, ct);
        if (shop is null || shop.OwnerId != request.OwnerId)
            return Result.Failure<IReadOnlyList<AffectedBookingDto>>(Error.Forbidden("Bu işletmenin sahibi siz değilsiniz."));

        var affected = await WorkingHoursImpactFinder.FindOutsideNewRangeAsync(
            ctx, request.StylistId, request.DayOfWeek, request.OpenTime, request.CloseTime, request.IsClosed, ct);

        return Result.Success<IReadOnlyList<AffectedBookingDto>>(affected);
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
