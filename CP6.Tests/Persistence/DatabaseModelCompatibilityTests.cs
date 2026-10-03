using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Entity.DomainModels.Sys;
using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.WebApi.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Persistence;

/// <summary>
/// Exercises the real provider models and migration SQL without opening a connection.
/// Installation, catalog semantics and business writes remain real-database gates.
/// </summary>
public sealed class DatabaseModelCompatibilityTests
{
    private static readonly Guid Tenant = Guid.Parse("22f65306-7c8b-4947-a6a5-cf2acafbd68d");
    private static readonly string[] ContextKinds = ["Core", "Space", "IdentityPriority", "ErpIntegration"];

    public static IEnumerable<object[]> Contexts => ContextKinds.Select(kind => new object[] { kind });
    public static IEnumerable<object[]> ContextProviders => ContextKinds.SelectMany(kind =>
        Enum.GetValues<DatabaseProvider>().Select(provider => new object[] { kind, provider }));

    [Theory]
    [MemberData(nameof(ContextProviders))]
    public void Design_factory_and_runtime_build_the_same_relational_contract(string kind, DatabaseProvider provider)
    {
        using var runtime = RuntimeContext(kind, provider);
        using var design = DesignContext(kind, provider);
        var runtimeDesignModel = DesignModel(runtime);
        var factoryModel = DesignModel(design);

        Assert.Equal(runtime.Database.ProviderName, design.Database.ProviderName);
        Assert.Equal(ModelContract(runtimeDesignModel), ModelContract(factoryModel));
        Assert.Equal(factoryModel.GetEntityTypes().Select(e => e.Name).Order(), runtime.Model.GetEntityTypes().Select(e => e.Name).Order());
        foreach (var entity in runtimeDesignModel.GetEntityTypes())
        {
            var optimized = AssertEntity(runtime.Model, entity.Name);
            foreach (var property in entity.GetProperties())
            {
                var actual = AssertProperty(optimized, property.Name);
                Assert.Equal(property.ClrType, actual.ClrType);
                Assert.Equal(property.IsConcurrencyToken, actual.IsConcurrencyToken);
                Assert.Equal(property.ValueGenerated, actual.ValueGenerated);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Sql_server_preserves_every_existing_snapshot_entity_contract(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.SqlServer);
        var actual = DesignModel(context);
        using var resource = typeof(DatabaseModelCompatibilityTests).Assembly.GetManifestResourceStream("CP6.Tests.Persistence.SqlServerD6074aaaContracts")!;
        using var frozen = JsonDocument.Parse(resource);
        Assert.Equal("d6074aaad3098b61adaeda902b92bf24b3eb0c04", frozen.RootElement.GetProperty("BaseSha").GetString());
        var original = frozen.RootElement.GetProperty("Contexts").GetProperty(kind).EnumerateObject().ToArray();
        Assert.Equal(kind switch { "Core" => 232, "Space" => 83, "IdentityPriority" => 4, _ => 8 }, original.Length);
        foreach (var entity in original)
            Assert.Equal(entity.Value.GetString(), EntityContract(AssertEntity(actual, entity.Name)));
        // A new forward generation table is intentional; it cannot justify changing existing tables.
        Assert.All(actual.GetEntityTypes().Where(e => !original.Any(old => old.Name == e.Name)), entity =>
            Assert.Equal("CrmIdentityTenantGeneration", entity.ClrType.Name));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Postgre_sql_generates_provider_sql_without_sql_server_types_or_expressions(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var model = DesignModel(context);
        var sql = GenerateSql(context, model);

        Assert.NotEmpty(sql);
        Assert.DoesNotMatch("(?i)\"\\s+(?:nvarchar|nchar|datetime2|datetimeoffset|uniqueidentifier|rowversion|varbinary|bit)\\b|\"\\s+varchar\\s*\\(max\\)", sql);
        Assert.DoesNotMatch(@"(?i)\b(?:LEN|ISJSON|YEAR|MONTH|ISNULL|GETDATE|SYSUTCDATETIME)\s*\(", sql);
        Assert.DoesNotMatch(@"\[[A-Za-z_][A-Za-z_0-9]*\]", sql);
        Assert.DoesNotMatch(@"(?i)\bLIKE\s+'[^']*\[\^", sql);
        foreach (var entity in model.GetEntityTypes())
        {
            Assert.All(entity.GetProperties(), p => Assert.DoesNotMatch(
                @"(?i)^(?:nvarchar|nchar|datetime2|datetimeoffset|uniqueidentifier|rowversion|varbinary|bit)\b|^varchar\s*\(max\)", p.GetColumnType() ?? ""));
            var predicates = entity.GetCheckConstraints().Select(c => c.Sql)
                .Concat(entity.GetIndexes().Select(i => i.GetFilter()).OfType<string>());
            foreach (var property in entity.GetProperties().Where(p => Underlying(p.ClrType) == typeof(bool)))
            {
                var column = Regex.Escape(ColumnName(property));
                foreach (var predicate in predicates)
                    Assert.DoesNotMatch($"\"{column}\"\\s*(?:=|<>|!=)\\s*[01]\\b|\\b[01]\\s*=\\s*\"{column}\"", predicate);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Postgre_sql_retains_all_existing_keys_foreign_keys_indexes_facets_and_tenant_filters(string kind)
    {
        using var sqlContext = RuntimeContext(kind, DatabaseProvider.SqlServer);
        using var pgContext = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var sqlModel = DesignModel(sqlContext);
        var pgModel = DesignModel(pgContext);

        foreach (var source in sqlModel.GetEntityTypes())
        {
            var target = AssertEntity(pgModel, source.Name);
            Assert.Equal(source.GetTableName(), target.GetTableName());
            Assert.Equal(source.GetSchema(), target.GetSchema());
            Assert.Equal(source.GetKeys().Select(KeyContract).Order(), target.GetKeys().Select(KeyContract).Order());
            Assert.Equal(source.GetForeignKeys().Select(ForeignKeyContract).Order(), target.GetForeignKeys().Select(ForeignKeyContract).Order());
            Assert.Equal(source.GetQueryFilter()?.ToString(), target.GetQueryFilter()?.ToString());
            foreach (var property in source.GetProperties())
            {
                var actual = AssertProperty(target, property.Name);
                var generatedToken = property.ClrType == typeof(byte[]) && property.IsConcurrencyToken &&
                    property.ValueGenerated == ValueGenerated.OnAddOrUpdate;
                // PostgreSQL's generated-token contract intentionally tightens
                // SQL's nullable rowversion metadata to an always present bytea(8).
                // Every ordinary property retains its original nullability.
                Assert.Equal(PropertyFacets(property, generatedToken ? false : null), PropertyFacets(actual));
                var numeric = Regex.Match(property.GetColumnType() ?? "", @"(?i)^decimal\((\d+),\s*(\d+)\)$");
                if (numeric.Success)
                    Assert.Matches($"(?i)^numeric\\({numeric.Groups[1].Value},\\s*{numeric.Groups[2].Value}\\)$", actual.GetColumnType()!);
            }
            foreach (var index in source.GetIndexes())
            {
                var matches = target.GetIndexes().Where(i => PropertyNames(i.Properties) == PropertyNames(index.Properties) && i.IsUnique == index.IsUnique).ToArray();
                Assert.NotEmpty(matches);
                if (!string.IsNullOrWhiteSpace(index.GetFilter()))
                    Assert.Contains(matches, i => !string.IsNullOrWhiteSpace(i.GetFilter()));
            }
            // Added byte-length and Unicode-capacity checks must not replace business checks.
            Assert.True(target.GetCheckConstraints().Count() >= source.GetCheckConstraints().Count(), source.Name);
            foreach (var check in source.GetCheckConstraints())
            {
                var originalName = check.Name ?? check.ModelName;
                var referencedColumns = source.GetProperties().Where(p => check.Sql.Contains($"[{ColumnName(p)}]", StringComparison.Ordinal)).ToArray();
                Assert.Contains(target.GetCheckConstraints(), candidate => referencedColumns.All(p =>
                    candidate.Sql.Contains($"\"{ColumnName(p)}\"", StringComparison.Ordinal)) &&
                    (candidate.Name == originalName || (candidate.Name ?? candidate.ModelName).StartsWith(originalName[..Math.Min(originalName.Length, 30)], StringComparison.Ordinal) ||
                     candidate.GetAnnotations().Any(a => a.Value as string == originalName)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_postgre_sql_rowversion_is_generated_eight_byte_binary_and_uses_database_readback(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var model = DesignModel(context);
        var versions = model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.Name == "RowVersion").ToArray();
        Assert.NotEmpty(versions);
        foreach (var property in versions)
        {
            Assert.Equal(typeof(byte[]), property.ClrType);
            Assert.Equal("bytea", property.GetColumnType());
            Assert.Equal(8, property.GetMaxLength());
            Assert.False(property.IsNullable);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
            Assert.Equal(PropertySaveBehavior.Ignore, property.GetBeforeSaveBehavior());
            Assert.Equal(PropertySaveBehavior.Ignore, property.GetAfterSaveBehavior());
            Assert.DoesNotContain(property.GetAnnotations(), a => a.Name.Contains("xmin", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(((IEntityType)property.DeclaringType).GetCheckConstraints(), c =>
                Regex.IsMatch(c.Sql, $"(?i)(?:octet_length|length)\\(\\s*\"{Regex.Escape(ColumnName(property))}\"\\s*\\)\\s*=\\s*8"));
        }
    }

    [Theory]
    [InlineData("Core", 4)]
    [InlineData("IdentityPriority", 4)]
    [InlineData("ErpIntegration", 2)]
    public void Actual_platform_private_setters_keep_generated_concurrency_metadata(string kind, int expectedEntities)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var entities = DesignModel(context).GetEntityTypes().Where(e => e.ClrType.Assembly.GetName().Name == "CP6.Platform.EntityFramework").ToArray();
        Assert.Equal(expectedEntities, entities.Length);
        foreach (var entity in entities)
        {
            var property = AssertProperty(entity, "RowVersion");
            Assert.False(property.PropertyInfo!.SetMethod!.IsPublic);
            Assert.True(property.IsConcurrencyToken);
            Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
            Assert.Equal("bytea", property.GetRelationalTypeMapping().StoreType);
        }
    }

    [Theory]
    [InlineData("ErpInboxReplayAudit")]
    [InlineData("ErpDeliveryReplayAudit")]
    public void Replay_input_rowversion_is_saved_as_ordinary_eight_byte_binary(string entityName)
    {
        using var context = RuntimeContext("ErpIntegration", DatabaseProvider.PostgreSql);
        var entity = DesignModel(context).GetEntityTypes().Single(e => e.ClrType.Name == entityName);
        var property = AssertProperty(entity, "InputRowVersion");

        Assert.Equal(typeof(byte[]), property.ClrType);
        Assert.Equal("bytea", property.GetColumnType());
        Assert.Equal(8, property.GetMaxLength());
        Assert.False(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
        Assert.Equal(PropertySaveBehavior.Save, property.GetBeforeSaveBehavior());
        Assert.Equal(PropertySaveBehavior.Save, property.GetAfterSaveBehavior());
        var bytes = new byte[] { 0, 1, 2, 3, 128, 253, 254, 255 };
        var literal = property.GetRelationalTypeMapping().GenerateSqlLiteral(bytes);
        Assert.Contains("BYTEA", literal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Convert.ToHexString(bytes), literal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(entity.GetCheckConstraints(), c => c.Sql.Contains("InputRowVersion", StringComparison.Ordinal) && c.Sql.Contains("8", StringComparison.Ordinal));
    }

    [Fact]
    public void Postgre_sql_global_language_key_is_unique_even_when_tenant_is_null()
    {
        using var context = RuntimeContext("Core", DatabaseProvider.PostgreSql);
        var entity = DesignModel(context).FindEntityType(typeof(Sys_Lang))!;
        var composite = entity.GetIndexes().Single(i => i.IsUnique && PropertyNames(i.Properties) == "TenantId,LangKey");
        Assert.True(AssertProperty(entity, "TenantId").IsNullable);
        var nullsNotDistinct = composite.FindAnnotation("Npgsql:NullsDistinct")?.Value is false && composite.GetFilter() is null;
        var separateGlobal = entity.GetIndexes().Any(i => i.IsUnique && PropertyNames(i.Properties) == "LangKey" &&
            Regex.IsMatch(i.GetFilter() ?? "", "(?i)\"TenantId\"\\s+IS\\s+NULL"));
        Assert.True(nullsNotDistinct || separateGlobal, "Default PostgreSQL NULL-distinct composite uniqueness permits duplicate global translations.");
        var sql = GenerateSql(context, DesignModel(context));
        Assert.True(sql.Contains("NULLS NOT DISTINCT", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(sql, "(?i)CREATE UNIQUE INDEX[^;]*\"LangKey\"[^;]*WHERE[^;]*\"TenantId\"\\s+IS\\s+NULL"));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Postgre_sql_identifiers_fit_utf8_limit_and_do_not_collide_in_their_catalog_namespace(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var seen = new HashSet<(string Schema, string Namespace, string Name)>();
        foreach (var table in DesignModel(context).GetRelationalModel().Tables)
        {
            Register(table.Schema, "relation", table.Name);
            foreach (var index in table.Indexes) Register(table.Schema, "relation", index.Name);
            foreach (var key in table.UniqueConstraints) Register(table.Schema, "relation", key.Name);
            foreach (var key in table.ForeignKeyConstraints) Register(table.Schema, $"constraint:{table.Name}", key.Name);
            foreach (var check in table.CheckConstraints) Register(table.Schema, $"constraint:{table.Name}", check.Name ?? check.ModelName);
        }

        void Register(string? schema, string scope, string name)
        {
            Assert.InRange(Encoding.UTF8.GetByteCount(name), 1, 63);
            Assert.True(seen.Add((schema ?? "public", scope, name)), $"Identifier collision: {schema}.{name} ({scope}).");
        }
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Postgre_sql_datetimeoffset_columns_require_utc_and_preserve_the_instant(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var properties = DesignModel(context).GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => Underlying(p.ClrType) == typeof(DateTimeOffset)).ToArray();
        Assert.NotEmpty(properties);
        var utc = new DateTimeOffset(2026, 10, 2, 12, 34, 56, TimeSpan.Zero);
        foreach (var property in properties)
        {
            Assert.Equal("timestamp with time zone", property.GetRelationalTypeMapping().StoreType);
            var mapping = property.GetRelationalTypeMapping();
            Assert.Contains("2026-10-02", mapping.GenerateSqlLiteral(utc));
            Assert.ThrowsAny<Exception>(() => mapping.GenerateSqlLiteral(utc.ToOffset(TimeSpan.FromHours(2))));
        }
    }

    [Fact]
    public void Space_datetime_contract_is_utc_on_write_and_read_for_all_audited_properties()
    {
        using var context = RuntimeContext("Space", DatabaseProvider.PostgreSql);
        var properties = DesignModel(context).GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => Underlying(p.ClrType) == typeof(DateTime)).ToArray();
        Assert.Equal(237, properties.Length);
        AssertUtcProperties(properties);
        var day = DesignModel(context).FindEntityType(typeof(SpaceAiBudgetReservation))!.FindProperty("PeriodDay")!;
        Assert.Equal(typeof(DateOnly), day.ClrType);
        Assert.Equal("date", day.GetRelationalTypeMapping().StoreType);
        Assert.Equal("DATE '2026-10-02'", day.GetRelationalTypeMapping().GenerateSqlLiteral(new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public void Core_preserves_local_and_calendar_fields_and_only_maps_verified_instants_to_utc()
    {
        using var context = RuntimeContext("Core", DatabaseProvider.PostgreSql);
        var model = DesignModel(context);
        var utc = new[] { FindProperty(model, "Space_AuditEvent", "OccurredAtUtc"), FindProperty(model, "IntegrationEvent", "OccurredAtUtc") };
        AssertUtcProperties(utc);
        foreach (var (entity, name) in new[] { ("Sys_User", "CreateDate"), ("InboundReceipt", "ReceiveDateTime"), ("StockTransaction", "TxnDateTime"), ("EstimateCalc", "QtnDate"), ("CreditNote", "IssueDate") })
        {
            var property = FindProperty(model, entity, name);
            Assert.Equal("timestamp without time zone", property.GetRelationalTypeMapping().StoreType);
            var wallClock = new DateTime(2026, 10, 2, 0, 15, 30, DateTimeKind.Unspecified);
            var literal = property.GetRelationalTypeMapping().GenerateSqlLiteral(wallClock);
            Assert.Contains("2026-10-02", literal);
            Assert.Contains("00:15:30", literal);
            var converter = property.GetTypeMapping().Converter;
            if (converter is not null)
            {
                var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Local);
                Assert.Equal(wallClock.Ticks, ((DateTime)converter.ConvertToProvider(local)!).Ticks);
            }
        }
        Assert.All(model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => Underlying(p.ClrType) == typeof(DateOnly)),
            p => Assert.Equal("date", p.GetRelationalTypeMapping().StoreType));
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("ErpIntegration")]
    public void Postgre_sql_binary_sensitive_identity_and_idempotency_fields_retain_explicit_collation(string kind)
    {
        using var sqlContext = RuntimeContext(kind, DatabaseProvider.SqlServer);
        using var pgContext = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var pg = DesignModel(pgContext);
        var sensitive = DesignModel(sqlContext).GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.GetCollation()?.EndsWith("_BIN2", StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(sensitive);
        foreach (var source in sensitive)
        {
            var actual = AssertProperty(AssertEntity(pg, source.DeclaringType.Name), source.Name);
            Assert.Equal("C", actual.GetCollation());
            Assert.Equal(source.GetMaxLength(), actual.GetMaxLength());
        }
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Tenant_queries_keep_actual_tenant_predicates_and_global_refresh_token_exception(DatabaseProvider provider)
    {
        using var core = (CP6Context)RuntimeContext("Core", provider);
        using var space = (SpaceContext)RuntimeContext("Space", provider);
        Assert.Contains("TenantId", core.Sys_Users.ToQueryString());
        Assert.Contains(Tenant.ToString(), core.Sys_Users.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TenantId", space.Models.ToQueryString());
        Assert.Contains(Tenant.ToString(), space.Models.ToQueryString(), StringComparison.OrdinalIgnoreCase);
        var refresh = DesignModel(core).FindEntityType(typeof(Sys_RefreshToken))!;
        Assert.Contains(refresh.GetIndexes(), i => i.IsUnique && PropertyNames(i.Properties) == "TokenHash");
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public void Durable_generation_is_a_tenant_keyed_nonnegative_counter_separate_from_rowversion(DatabaseProvider provider)
    {
        using var context = RuntimeContext("Core", provider);
        var entity = Assert.Single(DesignModel(context).GetEntityTypes(), e => e.ClrType.Name == "CrmIdentityTenantGeneration");
        Assert.Equal("CrmIdentityTenantGenerations", entity.GetTableName());
        Assert.Equal("TenantId", PropertyNames(entity.FindPrimaryKey()!.Properties));
        Assert.Equal(typeof(Guid), AssertProperty(entity, "TenantId").ClrType);
        Assert.Equal(typeof(long), AssertProperty(entity, "Generation").ClrType);
        Assert.Null(entity.FindProperty("RowVersion"));
        Assert.Contains(entity.GetCheckConstraints(), c => Regex.IsMatch(c.Sql, @"Generation[^>]*>=\s*\(?0\)?", RegexOptions.IgnoreCase));
    }

    private static void AssertUtcProperties(IEnumerable<IProperty> properties)
    {
        var instant = new DateTime(2026, 10, 2, 12, 34, 56, DateTimeKind.Utc);
        foreach (var property in properties)
        {
            var mapping = property.GetRelationalTypeMapping();
            Assert.Equal("timestamp with time zone", mapping.StoreType);
            Assert.Contains("2026-10-02", mapping.GenerateSqlLiteral(instant));
            Assert.ThrowsAny<Exception>(() => mapping.GenerateSqlLiteral(DateTime.SpecifyKind(instant, DateTimeKind.Local)));
            Assert.ThrowsAny<Exception>(() => mapping.GenerateSqlLiteral(DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)));
            var converter = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter>(property.GetTypeMapping().Converter);
            var read = Assert.IsType<DateTime>(converter.ConvertFromProvider(DateTime.SpecifyKind(instant, DateTimeKind.Unspecified)));
            Assert.Equal(DateTimeKind.Utc, read.Kind);
            Assert.Equal(instant.Ticks, read.Ticks);
        }
    }

    private static IModel DesignModel(DbContext context) => context.GetService<IDesignTimeModel>().Model;
    [Theory]
    [MemberData(nameof(Contexts))]
    public void Postgre_sql_fixed_text_is_limited_to_the_exact_audited_ascii_domains(string kind)
    {
        using var context = RuntimeContext(kind, DatabaseProvider.PostgreSql);
        var model = DesignModel(context);
        var expected = kind switch
        {
            "Core" => PostgreSqlFixedTextDomainsV1.Core,
            "Space" => PostgreSqlFixedTextDomainsV1.Space,
            "IdentityPriority" => PostgreSqlFixedTextDomainsV1.IdentityPriority,
            _ => PostgreSqlFixedTextDomainsV1.ErpIntegration
        };
        var actual = model.GetEntityTypes().SelectMany(entity => entity.GetProperties())
            .Where(property => property.FindAnnotation("CP6:FixedAsciiDomain")?.Value is true).ToArray();
        Assert.Equal(expected.Count, actual.Length);
        foreach (var property in actual)
        {
            var entity = (IEntityType)property.DeclaringType;
            Assert.Equal($"character({expected[(entity.ClrType.FullName!, property.Name)]})", property.GetColumnType());
            Assert.Contains(entity.GetCheckConstraints(), check => check.FindAnnotation("CP6:FixedAsciiDomain")?.Value is true &&
                check.Sql.Contains($"pg_catalog.bpcharsend(\"{ColumnName(property)}\")", StringComparison.Ordinal));
        }
        Assert.All(model.GetEntityTypes().SelectMany(entity => entity.GetCheckConstraints()), check =>
            Assert.DoesNotContain("~", check.FindAnnotation(PostgreSqlModelConfiguration.OriginalNameAnnotation)?.Value as string ?? check.Name));
    }

    [Theory]
    [InlineData("char(8)")]
    [InlineData("nchar(8)")]
    public void A_new_general_fixed_unicode_column_cannot_silently_use_pg_codepoint_padding(string storage)
    {
        var builder = new ModelBuilder();
        builder.Entity<NewFixedTextFixture>().ToTable("NewFixedTextFixture");
        builder.Entity<NewFixedTextFixture>().Property(row => row.Value).HasColumnType(storage).HasMaxLength(8).IsFixedLength();
        Assert.Throws<InvalidOperationException>(() => PostgreSqlModelConfiguration.Apply(builder));
    }

    private sealed class NewFixedTextFixture { public Guid Id { get; set; } public string Value { get; set; } = ""; }

    [Fact]
    public void Frozen_sql_baseline_detects_an_existing_column_facet_change()
    {
        using var context = new ChangedSqlFacetContext();
        using var resource = typeof(DatabaseModelCompatibilityTests).Assembly.GetManifestResourceStream("CP6.Tests.Persistence.SqlServerD6074aaaContracts")!;
        using var frozen = JsonDocument.Parse(resource);
        var entity = DesignModel(context).FindEntityType(typeof(Sys_User))!;
        Assert.Equal(99, entity.FindProperty(nameof(Sys_User.UserName))!.GetMaxLength());
        Assert.NotEqual(frozen.RootElement.GetProperty("Contexts").GetProperty("Core").GetProperty(entity.Name).GetString(), EntityContract(entity));
    }

    private sealed class ChangedSqlFacetContext : CP6Context
    {
        public ChangedSqlFacetContext() : base(new DbContextOptionsBuilder<CP6Context>().UseSqlServer(Connection(DatabaseProvider.SqlServer)).Options) { }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Sys_User>().Property(row => row.UserName).HasMaxLength(99);
        }
    }
    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
    private static IEntityType AssertEntity(IModel model, string name) => Assert.IsAssignableFrom<IEntityType>(model.FindEntityType(name));
    private static IProperty AssertProperty(IEntityType entity, string name) => Assert.IsAssignableFrom<IProperty>(entity.FindProperty(name));
    private static IProperty FindProperty(IModel model, string entity, string name) => AssertProperty(model.GetEntityTypes().Single(e => e.ClrType.Name == entity), name);
    private static string PropertyNames(IEnumerable<IReadOnlyProperty> properties) => string.Join(",", properties.Select(p => p.Name));
    private static string ColumnName(IProperty property) => property.GetColumnName(StoreObjectIdentifier.Table(((IEntityType)property.DeclaringType).GetTableName()!, ((IEntityType)property.DeclaringType).GetSchema()))!;
    private static string KeyContract(IKey key) => $"{key.IsPrimaryKey()}:{PropertyNames(key.Properties)}";
    private static string ForeignKeyContract(IForeignKey key) => $"{PropertyNames(key.Properties)}->{key.PrincipalEntityType.Name}:{PropertyNames(key.PrincipalKey.Properties)}:{key.IsUnique}:{key.IsRequired}:{key.DeleteBehavior}";
    private static string PropertyFacets(IProperty p, bool? nullableOverride = null) => string.Join("|", p.Name, ProviderClrType(p).FullName, nullableOverride ?? p.IsNullable,
        p.Name == "RowVersion" ? 8 : p.GetMaxLength(), p.IsUnicode(), NumericFacets(p),
        DefaultLiteral(p), ColumnName(p));
    private static Type ProviderClrType(IProperty p) => p.GetTypeMapping().Converter?.ProviderClrType ??
        (Underlying(p.ClrType).IsEnum ? Enum.GetUnderlyingType(Underlying(p.ClrType)) : Underlying(p.ClrType));
    private static string? DefaultLiteral(IProperty p)
    {
        var value = p.GetDefaultValue();
        return value is Enum ? Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
    private static string NumericFacets(IProperty p)
    {
        var store = Regex.Match(p.GetColumnType() ?? "", @"(?i)^(?:decimal|numeric)\((\d+),\s*(\d+)\)$");
        return store.Success ? $"{store.Groups[1].Value},{store.Groups[2].Value}" : $"{p.GetPrecision()},{p.GetScale()}";
    }
    private static string EntityContract(IEntityType entity) => string.Join("\n", new[] { entity.Name, entity.GetSchema(), entity.GetTableName() }
        .Concat(entity.GetProperties().Select(p => $"P:{PropertyFacets(p)}:{p.GetColumnType()}:{p.IsConcurrencyToken}:{p.ValueGenerated}:{p.GetCollation()}").Order(StringComparer.Ordinal))
        .Concat(entity.GetKeys().Select(k => $"K:{KeyContract(k)}:{k.GetName()}").Order(StringComparer.Ordinal))
        .Concat(entity.GetForeignKeys().Select(k => $"FK:{ForeignKeyContract(k)}:{k.GetConstraintName()}").Order(StringComparer.Ordinal))
        .Concat(entity.GetIndexes().Select(i => $"IX:{PropertyNames(i.Properties)}:{i.IsUnique}:{i.GetFilter()}:{i.GetDatabaseName()}").Order(StringComparer.Ordinal))
        .Concat(entity.GetCheckConstraints().Select(c => $"CK:{c.Name}:{c.Sql}").Order(StringComparer.Ordinal)));
    private static string[] ModelContract(IModel model) => model.GetEntityTypes().Select(EntityContract).Order().ToArray();

    private static string GenerateSql(DbContext context, IModel model)
    {
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        return string.Join("\n", context.GetService<IMigrationsSqlGenerator>().Generate(operations, model).Select(c => c.CommandText));
    }

    private static string Connection(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? "Server=localhost;Database=model_contract;Integrated Security=True;TrustServerCertificate=True"
        : "Host=localhost;Database=model_contract;Username=model_contract";

    private static DbContext DesignContext(string kind, DatabaseProvider provider)
    {
        string[] args = [$"--Database:Provider={provider}", "--ConnectionStrings:DefaultConnection", Connection(provider)];
        return kind switch
        {
            "Core" => new CP6ContextDesignFactory().CreateDbContext(args),
            "Space" => new SpaceContextDesignFactory().CreateDbContext(args),
            "IdentityPriority" => new IdentityMessagingContextDesignFactory().CreateDbContext(args),
            "ErpIntegration" => new ErpIntegrationContextDesignFactory().CreateDbContext(args),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static DbContext RuntimeContext(string kind, DatabaseProvider provider) => kind switch
    {
        "Core" => new CP6Context(Options<CP6Context>(provider), new TestTenant()),
        "Space" => new SpaceContext(Options<SpaceContext>(provider), new TestExecution(), new SystemSpaceClock()),
        "IdentityPriority" => new IdentityMessagingContext(Options<IdentityMessagingContext>(provider)),
        "ErpIntegration" => new ErpIntegrationContext(Options<ErpIntegrationContext>(provider)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static DbContextOptions<T> Options<T>(DatabaseProvider provider) where T : DbContext =>
        DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), new(provider), Connection(provider)).Options;
    private sealed class TestTenant : CP6.Core.Services.Common.ITenantContext { public Guid CurrentTenantId { get; set; } = Tenant; }
    private sealed class TestExecution : ISpaceExecutionContext
    {
        public Guid TenantId => Tenant;
        public Guid ActorId => Guid.Parse("29b910c6-a6be-4c1f-8173-165a49b11898");
    }
}
