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

    /// <summary>
    /// RFC-009 ücretsiz büyüme modu: online ödeme (PSP checkout) feature-flag'i. Fail-closed
    /// default <c>false</c> — PayTR merchant hesabı bağlanana kadar randevu salonda ödenir,
    /// komisyon tahsil edilmez. <c>appsettings.Development.json</c> bunu <c>true</c>'ya override
    /// eder ki RFC-006 kapora/webhook integration test suite'i regresyonsuz çalışsın.
    /// <see cref="InitiatePaymentCommand"/> handler'ı bu false iken 409 döner; RFC-006'nın
    /// kapora+komisyon+webhook mimarisi koddan sökülmüyor, yalnızca aktivasyonu bu flag'e bağlı.
    /// </summary>
    public bool OnlinePaymentsEnabled { get; set; } = false;
}
