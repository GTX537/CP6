using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CP6.Core.Persistence;

/// <summary>PostgreSQL storage differences, applied after the shared business model.</summary>
public static partial class PostgreSqlModelConfiguration
{
    public const string OriginalNameAnnotation = "CP6:OriginalName";
    public const string OriginalSqlAnnotation = "CP6:OriginalSql";
    public const string TimeContractAnnotation = "CP6:TimeContract";
    public const string DatabaseTokenAnnotation = "CP6:DatabaseToken";
    public const string BusinessTextCollation = "cp6_ci_as_provider_v1";

    private static readonly ValueConverter<DateTime, DateTime> UtcDateTime = new(
        value => RequireUtc(value), value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static readonly ValueConverter<DateTime, DateTime> WallClockDateTime = new(
        value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
        value => DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcOffset = new(
        value => RequireZeroOffset(value), value => RequireZeroOffset(value));

    public static void Apply(ModelBuilder modelBuilder,
        IReadOnlySet<(Type EntityType, string Property)>? utcDateTimes = null,
        IReadOnlyDictionary<(string EntityFullName, string Property), int>? fixedAsciiDomains = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasCollation(BusinessTextCollation, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        modelBuilder.UseCollation(BusinessTextCollation);
        var model = modelBuilder.Model;
        var entities = model.GetEntityTypes().Where(entity => entity.GetTableName() is not null).ToArray();
        if (utcDateTimes is not null)
            foreach (var (entityType, propertyName) in utcDateTimes)
            {
                var property = entities.SingleOrDefault(entity => entity.ClrType == entityType)?.FindProperty(propertyName);
                if (property is null || Underlying(property.ClrType) != typeof(DateTime))
                    throw new InvalidOperationException($"Unmapped UTC DateTime classification: {entityType.FullName}.{propertyName}.");
            }

        foreach (var entity in entities)
        {
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var property in entity.GetProperties())
            {
                var originalType = property.FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value as string;
                var type = Underlying(property.ClrType);
                var token = type == typeof(byte[]) && property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate;
                var fixedText = type == typeof(string) && (property.IsFixedLength() == true ||
                    originalType is not null && Regex.IsMatch(originalType, @"\A(?:nchar|char)\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                if (fixedText)
                {
                    if (fixedAsciiDomains is null || !fixedAsciiDomains.TryGetValue((entity.ClrType.FullName!, property.Name), out var capacity) ||
                        StorageLimit(property, originalType) != capacity || IsUnicodeStorage(property, originalType))
                        throw new InvalidOperationException($"Unclassified fixed text domain on {entity.Name}.{property.Name}; general Unicode fixed padding is not supported.");
                    property.SetAnnotation("CP6:FixedAsciiDomain", true);
                    var raw = PostgreSqlModelExpressions.RawText(Quote(property.GetColumnName(store)!));
                    var check = entity.AddCheckConstraint($"CK_{store.Name}_{property.GetColumnName(store)}_AsciiDomain",
                        $"octet_length({raw}) = char_length({raw})");
                    check.SetAnnotation("CP6:GeneratedCapacity", true);
                    check.SetAnnotation("CP6:FixedAsciiDomain", true);
                }
                RemoveSqlServerAnnotations(property);
                if (token)
                {
                    property.SetColumnType("bytea");
                    property.SetMaxLength(8);
                    property.IsNullable = false;
                    property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    property.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
                    property.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
                    property.SetAnnotation(DatabaseTokenAnnotation, true);
                    AddCapacityCheck(entity, property, store, "TokenLength", $"octet_length({Quote(property.GetColumnName(store)!)}) = 8");
                }
                else if (type == typeof(DateTime))
                {
                    var isUtc = utcDateTimes?.Contains((entity.ClrType, property.Name)) == true;
                    property.SetColumnType(isUtc ? "timestamp with time zone" : "timestamp without time zone");
                    property.SetValueConverter(isUtc ? UtcDateTime : WallClockDateTime);
                    property.SetAnnotation(TimeContractAnnotation, isUtc ? "UtcInstant" : "PreservedWallClock");
                }
                else if (type == typeof(DateTimeOffset))
                {
                    property.SetColumnType("timestamp with time zone");
                    property.SetValueConverter(UtcOffset);
                    property.SetAnnotation(TimeContractAnnotation, "UtcOffsetZero");
                }
                else if (type == typeof(DateOnly))
                {
                    property.SetColumnType("date");
                    property.SetAnnotation(TimeContractAnnotation, "CalendarDate");
                }
                else
                {
                    // SQL Server's smallint mapping supplies an Int16 enum conversion even without HasConversion.
                    // Npgsql otherwise retains the enum's Int32 underlying type; make that storage contract explicit.
                    if (type.IsEnum && property.GetValueConverter() is null && property.GetProviderClrType() is null &&
                        string.Equals(originalType, "smallint", StringComparison.OrdinalIgnoreCase))
                    {
                        var converterType = typeof(EnumToNumberConverter<,>).MakeGenericType(type, typeof(short));
                        property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType, new object?[] { null })!);
                    }
                    property.SetColumnType(MapType(property, originalType));
                    if (type == typeof(byte[]) && StorageLimit(property, originalType) is { } byteLimit)
                    {
                        // Replay input is ordinary data, not a generated token. SQL varbinary(n) is a capacity, not padding.
                        AddCapacityCheck(entity, property, store, "BinaryCapacity",
                            $"octet_length({Quote(property.GetColumnName(store)!)}) <= {byteLimit.ToString(CultureInfo.InvariantCulture)}");
                    }
                }

                if (type == typeof(string))
                {
                    var collation = property.GetCollation();
                    if (collation == "Latin1_General_100_BIN2") property.SetCollation("C");
                    else if (collation is not null && collation != BusinessTextCollation)
                        throw new InvalidOperationException($"Unclassified SQL Server text collation on {entity.Name}.{property.Name}.");
                    else property.SetCollation(BusinessTextCollation);

                    if (StorageLimit(property, originalType) is { } limit && IsUnicodeStorage(property, originalType))
                        AddCapacityCheck(entity, property, store, "Utf16Capacity",
                            $"{PostgreSqlModelExpressions.Utf16Length(PostgreSqlModelExpressions.RawText(Quote(property.GetColumnName(store)!)))} <= {limit.ToString(CultureInfo.InvariantCulture)}");
                    else if (StorageLimit(property, originalType) is { } codePageLimit)
                        AddCapacityCheck(entity, property, store, "Cp936Capacity",
                            $"public.cp6_cp936_length_v1({PostgreSqlModelExpressions.RawText(Quote(property.GetColumnName(store)!))}) <= {codePageLimit.ToString(CultureInfo.InvariantCulture)}");
                }

                if (property.GetDefaultValueSql() is not null || property.GetComputedColumnSql() is not null)
                    throw new InvalidOperationException($"Unclassified SQL expression on {entity.Name}.{property.Name}.");
            }

            foreach (var index in entity.GetIndexes())
            {
                // The SQL provider adds these convention filters only while finalizing its model, after this adapter.
                // An explicit HasFilter(null) remains distinct from an absent filter annotation.
                if (index.IsUnique && index.FindAnnotation(RelationalAnnotationNames.Filter) is null &&
                    index.FindAnnotation("SqlServer:Clustered")?.Value is not true &&
                    !(entity.ClrType.FullName == "CP6.Entity.DomainModels.Sys.Sys_Lang" &&
                      index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "LangKey"])))
                {
                    var nullableColumns = index.Properties.Where(property => property.IsNullable)
                        .Select(property => property.GetColumnName(store)!).ToArray();
                    if (nullableColumns.Length != 0)
                        index.SetFilter(string.Join(" AND ", nullableColumns.Select(column => $"[{column}] IS NOT NULL")));
                }
                if (index.GetFilter() is { } filter)
                {
                    index.SetAnnotation(OriginalSqlAnnotation, filter);
                    index.SetFilter(PostgreSqlModelExpressions.Translate(entity, store, filter));
                }
            }

            foreach (var constraint in entity.GetCheckConstraints().ToArray())
            {
                if (constraint.FindAnnotation("CP6:GeneratedCapacity")?.Value is true) continue;
                var originalName = constraint.Name;
                var sql = constraint.Sql;
                var annotations = constraint.GetAnnotations().ToArray();
                entity.RemoveCheckConstraint(constraint.ModelName);
                var adapted = entity.AddCheckConstraint(constraint.ModelName, PostgreSqlModelExpressions.Translate(entity, store, sql));
                adapted.Name = originalName;
                foreach (var annotation in annotations) adapted.SetAnnotation(annotation.Name, annotation.Value);
                adapted.SetAnnotation(OriginalSqlAnnotation, sql);
            }
            RemoveSqlServerAnnotations(entity);
        }

        var language = entities.SingleOrDefault(entity => entity.ClrType.FullName == "CP6.Entity.DomainModels.Sys.Sys_Lang");
        if (language is not null)
        {
            var globalAndTenantKey = language.GetIndexes().Single(index => index.IsUnique &&
                index.Properties.Select(property => property.Name).SequenceEqual(["TenantId", "LangKey"]));
            globalAndTenantKey.SetAreNullsDistinct(false);
        }
        RemoveSqlServerAnnotations(model);
        if (fixedAsciiDomains is not null)
            foreach (var (domain, _) in fixedAsciiDomains)
                if (entities.SingleOrDefault(entity => entity.ClrType.FullName == domain.EntityFullName)?.FindProperty(domain.Property)
                    ?.FindAnnotation("CP6:FixedAsciiDomain")?.Value is not true)
                    throw new InvalidOperationException("The audited fixed text manifest contains a missing or changed property.");
        PostgreSqlModelIdentifiers.Apply(model);
    }

    private static void AddCapacityCheck(IMutableEntityType entity, IMutableProperty property,
        StoreObjectIdentifier store, string suffix, string sql)
    {
        var name = $"CK_{store.Name}_{property.GetColumnName(store)}_{suffix}";
        var check = entity.AddCheckConstraint(name, sql);
        check.SetAnnotation("CP6:GeneratedCapacity", true);
    }

    private static bool IsUnicodeStorage(IReadOnlyProperty property, string? originalType) =>
        originalType is not null ? originalType.StartsWith("nvarchar", StringComparison.OrdinalIgnoreCase) ||
                                  originalType.StartsWith("nchar", StringComparison.OrdinalIgnoreCase)
            : property.IsUnicode() != false;

    private static int? StorageLimit(IReadOnlyProperty property, string? originalType)
    {
        if (property.GetMaxLength() is { } facet) return facet;
        if (originalType is null) return null;
        var sized = SizedType().Match(originalType.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal));
        return sized.Success && (sized.Groups[1].Value is "nvarchar" or "varchar" or "nchar" or "char" or "binary" or "varbinary") &&
            int.TryParse(sized.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var length) ? length : null;
    }

    private static string MapType(IReadOnlyProperty property, string? originalType)
    {
        var type = Underlying(property.GetValueConverter()?.ProviderClrType ?? property.GetProviderClrType() ?? property.ClrType);
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (originalType is not null)
        {
            var normalized = originalType.ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
            var sized = SizedType().Match(normalized);
            if (sized.Success)
            {
                var family = sized.Groups[1].Value;
                var size = sized.Groups[2].Value;
                return family switch
                {
                    // bpchar without a typmod retains variable storage and SQL PAD SPACE equality.
                    // The provider service supplies a non-trimming string mapping; capacity stays an explicit check.
                    "nvarchar" or "varchar" => "bpchar",
                    "nchar" or "char" => $"character({size})",
                    "binary" or "varbinary" => "bytea",
                    "decimal" or "numeric" => $"numeric({size})",
                    _ => throw new InvalidOperationException($"Unclassified SQL Server type {originalType}.")
                };
            }
            return normalized switch
            {
                "uniqueidentifier" => "uuid", "bit" => "boolean", "tinyint" => "smallint",
                "smallint" => "smallint", "int" => "integer", "bigint" => "bigint",
                "float" => "double precision", "real" => "real", "date" => "date",
                "text" or "ntext" => "bpchar", "image" => "bytea",
                "uuid" or "boolean" or "integer" or "bytea" or "doubleprecision" => originalType,
                _ => throw new InvalidOperationException($"Unclassified SQL Server type {originalType} on {property.DeclaringType.Name}.{property.Name}.")
            };
        }
        if (type == typeof(string))
            return property.IsFixedLength() == true && property.GetMaxLength() is { } length
                ? $"character({length.ToString(CultureInfo.InvariantCulture)})" : "bpchar";
        if (type == typeof(byte[])) return "bytea";
        if (type == typeof(Guid)) return "uuid";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(int)) return "integer";
        if (type == typeof(long)) return "bigint";
        if (type == typeof(short) || type == typeof(byte)) return "smallint";
        if (type == typeof(double)) return "double precision";
        if (type == typeof(float)) return "real";
        if (type == typeof(decimal)) return $"numeric({property.GetPrecision() ?? 18},{property.GetScale() ?? 2})";
        if (type == typeof(TimeSpan)) return "interval";
        throw new InvalidOperationException($"Unclassified PostgreSQL storage type on {property.DeclaringType.Name}.{property.Name}.");
    }

    private static void RemoveSqlServerAnnotations(IMutableAnnotatable metadata)
    {
        foreach (var annotation in metadata.GetAnnotations().Where(annotation => annotation.Name.StartsWith("SqlServer:", StringComparison.Ordinal)).ToArray())
            metadata.RemoveAnnotation(annotation.Name);
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
    internal static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    private static DateTime RequireUtc(DateTime value)
    {
        // Retain existing Min/Max sentinel handling while normal instants require an explicit UTC source.
        if (value.Ticks == DateTime.MinValue.Ticks || value.Ticks == DateTime.MaxValue.Ticks)
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        if (value.Kind != DateTimeKind.Utc) throw new InvalidOperationException("PostgreSQL UTC DateTime requires DateTimeKind.Utc.");
        return value;
    }
    private static DateTimeOffset RequireZeroOffset(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        ? value : throw new InvalidOperationException("PostgreSQL DateTimeOffset requires Offset=0.");

    [GeneratedRegex(@"\A(nvarchar|varchar|nchar|char|binary|varbinary|decimal|numeric)\((max|\d+(?:,\d+)?)\)\z", RegexOptions.CultureInvariant)]
    private static partial Regex SizedType();
}
