using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing;

/// <summary>
/// RFC-012: admin-ayarlı global konfigürasyon, DB-backed (appsettings/IOptions DEĞİL — admin
/// self-servis değiştirebilmeli, restart gerektirmemeli). Tek satır: seeder (PlatformSettingsSeeder)
/// startup'ta idempotent olarak bir kez oluşturur, sonrasında yalnız Update* metodlarıyla değişir —
/// ikinci bir satır asla eklenmez. Okuma tarafı "basit cache'siz": her handler/job doğrudan DB'den
/// okur (Redis/in-memory cache YOK — RFC-013 bu tabloyu genişletecek, cache invalidation karmaşıklığı
/// bu erken aşamada gereksiz maliyet).
///
/// RFC-014 §2 D1: <c>CommissionRateGlobal</c> KALDIRILDI — gelir modeli ciro komisyonundan sabit
/// aylık aboneliğe geçti (bkz. SubscriptionInvoice, SubscriptionPlan.PriceMinor tek doğruluk
/// kaynağı). Bu alan hiçbir fatura hesabını beslemiyordu (yalnızca CommissionStatement.RateApplied
/// snapshot'ının kaynağıydı, o mekanizma tamamen kalktı) — sessizce ölü bırakmak yerine silindi.
/// </summary>
public sealed class PlatformSettings : AggregateRoot
{
    private PlatformSettings() { }

    public string? CommissionPaymentIban { get; private set; }
    public string? CommissionPaymentBankName { get; private set; }
    public string? CommissionPaymentAccountHolderName { get; private set; }

    /// <summary>RFC-013 C3: owner'ların BookingPolicy.DepositPercent'i için admin-ayarlı alt/üst sınır (örn. %5-%50).</summary>
    public decimal DepositPercentMin { get; private set; }

    /// <summary>RFC-013 C3: bkz. DepositPercentMin.</summary>
    public decimal DepositPercentMax { get; private set; }

    /// <summary>
    /// Seeder tarafından bir kez çağrılır. DepositPercentMin/Max için RFC-013'ün örnek
    /// %5-%50 aralığı varsayılan.
    /// </summary>
    public static PlatformSettings CreateDefault(
        decimal depositPercentMin = 0.05m,
        decimal depositPercentMax = 0.50m)
    {
        return new PlatformSettings
        {
            DepositPercentMin = depositPercentMin,
            DepositPercentMax = depositPercentMax,
        };
    }

    public Result UpdateDepositPercentBounds(decimal min, decimal max)
    {
        // Asimetrik aralık kasıtlı: alt sınır %100'e EŞİT olamaz (kapora hiçbir zaman
        // hizmet bedelinin tamamı olamaz — o zaman "kapora" değil "tam ödeme" olur), üst
        // sınır ise %100'e eşit OLABİLİR (bir owner teoride kaporayı tam bedele eşitleyebilir).
        if (min <= 0 || min >= 1)
            return Result.Failure(Error.Validation("Kapora oranı alt sınırı %0'dan büyük ve %100'den küçük olmalı."));
        if (max <= 0 || max > 1)
            return Result.Failure(Error.Validation("Kapora oranı üst sınırı %0'dan büyük olmalı ve %100'ü aşamaz."));
        if (min >= max)
            return Result.Failure(Error.Validation("Kapora oranı alt sınırı üst sınırından küçük olmalı."));

        DepositPercentMin = min;
        DepositPercentMax = max;
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
