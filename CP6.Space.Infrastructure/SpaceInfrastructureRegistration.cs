using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.Infrastructure;

public static class SpaceInfrastructureRegistration
{
    public static IServiceCollection AddSpaceDesignV1Persistence(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The Space database connection string is required.");
        }

        services.AddDbContext<SpaceContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(
                    SpaceContext.MigrationsHistoryTable)));
        services.AddSingleton<ISpaceClock, SystemSpaceClock>();
        services.AddScoped<ISpaceVersionCloneStore, EfSpaceVersionCloneStore>();
        services.AddScoped<SpaceVersionCloneCoordinator>();
        services.AddScoped<SpaceSourceCoordinator>();
        services.AddScoped<ISpaceDesignV1Service, SpaceDesignV1Service>();
        return services;
    }
}
