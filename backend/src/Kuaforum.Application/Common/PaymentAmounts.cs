namespace Kuaforum.Application.Common;

/// <summary>
/// RFC-006 tutar hesapları. Deposit (kapora) her zaman YUKARI (ceiling) yuvarlanır — bu,
/// PaymentOptions.DepositRate >= PaymentOptions.CommissionRate invariantıyla birleşince
/// kapora tutarının platform komisyonunu hiçbir zaman altına düşmemesini garanti eder
/// (bkz. PaymentOptions, DependencyInjection.AddInfrastructure startup validation).
/// </summary>
public static class PaymentAmounts
{
    public static long CalculateDepositMinor(long totalMinor, decimal depositRate)
        => (long)Math.Ceiling(totalMinor * depositRate);

    public static long CalculateCommissionMinor(long totalMinor, decimal commissionRate)
        => (long)Math.Round(totalMinor * commissionRate, MidpointRounding.AwayFromZero);
}
