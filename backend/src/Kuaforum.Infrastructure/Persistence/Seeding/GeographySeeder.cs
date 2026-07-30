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

        if (existingCount < Tr81CitySeed.Cities.Count)
        {
            logger.LogInformation("GeographySeeder: seeding {Count} cities...", Tr81CitySeed.Cities.Count);

            var existingPlates = await db.Cities.Select(c => c.PlateCode).ToListAsync(ct);
            var existingPlateSet = existingPlates.ToHashSet();

            foreach (var (plate, name) in Tr81CitySeed.Cities)
            {
                if (existingPlateSet.Contains(plate)) continue;
                db.Cities.Add(City.Create(plate, name));
            }
            await db.SaveChangesAsync(ct);
        }
        else
        {
            logger.LogDebug("GeographySeeder: 81 cities already present, skipping cities.");
        }

        // Districts seed her zaman çalıştırılır — yeni şehirler eklendikçe SampleDistricts genişler.
        var citiesByPlate = await db.Cities.ToDictionaryAsync(c => c.PlateCode, ct);

        var districtsAdded = 0;
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
                districtsAdded++;
            }
        }
        if (districtsAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("GeographySeeder: added {N} new districts.", districtsAdded);
        }

        // Erzurum-pilot mahalle seed'i (RFC-007) — mahalle adres adımında opsiyonel,
        // ama default şehirde seçilebilir liste olmalı. Idempotent (slug guard).
        if (citiesByPlate.TryGetValue(25, out var erzurum))
        {
            var erzurumDistricts = await db.Districts
                .Where(d => d.CityId == erzurum.Id)
                .ToListAsync(ct);
            var districtsByName = erzurumDistricts.ToDictionary(d => d.Name);

            var neighborhoodsAdded = 0;
            foreach (var (districtName, neighborhoodNames) in Tr81CitySeed.ErzurumNeighborhoods)
            {
                if (!districtsByName.TryGetValue(districtName, out var district)) continue;

                var existingNeighborhoodSlugs = await db.Neighborhoods
                    .Where(n => n.DistrictId == district.Id)
                    .Select(n => n.Slug)
                    .ToListAsync(ct);
                var existingNeighborhoodSlugSet = existingNeighborhoodSlugs.ToHashSet();

                foreach (var neighborhoodName in neighborhoodNames)
                {
                    var n = Neighborhood.Create(district.Id, neighborhoodName);
                    if (existingNeighborhoodSlugSet.Contains(n.Slug)) continue;
                    db.Neighborhoods.Add(n);
                    neighborhoodsAdded++;
                }
            }
            if (neighborhoodsAdded > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("GeographySeeder: added {N} Erzurum pilot neighborhoods.", neighborhoodsAdded);
            }
        }
    }
}
