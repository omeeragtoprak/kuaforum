using Kuaforum.Domain.Modules.Barber;
using Xunit;

namespace Kuaforum.Tests.Domain.Barber;

public class StylistTests
{
    private static Stylist Create(string name = "Ali Berber") =>
        Stylist.Create(Guid.NewGuid(), name).Value;

    [Fact]
    public void Create_HappyPath_DefaultsActiveAndNoWalkIn()
    {
        var s = Create();
        Assert.True(s.IsActive);
        Assert.False(s.WalkInAvailable);
        Assert.Null(s.WalkInToggledAt);
        Assert.Equal("Ali Berber", s.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_Fails(string name)
    {
        var r = Stylist.Create(Guid.NewGuid(), name);
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var r = Stylist.Create(Guid.NewGuid(), new string('a', 81));
        Assert.True(r.IsFailure);
    }

    [Fact]
    public void Create_TrimsName()
    {
        var s = Stylist.Create(Guid.NewGuid(), "  Ali  ").Value;
        Assert.Equal("Ali", s.DisplayName);
    }

    [Fact]
    public void UpdateProfile_NullFields_DoesNotOverwrite()
    {
        var s = Create("Original");
        s.UpdateProfile(null, null, null);
        Assert.Equal("Original", s.DisplayName);
    }

    [Fact]
    public void UpdateProfile_NewName_Updates()
    {
        var s = Create("Original");
        var r = s.UpdateProfile("Yeni İsim", null, null);
        Assert.True(r.IsSuccess);
        Assert.Equal("Yeni İsim", s.DisplayName);
    }

    [Fact]
    public void UpdateProfile_BlankName_Fails()
    {
        var s = Create("Original");
        var r = s.UpdateProfile("   ", null, null);
        Assert.True(r.IsFailure);
        Assert.Equal("Original", s.DisplayName); // unchanged
    }

    [Fact]
    public void UpdateProfile_TruncatesPhotoUrlAndBio()
    {
        var s = Create();
        s.UpdateProfile(null, new string('x', 600), new string('y', 1200));
        Assert.Equal(500, s.PhotoUrl!.Length);
        Assert.Equal(1000, s.Bio!.Length);
    }

    [Fact]
    public void ToggleWalkIn_OnActive_TogglesAndStampsTime()
    {
        var s = Create();
        var r = s.ToggleWalkIn(true);
        Assert.True(r.IsSuccess);
        Assert.True(s.WalkInAvailable);
        Assert.NotNull(s.WalkInToggledAt);
    }

    [Fact]
    public void ToggleWalkIn_OnInactiveToTrue_Fails()
    {
        var s = Create();
        s.SetActive(false);
        var r = s.ToggleWalkIn(true);
        Assert.True(r.IsFailure);
        Assert.False(s.WalkInAvailable);
    }

    [Fact]
    public void SetActive_False_AlsoClearsWalkIn()
    {
        var s = Create();
        s.ToggleWalkIn(true);
        Assert.True(s.WalkInAvailable);

        s.SetActive(false);
        Assert.False(s.IsActive);
        Assert.False(s.WalkInAvailable);
    }

    [Fact]
    public void ShouldExpireWalkIn_ReturnsTrue_WhenOlderThanMaxAge()
    {
        var s = Create();
        s.ToggleWalkIn(true);
        // Direct field manipulation imkansız (private set). Sentinel: very small TimeSpan.
        // ToggledAt = UtcNow ~ç anki şu an, maxAge 0ms ile her zaman expired sayılacak.
        Assert.True(s.ShouldExpireWalkIn(TimeSpan.Zero));
    }

    [Fact]
    public void ShouldExpireWalkIn_ReturnsFalse_WhenNotWalkIn()
    {
        var s = Create();
        Assert.False(s.ShouldExpireWalkIn(TimeSpan.FromHours(2)));
    }
}
