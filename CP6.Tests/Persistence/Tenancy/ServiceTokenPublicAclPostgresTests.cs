using CP6.Core.Persistence.Tenancy;
using CP6.Persistence.PostgreSql.Tenancy;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("ServiceTokenPublicAclPostgres", DisableParallelization = true)]
public sealed class ServiceTokenPublicAclPostgresCollection : ICollectionFixture<TenantStoragePostgresFixture>;

[Collection("ServiceTokenPublicAclPostgres")]
public sealed class ServiceTokenPublicAclPostgresTests(TenantStoragePostgresFixture server, ITestOutputHelper output)
{
    [TenantStoragePostgresFact]
    public async Task Public_system_catalog_read_is_rejected_on_fresh_install_and_replay()
    {
        const string database = "service_public_acl";
        await using (var root = await server.OpenOwnerAsync("postgres"))
            await TenantStoragePostgresFixture.Sql(root, $"CREATE DATABASE {database}");
        await using var control = await server.OpenOwnerAsync(database);
        var oid = (uint)(await TenantStoragePostgresFixture.Scalar(control,
            "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
        var system = (string)(await TenantStoragePostgresFixture.Scalar(control,
            "SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!;
        var request = new ServiceTokenInstallRequest(Guid.NewGuid(), "127.0.0.1", TenantStoragePostgresFixture.Port,
            database, oid, system, [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")]);
        var baseline = ServiceTokenBaselineV1.GenerateSql();
        Task<ServiceRevocationInstallResult> Install() => ServiceRevocationInstallation.InstallServiceTokenAsync(control, request, baseline);
        async Task<string> Hash()
        {
            await using var transaction = await control.BeginTransactionAsync();
            return await ServiceTokenProfileV1.HashAsync(control, transaction, default);
        }
        async Task<bool> CanRead()
        {
            await using var tenant = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", server.PasswordA, database);
            return (bool)(await TenantStoragePostgresFixture.Scalar(tenant,
                "SELECT pg_catalog.has_table_privilege(current_user,'pg_catalog.pg_authid','SELECT')"))!;
        }
        async Task CannotRead()
        {
            Assert.False(await CanRead());
            await using var tenant = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", server.PasswordA, database);
            // Only an aggregate, never fetch or print any role or credential fields.
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() =>
                TenantStoragePostgresFixture.Scalar(tenant, "SELECT count(*) FROM pg_catalog.pg_authid"))).SqlState);
        }
        const string grant = "GRANT SELECT ON pg_catalog.pg_authid TO PUBLIC";
        const string revoke = "REVOKE SELECT ON pg_catalog.pg_authid FROM PUBLIC";
        try
        {
            await CannotRead();
            await using (var tenant = await TenantStoragePostgresFixture.OpenRawAsync("tenant_a", server.PasswordA, database))
                Assert.True((bool)(await TenantStoragePostgresFixture.Scalar(tenant, """
                    SELECT has_table_privilege(current_user,'pg_catalog.pg_roles','SELECT')
                      AND has_function_privilege(current_user,'pg_catalog.current_database()','EXECUTE')
                      AND has_schema_privilege(current_user,'pg_catalog','USAGE')
                    """))!);
            // A second object kind exercises the native initial-ACL exceptions without invoking
            // a privileged function or exposing any file or credential content.
            await TenantStoragePostgresFixture.Sql(control, "GRANT EXECUTE ON FUNCTION pg_catalog.pg_read_file(text) TO PUBLIC");
            try
            {
                Assert.Equal(TenantStorageFailure.ConfigurationRejected,
                    (await Assert.ThrowsAsync<TenantStorageException>(Install)).Failure);
            }
            finally { await TenantStoragePostgresFixture.Sql(control, "REVOKE EXECUTE ON FUNCTION pg_catalog.pg_read_file(text) FROM PUBLIC"); }
            await TenantStoragePostgresFixture.Sql(control, grant);
            Assert.True(await CanRead());
            var freshError = await Record.ExceptionAsync(Install);
            await TenantStoragePostgresFixture.Sql(control, revoke);
            await CannotRead();
            // After a correctly rejected fresh attempt, install the clean original request so the
            // same scenario can independently exercise replay. A vulnerable install already exists.
            if (freshError is not null) await Install();
            var before = await Hash();
            await TenantStoragePostgresFixture.Sql(control, grant);
            Assert.True(await CanRead());
            var after = await Hash();
            var replayError = await Record.ExceptionAsync(Install);
            output.WriteLine($"freshRejected={freshError is not null}; sealChanged={before != after}; replayRejected={replayError is not null}; ordinaryTenantRead=true");
            await TenantStoragePostgresFixture.Sql(control, revoke);
            await CannotRead();
            output.WriteLine("PUBLIC grant removed; ordinary tenant pg_authid count(*) denied (42501).");
            Assert.Equal(TenantStorageFailure.ConfigurationRejected, Assert.IsType<TenantStorageException>(freshError).Failure);
            Assert.NotEqual(before, after);
            Assert.Equal(TenantStorageFailure.ConfigurationRejected, Assert.IsType<TenantStorageException>(replayError).Failure);
            Assert.Equal(before, await Hash());
            var installed = await Install();
            output.WriteLine("Original request replay succeeds after PUBLIC grant/revoke; normalized seal restored.");
            foreach (var (change, restore) in new[]
            {
                ("GRANT SELECT(rolname) ON pg_catalog.pg_authid TO PUBLIC", "REVOKE SELECT(rolname) ON pg_catalog.pg_authid FROM PUBLIC"),
                ("GRANT EXECUTE ON FUNCTION pg_catalog.pg_read_file(text) TO PUBLIC", "REVOKE EXECUTE ON FUNCTION pg_catalog.pg_read_file(text) FROM PUBLIC"),
                ("REVOKE SELECT ON pg_catalog.pg_roles FROM PUBLIC", "GRANT SELECT ON pg_catalog.pg_roles TO PUBLIC")
            })
            {
                await TenantStoragePostgresFixture.Sql(control, change);
                try
                {
                    Assert.NotEqual(before, await Hash());
                    Assert.Equal(TenantStorageFailure.ConfigurationRejected,
                        (await Assert.ThrowsAsync<TenantStorageException>(Install)).Failure);
                }
                finally { await TenantStoragePostgresFixture.Sql(control, restore); }
                Assert.Equal(before, await Hash());
                Assert.Equal(installed, await Install());
            }
            output.WriteLine("Column/function extra PUBLIC grants and removal of native pg_roles SELECT rejected; each restored original request succeeds.");
        }
        finally { await TenantStoragePostgresFixture.Sql(control, revoke); }
    }
}

[CollectionDefinition("ServiceTokenIdentifierPostgres", DisableParallelization = true)]
public sealed class ServiceTokenIdentifierPostgresCollection : ICollectionFixture<TenantStoragePostgresFixture>;

[Collection("ServiceTokenIdentifierPostgres")]
public sealed class ServiceTokenIdentifierPostgresTests(TenantStoragePostgresFixture server)
{
    [TenantStoragePostgresFact]
    public async Task Database_and_login_containing_legacy_schema_text_keep_their_exact_identity()
    {
        const string database = "cp6_service_revocation_v1";
        const string login = "tenant_cp6_service_revocation_v1";
        await using (var root = await server.OpenOwnerAsync("postgres"))
        {
            await TenantStoragePostgresFixture.Sql(root, $"CREATE DATABASE {database}");
            await TenantStoragePostgresFixture.Sql(root, $"CREATE ROLE {login} LOGIN PASSWORD '{server.PasswordA}'");
        }
        await using var control = await server.OpenOwnerAsync(database);
        var oid = (uint)(await TenantStoragePostgresFixture.Scalar(control,
            "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
        var system = (string)(await TenantStoragePostgresFixture.Scalar(control,
            "SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!;
        var request = new ServiceTokenInstallRequest(Guid.NewGuid(), "127.0.0.1", TenantStoragePostgresFixture.Port,
            database, oid, system, [new(TenantStorageContext.A, login)]);
        var baseline = ServiceTokenBaselineV1.GenerateSql();
        var result = await ServiceRevocationInstallation.InstallServiceTokenAsync(control, request, baseline);
        Assert.Equal(result, await ServiceRevocationInstallation.InstallServiceTokenAsync(control, request, baseline));
        await using var tenant = await TenantStoragePostgresFixture.OpenRawAsync(login, server.PasswordA, database);
        Assert.Equal(database, await TenantStoragePostgresFixture.Scalar(tenant, "SELECT current_database()"));
        Assert.Equal(login, await TenantStoragePostgresFixture.Scalar(tenant, "SELECT session_user::text"));
        Assert.Equal(TenantStorageContext.A, await TenantStoragePostgresFixture.Scalar(tenant, "SELECT cp6_tenancy_v1.current_tenant_id()"));
    }
}
