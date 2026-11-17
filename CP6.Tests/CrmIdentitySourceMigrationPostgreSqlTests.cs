using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class CrmIdentitySourceMigrationPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [TenantManagementPostgresTheory]
    [InlineData("issuer")]
    [InlineData("region")]
    [InlineData("client")]
    public async Task Config_change_without_approved_successor_cannot_relabel_existing_directory(string changed)
    {
        var tenant = Guid.NewGuid();
        await using var seed = Context(tenant);
        seed.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = tenant.ToString("N"),
            TenantName = "身份来源迁移模拟租户", Enable = true });
        await seed.SaveChangesAsync();
        await new IdentityBootstrapService(seed, Runtime(tenant)).InitializeAsync(tenant);
        await using var read = Context(tenant);
        var candidate = Runtime(tenant, changed == "issuer" ? "https://successor.example.test" : Issuer,
            changed == "region" ? "eu" : "us", changed == "client" ? "successor-reader" : "source-reader");
        var reader = new IdentitySnapshotReader(read, candidate, new EphemeralDataProtectionProvider());
        var error = await Assert.ThrowsAsync<IdentityReadException>(() => reader.ReadVersionsAsync(tenant, null));
        Assert.Equal("C02_SOURCE_NOT_APPROVED", error.Message);
        Assert.Equal(1, await read.CrmIdentitySnapshots.CountAsync(x => x.TenantId == tenant));
    }

    private const string Issuer = "https://source.example.test";

    [CurrentPermissionPostgresFact]
    public async Task Incomplete_legacy_bootstrap_cannot_enroll_existing_identity_facts_from_startup_configuration()
    {
        var tenant = Guid.NewGuid();
        await using var db = Context(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = tenant.ToString("N"), TenantName = "历史未完成初始化", Enable = true });
        await db.SaveChangesAsync();
        var legacy = Runtime(tenant);
        legacy.Options.SourceBindings.Clear();
        await new IdentityBootstrapService(db, legacy).InitializeAsync(tenant);
        await db.CrmIdentityBootstrapStates.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        var candidate = Runtime(tenant, "https://unapproved.example.test");
        candidate.Options.RequireSourceRegistration = true;
        Assert.Equal("C02_SOURCE_ENROLLMENT_REQUIRED", (await Assert.ThrowsAsync<IdentityReadException>(() =>
            new IdentityBootstrapService(db, candidate).InitializeAsync(tenant))).Message);
        Assert.Equal(1, await db.CrmIdentitySnapshots.CountAsync(x => x.TenantId == tenant));
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_identity_source_v1.registrations WHERE tenant_id={tenant}").SingleAsync());
    }

    private CP6Context Context(Guid tenant, CrmIdentityRuntime? identity = null)
        => server.Context(new TenantContext { CurrentTenantId = tenant }, identity);
    private static CrmIdentityRuntime Runtime(Guid tenant, string issuer = Issuer,
        string region = "us", string client = "source-reader")
    {
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer,
            Tenants = { [tenant] = region }, ProjectionReaderClientIds = [client] };
        var runtime = new CrmIdentityRuntime(options, new IdentityEventValidator(Cp6ContractBundle.Load(
            Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer));
        options.SourceBindings.Add(tenant, new(tenant, tenant.ToString("N"), issuer, region, [client], [client], runtime.ContractBundleSha256));
        return runtime;
    }
}
