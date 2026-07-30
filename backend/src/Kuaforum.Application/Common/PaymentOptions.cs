namespace Kuaforum.Application.Common;

/// <summary>
/// RFC-006 kapora planı: platform komisyon oranı + kapora oranı, config-driven.
/// Invariant: DepositRate >= CommissionRate (startup'ta doğrulanır — bkz.
/// Kuaforum.Infrastructure.DependencyInjection.AddInfrastructure) — kapora, platform
/// komisyonunu her zaman karşılamalı.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>Platform komisyonu (toplam hizmet bedeli üzerinden). RFC-006.</summary>
    public decimal CommissionRate { get; set; } = 0.10m;

    /// <summary>Kapora oranı. Invariant: >= CommissionRate (startup'ta doğrulanır).</summary>
    public decimal DepositRate { get; set; } = 0.15m;
}
