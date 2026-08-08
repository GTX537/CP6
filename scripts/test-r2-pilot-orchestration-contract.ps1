[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-r2-pilot-$([Guid]::NewGuid().ToString('N'))"
$server = $null
$previousToken = [Environment]::GetEnvironmentVariable(
    "CP6_PILOT_ACCESS_TOKEN",
    "Process"
)

if ($null -eq ("Cp6R2PilotContractServer" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class Cp6R2PilotContractServer : IDisposable
{
    private readonly HttpListener _listener = new HttpListener();
    private readonly Task _loop;
    private int _taskCreateCount;
    private int _taskCancelCount;

    public int FailCreateAt { get; set; }
    public int TaskCreateCount { get { return _taskCreateCount; } }
    public int TaskCancelCount { get { return _taskCancelCount; } }

    public Cp6R2PilotContractServer(string baseUrl)
    {
        _listener.Prefixes.Add(baseUrl.TrimEnd('/') + "/");
        _listener.Start();
        _loop = Task.Run((Action)ListenLoop);
    }

    private void ListenLoop()
    {
        while (_listener.IsListening)
        {
            try
            {
                Handle(_listener.GetContext());
            }
            catch (HttpListenerException)
            {
                if (!_listener.IsListening) return;
                throw;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        string path = request.Url.AbsolutePath;
        string payload;

        if (request.Headers["Authorization"] != "Bearer contract-token")
        {
            response.StatusCode = 401;
            payload = "{\"code\":\"UNAUTHORIZED\"}";
        }
        else if (request.HttpMethod == "GET" &&
            (path == "/health/live" || path == "/health/ready"))
        {
            payload = "{\"status\":\"Healthy\"}";
        }
        else if (request.HttpMethod == "GET" &&
            path == "/api/client/bootstrap")
        {
            payload =
                "{\"apiVersion\":\"2\",\"serverUtc\":\"" +
                DateTimeOffset.UtcNow.ToString("O") +
                "\",\"upgradeRequired\":false}";
        }
        else if (request.HttpMethod == "GET" &&
            path == "/api/v2/admin/wms-features")
        {
            payload =
                "[{\"warehouseCd\":\"PILOT-WH\"," +
                "\"productionMoveEnabled\":true}]";
        }
        else if (request.HttpMethod == "GET" &&
            path == "/api/v2/admin/client-devices")
        {
            payload =
                "{\"items\":[" +
                "{\"deviceId\":\"pilot-rf-01\",\"status\":\"Active\"," +
                "\"warehouseCd\":\"PILOT-WH\",\"areaCd\":\"PILOT-A\"}," +
                "{\"deviceId\":\"pilot-rf-02\",\"status\":\"Active\"," +
                "\"warehouseCd\":\"PILOT-WH\",\"areaCd\":\"PILOT-A\"}]," +
                "\"total\":2,\"page\":1,\"pageSize\":500}";
        }
        else if (request.HttpMethod == "POST" &&
            path == "/api/v2/wms/tasks")
        {
            string body;
            using (StreamReader reader = new StreamReader(
                request.InputStream,
                request.ContentEncoding ?? Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }
            if (!body.Contains("\"sourceType\":\"R2_PILOT\"") ||
                !body.Contains("\"operationId\":"))
            {
                response.StatusCode = 400;
                payload = "{\"code\":\"BAD-PILOT-TASK\"}";
            }
            else
            {
                int taskIndex = Interlocked.Increment(ref _taskCreateCount);
                if (FailCreateAt > 0 && taskIndex == FailCreateAt)
                {
                    response.StatusCode = 409;
                    payload = "{\"code\":\"CONTRACT-CREATE-FAILURE\"}";
                }
                else
                {
                    response.StatusCode = 201;
                    payload =
                        "{\"taskNo\":\"MOV-PILOT-" +
                        taskIndex.ToString("000") +
                        "\",\"status\":0,\"rowVersion\":\"AQ==\"}";
                }
            }
        }
        else if (request.HttpMethod == "POST" &&
            path.StartsWith("/api/v2/wms/tasks/", StringComparison.Ordinal) &&
            path.EndsWith("/cancel", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _taskCancelCount);
            payload =
                "{\"taskNo\":\"MOV-PILOT-CANCELLED\"," +
                "\"status\":6,\"rowVersion\":\"Ag==\"}";
        }
        else
        {
            response.StatusCode = 404;
            payload = "{\"code\":\"NOT-FOUND\"}";
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _loop.Wait(TimeSpan.FromSeconds(5));
    }
}
'@
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    try {
        $port = ([Net.IPEndPoint]$portProbe.LocalEndpoint).Port
    }
    finally {
        $portProbe.Stop()
    }
    $baseUrl = "http://127.0.0.1:$port"
    $server = [Cp6R2PilotContractServer]::new($baseUrl)
    [Environment]::SetEnvironmentVariable(
        "CP6_PILOT_ACCESS_TOKEN",
        "contract-token",
        "Process"
    )

    $readyDirectory = Join-Path $temporaryRoot "ready"
    & (Join-Path $repoRoot "scripts\prepare-r2-pilot.ps1") `
        -BaseUrl $baseUrl `
        -WarehouseCd "PILOT-WH" `
        -AreaCd "PILOT-A" `
        -FromLocationCd "PILOT-SOURCE" `
        -ToLocationCd "PILOT-TARGET" `
        -ProductCd "PILOT-SKU" `
        -Quantity 1 `
        -TaskCount 2 `
        -DeviceIds @("pilot-rf-01", "pilot-rf-02") `
        -OutputDirectory $readyDirectory | Out-Null

    $readyPath = Join-Path $readyDirectory "pilot-input.json"
    $readyText = [IO.File]::ReadAllText($readyPath, [Text.Encoding]::UTF8)
    $ready = $readyText | ConvertFrom-Json
    if ($ready.Status -ne "Ready" -or
        @($ready.Tasks).Count -ne 2 -or
        $server.TaskCreateCount -ne 2) {
        throw "Pilot preparation did not create a complete Ready manifest."
    }
    if ($readyText.Contains("contract-token")) {
        throw "Pilot Ready manifest leaked the access token."
    }

    $server.FailCreateAt = 4
    $failedDirectory = Join-Path $temporaryRoot "failed"
    $partialFailureObserved = $false
    try {
        & (Join-Path $repoRoot "scripts\prepare-r2-pilot.ps1") `
            -BaseUrl $baseUrl `
            -WarehouseCd "PILOT-WH" `
            -AreaCd "PILOT-A" `
            -FromLocationCd "PILOT-SOURCE" `
            -ToLocationCd "PILOT-TARGET" `
            -ProductCd "PILOT-SKU" `
            -Quantity 1 `
            -TaskCount 3 `
            -DeviceIds @("pilot-rf-01") `
            -OutputDirectory $failedDirectory | Out-Null
    }
    catch {
        $partialFailureObserved =
            $_.Exception.Message -match "CONTRACT-CREATE-FAILURE"
    }
    $failedPath = Join-Path $failedDirectory "pilot-input.json"
    $failedText = [IO.File]::ReadAllText($failedPath, [Text.Encoding]::UTF8)
    $failed = $failedText | ConvertFrom-Json
    if (-not $partialFailureObserved -or
        $failed.Status -ne "Failed" -or
        @($failed.Tasks).Count -ne 1 -or
        $server.TaskCancelCount -ne 1) {
        throw "Pilot preparation did not persist and roll back partial failure."
    }
    if ($failedText.Contains("contract-token")) {
        throw "Pilot Failed manifest leaked the access token."
    }

    $createsBeforeInvalidDevice = $server.TaskCreateCount
    $invalidDeviceRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\prepare-r2-pilot.ps1") `
            -BaseUrl $baseUrl `
            -WarehouseCd "PILOT-WH" `
            -AreaCd "PILOT-A" `
            -FromLocationCd "PILOT-SOURCE" `
            -ToLocationCd "PILOT-TARGET" `
            -ProductCd "PILOT-SKU" `
            -TaskCount 1 `
            -DeviceIds @("disabled-rf") `
            -OutputDirectory (Join-Path $temporaryRoot "invalid") | Out-Null
    }
    catch {
        $invalidDeviceRejected = $_.Exception.Message -match "not active"
    }
    if (-not $invalidDeviceRejected -or
        $server.TaskCreateCount -ne $createsBeforeInvalidDevice) {
        throw "Pilot preparation created a task for an inactive device."
    }

    Write-Host "R2 pilot orchestration contract test passed."
}
finally {
    [Environment]::SetEnvironmentVariable(
        "CP6_PILOT_ACCESS_TOKEN",
        $previousToken,
        "Process"
    )
    if ($null -ne $server) {
        $server.Dispose()
    }
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (
        (Test-Path -LiteralPath $resolvedTemporaryRoot) -and
        $resolvedTemporaryRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase
        ) -and
        ([IO.Path]::GetFileName($resolvedTemporaryRoot)).StartsWith(
            "cp6-r2-pilot-",
            [StringComparison]::Ordinal
        )
    ) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
