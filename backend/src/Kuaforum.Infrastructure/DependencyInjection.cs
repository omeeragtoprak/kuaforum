using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Kuaforum.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var pgConnString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(pgConnString, npg =>
            {
                npg.UseNetTopologySuite();
                npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        var redisConnString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is missing.");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnString));

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}
