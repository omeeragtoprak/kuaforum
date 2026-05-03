using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.Events;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Xunit;

namespace Kuaforum.Tests.Domain.Identity;

public class UserTests
{
    private static PhoneE164 Phone() => PhoneE164.TryCreate("+905551234567").Value;

    [Fact]
    public void CreateCustomerByPhone_RaisesUserRegisteredEvent()
    {
        var user = User.CreateCustomerByPhone(Phone(), displayName: "Ali").Value;
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("Ali", user.DisplayName);
        Assert.Single(user.DomainEvents);
        Assert.IsType<UserRegisteredEvent>(user.DomainEvents.First());
    }

    [Fact]
    public void CreateCustomerByPhone_DisplayNameTooLong_Fails()
    {
        var longName = new string('A', 81);
        var result = User.CreateCustomerByPhone(Phone(), displayName: longName);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void SoftDelete_ChangesStatusAndRaisesEvent()
    {
        var user = User.CreateCustomerByPhone(Phone()).Value;
        user.ClearDomainEvents();

        var result = user.SoftDelete();
        Assert.True(result.IsSuccess);
        Assert.True(user.IsDeleted);
        Assert.Equal(UserStatus.Deleted, user.Status);
        Assert.Single(user.DomainEvents.Where(e => e is UserAccountDeletedEvent));
    }

    [Fact]
    public void RestoreFromSoftDelete_WithinWindow_Succeeds()
    {
        var user = User.CreateCustomerByPhone(Phone()).Value;
        user.SoftDelete();
        user.ClearDomainEvents();

        var result = user.RestoreFromSoftDelete();
        Assert.True(result.IsSuccess);
        Assert.False(user.IsDeleted);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Single(user.DomainEvents.Where(e => e is UserAccountRestoredEvent));
    }

    [Fact]
    public void Suspend_DeletedUser_Fails()
    {
        var user = User.CreateCustomerByPhone(Phone()).Value;
        user.SoftDelete();

        var result = user.Suspend();
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void MarkLoggedIn_DeletedUser_Fails()
    {
        var user = User.CreateCustomerByPhone(Phone()).Value;
        user.SoftDelete();

        var result = user.MarkLoggedIn("dev-1", "127.0.0.1");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void AnonymizeForHardDelete_RemovesIdentifyingInfo()
    {
        var user = User.CreateCustomerByPhone(Phone(), displayName: "Ali").Value;
        user.AnonymizeForHardDelete();
        Assert.NotEqual("+905551234567", user.PhoneE164);
        Assert.Equal("deleted-user", user.DisplayName);
        Assert.Null(user.Email);
    }
}
