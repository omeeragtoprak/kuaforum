using Kuaforum.Domain.Common;
using Xunit;

namespace Kuaforum.Tests.Application;

public class PaymentAmountsTests
{
    // xUnit InlineData tarayıcısı decimal literal kabul etmez (compile-time constant değil) —
    // rate double alınıp gövdede (decimal) cast edilir.
    [Theory]
    [InlineData(100_00, 0.15, 15_00)]   // düz
    [InlineData(333, 0.15, 50)]         // 49.95 → ceil 50
    [InlineData(1, 0.15, 1)]            // min 1 kuruş
    [InlineData(100_00, 0.10, 10_00)]
    public void Deposit_is_ceiled(long total, double rate, long expected)
        => Assert.Equal(expected, PaymentAmounts.CalculateDepositMinor(total, (decimal)rate));

    [Theory]
    [InlineData(345, 0.10, 35)]         // 34.5 → AwayFromZero 35 (ToEven olsaydı 34 dönerdi)
    [InlineData(100_00, 0.10, 10_00)]   // düz
    public void Commission_rounds_away_from_zero(long total, double rate, long expected)
        => Assert.Equal(expected, PaymentAmounts.CalculateCommissionMinor(total, (decimal)rate));

    [Theory]
    [InlineData(0.15, 0.10)]  // RFC-006 default kapora/komisyon
    [InlineData(0.10, 0.10)]  // validator'ın izin verdiği eşit-oran sınır durumu (DepositRate >= CommissionRate)
    public void Deposit_never_below_commission_when_rate_gte_commission_rate(double depositRate, double commissionRate)
    {
        for (long total = 1; total <= 5_000; total++)
        {
            var deposit = PaymentAmounts.CalculateDepositMinor(total, (decimal)depositRate);
            var commission = PaymentAmounts.CalculateCommissionMinor(total, (decimal)commissionRate);
            Assert.True(deposit >= commission, $"total={total}: deposit {deposit} < commission {commission}");
        }
    }
}
