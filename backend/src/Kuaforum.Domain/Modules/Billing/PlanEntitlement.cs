using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing;

/// <summary>
/// RFC-014 §2 D1: (PlanCode, Key) başına tek satır — DB'deki tek doğruluk kaynağı. Value her
/// zaman string saklanır (bool/int gibi tipli yorumlama caller sorumluluğunda, bkz.
/// IEntitlementService implementasyonu) — bu, yeni bir entitlement tipi eklendiğinde şema
/// migration'ı gerektirmeden yalnızca yeni bir satır eklenebilmesini sağlar.
/// </summary>
public sealed class PlanEntitlement : Entity
{
    private PlanEntitlement() { }

    public string PlanCode { get; private set; } = null!;
    public string Key { get; private set; } = null!;
    public string Value { get; private set; } = null!;

    public static Result<PlanEntitlement> Create(string planCode, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(planCode))
            return Result.Failure<PlanEntitlement>(Error.Validation("Plan kodu gerekli."));
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            return Result.Failure<PlanEntitlement>(Error.Validation("Entitlement anahtarı gerekli ve en fazla 64 karakter olabilir."));
        if (value is null || value.Length > 200)
            return Result.Failure<PlanEntitlement>(Error.Validation("Entitlement değeri gerekli ve en fazla 200 karakter olabilir."));

        return new PlanEntitlement
        {
            PlanCode = planCode.Trim().ToUpperInvariant(),
            Key = key.Trim(),
            Value = value,
        };
    }
}
