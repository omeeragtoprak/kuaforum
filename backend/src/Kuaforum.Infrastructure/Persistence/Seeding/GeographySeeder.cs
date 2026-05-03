using Kuaforum.Domain.Modules.Geography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

/// <summary>
/// Idempotent seeder: 81 il + sample districts (4 büyükşehir).
/// Tam NVI 970+ ilçe + 30K+ mahalle import scripti runbook'ta.
/// </summary>
public sealed class GeographySeeder(ApplicationDbContext db, ILogger<GeographySeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existingCount = await db.Cities.CountAsync(ct);
        if (existingCount >= Tr81CitySeed.Cities.Count)
        {
            logger.LogDebug("GeographySeeder: 81 cities already present, skipping.");
            return;
        }

        logger.LogInformation("GeographySeeder: seeding {Count} cities...", Tr81CitySeed.Cities.Count);

        var existingPlates = await db.Cities.Select(c => c.PlateCode).ToListAsync(ct);
        var existingPlateSet = existingPlates.ToHashSet();

        foreach (var (plate, name) in Tr81CitySeed.Cities)
        {
            if (existingPlateSet.Contains(plate)) continue;
            db.Cities.Add(City.Create(plate, name));
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("GeographySeeder: seeding sample districts for big cities...");

        var citiesByPlate = await db.Cities.ToDictionaryAsync(c => c.PlateCode, ct);

        foreach (var (plate, districts) in Tr81CitySeed.SampleDistricts)
        {
            if (!citiesByPlate.TryGetValue(plate, out var city)) continue;

            var existingDistrictSlugs = await db.Districts
                .Where(d => d.CityId == city.Id)
                .Select(d => d.Slug)
                .ToListAsync(ct);
            var existingSlugSet = existingDistrictSlugs.ToHashSet();

            foreach (var districtName in districts)
            {
                var d = District.Create(city.Id, districtName);
                if (existingSlugSet.Contains(d.Slug)) continue;
                db.Districts.Add(d);
            }
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("GeographySeeder: complete.");
    }
}
