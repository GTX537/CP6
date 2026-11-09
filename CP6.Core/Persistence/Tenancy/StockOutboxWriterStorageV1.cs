using CP6.Core.Services.Integration.StockOutbox;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Explicitly prepares the existing native Stock route and its enqueue grants on the isolated layout.</summary>
internal static class StockOutboxWriterStorageV1
{
    internal const string Contract = "stock-outbox-writer-v1";
    private static readonly string[] Tables = ["Authority", "Cp6_OutboxRouteV2", "Cp6_OutboxMessage", "Cp6_OutboxBindingV2", "Source"];
    private const string Tenant = """
        "TenantId"=(SELECT cp6_tenancy_v1.current_tenant_id())
        """;
    private const string BindingRoute = """
        EXISTS (SELECT FROM cp6_stock_outbox_v1."Authority" a
                WHERE a."RootId"="Cp6_OutboxBindingV2"."RootId")
        """;

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var bindings = writers.ToArray();
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            bindings, requireFence: true, ct);
        var tables = Tables.Select(table => $"cp6_stock_outbox_v1.\"{table}\"").ToArray();
        await Execute("LOCK TABLE " + string.Join(", ", tables) + " IN ACCESS EXCLUSIVE MODE");
        await using (var layout = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pg_catalog.pg_class c
                WHERE c.relnamespace='cp6_stock_outbox_v1'::regnamespace AND c.relname::text=ANY(@tables)
                  AND c.relkind='r' AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid=c.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute col, LATERAL pg_catalog.aclexplode(col.attacl) a
                        WHERE col.attrelid=c.oid AND a.grantee<>c.relowner))=cardinality(@tables)
              AND (SELECT NOT relrowsecurity FROM pg_catalog.pg_class WHERE oid='public."Sys_Tenants"'::regclass)
              AND NOT EXISTS (
                SELECT FROM pg_catalog.pg_roles r WHERE r.rolname::text=ANY(@logins)
                  AND (pg_catalog.has_function_privilege(r.oid, pg_catalog.to_regprocedure(
                         'cp6_stock_original_journal_v1.store(uuid,uuid,uuid,text,text,bytea,text,bytea,uuid[],jsonb)'), 'EXECUTE')
                    AND pg_catalog.has_column_privilege(r.oid, 'cp6_main_owner_v1.operations', 'result_bytes', 'SELECT')) IS DISTINCT FROM true)
            """, connection, transaction))
        {
            layout.Parameters.AddWithValue("tables", Tables);
            layout.Parameters.AddWithValue("logins", bindings.Select(binding => binding.Login).ToArray());
            if (await layout.ExecuteScalarAsync(ct) is not true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }

        await using var queue = new StockOutboxContextV1(new DbContextOptionsBuilder<StockOutboxContextV1>()
            .UseNpgsql(connection, contextOwnsConnection: false).Options);
        await queue.Database.UseTransactionAsync(transaction, ct);
        foreach (var binding in bindings)
        {
            await using var tenant = new NpgsqlCommand("""
                SELECT "Id" FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE
                """, connection, transaction);
            tenant.Parameters.AddWithValue("tenant", binding.TenantId);
            if (await tenant.ExecuteScalarAsync(ct) is not Guid)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
            await StockOutboxEnqueueV1.EnsureRouteAsync(queue, StockOutboxProtocolV1.Root(binding.TenantId), ct);
        }

        foreach (var table in tables)
            await Execute($"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY cp6_stock_queue_control_v1 ON {table} TO CURRENT_USER USING (true) WITH CHECK (true);
                """);

        await Execute($$"""
            CREATE ROLE cp6_stock_outbox_tenant_lock_v1
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE SCHEMA cp6_stock_outbox_writer_v1;
            REVOKE ALL ON SCHEMA cp6_stock_outbox_writer_v1 FROM PUBLIC;
            GRANT USAGE ON SCHEMA public, cp6_tenancy_v1 TO cp6_stock_outbox_tenant_lock_v1;
            GRANT SELECT ("Id"), UPDATE ("Id") ON public."Sys_Tenants" TO cp6_stock_outbox_tenant_lock_v1;
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO cp6_stock_outbox_tenant_lock_v1;
            CREATE FUNCTION cp6_stock_outbox_writer_v1.lock_tenant(expected_tenant uuid)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                IF cp6_tenancy_v1.current_tenant_id() IS DISTINCT FROM expected_tenant THEN
                    RAISE EXCEPTION 'Stock outbox tenant binding rejected' USING ERRCODE='42501';
                END IF;
                PERFORM 1 FROM public."Sys_Tenants" WHERE "Id"=expected_tenant FOR UPDATE;
                IF NOT FOUND THEN RAISE EXCEPTION 'STOCK_OUTBOX_ROUTE_UNAVAILABLE'; END IF;
            END $cp6$;
            ALTER FUNCTION cp6_stock_outbox_writer_v1.lock_tenant(uuid) OWNER TO cp6_stock_outbox_tenant_lock_v1;
            REVOKE ALL ON FUNCTION cp6_stock_outbox_writer_v1.lock_tenant(uuid) FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_stock_outbox_v1, cp6_stock_outbox_writer_v1 TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_stock_outbox_writer_v1.lock_tenant(uuid) TO {{roles}};
            GRANT SELECT (xmin) ON cp6_main_owner_v1.operations TO {{roles}};

            CREATE POLICY cp6_stock_queue_read_v1 ON cp6_stock_outbox_v1."Authority" FOR SELECT TO {{roles}} USING ({{Tenant}});
            CREATE POLICY cp6_stock_queue_read_v1 ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2" FOR SELECT TO {{roles}} USING ({{Tenant}});
            -- PostgreSQL row locking requires UPDATE on a column. The policies reject replacement rows.
            CREATE POLICY cp6_stock_queue_lock_v1 ON cp6_stock_outbox_v1."Authority" FOR UPDATE TO {{roles}}
                USING ({{Tenant}}) WITH CHECK (false);
            CREATE POLICY cp6_stock_queue_lock_v1 ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2" FOR UPDATE TO {{roles}}
                USING ({{Tenant}}) WITH CHECK (false);
            GRANT SELECT ON cp6_stock_outbox_v1."Authority", cp6_stock_outbox_v1."Cp6_OutboxRouteV2" TO {{roles}};
            GRANT UPDATE ("RootId") ON cp6_stock_outbox_v1."Authority" TO {{roles}};
            GRANT UPDATE ("Id") ON cp6_stock_outbox_v1."Cp6_OutboxRouteV2" TO {{roles}};

            CREATE POLICY cp6_stock_queue_read_v1 ON cp6_stock_outbox_v1."Cp6_OutboxMessage" FOR SELECT TO {{roles}} USING ({{Tenant}});
            CREATE POLICY cp6_stock_queue_append_v1 ON cp6_stock_outbox_v1."Cp6_OutboxMessage" FOR INSERT TO {{roles}} WITH CHECK ({{Tenant}});
            GRANT SELECT ("Id", "MessageId", "TenantId", xmin), INSERT ON cp6_stock_outbox_v1."Cp6_OutboxMessage" TO {{roles}};

            CREATE POLICY cp6_stock_queue_read_v1 ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2" FOR SELECT TO {{roles}} USING ({{BindingRoute}});
            CREATE POLICY cp6_stock_queue_append_v1 ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2" FOR INSERT TO {{roles}}
                WITH CHECK ({{BindingRoute}} AND EXISTS (SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" m
                    WHERE m."Id"="Cp6_OutboxBindingV2"."NativeId" AND m."MessageId"="Cp6_OutboxBindingV2"."MessageId"));
            GRANT SELECT ("NativeId", "RootId", "MessageId", "RawEnvelopeSha256", "AssignedEpoch", "CurrentClaimId", "ClaimEpoch", xmin), INSERT
                ON cp6_stock_outbox_v1."Cp6_OutboxBindingV2" TO {{roles}};

            CREATE POLICY cp6_stock_queue_read_v1 ON cp6_stock_outbox_v1."Source" FOR SELECT TO {{roles}} USING ({{Tenant}});
            CREATE POLICY cp6_stock_queue_append_v1 ON cp6_stock_outbox_v1."Source" FOR INSERT TO {{roles}}
                WITH CHECK ({{Tenant}} AND EXISTS (SELECT FROM cp6_stock_outbox_v1."Cp6_OutboxMessage" m WHERE m."Id"="Source"."NativeId"));
            GRANT SELECT, INSERT ON cp6_stock_outbox_v1."Source" TO {{roles}};
            """);

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
