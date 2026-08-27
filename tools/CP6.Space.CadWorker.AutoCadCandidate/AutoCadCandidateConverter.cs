using CP6.Space.Application;
using CP6.Space.CadExperiment;
using CP6.Space.Contracts;

namespace CP6.Space.CadWorker.AutoCadCandidate;

/// <summary>
/// Candidate chain identity covering native DWG through AutoCAD Core Console
/// and native DXF through the same managed DXF-to-CAD-IR parser. Both inner
/// converters remain behind the mandatory converter contract runner.
/// </summary>
public sealed class AutoCadCandidateConverter : ICadConverter
{
    public const string DevelopmentConverterId = "cp6-autocad-worker-development";

    private readonly IAutoCadDwgExporter _exporter;
    private readonly string _workingRoot;
    private readonly string _converterId;

    public AutoCadCandidateConverter(
        IAutoCadDwgExporter exporter,
        string workingRoot) :
        this(
            exporter ?? throw new ArgumentNullException(nameof(exporter)),
            workingRoot,
            DevelopmentConverterId,
            VersionFor(exporter.ProviderVersion))
    {
    }

    internal AutoCadCandidateConverter(
        IAutoCadDwgExporter exporter,
        string workingRoot,
        string converterId,
        string converterVersion)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        if (string.IsNullOrWhiteSpace(workingRoot))
            throw new ArgumentException("A candidate conversion root is required.");
        _workingRoot = Path.GetFullPath(workingRoot);
        if (string.IsNullOrWhiteSpace(converterId)
            || converterId.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new ArgumentException("A bounded candidate converter ID is required.");
        }
        if (string.IsNullOrWhiteSpace(converterVersion)
            || converterVersion.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new ArgumentException("A bounded candidate converter version is required.");
        }
        _converterId = converterId;
        ConverterVersion = converterVersion;
    }

    public string ConverterVersion { get; }

    public static string VersionFor(string autoCadProviderVersion)
    {
        var normalized = autoCadProviderVersion?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Any(char.IsWhiteSpace) ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A bounded AutoCAD Provider version is required.",
                nameof(autoCadProviderVersion));
        }
        var version =
            $"{normalized}+cp6-dxf-{DevelopmentDxfCadConverter.ConverterVersion}";
        if (version.Length > 100)
        {
            throw new ArgumentException(
                "The composite candidate Provider version is too long.",
                nameof(autoCadProviderVersion));
        }
        return version;
    }

    public async Task<SpaceCadConversionResult> ConvertAsync(
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken = default)
    {
        SpaceCadConversionContract.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));
        if (request.ConverterId != _converterId ||
            request.ConverterVersion != ConverterVersion)
        {
            throw new InvalidDataException(
                "The candidate converter identity does not match its frozen chain.");
        }

        Directory.CreateDirectory(_workingRoot);
        var innerRequest = request with
        {
            ConverterId = request.SourceFormat == SpaceCadSourceFormat.Dwg
                ? AutoCadCoreConsoleDevelopmentConverter.ConverterId
                : DevelopmentDxfCadConverter.ConverterId,
            ConverterVersion = request.SourceFormat == SpaceCadSourceFormat.Dwg
                ? _exporter.ProviderVersion
                : DevelopmentDxfCadConverter.ConverterVersion,
        };
        ICadConverter innerConverter = request.SourceFormat switch
        {
            SpaceCadSourceFormat.Dwg => new AutoCadCoreConsoleDevelopmentConverter(
                _exporter,
                Path.Combine(_workingRoot, "autocad")),
            SpaceCadSourceFormat.Dxf => new DevelopmentDxfCadConverter(),
            _ => throw new InvalidDataException(
                "The candidate converter accepts only DWG or DXF."),
        };
        var innerOutput = Path.Combine(
            _workingRoot,
            $"inner-{Guid.NewGuid():N}.json");
        var innerSink = new DevelopmentCadIrFileSink(innerRequest, innerOutput);
        _ = await SpaceCadConverterContractRunner.ConvertAsync(
            innerConverter,
            innerRequest,
            source,
            innerSink,
            cancellationToken);
        var package = innerSink.Package ?? throw new InvalidDataException(
            "The candidate converter did not complete an inner CAD IR package.");

        var document = package.Document with
        {
            ConverterId = request.ConverterId,
            ConverterVersion = request.ConverterVersion,
        };
        await sink.WriteDocumentAsync(document, cancellationToken);
        foreach (var layer in package.Layers)
            await sink.WriteLayerAsync(layer, cancellationToken);
        foreach (var block in package.Blocks)
            await sink.WriteBlockAsync(block, cancellationToken);
        foreach (var entity in package.Entities)
            await sink.WriteEntityAsync(entity, cancellationToken);
        var cadIrSha256 = await sink.CompleteAsync(
            package.Issues,
            package.Summary,
            cancellationToken);
        return new SpaceCadConversionResult(
            request.SourceSha256,
            cadIrSha256,
            request.ConverterId,
            request.ConverterVersion,
            package.Summary,
            package.Issues);
    }
}
