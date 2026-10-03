using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal.Mapping;
using NpgsqlTypes;

namespace CP6.Core.Persistence;

// This adapter targets the repository's exact EF 8.0.30 / Npgsql EF 8.0.11 pins.
// Provider upgrades must repeat the real text/parameter regression gate.
#pragma warning disable EF1001
public sealed class PostgreSqlTextTypeMappingSource : NpgsqlTypeMappingSource
{
    public PostgreSqlTextTypeMappingSource(TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies,
        ISqlGenerationHelper sqlGenerationHelper, INpgsqlSingletonOptions options)
        : base(dependencies, relationalDependencies, sqlGenerationHelper, options) { }

    public override CoreTypeMapping? FindMapping(IProperty property)
    {
        var mapping = base.FindMapping(property);
        if (mapping is not PostgreSqlBusinessStringTypeMapping business) return mapping;
        var collation = property.GetCollation() ?? property.DeclaringType.Model.GetCollation();
        return business.WithCollation(collation).Clone(keyComparer: new PostgreSqlBusinessTextComparer(collation == "C"));
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo info)
    {
        if ((info.ClrType is null || info.ClrType == typeof(string)) &&
            (info.StoreTypeNameBase == "bpchar" || info.StoreTypeNameBase == "character"))
            return new PostgreSqlBusinessStringTypeMapping(info.StoreTypeName ?? "bpchar").Clone(info);
        return base.FindMapping(info);
    }
}

internal sealed class PostgreSqlBusinessStringTypeMapping : NpgsqlStringTypeMapping
{
    public string? BusinessCollation { get; }

    public PostgreSqlBusinessStringTypeMapping(string storeType, string? collation = null)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(string)),
            storeType, StoreTypePostfix.None, System.Data.DbType.String), NpgsqlDbType.Char) => BusinessCollation = collation;

    private PostgreSqlBusinessStringTypeMapping(RelationalTypeMappingParameters parameters, string? collation)
        : base(parameters, NpgsqlDbType.Char) => BusinessCollation = collation;

    public PostgreSqlBusinessStringTypeMapping WithCollation(string? collation) => new(Parameters, collation);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new PostgreSqlBusinessStringTypeMapping(parameters, BusinessCollation);

    protected override void ConfigureParameter(DbParameter parameter)
    {
        base.ConfigureParameter(parameter);
        // MaxLength belongs to database capacity checks. Npgsql's parameter Size
        // must not truncate UTF-16 values, and the built-in character mapper trims.
        parameter.Size = 0;
    }
}
#pragma warning restore EF1001
