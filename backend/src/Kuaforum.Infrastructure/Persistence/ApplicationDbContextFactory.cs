using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kuaforum.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by `dotnet ef` CLI for migrations.
/// Production runtime uses DI registration in Infrastructure.DependencyInjection.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("KUAFORUM_DESIGN_TIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=kuaforum_dev;Username=kuaforum;Password=devpassword";

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npg =>
        {
            npg.UseNetTopologySuite();
            npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
