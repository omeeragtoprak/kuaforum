using Kuaforum.Application.Common;
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

    [Fact]
    public void Deposit_never_below_commission_when_rate_gte_commission_rate()
    {
        for (long total = 1; total <= 5_000; total++)
        {
            var deposit = PaymentAmounts.CalculateDepositMinor(total, 0.15m);
            var commission = PaymentAmounts.CalculateCommissionMinor(total, 0.10m);
            Assert.True(deposit >= commission, $"total={total}: deposit {deposit} < commission {commission}");
        }
    }
}
