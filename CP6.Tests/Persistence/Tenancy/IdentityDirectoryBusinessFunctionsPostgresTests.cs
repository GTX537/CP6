using CP6.Core.Persistence.Tenancy;
using Npgsql;
using static CP6.Tests.Persistence.Tenancy.IdentityDirectoryPostgresFixture;

namespace CP6.Tests.Persistence.Tenancy;

[CollectionDefinition("DirectoryBusinessFunctionsPostgres",DisableParallelization=true)]
public sealed class DirectoryBusinessFunctionsCollection : ICollectionFixture<IdentityDirectoryInstallationPostgresFixture>;

[Collection("DirectoryBusinessFunctionsPostgres")]
public sealed class IdentityDirectoryBusinessFunctionsPostgresTests(IdentityDirectoryInstallationPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task Actual_migrated_owner_outbox_functions_allow_directory_install_without_any_reader_business_privileges()
    {
        await using var owner=await postgres.OpenOwner();await using var tx=await owner.BeginTransactionAsync();
        await IdentityDirectoryRowSecurityV1.InstallAsync(owner,tx,Logins);
        await using var command=new NpgsqlCommand("""
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname IN ('cp6_order_owner_v1','cp6_operations_owner_v1','cp6_oee_legacy_v1','cp6_main_owner_v1','cp6_stock_outbox_v1','cp6_inquiry_scope_v1')
              AND (has_function_privilege('directory_a',p.oid,'EXECUTE') OR has_function_privilege('directory_b',p.oid,'EXECUTE'))
            """,owner,tx);
        Assert.Equal(0L,await command.ExecuteScalarAsync());await tx.RollbackAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Unknown_or_modified_business_functions_and_public_execute_cannot_enter_the_source_profile()
    {
        await using var owner=await postgres.OpenOwner();
        foreach(var anomaly in new[]{
            "CREATE FUNCTION cp6_main_owner_v1.unknown() RETURNS int LANGUAGE sql AS 'SELECT 1'",
            "CREATE OR REPLACE FUNCTION cp6_main_owner_v1.immutable() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS 'BEGIN RETURN NULL; END'",
            "ALTER FUNCTION cp6_stock_outbox_v1.source_proof() SECURITY DEFINER",
            "CREATE OR REPLACE FUNCTION cp6_stock_outbox_v1.source_proof() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS 'BEGIN RETURN NEW; END'",
            "ALTER FUNCTION cp6_stock_outbox_v1.run_guard() SET search_path=public,pg_catalog",
            "CREATE OR REPLACE FUNCTION cp6_operations_owner_v1.mapping_validate() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS 'BEGIN RETURN NEW; END'",
            "GRANT EXECUTE ON FUNCTION cp6_oee_legacy_v1.validate() TO PUBLIC",
            "GRANT EXECUTE ON FUNCTION cp6_stock_outbox_v1.cp6_outbox_v2_guard() TO PUBLIC",
            "CREATE FUNCTION cp6_inquiry_scope_v1.unknown() RETURNS int LANGUAGE sql AS 'SELECT 1'",
            "CREATE OR REPLACE FUNCTION cp6_inquiry_scope_v1.changed() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS 'BEGIN RETURN NULL; END'",
            "CREATE OR REPLACE FUNCTION cp6_inquiry_scope_v1.require_snapshot() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS 'BEGIN RETURN NULL; END'",
            "ALTER FUNCTION cp6_inquiry_scope_v1.changed() SECURITY DEFINER",
            "ALTER FUNCTION cp6_inquiry_scope_v1.require_snapshot() SET search_path=public,pg_catalog",
            "GRANT EXECUTE ON FUNCTION cp6_inquiry_scope_v1.changed() TO PUBLIC",
            "GRANT EXECUTE ON FUNCTION cp6_inquiry_scope_v1.require_snapshot() TO PUBLIC"})
        {
            await using var tx=await owner.BeginTransactionAsync();await using var change=new NpgsqlCommand(anomaly,owner,tx);await change.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>IdentityDirectoryRowSecurityV1.InstallAsync(owner,tx,Logins));await tx.RollbackAsync();
        }
    }
}
