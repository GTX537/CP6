using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CP6.Core.Persistence;

internal static class PostgreSqlModelIdentifiers
{
    public static void Apply(IMutableModel model)
    {
        var entities = model.GetEntityTypes().Where(entity => entity.GetTableName() is not null).ToArray();
        var originals = entities.ToDictionary(entity => entity,
            entity => StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema()));
        var names = new Dictionary<(string Schema, string Scope, string Name), string>();

        void Claim(string? schema, string scope, string name, string owner)
        {
            var key = (schema ?? "public", scope, name);
            if (names.TryGetValue(key, out var existing) && existing != owner)
                throw new InvalidOperationException($"PostgreSQL identifier collision between {existing} and {owner}.");
            names[key] = owner;
        }

        foreach (var entity in entities)
        {
            var original = originals[entity];
            var table = Shorten(original.Name);
            Claim(original.Schema, "relation", table, $"table:{original.Schema}.{original.Name}");
            entity.SetAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation, original.Name);
            if (table != original.Name) entity.SetTableName(table);
            var columns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in entity.GetProperties())
            {
                var originalColumn = property.GetColumnName(original)!;
                var column = Shorten(originalColumn);
                if (!columns.Add(column)) throw new InvalidOperationException($"PostgreSQL column identifier collision on {entity.Name}.");
                if (column != originalColumn) property.SetColumnName(column);
            }
        }

        foreach (var entity in entities)
        {
            var original = originals[entity];
            string Columns(IEnumerable<IReadOnlyProperty> properties) => string.Join("_", properties.Select(property =>
                property.FindAnnotation(RelationalAnnotationNames.ColumnName)?.Value as string ?? property.Name));
            foreach (var key in entity.GetKeys())
            {
                var full = key.FindAnnotation(RelationalAnnotationNames.Name)?.Value as string ??
                    (key.IsPrimaryKey() ? $"PK_{original.Name}" : $"AK_{original.Name}_{Columns(key.Properties)}");
                var name = Shorten(full);
                key.SetAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation, full);
                key.SetName(name);
                Claim(original.Schema, "relation", name, $"key:{entity.Name}:{string.Join(',', key.Properties.Select(property => property.Name))}");
                Claim(original.Schema, $"constraint:{entity.GetTableName()}", name, $"key:{entity.Name}:{full}");
            }
            foreach (var index in entity.GetIndexes())
            {
                var full = index.FindAnnotation(RelationalAnnotationNames.Name)?.Value as string ??
                    $"IX_{original.Name}_{Columns(index.Properties)}";
                var name = Shorten(full);
                index.SetAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation, full);
                index.SetDatabaseName(name);
                Claim(original.Schema, "relation", name, $"index:{entity.Name}:{string.Join(',', index.Properties.Select(property => property.Name))}");
            }
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = originals[foreignKey.PrincipalEntityType];
                var full = foreignKey.FindAnnotation(RelationalAnnotationNames.Name)?.Value as string ??
                    $"FK_{original.Name}_{principal.Name}_{Columns(foreignKey.Properties)}";
                var name = Shorten(full);
                foreignKey.SetAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation, full);
                foreignKey.SetConstraintName(name);
                Claim(original.Schema, $"constraint:{entity.GetTableName()}", name, $"foreign:{entity.Name}:{full}");
            }
            foreach (var check in entity.GetCheckConstraints())
            {
                var full = check.Name ?? throw new InvalidOperationException($"Unnamed PostgreSQL check constraint on {entity.Name}.");
                var name = Shorten(full);
                check.SetAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation, full);
                check.Name = name;
                Claim(original.Schema, $"constraint:{entity.GetTableName()}", name, $"check:{entity.Name}:{full}");
            }
        }
        model.SetMaxIdentifierLength(63);
    }

    private static string Shorten(string identifier)
    {
        if (Encoding.UTF8.GetByteCount(identifier) <= 63) return identifier;
        var suffix = "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..12].ToLowerInvariant();
        var prefix = new StringBuilder();
        var length = 0;
        foreach (var rune in identifier.EnumerateRunes())
        {
            if (length + rune.Utf8SequenceLength + suffix.Length > 63) break;
            prefix.Append(rune.ToString());
            length += rune.Utf8SequenceLength;
        }
        return prefix + suffix;
    }
}
