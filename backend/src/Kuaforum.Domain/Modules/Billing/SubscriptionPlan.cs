using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Billing;

/// <summary>
/// RFC-014 §2 D1: sabit aylık abonelik planı (LAUNCH_FREE | STARTER | PRO). Bilinçli olarak
/// <see cref="Entity"/>/<see cref="AggregateRoot"/>'tan TÜREMİYOR — Code, bir Guid surrogate
/// key yerine doğal birincil anahtar seçildi çünkü plan kodları kod tabanında
/// (<see cref="SubscriptionPlanCodes"/>) sabit string olarak referans alınıyor; plan sayısı
/// da (V1'de üç, statik) bir surrogate key + ekstra join'i gerektirmeyecek kadar az.
/// Version/DomainEvents de gereksiz — bu satırlar yalnız seeder tarafından idempotent olarak
/// yaratılır, PlatformSettings gibi admin'in sık mutasyona uğrattığı bir aggregate değil
/// (V1'de en azından; plan yönetim endpoint'leri bu paketin kapsamı dışında bırakıldı).
/// </summary>
public sealed class SubscriptionPlan
{
    private SubscriptionPlan() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    /// <summary>
    /// null = fiyat henüz belirlenmedi (STARTER/PRO — V2'de fiyatlandırma kararı netleşince
    /// doldurulacak). Kasıtlı olarak nullable: 0 kullanılmadı çünkü LAUNCH_FREE'nin "gerçekten
    /// her zaman ücretsiz" (PriceMinor=0) anlamıyla karışırdı — "belirsiz" ile "ücretsiz" farklı
    /// durumlar, aynı sentinel değeri paylaşamaz.
    /// </summary>
    public long? PriceMinor { get; private set; }

    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public static Result<SubscriptionPlan> Create(string code, string name, long? priceMinor, bool isActive, int displayOrder)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32)
            return Result.Failure<SubscriptionPlan>(Error.Validation("Plan kodu gerekli ve en fazla 32 karakter olabilir."));
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            return Result.Failure<SubscriptionPlan>(Error.Validation("Plan adı gerekli ve en fazla 80 karakter olabilir."));
        if (priceMinor is < 0)
            return Result.Failure<SubscriptionPlan>(Error.Validation("Plan fiyatı negatif olamaz."));
        if (displayOrder < 0)
            return Result.Failure<SubscriptionPlan>(Error.Validation("Sıralama değeri negatif olamaz."));

        return new SubscriptionPlan
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            PriceMinor = priceMinor,
            IsActive = isActive,
            DisplayOrder = displayOrder,
        };
    }
}
