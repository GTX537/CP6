using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Platform.Abstractions;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.AspNetCore.Context;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts.Context;
using CP6.Platform.Contracts.ObservabilityV1;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging.Context;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

// Task-private diagnostic host. Every identity/registration below is synthetic.
// No Main Program, business service, database, or external exporter is registered.
return await DiagnosticProbe.RunAsync(args);

internal static class DiagnosticProbe
{
    private const string Marker = "SYNTHETIC-private-probe-payload";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        // Reject unsafe CLI before starting a host or writing any output.
        if (args.Length != 4 || args[0] != "--owner" || args[2] != "--output"
            || !Regex.IsMatch(args[1], "\\A[0-9a-f]{32}\\z") || !Path.IsPathFullyQualified(args[3]))
        {
            Console.Error.WriteLine("Expected --owner <32lowerhex> --output <owner>/runtime/runtime.json");
            return 2;
        }
        var owner = args[1];
        var output = Path.GetFullPath(args[3]);
        var ownerRoot = Directory.GetParent(Path.GetDirectoryName(output)!)?.FullName;
        if (ownerRoot is null || Path.GetFileName(ownerRoot) != owner
            || !string.Equals(output, Path.Combine(ownerRoot, "runtime", "runtime.json"), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(AppContext.BaseDirectory).StartsWith(Path.Combine(ownerRoot, "probe") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || HasReparsePoint(Path.GetDirectoryName(output)!)
            || File.Exists(output) || Directory.Exists(output))
        {
            Console.Error.WriteLine("Output and executable must belong to the same unreparseable owner directory.");
            return 2;
        }

        var report = new RuntimeReport { Owner = owner };
        WebApplication? host = null;
        Uri? address = null;
        ObservabilityRuntimeV1? runtime = null;
        var sink = new LocalTelemetrySinkV1(4096);
        var originalPropagator = DistributedContextPropagator.Current;
        var originalOtelPropagator = Propagators.DefaultTextMapPropagator;
        var observedTypes = new HashSet<Type>();
        var state = new SyntheticState();
        var stage = "host-construction";
        try
        {
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
            {
                Args = [], EnvironmentName = "WP02Diagnostic", ContentRootPath = Path.Combine(ownerRoot, "probe")
            });
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddRouting();
            builder.Services.AddSingleton(state);
            builder.Services.AddCp6TrustedRequestContextV1<SyntheticHttpVerifier, SyntheticAuthority>();
            var policy = new TelemetryPolicyV1("wp02-probe", "diagnostic", "local",
                ["/probe/positive", "/probe/target", "/probe/unauthenticated", "/probe/revoked", "/probe/unavailable"]);
            builder.Services.AddCp6ObservabilityV1(policy, sink,
                new TelemetryLimitsV1(queueCapacity: 4096, maximumRecords: 4096, batchSize: 32, recordsPerSecond: 4096, maximumSeries: 128));
            host = builder.Build();
            host.UseCp6TrustedRequestContextV1();
            host.MapGet("/probe/positive", async (HttpContext http, ILogger<RuntimeReport> logger) =>
            {
                Interlocked.Increment(ref state.PositiveHandlers);
                var accessor = http.RequestServices.GetRequiredService<IRequestContextAccessor>();
                var trusted = http.RequestServices.GetRequiredService<ITrustedContextAccessorV1>();
                Check(report, "http.trusted-context", accessor.Current?.TenantId == SyntheticState.Tenant
                    && accessor.Current.Subject == "synthetic-human" && trusted.CurrentTrusted?.Evidence.EntryKind == ContextEntryKindV1.Http);
                Check(report, "http.caller-fields-ignored", accessor.Current?.Subject != http.Request.Query["actor"]
                    && accessor.Current?.TenantId.ToString() != http.Request.Headers["X-Tenant"].ToString());
                using var source = new ActivitySource(Cp6TelemetrySources.AspNetCore);
                using var activity = source.StartActivity("synthetic-operation");
                activity?.SetTag("cp6.outcome", "success");
                activity?.SetTag("secret", Marker);
                activity?.SetBaggage("secret", Marker);
                logger.LogWarning("Synthetic payload {Payload}", Marker);
                using var meter = new Meter(Cp6TelemetryMeters.Messaging);
                meter.CreateCounter<long>("cp6.messaging.published").Add(1,
                    new KeyValuePair<string, object?>("cp6.outcome", "success"), new("secret", Marker));
                await ExerciseExecutorsAsync(http.RequestServices, report, observedTypes);
                using var downstream = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
                using var response = await downstream.GetAsync(new Uri(address!, "/probe/target"));
                Check(report, "http.downstream", response.StatusCode == HttpStatusCode.OK);
                return Results.Text("synthetic-context-ok");
            });
            host.MapGet("/probe/target", (HttpContext http) =>
            {
                Interlocked.Increment(ref state.TargetHandlers);
                Check(report, "http.trace-propagation", http.Request.Headers.ContainsKey("traceparent"));
                Check(report, "http.baggage-stripped", !http.Request.Headers.ContainsKey("baggage"));
                return Results.Text("synthetic-target-ok");
            });
            foreach (var denied in new[] { "unauthenticated", "revoked", "unavailable" })
                host.MapGet("/probe/" + denied, () => { Interlocked.Increment(ref state.DeniedHandlers); return Results.Ok(); });

            // Force actual managed EF code execution without creating any DbContext or connection.
            var efOptions = new Cp6TransactionalMessagingOptions();
            efOptions.Validate();
            observedTypes.Add(efOptions.GetType());
            observedTypes.UnionWith([typeof(ContextResolutionInputV1), typeof(ActivitySource), typeof(Meter), typeof(Sdk),
                typeof(OpenTelemetry.Trace.TracerProvider), typeof(OpenTelemetry.Metrics.MeterProvider), typeof(OpenTelemetry.Logs.LogRecord)]);
            observedTypes.Add(sink.GetType());
            stage = "host-start";
            await host.StartAsync();
            var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Check(report, "http.single-loopback-listener", addresses.Count == 1
                && Uri.TryCreate(addresses.Single(), UriKind.Absolute, out var bound)
                && bound.Scheme == "http" && bound.Host == "127.0.0.1" && bound.Port > 0);
            address = new Uri(addresses.Single());
            report.Http.Address = address.ToString();
            runtime = host.Services.GetRequiredService<ObservabilityRuntimeV1>();
            observedTypes.Add(runtime.GetType());

            stage = "http-requests";
            using (var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) })
            {
                foreach (var (name, status) in new[] { ("positive", 200), ("unauthenticated", 401), ("revoked", 403), ("unavailable", 503) })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, "/probe/" + name + "?actor=caller-admin");
                    request.Headers.Add("X-Tenant", "caller-tenant");
                    request.Headers.Add("baggage", "secret=" + Marker);
                    using var response = await client.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();
                    var passed = (int)response.StatusCode == status && (status == 200 ? body == "synthetic-context-ok" : body.Length == 0);
                    report.Http.Requests.Add(new(name, "/probe/" + name, status, (int)response.StatusCode, body, passed));
                    Check(report, "http.response." + name, passed);
                }
            }
            Check(report, "http.denied-handlers-not-run", state.DeniedHandlers == 0 && state.PositiveHandlers == 1 && state.TargetHandlers == 1);
            report.Context.HttpVerifications = state.HttpVerifications;
            report.Context.AuthorityResolutions = state.AuthorityResolutions;
            report.Context.DeniedHandlers = state.DeniedHandlers;

            stage = "telemetry-flush";
            // The HTTP response may arrive before the instrumentation OnEnd callback; wait for
            // the real five server spans, then flush their actual SDK metric/log exports.
            var deadline = Stopwatch.StartNew();
            do
            {
                report.Telemetry.FlushSucceeded = await runtime.FlushAsync(TimeSpan.FromSeconds(2));
                if (sink.Snapshot().Count(x => x.Signal == TelemetrySignalV1.Trace && x.Name == "http.server") >= 5) break;
                await Task.Delay(20);
            } while (deadline.Elapsed < TimeSpan.FromSeconds(5));
            report.Telemetry.Records = sink.Snapshot();
            report.Telemetry.DroppedCount = runtime.DroppedCount;
            report.Telemetry.PropagationIntact = runtime.PropagationIntact;
            report.Telemetry.ExternalExportEnabled = runtime.ExternalExportEnabled;
            report.Telemetry.Completeness = runtime.Completeness.ToString();
            report.Telemetry.Counts = report.Telemetry.Records.GroupBy(x => x.Signal.ToString()).ToDictionary(x => x.Key, x => x.Count());
            Check(report, "telemetry.flush", report.Telemetry.FlushSucceeded && report.Telemetry.DroppedCount == 0);
            Check(report, "telemetry.server-traces", report.Telemetry.Records.Count(x => x.Signal == TelemetrySignalV1.Trace && x.Name == "http.server") >= 5);
            Check(report, "telemetry.client-trace", report.Telemetry.Records.Any(x => x.Signal == TelemetrySignalV1.Trace && x.Name == "http.client"));
            Check(report, "telemetry.metric", report.Telemetry.Records.Any(x => x.Signal == TelemetrySignalV1.Metric && x.Name == "cp6.messaging.published" && x.Value == 1));
            Check(report, "telemetry.log", report.Telemetry.Records.Any(x => x.Signal == TelemetrySignalV1.Log && x.Name == "log.event"));
            Check(report, "telemetry.privacy", !JsonSerializer.Serialize(report.Telemetry.Records, Json).Contains(Marker, StringComparison.Ordinal)
                && report.Telemetry.Records.All(x => x.Attributes.All(a => policy.Allows(a.Key, a.Value)) && x.Resources.Count == 3));
            Check(report, "telemetry.local-only", !runtime.ExternalExportEnabled && runtime.PropagationIntact);
        }
        catch (Exception error) { report.Failures.Add(new(stage, error.GetType().FullName!, error.Message)); }
        finally
        {
            if (runtime is not null && report.Telemetry.Records.Count == 0)
                report.Telemetry.Records = sink.Snapshot();
            if (host is not null)
            {
                try { using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await host.StopAsync(budget.Token); report.Cleanup.StopSucceeded = true; }
                catch (Exception error) { report.Failures.Add(new("host-stop", error.GetType().FullName!, error.Message)); }
                try { await host.DisposeAsync(); report.Cleanup.DisposeSucceeded = true; }
                catch (Exception error) { report.Failures.Add(new("host-dispose", error.GetType().FullName!, error.Message)); }
            }
            if (address is not null)
            {
                var listenerCheck = Stopwatch.StartNew();
                try
                {
                    using var socket = new TcpClient();
                    // Windows may report connection refusal after its ~2s SYN retry.
                    // A timeout is still a failure; only an actual refusal proves closure.
                    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await socket.ConnectAsync(IPAddress.Loopback, address.Port, budget.Token);
                }
                catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    report.Cleanup.ListenerClosed = true;
                    report.Cleanup.ListenerSocketError = error.SocketErrorCode.ToString();
                }
                catch (Exception error) { report.Failures.Add(new("listener-check", error.GetType().FullName!, error.Message)); }
                finally { report.Cleanup.ListenerCheckMilliseconds = listenerCheck.Elapsed.TotalMilliseconds; }
            }
            report.Cleanup.PropagatorsRestored = ReferenceEquals(originalPropagator, DistributedContextPropagator.Current)
                && ReferenceEquals(originalOtelPropagator, Propagators.DefaultTextMapPropagator);
            RecordCheck(report, "cleanup.stop", report.Cleanup.StopSucceeded);
            RecordCheck(report, "cleanup.dispose", report.Cleanup.DisposeSucceeded);
            RecordCheck(report, "cleanup.listener-closed", report.Cleanup.ListenerClosed);
            RecordCheck(report, "cleanup.propagators-restored", report.Cleanup.PropagatorsRestored);
            try { CaptureAssemblies(report, observedTypes, ownerRoot); }
            catch (Exception error) { report.Failures.Add(new("assembly-capture", error.GetType().FullName!, error.Message)); }
            report.CompletedUtc = DateTimeOffset.UtcNow;
            report.Status = report.Failures.Count == 0 && report.Assertions.All(x => x.Passed) ? "PASS" : "FAIL";
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using var reportFile = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(reportFile, report, Json);
        }
        Console.WriteLine($"WP02-02 runtime diagnostic {report.Status}; authority=SYNTHETIC_AUTHORITY; Main=NOT_RUN; formal trust=BLOCKED");
        return report.Status == "PASS" ? 0 : 1;
    }

    private static bool HasReparsePoint(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    private static async Task ExerciseExecutorsAsync(IServiceProvider services, RuntimeReport report, HashSet<Type> types)
    {
        var executor = services.GetRequiredService<TrustedContextExecutorV1>();
        var accessor = services.GetRequiredService<ITrustedContextAccessorV1>();
        var outer = accessor.CurrentTrusted;
        types.Add(executor.GetType());
        foreach (var kind in new[] { ContextEntryKindV1.Background, ContextEntryKindV1.Message })
        {
            var verifier = new SyntheticEntryVerifier(kind);
            var background = new BackgroundContextExecutorV1<bool>(executor, verifier);
            var message = new MessageContextExecutorV1<bool>(executor, verifier);
            types.Add(kind == ContextEntryKindV1.Message ? message.GetType() : background.GetType());
            Task Execute(bool authenticated, Func<CancellationToken, Task> handler) => kind == ContextEntryKindV1.Background
                ? background.ExecuteAsync(authenticated, handler) : message.ExecuteAsync(authenticated, handler);
            var name = kind.ToString().ToLowerInvariant();
            var successfulHandlers = 0;
            await Execute(true, _ =>
            {
                Check(report, "context." + name + ".trusted", accessor.CurrentTrusted?.Evidence.EntryKind == kind
                    && accessor.CurrentTrusted.Context.Subject == "synthetic-service"
                    && accessor.CurrentTrusted.Context.TenantId == SyntheticState.Tenant);
                successfulHandlers++;
                return Task.CompletedTask;
            });
            Check(report, "context." + name + ".restored", ReferenceEquals(outer, accessor.CurrentTrusted));
            var deniedHandler = false;
            var denied = false;
            try { await Execute(false, _ => { deniedHandler = true; return Task.CompletedTask; }); }
            catch (ContextResolutionExceptionV1 error) when (error.Failure == ContextResolutionFailureV1.AuthenticationRequired) { denied = true; }
            Check(report, "context." + name + ".denied", denied && !deniedHandler && ReferenceEquals(outer, accessor.CurrentTrusted));
            report.Context.Executors.Add(new(name, verifier.Calls, successfulHandlers, denied ? 1 : 0, ReferenceEquals(outer, accessor.CurrentTrusted)));
        }
    }

    private static void CaptureAssemblies(RuntimeReport report, HashSet<Type> observedTypes, string ownerRoot)
    {
        var required = new[] { "CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging", "System.Diagnostics.DiagnosticSource", "OpenTelemetry", "OpenTelemetry.Api" };
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic &&
            (a.GetName().Name!.StartsWith("CP6.Platform.", StringComparison.Ordinal) || a.GetName().Name!.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || a.GetName().Name == "System.Diagnostics.DiagnosticSource")).ToArray();
        foreach (var assembly in loaded.OrderBy(x => x.GetName().Name, StringComparer.Ordinal))
        {
            var path = Path.GetFullPath(assembly.Location);
            using var stream = File.OpenRead(path);
            report.Assemblies.Add(new(assembly.GetName().Name!, assembly.GetName().Version!.ToString(), path,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), assembly.ManifestModule.ModuleVersionId.ToString(),
                observedTypes.Where(x => x.Assembly == assembly).Select(x => x.FullName!).Order(StringComparer.Ordinal).ToArray()));
        }
        Check(report, "assemblies.required", required.All(name => report.Assemblies.Count(a => a.Name == name) == 1));
        Check(report, "assemblies.owner-contained", report.Assemblies.All(a => a.Path.StartsWith(ownerRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        Check(report, "assemblies.platform-types-observed", report.Assemblies.Where(a => a.Name.StartsWith("CP6.Platform.", StringComparison.Ordinal)).All(a => a.ObservedTypes.Length > 0));
    }

    private static void RecordCheck(RuntimeReport report, string name, bool passed) => report.Assertions.Enqueue(new(name, passed));
    private static void Check(RuntimeReport report, string name, bool passed)
    {
        RecordCheck(report, name, passed);
        if (!passed) throw new InvalidOperationException("Diagnostic assertion failed: " + name);
    }
}

internal sealed class SyntheticState
{
    public static readonly Guid Tenant = Guid.Parse("00000000-0000-4000-8000-000000000002");
    public int HttpVerifications, AuthorityResolutions, PositiveHandlers, TargetHandlers, DeniedHandlers;
    public static ContextResolutionInputV1 Input(ContextEntryKindV1 kind, string profile = "synthetic-profile")
    {
        var now = DateTimeOffset.UtcNow;
        var actor = new ContextActorV1(Tenant, "https://synthetic.invalid", kind == ContextEntryKindV1.Http ? "synthetic-human" : "synthetic-service",
            kind == ContextEntryKindV1.Http ? ContextActorKindV1.Human : ContextActorKindV1.Service, null);
        return new(new(kind, "synthetic-host", profile, "synthetic-audience", "diagnostic", Tenant, "synthetic-source", "synthetic-correlation", ["diagnostic"]),
            new(actor, actor, "synthetic-audience", "local", now.AddMinutes(-1), now.AddMinutes(5), "synthetic-authentication", null, ["diagnostic"]));
    }
}

internal sealed class SyntheticHttpVerifier(SyntheticState state) : IContextSourceVerifierV1<HttpContext>
{
    public ValueTask<ContextResolutionInputV1?> VerifyAsync(HttpContext input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref state.HttpVerifications);
        var path = input.Request.Path.Value;
        return ValueTask.FromResult(path == "/probe/unauthenticated" ? null : SyntheticState.Input(ContextEntryKindV1.Http,
            path == "/probe/revoked" ? "synthetic-revoked" : path == "/probe/unavailable" ? "synthetic-unavailable" : "synthetic-profile"));
    }
}

internal sealed class SyntheticEntryVerifier(ContextEntryKindV1 kind) : IContextSourceVerifierV1<bool>
{
    public int Calls { get; private set; }
    public ValueTask<ContextResolutionInputV1?> VerifyAsync(bool input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Calls++;
        return ValueTask.FromResult(input ? SyntheticState.Input(kind) : null);
    }
}

internal sealed class SyntheticAuthority(SyntheticState state) : IContextBindingAuthorityV1
{
    public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation, AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref state.AuthorityResolutions);
        if (operation.ProfileId == "synthetic-unavailable") throw new ContextResolutionExceptionV1(ContextResolutionFailureV1.AuthorityUnavailable);
        var now = DateTimeOffset.UtcNow;
        return ValueTask.FromResult<ContextBindingRegistrationV1?>(new(SyntheticState.Tenant, SyntheticState.Tenant,
            operation.HostId, operation.ProfileId, source.Actor.Issuer, source.Region, operation.Audience, operation.Purpose,
            operation.EntryKind, source.Actor, source.EffectiveActor, "synthetic-registration", "synthetic-v1",
            now.AddMinutes(-5), now.AddMinutes(5), operation.ProfileId != "synthetic-revoked", ["diagnostic"]));
    }
}

internal sealed class RuntimeReport
{
    public int SchemaVersion { get; } = 1;
    public string EvidenceKind { get; } = "SYNTHETIC_AUTHORITY";
    public string Owner { get; init; } = "";
    public string Status { get; set; } = "FAIL";
    public string MainConsumption { get; } = "NOT_RUN";
    public string FormalPackageTrust { get; } = "BLOCKED";
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedUtc { get; set; }
    public HttpEvidence Http { get; } = new();
    public ContextEvidence Context { get; } = new();
    public TelemetryEvidence Telemetry { get; } = new();
    public CleanupEvidence Cleanup { get; } = new();
    public List<AssemblyEvidence> Assemblies { get; } = [];
    public ConcurrentQueue<AssertionEvidence> Assertions { get; } = new();
    public List<FailureEvidence> Failures { get; } = [];
}
internal sealed class HttpEvidence { public string? Address { get; set; } public List<HttpRequestEvidence> Requests { get; } = []; }
internal sealed record HttpRequestEvidence(string Name, string Path, int ExpectedStatus, int ActualStatus, string Body, bool Passed);
internal sealed class ContextEvidence
{
    public int HttpVerifications { get; set; }
    public int AuthorityResolutions { get; set; }
    public int DeniedHandlers { get; set; }
    public List<ExecutorEvidence> Executors { get; } = [];
}
internal sealed record ExecutorEvidence(string Name, int Verifications, int SuccessfulHandlers, int Denials, bool Restored);
internal sealed class TelemetryEvidence
{
    public bool FlushSucceeded { get; set; }
    public long DroppedCount { get; set; }
    public bool PropagationIntact { get; set; }
    public bool ExternalExportEnabled { get; set; }
    public string? Completeness { get; set; }
    public IReadOnlyList<SafeTelemetryRecordV1> Records { get; set; } = [];
    public Dictionary<string, int> Counts { get; set; } = [];
}
internal sealed class CleanupEvidence
{
    public bool StopSucceeded { get; set; }
    public bool DisposeSucceeded { get; set; }
    public bool ListenerClosed { get; set; }
    public string? ListenerSocketError { get; set; }
    public double ListenerCheckMilliseconds { get; set; }
    public bool PropagatorsRestored { get; set; }
}
internal sealed record AssemblyEvidence(string Name, string Version, string Path, string Sha256, string Mvid, string[] ObservedTypes);
internal sealed record AssertionEvidence(string Name, bool Passed);
internal sealed record FailureEvidence(string Stage, string Type, string Message);
