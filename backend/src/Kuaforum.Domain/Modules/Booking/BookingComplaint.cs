using Kuaforum.Domain.Common;
using Kuaforum.Domain.Modules.Booking.Events;

namespace Kuaforum.Domain.Modules.Booking;

public enum BookingComplaintStatus
{
    Open = 0,
    Resolved = 1,
}

/// <summary>
/// RFC-015 §2 D3 / docs/GAPS.md G4: müşterinin "kuaför gelmedi / salon kapalıydı" bildirimi.
/// Bilinçli olarak MİNİMUM: tek durum makinesi yok (Status alanı ileriye dönük — bu paket
/// yalnız <see cref="Create"/> ekliyor, bir "Resolve" metodu YOK; RFC'nin kendi kapsamı da
/// çözümü mevcut admin-iptal/iade akışına bırakıyor, yeni bir "ticket kapatma" akışı istemiyor).
/// Otomatik iade TETİKLEMEZ — yalnız admin kuyruğuna düşen bir kayıt + kuaföre nötr bildirim.
/// </summary>
public sealed class BookingComplaint : Entity
{
    private BookingComplaint() { }

    public Guid BookingId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid BarberShopId { get; private set; }
    public string? Note { get; private set; }
    public BookingComplaintStatus Status { get; private set; }
    public DateTimeOffset ReportedAt { get; private set; }

    public static Result<BookingComplaint> Create(Guid bookingId, Guid customerId, Guid barberShopId, string? note)
    {
        if (note is { Length: > 1000 })
            return Result.Failure<BookingComplaint>(Error.Validation("Açıklama en fazla 1000 karakter olabilir."));

        var complaint = new BookingComplaint
        {
            BookingId = bookingId,
            CustomerId = customerId,
            BarberShopId = barberShopId,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = BookingComplaintStatus.Open,
            ReportedAt = DateTimeOffset.UtcNow,
        };

        complaint.RaiseDomainEvent(new BookingComplaintFiledEvent(complaint.Id, bookingId, customerId, barberShopId));
        return complaint;
    }
}
