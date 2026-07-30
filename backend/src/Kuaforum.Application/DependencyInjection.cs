using FluentValidation;
using Kuaforum.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Kuaforum.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        // Capture → booking Confirm ortak bloğu — CompletePaymentCommand + PayTR webhook (RFC-006 T4).
        services.AddScoped<Modules.Payment.PaymentCompletionService>();

        return services;
    }
}
