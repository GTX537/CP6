using System.Security.Cryptography;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadWorker.AutoCadCandidate;

public sealed class ReleaseBoundAutoCadDwgExporter : IAutoCadDwgExporter
{
    private readonly IAutoCadDwgExporter _inner;
    private readonly string _coreConsolePath;
    private readonly byte[] _expectedSha256;

    public ReleaseBoundAutoCadDwgExporter(
        IAutoCadDwgExporter inner,
        string coreConsolePath,
        string expectedProviderVersion,
        string expectedSha256)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _coreConsolePath = Path.GetFullPath(coreConsolePath);
        if (!File.Exists(_coreConsolePath))
            throw new FileNotFoundException(
                "The release-bound AutoCAD Core Console does not exist.",
                _coreConsolePath);
        if ((File.GetAttributes(_coreConsolePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The release-bound AutoCAD Core Console cannot be a reparse point.");
        }
        if (!string.Equals(
                _inner.ProviderVersion,
                expectedProviderVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The AutoCAD exporter version does not match the Worker release.");
        }
        if (string.IsNullOrWhiteSpace(expectedSha256)
            || expectedSha256.Length != 64
            || expectedSha256.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                "The release-bound AutoCAD Core Console SHA-256 is invalid.");
        }
        CoreConsoleSha256 = expectedSha256;
        _expectedSha256 = Convert.FromHexString(CoreConsoleSha256);
    }

    public string ProviderVersion => _inner.ProviderVersion;
    public string CoreConsoleSha256 { get; }

    public async Task ExportDxfAsync(
        string inputDwgPath,
        string outputDxfPath,
        CancellationToken cancellationToken = default)
    {
        await using var executable = new FileStream(
            _coreConsolePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actualSha256 = await SHA256.HashDataAsync(executable, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actualSha256, _expectedSha256))
        {
            throw new InvalidDataException(
                "The AutoCAD Core Console changed after Worker release verification.");
        }
        await _inner.ExportDxfAsync(
            inputDwgPath,
            outputDxfPath,
            cancellationToken);
    }
}
