using CP6.Core.Services.Integration.StockOutbox;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Scoped ordinary-login access to the existing, immutable Main Stock originals.</summary>
internal static class StockOriginalJournalStorageV1
{
    internal const string Contract = "stock-original-journal-v1";

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            writers, requireFence: true, ct);
        await using (var gate = new NpgsqlCommand("LOCK TABLE cp6_main_owner_v1.operations IN ACCESS EXCLUSIVE MODE", connection, transaction))
            await gate.ExecuteNonQueryAsync(ct);
        await using (var layout = new NpgsqlCommand("""
            SELECT c.relkind='r' AND c.relowner=current_user::regrole
              AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid=c.oid)
              AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute col, LATERAL pg_catalog.aclexplode(col.attacl) a
                    WHERE col.attrelid=c.oid AND a.grantee<>c.relowner)
              AND EXISTS (SELECT FROM public."__EFMigrationsHistory"
                    WHERE "MigrationId"='20261109014500_LocationWithdrawalJournalDispatchV1')
            FROM pg_catalog.pg_class c WHERE c.oid='cp6_main_owner_v1.operations'::regclass
            """, connection, transaction))
        {
            if (await layout.ExecuteScalarAsync(ct) is not true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }

        await using var install = new NpgsqlCommand($$"""
            CREATE ROLE cp6_stock_original_journal_executor_v1
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE SCHEMA cp6_stock_original_journal_v1;
            REVOKE ALL ON SCHEMA cp6_stock_original_journal_v1 FROM PUBLIC;

            ALTER TABLE cp6_main_owner_v1.operations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE cp6_main_owner_v1.operations FORCE ROW LEVEL SECURITY;
            CREATE POLICY cp6_stock_journal_control_v1 ON cp6_main_owner_v1.operations
                TO CURRENT_USER USING (true) WITH CHECK (true);
            CREATE POLICY cp6_stock_journal_read_v1 ON cp6_main_owner_v1.operations FOR SELECT TO {{roles}}
                USING (tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id()) AND owner='{{StockOutboxProtocolV1.SourceOwner}}');
            CREATE POLICY cp6_stock_journal_append_v1 ON cp6_main_owner_v1.operations FOR INSERT
                TO cp6_stock_original_journal_executor_v1 WITH CHECK (
                    tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id()) AND owner='{{StockOutboxProtocolV1.SourceOwner}}'
                    AND contract IN ('{{StockOutboxProtocolV1.SourceContract}}', '{{StockOutboxProtocolV1.CreationSourceContract}}'));

            GRANT USAGE ON SCHEMA cp6_main_owner_v1, cp6_stock_original_journal_v1 TO {{roles}};
            GRANT SELECT (tenant_id, owner, operation_id, actor_id, contract, state, input_bytes, input_sha256, result_bytes, object_ids)
                ON cp6_main_owner_v1.operations TO {{roles}};
            GRANT USAGE ON SCHEMA cp6_main_owner_v1, cp6_authentication_v1, cp6_platform_admin_guard_v1, cp6_tenancy_v1
                TO cp6_stock_original_journal_executor_v1;
            GRANT EXECUTE ON FUNCTION cp6_authentication_v1.acquire(), cp6_platform_admin_guard_v1.acquire(),
                cp6_tenancy_v1.current_tenant_id() TO cp6_stock_original_journal_executor_v1;
            GRANT SELECT (singleton, revision), UPDATE (revision) ON cp6_platform_admin_guard_v1.root
                TO cp6_stock_original_journal_executor_v1;
            GRANT INSERT (tenant_id, owner, operation_id, actor_id, contract, state, input_bytes, input_sha256,
                result_bytes, object_ids, audit_json) ON cp6_main_owner_v1.operations TO cp6_stock_original_journal_executor_v1;

            CREATE FUNCTION cp6_stock_original_journal_v1.store(
                tenant uuid, operation uuid, actor uuid, contract_version text, status text, input bytea,
                digest text, result bytea, objects uuid[], audit jsonb)
            RETURNS void LANGUAGE sql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
                INSERT INTO cp6_main_owner_v1.operations
                    (tenant_id,owner,operation_id,actor_id,contract,state,input_bytes,input_sha256,result_bytes,object_ids,audit_json)
                VALUES ($1,'{{StockOutboxProtocolV1.SourceOwner}}',$2,$3,$4,$5,$6,$7,$8,$9,$10);
            $cp6$;
            ALTER FUNCTION cp6_stock_original_journal_v1.store(uuid,uuid,uuid,text,text,bytea,text,bytea,uuid[],jsonb)
                OWNER TO cp6_stock_original_journal_executor_v1;
            REVOKE ALL ON FUNCTION cp6_stock_original_journal_v1.store(uuid,uuid,uuid,text,text,bytea,text,bytea,uuid[],jsonb) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION cp6_stock_original_journal_v1.store(uuid,uuid,uuid,text,text,bytea,text,bytea,uuid[],jsonb) TO {{roles}};
            """, connection, transaction);
        await install.ExecuteNonQueryAsync(ct);
    }
}
