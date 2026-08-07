using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kuaforum.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by `dotnet ef` CLI for migrations.
/// Production runtime uses DI registration in Infrastructure.DependencyInjection.
///
/// Review fix (2026-08-07): önceden <c>KUAFORUM_DESIGN_TIME_CONNECTION</c> set edilmemişse
/// sessizce <c>Port=5432</c>'ye düşüyordu — ama dev Postgres 5435'te (bkz. appsettings.
/// Development.json), 5432'de BAŞKA bir projenin veritabanı var. Env set etmeden
/// <c>dotnet ef database update</c> çalıştıran biri (özellikle bu repo'da çalışan farklı
/// bir ajan/geliştirici) o projenin DB'sini bozabilirdi. Artık fail-fast: env yoksa migration
/// hangi veritabanına uygulanacağı BELİRSİZ olduğu için (varsayılan bir porta sessizce
/// düşmek yerine) net bir hata ile durur.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KUAFORUM_DESIGN_TIME_CONNECTION")
            ?? throw new InvalidOperationException(
                "KUAFORUM_DESIGN_TIME_CONNECTION ortam değişkeni set edilmemiş. `dotnet ef` " +
                "migration komutları hangi veritabanına uygulanacağını BELİRSİZ bırakamaz — " +
                "sessiz bir varsayılana (ör. Port=5432, ki o başka bir projenin veritabanı " +
                "olabilir) düşmek yerine burada durur. Dev ortamı için şunu set edin: " +
                "KUAFORUM_DESIGN_TIME_CONNECTION=\"Host=localhost;Port=5435;Database=kuaforum_dev;" +
                "Username=kuaforum;Password=devpassword\" (bkz. appsettings.Development.json " +
                "ConnectionStrings:Postgres — dev Postgres 5435'te dinliyor, 5432 DEĞİL).");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npg =>
        {
            npg.UseNetTopologySuite();
            npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
