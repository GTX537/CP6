using System.Text.Json;
using CP6.Client.Api;
using CP6.Client.Core;

// Local regression probe: real API and client libraries, explicitly simulated printer output.
// Authentication is passed only through the child environment and never written to evidence.
var phase = Required("CP6_LABEL_PHASE");
if (phase is not ("lose-completion" or "resume")) throw new InvalidOperationException("Unsupported probe phase.");
var root = Path.GetFullPath(Required("CP6_LABEL_ROOT"));
var identity = new LabelGatewayIdentity(new Uri(Required("CP6_LABEL_ORIGIN")),
    Guid.Parse(Required("CP6_LABEL_TENANT")), Guid.Parse(Required("CP6_LABEL_ACTOR")), Required("CP6_LABEL_DEVICE"));
var options = new ClientOptions
{
    ApiBaseAddress = identity.Source, Platform = "Windows",
    Context = new() { ClientKind = "Windows", DeviceId = identity.DeviceId },
    LabelJournalDirectory = Path.Combine(root, "journal")
};
Directory.CreateDirectory(root);
using var client = new HttpClient(new LoseCommittedCompletionHandler(
    phase == "lose-completion", Required("CP6_LABEL_COOKIE"), Required("CP6_LABEL_CSRF"), root))
    { BaseAddress = identity.Source, Timeout = TimeSpan.FromSeconds(25) };
var store = new FileLabelGatewayJournal(options.LabelJournalDirectory);
var service = new LabelGatewayService(new Clients(client), options, new SimulatedPrinter(root), store, new Context(identity));
LabelGatewayRecoveryException? recovery = null;
try { await service.ProcessOnceAsync(); }
catch (LabelGatewayRecoveryException error) { recovery = error; }
if (phase == "lose-completion" && recovery?.Code != "CompletionRecoveryPending")
    throw new InvalidOperationException("The committed response loss was not preserved as pending recovery.");
if (phase == "resume" && recovery != null) throw recovery;
var files = Directory.GetFiles(options.LabelJournalDirectory, "*.json", SearchOption.AllDirectories);
if (files.Length != 1) throw new InvalidOperationException("Expected one durable job record.");
var entry = JsonSerializer.Deserialize<LabelGatewayJournalEntry>(await File.ReadAllTextAsync(files[0]))!;
if (entry.Stage != (phase == "lose-completion" ? LabelGatewayStage.CompletionPending : LabelGatewayStage.Completed))
    throw new InvalidOperationException("Unexpected durable recovery stage.");
Console.WriteLine(JsonSerializer.Serialize(new
{
    PrintCalls = Counter.Read(root, "print-count.txt"), FailCalls = Counter.Read(root, "fail-count.txt"),
    JournalStage = entry.Stage.ToString(), entry.CompletionOperationId, RecoveryCode = recovery?.Code
}));

static string Required(string key) => Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException("Missing probe configuration: " + key);

sealed class Context(LabelGatewayIdentity identity) : ILabelGatewayContext
{ public LabelGatewayIdentity Capture() => identity; }

sealed class Clients(HttpClient client) : IHttpClientFactory
{ public HttpClient CreateClient(string name) => client; }

sealed class SimulatedPrinter(string root) : ILabelPrinter
{
    public Task PrintAsync(LabelJob job, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Counter.Increment(root, "print-count.txt");
        return Task.CompletedTask;
    }
}

sealed class LoseCommittedCompletionHandler(bool lose, string cookie, string csrf, string root) : DelegatingHandler(
    new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("Cookie", cookie); request.Headers.Add("X-CSRF-Token", csrf);
        if (request.RequestUri!.AbsolutePath.EndsWith("/fail", StringComparison.Ordinal)) Counter.Increment(root, "fail-count.txt");
        var response = await base.SendAsync(request, ct);
        if (lose && request.RequestUri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal) && response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync(); response.Dispose();
            throw new HttpRequestException("Simulated loss after Main committed the completion.");
        }
        return response;
    }
}

static class Counter
{
    public static int Read(string root, string name) => File.Exists(Path.Combine(root, name))
        ? int.Parse(File.ReadAllText(Path.Combine(root, name)), System.Globalization.CultureInfo.InvariantCulture) : 0;
    public static void Increment(string root, string name) => File.WriteAllText(Path.Combine(root, name),
        (Read(root, name) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
}
