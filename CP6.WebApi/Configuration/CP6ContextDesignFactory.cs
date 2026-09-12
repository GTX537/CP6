using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CP6.WebApi.Configuration;

// Model scaffolding never starts workers or reads application credentials. Forward application
// remains the responsibility of CP6Context migrations and the existing db-init entry point.
public sealed class CP6ContextDesignFactory : IDesignTimeDbContextFactory<CP6Context>
{
    public CP6Context CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer("Server=localhost;Database=CP6_Design;Integrated Security=true;TrustServerCertificate=true").Options);
}

public sealed class IdentityMessagingContextDesignFactory : IDesignTimeDbContextFactory<IdentityMessagingContext>
{
    public IdentityMessagingContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityMessagingContext>()
        .UseSqlServer("Server=localhost;Database=CP6_Design;Integrated Security=true;TrustServerCertificate=true").Options);
}

public sealed class ErpIntegrationContextDesignFactory : IDesignTimeDbContextFactory<ErpIntegrationContext>
{
    public ErpIntegrationContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ErpIntegrationContext>()
        .UseSqlServer("Server=localhost;Database=CP6_Design;Integrated Security=true;TrustServerCertificate=true").Options);
}
