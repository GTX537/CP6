using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using CP6.Space.Infrastructure;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    internal static readonly JsonSerializerOptions Json = new()
        { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal static void Require([DoesNotReturnIf(false)] bool condition, string assertion)
    {
        if (!condition) throw new ProbeFailure(assertion);
    }

    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    internal static string ObjectHash<T>(T value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value, Json));

    internal static async Task<int> RunAsync(string[] args)
    {
        SafeReport? report = null;
        FileStream? output = null;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var options = ProbeArguments.Parse(args);
            cancellation.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
            output = new FileStream(options.ReportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            report = new(options.Command, options.Provider.ToString());
            report.Inputs = [AssemblyInput(typeof(AppProbe)), AssemblyInput(typeof(CP6Context)), AssemblyInput(typeof(SpaceContext))];
            var database = new DatabaseOptions(options.Provider);
            var connection = Environment.GetEnvironmentVariable(options.Provider == DatabaseProvider.SqlServer
                ? "CP6_TEST_SQLSERVER" : "CP6_TEST_POSTGRES");
            var owned = OwnedTestDatabase.FromEnvironment(database, connection,
                [DatabaseFixtureRole.Application, DatabaseFixtureRole.Restore], "CP6.WP6.ApplicationProbe");
            Require(owned is not null, "wp6-owned-configuration-required");
            var identity = await owned!.VerifyAsync(cancellation.Token);
            report.DatabaseName = identity.DatabaseName;
            report.Role = owned.Role.ToString();
            report.Assertions.Add(new("owner-and-physical-database-verified", true));
            switch (options.Command)
            {
                case "histories":
                    report.HistoryProfiles = await ReadHistoriesAsync(owned, null, null, cancellation.Token);
                    report.Counts["EffectiveProfiles"] = report.HistoryProfiles.Count;
                    report.Assertions.Add(new("four-effective-history-profiles-complete", true));
                    break;
                case "enqueue-notification": await EnqueueAsync(owned, options, report, cancellation.Token); break;
                case "notification-status": await NotificationStatusAsync(owned, options, report, cancellation.Token); break;
                case "capture": await CaptureAsync(owned, options, report, cancellation.Token); break;
                case "verify": await VerifyAsync(owned, options, report, cancellation.Token); break;
                case "signalr-verification": await SignalRVerificationAsync(owned, options, report, cancellation.Token); break;
                case "replay-notification": await ReplayNotificationAsync(owned, options, report, cancellation.Token); break;
            }
            report.Status = "Passed";
        }
        catch (Exception exception)
        {
            var kind = exception is ProbeFailure failure ? failure.Assertion
                : exception is OperationCanceledException ? "bounded-operation-cancelled"
                : exception.GetBaseException().GetType().Name;
            if (report is not null && output is not null)
            {
                report.Status = "Failed";
                report.FailureKind = kind;
                var native = DatabaseFailureClassifier.Classify(exception);
                report.NativeFailure = new(native.Kind.ToString(), native.SqlState, native.DatabaseErrorCode);
            }
            else Console.Error.WriteLine(JsonSerializer.Serialize(new { Status = "Failed", FailureKind = kind }));
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
        if (report is null || output is null) return 1;
        await using (output)
        {
            try
            {
                await WriteReportAsync(output, report);
                Console.WriteLine(JsonSerializer.Serialize(report, Json));
                return report.Status == "Passed" ? 0 : 1;
            }
            catch
            {
                // Do not append a second JSON object to a partially written exclusive report.
                Console.Error.WriteLine("{\"Status\":\"Failed\",\"FailureKind\":\"public-report-write-failed\"}");
                return 1;
            }
        }
    }

    private static AssemblyObservation AssemblyInput(Type type)
    {
        var assembly = type.Assembly;
        return new(assembly.GetName().Name!, assembly.GetName().Version?.ToString(), Hash(File.ReadAllBytes(assembly.Location)));
    }

    private static async Task WriteReportAsync(FileStream stream, SafeReport report)
    {
        report.FinishedUtc = DateTimeOffset.UtcNow;
        // The stream was exclusively created before work. No existing report is replaced.
        await JsonSerializer.SerializeAsync(stream, report, Json);
        await stream.FlushAsync();
    }
}

internal sealed class SafeReport(string command, string provider)
{
    public int SchemaVersion { get; } = 1;
    public string Task { get; } = "DB-COMPAT-01-WP6";
    public string Command { get; } = command;
    public string Provider { get; } = provider;
    public string? DatabaseName { get; set; }
    public string? Role { get; set; }
    public string Status { get; set; } = "Preparing";
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset FinishedUtc { get; set; }
    public List<NamedAssertion> Assertions { get; } = [];
    public Dictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);
    public List<HistoryState> HistoryProfiles { get; set; } = [];
    public List<TableSummary> TableSummaries { get; set; } = [];
    public List<TableComparison> TableComparisons { get; set; } = [];
    public List<AssemblyObservation> Inputs { get; set; } = [];
    public string? FailureKind { get; set; }
    public NativeFailure? NativeFailure { get; set; }
    public string Scope { get; } = command == "signalr-verification"
        ? "WP6 owned application/restore fixture and loopback API: actual HTTP login, two WebSocket SignalR clients, exact user delivery, two subsequent real worker batches and production event replay. One target plus two fixture marker rows. Public hashes/counts only; no cookies/passwords/tokens/raw payloads. Bounded observation, not external CRM/email delivery, DP decryption or complete application acceptance."
        : "WP6 owned application/restore fixture only; metadata, counts and hashes. No raw connection, password, token, DP XML or row values. Does not establish HTTP authentication, DP decryption, external delivery or complete application acceptance.";
}

internal sealed record NamedAssertion(string Name, bool Passed, string? ExpectedSha256 = null,
    string? ActualSha256 = null, long? ExpectedCount = null, long? ActualCount = null);
internal sealed record AssemblyObservation(string Name, string? Version, string Sha256);
internal sealed record NativeFailure(string Kind, string? SqlState, int? DatabaseErrorCode);
internal sealed record TableSummary(string Schema, string Table, long Rows, string ColumnsSha256, string ContentSha256);
