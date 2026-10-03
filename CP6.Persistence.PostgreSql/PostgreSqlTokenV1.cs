using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Persistence.PostgreSql;

internal readonly record struct PostgreSqlTokenTableV1(string Schema, string Table, string Column);

/// <summary>Immutable v1 installation; manifests belong to each frozen baseline.</summary>
internal static class PostgreSqlTokenV1
{
    public static void Install(MigrationBuilder migration, string tokenSchema, IReadOnlyList<PostgreSqlTokenTableV1> tables)
    {
        migration.EnsureSchema(tokenSchema);
        var schema = Quote(tokenSchema);
        migration.Sql($$"""
            CREATE SEQUENCE {{schema}}.rowversion_v1 AS bigint MINVALUE 1 START WITH 1 NO CYCLE;
            CREATE FUNCTION {{schema}}.set_rowversion_v1()
            RETURNS trigger LANGUAGE plpgsql AS $cp6$
            BEGIN
              NEW."RowVersion" := pg_catalog.int8send(nextval('{{schema}}.rowversion_v1'::regclass));
              RETURN NEW;
            END $cp6$;
            """);
        foreach (var table in tables)
        {
            if (table.Column != "RowVersion") throw new InvalidOperationException("Unexpected v1 token column.");
            migration.Sql($"CREATE TRIGGER cp6_rowversion_v1 BEFORE INSERT OR UPDATE ON {Quote(table.Schema)}.{Quote(table.Table)} FOR EACH ROW EXECUTE FUNCTION {schema}.set_rowversion_v1();");
        }
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
