using System.Text;
using System.Text.RegularExpressions;

namespace CP6.Core.Persistence;

/// <summary>Shared v1 storage prerequisites; existing definitions are verified, never replaced.</summary>
public static class PostgreSqlMigrationPrerequisitesV1
{
    public const string Collation = "cp6_ci_as_provider_v1";
    public const string CollationAnnotation = "Npgsql:CollationDefinition:" + Collation;
    public const string CollationDefinition = "und-u-ks-level2,und-u-ks-level2,icu,False";
    public static string FunctionsSql => PostgreSqlManagedTextV1.CreateFunctions("public", Collation);
    public static string InstallSql { get; } = BuildInstallSql();

    private static string BuildInstallSql()
    {
        var sql = new StringBuilder("""
            DO $cp6_prerequisites_v1$
            DECLARE object_id oid;
            BEGIN
              SELECT c.oid INTO object_id FROM pg_catalog.pg_collation c
              JOIN pg_catalog.pg_namespace n ON n.oid=c.collnamespace
              WHERE n.nspname='public' AND c.collname='cp6_ci_as_provider_v1';
              IF object_id IS NULL THEN
                CREATE COLLATION public.cp6_ci_as_provider_v1
                  (provider=icu, locale='und-u-ks-level2', deterministic=false);
              ELSIF NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_collation c WHERE c.oid=object_id
                  AND c.collprovider='i' AND NOT c.collisdeterministic AND c.collencoding=-1
                  AND COALESCE(to_jsonb(c)->>'colllocale',to_jsonb(c)->>'colliculocale') COLLATE "C" = 'und-u-ks-level2'
                  AND COALESCE(to_jsonb(c)->>'collicurules','') = ''
              ) THEN
                RAISE EXCEPTION 'CP6 PostgreSQL v1 collation definition mismatch' USING ERRCODE='P0001';
              END IF;

            """);

        // Extract complete definitions and bodies from the frozen installer, without changing that v1 source.
        var functions = Regex.Matches(FunctionsSql,
            "CREATE FUNCTION \"public\"\\.(?<name>cp6_[a-z0-9_]+)\\((?<args>[^)]*)\\)\\s+"
            + "RETURNS (?<returns>boolean|integer) LANGUAGE plpgsql IMMUTABLE STRICT PARALLEL SAFE AS \\$cp6\\$"
            + "(?<body>.*?)\\$cp6\\$;", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (functions.Count != 2
            || functions[0].Groups["name"].Value != "cp6_text_range_v1"
            || functions[1].Groups["name"].Value != "cp6_cp936_length_v1")
            throw new InvalidOperationException("The frozen PostgreSQL v1 text-function installer changed unexpectedly.");

        foreach (Match function in functions)
        {
            var name = function.Groups["name"].Value;
            var range = name == "cp6_text_range_v1";
            var signature = range ? "public.cp6_text_range_v1(text,text)" : "public.cp6_cp936_length_v1(text)";
            var argumentNames = range ? "ARRAY['value','kind']::text[]" : "ARRAY['value']::text[]";
            var returnType = range ? "boolean" : "integer";
            var body = Literal(function.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal));
            sql.AppendLine($$"""
                  object_id := pg_catalog.to_regprocedure('{{signature}}');
                  IF object_id IS NULL THEN
                {{function.Value}}
                  ELSIF NOT EXISTS (
                    SELECT 1 FROM pg_catalog.pg_proc p
                    JOIN pg_catalog.pg_language l ON l.oid=p.prolang
                    WHERE p.oid=object_id AND p.prokind='f' AND l.lanname='plpgsql'
                      AND p.prorettype='{{returnType}}'::regtype AND NOT p.proretset
                      AND p.provolatile='i' AND p.proisstrict AND p.proparallel='s'
                      AND NOT p.prosecdef AND NOT p.proleakproof AND p.proconfig IS NULL
                      AND p.proargnames={{argumentNames}} AND p.proargmodes IS NULL AND p.proallargtypes IS NULL
                      AND p.pronargdefaults=0 AND p.proargdefaults IS NULL
                      AND p.probin IS NULL AND p.procost=100 AND p.prorows=0 AND p.prosupport=0
                      AND convert_to(replace(p.prosrc,E'\r\n',E'\n'),'UTF8')=convert_to({{body}},'UTF8')
                  ) THEN
                    RAISE EXCEPTION 'CP6 PostgreSQL v1 function definition mismatch: {{name}}' USING ERRCODE='P0001';
                  END IF;
                """);
        }
        sql.AppendLine("END $cp6_prerequisites_v1$;");
        return sql.ToString();
    }

    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
