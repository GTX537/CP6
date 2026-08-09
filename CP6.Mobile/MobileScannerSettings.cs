using CP6.Client.Core;

namespace CP6.Mobile;

internal static class MobileScannerSettings
{
    private const string PrefixKey = "cp6.scan-prefix";
    private const string SuffixKey = "cp6.scan-suffix";
    private const string TerminatorKey = "cp6.scan-terminator";
    private const string DuplicateWindowKey = "cp6.scan-duplicate-ms";

    public const int MinimumDuplicateWindowMilliseconds = 100;
    public const int MaximumDuplicateWindowMilliseconds = 5000;
    public const int MaximumFramingLength = 32;

    public static ScannerInputOptions Read()
    {
        var terminatorValue = Preferences.Default.Get(
            TerminatorKey,
            ScannerHidTerminator.Enter.ToString());
        if (!Enum.TryParse<ScannerHidTerminator>(
                terminatorValue,
                ignoreCase: true,
                out var terminator))
        {
            terminator = ScannerHidTerminator.Enter;
        }

        var duplicateMilliseconds = Math.Clamp(
            Preferences.Default.Get(
                DuplicateWindowKey,
                ScannerInputOptions.DefaultDuplicateWindowMilliseconds),
            MinimumDuplicateWindowMilliseconds,
            MaximumDuplicateWindowMilliseconds);

        return new ScannerInputOptions
        {
            Prefix = Preferences.Default.Get(PrefixKey, string.Empty),
            Suffix = Preferences.Default.Get(SuffixKey, string.Empty),
            HidTerminator = terminator,
            DuplicateWindow = TimeSpan.FromMilliseconds(duplicateMilliseconds),
        };
    }

    public static void Save(ScannerInputOptions options)
    {
        Preferences.Default.Set(PrefixKey, options.Prefix);
        Preferences.Default.Set(SuffixKey, options.Suffix);
        Preferences.Default.Set(TerminatorKey, options.HidTerminator.ToString());
        Preferences.Default.Set(
            DuplicateWindowKey,
            (int)options.DuplicateWindow.TotalMilliseconds);
    }
}
