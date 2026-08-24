using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadConverterContractRunnerTests
{
    private const string ArtifactSha256 =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public async Task Happy_path_binds_read_only_source_and_completed_sink_evidence()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1, 2, 3]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            Assert.False(input.CanWrite);
            Assert.Equal(1, input.ReadByte());
            return await WriteValidAsync(conversion, input, output, ct);
        });

        var result = await SpaceCadConverterContractRunner.ConvertAsync(
            converter,
            request,
            source,
            sink);

        Assert.Equal(ArtifactSha256, result.CadIrSha256);
        Assert.Equal(1, sink.DocumentWrites);
        Assert.Equal(1, sink.LayerWrites);
        Assert.Equal(1, sink.EntityWrites);
        Assert.Equal(1, sink.CompleteCalls);
        Assert.True(source.CanRead);
        source.Position = 0;
        Assert.Equal(1, source.ReadByte());
    }

    [Fact]
    public async Task Source_write_attempt_cannot_be_suppressed_by_converter()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1, 2, 3]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            _ = Assert.Throws<InvalidDataException>(() => input.WriteByte(9));
            return await WriteValidAsync(conversion, input, output, ct);
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.SourceWriteAttempt,
            problem.Message,
            StringComparison.Ordinal);
        Assert.Equal(new byte[] { 1, 2, 3 }, source.ToArray());
    }

    [Fact]
    public async Task Converter_must_complete_the_sink_before_returning()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, _, output, ct) =>
        {
            await output.WriteDocumentAsync(Document(conversion), ct);
            return Result(conversion, Summary(), []);
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.NotCompleted,
            problem.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, sink.CompleteCalls);
    }

    [Fact]
    public async Task Converter_result_hash_must_equal_completed_artifact_hash()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            var result = await WriteValidAsync(conversion, input, output, ct);
            return result with { CadIrSha256 = new string('d', 64) };
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ResultMismatch,
            problem.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Converter_result_summary_and_issues_must_equal_sink_completion()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            var result = await WriteValidAsync(conversion, input, output, ct);
            return result with
            {
                Summary = result.Summary with { EntityCount = 2 },
                Issues = [new SpaceCadConversionIssueV1(
                    "SPACE_CAD_DIFFERENT_ISSUE",
                    SpaceCadIssueSeverity.Warning)]
            };
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ResultMismatch,
            problem.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_result_provenance_is_rejected_with_a_stable_mismatch()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            var result = await WriteValidAsync(conversion, input, output, ct);
            return result with { ConverterVersion = null! };
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ResultMismatch,
            problem.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suppressed_duplicate_record_keeps_entire_conversion_failed()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            await output.WriteDocumentAsync(Document(conversion), ct);
            await output.WriteLayerAsync(Layer(), ct);
            var entity = Entity();
            await output.WriteEntityAsync(entity, ct);
            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await output.WriteEntityAsync(entity, ct));
            return Result(conversion, Summary(), []);
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ProtocolViolation,
            problem.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, sink.CompleteCalls);
    }

    [Fact]
    public async Task Header_records_after_entities_are_rejected_fail_closed()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            await output.WriteDocumentAsync(Document(conversion), ct);
            await output.WriteLayerAsync(Layer(), ct);
            await output.WriteEntityAsync(Entity(), ct);
            _ = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await output.WriteBlockAsync(
                    new SpaceCadIrBlockV1("B:1", "RACK", false, null, 0),
                    ct));
            return Result(conversion, Summary(), []);
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ProtocolViolation,
            problem.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Streamed_layer_counts_are_checked_before_artifact_completion()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink();
        var converter = new DelegateConverter(async (conversion, input, output, ct) =>
        {
            await output.WriteDocumentAsync(Document(conversion), ct);
            await output.WriteLayerAsync(Layer() with { EntityCount = 2 }, ct);
            await output.WriteEntityAsync(Entity(), ct);
            var artifactSha256 = await output.CompleteAsync([], Summary(), ct);
            Assert.Equal(ArtifactSha256, artifactSha256);
            return Result(conversion, Summary(), []);
        });

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ProtocolViolation,
            problem.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, sink.CompleteCalls);
    }

    [Fact]
    public async Task Sink_must_return_a_canonical_artifact_hash()
    {
        var request = ValidRequest();
        await using var source = new MemoryStream([1]);
        var sink = new RecordingSink { ArtifactSha256 = new string('C', 64) };
        var converter = new DelegateConverter(WriteValidAsync);

        var problem = await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                request,
                source,
                sink));

        Assert.Contains(
            SpaceCadConverterContractRunner.ProtocolViolation,
            problem.Message,
            StringComparison.Ordinal);
    }

    private static async Task<SpaceCadConversionResult> WriteValidAsync(
        SpaceCadConversionRequest request,
        Stream _,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken)
    {
        await sink.WriteDocumentAsync(Document(request), cancellationToken);
        await sink.WriteLayerAsync(Layer(), cancellationToken);
        await sink.WriteEntityAsync(Entity(), cancellationToken);
        IReadOnlyList<SpaceCadConversionIssueV1> issues = [];
        var summary = Summary();
        var artifactSha256 = await sink.CompleteAsync(
            issues,
            summary,
            cancellationToken);
        return Result(request, summary, issues) with
        {
            CadIrSha256 = artifactSha256
        };
    }

    private static SpaceCadConversionRequest ValidRequest() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new string('a', 64),
            SpaceCadSourceFormat.Dwg,
            "contract-provider",
            "1.2.3");

    private static SpaceCadIrDocumentV1 Document(
        SpaceCadConversionRequest request) =>
        new(
            SpaceCadIrVersions.SchemaVersion,
            request.SourceSha256,
            request.SourceFormat,
            "AC1032",
            SpaceCadUnit.Millimeter,
            1m,
            SpaceCadIrVersions.CoordinateSystem,
            Bounds(),
            request.ConverterId,
            request.ConverterVersion);

    private static SpaceCadIrLayerV1 Layer() =>
        new("WALL", "WALL", 1);

    private static SpaceCadIrEntityV1 Entity() =>
        new(
            "H:1",
            SpaceCadIrEntityType.Line,
            "LINE",
            "WALL",
            null,
            [new SpaceCadPointV1(0, 0), new SpaceCadPointV1(1000, 1000)],
            null,
            null,
            null,
            SpaceCadAffineTransformV1.Identity,
            Bounds(),
            false,
            true,
            new Dictionary<string, string>());

    private static SpaceCadIrSummaryV1 Summary() =>
        new(1, 0, 1, 1, 0, 0, Bounds());

    private static SpaceCadBoundsV1 Bounds() =>
        new(0, 0, 1000, 1000);

    private static SpaceCadConversionResult Result(
        SpaceCadConversionRequest request,
        SpaceCadIrSummaryV1 summary,
        IReadOnlyList<SpaceCadConversionIssueV1> issues) =>
        new(
            request.SourceSha256,
            ArtifactSha256,
            request.ConverterId,
            request.ConverterVersion,
            summary,
            issues);

    private sealed class DelegateConverter(
        Func<
            SpaceCadConversionRequest,
            Stream,
            ISpaceCadIrSink,
            CancellationToken,
            Task<SpaceCadConversionResult>> callback) : ICadConverter
    {
        public Task<SpaceCadConversionResult> ConvertAsync(
            SpaceCadConversionRequest request,
            Stream source,
            ISpaceCadIrSink sink,
            CancellationToken cancellationToken = default) =>
            callback(request, source, sink, cancellationToken);
    }

    private sealed class RecordingSink : ISpaceCadIrSink
    {
        public string ArtifactSha256 { get; init; } =
            SpaceCadConverterContractRunnerTests.ArtifactSha256;
        public int DocumentWrites { get; private set; }
        public int LayerWrites { get; private set; }
        public int EntityWrites { get; private set; }
        public int CompleteCalls { get; private set; }

        public ValueTask WriteDocumentAsync(
            SpaceCadIrDocumentV1 document,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentWrites++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteLayerAsync(
            SpaceCadIrLayerV1 layer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LayerWrites++;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteBlockAsync(
            SpaceCadIrBlockV1 block,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteEntityAsync(
            SpaceCadIrEntityV1 entity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EntityWrites++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> CompleteAsync(
            IReadOnlyList<SpaceCadConversionIssueV1> issues,
            SpaceCadIrSummaryV1 summary,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompleteCalls++;
            return ValueTask.FromResult(ArtifactSha256);
        }
    }
}
