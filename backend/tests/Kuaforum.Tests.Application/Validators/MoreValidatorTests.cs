using FluentValidation.TestHelper;
using Kuaforum.Application.Modules.Admin;
using Kuaforum.Application.Modules.Barber;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Application.Modules.Identity.DeviceTokens;
using Kuaforum.Application.Modules.Identity.Users;
using Kuaforum.Application.Modules.Notification;
using Kuaforum.Application.Modules.Payment;
using Kuaforum.Application.Modules.Review;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Payment;
using Kuaforum.Domain.Modules.Review;

namespace Kuaforum.Tests.Application.Validators;

public sealed class ApproveReviewCommandValidatorTests
{
    [Fact]
    public void Empty_moderator_fails()
    {
        var r = new ApproveReviewCommandValidator().TestValidate(new ApproveReviewCommand(Guid.Empty, Guid.NewGuid()));
        r.ShouldHaveValidationErrorFor(x => x.ModeratorUserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new ApproveReviewCommandValidator().TestValidate(new ApproveReviewCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class RejectReviewCommandValidatorTests
{
    [Fact]
    public void Invalid_reason_fails()
    {
        var r = new RejectReviewCommandValidator().TestValidate(new RejectReviewCommand(
            Guid.NewGuid(), Guid.NewGuid(), (ReviewRejectionReason)999));
        r.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new RejectReviewCommandValidator().TestValidate(new RejectReviewCommand(
            Guid.NewGuid(), Guid.NewGuid(), ReviewRejectionReason.Profanity));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class MarkNotificationReadCommandValidatorTests
{
    [Fact]
    public void Empty_user_fails()
    {
        var r = new MarkNotificationReadCommandValidator().TestValidate(new MarkNotificationReadCommand(Guid.Empty, Guid.NewGuid()));
        r.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new MarkNotificationReadCommandValidator().TestValidate(new MarkNotificationReadCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class MarkAllNotificationsReadCommandValidatorTests
{
    [Fact]
    public void Empty_user_fails()
    {
        var r = new MarkAllNotificationsReadCommandValidator().TestValidate(new MarkAllNotificationsReadCommand(Guid.Empty));
        r.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new MarkAllNotificationsReadCommandValidator().TestValidate(new MarkAllNotificationsReadCommand(Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class SetWorkingHoursCommandValidatorTests
{
    private readonly SetWorkingHoursCommandValidator _v = new();

    [Fact]
    public void Open_after_close_fails_when_not_closed()
    {
        var r = _v.TestValidate(new SetWorkingHoursCommand(
            Guid.NewGuid(), Guid.NewGuid(), DayOfWeek.Monday,
            new TimeOnly(20, 0), new TimeOnly(10, 0), false));
        r.ShouldHaveValidationErrorFor(x => x.OpenTime);
    }

    [Fact]
    public void Closed_day_with_inverted_times_passes()
    {
        var r = _v.TestValidate(new SetWorkingHoursCommand(
            Guid.NewGuid(), Guid.NewGuid(), DayOfWeek.Sunday,
            new TimeOnly(20, 0), new TimeOnly(10, 0), true));
        r.ShouldNotHaveValidationErrorFor(x => x.OpenTime);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = _v.TestValidate(new SetWorkingHoursCommand(
            Guid.NewGuid(), Guid.NewGuid(), DayOfWeek.Monday,
            new TimeOnly(9, 0), new TimeOnly(20, 0), false));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class AddTimeOffCommandValidatorTests
{
    [Fact]
    public void End_before_start_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new AddTimeOffCommandValidator().TestValidate(new AddTimeOffCommand(
            Guid.NewGuid(), Guid.NewGuid(), now.AddDays(2), now.AddDays(1), null));
        r.ShouldHaveValidationErrorFor(x => x.StartsAt);
    }

    [Fact]
    public void Reason_too_long_fails()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new AddTimeOffCommandValidator().TestValidate(new AddTimeOffCommand(
            Guid.NewGuid(), Guid.NewGuid(), now.AddDays(1), now.AddDays(2), new string('x', 250)));
        r.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Valid_passes()
    {
        var now = DateTimeOffset.UtcNow;
        var r = new AddTimeOffCommandValidator().TestValidate(new AddTimeOffCommand(
            Guid.NewGuid(), Guid.NewGuid(), now.AddDays(1), now.AddDays(2), "Tatil"));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class ToggleStylistWalkInCommandValidatorTests
{
    [Fact]
    public void Empty_owner_fails()
    {
        var r = new ToggleStylistWalkInCommandValidator().TestValidate(new ToggleStylistWalkInCommand(Guid.Empty, Guid.NewGuid(), true));
        r.ShouldHaveValidationErrorFor(x => x.OwnerId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new ToggleStylistWalkInCommandValidator().TestValidate(new ToggleStylistWalkInCommand(Guid.NewGuid(), Guid.NewGuid(), false));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class SetBarberShopAddressCommandValidatorTests
{
    private SetBarberShopAddressCommand Build(double lat = 41.0, double lng = 29.0, string line1 = "Cad. 1", Guid? neighborhoodId = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), neighborhoodId,
            line1, null, null, lat, lng);

    // RFC-007 kapanış: mahalle OPSİYONEL — null geçerli, ama gönderildiyse boş Guid olamaz.
    [Fact]
    public void Null_neighborhood_passes()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(neighborhoodId: null));
        r.ShouldNotHaveValidationErrorFor(x => x.NeighborhoodId);
    }

    [Fact]
    public void Empty_guid_neighborhood_fails()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(neighborhoodId: Guid.Empty));
        r.ShouldHaveValidationErrorFor(x => x.NeighborhoodId);
    }

    [Fact]
    public void Provided_neighborhood_passes_validator()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(neighborhoodId: Guid.NewGuid()));
        r.ShouldNotHaveValidationErrorFor(x => x.NeighborhoodId);
    }

    [Fact]
    public void Outside_turkey_lat_fails()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(lat: 50.0));
        r.ShouldHaveValidationErrorFor(x => x.Latitude);
    }

    [Fact]
    public void Outside_turkey_lng_fails()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(lng: 60.0));
        r.ShouldHaveValidationErrorFor(x => x.Longitude);
    }

    [Fact]
    public void Empty_line1_fails()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(line1: ""));
        r.ShouldHaveValidationErrorFor(x => x.Line1);
    }

    [Fact]
    public void Istanbul_coords_pass()
    {
        var r = new SetBarberShopAddressCommandValidator().TestValidate(Build(lat: 41.015, lng: 28.979, line1: "Beyoğlu"));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class UnsuspendUserCommandValidatorTests
{
    [Fact]
    public void Empty_target_fails()
    {
        var r = new UnsuspendUserCommandValidator().TestValidate(new UnsuspendUserCommand(Guid.NewGuid(), Guid.Empty));
        r.ShouldHaveValidationErrorFor(x => x.TargetUserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new UnsuspendUserCommandValidator().TestValidate(new UnsuspendUserCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class DeleteMyAccountCommandValidatorTests
{
    [Fact]
    public void Empty_user_fails()
    {
        var r = new DeleteMyAccountCommandValidator().TestValidate(new DeleteMyAccountCommand(Guid.Empty));
        r.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new DeleteMyAccountCommandValidator().TestValidate(new DeleteMyAccountCommand(Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class AdminCancelBookingCommandValidatorTests
{
    [Fact]
    public void Empty_reason_fails()
    {
        var r = new AdminCancelBookingCommandValidator().TestValidate(new AdminCancelBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), ""));
        r.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Reason_too_long_fails()
    {
        var r = new AdminCancelBookingCommandValidator().TestValidate(new AdminCancelBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), new string('x', 600)));
        r.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new AdminCancelBookingCommandValidator().TestValidate(new AdminCancelBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Müşteri talep etti, sistem hatası nedeniyle iade gerekiyor."));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class RegisterDeviceTokenCommandValidatorTests
{
    private readonly RegisterDeviceTokenCommandValidator _v = new();

    [Fact]
    public void Empty_token_fails()
    {
        var r = _v.TestValidate(new RegisterDeviceTokenCommand(Guid.NewGuid(), "", DevicePlatform.Ios, null, null));
        r.ShouldHaveValidationErrorFor(x => x.FcmToken);
    }

    [Fact]
    public void Token_too_long_fails()
    {
        var r = _v.TestValidate(new RegisterDeviceTokenCommand(
            Guid.NewGuid(), new string('x', 600), DevicePlatform.Android, null, null));
        r.ShouldHaveValidationErrorFor(x => x.FcmToken);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = _v.TestValidate(new RegisterDeviceTokenCommand(
            Guid.NewGuid(), "fcm-token-abc123", DevicePlatform.Android, "1.0.0", "Pixel 7"));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class InitiatePaymentCommandValidatorTests
{
    [Fact]
    public void Empty_customer_fails()
    {
        var r = new InitiatePaymentCommandValidator().TestValidate(new InitiatePaymentCommand(
            Guid.Empty, Guid.NewGuid(), PaymentKind.FullOnline, null));
        r.ShouldHaveValidationErrorFor(x => x.CustomerId);
    }

    [Fact]
    public void Undefined_kind_fails()
    {
        var r = new InitiatePaymentCommandValidator().TestValidate(new InitiatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), (PaymentKind)99, null));
        r.ShouldHaveValidationErrorFor(x => x.Kind);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new InitiatePaymentCommandValidator().TestValidate(new InitiatePaymentCommand(
            Guid.NewGuid(), Guid.NewGuid(), PaymentKind.Deposit, "idem-key-1"));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class DevLoginCommandValidatorTests
{
    [Fact]
    public void Empty_fingerprint_fails()
    {
        var r = new Kuaforum.Application.Modules.Identity.Auth.DevLoginCommandValidator().TestValidate(
            new Kuaforum.Application.Modules.Identity.Auth.DevLoginCommand(
                Kuaforum.Application.Modules.Identity.Auth.DevLoginRole.Customer, "", null, null));
        r.ShouldHaveValidationErrorFor(x => x.DeviceFingerprint);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new Kuaforum.Application.Modules.Identity.Auth.DevLoginCommandValidator().TestValidate(
            new Kuaforum.Application.Modules.Identity.Auth.DevLoginCommand(
                Kuaforum.Application.Modules.Identity.Auth.DevLoginRole.BarberOwner, "fp-1", "Test Device", "127.0.0.1"));
        r.ShouldNotHaveAnyValidationErrors();
    }
}
