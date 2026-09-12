using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;

namespace CP6.ErpIntegration.SqlTests;

/// <summary>Test-only diagnosis records SQL error numbers; SQL text, connection details and values are never collected.</summary>
internal sealed class SqlFailureProbe : IDisposable
{
    private readonly ConcurrentQueue<int> numbers = new();
    public SqlFailureProbe() => AppDomain.CurrentDomain.FirstChanceException += OnException;
    public int[] Numbers => numbers.Distinct().Order().ToArray();

    private void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not SqlException sql) return;
        foreach (SqlError error in sql.Errors) numbers.Enqueue(error.Number);
    }

    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnException;
}
