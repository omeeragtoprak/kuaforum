using Kuaforum.Domain.Modules.Identity;
using Xunit;

namespace Kuaforum.Tests.Domain.Identity;

public class OtpCodeTests
{
    [Fact]
    public void AttemptVerify_CorrectCode_Consumes()
    {
        var otp = OtpCode.Issue("+905551234567", "hash-A", OtpPurpose.Login);
        var result = otp.AttemptVerify("hash-A");
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(OtpCodeStatus.Consumed, otp.Status);
        Assert.NotNull(otp.UsedAt);
    }

    [Fact]
    public void AttemptVerify_WrongCode_DecrementsAttempts()
    {
        var otp = OtpCode.Issue("+905551234567", "hash-A", OtpPurpose.Login);
        var initialAttempts = otp.AttemptsLeft;

        var result = otp.AttemptVerify("hash-WRONG");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal(initialAttempts - 1, otp.AttemptsLeft);
        Assert.Equal(OtpCodeStatus.Pending, otp.Status);
    }

    [Fact]
    public void AttemptVerify_AllAttemptsFail_Expires()
    {
        var otp = OtpCode.Issue("+905551234567", "hash-A", OtpPurpose.Login, attemptsAllowed: 3);
        otp.AttemptVerify("wrong-1");
        otp.AttemptVerify("wrong-2");
        otp.AttemptVerify("wrong-3");

        Assert.Equal(OtpCodeStatus.Expired, otp.Status);
        Assert.Equal(0, otp.AttemptsLeft);

        var afterExpire = otp.AttemptVerify("hash-A");
        Assert.True(afterExpire.IsFailure);
    }

    [Fact]
    public void AttemptVerify_PastExpiry_Fails()
    {
        var otp = OtpCode.Issue("+905551234567", "hash-A", OtpPurpose.Login, lifetime: TimeSpan.FromMilliseconds(1));
        Thread.Sleep(20);

        var result = otp.AttemptVerify("hash-A");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void IsActive_ReflectsStatusAndExpiry()
    {
        var otp = OtpCode.Issue("+905551234567", "hash", OtpPurpose.Register);
        Assert.True(otp.IsActive);

        otp.AttemptVerify("hash");
        Assert.False(otp.IsActive);
    }
}
