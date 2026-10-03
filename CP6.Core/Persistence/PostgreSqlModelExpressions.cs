using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CP6.Core.Persistence;

/// <summary>Translates only the check/filter grammar present in the audited shared model.</summary>
internal static class PostgreSqlModelExpressions
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IS", "NULL", "IN", "BETWEEN", "LIKE", "LEN", "ISJSON", "YEAR", "MONTH", "TRUE", "FALSE"
    };

    public static string Utf16Length(string value) =>
        $"char_length(regexp_replace(({value})::text COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g'))";

    // A bpchar::text cast drops U+0020 tails; bpcharsend exposes the stored bytes without changing the value.
    public static string RawText(string column) => $"convert_from(pg_catalog.bpcharsend({column}), 'UTF8')";

    public static string Translate(IMutableEntityType entity, StoreObjectIdentifier store, string sql)
    {
        var properties = entity.GetProperties().ToDictionary(property => property.GetColumnName(store)!, StringComparer.Ordinal);
        var literals = new List<string>();
        var result = new StringBuilder();
        var masked = new StringBuilder();
        for (var position = 0; position < sql.Length; position++)
        {
            var current = sql[position];
            if (current == '\'')
            {
                var start = position;
                var closed = false;
                while (++position < sql.Length)
                    if (sql[position] == '\'')
                    {
                        if (position + 1 < sql.Length && sql[position + 1] == '\'') { position++; continue; }
                        closed = true;
                        break;
                    }
                if (!closed) throw Unclassified(entity);
                literals.Add(sql[start..(position + 1)]);
                result.Append($"__cp6_literal_{literals.Count - 1}__");
                masked.Append(' ');
            }
            else if (current is '[' or '"')
            {
                var end = sql.IndexOf(current == '[' ? ']' : '"', position + 1);
                if (end < 0) throw Unclassified(entity);
                var column = sql[(position + 1)..end];
                if (!properties.ContainsKey(column)) throw new InvalidOperationException($"Unknown check/filter column {entity.Name}.{column}.");
                result.Append(PostgreSqlModelConfiguration.Quote(column));
                masked.Append(' ');
                position = end;
            }
            else { result.Append(current); masked.Append(current); }
        }
        foreach (Match word in Regex.Matches(masked.ToString(), @"\b[A-Za-z_][A-Za-z_0-9]*\b", RegexOptions.CultureInvariant))
            if (!Words.Contains(word.Value)) throw Unclassified(entity);
        if (Regex.IsMatch(masked.ToString(), @"[^\sA-Za-z_0-9().,+*/<>=!\-]", RegexOptions.CultureInvariant))
            throw Unclassified(entity);

        var translated = result.ToString();
        foreach (var property in properties.Values.Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(bool)))
        {
            var column = PostgreSqlModelConfiguration.Quote(property.GetColumnName(store)!);
            var escaped = Regex.Escape(column);
            translated = Regex.Replace(translated, $@"{escaped}\s*(=|<>|!=)\s*([01])\b",
                match => $"{column} {match.Groups[1].Value} {(match.Groups[2].Value == "1" ? "TRUE" : "FALSE")}", Options);
            translated = Regex.Replace(translated, $@"\b([01])\s*(=|<>|!=)\s*{escaped}",
                match => $"{(match.Groups[1].Value == "1" ? "TRUE" : "FALSE")} {match.Groups[2].Value} {column}", Options);
            if (Regex.IsMatch(translated, $@"{escaped}\s*(?:=|<>|!=|IN|BETWEEN)\s*\(?[01]\b", Options))
                throw Unclassified(entity);
        }

        translated = Regex.Replace(translated, "ISJSON\\s*\\(\\s*(\"[^\"]+\")\\s*\\)\\s*=\\s*1\\b", match =>
        {
            var column = match.Groups[1].Value;
            // SQL Server ISJSON without a type argument accepts objects/arrays and propagates NULL.
            return $"(CASE WHEN {column} IS NULL THEN NULL ELSE ({column} IS JSON OBJECT OR {column} IS JSON ARRAY) END)";
        }, Options);
        translated = Regex.Replace(translated, "LEN\\s*\\(\\s*(\"[^\"]+\")\\s*\\)",
            match => Utf16Length($"rtrim({RawText(match.Groups[1].Value)}, ' ')") , Options);
        translated = Regex.Replace(translated, "(YEAR|MONTH)\\s*\\(\\s*(\"[^\"]+\")\\s*\\)",
            match => $"EXTRACT({match.Groups[1].Value.ToUpperInvariant()} FROM {match.Groups[2].Value})::integer", Options);
        translated = Regex.Replace(translated, "(\"[^\"]+\")\\s+NOT\\s+LIKE\\s+__cp6_literal_(\\d+)__", match =>
        {
            var literal = literals[int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)];
            var range = literal switch
            {
                "'%[^0-9a-f]%'" => "hex",
                "'%[^A-Z]%'" => "alpha",
                _ => throw Unclassified(entity)
            };
            // The managed function compares character weights in the supported CI/AS collation.
            // Native range equivalence remains a real-database gate, not an ASCII regex assumption.
            return $"public.cp6_text_range_v1({RawText(match.Groups[1].Value)}, '{range}')";
        }, Options);
        if (Regex.IsMatch(translated, @"\b(?:LEN|ISJSON|YEAR|MONTH)\s*\(|\bLIKE\b", Options)) throw Unclassified(entity);
        for (var index = 0; index < literals.Count; index++)
            translated = translated.Replace($"__cp6_literal_{index}__", literals[index], StringComparison.Ordinal);
        return translated;
    }

    private static InvalidOperationException Unclassified(IReadOnlyEntityType entity) =>
        new($"Unclassified PostgreSQL check/filter grammar on {entity.Name}; preserve and audit its SQL Server rule before adapting it.");
}
