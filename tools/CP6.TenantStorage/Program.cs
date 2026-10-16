using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Persistence.Tenancy;
using Npgsql;

// No arbitrary connection string, default business connection, password argument, or startup hook.
if (!args.SequenceEqual(["provision", "--stdin"]) || !Console.IsInputRedirected)
{
    Console.Error.WriteLine("Usage: CP6.TenantStorage provision --stdin (one bounded JSON document on redirected standard input).");
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
catch (TenantProvisioningException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch
{
    Console.Error.WriteLine("Tenant storage provisioning rejected: input-or-execution-rejected. Retry uncertain execution with the same operation and input.");
    return 1;
}

static bool Unique(JsonElement element) => element.ValueKind switch
{
    JsonValueKind.Object => element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count()
        == element.EnumerateObject().Count() && element.EnumerateObject().All(p => Unique(p.Value)),
    JsonValueKind.Array => element.EnumerateArray().All(Unique),
    _ => true
};

internal sealed record ProvisioningInput(TenantProvisioningRequest Request,string AdminUsername,string AdminPassword,string SslMode="VerifyFull");
