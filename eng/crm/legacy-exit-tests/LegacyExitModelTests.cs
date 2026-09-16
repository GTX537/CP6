using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Crm.LegacyExit.Tests;

public class LegacyExitModelTests
{
    internal const string ExitMigration = "20260916130000_RetireLegacyCrmModel";
    internal static readonly string[] Tables =
    [
        "Crm_Account", "Crm_Activity", "Crm_Collaborator", "Crm_Contact", "Crm_ErpLink",
        "Crm_IntakeConfig", "Crm_IntakeMember", "Crm_Lead", "Crm_MediaAsset", "Crm_MergeRecord",
        "Crm_Opportunity", "Crm_PageRevision", "Crm_PageTranslation", "Crm_PublicForm", "Crm_PublicRoute",
        "Crm_PublicSubmission", "Crm_Site", "Crm_SitePage", "Crm_SourceTouch", "Crm_StageHistory"
    ];
    internal static CP6Context ModelContext() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseSqlServer("Server=localhost;Database=C04B_ModelOnly;Integrated Security=true;TrustServerCertificate=true").Options);

    [Fact]
    public void RuntimeAndSnapshotDoNotOwnLegacyBusinessTables()
    {
        using var db = ModelContext();
        var runtimeTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!;
        var snapshotTables = snapshot.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();
        Assert.Empty(Tables.Intersect(runtimeTables));
        Assert.Empty(Tables.Intersect(snapshotTables));
    }

    [Fact]
    public void LegacyEntityAndStateMachineTypesAreNotShippedInProductionAssemblies()
    {
        Assert.DoesNotContain(typeof(Sys_BrowserSession).Assembly.GetTypes(),
            t => t.Namespace == "CP6.Entity.DomainModels.Crm");
        Assert.DoesNotContain(typeof(CP6Context).Assembly.GetTypes(),
            t => t.Namespace == "CP6.Core.Services.Crm");
    }

    [Fact]
    public void IdentityAndErpReferencesRemainOwnedByCore()
    {
        using var db = ModelContext();
        foreach (var type in new[] { typeof(Sys_BrowserSession), typeof(CrmIdentitySnapshot),
                     typeof(CrmServiceTokenRecord), typeof(CrmIdentityBootstrapState), typeof(BusinessPartner) })
            Assert.NotNull(db.Model.FindEntityType(type));
        Assert.NotNull(db.Model.FindEntityType(typeof(BusinessPartner))!.FindProperty("CrmAccountId"));
    }

    [Fact]
    public void ForwardMigrationPreservesPhysicalTablesAndRejectsRollback()
    {
        using var db = ModelContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        Assert.True(assembly.Migrations.ContainsKey(ExitMigration));
        var migration = assembly.CreateMigration(assembly.Migrations[ExitMigration], db.Database.ProviderName!);
        Assert.Empty(migration.UpOperations);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        var script = db.GetService<IMigrator>().GenerateScript("20260912085508_CrmErpDeliveryReplayAudit", ExitMigration);
        Assert.Contains(ExitMigration, script);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
    }
}
