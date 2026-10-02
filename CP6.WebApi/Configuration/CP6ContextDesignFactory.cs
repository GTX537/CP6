using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CP6.WebApi.Configuration;

// Model scaffolding never starts workers or reads application credentials. Forward application
// remains the responsibility of CP6Context migrations and the existing db-init entry point.
public sealed class CP6ContextDesignFactory : IDesignTimeDbContextFactory<CP6Context>
{
    public CP6Context CreateDbContext(string[] args)
    {
        var design = DatabaseDesignTimeConfiguration.FromArguments(args);
        var profile = DatabaseMigrationProfile.For(design.Database, DatabaseContextKind.Core);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), design.Database,
            design.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }
}

public sealed class IdentityMessagingContextDesignFactory : IDesignTimeDbContextFactory<IdentityMessagingContext>
{
    public IdentityMessagingContext CreateDbContext(string[] args)
    {
        var design = DatabaseDesignTimeConfiguration.FromArguments(args);
        var profile = DatabaseMigrationProfile.For(design.Database, DatabaseContextKind.IdentityPriority);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<IdentityMessagingContext>(), design.Database,
            design.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }
}

public sealed class ErpIntegrationContextDesignFactory : IDesignTimeDbContextFactory<ErpIntegrationContext>
{
    public ErpIntegrationContext CreateDbContext(string[] args)
    {
        var design = DatabaseDesignTimeConfiguration.FromArguments(args);
        var profile = DatabaseMigrationProfile.For(design.Database, DatabaseContextKind.ErpIntegration);
        return new(DatabaseContextOptions.Configure(new DbContextOptionsBuilder<ErpIntegrationContext>(), design.Database,
            design.ConnectionString, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options);
    }
}
