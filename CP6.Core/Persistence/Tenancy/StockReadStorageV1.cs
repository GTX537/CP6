using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Explicit first installation after the Core migrations in a new isolated database.
/// The control caller owns the transaction. All existing Owner tables, keys and rows stay in place.</summary>
internal static class StockReadStorageV1
{
    internal const string ModelVersion = "stock-read-v1";
    internal const string MigrationVersion = "stock-read-rls-v1";

    private sealed record ReadTable(string Schema, string Name, string TenantColumn, string? Columns = null, string? Scope = null)
    {
        internal string Qualified => Quote(Schema) + "." + Quote(Name);
    }

    private static readonly ReadTable[] Tables =
    [
        new("public", "T_Stock", "TenantId"),
        new("public", "T_StockTransaction", "TenantId"),
        new("public", "T_WmsRoleScope", "TenantId", """
            "TenantId","RoleId","WarehouseCd","AreaCd"
            """),
        new("public", "T_Location", "TenantId", """
            "TenantId","IsDeleted","IsInitializationQuarantined","WarehouseCd","LocationCd","AreaCd"
            """),
        new("public", "Sys_Dept", "TenantId", """
            "Id","TenantId","ParentId","Enable","Path"
            """),
        new("cp6_operations_owner_v1", "department_mappings", "tenant_id",
            "tenant_id,object_kind,object_id,department_id,enabled", "object_kind IN ('stock','stock-transaction')")
    ];

    internal static async Task<Guid> InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, CancellationToken ct = default)
    {
        var bindings = logins.ToArray();
        TenantLoginDirectoryV1.ValidateArguments(connection, transaction, bindings);
        await Execute("SET LOCAL search_path = pg_catalog");
        await Execute("LOCK TABLE " + string.Join(", ", Tables.Select(table => table.Qualified)) + " IN ACCESS EXCLUSIVE MODE");

        await using var preflight = new NpgsqlCommand("""
            WITH runtime AS (
                SELECT oid FROM pg_catalog.pg_roles
                WHERE rolname=ANY(@roles) AND rolcanlogin
                  AND NOT (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members WHERE member=pg_roles.oid OR roleid=pg_roles.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_database WHERE datdba=pg_roles.oid)
            ), targets AS (
                SELECT c.* FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname || '.' || c.relname=ANY(@tables)
            )
            SELECT session_user::text FROM pg_catalog.pg_roles control
            WHERE control.rolname=session_user AND current_user=session_user AND control.rolsuper
              AND (SELECT count(*) FROM runtime)=cardinality(@roles)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace WHERE nspname IN ('cp6_tenancy_v1','cp6_stock_read_v1'))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1')
              AND (SELECT count(*) FROM targets t WHERE t.relkind='r' AND t.relowner=control.oid
                AND NOT t.relrowsecurity AND NOT t.relforcerowsecurity
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy WHERE polrelid=t.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_inherits WHERE inhrelid=t.oid OR inhparent=t.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(t.relacl) a WHERE a.grantee<>t.relowner)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute c,
                  LATERAL pg_catalog.aclexplode(c.attacl) a WHERE c.attrelid=t.oid AND a.grantee<>t.relowner)
                AND EXISTS (SELECT FROM pg_catalog.pg_attribute c WHERE c.attrelid=t.oid AND NOT c.attisdropped
                  AND c.attname=CASE WHEN t.relname='department_mappings' THEN 'tenant_id' ELSE 'TenantId' END
                  AND c.attnotnull AND c.atttypid='uuid'::regtype))=cardinality(@tables)
              AND NOT EXISTS (
                SELECT FROM runtime r CROSS JOIN pg_catalog.pg_namespace n
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                  AND (pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE')
                    OR EXISTS (SELECT FROM pg_catalog.pg_class c WHERE c.relnamespace=n.oid AND
                       CASE WHEN c.relkind='S' THEN pg_catalog.has_sequence_privilege(r.oid,c.oid,'SELECT,UPDATE,USAGE')
                       WHEN c.relkind IN ('r','v','m','f','p') THEN
                         pg_catalog.has_table_privilege(r.oid,c.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                         OR pg_catalog.has_any_column_privilege(r.oid,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
                       ELSE false END)
                    OR EXISTS (SELECT FROM pg_catalog.pg_proc p WHERE p.pronamespace=n.oid AND p.prosecdef
                       AND (pg_catalog.has_schema_privilege(r.oid,n.oid,'USAGE')
                            OR n.nspname IN ('public','cp6_operations_owner_v1'))
                       AND pg_catalog.has_function_privilege(r.oid,p.oid,'EXECUTE'))))
              AND NOT EXISTS (SELECT FROM runtime r WHERE pg_catalog.has_database_privilege(r.oid,current_database(),'CREATE'))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_default_acl d, LATERAL pg_catalog.aclexplode(d.defaclacl) a
                WHERE d.defaclrole=control.oid AND a.grantee<>control.oid
                  AND NOT (d.defaclobjtype='f' AND a.grantee=0 AND a.privilege_type='EXECUTE'))
            """, connection, transaction);
        preflight.Parameters.AddWithValue("roles", bindings.Select(x => x.Login).ToArray());
        preflight.Parameters.AddWithValue("tables", Tables.Select(t => t.Schema + "." + t.Name).ToArray());
        if (await preflight.ExecuteScalarAsync(ct) is not string owner)
            throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);

        await Execute($"REVOKE TEMPORARY ON DATABASE {Quote(connection.Database)} FROM PUBLIC");
        await using (var temporary = new NpgsqlCommand("""
            SELECT EXISTS (SELECT FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles)
                AND pg_catalog.has_database_privilege(r.oid,current_database(),'TEMP'))
            """, connection, transaction))
        {
            temporary.Parameters.AddWithValue("roles", bindings.Select(x => x.Login).ToArray());
            if (await temporary.ExecuteScalarAsync(ct) is true)
                throw new TenantStorageException(TenantStorageFailure.ConfigurationRejected);
        }
        await TenantLoginDirectoryV1.InstallAsync(connection, transaction, bindings, ct);

        var roles = string.Join(", ", bindings.Select(binding => Quote(binding.Login)));
        foreach (var table in Tables)
        {
            var target = table.Qualified;
            var tenant = Quote(table.TenantColumn);
            var restriction = table.Scope is null ? "" : " AND " + table.Scope;
            var columns = table.Columns is null ? "" : " (" + table.Columns + ")";
            await Execute($$"""
                ALTER TABLE {{target}} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {{target}} FORCE ROW LEVEL SECURITY;
                CREATE POLICY cp6_stock_read_owner_v1 ON {{target}} TO {{Quote(owner)}} USING (true) WITH CHECK (true);
                CREATE POLICY cp6_stock_read_tenant_v1 ON {{target}} FOR SELECT TO {{roles}}
                    USING ({{tenant}} = (SELECT cp6_tenancy_v1.current_tenant_id()){{restriction}});
                GRANT SELECT{{columns}} ON {{target}} TO {{roles}};
                """);
        }

        await Execute($$"""
            CREATE SCHEMA cp6_stock_read_v1;
            REVOKE ALL ON SCHEMA cp6_stock_read_v1 FROM PUBLIC;
            CREATE TABLE cp6_stock_read_v1.installation (
                singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
                installation_id uuid NOT NULL, model_version text NOT NULL, migration_version text NOT NULL);
            GRANT CONNECT ON DATABASE {{Quote(connection.Database)}} TO {{roles}};
            GRANT USAGE ON SCHEMA public, cp6_operations_owner_v1, cp6_tenancy_v1, cp6_stock_read_v1 TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO {{roles}};
            GRANT SELECT ON cp6_stock_read_v1.installation TO {{roles}};
            """);
        var id = Guid.NewGuid();
        await using var receipt = new NpgsqlCommand("""
            INSERT INTO cp6_stock_read_v1.installation(installation_id,model_version,migration_version)
            VALUES (@id,@model,@migration)
            """, connection, transaction);
        receipt.Parameters.AddWithValue("id", id);
        receipt.Parameters.AddWithValue("model", ModelVersion);
        receipt.Parameters.AddWithValue("migration", MigrationVersion);
        await receipt.ExecuteNonQueryAsync(ct);
        return id;

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    // Compare both real backends while the authority transaction is still open. A different database,
    // server incarnation, installation, schema version or replica cannot use that transaction's fences.
    internal static async Task<string> ReadDatabaseIdentityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct, bool requireRowSecurity = false)
    {
        await using var command = new NpgsqlCommand("""
            SELECT pg_catalog.concat_ws('/', pg_catalog.host(pg_catalog.inet_server_addr()), pg_catalog.inet_server_port(),
                d.oid, extract(epoch FROM pg_catalog.pg_postmaster_start_time()), i.installation_id)
            FROM cp6_stock_read_v1.installation i
            JOIN pg_catalog.pg_database d ON d.datname=pg_catalog.current_database()
            WHERE i.singleton AND i.model_version=@model AND i.migration_version=@migration
              AND pg_catalog.inet_server_addr() IS NOT NULL AND NOT pg_catalog.pg_is_in_recovery()
              AND (NOT @require_rls OR NOT EXISTS (
                    SELECT FROM pg_catalog.unnest(@tables) AS expected(name)
                    WHERE NOT pg_catalog.row_security_active(expected.name::regclass)))
            """, connection, transaction);
        command.Parameters.AddWithValue("model", ModelVersion);
        command.Parameters.AddWithValue("migration", MigrationVersion);
        command.Parameters.AddWithValue("require_rls", requireRowSecurity);
        command.Parameters.AddWithValue("tables", Tables.Select(t => t.Qualified).ToArray());
        return await command.ExecuteScalarAsync(ct) as string
            ?? throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
    }

    private static string Quote(string name) => new NpgsqlCommandBuilder().QuoteIdentifier(name);
}
