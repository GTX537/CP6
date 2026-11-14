using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>Verified function and trigger catalog of the complete PostgreSQL Core migration layout.</summary>
internal static class IdentityDirectoryCoreCatalogV1
{
    // PostgreSQL 18 migrated through 20261114030500_StockOrdinaryWriteAuthorityV1.
    private const string Fingerprint = "a7244dc8b3a3f7aff90e6529d8b0419ca02d9b9c323abb354c7f874591fad619";

    internal static async Task<bool> MatchesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var preflight = new NpgsqlCommand("""
            SELECT current_setting('search_path') WHERE pg_catalog.to_regclass('public."__EFMigrationsHistory"') IS NOT NULL
            """, connection, transaction);
        if (await preflight.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string originalSearchPath)
            return false;

        await using var setting = new NpgsqlCommand("SELECT pg_catalog.set_config('search_path', @path, true)", connection, transaction);
        setting.Parameters.AddWithValue("path", "pg_catalog");
        await using var fingerprint = new NpgsqlCommand(FingerprintSql, connection, transaction);
        await setting.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        var observed = await fingerprint.ExecuteScalarAsync(ct).ConfigureAwait(false);
        setting.Parameters["path"].Value = originalSearchPath;
        await setting.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return observed is string current && string.Equals(current, Fingerprint, StringComparison.Ordinal);
    }

    internal static async Task RevokePublicExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var grants = new NpgsqlCommand("""
            SELECT string_agg(pg_catalog.format('REVOKE EXECUTE ON %s %I.%I(%s) FROM PUBLIC',
                CASE WHEN p.prokind='p' THEN 'PROCEDURE' ELSE 'FUNCTION' END,
                n.nspname,p.proname,pg_catalog.pg_get_function_identity_arguments(p.oid)),';')
            FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema'
              AND EXISTS(SELECT FROM pg_catalog.aclexplode(coalesce(p.proacl,pg_catalog.acldefault('f',p.proowner))) a
                WHERE a.grantee=0 AND a.privilege_type='EXECUTE')
            """, connection, transaction);
        if (await grants.ExecuteScalarAsync(ct).ConfigureAwait(false) is string revoke)
        {
            await using var command = new NpgsqlCommand(revoke, connection, transaction);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    // Deparsed definitions include function flags, SET options and full bodies. Trigger
    // definitions and enable states pin the use of reviewed functions as well as their definitions.
    private const string FingerprintSql = """
        SELECT encode(sha256(convert_to(jsonb_build_array(
            (SELECT jsonb_agg("MigrationId" ORDER BY "MigrationId" COLLATE "C") FROM public."__EFMigrationsHistory"),
            (SELECT jsonb_agg(jsonb_build_array(n.nspname,p.proname,pg_get_function_identity_arguments(p.oid),
                CASE WHEN p.prokind='a' THEN NULL ELSE replace(pg_get_functiondef(p.oid),chr(13)||chr(10),chr(10)) END,
                p.proowner=current_user::regrole,
                coalesce((SELECT jsonb_agg(jsonb_build_array(CASE WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee)::text END,
                           a.privilege_type,a.is_grantable) ORDER BY a.grantee,a.privilege_type COLLATE "C")
                          FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a WHERE a.grantee<>p.proowner),'[]'),
                (SELECT coalesce(jsonb_agg(jsonb_build_array(ns.nspname,c.relname,t.tgname,t.tgtype,t.tgenabled,
                           replace(pg_get_triggerdef(t.oid),chr(13)||chr(10),chr(10)))
                           ORDER BY ns.nspname COLLATE "C",c.relname COLLATE "C",t.tgname COLLATE "C"),'[]')
                 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace ns ON ns.oid=c.relnamespace
                 WHERE t.tgfoid=p.oid AND NOT t.tgisinternal))
                 ORDER BY n.nspname COLLATE "C",p.proname COLLATE "C",pg_get_function_identity_arguments(p.oid) COLLATE "C")
             FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
             WHERE n.nspname !~ '^pg_' AND n.nspname<>'information_schema')
        )::text,'UTF8')),'hex')
        """;
}
