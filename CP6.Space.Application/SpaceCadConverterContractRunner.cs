using CP6.Space.Contracts;

namespace CP6.Space.Application;

/// <summary>
/// The mandatory vendor-neutral execution boundary for an <see cref="ICadConverter"/>.
/// It keeps the source stream read-only, validates the streaming sink protocol, and
/// binds the converter result to the artifact hash, summary, and issues actually
/// committed by the sink.
/// </summary>
public static class SpaceCadConverterContractRunner
{
    public const string ProtocolViolation = "SPACE_CAD_CONVERTER_PROTOCOL_VIOLATION";
    public const string SourceWriteAttempt = "SPACE_CAD_CONVERTER_SOURCE_WRITE_ATTEMPT";
    public const string NotCompleted = "SPACE_CAD_CONVERTER_NOT_COMPLETED";
    public const string ResultMismatch = "SPACE_CAD_CONVERTER_RESULT_MISMATCH";

    public static async Task<SpaceCadConversionResult> ConvertAsync(
        ICadConverter converter,
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        SpaceCadConversionContract.ValidateRequest(request);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));

        var readOnlySource = new ReadOnlySourceStream(source);
        var guardedSink = new GuardedSink(request, sink);
        var result = await converter.ConvertAsync(
            request,
            readOnlySource,
            guardedSink,
            cancellationToken);

        if (readOnlySource.WriteAttempted)
        {
            throw Violation(
                SourceWriteAttempt,
                "The converter attempted to modify the read-only CAD source stream.");
        }
        guardedSink.ThrowIfViolated();
        guardedSink.EnsureCompleted();
        ValidateResult(request, guardedSink, result);
        return result;
    }

    private static void ValidateResult(
        SpaceCadConversionRequest request,
        GuardedSink sink,
        SpaceCadConversionResult? result)
    {
        if (result is null)
            throw Violation(ResultMismatch, "The converter returned a null result.");
        try
        {
            SpaceCadConversionContract.ValidateArtifactSha256(result.CadIrSha256);
        }
        catch (ArgumentException exception)
        {
            throw Violation(
                ResultMismatch,
                "The converter result contains an invalid artifact SHA-256.",
                exception);
        }
        if (!string.Equals(
                result.SourceSha256,
                request.SourceSha256,
                StringComparison.Ordinal) ||
            !string.Equals(
                result.ConverterId,
                request.ConverterId,
                StringComparison.Ordinal) ||
            !string.Equals(
                result.ConverterVersion,
                request.ConverterVersion,
                StringComparison.Ordinal) ||
            !string.Equals(
                result.CadIrSha256,
                sink.CadIrSha256,
                StringComparison.Ordinal) ||
            result.Summary != sink.Summary ||
            result.Issues is null ||
            !result.Issues.SequenceEqual(sink.Issues))
        {
            throw Violation(
                ResultMismatch,
                "The converter result does not match the request and completed sink artifact.");
        }
    }

    private static InvalidDataException Violation(
        string code,
        string message,
        Exception? innerException = null) =>
        new($"{code}: {message}", innerException);

    private sealed class GuardedSink(
        SpaceCadConversionRequest request,
        ISpaceCadIrSink inner) : ISpaceCadIrSink
    {
        private readonly HashSet<string> _layerIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _declaredLayerCounts =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _actualLayerCounts =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _blockIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sourceRefs = new(StringComparer.Ordinal);
        private SpaceCadIrDocumentV1? _document;
        private Exception? _violation;
        private int _operationInProgress;
        private bool _entitiesStarted;
        private bool _completed;
        private long _entityCount;
        private long _supportedEntityCount;
        private long _unsupportedEntityCount;

        public string CadIrSha256 { get; private set; } = string.Empty;
        public SpaceCadIrSummaryV1? Summary { get; private set; }
        public IReadOnlyList<SpaceCadConversionIssueV1> Issues { get; private set; } = [];

        public async ValueTask WriteDocumentAsync(
            SpaceCadIrDocumentV1 document,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                EnsureWritable();
                if (_document is not null)
                    throw Protocol("The CAD IR document may be written exactly once.");
                SpaceCadConversionContract.ValidateDocument(request, document);
                await inner.WriteDocumentAsync(document, cancellationToken);
                _document = document;
            }
            catch (Exception exception)
            {
                RecordViolation(exception);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public async ValueTask WriteLayerAsync(
            SpaceCadIrLayerV1 layer,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                EnsureRecordHeaderPhase("layer");
                SpaceCadConversionContract.ValidateLayer(layer);
                if (_layerIds.Contains(layer.LayerId))
                    throw Protocol($"Duplicate CAD layer '{layer.LayerId}'.");
                await inner.WriteLayerAsync(layer, cancellationToken);
                _layerIds.Add(layer.LayerId);
                _declaredLayerCounts.Add(layer.LayerId, layer.EntityCount);
            }
            catch (Exception exception)
            {
                RecordViolation(exception);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public async ValueTask WriteBlockAsync(
            SpaceCadIrBlockV1 block,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                EnsureRecordHeaderPhase("block");
                SpaceCadConversionContract.ValidateBlock(block);
                if (_blockIds.Contains(block.BlockId))
                    throw Protocol($"Duplicate CAD block '{block.BlockId}'.");
                await inner.WriteBlockAsync(block, cancellationToken);
                _blockIds.Add(block.BlockId);
            }
            catch (Exception exception)
            {
                RecordViolation(exception);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public async ValueTask WriteEntityAsync(
            SpaceCadIrEntityV1 entity,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                EnsureDocumentWritten();
                EnsureWritable();
                SpaceCadConversionContract.ValidateEntity(entity);
                if (_sourceRefs.Contains(entity.SourceRef))
                    throw Protocol($"Duplicate CAD source reference '{entity.SourceRef}'.");
                await inner.WriteEntityAsync(entity, cancellationToken);
                _entitiesStarted = true;
                _sourceRefs.Add(entity.SourceRef);
                _entityCount++;
                if (entity.IsSupported)
                    _supportedEntityCount++;
                else
                    _unsupportedEntityCount++;
                _actualLayerCounts[entity.LayerId] =
                    _actualLayerCounts.GetValueOrDefault(entity.LayerId) + 1;
            }
            catch (Exception exception)
            {
                RecordViolation(exception);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public async ValueTask<string> CompleteAsync(
            IReadOnlyList<SpaceCadConversionIssueV1> issues,
            SpaceCadIrSummaryV1 summary,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                EnsureDocumentWritten();
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(issues);
                SpaceCadConversionContract.ValidateSummary(summary);
                foreach (var issue in issues)
                    SpaceCadConversionContract.ValidateIssue(issue);
                ValidateAggregate(issues, summary);
                var artifactSha256 = await inner.CompleteAsync(
                    issues,
                    summary,
                    cancellationToken);
                try
                {
                    SpaceCadConversionContract.ValidateArtifactSha256(artifactSha256);
                }
                catch (ArgumentException exception)
                {
                    throw Protocol(
                        "The CAD IR sink returned an invalid artifact SHA-256.",
                        exception);
                }
                _completed = true;
                CadIrSha256 = artifactSha256;
                Summary = summary;
                Issues = issues.ToArray();
                return artifactSha256;
            }
            catch (Exception exception)
            {
                RecordViolation(exception);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        public void ThrowIfViolated()
        {
            var violation = Volatile.Read(ref _violation);
            if (violation is not null)
            {
                throw Violation(
                    ProtocolViolation,
                    "The converter suppressed a sink protocol failure.",
                    violation);
            }
        }

        public void EnsureCompleted()
        {
            if (!_completed)
            {
                throw Violation(
                    NotCompleted,
                    "The converter returned before completing the CAD IR sink.");
            }
        }

        private void ValidateAggregate(
            IReadOnlyList<SpaceCadConversionIssueV1> issues,
            SpaceCadIrSummaryV1 summary)
        {
            foreach (var (layerId, actualCount) in _actualLayerCounts)
            {
                if (!_declaredLayerCounts.ContainsKey(layerId))
                    throw Protocol($"CAD entity references unknown layer '{layerId}'.");
                if (_declaredLayerCounts[layerId] != actualCount)
                {
                    throw Protocol(
                        $"CAD layer '{layerId}' entity count does not match its records.");
                }
            }
            foreach (var (layerId, declaredCount) in _declaredLayerCounts)
            {
                if (declaredCount != _actualLayerCounts.GetValueOrDefault(layerId))
                {
                    throw Protocol(
                        $"CAD layer '{layerId}' entity count does not match its records.");
                }
            }
            var missingSourceRefCount = issues.LongCount(issue =>
                issue.Code.Equals(
                    "SPACE_CAD_SOURCE_REF_SYNTHESIZED",
                    StringComparison.Ordinal));
            if (summary.LayerCount != _layerIds.Count ||
                summary.BlockCount != _blockIds.Count ||
                summary.EntityCount != _entityCount ||
                summary.SupportedEntityCount != _supportedEntityCount ||
                summary.UnsupportedEntityCount != _unsupportedEntityCount ||
                summary.MissingSourceRefCount != missingSourceRefCount ||
                summary.Bounds != _document!.Bounds)
            {
                throw Protocol(
                    "CAD IR summary does not match the streamed records and document bounds.");
            }
        }

        private void EnsureRecordHeaderPhase(string recordType)
        {
            EnsureDocumentWritten();
            EnsureWritable();
            if (_entitiesStarted)
            {
                throw Protocol(
                    $"CAD {recordType} records must be written before entity records.");
            }
        }

        private void EnsureDocumentWritten()
        {
            if (_document is null)
                throw Protocol("The CAD IR document must be written first.");
        }

        private void EnsureWritable()
        {
            if (_completed)
                throw Protocol("The CAD IR sink was already completed.");
            var violation = Volatile.Read(ref _violation);
            if (violation is not null)
            {
                throw Violation(
                    ProtocolViolation,
                    "The CAD IR sink cannot continue after a protocol failure.",
                    violation);
            }
        }

        private void BeginOperation()
        {
            if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
            {
                var violation = Protocol(
                    "Concurrent CAD IR sink operations are not allowed.");
                RecordViolation(violation);
                throw violation;
            }
        }

        private void EndOperation() =>
            Volatile.Write(ref _operationInProgress, 0);

        private void RecordViolation(Exception exception) =>
            Interlocked.CompareExchange(ref _violation, exception, null);

        private static InvalidDataException Protocol(
            string message,
            Exception? innerException = null) =>
            Violation(ProtocolViolation, message, innerException);
    }

    private sealed class ReadOnlySourceStream(Stream inner) : Stream
    {
        private int _writeAttempted;

        public bool WriteAttempted => Volatile.Read(ref _writeAttempted) != 0;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) =>
            inner.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override int ReadByte() => inner.ReadByte();

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => ThrowWriteAttempt();

        public override void Write(byte[] buffer, int offset, int count) =>
            ThrowWriteAttempt();

        public override void Write(ReadOnlySpan<byte> buffer) =>
            ThrowWriteAttempt();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(WriteAttempt());

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            Task.FromException(WriteAttempt());

        public override void WriteByte(byte value) => ThrowWriteAttempt();

        protected override void Dispose(bool disposing)
        {
            // The converter does not own the caller-provided source stream.
        }

        private void ThrowWriteAttempt() => throw WriteAttempt();

        private InvalidDataException WriteAttempt()
        {
            Interlocked.Exchange(ref _writeAttempted, 1);
            return Violation(
                SourceWriteAttempt,
                "The converter attempted to modify the read-only CAD source stream.");
        }
    }
}
