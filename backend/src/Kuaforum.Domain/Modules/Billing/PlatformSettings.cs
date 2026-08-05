using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing;

/// <summary>
/// RFC-012: admin-ayarlı global konfigürasyon, DB-backed (appsettings/IOptions DEĞİL — admin
/// self-servis değiştirebilmeli, restart gerektirmemeli). Tek satır: seeder (PlatformSettingsSeeder)
/// startup'ta idempotent olarak bir kez oluşturur, sonrasında yalnız Update* metodlarıyla değişir —
/// ikinci bir satır asla eklenmez. Okuma tarafı "basit cache'siz": her handler/job doğrudan DB'den
/// okur (Redis/in-memory cache YOK — RFC-013 bu tabloyu genişletecek, cache invalidation karmaşıklığı
/// bu erken aşamada gereksiz maliyet).
/// </summary>
public sealed class PlatformSettings : AggregateRoot
{
    private PlatformSettings() { }

    public decimal CommissionRateGlobal { get; private set; }
    public string? CommissionPaymentIban { get; private set; }
    public string? CommissionPaymentBankName { get; private set; }
    public string? CommissionPaymentAccountHolderName { get; private set; }

    /// <summary>
    /// Seeder tarafından bir kez çağrılır. Founder'ın kesin ilk oran kararı implementasyon
    /// anında netleşmediği için burada örnek %20 (RFC-012 founder örneği) varsayılan —
    /// admin PUT /v1/admin/billing/settings ile launch öncesi kesinleştirmeli.
    /// </summary>
    public static PlatformSettings CreateDefault(decimal commissionRateGlobal = 0.20m)
    {
        return new PlatformSettings
        {
            CommissionRateGlobal = commissionRateGlobal,
        };
    }

    public Result UpdateCommissionRateGlobal(decimal rate)
    {
        if (rate <= 0 || rate > 0.5m)
            return Result.Failure(Error.Validation("Komisyon oranı %0 ile %50 arasında olmalı."));

        CommissionRateGlobal = rate;
        Touch();
        return Result.Success();
    }

    public Result UpdateBankingInfo(string? iban, string? bankName, string? accountHolderName)
    {
        if (iban?.Length > 34)
            return Result.Failure(Error.Validation("IBAN en fazla 34 karakter olabilir."));
        if (bankName?.Length > 120)
            return Result.Failure(Error.Validation("Banka adı en fazla 120 karakter olabilir."));
        if (accountHolderName?.Length > 120)
            return Result.Failure(Error.Validation("Hesap sahibi adı en fazla 120 karakter olabilir."));

        CommissionPaymentIban = string.IsNullOrWhiteSpace(iban) ? null : iban.Trim();
        CommissionPaymentBankName = string.IsNullOrWhiteSpace(bankName) ? null : bankName.Trim();
        CommissionPaymentAccountHolderName = string.IsNullOrWhiteSpace(accountHolderName) ? null : accountHolderName.Trim();
        Touch();
        return Result.Success();
    }
}
