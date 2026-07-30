using System.Text;

namespace CP6.Space.CadExperiment;

public sealed record DwgHeaderProbeResult(
    string? CadVersion,
    bool HeaderValid,
    IReadOnlyList<string> Errors);

public static class DwgHeaderProbe
{
    private const int VersionHeaderLength = 6;

    public static DwgHeaderProbeResult Inspect(string path)
    {
        var errors = new List<string>();
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        if (stream.Length < VersionHeaderLength)
        {
            errors.Add("DWG is shorter than its six-byte version header.");
            return new DwgHeaderProbeResult(null, false, errors);
        }

        Span<byte> header = stackalloc byte[VersionHeaderLength];
        stream.ReadExactly(header);
        var version = Encoding.ASCII.GetString(header);
        var headerValid = version.Length == VersionHeaderLength
            && version.StartsWith("AC", StringComparison.Ordinal)
            && version.AsSpan(2).ToString().All(char.IsAsciiDigit);
        if (!headerValid)
        {
            errors.Add(
                $"DWG version header '{ToPrintableAscii(header)}' is not an ACdddd signature.");
        }

        return new DwgHeaderProbeResult(
            headerValid ? version : null,
            headerValid,
            errors);
    }

    private static string ToPrintableAscii(ReadOnlySpan<byte> value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            result.Append(character is >= 0x20 and <= 0x7e ? (char)character : '?');
        }

        return result.ToString();
    }
}
