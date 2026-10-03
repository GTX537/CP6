using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CP6.Core.Persistence;
using Microsoft.Data.SqlClient;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.ErpIntegration.SqlTests;

/// <summary>Records native provider codes and constraint identity, never SQL text, connection details or values.</summary>
internal sealed class SqlFailureProbe : IDisposable
{
    private readonly ConcurrentQueue<Observation> observations = new();
    private readonly DatabaseProvider provider;
    private readonly ITestOutputHelper output;

    public SqlFailureProbe(DatabaseProvider provider, ITestOutputHelper output)
    {
        this.provider = provider;
        this.output = output;
        AppDomain.CurrentDomain.FirstChanceException += OnException;
    }

    public string Description => $"Provider={provider}; native=" + string.Join("; ", observations.Distinct()
        .Select(x => Format(x.Provider, x.Failure)).Order(StringComparer.Ordinal));

    public void AssertObservedTransactionConflict() => Assert.Contains(observations,
        x => x.Provider == provider && x.Failure.CanRetryTransaction);

    public void AssertOnlyTransactionConflicts()
    {
        foreach (var observation in observations)
        {
            Assert.Equal(provider, observation.Provider);
            if (provider == DatabaseProvider.SqlServer)
                Assert.Contains(observation.Failure.DatabaseErrorCode, new int?[] { 1205, 3903 });
            else
                Assert.Contains(observation.Failure.SqlState, new[] { "40P01", "40001" });
        }
    }

    public void AssertNoErrors() => Assert.Empty(observations);

    public static void AssertConstraint(Exception error, DatabaseProvider provider, DatabaseFailureKind kind,
        string constraint, ITestOutputHelper output)
    {
        var failure = DatabaseFailureClassifier.Classify(error);
        output.WriteLine(Format(provider, failure));
        Assert.Equal(kind, failure.Kind);
        Assert.Equal(constraint, failure.ConstraintName);
        if (provider == DatabaseProvider.PostgreSql)
        {
            Assert.Equal(kind == DatabaseFailureKind.UniqueConstraint ? "23505" : "23514", failure.SqlState);
            Assert.Null(failure.DatabaseErrorCode);
        }
        else
        {
            Assert.Null(failure.SqlState);
            Assert.Contains(failure.DatabaseErrorCode, kind == DatabaseFailureKind.UniqueConstraint
                ? new int?[] { 2601, 2627 } : new int?[] { 547 });
        }
    }

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is PostgresException postgres)
            observations.Enqueue(new(DatabaseProvider.PostgreSql, DatabaseFailureClassifier.Classify(postgres)));
        else if (args.Exception is SqlException sql)
        {
            var classified = DatabaseFailureClassifier.Classify(sql);
            foreach (SqlError error in sql.Errors)
                observations.Enqueue(new(DatabaseProvider.SqlServer, classified with
                {
                    Kind = error.Number == classified.DatabaseErrorCode ? classified.Kind : DatabaseFailureKind.Unknown,
                    ConstraintName = error.Number == classified.DatabaseErrorCode ? classified.ConstraintName : null,
                    DatabaseErrorCode = error.Number
                }));
        }
    }

    private static string Format(DatabaseProvider provider, DatabaseFailure failure) =>
        $"Provider={provider}; code={failure.SqlState ?? failure.DatabaseErrorCode?.ToString() ?? "none"}; kind={failure.Kind}; constraint={failure.ConstraintName ?? "none"}";

    public void Dispose()
    {
        AppDomain.CurrentDomain.FirstChanceException -= OnException;
        output.WriteLine(Description);
    }

    private sealed record Observation(DatabaseProvider Provider, DatabaseFailure Failure);
}
