namespace CP6.Space.Domain;

public sealed class SpaceFile : SpaceTenantEntity
{
    private SpaceFile()
    {
    }

    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalName { get; private set; } = string.Empty;
    public string? DeclaredContentType { get; private set; }
    public string? DetectedContentType { get; private set; }
    public string? Extension { get; private set; }
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public SpaceFileState State { get; private set; }
    public string? ScanEngine { get; private set; }
    public string? SignatureVersion { get; private set; }
    public string? ScanResultCode { get; private set; }
    public SpaceFileRetentionClass RetentionClass { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static SpaceFile CreateUploading(
        Guid id,
        Guid tenantId,
        string storageKey,
        string originalName,
        string? declaredContentType,
        SpaceFileRetentionClass retentionClass)
    {
        var file = new SpaceFile
        {
            StorageKey = RequireText(storageKey, 500, nameof(storageKey)),
            OriginalName = RequireText(originalName, 260, nameof(originalName)),
            DeclaredContentType = OptionalText(declaredContentType, 200, nameof(declaredContentType)),
            RetentionClass = retentionClass,
            State = SpaceFileState.Uploading,
        };
        file.SetId(id);
        file.SetTenant(tenantId);
        return file;
    }

    public void CompleteQuarantine(
        string detectedContentType,
        string extension,
        long sizeBytes,
        string sha256)
    {
        RequireState(SpaceFileState.Uploading);
        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        DetectedContentType = RequireText(
            detectedContentType,
            200,
            nameof(detectedContentType));
        Extension = NormalizeExtension(extension);
        SizeBytes = sizeBytes;
        Sha256 = RequireHash(sha256);
        State = SpaceFileState.Quarantined;
    }

    public void BeginScanning()
    {
        RequireState(SpaceFileState.Quarantined);
        State = SpaceFileState.Scanning;
    }

    public void MarkClean(
        string scanEngine,
        string signatureVersion,
        string scanResultCode = "CLEAN")
    {
        RequireState(SpaceFileState.Scanning);
        ScanEngine = RequireText(scanEngine, 100, nameof(scanEngine));
        SignatureVersion = RequireText(signatureVersion, 100, nameof(signatureVersion));
        ScanResultCode = RequireText(scanResultCode, 100, nameof(scanResultCode));
        State = SpaceFileState.Clean;
    }

    public void Reject(
        string scanResultCode,
        string? scanEngine = null,
        string? signatureVersion = null)
    {
        if (State is SpaceFileState.Clean or SpaceFileState.Deleted)
            throw new SpaceFileStateException($"File state {State} cannot be rejected.");

        ScanResultCode = RequireText(scanResultCode, 100, nameof(scanResultCode));
        ScanEngine = OptionalText(scanEngine, 100, nameof(scanEngine));
        SignatureVersion = OptionalText(signatureVersion, 100, nameof(signatureVersion));
        State = SpaceFileState.Rejected;
    }

    public void Delete(int activeReferenceCount)
    {
        if (activeReferenceCount < 0)
            throw new ArgumentOutOfRangeException(nameof(activeReferenceCount));
        if (activeReferenceCount != 0)
            throw new SpaceFileReferenceException(
                "A referenced Space file cannot be deleted.");
        if (State == SpaceFileState.Deleted)
            return;

        State = SpaceFileState.Deleted;
        MarkEntityDeleted();
    }

    private void RequireState(SpaceFileState expected)
    {
        if (State != expected)
            throw new SpaceFileStateException(
                $"File state must be {expected}, but was {State}.");
    }

    private static string NormalizeExtension(string value)
    {
        var normalized = RequireText(value, 20, nameof(value)).ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized : $".{normalized}";
    }

    private static string RequireHash(string value)
    {
        if (value is null || value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hex value is required.", nameof(value));
        return value.ToLowerInvariant();
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
            throw new ArgumentException(
                $"A value between 1 and {maxLength} characters is required.",
                parameterName);
        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return RequireText(value, maxLength, parameterName);
    }
}
