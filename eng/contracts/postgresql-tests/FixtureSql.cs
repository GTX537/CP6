using System.Text;
using System.Text.RegularExpressions;

namespace CP6.ExactValues.PostgreSqlTests;

internal static class FixtureSql
{
    internal static string Quote(string identifier)
    {
        if (!Regex.IsMatch(identifier, @"\A[A-Za-z][A-Za-z0-9_]*\z")) throw new InvalidOperationException("UNTRUSTED_FIXTURE_IDENTIFIER");
        return '"' + identifier + '"';
    }

    internal static string Build(string repository)
    {
        var result = new StringBuilder();
        foreach (var file in new[] { "exact-guards.sql", "atp-reviewed-subset.sql", "atp-recovery-subset.sql" })
            result.AppendLine(File.ReadAllText(Path.Combine(repository, "eng/contracts/postgresql-tests", file)));
        var fields = TestInputs.Inventory;
        if (fields.Length != 66 || fields.Select(x => x.Key).Distinct().Count() != 66 || fields.Count(x => x.Nullable) != 15 || fields.Select(x => x.Table).Distinct().Count() != 13)
            throw new InvalidOperationException("INVENTORY_MAPPING_COVERAGE_CHANGED");
        foreach (var table in fields.GroupBy(x => x.Table))
        {
            result.AppendLine($"CREATE TABLE exact_fixture.{Quote(table.Key)} (id text PRIMARY KEY,");
            var definitions = new List<string>();
            foreach (var field in table)
            {
                var required = field.Nullable ? "" : " NOT NULL";
                definitions.Add($"{Quote(field.Digits)} text{required}, {Quote(field.Scale)} smallint{required}, {Quote(field.Wire)} text{required}");
                definitions.Add($"CHECK (exact_fixture.inventory_valid({Quote(field.Digits)}, {Quote(field.Scale)}, {Quote(field.Wire)}, '{field.Profile}', {field.Nullable.ToString().ToLowerInvariant()}, '{field.Rule}'))");
                definitions.Add($"CHECK ({Quote(field.Scale)} BETWEEN 0 AND {field.MaximumScale})");
            }
            if (table.Key == "MovingAveragePoolRevision")
            {
                definitions.Add("state text NOT NULL CHECK (state IN ('ACTIVE','EMPTY'))");
                var average = table.Where(x => x.Nullable).ToArray();
                if (average.Length != 5) throw new InvalidOperationException("MAVG_OPTIONAL_FIELDS_CHANGED");
                var active = string.Join(" AND ", average.Select(x => Quote(x.Digits) + " IS NOT NULL"));
                var empty = string.Join(" AND ", average.Select(x => Quote(x.Digits) + " IS NULL"));
                definitions.Add($"""
                    CHECK ((state='ACTIVE' AND "AfterQtyUnscaledDigits"<>'0' AND left("AfterQtyUnscaledDigits",1)<>'-' AND {active})
                      OR (state='EMPTY' AND "AfterQtyUnscaledDigits"='0' AND left("AfterQtyOriginalWire",1)<>'-'
                        AND "AfterUnroundedValueUnscaledDigits"='0' AND {empty}))
                    """);
            }
            result.AppendLine(string.Join(",\n", definitions));
            result.AppendLine(");");
        }
        return result.ToString();
    }
}
