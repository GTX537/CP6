using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Stages the original WMS stock finance intent on the restricted movement transaction.</summary>
internal static class StockMovementFinanceWriterStorageV1
{
    internal const string Contract = "stock-movement-finance-v1";
    internal const string Function = "cp6_stock_movement_writer_v1.stage_finance_event(uuid,uuid,numeric,numeric)";
    private const string Executor = "cp6_stock_movement_executor_v1";

    private const string Body = $"""
        BEGIN
            INSERT INTO public."T_IntegrationEvent"
                ("Id","TenantId","CorrelationId","SourceModule","TargetModule","HookName","SourceNo",
                 "Status","Attempts","NextRetryAt","PayloadJson","Creator","CreateDate","IsDeleted")
            SELECT t."Id", t."TenantId", gen_random_uuid(), 'WMS', 'FIN', '{nameof(StockFinBridge.OnStockMovedAsync)}',
                t."TxnNo", '{IntegrationEventStatus.Pending}', 0, clock_timestamp(),
                json_build_object(
                    'version', 1, 'stockTransactionId', t."Id", 'txnNo', t."TxnNo"::text,
                    'TxnType', convert_from(bpcharsend(t."TxnType"), 'UTF8'),
                    'relatedType', coalesce(convert_from(bpcharsend(t."RelatedType"), 'UTF8'), ''),
                    'Qty', raw_qty, 'UnitPrice', raw_unit_price)::text,
                coalesce(nullif(t."OperatorCd"::text, ''), 'system'), t."CreateDate", false
            FROM public."T_StockTransaction" t
            WHERE expected=cp6_tenancy_v1.current_tenant_id() AND t."TenantId"=expected AND t."Id"=movement
                AND NOT t."IsDeleted" AND t.xmin=pg_current_xact_id_if_assigned()::text::xid
                AND raw_qty::text NOT IN ('NaN','Infinity','-Infinity')
                AND (raw_unit_price IS NULL OR raw_unit_price::text NOT IN ('NaN','Infinity','-Infinity'))
                AND t."Qty"=raw_qty::numeric(21,8)
                AND t."UnitPrice" IS NOT DISTINCT FROM raw_unit_price::numeric(18,4);
            IF NOT FOUND THEN RAISE EXCEPTION 'Stock finance movement binding rejected' USING ERRCODE='42501'; END IF;
            RETURN movement;
        END;
        """;

    internal static Task<Guid> StageAsync(StockMovementWriteContextV1 writer, StockTransaction movement, CancellationToken ct) =>
        writer.Database.SqlQueryRaw<Guid>("""
            SELECT cp6_stock_movement_writer_v1.stage_finance_event(@expected, @movement, @raw_qty, @raw_unit_price) AS "Value"
            """,
            new NpgsqlParameter("expected", writer.CurrentTenantId),
            new NpgsqlParameter("movement", movement.Id),
            new NpgsqlParameter("raw_qty", NpgsqlDbType.Numeric) { Value = movement.Qty },
            new NpgsqlParameter("raw_unit_price", NpgsqlDbType.Numeric) { Value = (object?)movement.UnitPrice ?? DBNull.Value })
            .SingleAsync(ct);

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid readInstallationId, IReadOnlyCollection<TenantLoginV1> writers, CancellationToken ct = default)
    {
        var roles = await StockWriteAuthorityFenceV1.PrepareInstallAsync(connection, transaction, readInstallationId,
            writers, requireFence: true, ct);
        await InstallAfterMovementAsync(connection, transaction, writers, roles, ct);
    }

    internal static async Task InstallAfterMovementAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> writers, string roles, CancellationToken ct)
    {
        await using (var installLock = new NpgsqlCommand("""
            LOCK TABLE public."T_IntegrationEvent" IN SHARE ROW EXCLUSIVE MODE
            """, connection, transaction))
            await installLock.ExecuteNonQueryAsync(ct);

        await using var verify = new NpgsqlCommand("""
            WITH movement AS (
                SELECT p.oid, p.proowner
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_roles e ON e.oid=p.proowner
                WHERE p.oid=pg_catalog.to_regprocedure('cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid)')
                  AND e.rolname=@executor AND p.prosecdef
                  AND p.proconfig=ARRAY['search_path=pg_catalog, pg_temp']
                  AND NOT (e.rolcanlogin OR e.rolinherit OR e.rolsuper OR e.rolbypassrls OR
                           e.rolcreaterole OR e.rolcreatedb OR e.rolreplication)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members a WHERE a.roleid=e.oid OR a.member=e.oid)
            ), target AS (
                SELECT c.oid FROM pg_catalog.pg_class c
                WHERE c.oid=pg_catalog.to_regclass('public."T_IntegrationEvent"')
                  AND c.relowner=current_user::regrole AND c.relkind='r' AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
            )
            SELECT installed.oid IS NOT NULL
            FROM movement CROSS JOIN target
            LEFT JOIN pg_catalog.pg_proc installed ON installed.oid=pg_catalog.to_regprocedure(@projector)
            WHERE NOT EXISTS (
                SELECT FROM pg_catalog.pg_roles r WHERE r.rolname::text=ANY(@logins)
                  AND (NOT pg_catalog.has_function_privilege(r.oid, movement.oid, 'EXECUTE')
                    OR pg_catalog.has_table_privilege(r.oid, target.oid, 'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege(r.oid, target.oid, 'SELECT,INSERT,UPDATE,REFERENCES')))
              AND (installed.oid IS NULL OR (
                  installed.proowner=movement.proowner AND installed.prosecdef AND installed.provolatile='v'
                  AND installed.prorettype='uuid'::regtype AND NOT installed.proretset
                  AND installed.proconfig=ARRAY['search_path=pg_catalog, pg_temp']
                  AND pg_catalog.btrim(pg_catalog.replace(installed.prosrc, pg_catalog.chr(13), ''), E' \n\t')=@body
                  AND NOT EXISTS (
                      SELECT FROM pg_catalog.aclexplode(coalesce(installed.proacl, pg_catalog.acldefault('f', installed.proowner))) a
                      WHERE a.grantee<>installed.proowner AND (a.is_grantable OR a.privilege_type<>'EXECUTE' OR NOT EXISTS (
                          SELECT FROM cp6_tenancy_v1.login_tenant login
                          JOIN pg_catalog.pg_roles role ON role.oid=login.role_oid AND role.rolname=login.role_name
                          WHERE login.role_oid=a.grantee AND role.rolcanlogin
                            AND pg_catalog.has_function_privilege(role.oid, movement.oid, 'EXECUTE'))))))
            """, connection, transaction);
        verify.Parameters.AddWithValue("executor", Executor);
        verify.Parameters.AddWithValue("projector", Function);
        verify.Parameters.AddWithValue("logins", writers.Select(writer => writer.Login).ToArray());
        verify.Parameters.AddWithValue("body", Body.Replace("\r", "", StringComparison.Ordinal).Trim());
        if (await verify.ExecuteScalarAsync(ct) is not bool installed)
            throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);

        if (!installed)
        {
            await using var create = new NpgsqlCommand($$"""
                CREATE FUNCTION cp6_stock_movement_writer_v1.stage_finance_event(
                    expected uuid, movement uuid, raw_qty numeric, raw_unit_price numeric)
                RETURNS uuid LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $cp6$
                {{Body}}
                $cp6$;
                ALTER FUNCTION {{Function}} OWNER TO {{Executor}};
                REVOKE ALL ON FUNCTION {{Function}} FROM PUBLIC;
                """, connection, transaction);
            await create.ExecuteNonQueryAsync(ct);
        }

        await using var grant = new NpgsqlCommand($$"""
            GRANT USAGE ON SCHEMA cp6_storage_core TO {{Executor}};
            GRANT USAGE ON SEQUENCE cp6_storage_core.rowversion_v1 TO {{Executor}};
            GRANT SELECT ("TxnNo","TxnType","RelatedType","Qty","UnitPrice","OperatorCd","CreateDate")
                ON public."T_StockTransaction" TO {{Executor}};
            GRANT INSERT ("Id","TenantId","CorrelationId","SourceModule","TargetModule","HookName","SourceNo",
                "Status","Attempts","NextRetryAt","PayloadJson","Creator","CreateDate","IsDeleted")
                ON public."T_IntegrationEvent" TO {{Executor}};
            GRANT EXECUTE ON FUNCTION {{Function}} TO {{roles}};
            """, connection, transaction);
        await grant.ExecuteNonQueryAsync(ct);
    }
}
