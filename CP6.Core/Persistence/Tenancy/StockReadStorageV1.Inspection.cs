using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Persistence.Tenancy;

internal static partial class StockReadStorageV1
{
    // The caller pins the physical target and uses a single read-only snapshot.
    internal static async Task<Guid?> InspectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, CancellationToken ct)
    {
        TenantLoginDirectoryV1.ValidateArguments(connection, transaction, logins);
        await using (var control = new NpgsqlCommand("""
            SELECT CASE
                WHEN pg_catalog.to_regnamespace('cp6_stock_read_v1') IS NOT NULL THEN 'present'
                WHEN EXISTS (
                    SELECT FROM pg_catalog.pg_policy p
                    WHERE p.polrelid IN (
                        SELECT pg_catalog.to_regclass(name)::oid FROM pg_catalog.unnest(@relations) AS read_table(name))
                      AND pg_catalog.starts_with(p.polname::text,'cp6_stock_read_'))
                    THEN 'orphaned'
                ELSE 'absent'
            END
            FROM pg_catalog.pg_roles WHERE rolname=session_user AND current_user=session_user AND rolsuper
            """, connection, transaction))
        {
            control.Parameters.AddWithValue("relations", Tables.Select(table => table.Qualified).ToArray());
            switch (await control.ExecuteScalarAsync(ct))
            {
                case "absent": return null;
                case "orphaned": throw new TenantProvisioningException("inspection-layout-rejected");
                case "present": break;
                default: throw new TenantProvisioningException("inspection-control-rejected");
            }
        }

        await using (var shape = new NpgsqlCommand("""
            WITH expected(relation, names, types) AS (VALUES
              ('cp6_stock_read_v1.installation', ARRAY['singleton','installation_id','model_version','migration_version'],
                ARRAY['bool'::regtype,'uuid'::regtype,'text'::regtype,'text'::regtype]),
              ('cp6_tenancy_v1.login_tenant', ARRAY['role_oid','role_name','tenant_id'],
                ARRAY['oid'::regtype,'name'::regtype,'uuid'::regtype]))
            SELECT count(*)=2 AND bool_and(
              c.relowner=current_user::regrole AND n.nspowner=c.relowner AND c.relkind='r'
              AND c.relpersistence='p' AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_inherits WHERE inhrelid=c.oid OR inhparent=c.oid)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_rewrite WHERE ev_class=c.oid)
              AND (SELECT array_agg(a.attname::text ORDER BY a.attnum)=e.names
                     AND array_agg(a.atttypid::regtype ORDER BY a.attnum)=e.types
                     AND bool_and(a.attnotnull AND NOT a.attisdropped AND a.attgenerated='' AND a.attidentity='')
                   FROM pg_catalog.pg_attribute a WHERE a.attrelid=c.oid AND a.attnum>0))
            FROM expected e JOIN pg_catalog.pg_class c ON c.oid=pg_catalog.to_regclass(e.relation)
            JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            """, connection, transaction))
        {
            if (await shape.ExecuteScalarAsync(ct) is not true)
                throw new TenantProvisioningException("inspection-layout-rejected");
        }

        Guid installationId;
        await using (var receipt = new NpgsqlCommand("""
            SELECT installation_id FROM cp6_stock_read_v1.installation
            WHERE singleton AND model_version=@model AND migration_version=@migration
              AND (SELECT count(*) FROM cp6_stock_read_v1.installation)=1
            """, connection, transaction))
        {
            receipt.Parameters.AddWithValue("model", ModelVersion);
            receipt.Parameters.AddWithValue("migration", MigrationVersion);
            if (await receipt.ExecuteScalarAsync(ct) is not Guid id || id == Guid.Empty)
                throw new TenantProvisioningException("inspection-receipt-rejected");
            installationId = id;
        }

        var roles = await InspectDirectoryAsync(connection, transaction, logins, ct);
        foreach (var table in Tables)
            await InspectReadTableAsync(connection, transaction, table, roles, ct);
        return installationId;
    }

    private static async Task<uint[]> InspectDirectoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            WITH lookup AS (
                SELECT p.oid
                FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_roles owner ON owner.oid=p.proowner
                WHERE p.oid=pg_catalog.to_regprocedure('cp6_tenancy_v1.current_tenant_id()')
                  AND owner.rolname=@helper_owner AND NOT (owner.rolcanlogin OR owner.rolsuper OR owner.rolbypassrls
                      OR owner.rolcreatedb OR owner.rolcreaterole OR owner.rolreplication OR owner.rolinherit)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member=owner.oid OR roleid=owner.oid)
                  AND p.prokind='f' AND p.prorettype='uuid'::regtype AND p.prosecdef AND p.provolatile='v'
                  AND NOT p.proretset AND p.prolang=(SELECT oid FROM pg_catalog.pg_language WHERE lanname='plpgsql')
                  AND p.proconfig=ARRAY['search_path=pg_catalog, cp6_tenancy_v1, pg_temp']
                  AND btrim(replace(p.prosrc,chr(13),''),chr(32)||chr(9)||chr(10))=@helper_body
                  AND pg_catalog.has_schema_privilege(owner.oid,'cp6_tenancy_v1','USAGE')
                  AND pg_catalog.has_table_privilege(owner.oid,'cp6_tenancy_v1.login_tenant','SELECT')
            ), directory_fence AS (
                SELECT p.oid FROM pg_catalog.pg_proc p
                WHERE p.oid=pg_catalog.to_regprocedure('cp6_tenancy_v1.lock_login_tenant_v1()')
                  AND p.proowner=current_user::regrole AND p.prokind='f' AND p.prorettype='trigger'::regtype
                  AND NOT p.prosecdef AND p.provolatile='v' AND NOT p.proretset
                  AND p.prolang=(SELECT oid FROM pg_catalog.pg_language WHERE lanname='plpgsql')
                  AND p.proconfig=ARRAY['search_path=pg_catalog, cp6_tenancy_v1, pg_temp']
                  AND btrim(replace(p.prosrc,chr(13),''),chr(32)||chr(9)||chr(10))=@lock_body
                  AND (SELECT count(*)=2 AND bool_and(t.tgfoid=p.oid AND t.tgenabled='O' AND t.tgnargs=0
                         AND t.tgqual IS NULL AND t.tgattr=''::int2vector AND NOT t.tgdeferrable AND NOT t.tginitdeferred
                         AND ((t.tgname='login_tenant_write_v1' AND t.tgtype=30)
                           OR (t.tgname='login_tenant_no_truncate_v1' AND t.tgtype=34)))
                       FROM pg_catalog.pg_trigger t
                       WHERE t.tgrelid='cp6_tenancy_v1.login_tenant'::regclass AND NOT t.tgisinternal)
            ), runtime AS (
                SELECT r.oid
                FROM ROWS FROM (pg_catalog.unnest(@tenants),pg_catalog.unnest(@logins)) expected(tenant, login)
                JOIN cp6_tenancy_v1.login_tenant m ON m.tenant_id=expected.tenant
                JOIN pg_catalog.pg_roles r ON r.oid=m.role_oid AND r.rolname=m.role_name AND r.rolname::text=expected.login
                WHERE r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member=r.oid OR roleid=r.oid)
                  AND NOT pg_catalog.has_database_privilege(r.oid,pg_catalog.current_database(),'CREATE,TEMP')
                  AND pg_catalog.has_database_privilege(r.oid,pg_catalog.current_database(),'CONNECT')
                  AND pg_catalog.has_schema_privilege(r.oid,'cp6_tenancy_v1','USAGE')
                  AND pg_catalog.has_schema_privilege(r.oid,'cp6_stock_read_v1','USAGE')
                  AND NOT pg_catalog.has_schema_privilege(r.oid,'cp6_tenancy_v1','CREATE')
                  AND NOT pg_catalog.has_schema_privilege(r.oid,'cp6_stock_read_v1','CREATE')
                  AND pg_catalog.has_function_privilege(r.oid,(SELECT oid FROM lookup),'EXECUTE')
                  AND NOT pg_catalog.has_table_privilege(r.oid,'cp6_tenancy_v1.login_tenant','DELETE,TRUNCATE,TRIGGER')
                  AND NOT pg_catalog.has_any_column_privilege(r.oid,'cp6_tenancy_v1.login_tenant','SELECT,INSERT,UPDATE,REFERENCES')
                  AND pg_catalog.has_table_privilege(r.oid,'cp6_stock_read_v1.installation','SELECT')
                  AND NOT pg_catalog.has_table_privilege(r.oid,'cp6_stock_read_v1.installation','DELETE,TRUNCATE,TRIGGER,SELECT WITH GRANT OPTION')
                  AND NOT pg_catalog.has_any_column_privilege(r.oid,'cp6_stock_read_v1.installation','INSERT,UPDATE,REFERENCES')
            )
            SELECT ARRAY(SELECT oid FROM runtime ORDER BY oid)
            WHERE EXISTS (SELECT FROM directory_fence)
              AND (SELECT count(*) FROM cp6_tenancy_v1.login_tenant)=cardinality(@logins)
              AND (SELECT count(DISTINCT oid) FROM runtime)=cardinality(@logins)
            """, connection, transaction);
        command.Parameters.AddWithValue("tenants", logins.Select(login => login.TenantId).ToArray());
        command.Parameters.AddWithValue("logins", logins.Select(login => login.Login).ToArray());
        command.Parameters.AddWithValue("helper_owner", TenantLoginDirectoryV1.HelperOwner);
        command.Parameters.AddWithValue("helper_body", TenantLoginDirectoryV1.HelperBody.Replace("\r", "", StringComparison.Ordinal).Trim());
        command.Parameters.AddWithValue("lock_body", TenantLoginDirectoryV1.LockBody.Replace("\r", "", StringComparison.Ordinal).Trim());
        return await command.ExecuteScalarAsync(ct) is uint[] roles
            ? roles
            : throw new TenantProvisioningException("inspection-directory-rejected");
    }

    private static async Task InspectReadTableAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ReadTable table, uint[] roles, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            WITH approved_reads(relation, policy_name, role_name, object_kind) AS (VALUES
                ('public."Sys_Dept"', 'cp6_stock_movement_lineage_read_v1', 'cp6_stock_movement_executor_v1', NULL),
                ('public."T_StockTransaction"', 'cp6_stock_movement_lineage_read_v1', 'cp6_stock_movement_executor_v1', NULL),
                ('cp6_operations_owner_v1.department_mappings', 'cp6_stock_movement_lineage_read_v1', 'cp6_stock_movement_executor_v1', 'stock-transaction'),
                ('public."Sys_Dept"', 'cp6_stock_creation_read_v2', 'cp6_stock_creation_executor_v2', NULL),
                ('public."T_Stock"', 'cp6_stock_creation_read_v2', 'cp6_stock_creation_executor_v2', NULL),
                ('cp6_operations_owner_v1.department_mappings', 'cp6_stock_creation_read_v2', 'cp6_stock_creation_executor_v2', 'stock'))
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_class c
                WHERE c.oid=pg_catalog.to_regclass(@relation) AND c.relowner=current_user::regrole AND c.relkind='r'
                  AND c.relrowsecurity AND c.relforcerowsecurity
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_inherits WHERE inhrelid=c.oid OR inhparent=c.oid)
                  AND EXISTS (
                      SELECT FROM pg_catalog.pg_policy p
                      WHERE p.polrelid=c.oid AND p.polname='cp6_stock_read_tenant_v1'
                        AND p.polcmd='r' AND p.polpermissive AND p.polwithcheck IS NULL
                        AND p.polroles @> @roles AND p.polroles <@ @roles
                        AND pg_catalog.regexp_replace(pg_catalog.pg_get_expr(p.polqual,c.oid),'[[:space:]]+',' ','g')
                            = @predicate)
                  AND EXISTS (
                      SELECT FROM pg_catalog.pg_policy p
                      WHERE p.polrelid=c.oid AND p.polname='cp6_stock_read_owner_v1'
                        AND p.polcmd='*' AND p.polpermissive AND p.polroles=ARRAY[c.relowner]
                        AND pg_catalog.pg_get_expr(p.polqual,c.oid)='true'
                        AND pg_catalog.pg_get_expr(p.polwithcheck,c.oid)='true')
                  AND NOT EXISTS (
                      SELECT FROM pg_catalog.pg_policy p
                      WHERE p.polrelid=c.oid AND p.polcmd IN ('r','*')
                        AND p.polname NOT IN ('cp6_stock_read_tenant_v1','cp6_stock_read_owner_v1')
                        AND NOT EXISTS (
                            SELECT FROM approved_reads a
                            JOIN pg_catalog.pg_roles executor ON executor.rolname::text=a.role_name
                            WHERE pg_catalog.to_regclass(a.relation)=c.oid AND p.polname::text=a.policy_name
                              AND p.polcmd='r' AND p.polpermissive AND p.polwithcheck IS NULL
                              AND p.polroles=ARRAY[executor.oid]
                              AND NOT (executor.rolcanlogin OR executor.rolsuper OR executor.rolbypassrls)
                              AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members
                                  WHERE roleid=executor.oid OR member=executor.oid)
                              AND pg_catalog.regexp_replace(pg_catalog.pg_get_expr(p.polqual,c.oid),'[[:space:]]+',' ','g')
                                = CASE WHEN a.object_kind IS NULL THEN @tenant_predicate
                                  ELSE '(' || @tenant_predicate || ' AND (object_kind = '
                                    || pg_catalog.quote_literal(a.object_kind) || '::text))' END))
                  AND NOT EXISTS (
                      SELECT FROM pg_catalog.unnest(@roles) AS runtime(oid)
                      WHERE NOT pg_catalog.has_schema_privilege(runtime.oid,c.relnamespace,'USAGE')
                        OR pg_catalog.has_schema_privilege(runtime.oid,c.relnamespace,'CREATE')
                        OR pg_catalog.has_any_column_privilege(runtime.oid,c.oid,'SELECT WITH GRANT OPTION')
                        OR (@whole_table AND NOT pg_catalog.has_table_privilege(runtime.oid,c.oid,'SELECT'))
                        OR (NOT @whole_table AND (
                            pg_catalog.has_table_privilege(runtime.oid,c.oid,'SELECT')
                            OR EXISTS (
                                SELECT FROM pg_catalog.unnest(@columns) AS required(name)
                                WHERE NOT pg_catalog.has_column_privilege(runtime.oid,c.oid,required.name,'SELECT'))
                            OR EXISTS (
                                SELECT FROM pg_catalog.pg_attribute a
                                WHERE a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
                                  AND a.attname::text<>ALL(@columns)
                                  AND pg_catalog.has_column_privilege(runtime.oid,c.oid,a.attnum,'SELECT')
                                  AND NOT (
                                      (a.attname::text=ANY(@movement_columns) AND pg_catalog.has_function_privilege(runtime.oid,
                                          pg_catalog.to_regprocedure('cp6_stock_movement_writer_v1.record_department(uuid,uuid,uuid,uuid)'),'EXECUTE') IS TRUE)
                                      OR (a.attname::text=ANY(@creation_columns) AND pg_catalog.has_function_privilege(runtime.oid,
                                          pg_catalog.to_regprocedure('cp6_stock_creation_writer_v2.record_department(uuid,uuid,uuid,uuid)'),'EXECUTE') IS TRUE)
                                  ))
                        )))
            )
            """, connection, transaction);
        // Compare PostgreSQL's deparsed predicate under pg_catalog, including the mapping's object-kind scope.
        var tenantColumn = table.TenantColumn == "tenant_id" ? table.TenantColumn : Quote(table.TenantColumn);
        var tenantPredicate = $"({tenantColumn} = ( SELECT cp6_tenancy_v1.current_tenant_id() AS current_tenant_id))";
        var predicate = table.Scope is null
            ? tenantPredicate
            : $"({tenantPredicate} AND (object_kind = ANY (ARRAY['stock'::text, 'stock-transaction'::text])))";
        command.Parameters.AddWithValue("relation", table.Qualified);
        command.Parameters.AddWithValue("roles", NpgsqlDbType.Array | NpgsqlDbType.Oid, roles);
        command.Parameters.AddWithValue("predicate", predicate);
        command.Parameters.AddWithValue("tenant_predicate", tenantPredicate);
        command.Parameters.AddWithValue("whole_table", table.Columns is null);
        command.Parameters.AddWithValue("columns", table.Columns?.Split(',').Select(column => column.Trim().Trim('"')).ToArray() ?? []);
        command.Parameters.AddWithValue("movement_columns", table.MovementReadColumns ?? []);
        command.Parameters.AddWithValue("creation_columns", table.CreationReadColumns ?? []);
        if (await command.ExecuteScalarAsync(ct) is not true)
            throw new TenantProvisioningException("inspection-read-boundary-rejected");
    }
}
