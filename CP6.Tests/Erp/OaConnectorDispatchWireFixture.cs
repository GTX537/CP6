using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CP6.Tests.Erp;

internal sealed class OaConnectorDispatchWireFixture : IAsyncDisposable
{
    private WebApplication app = null!;
    private readonly ConcurrentDictionary<string, Call> effects = new();
    internal readonly ConcurrentQueue<Call> Sends = new();
    internal readonly ConcurrentQueue<Call> Queries = new();
    internal readonly TaskCompletionSource<Call> Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<Call, Task>? BeforeEffect { get; set; }
    internal Task? HoldReply { get; set; }
    internal bool LoseReply { get; set; }
    internal bool FirstQueryNotObserved { get; set; }
    internal bool SecondQueryWrongDigest { get; set; }
    internal int Effects => effects.Count;
    internal Uri Origin => new(app.Urls.Single());
    internal sealed record Call(string Effect, string Transport, string Digest, byte[] Body);

    internal static async Task<OaConnectorDispatchWireFixture> StartAsync()
    {
        var fixture = new OaConnectorDispatchWireFixture();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        fixture.app = builder.Build();
        fixture.app.MapPost("/operations", fixture.Send);
        fixture.app.MapGet("/operations/{key}", fixture.Query);
        await fixture.app.StartAsync();
        return fixture;
    }

    private async Task Send(HttpContext context)
    {
        var request = context.Request;
        using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream, context.RequestAborted);
        var call = Read(request, stream.ToArray());
        Sends.Enqueue(call);
        try
        {
            if (BeforeEffect != null) await BeforeEffect(call);
            Assert.Equal(call.Digest, Convert.ToHexString(SHA256.HashData(call.Body)).ToLowerInvariant());
            Assert.Equal(call.Body, effects.GetOrAdd(call.Effect, call).Body);
            Arrived.TrySetResult(call);
        }
        catch (Exception error)
        {
            Arrived.TrySetException(error);
            throw;
        }
        if (HoldReply != null) await HoldReply.WaitAsync(context.RequestAborted);
        if (LoseReply) { context.Abort(); return; }
        await context.Response.WriteAsJsonAsync(Reply(call, "ACK"));
    }

    private async Task Query(HttpContext context)
    {
        var call = Read(context.Request, []);
        Assert.Equal(call.Effect, context.Request.RouteValues["key"]);
        Queries.Enqueue(call);
        if (FirstQueryNotObserved && Queries.Count == 1 || !effects.TryGetValue(call.Effect, out var original))
        {
            await context.Response.WriteAsJsonAsync(Reply(call, "NOT_OBSERVED"));
            return;
        }
        Assert.Equal((original.Effect, original.Transport, original.Digest), (call.Effect, call.Transport, call.Digest));
        if (SecondQueryWrongDigest && Queries.Count == 2)
            original = original with { Digest = new string('0', 64) };
        await context.Response.WriteAsJsonAsync(Reply(original, "ORIGINAL", "simulated-provider-receipt/1"));
    }

    private static Call Read(HttpRequest request, byte[] body) =>
        new(request.Headers["CP6-Effect-Key"].ToString(), request.Headers["Idempotency-Key"].ToString(),
            request.Headers["CP6-Input-SHA256"].ToString(), body);

    private static object Reply(Call call, string state, string? proof = null) =>
        new { state, effectKey = call.Effect, transportKey = call.Transport, inputSha256 = call.Digest, originalProofRef = proof };

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}

internal sealed class OaConnectorDispatchHttpParticipant(Guid tenant, Guid environment, ConnectorPinV1 pin, Uri origin, HttpClient client)
    : IConnectorDispatchAdmissionV1
{
    internal int Preparations { get; private set; }
    public bool IsSimulation => true;
    public bool Matches(RegisteredConnectorVersionV1 v) => v.TenantId == tenant && v.EnvironmentId == environment &&
        v.Pin == pin && v.Implementation == "wp1216-http-fixture" && v.ImplementationVersion == "1.0" &&
        v.DestinationPolicy == "local-read-policy-v1" && v.EndpointProfile == "isolated-test-v1" &&
        v.ParameterMapping == "params-v1" && v.InputSchema == "request-v1" && v.OutputSchema == "response-v1" &&
        v.DisclosurePolicy == "scope-v1" && v.CredentialLocator == "opaque-binding" &&
        v.CredentialGeneration == "generation-1" && v.AdoptionEvidence == "isolated-contract-fixture";

    public ConnectorOperationGuaranteesV1 Guarantees(RegisteredConnectorVersionV1 v) =>
        new(v.IdempotencyContract == "simulated-idempotency/1" ? TimeSpan.FromMinutes(30) : null,
            v.ResultQueryContract == "simulated-original-query/1" ? TimeSpan.FromMinutes(30) : null, false);

    public async Task<ConnectorDispatchQualificationV1?> PrepareAsync(CP6Context db, RegisteredConnectorVersionV1 version,
        ConnectorDispatchSourceV1 source, CancellationToken ct)
    {
        Preparations++;
        var access = await PostgreSqlPermissionReader.ReadAsync(db, source.StarterId, ct);
        if (!access.ActionKeys.Contains("oa-flow-admin:conn.connector.activate") ||
            version.AdmissionBasis != "simulated-owner-admission/1") return null;
        using var vars = JsonDocument.Parse(source.VarsJson);
        var subject = vars.RootElement.GetProperty("subject").GetString()!;
        return new("simulated-service/1", "simulated-disclosure/1", "simulated-owner/1", true,
            JsonSerializer.SerializeToUtf8Bytes(new { subject }));
    }

    public async Task<bool> MayQueryOriginalAsync(CP6Context db, RegisteredConnectorVersionV1 version, ConnectorOriginalOperationV1 o,
        ScopeAuthorizationSnapshotV1 authority, CancellationToken ct) =>
        (await PostgreSqlPermissionReader.ReadAsync(db, o.StarterId, ct)).ActionKeys.Contains("oa-flow-admin:conn.read");

    public Task<ConnectorTransportObservationV1> SendAsync(RegisteredConnectorVersionV1 version, ConnectorDispatchRequestV1 request,
        CancellationToken ct) => Call(HttpMethod.Post, "operations", request.Original, request.FrozenInput.ToArray(), ct);

    public Task<ConnectorTransportObservationV1> QueryOriginalAsync(RegisteredConnectorVersionV1 version,
        ConnectorOriginalOperationV1 original, CancellationToken ct) =>
        Call(HttpMethod.Get, "operations/" + original.EffectKey, original, null, ct);

    private sealed record WireObservation(string State, string EffectKey, string TransportKey, string InputSha256, string? OriginalProofRef);

    private async Task<ConnectorTransportObservationV1> Call(HttpMethod method, string path, ConnectorOriginalOperationV1 o,
        byte[]? body, CancellationToken ct)
    {
        Assert.True(origin.IsLoopback);
        using var request = new HttpRequestMessage(method, new Uri(origin, path));
        request.Headers.Add("CP6-Effect-Key", o.EffectKey);
        request.Headers.Add("Idempotency-Key", o.TransportKey);
        request.Headers.Add("CP6-Input-SHA256", o.InputSha256);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK) return new(ConnectorTransportStateV1.Unknown);
        var observed = (await response.Content.ReadFromJsonAsync<WireObservation>(ct))!;
        var state = observed.State switch
        {
            "ACK" => ConnectorTransportStateV1.Acknowledged,
            "NOT_OBSERVED" => ConnectorTransportStateV1.NotObserved,
            "ORIGINAL" => ConnectorTransportStateV1.OriginalOperationObserved,
            _ => ConnectorTransportStateV1.Unknown,
        };
        return new(state, observed.EffectKey, observed.TransportKey, observed.InputSha256, observed.OriginalProofRef);
    }
}
