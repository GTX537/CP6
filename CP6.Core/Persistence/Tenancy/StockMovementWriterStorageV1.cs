using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Sys;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal static class StockMovementWriterStorageV1
{
    internal const string Contract = "stock-movement-writer-v1";
    private static readonly (string Schema, string Name)[] Tables =
    [
        ("public", "T_Warehouse"), ("public", "T_StockTake"), ("public", "T_StockTakeDetail"), ("public", "T_WmsSequence"),
        ("cp6_stock_move_v1", "writer_bindings"), ("cp6_stock_return_v1", "movement_sequences"),
        ("cp6_authentication_v1", "identity_admission_revisions"), ("cp6_warehouse_draft_v1", "warehouses")
    ];
    private const string Tenant = """
        "TenantId"=(SELECT cp6_tenancy_v1.current_tenant_id())
        """;
    private const string NativeTenant = "tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id())";
    private const string Sequence = """
        "Prefix"='TXN' AND "DateKey"='*'
        """;

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var bindings = writers.ToArray();
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            bindings, requireFence: true, ct);
        var tables = Tables.Select(table => $"{table.Schema}.\"{table.Name}\"").ToArray();
        await Execute("LOCK TABLE " + string.Join(", ", tables) + " IN ACCESS EXCLUSIVE MODE");
        await using (var layout = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname||'.'||c.relname=ANY(@tables)
                  AND c.relkind='r' AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid=c.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee<>c.relowner)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute col, LATERAL pg_catalog.aclexplode(col.attacl) a
                        WHERE col.attrelid=c.oid AND a.grantee<>c.relowner))=cardinality(@tables)
              AND EXISTS (SELECT FROM public."__EFMigrationsHistory"
                    WHERE "MigrationId"='20261109023800_StockWriterGuardDispatchV1')
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_roles r WHERE r.rolname::text=ANY(@logins)
                  AND (pg_catalog.has_function_privilege(r.oid, pg_catalog.to_regprocedure(
                         'cp6_stock_outbox_writer_v1.lock_tenant(uuid)'), 'EXECUTE')
                    AND pg_catalog.has_table_privilege(r.oid, 'cp6_stock_outbox_v1."Cp6_OutboxMessage"', 'INSERT')) IS DISTINCT FROM true)
            """, connection, transaction))
        {
            layout.Parameters.AddWithValue("tables", Tables.Select(table => table.Schema + "." + table.Name).ToArray());
            layout.Parameters.AddWithValue("logins", bindings.Select(binding => binding.Login).ToArray());
            if (await layout.ExecuteScalarAsync(ct) is not true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }
        foreach (var table in tables)
            await Execute($"""
                ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                CREATE POLICY cp6_stock_movement_control_v1 ON {table} TO CURRENT_USER USING (true) WITH CHECK (true);
                """);

        await Execute($$"""
            CREATE ROLE cp6_stock_movement_executor_v1
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            CREATE SCHEMA cp6_stock_movement_writer_v1;
            REVOKE ALL ON SCHEMA cp6_stock_movement_writer_v1 FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_stock_movement_writer_v1, cp6_stock_move_v1, cp6_stock_return_v1,
                cp6_authentication_v1, cp6_storage_core, cp6_warehouse_draft_v1 TO {{roles}};
            GRANT USAGE ON SEQUENCE cp6_storage_core.rowversion_v1 TO {{roles}};

            CREATE POLICY cp6_stock_movement_insert_v1 ON public."T_Stock" FOR INSERT TO {{roles}} WITH CHECK ({{Tenant}});
            CREATE POLICY cp6_stock_movement_update_v1 ON public."T_Stock" FOR UPDATE TO {{roles}}
                USING ({{Tenant}}) WITH CHECK ({{Tenant}});
            CREATE POLICY cp6_stock_movement_insert_v1 ON public."T_StockTransaction" FOR INSERT TO {{roles}} WITH CHECK ({{Tenant}});
            GRANT INSERT ON public."T_Stock", public."T_StockTransaction" TO {{roles}};
            GRANT UPDATE ("PhysicalQty", "AllocatedQty", "AvailableQty", "Modifier", "ModifyDate") ON public."T_Stock" TO {{roles}};

            CREATE POLICY cp6_stock_movement_select_v1 ON public."T_Warehouse" FOR SELECT TO {{roles}} USING ({{Tenant}});
            CREATE POLICY cp6_stock_movement_select_v1 ON public."T_StockTake" FOR SELECT TO {{roles}} USING ({{Tenant}});
            CREATE POLICY cp6_stock_movement_select_v1 ON public."T_StockTakeDetail" FOR SELECT TO {{roles}} USING ({{Tenant}});
            GRANT SELECT ("Id","TenantId","WarehouseCd","AllowNegative","IsDeleted") ON public."T_Warehouse" TO {{roles}};
            CREATE POLICY cp6_stock_movement_select_v1 ON cp6_warehouse_draft_v1.warehouses FOR SELECT TO {{roles}}
                USING ({{NativeTenant}});
            GRANT SELECT (id,tenant_id) ON cp6_warehouse_draft_v1.warehouses TO {{roles}};
            GRANT SELECT ("TenantId","StockTakeNo","Status","IsDeleted") ON public."T_StockTake" TO {{roles}};
            GRANT SELECT ("TenantId","StockTakeNo","WarehouseCd","LocationCd","IsDeleted") ON public."T_StockTakeDetail" TO {{roles}};
            GRANT SELECT ("IsBlocked") ON public."T_Location" TO {{roles}};

            CREATE POLICY cp6_stock_movement_select_v1 ON public."T_WmsSequence" FOR SELECT TO {{roles}}
                USING ({{Tenant}} AND {{Sequence}});
            CREATE POLICY cp6_stock_movement_insert_v1 ON public."T_WmsSequence" FOR INSERT TO {{roles}}
                WITH CHECK ({{Tenant}} AND {{Sequence}});
            CREATE POLICY cp6_stock_movement_update_v1 ON public."T_WmsSequence" FOR UPDATE TO {{roles}}
                USING ({{Tenant}} AND {{Sequence}}) WITH CHECK ({{Tenant}} AND {{Sequence}});
            GRANT SELECT, INSERT ON public."T_WmsSequence" TO {{roles}};
            GRANT UPDATE ("NextNo") ON public."T_WmsSequence" TO {{roles}};
            CREATE POLICY cp6_stock_movement_insert_v1 ON cp6_stock_return_v1.movement_sequences FOR INSERT TO {{roles}}
                WITH CHECK ({{NativeTenant}});
            GRANT INSERT (tenant_id,pool_id,movement_id) ON cp6_stock_return_v1.movement_sequences TO {{roles}};
            CREATE POLICY cp6_stock_movement_select_v1 ON cp6_stock_move_v1.writer_bindings FOR SELECT TO {{roles}}
                USING ({{NativeTenant}});
            GRANT SELECT (tenant_id,warehouse_code,location_code,product_code) ON cp6_stock_move_v1.writer_bindings TO {{roles}};

            CREATE POLICY cp6_stock_movement_select_v1 ON cp6_authentication_v1.identity_admission_revisions FOR SELECT TO {{roles}}
                USING ({{NativeTenant}} AND object_key IN ('tree','order-department-mapping','standalone-department-mapping'));
            GRANT SELECT (tenant_id,object_key,revision) ON cp6_authentication_v1.identity_admission_revisions TO {{roles}};
            GRANT SELECT (id,revision) ON cp6_operations_owner_v1.department_mappings TO {{roles}};

            GRANT USAGE ON SCHEMA public, cp6_tenancy_v1, cp6_authentication_v1, cp6_platform_admin_guard_v1,
                cp6_operations_owner_v1, cp6_privacy_security_fields_v1 TO cp6_stock_movement_executor_v1;
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id(), cp6_authentication_v1.acquire(),
                cp6_platform_admin_guard_v1.acquire(), cp6_authentication_v1.bump_admission_revision(uuid,text)
                TO cp6_stock_movement_executor_v1;
            GRANT SELECT (singleton,revision), UPDATE (revision) ON cp6_platform_admin_guard_v1.root TO cp6_stock_movement_executor_v1;
            CREATE POLICY cp6_stock_movement_lineage_v1 ON cp6_operations_owner_v1.department_mappings FOR INSERT
                TO cp6_stock_movement_executor_v1 WITH CHECK (object_kind='stock-transaction');
            GRANT INSERT (id,tenant_id,object_kind,object_id,department_id,enabled) ON cp6_operations_owner_v1.department_mappings
                TO cp6_stock_movement_executor_v1;
            CREATE POLICY cp6_stock_movement_lineage_read_v1 ON cp6_operations_owner_v1.department_mappings FOR SELECT
                TO cp6_stock_movement_executor_v1 USING ({{NativeTenant}} AND object_kind='stock-transaction');
            GRANT SELECT (id,tenant_id,object_id,revision,xmin) ON cp6_operations_owner_v1.department_mappings
                TO cp6_stock_movement_executor_v1;

            -- These projections are the existing native mapping trigger's fixed relation dependencies.
            CREATE POLICY cp6_stock_movement_lineage_read_v1 ON public."Sys_Dept" FOR SELECT TO cp6_stock_movement_executor_v1 USING ({{Tenant}});
            CREATE POLICY cp6_stock_movement_lineage_read_v1 ON public."T_StockTransaction" FOR SELECT TO cp6_stock_movement_executor_v1 USING ({{Tenant}});
            GRANT SELECT ("Id","TenantId") ON public."Sys_Dept" TO cp6_stock_movement_executor_v1;
            GRANT SELECT ("Id","TenantId","IsDeleted",xmin) ON public."T_StockTransaction" TO cp6_stock_movement_executor_v1;

            CREATE POLICY cp6_stock_movement_revision_v1 ON cp6_authentication_v1.identity_admission_revisions
                TO cp6_stock_movement_executor_v1
                USING ({{NativeTenant}} AND object_key='standalone-department-mapping')
                WITH CHECK ({{NativeTenant}} AND object_key='standalone-department-mapping');
            GRANT SELECT (tenant_id,object_key,revision), INSERT (tenant_id,object_key,revision), UPDATE (revision)
                ON cp6_authentication_v1.identity_admission_revisions TO cp6_stock_movement_executor_v1;

            GRANT SELECT ON cp6_privacy_security_fields_v1.tombstones TO cp6_stock_movement_executor_v1;
            GRANT INSERT ("Id","TenantId","UserId","UserName","EventType","CreatedAt","Reason","ContextJson","CreateDate")
                ON public."Sys_SecurityLogs" TO cp6_stock_movement_executor_v1;

            CREATE FUNCTION cp6_stock_movement_writer_v1.record_department(expected uuid, mapping uuid, movement uuid, department uuid)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                SELECT mapping,expected,'stock-transaction',movement,department,true
                WHERE expected=cp6_tenancy_v1.current_tenant_id() AND EXISTS (
                    SELECT FROM public."T_StockTransaction" t WHERE t."TenantId"=expected AND t."Id"=movement
                        AND t.xmin=pg_current_xact_id_if_assigned()::text::xid);
                IF NOT FOUND THEN RAISE EXCEPTION 'Stock movement tenant binding rejected' USING ERRCODE='42501'; END IF;
            END $cp6$;
            CREATE FUNCTION cp6_stock_movement_writer_v1.record_audit(expected uuid, actor uuid, actor_name text,
                created_at timestamp without time zone, create_date timestamp without time zone, context jsonb)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                INSERT INTO public."Sys_SecurityLogs"("Id","TenantId","UserId","UserName","EventType","CreatedAt","Reason","ContextJson","CreateDate")
                SELECT gen_random_uuid(),expected,actor,actor_name,{{(int)SecurityEventType.IamAdministrationChanged}},
                    created_at,'{{StockTransactionInheritanceV1.AuditReason}}',context,create_date
                WHERE expected=cp6_tenancy_v1.current_tenant_id() AND context->>'kind'='stock-transaction-inheritance.v1'
                    AND EXISTS (SELECT FROM cp6_operations_owner_v1.department_mappings m WHERE m.tenant_id=expected
                        AND m.id=(context->>'mappingId')::uuid AND m.object_id=(context->>'transactionId')::uuid
                        AND m.revision=(context->>'mappingRevision')::uuid AND m.xmin=pg_current_xact_id_if_assigned()::text::xid);
                IF NOT FOUND THEN RAISE EXCEPTION 'Stock movement audit rejected' USING ERRCODE='42501'; END IF;
            END $cp6$;
            ALTER FUNCTION cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid) OWNER TO cp6_stock_movement_executor_v1;
            ALTER FUNCTION cp6_stock_movement_writer_v1.record_audit(uuid,uuid,text,timestamp,timestamp,jsonb) OWNER TO cp6_stock_movement_executor_v1;
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_stock_movement_writer_v1 FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid),
                cp6_stock_movement_writer_v1.record_audit(uuid,uuid,text,timestamp,timestamp,jsonb) TO {{roles}};
            """);

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
