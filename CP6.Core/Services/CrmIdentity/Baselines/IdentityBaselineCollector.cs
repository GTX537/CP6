using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Core.Services.CrmIdentity.Baselines;

public sealed record IdentityBaselineCollectorOptions(int PageSize = 200, IdentityBaselineLimits? Limits = null,
    TimeSpan? OperationTimeout = null, bool AllowLoopbackHttp = false);

public sealed class IdentityBaselineCollector : IDisposable
{
    private const string Path = "internal/crm/identity/directory/v1/";
    private const int MaximumPageBytes = 1_048_576, MaximumRawBytes = 9 * 1_048_576;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, NumberHandling = JsonNumberHandling.Strict, MaxDepth = 32 };
    private readonly CrmIdentityRuntime _runtime;
    private readonly IdentityBaselineSourceV1 _source;
    private readonly Uri _origin;
    private readonly Func<CancellationToken, ValueTask<string>> _bearer;
    private readonly IdentityBaselineLimits _limits;
    private readonly int _pageSize;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _client;

    public IdentityBaselineCollector(CrmIdentityRuntime runtime, IdentityBaselineSourceV1 source, Uri origin,
        Func<CancellationToken, ValueTask<string>> bearer, IdentityBaselineCollectorOptions? options = null)
        : this(runtime, source, origin, bearer, options, null) { }

    // Test-only transport injection; production construction always owns a handler with redirects,
    // cookies and decompression disabled. No arbitrary caller-configured HttpClient is accepted.
    internal IdentityBaselineCollector(CrmIdentityRuntime runtime, IdentityBaselineSourceV1 source, Uri origin,
        Func<CancellationToken, ValueTask<string>> bearer, IdentityBaselineCollectorOptions? options, HttpMessageHandler? handler)
    {
        options ??= new(); _limits = options.Limits ?? new(); _pageSize = options.PageSize; _timeout = options.OperationTimeout ?? TimeSpan.FromMinutes(10);
        _ = new IdentityBaselineVerifier(runtime, source, "configuration-validation", 0, [], _limits);
        if (origin is null || !origin.IsAbsoluteUri || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0 ||
            origin.AbsolutePath != "/" || (origin.Scheme != Uri.UriSchemeHttps && !(options.AllowLoopbackHttp && origin.Scheme == Uri.UriSchemeHttp &&
                IPAddress.TryParse(origin.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip))) ||
            bearer is null || _pageSize is < 1 or > 200 || _timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromDays(1)) throw Invalid();
        _runtime = runtime; _source = source; _origin = origin; _bearer = bearer;
        _client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None, MaxResponseHeadersLength = 32, ConnectTimeout = TimeSpan.FromSeconds(30) }, disposeHandler: true)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<VerifiedIdentityBaseline> CollectAsync(CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(_timeout);
        var token = deadline.Token; token.ThrowIfCancellationRequested();
        var first = await DirectoryAsync(token).ConfigureAwait(false);
        var verifier = new IdentityBaselineVerifier(_runtime, _source, first.Boundary, first.Count, first.Items, _limits, token);
        foreach (var item in first.Items)
        {
            var raw = await GetAsync<IdentityRawSnapshotV1>("snapshots/" + Uri.EscapeDataString(item.AggregateId), MaximumRawBytes, token).ConfigureAwait(false);
            if (raw.Item != item) throw Invalid();
            verifier.Add(raw, token);
        }
        var last = await DirectoryAsync(token).ConfigureAwait(false);
        return verifier.Complete(last.Boundary, last.Count, last.Items, token);
    }

    private async Task<Directory> DirectoryAsync(CancellationToken ct)
    {
        List<IdentityVersion> items = []; HashSet<string> aggregates = new(StringComparer.Ordinal), cursors = new(StringComparer.Ordinal);
        string? cursor = null, boundary = null; long? count = null;
        do
        {
            var path = "versions?pageSize=" + _pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await GetAsync<IdentityBaselinePageV1>(path, MaximumPageBytes, ct).ConfigureAwait(false);
            if (page.Contract != IdentityBaselineVerifier.Contract || page.Source != _source) throw new IdentityBaselineException("C02_BASELINE_SOURCE_CHANGED");
            if (page.Boundary is not { Length: > 0 and <= 2048 } || page.TotalCount < 0 || page.Items is null || page.Items.Length > _pageSize) throw Invalid();
            if (page.TotalCount > _limits.MaximumItems) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
            boundary ??= page.Boundary; count ??= page.TotalCount;
            if (boundary != page.Boundary || count != page.TotalCount) throw new IdentityBaselineException("C02_BASELINE_BOUNDARY_CHANGED");
            foreach (var item in page.Items)
            {
                ct.ThrowIfCancellationRequested();
                if (item is null || string.IsNullOrEmpty(item.AggregateId) || !aggregates.Add(item.AggregateId)) throw Invalid();
                items.Add(item);
                if (items.Count > count) throw new IdentityBaselineException("C02_BASELINE_INCOMPLETE");
            }
            cursor = page.NextCursor;
            if (cursor is not null && (cursor.Length is 0 or > 4096 || cursor.Any(char.IsControl) ||
                page.Items.Length == 0 || items.Count >= count || !cursors.Add(cursor))) throw Invalid();
            if (cursor is null && items.Count != count) throw new IdentityBaselineException("C02_BASELINE_INCOMPLETE");
        } while (cursor is not null);
        return new(boundary!, count!.Value, items.ToArray());
    }

    private async Task<T> GetAsync<T>(string path, int maximumBytes, CancellationToken ct)
    {
        string bearer;
        try { bearer = await _bearer(ct).AsTask().WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch { throw new IdentityBaselineException("C02_BASELINE_AUTH_UNAVAILABLE"); }
        if (string.IsNullOrEmpty(bearer) || bearer.Length > 16384 || bearer.Any(c => c <= ' ' || c > '~'))
            throw new IdentityBaselineException("C02_BASELINE_AUTH_UNAVAILABLE");
        ct.ThrowIfCancellationRequested(); var uri = new Uri(_origin, Path + path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IdentityBaselineException("C02_BASELINE_HTTP_" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (response.RequestMessage?.RequestUri != uri) throw new IdentityBaselineException("C02_BASELINE_SOURCE_CHANGED");
            var type = response.Content.Headers.ContentType;
            if (!string.Equals(type?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                (type.CharSet is not null && !string.Equals(type.CharSet.Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase)) ||
                response.Content.Headers.ContentEncoding.Count != 0 || response.Headers.CacheControl?.NoStore != true) throw Invalid();
            var length = response.Content.Headers.ContentLength;
            if (length > maximumBytes) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > maximumBytes) throw new IdentityBaselineException("C02_BASELINE_LIMIT_EXCEEDED");
                buffer.Write(chunk, 0, read);
            }
            if (length.HasValue && length != buffer.Length) throw Invalid();
            var bytes = buffer.ToArray(); _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            UniqueProperties(document.RootElement); RequireWireShape(document.RootElement, typeof(T) == typeof(IdentityBaselinePageV1));
            var result = JsonSerializer.Deserialize<T>(bytes, Json);
            ct.ThrowIfCancellationRequested(); return result is null ? throw Invalid() : result;
        }
        catch (HttpRequestException) { throw new IdentityBaselineException("C02_BASELINE_HTTP_UNAVAILABLE"); }
        catch (IOException) { throw new IdentityBaselineException("C02_BASELINE_HTTP_UNAVAILABLE"); }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        { throw Invalid(); }
    }

    private static void RequireWireShape(JsonElement root, bool page)
    {
        Fields(root, page ? ["contract", "source", "boundary", "totalCount", "items", "nextCursor"] : ["contract", "source", "boundary", "item", "payloadBase64"]);
        Fields(root.GetProperty("source"), ["tenantId", "issuer", "region", "contractBundleSha256", "storageIdentitySha256"]);
        if (page)
        {
            if (root.GetProperty("items").ValueKind != JsonValueKind.Array) throw Invalid();
            foreach (var item in root.GetProperty("items").EnumerateArray()) Item(item);
        }
        else Item(root.GetProperty("item"));
    }
    private static void Item(JsonElement item) => Fields(item, ["aggregateId", "eventType", "version", "payloadSha256", "isDeleted"]);
    private static void Fields(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != names.Length || names.Any(n => !element.TryGetProperty(n, out _))) throw Invalid();
    }
    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw Invalid(); UniqueProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) UniqueProperties(item);
    }
    private sealed record Directory(string Boundary, long Count, IdentityVersion[] Items);
    private static IdentityBaselineException Invalid() => new("C02_BASELINE_INVALID");
    public void Dispose() => _client.Dispose();
}
