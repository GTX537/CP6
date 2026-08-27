using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.CadWorker.AutoCadCandidate;
using CP6.Space.Contracts;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var listenUrl = RequiredEnvironment("CP6_SPACE_CAD_LISTEN_URL");
if (!Uri.TryCreate(listenUrl, UriKind.Absolute, out var listen) ||
    listen.Scheme != Uri.UriSchemeHttps ||
    !string.IsNullOrEmpty(listen.UserInfo) ||
    !string.IsNullOrEmpty(listen.Query) ||
    !string.IsNullOrEmpty(listen.Fragment) ||
    !listen.AbsolutePath.Equals("/", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "CP6_SPACE_CAD_LISTEN_URL must be an absolute HTTPS origin.");
}
var clientCertificateSha256 = NormalizeSha256(
    RequiredEnvironment("CP6_SPACE_CAD_CLIENT_CERT_SHA256"));
var coreConsolePath = RequiredEnvironment("CP6_SPACE_CAD_ACCORECONSOLE_PATH");
var workRoot = RequiredEnvironment("CP6_SPACE_CAD_WORK_ROOT");
var timeoutSeconds = IntegerEnvironment(
    "CP6_SPACE_CAD_CONVERSION_TIMEOUT_SECONDS",
    300,
    1,
    1_800);
var maximumConcurrency = IntegerEnvironment(
    "CP6_SPACE_CAD_MAX_CONCURRENCY",
    1,
    1,
    4);
var service = new AutoCadCandidateConversionService(
    coreConsolePath,
    workRoot,
    TimeSpan.FromSeconds(timeoutSeconds),
    maximumConcurrency);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(listen.AbsoluteUri);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = SpaceCadWorkerProtocolVersions.MaximumSourceBytes;
    options.ConfigureHttpsDefaults(https =>
    {
        https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        https.ClientCertificateValidation = (certificate, _, errors) =>
            errors == SslPolicyErrors.None &&
            FixedTimeSha256Equals(certificate.RawData, clientCertificateSha256);
    });
});
builder.Services.AddSingleton(service);
var app = builder.Build();

app.MapGet("/health/live", (AutoCadCandidateConversionService worker) =>
    Results.Json(new
    {
        status = "healthy",
        protocolVersion = SpaceCadWorkerProtocolVersions.SchemaVersion,
        providerKey = worker.ProviderKey,
        providerVersion = worker.ProviderVersion,
        supportsDwg = true,
        supportsDxf = false,
    }));

app.MapPost(
    "/v1/conversions",
    async (HttpContext context, AutoCadCandidateConversionService worker) =>
    {
        try
        {
            if (!string.Equals(
                    MediaTypeHeaderValue.Parse(context.Request.ContentType ?? string.Empty)
                        .MediaType,
                    "application/vnd.cp6.space.cad-source",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status415UnsupportedMediaType,
                    title: "Unsupported CAD Worker content type.");
            }
            var request = Request(context.Request);
            var response = await worker.ConvertAsync(
                request,
                context.Request.Body,
                context.RequestAborted);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                response,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (bytes.LongLength > SpaceCadWorkerProtocolVersions.MaximumResponseBytes)
                throw new InvalidDataException("The CAD IR response exceeds the Worker limit.");
            return Results.Bytes(
                bytes,
                "application/vnd.cp6.space.cad-worker-response+json");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException or FormatException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The CAD Worker request was rejected.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or TimeoutException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The CAD Worker conversion is unavailable.");
        }
    });

await app.RunAsync();

static SpaceCadWorkerConversionRequestV1 Request(HttpRequest request)
{
    if (!int.TryParse(RequiredHeader(request, "X-CP6-Cad-Schema"), out var schema) ||
        !Guid.TryParse(RequiredHeader(request, "X-CP6-Cad-Attempt"), out var attemptId) ||
        !Enum.TryParse<SpaceCadSourceFormat>(
            RequiredHeader(request, "X-CP6-Cad-Source-Format"),
            ignoreCase: false,
            out var format))
    {
        throw new InvalidDataException("The CAD Worker request headers are invalid.");
    }
    var value = new SpaceCadWorkerConversionRequestV1(
        schema,
        attemptId,
        RequiredHeader(request, "X-CP6-Cad-Source-Sha256"),
        format,
        RequiredHeader(request, "X-CP6-Cad-Provider-Key"),
        RequiredHeader(request, "X-CP6-Cad-Provider-Version"));
    SpaceCadWorkerProtocol.ValidateRequest(value);
    return value;
}

static string RequiredHeader(HttpRequest request, string name)
{
    var values = request.Headers[name];
    if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
        throw new InvalidDataException($"The required CAD Worker header '{name}' is missing.");
    return values[0]!;
}

static string RequiredEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name)?.Trim();
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"The required setting '{name}' is missing.");
    return value;
}

static int IntegerEnvironment(string name, int fallback, int minimum, int maximum)
{
    var raw = Environment.GetEnvironmentVariable(name);
    var value = string.IsNullOrWhiteSpace(raw) ? fallback : int.Parse(raw);
    if (value < minimum || value > maximum)
        throw new InvalidOperationException($"The setting '{name}' is outside its allowed range.");
    return value;
}

static string NormalizeSha256(string value)
{
    var normalized = value.Trim().ToLowerInvariant();
    if (normalized.Length != 64 ||
        !normalized.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f'))
    {
        throw new InvalidOperationException("A valid certificate SHA-256 is required.");
    }
    return normalized;
}

static bool FixedTimeSha256Equals(byte[] bytes, string expected)
{
    var actual = SHA256.HashData(bytes);
    return CryptographicOperations.FixedTimeEquals(
        actual,
        Convert.FromHexString(expected));
}
