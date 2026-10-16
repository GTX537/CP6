using System.Data;
using System.Text;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// Explicit overlay for a fresh Identity baseline. A known v1 directory may be validated and shared.
/// The control caller owns the transaction and must roll back any failure. No startup registration,
/// existing-object takeover, automatic commit, provisioning retry or data migration is provided here.
/// </summary>
internal static class IdentityTenantRowSecurityV1
{
    internal const string HelperOwner = TenantLoginDirectoryV1.HelperOwner;
    internal const string DirectorySchema = TenantLoginDirectoryV1.DirectorySchema;
    // Database-local two-int advisory namespace: ASCII CP6T, directory version 1.
    internal const int DirectoryLockNamespace = TenantLoginDirectoryV1.DirectoryLockNamespace;
    internal static readonly IReadOnlyList<string> Tables = Array.AsReadOnly(new[]
    {
        "Cp6_DeadLetterRecord", "Cp6_InboxAggregateCheckpoint", "Cp6_InboxMessage", "Cp6_OutboxMessage"
    });

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, bool appendToKnownDirectory = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(logins);
        var bindings = logins.ToArray();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || bindings.Length == 0 || bindings.Any(x => x is null || x.TenantId == Guid.Empty
                || string.IsNullOrWhiteSpace(x.Login) || x.Login.Any(char.IsControl) || Encoding.UTF8.GetByteCount(x.Login) > 63)
            || bindings.Select(x => x.TenantId).Distinct().Count() != bindings.Length
            || bindings.Select(x => x.Login).Distinct(StringComparer.Ordinal).Count() != bindings.Length)
            throw TenantStorageBindingV1.Rejected();

        if (appendToKnownDirectory)
            await TenantLoginDirectoryV1.ValidateAsync(connection, transaction, bindings, ct);

        // Serialize against changes to the named tables. Unknown schemas, roles, policies, grants and
        // ownership are rejected before changing ACLs; reinstalling Identity is deliberately rejected.
        await Execute("LOCK TABLE " + string.Join(", ", Tables.Select(Table)) + " IN ACCESS EXCLUSIVE MODE");
        await using (var preflight = new NpgsqlCommand("""
            SELECT (@append OR (NOT EXISTS (SELECT FROM pg_catalog.pg_namespace WHERE nspname = 'cp6_tenancy_v1')
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'cp6_tenant_lookup_v1')))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_default_acl d,
                LATERAL pg_catalog.aclexplode(d.defaclacl) a
                WHERE d.defaclrole = current_user::regrole AND d.defaclnamespace = 0
                  AND a.grantee <> d.defaclrole
                  AND NOT (d.defaclobjtype = 'f' AND a.grantee = 0 AND a.privilege_type = 'EXECUTE'))
              AND (SELECT count(*) FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'public') = 2
              AND (SELECT count(*) FROM pg_catalog.pg_proc p
                WHERE p.oid IN ('public.cp6_cp936_length_v1(text)'::regprocedure, 'public.cp6_text_range_v1(text,text)'::regprocedure)
                  AND p.proowner = current_user::regrole AND NOT p.prosecdef
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(p.proacl) a
                    WHERE a.grantee <> p.proowner AND a.grantee <> 0
                      AND NOT (@append AND a.grantee IN (SELECT oid FROM pg_catalog.pg_roles WHERE rolname=ANY(@roles))
                        AND a.privilege_type='EXECUTE' AND NOT a.is_grantable))) = 2
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND (
                  EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee <> c.relowner)
                  OR EXISTS (SELECT FROM pg_catalog.pg_attribute column_acl,
                    LATERAL pg_catalog.aclexplode(column_acl.attacl) a
                    WHERE column_acl.attrelid = c.oid AND a.grantee <> c.relowner)))
              AND (SELECT count(*) FROM pg_catalog.pg_roles r WHERE r.rolname = ANY(@roles)
                AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_auth_members m WHERE m.member = r.oid OR m.roleid = r.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_database d WHERE d.datdba = r.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_namespace n WHERE n.nspowner = r.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c WHERE c.relowner = r.oid)
                AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc p WHERE p.proowner = r.oid)) = cardinality(@roles)
              AND (SELECT count(*) FROM pg_catalog.pg_namespace n
                WHERE n.nspname IN ('crm_identity_priority', 'cp6_storage_identity')
                  AND n.nspowner = current_user::regrole
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(n.nspacl) a WHERE a.grantee <> n.nspowner)) = 2
              AND (SELECT count(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'crm_identity_priority' AND c.relkind = 'r' AND c.relname = ANY(@tables)
                  AND c.relowner = current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_policy p WHERE p.polrelid = c.oid)
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee <> c.relowner)
                  AND NOT EXISTS (SELECT FROM pg_catalog.pg_attribute column_acl,
                    LATERAL pg_catalog.aclexplode(column_acl.attacl) a
                    WHERE column_acl.attrelid = c.oid AND a.grantee <> c.relowner)
                  AND EXISTS (SELECT FROM pg_catalog.pg_attribute a WHERE a.attrelid = c.oid
                    AND a.attname = 'TenantId' AND a.atttypid = 'uuid'::regtype AND a.attnotnull AND NOT a.attisdropped)) = 4
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'crm_identity_priority' AND c.relkind <> 'i' AND NOT (c.relkind = 'r' AND c.relname = ANY(@tables)))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'crm_identity_priority')
              AND EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'cp6_storage_identity' AND c.relname = 'rowversion_v1' AND c.relkind = 'S'
                  AND c.relowner = current_user::regrole
                  AND NOT EXISTS (SELECT FROM pg_catalog.aclexplode(c.relacl) a WHERE a.grantee <> c.relowner))
              AND EXISTS (SELECT FROM pg_catalog.pg_proc WHERE oid = 'cp6_storage_identity.set_rowversion_v1()'::regprocedure
                  AND proowner = current_user::regrole AND NOT prosecdef)
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'cp6_storage_identity' AND NOT (c.relname = 'rowversion_v1' AND c.relkind = 'S'))
              AND NOT EXISTS (SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
                WHERE n.nspname = 'cp6_storage_identity' AND p.oid <> 'cp6_storage_identity.set_rowversion_v1()'::regprocedure)
              AND (SELECT count(*) FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'crm_identity_priority' AND NOT t.tgisinternal
                  AND t.tgname = 'cp6_rowversion_v1' AND t.tgfoid = 'cp6_storage_identity.set_rowversion_v1()'::regprocedure
                  AND t.tgtype = 23 AND t.tgenabled = 'O' AND t.tgnargs = 0 AND t.tgqual IS NULL) = 4
              AND (SELECT count(*) FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'crm_identity_priority' AND NOT t.tgisinternal) = 4
            """, connection, transaction))
        {
            preflight.Parameters.AddWithValue("append", appendToKnownDirectory);
            preflight.Parameters.AddWithValue("roles", bindings.Select(x => x.Login).ToArray());
            preflight.Parameters.AddWithValue("tables", Tables.ToArray());
            if (await preflight.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new InvalidOperationException("Identity tenant security first-install preconditions were rejected.");
        }

        // Verify frozen public function/collation definitions using their existing authoritative validator.
        // The preflight has already required both functions; this does not substitute new definitions.
        await Execute(PostgreSqlMigrationPrerequisitesV1.InstallSql);

        await Execute($$"""
            REVOKE CREATE, TEMPORARY ON DATABASE {{Quote(connection.Database)}} FROM PUBLIC;
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
            REVOKE ALL ON FUNCTION public.cp6_cp936_length_v1(text), public.cp6_text_range_v1(text,text),
                cp6_storage_identity.set_rowversion_v1() FROM PUBLIC;
            """);

        if (!appendToKnownDirectory)
            await TenantLoginDirectoryV1.InstallAsync(connection, transaction, bindings, ct);

        var roles = string.Join(", ", bindings.Select(x => Quote(x.Login)));
        foreach (var table in Tables)
        {
            // Four distinct policies keep UPDATE's old-row guard independent of SELECT.
            var predicate = "\"TenantId\" = cp6_tenancy_v1.current_tenant_id()";
            await Execute($$"""
                ALTER TABLE {{Table(table)}} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {{Table(table)}} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_select_v1 ON {{Table(table)}} FOR SELECT TO {{roles}} USING ({{predicate}});
                CREATE POLICY tenant_insert_v1 ON {{Table(table)}} FOR INSERT TO {{roles}} WITH CHECK ({{predicate}});
                CREATE POLICY tenant_update_v1 ON {{Table(table)}} FOR UPDATE TO {{roles}} USING ({{predicate}}) WITH CHECK ({{predicate}});
                CREATE POLICY tenant_delete_v1 ON {{Table(table)}} FOR DELETE TO {{roles}} USING ({{predicate}});
                GRANT SELECT, INSERT, UPDATE
                    ON {{Table(table)}} TO {{roles}};
                """);
        }
        await Execute($$"""
            GRANT CONNECT ON DATABASE {{Quote(connection.Database)}} TO {{roles}};
            GRANT USAGE ON SCHEMA crm_identity_priority, cp6_tenancy_v1, public, cp6_storage_identity TO {{roles}};
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id(), public.cp6_cp936_length_v1(text),
                cp6_storage_identity.set_rowversion_v1() TO {{roles}};
            GRANT USAGE ON SEQUENCE cp6_storage_identity.rowversion_v1 TO {{roles}};
            """);
        // This is a new-layout installer, not a cleanup tool for pre-existing runtime rights.
        // Fail inside SQL so the caller cannot accidentally commit partially installed permissions.
        foreach (var binding in bindings)
            await Execute($$"""
                DO $cp6_acl$ BEGIN
                    IF pg_catalog.has_database_privilege({{Literal(binding.Login)}}, current_database(), 'CREATE')
                      OR pg_catalog.has_database_privilege({{Literal(binding.Login)}}, current_database(), 'TEMP')
                      OR EXISTS (SELECT FROM pg_catalog.pg_namespace n WHERE n.nspname !~ '^pg_'
                        AND pg_catalog.has_schema_privilege({{Literal(binding.Login)}}, n.oid, 'CREATE')) THEN
                        RAISE EXCEPTION 'Unexpected tenant runtime DDL privilege' USING ERRCODE = '42501';
                    END IF;
                END $cp6_acl$;
                """);

        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static string Table(string table) => "crm_identity_priority." + Quote(table);
    private static string Quote(string identifier) => new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
