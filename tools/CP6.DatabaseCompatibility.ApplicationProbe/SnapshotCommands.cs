using System.Text.Json;
using CP6.DatabaseCompatibility.Testing;

namespace CP6.DatabaseCompatibility.ApplicationProbe;

internal static partial class AppProbe
{
    private static async Task CaptureAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        var state = await ReadSnapshotAsync(owned, report, ct);
        SnapshotReport(state, report);
        report.Assertions.Add(new("native-all-tables-captured", true));
        report.Assertions.Add(new("capture-single-consistent-transaction", true));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        // Exclusive creation: callers must supply a new explicit private path. Never overwrite a baseline.
        await using (var stream = new FileStream(options.StatePath!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
        }
        Require(Hash(await File.ReadAllBytesAsync(options.StatePath!, ct)) == Hash(bytes), "private-state-byte-verification");
        report.Hashes["StateSha256"] = Hash(bytes);
        report.Assertions.Add(new("private-state-created-exclusive", true));
    }

    private static async Task VerifyAsync(OwnedTestDatabase owned, ProbeArguments options, SafeReport report, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(options.StatePath!, ct);
        var baseline = JsonSerializer.Deserialize<SnapshotState>(bytes, Json);
        Require(baseline is not null && baseline.SchemaVersion == 1 && baseline.Tables is not null
            && baseline.Histories is not null && baseline.Sequences is not null, "private-baseline-schema");
        report.Hashes["StateSha256"] = Hash(bytes);
        var sameProvider = baseline!.Provider == owned.Database.Provider.ToString();
        report.Assertions.Add(new("baseline-provider-matches", sameProvider));
        Require(sameProvider, "baseline-provider-matches");
        var actual = await ReadSnapshotAsync(owned, report, ct);
        SnapshotReport(actual, report);
        Require(baseline.NativeEncoding == actual.NativeEncoding, "native-encoding-matches");
        var expectedCatalog = CatalogHash(baseline);
        var actualCatalog = CatalogHash(actual);
        var expectedContent = ContentHash(baseline);
        var actualContent = ContentHash(actual);
        var expectedSequences = ObjectHash(baseline.Sequences);
        var actualSequences = ObjectHash(actual.Sequences);
        var expectedHistory = ObjectHash(baseline.Histories);
        var actualHistory = ObjectHash(actual.Histories);
        var catalog = expectedCatalog == actualCatalog;
        var content = expectedContent == actualContent;
        var sequences = expectedSequences == actualSequences;
        var history = expectedHistory == actualHistory;
        report.Assertions.Add(new("table-catalog-and-columns-match", catalog, expectedCatalog, actualCatalog,
            baseline.Tables.Count, actual.Tables.Count));
        report.Assertions.Add(new("all-table-counts-and-content-match", content, expectedContent, actualContent,
            baseline.Tables.Sum(x => x.Rows), actual.Tables.Sum(x => x.Rows)));
        report.Assertions.Add(new("sequence-state-matches", sequences, expectedSequences, actualSequences,
            baseline.Sequences.Count, actual.Sequences.Count));
        report.Assertions.Add(new("history-state-matches", history, expectedHistory, actualHistory,
            baseline.Histories.Count, actual.Histories.Count));
        // Detailed comparisons contain only names/counts/hashes, never raw cells or sequence counter values.
        report.TableComparisons = baseline.Tables.Select(expected =>
        {
            var found = actual.Tables.SingleOrDefault(x => x.Schema == expected.Schema && x.Table == expected.Table);
            return new TableComparison(expected.Schema, expected.Table,
                found is not null && expected.ColumnsSha256 == found.ColumnsSha256,
                found is not null && expected.Rows == found.Rows && expected.ContentSha256 == found.ContentSha256,
                expected.Rows, found?.Rows, expected.ColumnsSha256, found?.ColumnsSha256,
                expected.ContentSha256, found?.ContentSha256);
        }).Concat(actual.Tables.Where(found => !baseline.Tables.Any(x => x.Schema == found.Schema && x.Table == found.Table))
            .Select(found => new TableComparison(found.Schema, found.Table, false, false,
                null, found.Rows, null, found.ColumnsSha256, null, found.ContentSha256))).ToList();
        Require(catalog && content && sequences && history, "restored-state-exact-match");
    }

    private static void SnapshotReport(SnapshotState state, SafeReport report)
    {
        report.Counts["Tables"] = state.Tables.Count;
        report.Counts["Rows"] = state.Tables.Sum(x => x.Rows);
        report.Counts["Sequences"] = state.Sequences.Count;
        report.Hashes["CatalogSha256"] = CatalogHash(state);
        report.Hashes["ContentSha256"] = ContentHash(state);
        report.Hashes["SequenceSha256"] = ObjectHash(state.Sequences);
        report.Hashes["HistorySha256"] = ObjectHash(state.Histories);
        report.TableSummaries = state.Tables.Select(x => new TableSummary(x.Schema, x.Table, x.Rows,
            x.ColumnsSha256, x.ContentSha256)).ToList();
    }

    private static string CatalogHash(SnapshotState state) => ObjectHash(state.Tables.Select(x =>
        new { x.Schema, x.Table, x.ColumnsSha256 }).ToArray());

    private static string ContentHash(SnapshotState state) => ObjectHash(state.Tables.Select(x =>
        new { x.Schema, x.Table, x.Rows, x.ContentSha256 }).ToArray());
}

internal sealed record TableComparison(string Schema, string Table, bool ColumnsMatch, bool ContentMatch,
    long? ExpectedRows, long? ActualRows, string? ExpectedColumnsSha256, string? ActualColumnsSha256,
    string? ExpectedContentSha256, string? ActualContentSha256);
