using Kuaforum.Domain.Modules.Geography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(x => x.PlateCode).HasColumnName("plate_code");
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.PlateCode).IsUnique().HasDatabaseName("ux_cities_plate");
        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_cities_slug");
    }
}

internal sealed class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("districts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CityId).HasColumnName("city_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.CityId).HasDatabaseName("ix_districts_city");
        builder.HasIndex(x => new { x.CityId, x.Slug }).IsUnique().HasDatabaseName("ux_districts_city_slug");
    }
}

internal sealed class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("neighborhoods");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DistrictId).HasColumnName("district_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        builder.Property(x => x.PostalCode).HasColumnName("postal_code").HasMaxLength(8);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(x => x.DistrictId).HasDatabaseName("ix_neighborhoods_district");
        builder.HasIndex(x => new { x.DistrictId, x.Slug }).IsUnique().HasDatabaseName("ux_neighborhoods_district_slug");
        builder.HasIndex(x => x.PostalCode).HasDatabaseName("ix_neighborhoods_postal");
    }
}
