using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Sys;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal static class StockCreationWriterStorageV2
{
    internal const string Contract = "stock-creation-writer-v2";
    internal const string Migration = "20261109040000_StockCreationMappingDispatchV2";
    private const string Tenant = """
        "TenantId"=(SELECT cp6_tenancy_v1.current_tenant_id())
        """;
    private const string NativeTenant = "tenant_id=(SELECT cp6_tenancy_v1.current_tenant_id())";

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            writers, requireFence: true, ct);
        await using (var preflight = new NpgsqlCommand("""
            LOCK TABLE cp6_authentication_v1.identity_admission_revisions, cp6_operations_owner_v1.department_mappings,
                public."Sys_Dept", public."T_Stock" IN ACCESS EXCLUSIVE MODE;
            SELECT EXISTS (SELECT FROM public."__EFMigrationsHistory" WHERE "MigrationId"=@migration)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c WHERE c.oid IN (
                    'cp6_authentication_v1.identity_admission_revisions'::regclass,
                    'cp6_operations_owner_v1.department_mappings'::regclass,
                    'public."Sys_Dept"'::regclass, 'public."T_Stock"'::regclass)
                    AND NOT (c.relrowsecurity AND c.relforcerowsecurity))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_roles r WHERE r.rolname::text=ANY(@logins)
                  AND (pg_catalog.has_function_privilege(r.oid, pg_catalog.to_regprocedure(
                        'cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid)'), 'EXECUTE')
                    AND pg_catalog.has_column_privilege(r.oid, 'public."T_Stock"', 'Id', 'INSERT')) IS DISTINCT FROM true)
            """, connection, transaction))
        {
            preflight.Parameters.AddWithValue("migration", Migration);
            preflight.Parameters.AddWithValue("logins", writers.Select(binding => binding.Login).ToArray());
            if (await preflight.ExecuteScalarAsync(ct) is not true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }

        await using var install = new NpgsqlCommand($$"""
            GRANT SELECT ("DeptCode","DeptName","Sort") ON public."Sys_Dept" TO {{roles}};
            CREATE ROLE cp6_stock_creation_executor_v2
                NOLOGIN NOINHERIT NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
            GRANT USAGE ON SCHEMA public, cp6_tenancy_v1, cp6_authentication_v1, cp6_platform_admin_guard_v1,
                cp6_operations_owner_v1, cp6_privacy_security_fields_v1 TO cp6_stock_creation_executor_v2;
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id(), cp6_authentication_v1.acquire(),
                cp6_platform_admin_guard_v1.acquire(), cp6_authentication_v1.bump_admission_revision(uuid,text)
                TO cp6_stock_creation_executor_v2;
            GRANT SELECT (singleton,revision), UPDATE (revision) ON cp6_platform_admin_guard_v1.root TO cp6_stock_creation_executor_v2;

            CREATE POLICY cp6_stock_creation_insert_v2 ON cp6_operations_owner_v1.department_mappings FOR INSERT
                TO cp6_stock_creation_executor_v2 WITH CHECK ({{NativeTenant}} AND object_kind='stock');
            CREATE POLICY cp6_stock_creation_read_v2 ON cp6_operations_owner_v1.department_mappings FOR SELECT
                TO cp6_stock_creation_executor_v2 USING ({{NativeTenant}} AND object_kind='stock');
            GRANT INSERT (id,tenant_id,object_kind,object_id,department_id,enabled),
                SELECT (id,tenant_id,object_id,department_id,revision,xmin) ON cp6_operations_owner_v1.department_mappings
                TO cp6_stock_creation_executor_v2;
            CREATE POLICY cp6_stock_creation_read_v2 ON public."Sys_Dept" FOR SELECT TO cp6_stock_creation_executor_v2 USING ({{Tenant}});
            CREATE POLICY cp6_stock_creation_read_v2 ON public."T_Stock" FOR SELECT TO cp6_stock_creation_executor_v2 USING ({{Tenant}});
            GRANT SELECT ("Id","TenantId") ON public."Sys_Dept" TO cp6_stock_creation_executor_v2;
            GRANT SELECT ("Id","TenantId","IsDeleted") ON public."T_Stock" TO cp6_stock_creation_executor_v2;

            CREATE POLICY cp6_stock_creation_revision_v2 ON cp6_authentication_v1.identity_admission_revisions
                TO cp6_stock_creation_executor_v2
                USING ({{NativeTenant}} AND object_key='standalone-department-mapping')
                WITH CHECK ({{NativeTenant}} AND object_key='standalone-department-mapping');
            GRANT SELECT (tenant_id,object_key,revision), INSERT (tenant_id,object_key,revision), UPDATE (revision)
                ON cp6_authentication_v1.identity_admission_revisions TO cp6_stock_creation_executor_v2;
            GRANT SELECT ON cp6_privacy_security_fields_v1.tombstones TO cp6_stock_creation_executor_v2;
            GRANT INSERT ("Id","TenantId","UserId","UserName","EventType","CreatedAt","Reason","ContextJson","CreateDate")
                ON public."Sys_SecurityLogs" TO cp6_stock_creation_executor_v2;

            CREATE SCHEMA cp6_stock_creation_writer_v2;
            REVOKE ALL ON SCHEMA cp6_stock_creation_writer_v2 FROM PUBLIC;
            GRANT USAGE ON SCHEMA cp6_stock_creation_writer_v2 TO cp6_stock_creation_executor_v2, {{roles}};
            CREATE TABLE cp6_stock_creation_writer_v2.inserted_stock (
                tenant_id uuid NOT NULL, stock_id uuid NOT NULL, PRIMARY KEY (tenant_id,stock_id));
            GRANT INSERT (tenant_id,stock_id), SELECT (tenant_id,stock_id), DELETE
                ON cp6_stock_creation_writer_v2.inserted_stock TO cp6_stock_creation_executor_v2;
            CREATE FUNCTION cp6_stock_creation_writer_v2.note_insert() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                IF EXISTS (SELECT FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a
                    JOIN pg_roles r ON r.oid=a.grantee
                    WHERE p.oid='cp6_stock_creation_writer_v2.record_department(uuid,uuid,uuid,uuid)'::regprocedure
                        AND a.privilege_type='EXECUTE' AND r.rolname=session_user) THEN
                    INSERT INTO cp6_stock_creation_writer_v2.inserted_stock(tenant_id,stock_id)
                    SELECT NEW."TenantId",NEW."Id" WHERE NEW."TenantId"=cp6_tenancy_v1.current_tenant_id();
                END IF;
                RETURN NEW;
            END $cp6$;
            CREATE FUNCTION cp6_stock_creation_writer_v2.clear_insert() RETURNS trigger
            LANGUAGE plpgsql SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                DELETE FROM cp6_stock_creation_writer_v2.inserted_stock
                WHERE tenant_id=NEW.tenant_id AND stock_id=NEW.stock_id;
                RETURN NULL;
            END $cp6$;
            CREATE FUNCTION cp6_stock_creation_writer_v2.record_department(expected uuid, mapping uuid, stock uuid, department uuid)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                INSERT INTO cp6_operations_owner_v1.department_mappings(id,tenant_id,object_kind,object_id,department_id,enabled)
                SELECT mapping,expected,'stock',stock,department,true
                WHERE expected=cp6_tenancy_v1.current_tenant_id() AND EXISTS (
                    SELECT FROM cp6_stock_creation_writer_v2.inserted_stock s WHERE s.tenant_id=expected AND s.stock_id=stock);
                IF NOT FOUND THEN RAISE EXCEPTION 'Stock creation department rejected' USING ERRCODE='42501'; END IF;
            END $cp6$;
            CREATE FUNCTION cp6_stock_creation_writer_v2.record_audit(expected uuid, actor uuid, actor_name text,
                created_at timestamp without time zone, create_date timestamp without time zone, context jsonb)
            RETURNS void LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
            BEGIN
                WITH consumed AS (
                    DELETE FROM cp6_stock_creation_writer_v2.inserted_stock s
                    WHERE s.tenant_id=expected AND s.stock_id=(context->>'stockId')::uuid
                        AND expected=cp6_tenancy_v1.current_tenant_id() AND context->>'kind'='stock-creation.v2'
                        AND context->>'resource'='wms-stock' AND context->>'action' IN ('adjust','move')
                        AND EXISTS (SELECT FROM cp6_operations_owner_v1.department_mappings m WHERE m.tenant_id=expected
                            AND m.id=(context->>'mappingId')::uuid AND m.object_id=s.stock_id
                            AND m.department_id=(context->>'departmentId')::uuid AND m.revision=(context->>'mappingRevision')::uuid
                            AND m.xmin=pg_current_xact_id_if_assigned()::text::xid)
                    RETURNING s.stock_id
                )
                INSERT INTO public."Sys_SecurityLogs"("Id","TenantId","UserId","UserName","EventType","CreatedAt","Reason","ContextJson","CreateDate")
                SELECT gen_random_uuid(),expected,actor,actor_name,{{(int)SecurityEventType.IamAdministrationChanged}},
                    created_at,'{{OperationsAccessV1.StockCreationAuditReason}}',context,create_date
                FROM consumed;
                IF NOT FOUND THEN RAISE EXCEPTION 'Stock creation audit rejected' USING ERRCODE='42501'; END IF;
            END $cp6$;
            ALTER FUNCTION cp6_stock_creation_writer_v2.note_insert() OWNER TO cp6_stock_creation_executor_v2;
            ALTER FUNCTION cp6_stock_creation_writer_v2.clear_insert() OWNER TO cp6_stock_creation_executor_v2;
            CREATE TRIGGER cp6_stock_creation_note_insert_v2 AFTER INSERT ON public."T_Stock"
                FOR EACH ROW EXECUTE FUNCTION cp6_stock_creation_writer_v2.note_insert();
            CREATE CONSTRAINT TRIGGER cp6_stock_creation_clear_insert_v2 AFTER INSERT ON cp6_stock_creation_writer_v2.inserted_stock
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION cp6_stock_creation_writer_v2.clear_insert();
            ALTER FUNCTION cp6_stock_creation_writer_v2.record_department(uuid,uuid,uuid,uuid) OWNER TO cp6_stock_creation_executor_v2;
            ALTER FUNCTION cp6_stock_creation_writer_v2.record_audit(uuid,uuid,text,timestamp,timestamp,jsonb) OWNER TO cp6_stock_creation_executor_v2;
            REVOKE ALL ON ALL FUNCTIONS IN SCHEMA cp6_stock_creation_writer_v2 FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION cp6_stock_creation_writer_v2.record_department(uuid,uuid,uuid,uuid),
                cp6_stock_creation_writer_v2.record_audit(uuid,uuid,text,timestamp,timestamp,jsonb) TO {{roles}};
            """, connection, transaction);
        await install.ExecuteNonQueryAsync(ct);
    }
}
