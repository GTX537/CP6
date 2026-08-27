using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.Infrastructure;

public interface ISpaceCadRemoteWorkerClient
{
    Task<SpaceCadIrPackageV1> ConvertAsync(
        SpaceCadWorkerConversionRequestV1 request,
        Stream source,
        CancellationToken cancellationToken = default);
}

public sealed class HttpSpaceCadRemoteWorkerClient :
    ISpaceCadRemoteWorkerClient,
    IDisposable
{
    private const string RequestContentType =
        "application/vnd.cp6.space.cad-source";
    private const string ResponseContentType =
        "application/vnd.cp6.space.cad-worker-response+json";

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly HttpClient _client;
    private readonly SpaceCadRemoteWorkerOptions _options;
    private readonly IDisposable? _ownedResource;

    public HttpSpaceCadRemoteWorkerClient(
        HttpClient client,
        SpaceCadRemoteWorkerOptions options,
        IDisposable? ownedResource = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownedResource = ownedResource;
    }

    public async Task<SpaceCadIrPackageV1> ConvertAsync(
        SpaceCadWorkerConversionRequestV1 request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        SpaceCadWorkerProtocol.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));
        if ((request.SourceFormat == SpaceCadSourceFormat.Dwg && !_options.SupportsDwg) ||
            (request.SourceFormat == SpaceCadSourceFormat.Dxf && !_options.SupportsDxf))
        {
            throw new InvalidDataException(
                "The remote CAD Worker does not support the requested source format.");
        }

        using var content = new ValidatedCadSourceContent(
            source,
            request.SourceSha256,
            _options.MaximumSourceBytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(RequestContentType);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_options.Endpoint, UriKind.Absolute), "v1/conversions"))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = content,
        };
        AddHeader(message, "X-CP6-Cad-Schema", request.SchemaVersion.ToString());
        AddHeader(message, "X-CP6-Cad-Attempt", request.AttemptId.ToString("D"));
        AddHeader(message, "X-CP6-Cad-Source-Sha256", request.SourceSha256);
        AddHeader(message, "X-CP6-Cad-Source-Format", request.SourceFormat.ToString());
        AddHeader(message, "X-CP6-Cad-Provider-Key", request.ProviderKey);
        AddHeader(message, "X-CP6-Cad-Provider-Version", request.ProviderVersion);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await _client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The isolated CAD Worker exceeded its wall-clock limit.",
                exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.BadRequest or
                    HttpStatusCode.RequestEntityTooLarge or
                    HttpStatusCode.UnprocessableEntity)
                {
                    throw new InvalidDataException(
                        "The isolated CAD Worker rejected the CAD source.");
                }
                throw new HttpRequestException(
                    "The isolated CAD Worker is unavailable.",
                    inner: null,
                    response.StatusCode);
            }
            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    ResponseContentType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The isolated CAD Worker returned an unsupported content type.");
            }
            if (response.Content.Headers.ContentLength is { } length &&
                length > _options.MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    "The isolated CAD Worker response exceeds the configured limit.");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(
                timeout.Token);
            var bytes = await ReadBoundedAsync(
                responseStream,
                _options.MaximumResponseBytes,
                timeout.Token);
            SpaceCadWorkerConversionResponseV1 workerResponse;
            try
            {
                workerResponse = JsonSerializer.Deserialize<
                                     SpaceCadWorkerConversionResponseV1>(bytes, JsonOptions)
                                 ?? throw new JsonException();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "The isolated CAD Worker response is invalid JSON.",
                    exception);
            }
            SpaceCadWorkerProtocol.ValidateResponse(request, workerResponse);
            return workerResponse.Package;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _ownedResource?.Dispose();
    }

    private static void AddHeader(
        HttpRequestMessage message,
        string name,
        string value)
    {
        if (!message.Headers.TryAddWithoutValidation(name, value))
            throw new InvalidOperationException($"Could not add CAD Worker header '{name}'.");
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The isolated CAD Worker response exceeds the configured limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private sealed class ValidatedCadSourceContent : HttpContent
    {
        private readonly Stream _source;
        private readonly string _expectedSha256;
        private readonly long _maximumBytes;
        private readonly long? _knownLength;

        public ValidatedCadSourceContent(
            Stream source,
            string expectedSha256,
            long maximumBytes)
        {
            _source = source;
            _expectedSha256 = expectedSha256;
            _maximumBytes = maximumBytes;
            if (source.CanSeek)
            {
                _knownLength = checked(source.Length - source.Position);
                if (_knownLength < 0 || _knownLength > maximumBytes)
                {
                    throw new InvalidDataException(
                        "The CAD source exceeds the isolated Worker input limit.");
                }
            }
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await _source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                total = checked(total + read);
                if (total > _maximumBytes)
                {
                    throw new InvalidDataException(
                        "The CAD source exceeds the isolated Worker input limit.");
                }
                hash.AppendData(buffer, 0, read);
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!actual.Equals(_expectedSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The CAD source does not match its server-owned SHA-256.");
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _knownLength.GetValueOrDefault();
            return _knownLength.HasValue;
        }
    }
}
