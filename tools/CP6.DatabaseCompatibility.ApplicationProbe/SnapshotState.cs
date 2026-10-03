namespace CP6.DatabaseCompatibility.ApplicationProbe;

// Private state has no raw cells, connection strings, credentials or DP XML.
// Keep it private: object names and content digests still describe a database.
internal sealed record SnapshotState(int SchemaVersion, string Provider, string SourceDatabase,
    string SourceRole, string NativeEncoding, DateTimeOffset CapturedUtc,
    List<HistoryState> Histories, List<TableState> Tables, List<SequenceState> Sequences);

internal sealed record TableState(string Schema, string Table, List<ColumnState> Columns,
    string ColumnsSha256, long Rows, string ContentSha256);

internal sealed record ColumnState(int Ordinal, string Name, string Type, int Length,
    int Precision, int Scale, bool Nullable, string? Collation, string? Default,
    string? Generated, string? Identity, string? SendSchema, string? SendFunction);

internal sealed record SequenceState(string Kind, string Schema, string Name, string? Column,
    string Definition, string? Value, bool? IsCalled);
