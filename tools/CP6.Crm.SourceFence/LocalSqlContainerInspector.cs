using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace CP6.Crm.SourceFence;

public sealed record LocalSqlContainerBinding(string EnginePipeName, string EngineId, string ContainerId,
    string ImageSha256, string ContainerName, string HostName, string StartedAtUtc, int SqlPort, int HostPort,
    string PublishedBindingsSha256, string MountsSha256, string RestartPolicySha256, string NetworkMode)
{
    public string Format => "CP6.C04A.LocalSqlContainer.v1";
}

/// <summary>Read-only local Docker transport proof; never stops containers or changes their policy.</summary>
public static class LocalSqlContainerInspector
{
    private const string Api = "/v1.47";
    private static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    private static bool HashValid(string? value) => value is { Length: 64 } && value.All(c => c is >= 'a' and <= 'f' or >= '0' and <= '9');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static async Task<LocalSqlContainerBinding> InspectAsync(string pipe, string containerId, int hostPort,
        int sqlPort = 1433, CancellationToken token = default)
    {
        try
        {
            ValidateSelector(pipe, containerId, hostPort, sqlPort);
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false, AllowAutoRedirect = false,
                ConnectCallback = async (_, cancellation) =>
                {
                    var stream = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try { await stream.ConnectAsync(5000, cancellation); return stream; }
                    catch { await stream.DisposeAsync(); throw; }
                }
            };
            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromSeconds(10) };
            var engine = await ReadAsync(client, Api + "/info", info =>
            {
                if (info.GetProperty("OSType").GetString() != "linux") Fail("C04A_CONTAINER_ENGINE_IDENTITY");
                return info.GetProperty("ID").GetString() ?? throw new SourceFenceException("C04A_CONTAINER_ENGINE_IDENTITY");
            }, token);
            if (engine.Length is < 1 or > 128 || engine.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or ':' or '_')))
                Fail("C04A_CONTAINER_ENGINE_IDENTITY");
            return await ReadAsync(client, Api + "/containers/" + containerId + "/json", input =>
            {
                var state = input.GetProperty("State");
                if (input.GetProperty("Id").GetString() != containerId || state.GetProperty("Status").GetString() != "running"
                    || !state.GetProperty("Running").GetBoolean() || state.GetProperty("Paused").GetBoolean()
                    || state.GetProperty("Restarting").GetBoolean() || state.GetProperty("Dead").GetBoolean())
                    Fail("C04A_CONTAINER_NOT_RUNNING");
                var ports = input.GetProperty("NetworkSettings").GetProperty("Ports");
                if (!ports.TryGetProperty(sqlPort + "/tcp", out var bindings) || bindings.ValueKind != JsonValueKind.Array)
                    Fail("C04A_CONTAINER_PORT_BINDING");
                var local = bindings.EnumerateArray().Where(row => row.GetProperty("HostIp").GetString() is "127.0.0.1" or "0.0.0.0")
                    .Where(row => row.GetProperty("HostPort").GetString() == hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (local.Length != 1) Fail("C04A_CONTAINER_PORT_BINDING");
                var configuration = input.GetProperty("Config");
                var host = input.GetProperty("HostConfig");
                var result = new LocalSqlContainerBinding(pipe, engine, containerId, input.GetProperty("Image").GetString()!,
                    input.GetProperty("Name").GetString()!, configuration.GetProperty("Hostname").GetString()!, state.GetProperty("StartedAt").GetString()!,
                    sqlPort, hostPort, Hash(bindings.GetRawText()), Hash(input.GetProperty("Mounts").GetRawText()),
                    Hash(host.GetProperty("RestartPolicy").GetRawText()), host.GetProperty("NetworkMode").GetString()!);
                ValidateBinding(result);
                return result;
            }, token);
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException(token.IsCancellationRequested ? "C04A_CANCELLED" : "C04A_CONTAINER_UNAVAILABLE"); }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or TimeoutException)
        { throw new SourceFenceException("C04A_CONTAINER_UNAVAILABLE"); }
    }

    public static async Task VerifyAsync(LocalSqlContainerBinding expected, CancellationToken token = default)
    {
        ValidateBinding(expected);
        var actual = await InspectAsync(expected.EnginePipeName, expected.ContainerId, expected.HostPort, expected.SqlPort, token);
        if (actual != expected) Fail("C04A_CONTAINER_BINDING_CHANGED");
    }

    internal static void ValidateConnection(LocalSqlContainerBinding expected, SqlConnectionStringBuilder connection)
    {
        ValidateBinding(expected);
        var source = connection.DataSource;
        if (source.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) source = source[4..];
        if (source != "127.0.0.1," + expected.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || connection.IntegratedSecurity || connection.UserInstance || connection.MultiSubnetFailover)
            Fail("C04A_CONTAINER_SQL_ENDPOINT");
    }

    private static void ValidateSelector(string pipe, string containerId, int hostPort, int sqlPort)
    {
        if (!OperatingSystem.IsWindows() || pipe is not ("dockerDesktopLinuxEngine" or "docker_engine")
            || !HashValid(containerId) || hostPort is < 1 or > 65535 || sqlPort is < 1 or > 65535)
            Fail("C04A_CONTAINER_INVALID_OPTIONS");
    }

    private static void ValidateBinding(LocalSqlContainerBinding binding)
    {
        if (binding is null) Fail("C04A_CONTAINER_INVALID_OPTIONS");
        ValidateSelector(binding.EnginePipeName, binding.ContainerId, binding.HostPort, binding.SqlPort);
        if (string.IsNullOrWhiteSpace(binding.EngineId) || binding.EngineId.Length > 128
            || binding.ImageSha256 is not { Length: 71 } || !binding.ImageSha256.StartsWith("sha256:", StringComparison.Ordinal) || !HashValid(binding.ImageSha256[7..])
            || string.IsNullOrWhiteSpace(binding.ContainerName) || binding.ContainerName.Length > 256 || !binding.ContainerName.StartsWith("/", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(binding.HostName) || binding.HostName.Length > 128
            || !DateTimeOffset.TryParse(binding.StartedAtUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var started) || started.Offset != TimeSpan.Zero
            || !HashValid(binding.PublishedBindingsSha256) || !HashValid(binding.MountsSha256) || !HashValid(binding.RestartPolicySha256)
            || string.IsNullOrWhiteSpace(binding.NetworkMode) || binding.NetworkMode is "host" or "none" || binding.NetworkMode.StartsWith("container:", StringComparison.Ordinal))
            Fail("C04A_CONTAINER_INVALID_OPTIONS");
    }

    public static async Task<LocalSqlContainerBinding> ReadBindingAsync(string path, string expectedFileSha256, CancellationToken token = default)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !HashValid(expectedFileSha256)) Fail("C04A_CONTAINER_FILE_MISMATCH");
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length is < 2 or > 16384) Fail("C04A_CONTAINER_FILE_MISMATCH");
            var bytes = new byte[(int)file.Length];
            await file.ReadExactlyAsync(bytes, token);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expectedFileSha256) Fail("C04A_CONTAINER_FILE_MISMATCH");
            using var document = JsonDocument.Parse(bytes);
            return ReadBindingElement(document.RootElement);
        }
        catch (SourceFenceException) { throw; }
        catch (OperationCanceledException) { throw new SourceFenceException("C04A_CANCELLED"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException)
        { throw new SourceFenceException("C04A_CONTAINER_FILE_INVALID"); }
    }

    internal static LocalSqlContainerBinding ReadBindingElement(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) Fail("C04A_CONTAINER_FILE_INVALID");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!names.Add(property.Name)) Fail("C04A_CONTAINER_FILE_INVALID");
        if (!root.TryGetProperty("Format", out var format) || format.ValueKind != JsonValueKind.String
            || format.GetString() != "CP6.C04A.LocalSqlContainer.v1") Fail("C04A_CONTAINER_FILE_INVALID");
        var binding = root.Deserialize<LocalSqlContainerBinding>(Strict) ?? throw new SourceFenceException("C04A_CONTAINER_FILE_INVALID");
        ValidateBinding(binding);
        return binding;
    }

    // Container JSON can contain environment secrets. Keep it in bounded memory,
    // return only selected identity fields/hashes, and never expose server errors.
    private static async Task<T> ReadAsync<T>(HttpClient client, string path, Func<JsonElement, T> select, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        token = timeout.Token;
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) Fail("C04A_CONTAINER_UNAVAILABLE");
        var bytes = new byte[2 * 1024 * 1024 + 1];
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count == bytes.Length) Fail("C04A_CONTAINER_UNAVAILABLE");
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            return select(document.RootElement);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code) => throw new SourceFenceException(code);
}
