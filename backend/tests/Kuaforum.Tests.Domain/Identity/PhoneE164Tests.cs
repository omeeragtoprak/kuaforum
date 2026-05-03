using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Xunit;

namespace Kuaforum.Tests.Domain.Identity;

public class PhoneE164Tests
{
    [Theory]
    [InlineData("+905551234567")]
    [InlineData("+90 555 123 45 67")]
    public void TryCreate_ValidTrMobile_Succeeds(string raw)
    {
        var result = PhoneE164.TryCreate(raw);
        Assert.True(result.IsSuccess);
        Assert.Equal("+905551234567", result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("+1234567890")]
    [InlineData("05551234567")]
    [InlineData("+90 4 555 1234567")]
    [InlineData("+9055512345678")]
    public void TryCreate_Invalid_Fails(string? raw)
    {
        var result = PhoneE164.TryCreate(raw);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Masked_HidesMiddleDigits()
    {
        var phone = PhoneE164.TryCreate("+905551234567").Value;
        var masked = phone.Masked();
        Assert.Contains("•", masked);
        Assert.StartsWith("+90", masked);
        Assert.EndsWith("67", masked);
    }
}
