using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Rma_model_v2_shared_migration_creates_private_control_without_touching_the_original_journal()
    {
        await using var db = server.Context(new TenantContext());
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"=@migration)
                AND (SELECT count(*) FROM pg_class c
                    WHERE c.oid IN ('cp6_rma_model_v2.tenant_migrations'::regclass,
                                    'cp6_rma_model_v2.writer_transactions'::regclass)
                      AND c.relkind='r' AND c.relowner=current_user::regrole
                      AND NOT EXISTS (SELECT FROM aclexplode(c.relacl) acl WHERE acl.grantee<>c.relowner))=2
                AND NOT EXISTS (SELECT FROM cp6_rma_model_v2.tenant_migrations)
                AND NOT EXISTS (SELECT FROM cp6_rma_model_v2.writer_transactions)
                AND NOT EXISTS (SELECT FROM pg_trigger WHERE tgrelid='cp6_main_owner_v1.operations'::regclass
                    AND tgname='rma_model_v2_journal_writer')
            """, connection);
        command.Parameters.AddWithValue("migration", RmaTenantStorageV1.CoreReadSharedMigrationV2);
        Assert.True(await command.ExecuteScalarAsync() is true);
    }
}
