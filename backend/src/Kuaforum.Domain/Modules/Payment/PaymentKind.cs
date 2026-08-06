namespace Kuaforum.Domain.Modules.Payment;

/// <summary>RFC-006/RFC-013 C4: FullOnline = tamamı online (yine de owner onayı zorunlu); Deposit = shop.BookingPolicy.DepositPercent oranında kapora online, kalan salonda.</summary>
public enum PaymentKind
{
    FullOnline = 0,
    Deposit = 1,
}
