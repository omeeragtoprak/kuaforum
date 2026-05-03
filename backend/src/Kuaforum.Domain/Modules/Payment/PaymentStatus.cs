namespace Kuaforum.Domain.Modules.Payment;

public enum PaymentStatus
{
    Initiated = 0,
    Authorized = 1,
    Captured = 2,
    Settled = 3,
    Failed = 4,
    Cancelled = 5,
    Refunded = 6,
    PartiallyRefunded = 7,
}

public enum RefundReason
{
    CustomerRequested = 0,
    BarberRequested = 1,
    NoShowRefund = 2,
    AdminOverride = 3,
    PaymentFailed = 4,
}

public enum PaymentProviderName
{
    PayTr = 0,
    Iyzico = 1,
    Mock = 99,
}
