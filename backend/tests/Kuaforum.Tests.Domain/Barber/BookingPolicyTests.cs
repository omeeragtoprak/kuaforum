using Kuaforum.Domain.Modules.Barber;
using Xunit;

namespace Kuaforum.Tests.Domain.Barber;

public class BookingPolicyTests
{
    private const decimal DepositMin = 0.05m;
    private const decimal DepositMax = 0.50m;

    [Fact]
    public void CreateDefault_HasDepositDisabledAndTwoMaxPending()
    {
        var policy = BookingPolicy.CreateDefault();

        Assert.False(policy.DepositEnabled);
        Assert.Null(policy.DepositPercent);
        Assert.Equal(2, policy.MaxPendingPerCustomer);
        Assert.Null(policy.MinDaysBetweenBookings);
        Assert.Null(policy.MaxBookingsPerMonth);
        Assert.False(policy.AutoConfirmEnabled);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.20)]
    [InlineData(0.50)]
    public void Create_DepositEnabled_PercentInAdminRange_Succeeds(double percent)
    {
        var result = BookingPolicy.Create(
            depositEnabled: true, depositPercent: (decimal)percent,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.Equal((decimal)percent, result.Value.DepositPercent);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.51)]
    public void Create_DepositEnabled_PercentOutOfAdminRange_Fails(double percent)
    {
        var result = BookingPolicy.Create(
            depositEnabled: true, depositPercent: (decimal)percent,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_DepositEnabled_NullPercent_Fails()
    {
        var result = BookingPolicy.Create(
            depositEnabled: true, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_DepositDisabled_PercentInputIsNormalizedToNull()
    {
        // Kapora kapalıyken gönderilen bir yüzde değeri (frontend'in eski state'i, vs.)
        // sessizce yok sayılır — domain "anlamsız veri sakla" yapmaz.
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: 0.30m,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DepositEnabled);
        Assert.Null(result.Value.DepositPercent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Create_MaxPendingPerCustomer_InRange_Succeeds(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: value, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.MaxPendingPerCustomer);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(100)]
    public void Create_MaxPendingPerCustomer_OutOfPlatformCeiling_Fails(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: value, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_MinDaysBetweenBookings_Null_MeansDisabled_Succeeds()
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.MinDaysBetweenBookings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Create_MinDaysBetweenBookings_OutOfRange_Fails(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: value, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    public void Create_MinDaysBetweenBookings_BoundaryValues_Succeed(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: value, maxBookingsPerMonth: null,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.MinDaysBetweenBookings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Create_MaxBookingsPerMonth_OutOfRange_Fails(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: value,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Create_MaxBookingsPerMonth_BoundaryValues_Succeed(int value)
    {
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: value,
            autoConfirmEnabled: false,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.MaxBookingsPerMonth);
    }

    // ── RFC-014 §2 D2: AutoConfirmEnabled ─────────────────────────────────

    [Fact]
    public void Create_AutoConfirmEnabledTrue_NoAdminRangeRestriction_Succeeds()
    {
        // Kapora'nın aksine bir admin-ayarlı aralığa tabi değil — owner'ın kendi opt-in'i.
        var result = BookingPolicy.Create(
            depositEnabled: false, depositPercent: null,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: true,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AutoConfirmEnabled);
    }

    [Fact]
    public void Create_AutoConfirmEnabled_IndependentOfDepositEnabled_BothCanBeTrueTogether()
    {
        // RFC-014 madde 3'ün ön koşulu: bir salon hem kapora hem auto-confirm'i AYNI ANDA
        // açabilmeli — domain seviyesinde ikisi arasında bir kısıt yok (etkileşim Booking
        // aggregate'inde uygulanır, burada değil).
        var result = BookingPolicy.Create(
            depositEnabled: true, depositPercent: 0.15m,
            maxPendingPerCustomer: 2, minDaysBetweenBookings: null, maxBookingsPerMonth: null,
            autoConfirmEnabled: true,
            depositPercentMin: DepositMin, depositPercentMax: DepositMax);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.DepositEnabled);
        Assert.True(result.Value.AutoConfirmEnabled);
    }
}
