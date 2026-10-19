using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Tests.Platform;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[Collection("IdentityDirectoryPostgres")]
public sealed class IdentityDirectoryPostgresTests(IdentityDirectoryPostgresFixture fixture)
{
    [CurrentPermissionPostgresFact]
    public async Task Ordinary_logins_read_only_their_directory_even_without_a_tenant_predicate()
    {
        foreach (var tenant in new[] { A, B })
        {
            await using var connection = await fixture.OpenRuntime(tenant);
            await TenantConnectionGuard.ValidateAsync(connection, TenantManagementPostgresFixture.Database,
                tenant == A ? "directory_a" : "directory_b", default);
            foreach (var table in new[] { "crm_identity.\"Bootstrap\"", "crm_identity.\"Snapshot\"", "public.\"CrmIdentityTenantGenerations\"" })
            {
                var ids = await Scalar<Guid[]>(connection, $"SELECT array_agg(DISTINCT \"TenantId\") FROM {table}");
                Assert.Equal(new[] { tenant }, ids);
                Assert.Equal(0L, await Scalar<long>(connection, $"SELECT count(*) FROM {table} WHERE \"TenantId\"='{(tenant == A ? B : A):D}'::uuid"));
            }
            Assert.Equal(1L, await Scalar<long>(connection, "SELECT count(*) FROM crm_identity.\"Snapshot\" WHERE \"AggregateId\"='permission:role:777'"));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Directory_mutations_other_tables_functions_and_role_escalation_are_denied()
    {
        await using var connection = await fixture.OpenRuntime(A);
        foreach (var table in IdentityDirectoryRowSecurityV1.Tables)
        {
            foreach (var sql in new[]
            {
                $"INSERT INTO {table}(\"TenantId\") VALUES('{A:D}')",
                $"UPDATE {table} SET \"TenantId\"=\"TenantId\"",
                $"DELETE FROM {table}", $"TRUNCATE {table}"
            }) await Denied(connection, sql);
            // PG can report a warning/no-op GRANT when a role holds SELECT but no grant option.
            await Sql(connection, $"GRANT UPDATE ON {table} TO directory_a");
            Assert.False(await Scalar<bool>(connection, $"SELECT has_table_privilege(current_user,'{table}','UPDATE')"));
        }
        foreach (var sql in new[]
        {
            "SELECT * FROM cp6_tenancy_v1.login_tenant",
            "SELECT * FROM public.\"Sys_Users\"",
            "SELECT * FROM crm_identity.\"ServiceToken\"",
            "SELECT * FROM crm_identity.\"Cp6_OutboxMessage\"",
            "SELECT * FROM crm_identity_priority.\"Cp6_OutboxMessage\"",
            "SELECT nextval('cp6_storage_core.rowversion_v1')",
            "SELECT * FROM public.cp6_mes_process_progress_v1()",
            "SET ROLE fixture_owner", "SET ROLE cp6_tenant_lookup_v1", "SET ROLE directory_b",
            "CREATE TABLE public.runtime_probe(id integer)", "CREATE TEMP TABLE runtime_probe(id integer)"
        }) await Denied(connection, sql);
        await Sql(connection, "SET row_security=off");
        await Denied(connection, "SELECT * FROM crm_identity.\"Snapshot\"");
        await Sql(connection, "RESET row_security");
        await Sql(connection, $"SET cp6.tenant_id='{B:D}'");
        Assert.Equal(new[] { A }, await Scalar<Guid[]>(connection, "SELECT array_agg(DISTINCT \"TenantId\") FROM crm_identity.\"Snapshot\""));
    }

    [CurrentPermissionPostgresFact]
    public async Task Missing_or_renamed_login_mapping_rejects_the_existing_runtime_connection()
    {
        await using var owner = await fixture.OpenOwner();
        await using var runtime = await fixture.OpenRuntime(A);
        try
        {
            await Sql(owner, "ALTER ROLE directory_a RENAME TO directory_a_renamed");
            await Denied(runtime, "SELECT * FROM crm_identity.\"Snapshot\"");
        }
        finally { await Sql(owner, "ALTER ROLE directory_a_renamed RENAME TO directory_a"); }
        try
        {
            await Sql(owner, "DELETE FROM cp6_tenancy_v1.login_tenant WHERE role_name='directory_a'");
            await Denied(runtime, "SELECT * FROM crm_identity.\"Snapshot\"");
        }
        finally
        {
            await Sql(owner, $"INSERT INTO cp6_tenancy_v1.login_tenant SELECT oid,rolname,'{A:D}'::uuid FROM pg_roles WHERE rolname='directory_a'");
        }
        Assert.Equal(A, await Scalar<Guid>(runtime, "SELECT cp6_tenancy_v1.current_tenant_id()"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Explicit_control_owner_keeps_writes_and_version_triggers_under_force_rls()
    {
        await using var owner = await fixture.OpenOwner();
        await using var tx = await owner.BeginTransactionAsync();
        // PG18 protects the bootstrap superuser's attribute. Use a temporary ordinary control
        // member instead: object ownership is inherited, SUPERUSER/BYPASSRLS are not.
        // No runtime login joins this role; transaction rollback removes the probe entirely.
        await Sql(owner, "CREATE ROLE directory_control_probe NOLOGIN NOSUPERUSER NOBYPASSRLS INHERIT");
        await Sql(owner, "GRANT fixture_owner TO directory_control_probe WITH INHERIT TRUE, SET FALSE");
        await Sql(owner, "SET LOCAL ROLE directory_control_probe");
        Assert.False(await Scalar<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname=current_user"));
        Assert.True(await Scalar<bool>(owner, "SELECT pg_catalog.row_security_active('crm_identity.\"Snapshot\"'::regclass)"));
        var token = await Scalar<byte[]>(owner, $"SELECT \"RowVersion\" FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"='{A:D}' AND \"AggregateId\"='permission:role:777'");
        var generation = await Scalar<long>(owner, $"SELECT \"Generation\" FROM public.\"CrmIdentityTenantGenerations\" WHERE \"TenantId\"='{A:D}'");
        Assert.Equal(4L, await Scalar<long>(owner, "SELECT count(*) FROM crm_identity.\"Snapshot\""));
        await Sql(owner, $"UPDATE crm_identity.\"Snapshot\" SET \"UpdatedAtUtc\"=clock_timestamp() WHERE \"TenantId\"='{A:D}' AND \"AggregateId\"='permission:role:777'");
        Assert.NotEqual(token, await Scalar<byte[]>(owner, $"SELECT \"RowVersion\" FROM crm_identity.\"Snapshot\" WHERE \"TenantId\"='{A:D}' AND \"AggregateId\"='permission:role:777'"));
        Assert.Equal(generation + 1, await Scalar<long>(owner, $"SELECT \"Generation\" FROM public.\"CrmIdentityTenantGenerations\" WHERE \"TenantId\"='{A:D}'"));
        await tx.RollbackAsync();
        Assert.True(await Scalar<bool>(owner, "SELECT rolsuper FROM pg_roles WHERE rolname=current_user"));
        Assert.Equal(generation, await Scalar<long>(owner, $"SELECT \"Generation\" FROM public.\"CrmIdentityTenantGenerations\" WHERE \"TenantId\"='{A:D}'"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Reinstallation_and_runtime_control_attempts_are_rejected()
    {
        await using (var owner = await fixture.OpenOwner())
        await using (var tx = await owner.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => IdentityDirectoryRowSecurityV1.InstallAsync(owner, tx, Logins));
            await tx.RollbackAsync();
        }
        await using var runtime = await fixture.OpenRuntime(A);
        await using var attempt = await runtime.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => IdentityDirectoryRowSecurityV1.InstallAsync(runtime, attempt, Logins));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        await attempt.RollbackAsync();
        Assert.Equal(A, await Scalar<Guid>(runtime, "SELECT cp6_tenancy_v1.current_tenant_id()"));
    }

    private static async Task Denied(NpgsqlConnection connection, string sql)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Sql(connection, sql));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
