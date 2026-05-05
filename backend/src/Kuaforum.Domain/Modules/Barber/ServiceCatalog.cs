using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Barber;

public sealed class ServiceCatalog : Entity
{
    private ServiceCatalog() { }

    public Guid BarberShopId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ServiceGender Gender { get; private set; }
    public int DurationMinutes { get; private set; }
    public int BufferMinutes { get; private set; }
    public long PriceMinor { get; private set; } // kuruş
    public string Currency { get; private set; } = "TRY";
    public bool IsActive { get; private set; }

    public static Result<ServiceCatalog> Create(
        Guid barberShopId,
        string name,
        ServiceGender gender,
        int durationMinutes,
        long priceMinor,
        int bufferMinutes = 0,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
            return Result.Failure<ServiceCatalog>(Error.Validation("Hizmet adı en fazla 80 karakter olabilir."));
        if (durationMinutes is < 5 or > 600)
            return Result.Failure<ServiceCatalog>(Error.Validation("Süre 5-600 dakika arasında olmalı."));
        if (bufferMinutes is < 0 or > 60)
            return Result.Failure<ServiceCatalog>(Error.Validation("Tampon süresi 0-60 dakika arasında olmalı."));
        if (priceMinor < 0)
            return Result.Failure<ServiceCatalog>(Error.Validation("Fiyat negatif olamaz."));

        return new ServiceCatalog
        {
            BarberShopId = barberShopId,
            Name = name.Trim(),
            Description = description,
            Gender = gender,
            DurationMinutes = durationMinutes,
            BufferMinutes = bufferMinutes,
            PriceMinor = priceMinor,
            IsActive = true,
        };
    }

    public Result Update(string? name, string? description, int? durationMinutes, long? priceMinor, int? bufferMinutes)
    {
        if (name is not null) Name = name.Trim();
        if (description is not null) Description = description;
        if (durationMinutes.HasValue)
        {
            if (durationMinutes is < 5 or > 600)
                return Result.Failure(Error.Validation("Süre 5-600 dakika arasında olmalı."));
            DurationMinutes = durationMinutes.Value;
        }
        if (priceMinor.HasValue)
        {
            if (priceMinor.Value < 0) return Result.Failure(Error.Validation("Fiyat negatif olamaz."));
            PriceMinor = priceMinor.Value;
        }
        if (bufferMinutes.HasValue) BufferMinutes = bufferMinutes.Value;
        Touch();
        return Result.Success();
    }

    public void SetActive(bool active) { IsActive = active; Touch(); }
}

public sealed class StylistService : Entity
{
    private StylistService() { }

    public Guid StylistId { get; private set; }
    public Guid ServiceCatalogId { get; private set; }
    public int? OverrideDurationMinutes { get; private set; }
    public long? OverridePriceMinor { get; private set; }
    public bool IsActive { get; private set; }

    public static StylistService Create(Guid stylistId, Guid serviceCatalogId)
    {
        return new StylistService
        {
            StylistId = stylistId,
            ServiceCatalogId = serviceCatalogId,
            IsActive = true,
        };
    }

    public Result SetOverrides(int? duration, long? price)
    {
        if (duration is not null && duration is < 5 or > 600)
            return Result.Failure(Error.Validation("Özel süre 5-600 dakika arasında olmalı."));
        if (price is < 0)
            return Result.Failure(Error.Validation("Özel fiyat negatif olamaz."));
        OverrideDurationMinutes = duration;
        OverridePriceMinor = price;
        Touch();
        return Result.Success();
    }
}
