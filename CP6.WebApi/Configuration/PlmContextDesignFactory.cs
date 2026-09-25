using CP6.Core.Services.Common;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CP6.WebApi.Configuration;

public sealed class PlmContextDesignFactory : IDesignTimeDbContextFactory<PlmContext>
{
    public PlmContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PlmContext>()
            .UseSqlServer("Server=localhost;Database=CP6_Design;Integrated Security=true;TrustServerCertificate=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory_Plm", "plm")).Options,
        new TenantContext { CurrentTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000A1") });
}
