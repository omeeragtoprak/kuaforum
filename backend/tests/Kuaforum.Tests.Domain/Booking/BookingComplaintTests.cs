using Kuaforum.Domain.Modules.Booking;
using Kuaforum.Domain.Modules.Booking.Events;
using Xunit;

namespace Kuaforum.Tests.Domain.Booking;

/// <summary>RFC-015 §2 D3 / docs/GAPS.md G4.</summary>
public class BookingComplaintTests
{
    [Fact]
    public void Create_HappyPath_RaisesEventAndDefaultsToOpen()
    {
        var bookingId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        var result = BookingComplaint.Create(bookingId, customerId, shopId, "Kuaför gelmedi, salon kapalıydı.");

        Assert.True(result.IsSuccess);
        var complaint = result.Value;
        Assert.Equal(bookingId, complaint.BookingId);
        Assert.Equal(customerId, complaint.CustomerId);
        Assert.Equal(shopId, complaint.BarberShopId);
        Assert.Equal(BookingComplaintStatus.Open, complaint.Status);
        Assert.Equal("Kuaför gelmedi, salon kapalıydı.", complaint.Note);
        Assert.Contains(complaint.DomainEvents, e => e is BookingComplaintFiledEvent);
        var raised = Assert.IsType<BookingComplaintFiledEvent>(complaint.DomainEvents.Single(e => e is BookingComplaintFiledEvent));
        Assert.Equal(complaint.Id, raised.ComplaintId);
        Assert.Equal(bookingId, raised.BookingId);
        Assert.Equal(customerId, raised.CustomerId);
        Assert.Equal(shopId, raised.BarberShopId);
    }

    [Fact]
    public void Create_WithNullNote_Succeeds()
    {
        var result = BookingComplaint.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Note);
    }

    [Fact]
    public void Create_WithWhitespaceNote_NormalizesToNull()
    {
        var result = BookingComplaint.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "   ");
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Note);
    }

    [Fact]
    public void Create_NoteTooLong_Fails()
    {
        var result = BookingComplaint.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 1001));
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Create_NoteAtMaxLength_Succeeds()
    {
        var result = BookingComplaint.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('a', 1000));
        Assert.True(result.IsSuccess);
    }
}
