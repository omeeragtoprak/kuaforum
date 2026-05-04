using Kuaforum.Domain.Modules.Barber;
using Kuaforum.Domain.Modules.Geography;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Development-only seeder. Creates 6 demo BarberShops (2 in each big city)
/// with owner User, stylists, services, working hours, and addresses.
/// Idempotent: skips if any BarberShop with slug "demo-*" exists.
///
/// PRODUCTION: This seeder should NEVER run. Real shops onboard via barber panel.
/// </summary>
public sealed class DemoBarberSeeder(ApplicationDbContext db, ILogger<DemoBarberSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Idempotency: if any demo owner phone already exists, skip the entire seed.
        var demoPhones = DemoData.Select(d => d.OwnerPhone).ToArray();
        var hasDemo = await db.Users.AnyAsync(u => u.PhoneE164 != null && demoPhones.Contains(u.PhoneE164), ct);
        if (hasDemo)
        {
            logger.LogDebug("DemoBarberSeeder: demo data already present, skipping.");
            return;
        }

        logger.LogInformation("DemoBarberSeeder: creating 6 demo barber shops...");

        var cities = await db.Cities.ToDictionaryAsync(c => c.PlateCode, ct);
        var districts = await db.Districts.AsNoTracking().ToListAsync(ct);
        var districtsByName = districts
            .GroupBy(d => d.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var demo in DemoData)
        {
            if (!cities.TryGetValue(demo.CityPlate, out var city))
            {
                logger.LogWarning("DemoBarberSeeder: city plate {Plate} not found, skipping {Name}.", demo.CityPlate, demo.ShopName);
                continue;
            }
            if (!districtsByName.TryGetValue(demo.DistrictName.ToLowerInvariant(), out var district))
            {
                logger.LogWarning("DemoBarberSeeder: district {District} not found, skipping {Name}.", demo.DistrictName, demo.ShopName);
                continue;
            }

            var ownerPhone = PhoneE164.TryCreate(demo.OwnerPhone);
            if (ownerPhone.IsFailure) continue;
            var ownerResult = User.CreateCustomerByPhone(ownerPhone.Value, displayName: demo.OwnerName);
            if (ownerResult.IsFailure) continue;
            var owner = ownerResult.Value;
            db.Users.Add(owner);

            var shopResult = BarberShop.Create(owner.Id, demo.ShopName, demo.GenderType);
            if (shopResult.IsFailure) continue;
            var shop = shopResult.Value;
            shop.UpdateBasics(name: $"demo-{demo.Slug}", description: demo.Description, genderType: null, phone: demo.ShopPhone);
            shop.UpdateBasics(name: demo.ShopName, description: null, genderType: null, phone: null);

            var coordResult = Coordinate.TryCreate(demo.Latitude, demo.Longitude);
            if (coordResult.IsSuccess)
            {
                // Use neighborhood = first available in district, or null
                var neighborhood = await db.Neighborhoods.AsNoTracking()
                    .FirstOrDefaultAsync(n => n.DistrictId == district.Id, ct);
                var addressResult = Address.Create(
                    city.Id, district.Id,
                    neighborhood?.Id ?? district.Id, // fallback if no neighborhood
                    demo.AddressLine1, coordResult.Value);
                if (addressResult.IsSuccess) shop.SetAddress(addressResult.Value);
            }

            // Approve KYC immediately for demo
            shop.MarkPendingKyc();
            shop.ApproveKyc(subMerchantKey: $"DEMO-SUB-{demo.Slug}");

            db.BarberShops.Add(shop);

            // Stylists
            foreach (var stylistName in demo.Stylists)
            {
                var stylistResult = Stylist.Create(shop.Id, stylistName);
                if (stylistResult.IsFailure) continue;
                var stylist = stylistResult.Value;
                db.Stylists.Add(stylist);

                // Working hours: Mon-Sat 09:00-19:00, Sun closed
                foreach (var dow in Enum.GetValues<DayOfWeek>())
                {
                    var isClosed = dow == DayOfWeek.Sunday;
                    var open = new TimeOnly(9, 0);
                    var close = new TimeOnly(19, 0);
                    var whResult = WorkingHours.Create(stylist.Id, dow, open, close, isClosed);
                    if (whResult.IsSuccess) db.WorkingHours.Add(whResult.Value);
                }
            }

            // Services
            foreach (var (name, gender, duration, priceTl) in demo.Services)
            {
                var sResult = ServiceCatalog.Create(shop.Id, name, gender, duration, priceMinor: priceTl * 100, bufferMinutes: 5);
                if (sResult.IsSuccess) db.ServiceCatalogs.Add(sResult.Value);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DemoBarberSeeder: complete.");
    }

    private record DemoShop(
        int CityPlate,
        string DistrictName,
        string Slug,
        string ShopName,
        string Description,
        string ShopPhone,
        SalonGenderType GenderType,
        string AddressLine1,
        double Latitude,
        double Longitude,
        string OwnerName,
        string OwnerPhone,
        string[] Stylists,
        (string Name, ServiceGender Gender, int Duration, int PriceTl)[] Services);

    private static readonly DemoShop[] DemoData =
    {
        new(
            CityPlate: 34, DistrictName: "Kadıköy", Slug: "kadikoy-modern",
            ShopName: "Modern Stüdyo Kuaför", Description: "Modern kesim ve renklendirme uzmanı, Kadıköy merkez.",
            ShopPhone: "+902161234567", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Bahariye Cad. No:42", Latitude: 40.9897, Longitude: 29.0276,
            OwnerName: "Ayşe Modern", OwnerPhone: "+905001000001",
            Stylists: new[] { "Ayşe Modern", "Selin Kaya" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim (Erkek)", ServiceGender.Male, 30, 250),
                ("Saç Kesim (Kadın)", ServiceGender.Female, 45, 400),
                ("Saç Boyama (Tek Renk)", ServiceGender.Female, 90, 1200),
                ("Saç Boyama (Röfle)", ServiceGender.Female, 150, 2200),
                ("Fön", ServiceGender.Female, 30, 250),
                ("Sakal Tıraşı", ServiceGender.Male, 20, 150),
            }),
        new(
            CityPlate: 34, DistrictName: "Beşiktaş", Slug: "besiktas-classic",
            ShopName: "Klasik Berber Beşiktaş", Description: "70 yıllık aile berberi. Klasik tıraş + sakal bakım.",
            ShopPhone: "+902127654321", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Çarşı Cad. No:8", Latitude: 41.0431, Longitude: 29.0061,
            OwnerName: "Mehmet Usta", OwnerPhone: "+905001000002",
            Stylists: new[] { "Mehmet Usta", "Ali Çırak", "Hakan Berber" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 25, 200),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 100),
                ("Klasik Tıraş (Ustura)", ServiceGender.Male, 30, 250),
                ("Saç + Sakal Kombo", ServiceGender.Male, 40, 280),
                ("Yüz Bakımı", ServiceGender.Male, 30, 200),
            }),
        new(
            CityPlate: 6, DistrictName: "Çankaya", Slug: "cankaya-stilim",
            ShopName: "Stilim Kuaför", Description: "Çankaya'nın en sevilen kadın kuaförü.",
            ShopPhone: "+903121111111", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Tunalı Hilmi Cad. No:78", Latitude: 39.9035, Longitude: 32.8541,
            OwnerName: "Zeynep Stil", OwnerPhone: "+905001000003",
            Stylists: new[] { "Zeynep Stil", "Elif Renk" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 45, 350),
                ("Saç Boyama", ServiceGender.Female, 90, 900),
                ("Balyaj", ServiceGender.Female, 180, 2500),
                ("Saç Bakım Maskesi", ServiceGender.Female, 30, 350),
                ("Fön + Şekillendirme", ServiceGender.Female, 30, 200),
            }),
        new(
            CityPlate: 6, DistrictName: "Yenimahalle", Slug: "yenimahalle-bayan",
            ShopName: "Yenimahalle Güzellik", Description: "Saç, makyaj, manikür, kalıcı oje.",
            ShopPhone: "+903122222222", GenderType: SalonGenderType.FemaleOnly,
            AddressLine1: "Demetevler Cad. No:55", Latitude: 39.9676, Longitude: 32.7825,
            OwnerName: "Fatma Güzellik", OwnerPhone: "+905001000004",
            Stylists: new[] { "Fatma Güzellik" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Female, 40, 280),
                ("Manikür", ServiceGender.Female, 30, 200),
                ("Kalıcı Oje", ServiceGender.Female, 45, 350),
                ("Pedikür", ServiceGender.Female, 45, 300),
                ("Makyaj", ServiceGender.Female, 60, 600),
            }),
        new(
            CityPlate: 35, DistrictName: "Karşıyaka", Slug: "karsiyaka-egeb",
            ShopName: "Ege Berberhanesi", Description: "Karşıyaka sahil yakını klasik erkek berberi.",
            ShopPhone: "+902323333333", GenderType: SalonGenderType.MaleOnly,
            AddressLine1: "Cemal Gürsel Cad. No:120", Latitude: 38.4631, Longitude: 27.1086,
            OwnerName: "Hasan Berber", OwnerPhone: "+905001000005",
            Stylists: new[] { "Hasan Berber", "Murat Çırak" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Saç Kesim", ServiceGender.Male, 25, 180),
                ("Sakal", ServiceGender.Male, 15, 100),
                ("Saç + Sakal", ServiceGender.Male, 35, 250),
                ("Yıkama", ServiceGender.Male, 10, 50),
            }),
        new(
            CityPlate: 35, DistrictName: "Bornova", Slug: "bornova-ogrenci",
            ShopName: "Bornova Öğrenci Kuaförü", Description: "Üniversite çevresi, uygun fiyatlı, hızlı servis.",
            ShopPhone: "+902324444444", GenderType: SalonGenderType.Unisex,
            AddressLine1: "Üniversite Cad. No:23", Latitude: 38.4708, Longitude: 27.2154,
            OwnerName: "Cem Genç", OwnerPhone: "+905001000006",
            Stylists: new[] { "Cem Genç", "Burak Stilist" },
            Services: new (string, ServiceGender, int, int)[]
            {
                ("Erkek Saç Kesim", ServiceGender.Male, 20, 120),
                ("Kadın Saç Kesim", ServiceGender.Female, 30, 180),
                ("Sakal Tıraşı", ServiceGender.Male, 15, 80),
                ("Hızlı Fön", ServiceGender.Female, 20, 100),
            }),
    };
}
