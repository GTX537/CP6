using CP6.Core.Persistence.Tenancy;
using CP6.Persistence.PostgreSql.Tenancy;
using Npgsql;

namespace CP6.Tests.Persistence.Tenancy;

// Separate collection/owned run: fresh-install rejection must not be masked by an already-existing helper role.
[Collection("TenantStoragePostgres")]
public sealed class ServiceRevocationFreshInstallPostgresTests(TenantStoragePostgresFixture postgres)
{
    [TenantStoragePostgresFact]
    public async Task Fresh_install_rejects_unknown_system_objects_casts_and_operators_before_adoption()
    {
        const string database = "service_revocation_unknown";
        await using (var owner = await postgres.OpenOwnerAsync("postgres"))
        {
            await TenantStoragePostgresFixture.Sql(owner, "CREATE ROLE fresh_issuer LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION");
            await TenantStoragePostgresFixture.Sql(owner, "CREATE DATABASE " + database);
        }
        await using var control = await postgres.OpenOwnerAsync(database);
        var request = new ServiceRevocationInstallRequest(Guid.NewGuid(), "127.0.0.1", TenantStoragePostgresFixture.Port, database,
            (uint)(await TenantStoragePostgresFixture.Scalar(control, "SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!,
            (string)(await TenantStoragePostgresFixture.Scalar(control, "SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!,
            "fresh_issuer", [new(TenantStorageContext.A, "tenant_a"), new(TenantStorageContext.B, "tenant_b")]);
        var baseline = ServiceRevocationBaselineV1.GenerateSql();
        foreach (var (create, drop) in new[]
        {
            ("CREATE FUNCTION pg_catalog.unexpected() RETURNS integer LANGUAGE sql SECURITY DEFINER AS 'SELECT 1'", "DROP FUNCTION pg_catalog.unexpected()"),
            ("BEGIN; SET LOCAL allow_system_table_mods=on; CREATE TABLE pg_catalog.unexpected_table(id integer); COMMIT",
                "BEGIN; SET LOCAL allow_system_table_mods=on; DROP TABLE pg_catalog.unexpected_table; COMMIT"),
            ("BEGIN; SET LOCAL allow_system_table_mods=on; CREATE TYPE pg_catalog.unexpected_type AS ENUM ('only'); COMMIT",
                "BEGIN; SET LOCAL allow_system_table_mods=on; DROP TYPE pg_catalog.unexpected_type; COMMIT"),
            ("CREATE CAST (boolean AS uuid) WITH INOUT", "DROP CAST (boolean AS uuid)"),
            ("CREATE OPERATOR public.=== (LEFTARG=integer,RIGHTARG=integer,FUNCTION=pg_catalog.int4eq)", "DROP OPERATOR public.=== (integer,integer)")
        })
        {
            await AssertUninstalled();
            await TenantStoragePostgresFixture.Sql(control, create);
            Assert.Equal("off", await TenantStoragePostgresFixture.Scalar(control, "SHOW allow_system_table_mods"));
            try
            {
                await Assert.ThrowsAsync<TenantStorageException>(() => ServiceRevocationInstallation.InstallAsync(control, request, baseline));
                await AssertUninstalled();
            }
            finally { await TenantStoragePostgresFixture.Sql(control, drop); }
        }
        // Fresh ordinary issuance login must not bring a direct system-catalog grant into the profile.
        await TenantStoragePostgresFixture.Sql(control, "GRANT SELECT ON pg_catalog.pg_authid TO fresh_issuer");
        try
        {
            await Assert.ThrowsAsync<TenantStorageException>(() => ServiceRevocationInstallation.InstallAsync(control, request, baseline));
            await AssertUninstalled();
        }
        finally { await TenantStoragePostgresFixture.Sql(control, "REVOKE SELECT ON pg_catalog.pg_authid FROM fresh_issuer"); }
        // The same original request succeeds only after removing the unsupported input.
        var result = await ServiceRevocationInstallation.InstallAsync(control, request, baseline);
        Assert.Equal(request.InstallationId, result.InstallationId);
        Assert.Equal(result, await ServiceRevocationInstallation.InstallAsync(control, request, baseline));

        async Task AssertUninstalled()
        {
            Assert.Equal(false, await TenantStoragePostgresFixture.Scalar(control,
                "SELECT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='cp6_service_revocation_v1')"));
            Assert.Equal(false, await TenantStoragePostgresFixture.Scalar(control,
                "SELECT EXISTS(SELECT FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1')"));
        }
    }
}
