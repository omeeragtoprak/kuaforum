using FluentValidation.TestHelper;
using Kuaforum.Application.Modules.Booking;
using Kuaforum.Domain.Modules.Booking;

namespace Kuaforum.Tests.Application.Validators;

public sealed class CancelBookingCommandValidatorTests
{
    private readonly CancelBookingCommandValidator _v = new();

    [Fact]
    public void Empty_actor_user_id_fails()
    {
        var r = _v.TestValidate(new CancelBookingCommand(Guid.Empty, Guid.NewGuid(), BookingCancellationReason.CustomerRequested));
        r.ShouldHaveValidationErrorFor(x => x.ActorUserId);
    }

    [Fact]
    public void Empty_booking_id_fails()
    {
        var r = _v.TestValidate(new CancelBookingCommand(Guid.NewGuid(), Guid.Empty, BookingCancellationReason.CustomerRequested));
        r.ShouldHaveValidationErrorFor(x => x.BookingId);
    }

    [Fact]
    public void Invalid_reason_fails()
    {
        var r = _v.TestValidate(new CancelBookingCommand(Guid.NewGuid(), Guid.NewGuid(), (BookingCancellationReason)999));
        r.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = _v.TestValidate(new CancelBookingCommand(Guid.NewGuid(), Guid.NewGuid(), BookingCancellationReason.CustomerRequested));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class OwnerConfirmBookingCommandValidatorTests
{
    private readonly OwnerConfirmBookingCommandValidator _v = new();

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Empty_actor_fails(string actor)
    {
        var r = _v.TestValidate(new OwnerConfirmBookingCommand(Guid.Parse(actor), Guid.NewGuid()));
        r.ShouldHaveValidationErrorFor(x => x.ActorUserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = _v.TestValidate(new OwnerConfirmBookingCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class StartBookingCommandValidatorTests
{
    [Fact]
    public void Empty_booking_id_fails()
    {
        var r = new StartBookingCommandValidator().TestValidate(new StartBookingCommand(Guid.NewGuid(), Guid.Empty));
        r.ShouldHaveValidationErrorFor(x => x.BookingId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new StartBookingCommandValidator().TestValidate(new StartBookingCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class CompleteBookingCommandValidatorTests
{
    [Fact]
    public void Empty_actor_fails()
    {
        var r = new CompleteBookingCommandValidator().TestValidate(new CompleteBookingCommand(Guid.Empty, Guid.NewGuid()));
        r.ShouldHaveValidationErrorFor(x => x.ActorUserId);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = new CompleteBookingCommandValidator().TestValidate(new CompleteBookingCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class MarkNoShowCommandValidatorTests
{
    [Fact]
    public void Valid_passes()
    {
        var r = new MarkNoShowCommandValidator().TestValidate(new MarkNoShowCommand(Guid.NewGuid(), Guid.NewGuid()));
        r.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_actor_fails()
    {
        var r = new MarkNoShowCommandValidator().TestValidate(new MarkNoShowCommand(Guid.Empty, Guid.NewGuid()));
        r.ShouldHaveValidationErrorFor(x => x.ActorUserId);
    }
}

public sealed class RescheduleBookingCommandValidatorTests
{
    private readonly RescheduleBookingCommandValidator _v = new();

    [Fact]
    public void Past_time_fails()
    {
        var r = _v.TestValidate(new RescheduleBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1)));
        r.ShouldHaveValidationErrorFor(x => x.NewScheduledAt);
    }

    [Fact]
    public void Within_5min_window_fails()
    {
        var r = _v.TestValidate(new RescheduleBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2)));
        r.ShouldHaveValidationErrorFor(x => x.NewScheduledAt);
    }

    [Fact]
    public void Far_future_passes()
    {
        var r = _v.TestValidate(new RescheduleBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1)));
        r.ShouldNotHaveAnyValidationErrors();
    }
}

public sealed class InitiateBookingCommandValidatorTests
{
    private readonly InitiateBookingCommandValidator _v = new();

    [Fact]
    public void Empty_services_fails()
    {
        var r = _v.TestValidate(new InitiateBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1),
            Array.Empty<Guid>(), false, null));
        r.ShouldHaveValidationErrorFor(x => x.ServiceCatalogIds);
    }

    [Fact]
    public void Valid_passes()
    {
        var r = _v.TestValidate(new InitiateBookingCommand(
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(1),
            new[] { Guid.NewGuid() }, false, null));
        r.ShouldNotHaveAnyValidationErrors();
    }
}
