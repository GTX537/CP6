using CP6.Core.Persistence;
using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CP6.Space.Infrastructure;

public sealed class SpaceContextDesignFactory : IDesignTimeDbContextFactory<SpaceContext>
{
    public SpaceContext CreateDbContext(string[] args)
    {
        var design = DatabaseDesignTimeConfiguration.FromArguments(args,
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));
        var profile = DatabaseMigrationProfile.For(design.Database, DatabaseContextKind.Space);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<SpaceContext>(), design.Database,
            design.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;

        return new SpaceContext(options, new DesignExecutionContext(), new SystemSpaceClock());
    }

    private sealed class DesignExecutionContext : ISpaceExecutionContext
    {
        public Guid TenantId { get; } =
            Guid.Parse("00000000-0000-0000-0000-0000000000A1");

        public Guid ActorId { get; } =
            Guid.Parse("00000000-0000-0000-0000-000000000001");
    }
}
