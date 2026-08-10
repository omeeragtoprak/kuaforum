using FluentValidation;
using Kuaforum.Application.Common;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using BookingAggregate = Kuaforum.Domain.Modules.Booking.Booking;
using Kuaforum.Domain.Modules.Booking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Kuaforum.Application.Modules.Booking;

/// <summary>
/// RFC-015 §2 D3 / docs/GAPS.md G4: müşteri tarafı "kuaför gelmedi / salon kapalıydı" bildirimi
/// — G4'ün belirttiği tek-yönlülüğün (yalnız <c>MarkNoShowCommand</c>, yalnız owner→müşteri)
/// karşı yönü. Minimum sürüm (RFC'nin kendi ifadesiyle): admin kuyruğuna düşer, OTOMATİK İADE
/// TETİKLEMEZ, sahte şikayet riskine karşı insan incelemesi şart.
/// </summary>
public sealed record FileBookingComplaintCommand(Guid CustomerId, Guid BookingId, string? Note) : ICommand<Guid>;

public sealed class FileBookingComplaintCommandValidator : AbstractValidator<FileBookingComplaintCommand>
{
    public FileBookingComplaintCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.BookingId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

/// <summary>
/// Yalnız GEÇMİŞ randevularda izin verilir (RFC madde 1) — sınır <see
/// cref="BookingAggregate.MarkNoShow"/>'un ters yönüyle AYNI ilkeyi kullanır (<c>UtcNow &gt;=
/// ScheduledEndsAt</c>): owner "gelmedi" işaretini randevu bitiş saatinden ÖNCE koyamıyorsa,
/// müşteri de "gelmedi" şikayetini randevu fiilen bitmeden bildiremez — iki yön aynı temporal
/// sınırı paylaşır, tutarlılık bilinçli.
///
/// İzin verilen durumlar SADECE Confirmed/Started/Completed — bunlar "salonun randevuyu kabul
/// ettiği ve gerçekleşmesi beklenen" durumlar. Cancelled/NoShow zaten ne olduğunu açıklayan
/// terminal durumlar (NoShow üstelik TERS yönde zaten bir kayıt); Pending hiç onaylanmamış bir
/// talep — salonun fiilen bir taahhüdü yoktu, "gelmedi" şikayetinin konusu olamaz.
///
/// İdempotent: randevu başına TEK şikayet — ikinci çağrı mevcut kaydı no-op döner (audit/bildirim
/// TEKRARLANMAZ).
/// </summary>
public sealed class FileBookingComplaintCommandHandler(IApplicationDbContext db, IAuditLogger audit)
    : IRequestHandler<FileBookingComplaintCommand, Result<Guid>>
{
    private static readonly BookingStatus[] EligibleStatuses =
        [BookingStatus.Confirmed, BookingStatus.Started, BookingStatus.Completed];

    public async Task<Result<Guid>> Handle(FileBookingComplaintCommand request, CancellationToken ct)
    {
        var ctx = (DbContext)db;

        var booking = await ctx.Set<BookingAggregate>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BookingId, ct);
        if (booking is null) return Result.Failure<Guid>(Error.NotFound("Booking"));

        if (booking.CustomerId != request.CustomerId)
            return Result.Failure<Guid>(Error.Forbidden("Bu randevu size ait değil."));

        var existing = await ctx.Set<BookingComplaint>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.BookingId == request.BookingId, ct);
        if (existing is not null)
            return Result.Success(existing.Id); // idempotent no-op — ikinci bildirim tekrarlanmaz

        if (!EligibleStatuses.Contains(booking.Status))
            return Result.Failure<Guid>(Error.Validation(
                $"{booking.Status} durumundaki randevu için bu bildirim yapılamaz."));

        if (DateTimeOffset.UtcNow < booking.ScheduledEndsAt)
            return Result.Failure<Guid>(Error.Validation("Randevu henüz gerçekleşmedi — bitiş saatinden sonra bildirebilirsin."));

        var createResult = BookingComplaint.Create(booking.Id, booking.CustomerId, booking.BarberShopId, request.Note);
        if (createResult.IsFailure) return Result.Failure<Guid>(createResult.Error);

        var complaint = createResult.Value;
        ctx.Set<BookingComplaint>().Add(complaint);

        await audit.RecordAsync(
            "booking.complaint.filed",
            nameof(BookingComplaint),
            complaint.Id.ToString(),
            actorUserId: request.CustomerId,
            payload: new { bookingId = booking.Id, barberShopId = booking.BarberShopId },
            ct: ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrorClassifier.IsUniqueViolation(ex, out var constraint)
                                            && constraint == "ux_booking_complaints_booking")
        {
            // Çift-tıklama/retry yarışı: precheck'i atlatan ikinci istek ux_booking_complaints_booking'e
            // çarptı — kazanan tarafın kaydını okuyup aynı idempotent no-op sonucu döndür.
            var winner = await ctx.Set<BookingComplaint>().AsNoTracking()
                .FirstAsync(c => c.BookingId == request.BookingId, ct);
            return Result.Success(winner.Id);
        }

        return Result.Success(complaint.Id);
    }
}
