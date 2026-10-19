using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Fresh-only directory profile with minimal current-authorization reads. The caller owns
/// the transaction and must roll back any failure; an installed v1 or another profile is not upgraded.</summary>
internal static class IdentityDirectoryAuthorizationV2
{
    internal const string ModelVersion = "identity-directory-authorized-read-v2";
    internal const string MigrationVersion = "identity-directory-authorized-read-rls-v2";
    internal const string TenantTable = "public.\"Sys_Tenants\"";
    internal const string TokenTable = "crm_identity.\"ServiceToken\"";
    internal static readonly IReadOnlyList<string> TenantColumns = Array.AsReadOnly(new[] { "Id", "Enable", "ExpireDate" });
    internal static readonly IReadOnlyList<string> TokenColumns = Array.AsReadOnly(new[] { "Issuer", "Jti", "ClientId", "TenantId", "ExpiresAtUtc", "RevokedAtUtc" });

    internal static async Task InstallAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyCollection<TenantLoginV1> logins, CancellationToken ct = default)
    {
        var bindings = logins?.ToArray() ?? throw new ArgumentNullException(nameof(logins));
        // Reuse the complete fresh-layout/role/catalog checks and three-table v1 isolation.
        // No grant for the additional tables exists until all v1 checks have succeeded.
        await IdentityDirectoryRowSecurityV1.InstallAsync(connection, transaction, bindings, ct).ConfigureAwait(false);
        await Execute($"LOCK TABLE {TenantTable}, {TokenTable} IN ACCESS EXCLUSIVE MODE");
        await Require("""
            SELECT (SELECT count(*) FROM pg_catalog.pg_class c WHERE c.oid=ANY(@tables::regclass[])
              AND c.relkind='r' AND c.relowner=current_user::regrole AND NOT c.relrowsecurity AND NOT c.relforcerowsecurity
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_policy p WHERE p.polrelid=c.oid)
              AND NOT EXISTS(SELECT FROM pg_catalog.pg_rewrite r WHERE r.ev_class=c.oid))=2
            """);
        await ValidateColumns(TenantTable, TenantColumns.ToArray(), ["uuid", "bool", "timestamp"], [true, true, false], ["Id"]);
        await ValidateColumns(TokenTable, TokenColumns.ToArray(), ["bpchar", "bpchar", "bpchar", "uuid", "timestamptz", "timestamptz"],
            [true, true, true, true, true, false], ["Issuer", "Jti"]);
        string owner;
        await using (var command = new NpgsqlCommand("SELECT current_user::text", connection, transaction))
            owner = (string)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        var roles = string.Join(",", bindings.Select(x => Q(x.Login)));
        foreach (var (table, key, columns) in new[]
        {
            (TenantTable, "Id", TenantColumns), (TokenTable, "TenantId", TokenColumns)
        })
            await Execute($$"""
                ALTER TABLE {{table}} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE {{table}} FORCE ROW LEVEL SECURITY;
                CREATE POLICY directory_authorization_read_v2 ON {{table}} FOR SELECT TO {{roles}}
                    USING ({{Q(key)}}=cp6_tenancy_v1.current_tenant_id());
                CREATE POLICY directory_authorization_control_v2 ON {{table}} FOR ALL TO {{Q(owner)}} USING (true) WITH CHECK (true);
                GRANT SELECT ({{string.Join(",", columns.Select(Q))}}) ON {{table}} TO {{roles}};
                """);
        // Column grants must not silently become table-wide reads or grant options.
        await Require("""
            SELECT NOT EXISTS(SELECT FROM pg_catalog.pg_roles r WHERE r.rolname=ANY(@roles) AND (
              EXISTS(SELECT FROM unnest(@tables::regclass[]) t(oid) WHERE
                pg_catalog.has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER'))
              OR EXISTS(SELECT FROM pg_catalog.pg_attribute a WHERE a.attrelid=ANY(@tables::regclass[])
                AND a.attnum>0 AND NOT a.attisdropped AND (
                  (pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT') AND NOT
                    ((a.attrelid='public."Sys_Tenants"'::regclass AND a.attname=ANY(@tenantColumns)) OR
                     (a.attrelid='crm_identity."ServiceToken"'::regclass AND a.attname=ANY(@tokenColumns))))
                  OR pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'INSERT,UPDATE,REFERENCES')
                  OR pg_catalog.has_column_privilege(r.oid,a.attrelid,a.attnum,'SELECT WITH GRANT OPTION')))))
            """);

        async Task Execute(string sql)
        { await using var command = new NpgsqlCommand(sql, connection, transaction); await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
        async Task Require(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("tables", new[] { TenantTable, TokenTable });
            command.Parameters.AddWithValue("roles", bindings.Select(x => x.Login).ToArray());
            command.Parameters.AddWithValue("tenantColumns", TenantColumns.ToArray());
            command.Parameters.AddWithValue("tokenColumns", TokenColumns.ToArray());
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        }
        async Task ValidateColumns(string table, string[] names, string[] types, bool[] notNull, string[] key)
        {
            await using var command = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM unnest(@names::text[],@types::text[],@notNull::bool[]) expected(name,type,not_null)
                  JOIN pg_catalog.pg_attribute a ON a.attrelid=@table::regclass AND a.attname=expected.name
                  WHERE a.atttypid=expected.type::regtype AND a.attnotnull=expected.not_null AND NOT a.attisdropped
                    AND a.attidentity='' AND a.attgenerated='')=cardinality(@names)
                  AND (SELECT array_agg(a.attname::text ORDER BY columns.position) FROM pg_catalog.pg_constraint c
                    CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY columns(number,position)
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=columns.number
                    WHERE c.conrelid=@table::regclass AND c.contype='p' AND c.convalidated AND NOT c.condeferrable)=@key
                """, connection, transaction);
            command.Parameters.AddWithValue("table", table); command.Parameters.AddWithValue("names", names);
            command.Parameters.AddWithValue("types", types); command.Parameters.AddWithValue("notNull", notNull); command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not true) throw TenantStorageBindingV1.Rejected();
        }
    }
    private static string Q(string value) => new NpgsqlCommandBuilder().QuoteIdentifier(value);
}
