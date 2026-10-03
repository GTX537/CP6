using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;

namespace CP6.Core.Persistence;

#pragma warning disable EF1001
public sealed class PostgreSqlTextQuerySqlGeneratorFactory(
    QuerySqlGeneratorDependencies dependencies,
    IRelationalTypeMappingSource typeMappingSource,
    INpgsqlSingletonOptions options) : IQuerySqlGeneratorFactory
{
    public QuerySqlGenerator Create() => new PostgreSqlTextQuerySqlGenerator(dependencies,
        typeMappingSource, options.ReverseNullOrderingEnabled, options.PostgresVersion);
}

internal sealed class PostgreSqlTextQuerySqlGenerator(
    QuerySqlGeneratorDependencies dependencies, IRelationalTypeMappingSource mappingSource,
    bool reverseNullOrdering, Version postgresVersion)
    : NpgsqlQuerySqlGenerator(dependencies, mappingSource, reverseNullOrdering, postgresVersion)
{
    private readonly ISqlGenerationHelper _sqlGenerationHelper = dependencies.SqlGenerationHelper;
    // bpchar comparisons implement SQL PAD SPACE, but its implicit conversion to
    // text drops spaces. String operations must use the original stored bytes.
    private static bool IsBusinessText(SqlExpression expression) =>
        expression.Type == typeof(string) && expression.TypeMapping?.StoreTypeNameBase is "bpchar" or "character";

    private void VisitRawText(SqlExpression expression)
    {
        var collation = (expression.TypeMapping as PostgreSqlBusinessStringTypeMapping)?.BusinessCollation;
        if (collation is not null) Sql.Append("(");
        Sql.Append("convert_from(pg_catalog.bpcharsend(");
        Visit(expression);
        Sql.Append("), 'UTF8')");
        if (collation is not null) Sql.Append(" COLLATE ").Append(_sqlGenerationHelper.DelimitIdentifier(collation)).Append(")");
    }

    protected override Expression VisitSqlUnary(SqlUnaryExpression expression)
    {
        if (expression.OperatorType == ExpressionType.Convert && IsBusinessText(expression.Operand) &&
            expression.TypeMapping?.StoreType == "text")
        {
            VisitRawText(expression.Operand);
            return expression;
        }
        return base.VisitSqlUnary(expression);
    }

    protected override Expression VisitSqlBinary(SqlBinaryExpression expression)
    {
        if (expression.OperatorType == ExpressionType.Add && expression.Type == typeof(string) &&
            (IsBusinessText(expression.Left) || IsBusinessText(expression.Right)))
        {
            Sql.Append("(");
            if (IsBusinessText(expression.Left)) VisitRawText(expression.Left); else Visit(expression.Left);
            Sql.Append(" || ");
            if (IsBusinessText(expression.Right)) VisitRawText(expression.Right); else Visit(expression.Right);
            Sql.Append(")");
            if (IsBusinessText(expression)) Sql.Append("::bpchar");
            return expression;
        }
        return base.VisitSqlBinary(expression);
    }

    protected override Expression VisitSqlFunction(SqlFunctionExpression expression)
    {
        var arguments = expression.Arguments;
        if (!expression.IsBuiltIn || arguments is null || !arguments.Any(IsBusinessText))
            return base.VisitSqlFunction(expression);

        // Functions in the pinned provider's string translators take text;
        // special byte/array/JSON functions must retain their own type rules.
        if (expression.Name is not ("length" or "char_length" or "strpos" or "substring" or "substr" or
            "left" or "right" or "upper" or "lower" or "replace" or "btrim" or "ltrim" or "rtrim"))
            return base.VisitSqlFunction(expression);

        var length = expression.Name is "length" or "char_length";
        var resultBusinessText = IsBusinessText(expression);
        if (resultBusinessText) Sql.Append("(");
        if (length) Sql.Append("char_length(regexp_replace(rtrim(");
        else Sql.Append(expression.Name).Append("(");
        for (var i = 0; i < arguments.Count; i++)
        {
            if (i > 0) Sql.Append(", ");
            if (IsBusinessText(arguments[i])) VisitRawText(arguments[i]); else Visit(arguments[i]);
        }
        if (length) Sql.Append(", ' ') COLLATE \"C\", U&'[\\+010000-\\+10FFFF]', 'aa', 'g'))");
        else Sql.Append(")");
        if (resultBusinessText) Sql.Append(")::bpchar");
        return expression;
    }
}
#pragma warning restore EF1001
