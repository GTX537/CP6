using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CP6.Space.Infrastructure;

public sealed class SpaceContextDesignFactory : IDesignTimeDbContextFactory<SpaceContext>
{
    public SpaceContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ??
            "Server=(localdb)\\MSSQLLocalDB;Database=CP6_Space_Design;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseSqlServer(
                connectionString,
                sql => sql.MigrationsHistoryTable(SpaceContext.MigrationsHistoryTable))
            .Options;

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
