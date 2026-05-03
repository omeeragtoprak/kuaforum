using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Payment;
using Microsoft.Extensions.Configuration;

namespace Kuaforum.Infrastructure.Payments;

public sealed class PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers, IConfiguration config) : IPaymentProviderRegistry
{
    private readonly Dictionary<PaymentProviderName, IPaymentProvider> _byName =
        providers.ToDictionary(p => p.ProviderName);

    public PaymentProviderName DefaultProvider
    {
        get
        {
            var name = config.GetValue<string>("Payment:DefaultProvider")?.ToLowerInvariant() ?? "mock";
            return name switch
            {
                "paytr" => PaymentProviderName.PayTr,
                "iyzico" => PaymentProviderName.Iyzico,
                _ => PaymentProviderName.Mock,
            };
        }
    }

    public IPaymentProvider GetProvider(PaymentProviderName name)
    {
        if (_byName.TryGetValue(name, out var p)) return p;
        throw new InvalidOperationException($"Payment provider '{name}' is not registered.");
    }
}
