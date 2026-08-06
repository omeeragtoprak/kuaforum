using Kuaforum.Domain.Modules.Billing;
using Xunit;

namespace Kuaforum.Tests.Domain.Billing;

public class ShopSubscriptionTests
{
    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);

    // ── Create ──────────────────────────────────────────────────────────

    [Fact]
    public void Create_HappyPath_IsActiveWithGivenPlanAndPeriod()
    {
        var shopId = Guid.NewGuid();
        var result = ShopSubscription.Create(shopId, SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd);

        Assert.True(result.IsSuccess);
        var sub = result.Value;
        Assert.Equal(shopId, sub.BarberShopId);
        Assert.Equal(SubscriptionPlanCodes.LaunchFree, sub.PlanCode);
        Assert.Equal(ShopSubscriptionStatus.Active, sub.Status);
        Assert.Equal(PeriodStart, sub.CurrentPeriodStart);
        Assert.Equal(PeriodEnd, sub.CurrentPeriodEnd);
    }

    [Fact]
    public void Create_NormalizesPlanCodeToUpperInvariant()
    {
        var result = ShopSubscription.Create(Guid.NewGuid(), "launch_free", PeriodStart, PeriodEnd);
        Assert.True(result.IsSuccess);
        Assert.Equal("LAUNCH_FREE", result.Value.PlanCode);
    }

    [Fact]
    public void Create_EmptyBarberShopId_Fails()
    {
        var result = ShopSubscription.Create(Guid.Empty, SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Create_PeriodEndBeforeOrEqualStart_Fails()
    {
        var result = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodEnd, PeriodStart);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void CreateLaunchFreeDefault_UsesLaunchFreePlan_AndFarFuturePeriodEnd()
    {
        var shopId = Guid.NewGuid();
        var sub = ShopSubscription.CreateLaunchFreeDefault(shopId);

        Assert.Equal(shopId, sub.BarberShopId);
        Assert.Equal(SubscriptionPlanCodes.LaunchFree, sub.PlanCode);
        Assert.Equal(ShopSubscriptionStatus.Active, sub.Status);
        Assert.True(sub.CurrentPeriodEnd > DateTimeOffset.UtcNow.AddYears(50));
    }

    // ── ChangePlan ──────────────────────────────────────────────────────

    [Fact]
    public void ChangePlan_ActiveSubscription_UpdatesPlanCode()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var result = sub.ChangePlan(SubscriptionPlanCodes.Starter);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionPlanCodes.Starter, sub.PlanCode);
    }

    [Fact]
    public void ChangePlan_CancelledSubscription_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        sub.Cancel();

        var result = sub.ChangePlan(SubscriptionPlanCodes.Pro);
        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionPlanCodes.LaunchFree, sub.PlanCode); // değişmedi
    }

    // ── AdvancePeriod ───────────────────────────────────────────────────

    [Fact]
    public void AdvancePeriod_ValidNextPeriod_Succeeds()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var nextStart = PeriodEnd;
        var nextEnd = PeriodEnd.AddMonths(1);

        var result = sub.AdvancePeriod(nextStart, nextEnd);

        Assert.True(result.IsSuccess);
        Assert.Equal(nextStart, sub.CurrentPeriodStart);
        Assert.Equal(nextEnd, sub.CurrentPeriodEnd);
    }

    [Fact]
    public void AdvancePeriod_GoingBackwards_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var result = sub.AdvancePeriod(PeriodStart.AddDays(-1), PeriodEnd);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AdvancePeriod_CancelledSubscription_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        sub.Cancel();
        var result = sub.AdvancePeriod(PeriodEnd, PeriodEnd.AddMonths(1));
        Assert.True(result.IsFailure);
    }

    // ── Status transitions ──────────────────────────────────────────────

    [Fact]
    public void MarkPastDue_FromActive_Succeeds()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var result = sub.MarkPastDue();

        Assert.True(result.IsSuccess);
        Assert.Equal(ShopSubscriptionStatus.PastDue, sub.Status);
    }

    [Fact]
    public void MarkPastDue_AlreadyPastDue_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        sub.MarkPastDue();
        var result = sub.MarkPastDue();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Reactivate_FromPastDue_Succeeds()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        sub.MarkPastDue();
        var result = sub.Reactivate();

        Assert.True(result.IsSuccess);
        Assert.Equal(ShopSubscriptionStatus.Active, sub.Status);
    }

    [Fact]
    public void Reactivate_FromActive_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var result = sub.Reactivate();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Cancel_FromActive_Succeeds()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        var result = sub.Cancel();

        Assert.True(result.IsSuccess);
        Assert.Equal(ShopSubscriptionStatus.Cancelled, sub.Status);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_Fails()
    {
        var sub = ShopSubscription.Create(Guid.NewGuid(), SubscriptionPlanCodes.LaunchFree, PeriodStart, PeriodEnd).Value;
        sub.Cancel();
        var result = sub.Cancel();
        Assert.True(result.IsFailure);
    }
}
