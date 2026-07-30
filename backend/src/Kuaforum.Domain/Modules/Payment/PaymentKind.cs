namespace Kuaforum.Domain.Modules.Payment;

/// <summary>RFC-006: FullOnline = tamamı online; Deposit = %DepositRate kapora online, kalan salonda.</summary>
public enum PaymentKind
{
    FullOnline = 0,
    Deposit = 1,
}
