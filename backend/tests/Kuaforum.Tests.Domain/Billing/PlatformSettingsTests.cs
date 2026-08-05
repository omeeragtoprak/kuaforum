using Kuaforum.Domain.Modules.Billing;
using Xunit;

namespace Kuaforum.Tests.Domain.Billing;

public class PlatformSettingsTests
{
    [Fact]
    public void CreateDefault_UsesGivenRate()
    {
        var settings = PlatformSettings.CreateDefault(0.20m);
        Assert.Equal(0.20m, settings.CommissionRateGlobal);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    public void UpdateCommissionRateGlobal_ValidRange_Succeeds(double rate)
    {
        var settings = PlatformSettings.CreateDefault();
        var result = settings.UpdateCommissionRateGlobal((decimal)rate);

        Assert.True(result.IsSuccess);
        Assert.Equal((decimal)rate, settings.CommissionRateGlobal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(0.51)]
    public void UpdateCommissionRateGlobal_OutOfRange_Fails(double rate)
    {
        var settings = PlatformSettings.CreateDefault();
        var result = settings.UpdateCommissionRateGlobal((decimal)rate);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void UpdateBankingInfo_TrimsAndSetsFields()
    {
        var settings = PlatformSettings.CreateDefault();
        var result = settings.UpdateBankingInfo(" TR330006100519786457841326 ", " Ziraat Bankası ", " Kuaforum A.Ş. ");

        Assert.True(result.IsSuccess);
        Assert.Equal("TR330006100519786457841326", settings.CommissionPaymentIban);
        Assert.Equal("Ziraat Bankası", settings.CommissionPaymentBankName);
        Assert.Equal("Kuaforum A.Ş.", settings.CommissionPaymentAccountHolderName);
    }

    [Fact]
    public void UpdateBankingInfo_BlankStrings_ClearsFields()
    {
        var settings = PlatformSettings.CreateDefault();
        settings.UpdateBankingInfo("TR330006100519786457841326", "Ziraat", "Kuaforum");

        var result = settings.UpdateBankingInfo("  ", null, "");
        Assert.True(result.IsSuccess);
        Assert.Null(settings.CommissionPaymentIban);
        Assert.Null(settings.CommissionPaymentBankName);
        Assert.Null(settings.CommissionPaymentAccountHolderName);
    }

    [Fact]
    public void UpdateBankingInfo_IbanTooLong_Fails()
    {
        var settings = PlatformSettings.CreateDefault();
        var result = settings.UpdateBankingInfo(new string('A', 35), null, null);
        Assert.True(result.IsFailure);
    }
}
