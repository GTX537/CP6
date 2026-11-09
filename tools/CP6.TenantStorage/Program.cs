using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Persistence.Tenancy;
using Npgsql;

// No arbitrary connection string, default business connection, password argument, or startup hook.
var serviceToken = args.SequenceEqual(["install-service-token-v1", "--stdin"]);
var revocation = args.SequenceEqual(["install-service-revocation-v1", "--stdin"]);
var tenantControl = args.SequenceEqual(["service-token-tenant-directory-control-v1", "--stdin"]);
var directoryInstall = args.SequenceEqual(["install-identity-directory-v2", "--stdin"]);
var directoryInspect = args.SequenceEqual(["inspect-identity-directory-v2", "--stdin"]);
var directoryControl = directoryInstall || directoryInspect;
var stockInstallContract = args switch
{
    ["install-stock-read-v1", "--stdin"] => StockReadStorageV1.ModelVersion,
    ["install-stock-write-fence-v1", "--stdin"] => StockWriteAuthorityFenceV1.Contract,
    ["install-stock-original-journal-v1", "--stdin"] => StockOriginalJournalStorageV1.Contract,
    _ => null
};
var controlExecutionStarted = false;
if ((!args.SequenceEqual(["provision", "--stdin"]) && !revocation && !serviceToken && !tenantControl && !directoryControl && stockInstallContract is null) || !Console.IsInputRedirected)
{
    Console.Error.WriteLine("Usage: CP6.TenantStorage provision|install-service-revocation-v1|install-service-token-v1|service-token-tenant-directory-control-v1|install-identity-directory-v2|inspect-identity-directory-v2|install-stock-read-v1|install-stock-write-fence-v1|install-stock-original-journal-v1 --stdin (one bounded JSON document on redirected standard input).");
    return 2;
}
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var buffer = new char[16385];
    var size = 0;
    while (size < buffer.Length)
    {
        var read = await Console.In.ReadAsync(buffer.AsMemory(size),timeout.Token);
        if (read == 0) break;
        size += read;
    }
    if (size == buffer.Length) throw new TenantProvisioningException("input-rejected");
    using var document = JsonDocument.Parse(buffer.AsMemory(0,size));
    if (document.RootElement.ValueKind != JsonValueKind.Object || !Unique(document.RootElement))
        throw new TenantProvisioningException("input-rejected");
    if (stockInstallContract is not null)
    {
        var stockRequest = document.Deserialize<StockInstallInput>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new TenantProvisioningException("input-rejected");
        var stockReadInstall = stockInstallContract == StockReadStorageV1.ModelVersion;
        var readInstallationId = stockRequest.ReadInstallationId.GetValueOrDefault();
        if (!System.Net.IPAddress.TryParse(stockRequest.Server, out var ip) || ip.ToString() != stockRequest.Server ||
            stockRequest.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(stockRequest.Database) || stockRequest.DatabaseOid == 0 ||
            string.IsNullOrWhiteSpace(stockRequest.SystemIdentifier) || string.IsNullOrWhiteSpace(stockRequest.AdminUsername) ||
            string.IsNullOrEmpty(stockRequest.AdminPassword) || stockRequest.Tenants is not { Length: >= 1 and <= 128 } ||
            (!stockReadInstall && readInstallationId == Guid.Empty) ||
            (stockReadInstall && document.RootElement.TryGetProperty(nameof(StockInstallInput.ReadInstallationId), out _)) ||
            !Enum.TryParse<SslMode>(stockRequest.SslMode, out var sslMode) || stockRequest.SslMode != sslMode.ToString() ||
            !(sslMode == SslMode.VerifyFull || sslMode == SslMode.Disable && System.Net.IPAddress.IsLoopback(ip)))
            throw new TenantProvisioningException("input-rejected");
        await using var control = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = stockRequest.Server, Port = stockRequest.Port, Database = stockRequest.Database, Username = stockRequest.AdminUsername,
            Password = stockRequest.AdminPassword, SslMode = sslMode, Pooling = false, Enlist = false,
            SearchPath = "pg_catalog", Options = " ", IncludeErrorDetail = false, Timeout = 10, CommandTimeout = 65,
            ApplicationName = stockInstallContract switch
            {
                StockReadStorageV1.ModelVersion => "CP6.TenantStorage.StockReadV1",
                StockWriteAuthorityFenceV1.Contract => "CP6.TenantStorage.StockWriteFenceV1",
                _ => "CP6.TenantStorage.StockOriginalJournalV1"
            }
        }.ConnectionString);
        await control.OpenAsync(timeout.Token);
        await using var tx = await control.BeginTransactionAsync(timeout.Token);
        await using var target = new NpgsqlCommand("""
            SELECT d.oid=@oid AND s.system_identifier::text=@system
              AND pg_catalog.host(pg_catalog.inet_server_addr())=@server AND pg_catalog.inet_server_port()=@port
              AND NOT pg_catalog.pg_is_in_recovery()
            FROM pg_catalog.pg_database d CROSS JOIN pg_catalog.pg_control_system() s
            WHERE d.datname=pg_catalog.current_database() AND d.datname=@database
            """, control, tx);
        target.Parameters.AddWithValue("oid", NpgsqlTypes.NpgsqlDbType.Oid, stockRequest.DatabaseOid);
        target.Parameters.AddWithValue("system", stockRequest.SystemIdentifier);
        target.Parameters.AddWithValue("server", stockRequest.Server);
        target.Parameters.AddWithValue("port", stockRequest.Port);
        target.Parameters.AddWithValue("database", stockRequest.Database);
        if (await target.ExecuteScalarAsync(timeout.Token) is not true) throw new TenantProvisioningException("target-rejected");
        object receipt;
        if (stockReadInstall)
        {
            var id = await StockReadStorageV1.InstallAsync(control, tx, stockRequest.Tenants, timeout.Token);
            receipt = new { Outcome = "INSTALLED", InstallationId = id, stockRequest.DatabaseOid, stockRequest.SystemIdentifier };
        }
        else
        {
            if (stockInstallContract == StockWriteAuthorityFenceV1.Contract)
                await StockWriteAuthorityFenceV1.InstallAsync(control, tx, readInstallationId, stockRequest.Tenants, timeout.Token);
            else
                await StockOriginalJournalStorageV1.InstallAsync(control, tx, readInstallationId, stockRequest.Tenants, timeout.Token);
            receipt = new { Outcome = "INSTALLED", ReadInstallationId = readInstallationId, stockRequest.DatabaseOid, stockRequest.SystemIdentifier };
        }
        controlExecutionStarted = true; // After this point an unacknowledged commit/output requires inspection.
        await tx.CommitAsync(timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(receipt));
        return 0;
    }
    if (directoryControl)
    {
        var command = document.Deserialize<DirectoryInput>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new TenantProvisioningException("input-rejected");
        if (command.Request is not { } target || string.IsNullOrWhiteSpace(command.AdminUsername) || string.IsNullOrEmpty(command.AdminPassword)
            || !Enum.TryParse<SslMode>(command.SslMode, false, out var directorySsl) || directorySsl is not (SslMode.VerifyFull or SslMode.Disable))
            throw new TenantProvisioningException("input-rejected");
        await using var control = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = target.Server, Port = target.Port, Database = target.Database, Username = command.AdminUsername,
            Password = command.AdminPassword, SslMode = directorySsl, Pooling = false, Enlist = false,
            SearchPath = "pg_catalog", Options = " ", IncludeErrorDetail = false, Timeout = 10, CommandTimeout = 65,
            ApplicationName = "CP6.TenantStorage.IdentityDirectoryV2"
        }.ConnectionString);
        controlExecutionStarted = true;
        var installation = new IdentityDirectoryInstallationV2();
        var directoryResult = directoryInstall ? await installation.InstallAsync(control, target, timeout.Token)
            : await installation.InspectAsync(control, target, timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(directoryResult)); return 0;
    }
    if (tenantControl)
    {
        RequireLosslessTenantDeadlines(document.RootElement);
        var command = document.Deserialize<TenantControlInput>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new TenantProvisioningException("input-rejected");
        if (command.Request?.Installation is not { } target || string.IsNullOrWhiteSpace(command.AdminUsername)
            || string.IsNullOrEmpty(command.AdminPassword) || !Enum.TryParse<SslMode>(command.SslMode, false, out var controlSsl)
            || controlSsl is not (SslMode.VerifyFull or SslMode.Disable)) throw new TenantProvisioningException("input-rejected");
        await using var control = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = target.Server, Port = target.Port, Database = target.Database,
            Username = command.AdminUsername, Password = command.AdminPassword, SslMode = controlSsl,
            Pooling = false, Enlist = false, SearchPath = "pg_catalog", Options = " ", IncludeErrorDetail = false,
            Timeout = 10, CommandTimeout = 65, ApplicationName = "CP6.TenantStorage.TenantDirectoryControlV1"
        }.ConnectionString);
        controlExecutionStarted = true;
        var controlled = await new ServiceTokenTenantDirectoryControlV1().ExecuteAsync(control, command.Request,
            CP6.Persistence.PostgreSql.Tenancy.ServiceTokenBaselineV1.GenerateSql(), timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(controlled));
        return controlled.Outcome is "CURRENT" or "MISSING" or "INITIALIZED" or "UPDATED" ? 0 : 3;
    }
    if (revocation || serviceToken)
    {
        var tokenInput = serviceToken ? document.Deserialize<ServiceTokenInput>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) : null;
        var install = serviceToken
            ? tokenInput?.Request is { } r ? new RevocationInput(new(r.InstallationId,r.Server,r.Port,r.Database,
                r.DatabaseOid,r.SystemIdentifier,"",r.Tenants),tokenInput.AdminUsername,tokenInput.AdminPassword,tokenInput.SslMode)
                : throw new TenantProvisioningException("input-rejected")
            : document.Deserialize<RevocationInput>(new JsonSerializerOptions
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new TenantProvisioningException("input-rejected");
        if (install.Request is null || string.IsNullOrWhiteSpace(install.AdminUsername) || string.IsNullOrEmpty(install.AdminPassword)
            || !Enum.TryParse<SslMode>(install.SslMode, false, out var installSsl)
            || installSsl is not (SslMode.VerifyFull or SslMode.Disable)) throw new TenantProvisioningException("input-rejected");
        await using var control = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = install.Request.Server, Port = install.Request.Port, Database = install.Request.Database,
            Username = install.AdminUsername, Password = install.AdminPassword, SslMode = installSsl,
            Pooling = false, Enlist = false, SearchPath = "pg_catalog", Options = " ", IncludeErrorDetail = false,
            Timeout = 10, CommandTimeout = 65, ApplicationName = "CP6.TenantStorage.ServiceRevocation"
        }.ConnectionString);
        var installed = serviceToken
            ? await ServiceRevocationInstallation.InstallServiceTokenAsync(control, tokenInput!.Request,
                CP6.Persistence.PostgreSql.Tenancy.ServiceTokenBaselineV1.GenerateSql(), timeout.Token)
            : await ServiceRevocationInstallation.InstallAsync(control, install.Request,
                CP6.Persistence.PostgreSql.Tenancy.ServiceRevocationBaselineV1.GenerateSql(), timeout.Token);
        Console.WriteLine(JsonSerializer.Serialize(installed));
        return 0;
    }
    var input = document.Deserialize<ProvisioningInput>(new JsonSerializerOptions
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new TenantProvisioningException("input-rejected");
    if (input.Request is null || string.IsNullOrWhiteSpace(input.AdminUsername) || string.IsNullOrEmpty(input.AdminPassword)
        || input.AdminUsername == input.Request.RuntimeLogin || !Enum.TryParse<SslMode>(input.SslMode,false,out var ssl)
        || ssl is not (SslMode.VerifyFull or SslMode.Disable)) throw new TenantProvisioningException("input-rejected");
    input.Request.Validate();
    await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder
    {
        Host=input.Request.Host,Port=input.Request.Port,Database=input.Request.Database,
        Username=input.AdminUsername,Password=input.AdminPassword,SslMode=ssl,
        Pooling=false,Enlist=false,SearchPath="pg_catalog",Options=" ",IncludeErrorDetail=false,
        Timeout=10,CommandTimeout=65,ApplicationName="CP6.TenantStorage"
    }.ConnectionString);
    var result = await new TenantStorageProvisioner().ProvisionAsync(input.Request,connection,timeout.Token);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return 0;
}
catch (ServiceTokenTenantControlException error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Contract = ServiceTokenTenantDirectoryControlV1.Contract, error.Outcome, Code = error.Message }));
    return error.Outcome == "UNKNOWN" ? 4 : 1;
}
catch (IdentityDirectoryInstallationException error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { Contract = IdentityDirectoryInstallationV2.Contract, error.Outcome, Code = error.Message }));
    return error.Outcome == "UNKNOWN" ? 4 : 1;
}
catch (TenantProvisioningException error) when (!tenantControl && !directoryControl && stockInstallContract is null)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch
{
    if (stockInstallContract is not null)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { Contract = stockInstallContract,
            Outcome = controlExecutionStarted ? "UNKNOWN" : "NOT_APPLIED" }));
        return controlExecutionStarted ? 4 : 1;
    }
    if (directoryControl)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { Contract = IdentityDirectoryInstallationV2.Contract,
            Outcome = controlExecutionStarted ? directoryInstall ? "UNKNOWN" : "UNAVAILABLE" : "NOT_APPLIED",
            Code = controlExecutionStarted ? "execution-or-output-lost" : "input-rejected" }));
        return controlExecutionStarted && directoryInstall ? 4 : 1;
    }
    if (tenantControl)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new { Contract = ServiceTokenTenantDirectoryControlV1.Contract,
            Outcome = controlExecutionStarted ? "UNKNOWN" : "NOT_APPLIED", Code = controlExecutionStarted ? "execution-or-output-lost" : "input-rejected" }));
        return controlExecutionStarted ? 4 : 1;
    }
    Console.Error.WriteLine("Tenant storage provisioning rejected: input-or-execution-rejected. Retry uncertain execution with the same operation and input.");
    return 1;
}

static void RequireLosslessTenantDeadlines(JsonElement root)
{
    if (!root.TryGetProperty("Request", out var request) || request.ValueKind != JsonValueKind.Object) return;
    foreach (var name in new[] { "Expected", "Desired" })
    {
        if (!request.TryGetProperty(name, out var eligibility) || eligibility.ValueKind != JsonValueKind.Object
            || !eligibility.TryGetProperty("ExpireDate", out var expiry) || expiry.ValueKind != JsonValueKind.String) continue;
        // Decode JSON escapes, but inspect every original fractional digit before DateTime truncates after digit seven.
        // The existing deserializer and control validation retain responsibility for syntax, null, kind and finite range.
        var text = expiry.GetString()!;
        var dot = text.IndexOf('.');
        if (dot < 0) continue;
        for (var i = dot + 7; i < text.Length && text[i] is >= '0' and <= '9'; i++)
            if (text[i] != '0') throw new TenantProvisioningException("input-rejected");
    }
}

static bool Unique(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.Object => element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count()
        == element.EnumerateObject().Count() && element.EnumerateObject().All(p => Unique(p.Value)),
    JsonValueKind.Array => element.EnumerateArray().All(Unique),
    _ => true
};

internal sealed record ProvisioningInput(TenantProvisioningRequest Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");
internal sealed record RevocationInput(ServiceRevocationInstallRequest Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");

internal sealed record ServiceTokenInput(ServiceTokenInstallRequest Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");
internal sealed record TenantControlInput(ServiceTokenTenantControlRequestV1 Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");
internal sealed record StockInstallInput(string Server, int Port, string Database, uint DatabaseOid,
    string SystemIdentifier, string AdminUsername, string AdminPassword, TenantLoginV1[] Tenants, string SslMode = "VerifyFull",
    Guid? ReadInstallationId = null);
internal sealed record DirectoryInput(IdentityDirectoryInstallRequestV2 Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");
