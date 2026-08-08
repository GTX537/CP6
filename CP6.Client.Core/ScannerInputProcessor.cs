namespace CP6.Client.Core;

public enum ScannerInputSource
{
    Manual,
    Hid,
    Camera,
    Broadcast,
}

public enum ScannerInputStatus
{
    Accepted,
    Duplicate,
    Invalid,
}

public enum ScannerHidTerminator
{
    Enter,
    Tab,
    None,
}

public sealed class ScannerInputOptions
{
    public const int DefaultDuplicateWindowMilliseconds = 750;
    public const int DefaultMaxLength = 2048;

    public string Prefix { get; init; } = string.Empty;
    public string Suffix { get; init; } = string.Empty;
    public ScannerHidTerminator HidTerminator { get; init; } = ScannerHidTerminator.Enter;
    public TimeSpan DuplicateWindow { get; init; } =
        TimeSpan.FromMilliseconds(DefaultDuplicateWindowMilliseconds);
    public int MaxLength { get; init; } = DefaultMaxLength;
}

public sealed record ScannerInputResult(
    ScannerInputStatus Status,
    string? Value = null,
    string? ErrorCode = null)
{
    public bool IsAccepted => Status == ScannerInputStatus.Accepted;
}

public sealed class ScannerInputProcessor
{
    private readonly object _gate = new();
    private readonly ScannerInputOptions _options;
    private readonly Dictionary<string, DateTimeOffset> _recent =
        new(StringComparer.Ordinal);

    public ScannerInputProcessor(ScannerInputOptions? options = null)
    {
        options ??= new ScannerInputOptions();
        if (options.MaxLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxLength must be positive.");
        if (options.DuplicateWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "DuplicateWindow cannot be negative.");

        _options = new ScannerInputOptions
        {
            Prefix = options.Prefix ?? string.Empty,
            Suffix = options.Suffix ?? string.Empty,
            HidTerminator = options.HidTerminator,
            DuplicateWindow = options.DuplicateWindow,
            MaxLength = options.MaxLength,
        };
    }

    public ScannerHidTerminator HidTerminator => _options.HidTerminator;

    public ScannerInputResult Accept(
        string? rawValue,
        ScannerInputSource source,
        DateTimeOffset? receivedAt = null)
    {
        if (string.IsNullOrEmpty(rawValue) || rawValue.Length > _options.MaxLength)
            return Invalid("WM-SCAN-INPUT-INVALID");

        var value = rawValue.TrimEnd('\r', '\n', '\t').Trim();
        if (value.Length == 0 || value.Length > _options.MaxLength)
            return Invalid("WM-SCAN-INPUT-INVALID");

        var requireFraming = source == ScannerInputSource.Hid;
        if (!TryRemovePrefix(value, requireFraming, out value))
            return Invalid("WM-SCAN-PREFIX-MISMATCH");
        if (!TryRemoveSuffix(value, requireFraming, out value))
            return Invalid("WM-SCAN-SUFFIX-MISMATCH");

        value = value.Trim();
        if (value.Length == 0
            || value.Length > _options.MaxLength
            || value.Any(char.IsControl))
            return Invalid("WM-SCAN-INPUT-INVALID");

        var timestamp = receivedAt ?? DateTimeOffset.UtcNow;
        lock (_gate)
        {
            RemoveExpired(timestamp);
            if (_recent.TryGetValue(value, out var previous)
                && timestamp >= previous
                && timestamp - previous <= _options.DuplicateWindow)
            {
                return new ScannerInputResult(
                    ScannerInputStatus.Duplicate,
                    value,
                    "WM-SCAN-DUPLICATE-IGNORED");
            }

            if (_options.DuplicateWindow > TimeSpan.Zero)
                _recent[value] = timestamp;
        }

        return new ScannerInputResult(ScannerInputStatus.Accepted, value);
    }

    private bool TryRemovePrefix(
        string value,
        bool required,
        out string normalized)
    {
        normalized = value;
        if (_options.Prefix.Length == 0) return true;
        if (value.StartsWith(_options.Prefix, StringComparison.Ordinal))
        {
            normalized = value[_options.Prefix.Length..];
            return true;
        }
        return !required;
    }

    private bool TryRemoveSuffix(
        string value,
        bool required,
        out string normalized)
    {
        normalized = value;
        if (_options.Suffix.Length == 0) return true;
        if (value.EndsWith(_options.Suffix, StringComparison.Ordinal))
        {
            normalized = value[..^_options.Suffix.Length];
            return true;
        }
        return !required;
    }

    private void RemoveExpired(DateTimeOffset timestamp)
    {
        if (_recent.Count == 0) return;
        if (_options.DuplicateWindow == TimeSpan.Zero)
        {
            _recent.Clear();
            return;
        }

        var expired = _recent
            .Where(pair => timestamp < pair.Value
                           || timestamp - pair.Value > _options.DuplicateWindow)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
            _recent.Remove(key);
    }

    private static ScannerInputResult Invalid(string errorCode) =>
        new(ScannerInputStatus.Invalid, ErrorCode: errorCode);
}
