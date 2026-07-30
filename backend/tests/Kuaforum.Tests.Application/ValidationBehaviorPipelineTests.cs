using FluentValidation;
using Kuaforum.Application.Common.Behaviors;
using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Kuaforum.Tests.Application;

/// <summary>
/// T10: ValidationBehavior'ı FluentValidation TestValidate ile değil, gerçek MediatR pipeline'ı
/// üzerinden test eder. Bug (satır ~36 eski kod): `(TResponse)(object)Result.Failure(error)` —
/// Result.Failure(error) HER ZAMAN base `Result` döner; response tipi `Result&lt;T&gt;` (ICommand&lt;T&gt;)
/// olan bir command validation'da başarısız olduğunda bu cast InvalidCastException fırlatıyordu
/// (runtime tipi Result&lt;T&gt; değil, sadece Result). Var olan validator testleri hep TestValidate
/// üzerinden koştuğu için pipeline hiç egzersiz edilmemiş, bug gizli kalmıştı.
/// Bu test dosyası DependencyInjection.cs'deki AddApplication() kayıt şeklini (AddMediatR +
/// AddOpenBehavior(ValidationBehavior&lt;,&gt;) + AddValidatorsFromAssembly) birebir kullanır.
/// </summary>
public sealed class ValidationBehaviorPipelineTests
{
    // ICommand → IRequest<Result> (non-generic response) case.
    public sealed record PlainCommand(string Name) : ICommand;

    public sealed class PlainCommandValidator : AbstractValidator<PlainCommand>
    {
        public PlainCommandValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class PlainCommandHandler : IRequestHandler<PlainCommand, Result>
    {
        public Task<Result> Handle(PlainCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    // ICommand<Guid> → IRequest<Result<Guid>> (generic response) case — bug'lı yol tam burada.
    public sealed record ValuedCommand(string Name) : ICommand<Guid>;

    public sealed class ValuedCommandValidator : AbstractValidator<ValuedCommand>
    {
        public ValuedCommandValidator() => RuleFor(x => x.Name).NotEmpty();
    }

    public sealed class ValuedCommandHandler : IRequestHandler<ValuedCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(ValuedCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(Guid.NewGuid()));
    }

    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        var assembly = typeof(ValidationBehaviorPipelineTests).Assembly;

        // Kuaforum.Application/DependencyInjection.cs::AddApplication ile birebir aynı kayıt şekli.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Result_response_command_returns_failure_on_invalid_input_not_exception()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new PlainCommand(string.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Result_response_command_succeeds_on_valid_input()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new PlainCommand("ok"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Generic_result_response_command_returns_failure_on_invalid_input_not_InvalidCastException()
    {
        var mediator = BuildMediator();

        // Fix öncesi burada InvalidCastException fırlıyordu (Result.Failure(error) → base Result,
        // (TResponse)(object) cast'i Result<Guid>'e başarısız oluyordu).
        var result = await mediator.Send(new ValuedCommand(string.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Generic_result_response_command_succeeds_on_valid_input()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new ValuedCommand("ok"));

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);
    }
}
