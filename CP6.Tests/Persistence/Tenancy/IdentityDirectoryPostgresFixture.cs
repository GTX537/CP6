using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("IdentityDirectoryPostgres", DisableParallelization = true)]
public sealed class IdentityDirectoryPostgresCollection : ICollectionFixture<IdentityDirectoryPostgresFixture>;

public class IdentityDirectoryPostgresFixture : IAsyncLifetime
{
    protected virtual bool CheckV1Preconditions => true;
    protected virtual Task Install(NpgsqlConnection connection, NpgsqlTransaction transaction)
        => IdentityDirectoryRowSecurityV1.InstallAsync(connection, transaction, Logins);
    protected virtual Task CheckProfilePreconditions(NpgsqlConnection connection) => Task.CompletedTask;
    protected virtual Task AfterInstallation(NpgsqlConnection connection) => Task.CompletedTask;
    internal TenantManagementPostgresFixture Server { get; } = new();
    internal readonly string PasswordA = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal readonly string PasswordB = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal static Guid A => TenantStorageContext.A;
    internal static Guid B => TenantStorageContext.B;
    internal static TenantLoginV1[] Logins => [new(A, "directory_a"), new(B, "directory_b")];
    internal CP6Context OwnerContext(Guid tenant) => Server.Context(new TenantContext { CurrentTenantId = tenant });
    internal async Task<NpgsqlConnection> OpenOwner()
    {
        await using var db = OwnerContext(A);
        var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(); return connection;
    }
    internal async Task<NpgsqlConnection> OpenRuntime(Guid tenant)
    {
        await using var db = OwnerContext(tenant);
        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
        {
            Username = tenant == A ? "directory_a" : "directory_b", Password = tenant == A ? PasswordA : PasswordB,
            SearchPath = "pg_catalog", Pooling = false, Options = " ", Enlist = false, IncludeErrorDetail = false
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(); return connection;
    }
    internal static CrmIdentityRuntime Runtime()
    {
        const string issuer = "https://directory-isolation.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        options.Tenants.Add(A, "us"); options.Tenants.Add(B, "us");
        return new CrmIdentityRuntime(options, new IdentityEventValidator(Cp6ContractBundle.Load(
            Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer));
    }
    public async Task InitializeAsync()
    {
        await Server.InitializeAsync();
        if (!CurrentPermissionPostgresFactAttribute.Selected) return;
        try
        {
            await using var owner = await OpenOwner();
            await Sql(owner, $"CREATE ROLE directory_a LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD '{PasswordA}'");
            await Sql(owner, $"CREATE ROLE directory_b LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD '{PasswordB}'");
            var runtime = Runtime();
            foreach (var id in new[] { A, B })
            {
                await using var db = OwnerContext(id);
                db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = id.ToString("N"), TenantName = "read isolation", Enable = true });
                db.Sys_Roles.Add(new Sys_Role { TenantId = id, RoleId = 777, RoleName = "same aggregate", Enable = true });
                await db.SaveChangesAsync();
                await new IdentityBootstrapService(db, runtime).InitializeAsync(id);
            }
            await CheckProfilePreconditions(owner);
            // Each anomaly is rolled back before the next attempt, so rejection cannot be
            // accidentally satisfied by an already installed directory from an earlier case.
            foreach (var anomaly in CheckV1Preconditions ? new[]
            {
                "GRANT SELECT ON public.\"Sys_Users\" TO directory_a",
                "GRANT SELECT (\"PayloadJson\") ON crm_identity.\"Snapshot\" TO PUBLIC",
                "GRANT TEMP ON DATABASE tenant_management_component TO directory_a",
                "GRANT pg_read_all_data TO directory_a",
                "ALTER ROLE directory_a BYPASSRLS",
                "CREATE POLICY unexpected ON crm_identity.\"Snapshot\" USING (true)",
                "ALTER TABLE crm_identity.\"Snapshot\" DISABLE TRIGGER cp6_identity_generation_v1",
                "ALTER TABLE crm_identity.\"Snapshot\" ADD COLUMN unexpected text",
                "GRANT UPDATE ON pg_catalog.pg_class TO PUBLIC",
                "ALTER FUNCTION public.cp6_mes_process_progress_v1() SECURITY DEFINER",
                "CREATE FUNCTION public.unexpected() RETURNS integer LANGUAGE sql AS 'SELECT 1'",
                "CREATE SCHEMA cp6_tenancy_v1"
            } : [])
            {
                await using var rejected = await owner.BeginTransactionAsync();
                await Sql(owner, anomaly);
                var error = await Record.ExceptionAsync(() => IdentityDirectoryRowSecurityV1.InstallAsync(owner, rejected, Logins));
                Assert.True(error is InvalidOperationException or TenantStorageException,
                    "Expected a typed precondition rejection for: " + anomaly + "; actual: " + error?.GetType().Name);
                await rejected.RollbackAsync();
            }
            await using (var rolledBack = await owner.BeginTransactionAsync())
            {
                await Install(owner, rolledBack);
                await rolledBack.RollbackAsync();
            }
            Assert.False(await Scalar<bool>(owner, "SELECT EXISTS(SELECT FROM pg_namespace WHERE nspname='cp6_tenancy_v1')"));
            Assert.False(await Scalar<bool>(owner, "SELECT EXISTS(SELECT FROM pg_roles WHERE rolname='cp6_tenant_lookup_v1')"));
            Assert.False(await Scalar<bool>(owner, "SELECT relrowsecurity FROM pg_class WHERE oid='crm_identity.\"Snapshot\"'::regclass"));
            await using (var install = await owner.BeginTransactionAsync())
            {
                await Install(owner, install);
                await install.CommitAsync();
            }
            await AfterInstallation(owner);
        }
        catch { await Server.DisposeAsync(); throw; }
    }
    public Task DisposeAsync() => Server.DisposeAsync();
    internal static async Task Sql(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    internal static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); return (T)(await command.ExecuteScalarAsync())!; }
}
