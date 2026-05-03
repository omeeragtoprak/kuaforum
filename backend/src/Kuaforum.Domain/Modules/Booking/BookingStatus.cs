namespace Kuaforum.Domain.Modules.Booking;

public enum BookingStatus
{
    Pending = 0,
    Confirmed = 1,
    Started = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5,
}

public enum BookingCancellationReason
{
    CustomerRequested = 0,
    BarberRequested = 1,
    PaymentFailed = 2,
    PendingTimeout = 3,
    AdminOverride = 4,
    NoShow = 5,
}

public enum BookingType
{
    Scheduled = 0,
    WalkIn = 1,
}
