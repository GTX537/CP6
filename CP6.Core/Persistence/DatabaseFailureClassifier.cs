using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Persistence;

public enum DatabaseFailureKind
{
    Unknown,
    UniqueConstraint,
    ForeignKey,
    CheckConstraint,
    Deadlock,
    SerializationFailure,
    OptimisticConcurrency,
    UnknownIntegrity
}

public sealed record DatabaseFailure(
    DatabaseFailureKind Kind,
    string? ConstraintName,
    string? SqlState,
    int? DatabaseErrorCode)
{
    public bool CanRetryTransaction =>
        Kind is DatabaseFailureKind.Deadlock or DatabaseFailureKind.SerializationFailure;

    public bool MatchesConstraint(string? constraintName) =>
        !string.IsNullOrEmpty(ConstraintName)
        && !string.IsNullOrEmpty(constraintName)
        && string.Equals(ConstraintName, constraintName, StringComparison.Ordinal);
}

public static class DatabaseFailureClassifier
{
    private static readonly Regex SqlUniqueKeyName = Pattern(
        "\\AViolation of (?:PRIMARY KEY|UNIQUE KEY) constraint '(?<name>[^'\\r\\n]{1,128})'\\. Cannot insert duplicate key in object '");

    private static readonly Regex SqlUniqueIndexName = Pattern(
        "\\ACannot insert duplicate key row in object '[^'\\r\\n]+?' with unique index '(?<name>[^'\\r\\n]{1,128})'\\.(?: The duplicate key value is|\\z)");

    private static readonly Regex SqlIntegrityName = Pattern(
        "\\AThe (?:INSERT|UPDATE|DELETE|MERGE|ALTER TABLE) statement conflicted with the (?<kind>FOREIGN KEY|REFERENCE|CHECK) constraint \"(?<name>[^\"\\r\\n]{1,128})\"\\. The conflict occurred in database \"");

    // SQL Server exposes constraint identity only in localized error text. Recognize
    // these exact native templates; unsupported languages retain an unknown name.
    private static readonly Regex SqlUniqueKeyNameZh = Pattern(
        """\A违反了 (?:PRIMARY KEY|UNIQUE KEY) 约束“(?<name>[^“”\r\n]{1,128})”。不能在对象“""");

    private static readonly Regex SqlUniqueIndexNameZh = Pattern(
        """\A不能在具有唯一索引“(?<name>[^“”\r\n]{1,128})”的对象“[^“”\r\n]+”中插入重复键的行。重复键值为 """);

    private static readonly Regex SqlIntegrityNameZh = Pattern(
        """\A(?:INSERT|UPDATE|DELETE|MERGE|ALTER TABLE) 语句与 (?<kind>FOREIGN KEY|REFERENCE|CHECK) 约束"(?<name>[^"\r\n]{1,128})"冲突。该冲突发生于数据库""");

    public static DatabaseFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        DatabaseFailure? providerFailure = null;
        var optimisticConcurrency = false;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            optimisticConcurrency |= current is DbUpdateConcurrencyException;
            var candidate = current switch
            {
                DatabaseResourceLockDeadlockException => new DatabaseFailure(DatabaseFailureKind.Deadlock, null, null, null),
                PostgresException postgres => PostgreSql(postgres),
                SqlException sqlServer => SqlServer(sqlServer),
                _ => null
            };
            if (candidate is not null
                && (providerFailure is null
                    || providerFailure.Kind == DatabaseFailureKind.Unknown
                    && candidate.Kind != DatabaseFailureKind.Unknown))
            {
                providerFailure = candidate;
            }
        }

        var failure = providerFailure ?? new DatabaseFailure(DatabaseFailureKind.Unknown, null, null, null);
        return optimisticConcurrency
            ? failure with { Kind = DatabaseFailureKind.OptimisticConcurrency }
            : failure;
    }

    private static DatabaseFailure PostgreSql(PostgresException exception) => new(
        exception.SqlState switch
        {
            "23505" => DatabaseFailureKind.UniqueConstraint,
            "23503" or "23001" => DatabaseFailureKind.ForeignKey,
            "23514" => DatabaseFailureKind.CheckConstraint,
            "40P01" => DatabaseFailureKind.Deadlock,
            "40001" => DatabaseFailureKind.SerializationFailure,
            _ => DatabaseFailureKind.Unknown
        },
        exception.ConstraintName,
        exception.SqlState,
        null);

    private static DatabaseFailure SqlServer(SqlException exception)
    {
        DatabaseFailure? firstError = null;
        foreach (SqlError error in exception.Errors)
        {
            var failure = SqlServer(error.Number, error.Message);
            firstError ??= failure;
            if (failure.Kind != DatabaseFailureKind.Unknown)
                return failure;
        }

        return firstError ?? SqlServer(exception.Number, exception.Message);
    }

    private static DatabaseFailure SqlServer(int number, string message)
    {
        switch (number)
        {
            case 2601:
                return new(DatabaseFailureKind.UniqueConstraint,
                    Name(SqlUniqueIndexName, message) ?? Name(SqlUniqueIndexNameZh, message), null, number);
            case 2627:
                return new(DatabaseFailureKind.UniqueConstraint,
                    Name(SqlUniqueKeyName, message) ?? Name(SqlUniqueKeyNameZh, message), null, number);
            case 1205:
                return new(DatabaseFailureKind.Deadlock, null, null, number);
            case 547:
                var match = Match(SqlIntegrityName, message) ?? Match(SqlIntegrityNameZh, message);
                if (match is not null)
                {
                    var kind = match.Groups["kind"].Value == "CHECK"
                        ? DatabaseFailureKind.CheckConstraint
                        : DatabaseFailureKind.ForeignKey;
                    return new(kind, match.Groups["name"].Value, null, number);
                }

                return new(DatabaseFailureKind.UnknownIntegrity, null, null, number);
            default:
                return new(DatabaseFailureKind.Unknown, null, null, number);
        }
    }

    private static string? Name(Regex pattern, string message) => Match(pattern, message)?.Groups["name"].Value;

    private static Match? Match(Regex pattern, string message)
    {
        try
        {
            var match = pattern.Match(message);
            return match.Success ? match : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static Regex Pattern(string value) =>
        new(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
