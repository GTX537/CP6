using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// Explicit first-install overlay on the actual Core directory tables in an isolated layout.
/// The control caller owns the transaction and must roll back every failure. No startup install,
/// existing security-profile adoption, automatic commit, retry or historical migration is provided.
/// </summary>
internal static class IdentityDirectoryRowSecurityV1
{
    internal const string ModelVersion = "identity-directory-read-v1";
    internal const string MigrationVersion = "identity-directory-read-rls-v1";
    internal static readonly IReadOnlyList<string> Tables = Array.AsReadOnly(new[]
    {
        "crm_identity.\"Bootstrap\"", "crm_identity.\"Snapshot\"", "public.\"CrmIdentityTenantGenerations\""
    });

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, CancellationToken ct = default)
    {
        var bindings = logins?.ToArray() ?? throw new ArgumentNullException(nameof(logins));
        TenantLoginDirectoryV1.ValidateArguments(connection, transaction, bindings);
        // Directory namespace 1 is mapping, 2 is service provisioning, 3 is this fresh overlay.
        await Execute("SELECT pg_catalog.pg_advisory_xact_lock(1129330260, 3)");
        await Execute("LOCK TABLE " + string.Join(", ", Tables) + " IN ACCESS EXCLUSIVE MODE");
        var roles = bindings.Select(x => x.Login).ToArray();
        await Require("""
            SELECT session_user=current_user
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='cp6_tenancy_v1')
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_roles WHERE rolname='cp6_tenant_lookup_v1')
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_default_acl)
              AND (SELECT count(*) FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles)
                AND r.rolcanlogin AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication)
                AND r.rolname<>current_user
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_auth_members m WHERE m.member=r.oid OR m.roleid=r.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_database d WHERE d.datdba=r.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n WHERE n.nspowner=r.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_class c WHERE c.relowner=r.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc p WHERE p.proowner=r.oid))=cardinality(@roles)
              AND (SELECT count(*) FROM pg_catalog.pg_class c WHERE c.oid=ANY(@tables::regclass[])
                AND c.relkind='r' AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid)
                AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))=3
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_namespace n,LATERAL pg_catalog.aclexplode(n.nspacl) a
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                  AND a.grantee<>n.nspowner AND NOT (n.nspname='public' AND a.grantee=0 AND a.privilege_type='USAGE' AND NOT a.is_grantable))
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace,
                LATERAL pg_catalog.aclexplode(c.relacl) a
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND a.grantee<>c.relowner)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_attribute col JOIN pg_catalog.pg_class c ON c.oid=col.attrelid
                JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace,LATERAL pg_catalog.aclexplode(col.attacl) a
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND a.grantee<>c.relowner)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND (
                  p.proowner<>current_user::regrole OR p.prosecdef
                  OR NOT EXISTS(SELECT FROM unnest(ARRAY[
                    pg_catalog.to_regprocedure('public.cp6_cp936_length_v1(text)'),
                    pg_catalog.to_regprocedure('public.cp6_text_range_v1(text,text)'),
                    pg_catalog.to_regprocedure('public.cp6_mes_dashboard_summary_v1(date)'),
                    pg_catalog.to_regprocedure('public.cp6_mes_daily_trend_v1(integer,date)'),
                    pg_catalog.to_regprocedure('public.cp6_mes_process_progress_v1()'),
                    pg_catalog.to_regprocedure('cp6_storage_core.set_rowversion_v1()'),
                    pg_catalog.to_regprocedure('cp6_storage_core.advance_identity_generation_v1()'),
                    pg_catalog.to_regprocedure('cp6_storage_core.guard_journal_line_v1()'),
                    pg_catalog.to_regprocedure('cp6_platform_admin_guard_v1.acquire()'),
                    pg_catalog.to_regprocedure('cp6_platform_admin_guard_v1.before_change()'),
                    pg_catalog.to_regprocedure('cp6_platform_admin_guard_v1.after_change()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.acquire()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.before_change()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.credential_revision()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.policy_revision()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.sso_revision()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.track_preparation()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.track_device()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.track_device_policy()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.close_revoked_origin()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.require_open_browser_attempt()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.sso_admission_revision()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.sso_operation_immutable()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.federated_key_writer()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.username_admission_writer()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.bump_admission_revision(uuid,text)'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.admission_revision_writer()'),
                    pg_catalog.to_regprocedure('cp6_authentication_v1.organization_revision_writer()'),
                    pg_catalog.to_regprocedure('cp6_storage_identity.set_rowversion_v1()')]) known(oid) WHERE known.oid=p.oid)
                  OR EXISTS(SELECT FROM pg_catalog.aclexplode(coalesce(p.proacl,pg_catalog.acldefault('f',p.proowner))) a
                    WHERE a.grantee<>p.proowner AND NOT(n.nspname NOT IN ('cp6_authentication_v1','cp6_platform_admin_guard_v1')
                      AND a.grantee=0 AND a.privilege_type='EXECUTE' AND NOT a.is_grantable))))
              AND pg_catalog.to_regprocedure('public.cp6_cp936_length_v1(text)') IS NOT NULL
              AND pg_catalog.to_regprocedure('public.cp6_text_range_v1(text,text)') IS NOT NULL
            """);
        foreach (var role in roles)
            await ServiceTokenProfileV1.ValidateFreshLoginAsync(connection, transaction, role, ct).ConfigureAwait(false);
        // Reuse the existing native initial-ACL validator; never weaken the catalog baseline.
        await ServiceTokenProfileV1.ValidateFreshPublicAsync(connection, transaction, ct).ConfigureAwait(false);
        await ValidateColumns(Tables[0], ["TenantId", "CompletedAtUtc", "ContractBundleSha256", "RowVersion"],
            ["uuid", "timestamptz", "bpchar", "bytea"], [true, false, true, true], ["TenantId"]);
        await ValidateColumns(Tables[1], ["TenantId", "AggregateId", "EventType", "Version", "PayloadJson", "PayloadSha256", "IsDeleted", "UpdatedAtUtc", "RowVersion"],
            ["uuid", "bpchar", "bpchar", "int4", "bpchar", "bpchar", "bool", "timestamptz", "bytea"],
            [true, true, true, true, true, true, true, true, true], ["TenantId", "AggregateId"]);
        await ValidateColumns(Tables[2], ["TenantId", "Generation"], ["uuid", "int8"], [true, true], ["TenantId"]);
        await Require("""
            SELECT (SELECT count(*) FROM pg_catalog.pg_trigger t WHERE t.tgrelid=ANY(@tables::regclass[]) AND NOT t.tgisinternal)=3
              AND (SELECT count(*) FROM pg_catalog.pg_trigger t WHERE t.tgrelid=ANY(@tables::regclass[]) AND NOT t.tgisinternal
                AND t.tgenabled='O' AND t.tgnargs=0 AND t.tgqual IS NULL AND NOT t.tgdeferrable
                AND ((t.tgrelid IN ('crm_identity."Bootstrap"'::regclass,'crm_identity."Snapshot"'::regclass)
                    AND t.tgname='cp6_rowversion_v1' AND t.tgfoid='cp6_storage_core.set_rowversion_v1()'::regprocedure AND t.tgtype=23)
                  OR (t.tgrelid='crm_identity."Snapshot"'::regclass AND t.tgname='cp6_identity_generation_v1'
                    AND t.tgfoid='cp6_storage_core.advance_identity_generation_v1()'::regprocedure AND t.tgtype=29)))=3
            """);
        await Execute(PostgreSqlMigrationPrerequisitesV1.InstallSql);
        await Execute($$"""
            REVOKE CREATE,TEMPORARY ON DATABASE {{Q(connection.Database)}} FROM PUBLIC;
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            ALTER DEFAULT PRIVILEGES REVOKE EXECUTE ON FUNCTIONS FROM PUBLIC;
            REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public,crm_identity,cp6_storage_core FROM PUBLIC;
            """);
        await using (var optional = new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_catalog.pg_namespace WHERE nspname='cp6_storage_identity')", connection, transaction))
            if (await optional.ExecuteScalarAsync(ct).ConfigureAwait(false) is true)
                await Execute("REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA cp6_storage_identity FROM PUBLIC");
        await TenantLoginDirectoryV1.InstallAsync(connection, transaction, bindings, ct).ConfigureAwait(false);
        string owner;
        await using (var ownerCommand = new NpgsqlCommand("SELECT current_user::text", connection, transaction))
            owner = (string)(await ownerCommand.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var quotedRoles = string.Join(",", roles.Select(Q));
        foreach (var table in Tables)
            await Execute($$"""
                ALTER TABLE {{table}} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {{table}} FORCE ROW LEVEL SECURITY;
                CREATE POLICY directory_read_v1 ON {{table}} FOR SELECT TO {{quotedRoles}}
                    USING ("TenantId"=cp6_tenancy_v1.current_tenant_id());
                CREATE POLICY directory_control_v1 ON {{table}} FOR ALL TO {{Q(owner)}} USING (true) WITH CHECK (true);
                GRANT SELECT ON {{table}} TO {{quotedRoles}};
                """);
        await Execute($$"""
            GRANT CONNECT ON DATABASE {{Q(connection.Database)}} TO {{quotedRoles}};
            GRANT USAGE ON SCHEMA public,crm_identity,cp6_tenancy_v1 TO {{quotedRoles}};
            GRANT EXECUTE ON FUNCTION cp6_tenancy_v1.current_tenant_id() TO {{quotedRoles}};
            """);
        await TenantLoginDirectoryV1.ValidateAsync(connection, transaction, bindings, ct).ConfigureAwait(false);
        await Require("""
            SELECT NOT EXISTS(SELECT FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles) AND (
              pg_catalog.has_database_privilege(r.oid,current_database(),'CREATE,TEMP')
              OR EXISTS(SELECT FROM pg_catalog.pg_namespace n WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND
                (pg_catalog.has_schema_privilege(r.oid,n.oid,'CREATE') OR (n.nspname NOT IN ('public','crm_identity','cp6_tenancy_v1') AND pg_catalog.has_schema_privilege(r.oid,n.oid,'USAGE'))))
              OR EXISTS(SELECT FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema' AND CASE WHEN c.relkind IN ('r','v','m','f','p') THEN
                  ((c.oid<>ALL(@tables::regclass[]) AND pg_catalog.has_any_column_privilege(r.oid,c.oid,'SELECT'))
                    OR pg_catalog.has_table_privilege(r.oid,c.oid,'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                    OR pg_catalog.has_any_column_privilege(r.oid,c.oid,'INSERT,UPDATE,REFERENCES')) ELSE false END)
              OR EXISTS(SELECT FROM pg_catalog.pg_class c WHERE CASE WHEN c.relkind='S'
                THEN pg_catalog.has_sequence_privilege(r.oid,c.oid,'USAGE,SELECT,UPDATE') ELSE false END)
              OR EXISTS(SELECT FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
                  AND p.oid<>'cp6_tenancy_v1.current_tenant_id()'::regprocedure AND pg_catalog.has_function_privilege(r.oid,p.oid,'EXECUTE'))))
            """);

        async Task Execute(string sql)
        { await using var command = new NpgsqlCommand(sql, connection, transaction); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        async Task Require(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("roles", roles); command.Parameters.AddWithValue("tables", Tables.ToArray());
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new InvalidOperationException("Identity directory read isolation preconditions were rejected; roll back the control transaction.");
        }
        async Task ValidateColumns(string table, string[] names, string[] types, bool[] notNull, string[] key)
        {
            await using var command = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM pg_catalog.pg_attribute WHERE attrelid=@table::regclass AND attnum>0)=cardinality(@names)
                  AND (SELECT count(*) FROM unnest(@names::text[],@types::text[],@notNull::bool[]) expected(name,type,not_null)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=@table::regclass AND a.attname=expected.name
                    WHERE a.atttypid=expected.type::regtype AND a.attnotnull=expected.not_null AND NOT a.attisdropped
                      AND a.attidentity='' AND a.attgenerated='' AND NOT a.atthasdef)=cardinality(@names)
                  AND (SELECT array_agg(a.attname::text ORDER BY columns.position) FROM pg_catalog.pg_constraint c
                    CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY columns(number,position)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=columns.number
                    WHERE c.conrelid=@table::regclass AND c.contype='p' AND c.convalidated AND NOT c.condeferrable)=@key
                """, connection, transaction);
            command.Parameters.AddWithValue("table", table); command.Parameters.AddWithValue("names", names);
            command.Parameters.AddWithValue("types", types); command.Parameters.AddWithValue("notNull", notNull); command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true)
                throw new InvalidOperationException("Identity directory table shape was rejected; roll back the control transaction.");
        }
    }

    private static string Q(string value) => new NpgsqlCommandBuilder().QuoteIdentifier(value);
}
