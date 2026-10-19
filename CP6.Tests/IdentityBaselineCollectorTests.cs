using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.CrmIdentity.Baselines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Tests;

public sealed class IdentityBaselineCollectorTests
{
    [Fact]
    public async Task Collector_reads_both_complete_directories_and_preserves_exact_snapshot_bytes()
    {
        var runtime = IdentityBaselineTests.Runtime(); var raw = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime));
        List<string> paths = [];
        using var handler = new Reply(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("fixture-token", request.Headers.Authorization?.Parameter);
            paths.Add(request.RequestUri!.AbsolutePath);
            return Json(request, paths.Count == 2 ? raw : new IdentityBaselinePageV1(IdentityBaselineVerifier.Contract, raw.Source, raw.Boundary, 1, [raw.Item], null));
        });
        using var collector = new IdentityBaselineCollector(runtime, raw.Source, new("https://identity.example.test/"),
            _ => ValueTask.FromResult("fixture-token"), null, handler);
        var result = await collector.CollectAsync();
        Assert.Equal(3, paths.Count); Assert.EndsWith("/versions", paths[0]); Assert.Contains("/snapshots/", paths[1]); Assert.Equal(paths[0], paths[2]);
        Assert.Equal(raw, Assert.Single(result.Items)); Assert.Equal("SOURCE_SNAPSHOT", result.Provenance);
    }

    [Fact]
    public async Task Multiple_pages_are_complete_before_snapshot_reads_and_final_set_order_does_not_change_identity()
    {
        var runtime = IdentityBaselineTests.Runtime(); var first = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime));
        var second = Permission(first); var calls = 0;
        using var handler = new Reply(request => Json(request, calls++ switch
        {
            0 => Page(first, 2, [first.Item], "opaque-next"),
            1 => Page(first, 2, [second.Item]),
            2 => first,
            3 => second,
            4 => Page(first, 2, [second.Item], "different-final-cursor"),
            _ => Page(first, 2, [first.Item])
        }));
        using var collector = Create(runtime, first.Source, handler, new(PageSize: 1));
        var result = await collector.CollectAsync(); Assert.Equal(6, calls); Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { second, first }.OrderBy(x => x.Item.AggregateId, StringComparer.Ordinal), result.Items);
        var expected = new IdentityBaselineVerifier(runtime, first.Source, first.Boundary, 2, [first.Item, second.Item]);
        expected.Add(second); expected.Add(first);
        Assert.Equal(expected.Complete(first.Boundary, 2, [second.Item, first.Item]).ContentSha256, result.ContentSha256);
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("duplicate")]
    [InlineData("cursor-loop")]
    [InlineData("page-boundary")]
    [InlineData("source")]
    [InlineData("empty-next")]
    [InlineData("final-boundary")]
    [InlineData("final-version")]
    [InlineData("raw-hash")]
    [InlineData("item-limit")]
    [InlineData("payload-budget")]
    public async Task Incomplete_changed_or_over_budget_sources_never_return_a_partial_baseline(string scenario)
    {
        var runtime = IdentityBaselineTests.Runtime(); var raw = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime));
        var second = Permission(raw); var calls = 0;
        using var handler = new Reply(request =>
        {
            var index = calls++; object body = index == 1 ? raw : Page(raw, 1, [raw.Item]);
            body = scenario switch
            {
                "truncated" => Page(raw, 2, [raw.Item]),
                "duplicate" => Page(raw, 2, [raw.Item, raw.Item]),
                "cursor-loop" => Page(raw, 3, [index == 0 ? raw.Item : second.Item], "repeat"),
                "page-boundary" => Page(raw, 2, [index == 0 ? raw.Item : second.Item], index == 0 ? "next" : null) with { Boundary = index == 0 ? raw.Boundary : "changed" },
                "source" => Page(raw, 1, [raw.Item]) with { Source = raw.Source with { StorageIdentitySha256 = new string('b', 64) } },
                "empty-next" => Page(raw, 1, [], "next"),
                "final-boundary" when index == 2 => Page(raw, 1, [raw.Item]) with { Boundary = "changed" },
                "final-version" when index == 2 => Page(raw, 1, [raw.Item with { Version = 2 }]),
                "raw-hash" when index == 1 => raw with { PayloadBase64 = Convert.ToBase64String("{}"u8) },
                "item-limit" => Page(raw, 2, [raw.Item, second.Item]),
                _ => body
            };
            return Json(request, body);
        });
        var limits = scenario == "item-limit" ? new IdentityBaselineLimits(MaximumItems: 1) :
            scenario == "payload-budget" ? new(MaximumPayloadBytes: 1) : new();
        using var collector = Create(runtime, raw.Source, handler, new(Limits: limits));
        await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync());
        Assert.InRange(calls, 1, 3); // A failed attempt is not automatically retried.
    }

    [Theory]
    [InlineData(301)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(503)]
    public async Task HTTP_failure_is_fixed_and_never_retries_or_consumes_the_error_body(int status)
    {
        var runtime = IdentityBaselineTests.Runtime(); var source = IdentityBaselineTests.Source(runtime); var calls = 0;
        using var handler = new Reply(request =>
        { calls++; return new((HttpStatusCode)status) { RequestMessage = request, Content = new StringContent("private-server-detail") }; });
        using var collector = Create(runtime, source, handler);
        var error = await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync());
        Assert.Equal("C02_BASELINE_HTTP_" + status, error.Message); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("utf8")]
    [InlineData("content-type")]
    [InlineData("compressed")]
    [InlineData("cacheable")]
    [InlineData("redirected-uri")]
    [InlineData("declared-length")]
    public async Task Ambiguous_or_unsupported_wire_response_is_rejected_before_any_snapshot(string scenario)
    {
        var runtime = IdentityBaselineTests.Runtime(); var raw = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime)); var calls = 0;
        using var handler = new Reply(request =>
        {
            calls++; var page = Page(raw, 1, [raw.Item]); var json = JsonSerializer.Serialize(page, IdentityEventContracts.Json);
            var node = JsonNode.Parse(json)!;
            if (scenario == "missing") node["items"]![0]!.AsObject().Remove("isDeleted");
            if (scenario == "unknown") node["source"]!["unrecognized"] = true;
            if (scenario is "missing" or "unknown") json = node.ToJsonString();
            if (scenario == "duplicate") json = json.Replace("\"totalCount\":1", "\"totalCount\":1,\"totalCount\":1");
            var response = Json(request, page); response.Content.Dispose();
            response.Content = scenario == "utf8" ? new ByteArrayContent([0xff]) : new StringContent(json, Encoding.UTF8, "application/json");
            if (scenario == "utf8") response.Content.Headers.ContentType = new("application/json");
            if (scenario == "content-type") response.Content.Headers.ContentType = new("text/html");
            if (scenario == "compressed") response.Content.Headers.ContentEncoding.Add("gzip");
            if (scenario == "cacheable") response.Headers.CacheControl = null;
            if (scenario == "redirected-uri") response.RequestMessage = new(HttpMethod.Get, "https://foreign.example.test/");
            if (scenario == "declared-length") response.Content.Headers.ContentLength = 2_000_000;
            return response;
        });
        using var collector = Create(runtime, raw.Source, handler);
        await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync()); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Chunked_body_is_bounded_without_relying_on_content_length()
    {
        var runtime = IdentityBaselineTests.Runtime(); var source = IdentityBaselineTests.Source(runtime);
        var stream = new ChunkedStream(new byte[1_048_576 + 16384]);
        using var handler = new Reply(request =>
        {
            var response = Json(request, new { }); response.Content.Dispose(); response.Content = new StreamContent(stream);
            response.Content.Headers.ContentType = new("application/json"); return response;
        });
        using var collector = Create(runtime, source, handler);
        var error = await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync());
        Assert.Equal("C02_BASELINE_LIMIT_EXCEEDED", error.Message); Assert.InRange(stream.ReadBytes, 1_048_577, 1_048_576 + 8192); Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task Cancellation_and_auth_failure_do_not_send_or_publish_partial_work()
    {
        var runtime = IdentityBaselineTests.Runtime(); var raw = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime));
        using var cancellation = new CancellationTokenSource(); var calls = 0;
        using var handler = new Reply(request => { calls++; cancellation.Cancel(); return Json(request, Page(raw, 1, [raw.Item])); });
        using var collector = Create(runtime, raw.Source, handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.CollectAsync(cancellation.Token)); Assert.Equal(1, calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.CollectAsync(cancellation.Token)); Assert.Equal(1, calls);
        using var noAuth = new IdentityBaselineCollector(runtime, raw.Source, new("https://identity.example.test/"),
            _ => throw new InvalidOperationException("private-auth-detail"), null, new Reply(_ => throw new Xunit.Sdk.XunitException("Must not send")));
        Assert.Equal("C02_BASELINE_AUTH_UNAVAILABLE", (await Assert.ThrowsAsync<IdentityBaselineException>(() => noAuth.CollectAsync())).Message);
    }

    [Fact]
    public async Task Empty_source_is_complete_but_does_not_create_authorization()
    {
        var runtime = IdentityBaselineTests.Runtime(); var raw = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime)); var calls = 0;
        using var handler = new Reply(request => { calls++; return Json(request, Page(raw, 0, [])); });
        using var collector = Create(runtime, raw.Source, handler);
        var result = await collector.CollectAsync(); Assert.Empty(result.Items); Assert.Equal("SOURCE_SNAPSHOT", result.Provenance); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Owned_real_HTTP_transport_requires_explicit_loopback_and_never_follows_redirects()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build(); var arrivals = 0; var traps = 0;
        app.MapGet("/internal/crm/identity/directory/v1/versions", (HttpContext context) =>
        { Interlocked.Increment(ref arrivals); context.Response.StatusCode = 302; context.Response.Headers.Location = "/credential-trap"; return Task.CompletedTask; });
        app.MapGet("/credential-trap", () => { Interlocked.Increment(ref traps); return Results.Ok(); });
        await app.StartAsync();
        try
        {
            var uri = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            var runtime = IdentityBaselineTests.Runtime(); var source = IdentityBaselineTests.Source(runtime);
            Assert.Throws<IdentityBaselineException>(() => new IdentityBaselineCollector(runtime, source, uri, _ => ValueTask.FromResult("fixture-token")));
            using var collector = new IdentityBaselineCollector(runtime, source, uri, _ => ValueTask.FromResult("fixture-token"), new(AllowLoopbackHttp: true));
            Assert.Equal("C02_BASELINE_HTTP_302", (await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync())).Message);
            Assert.Equal(1, arrivals); Assert.Equal(0, traps);
            foreach (var invalid in new[] { "http://identity.example.test/", "https://identity.example.test/path", "https://user:pass@identity.example.test/", "https://identity.example.test/?target=elsewhere" })
                Assert.Throws<IdentityBaselineException>(() => new IdentityBaselineCollector(runtime, source, new(invalid), _ => ValueTask.FromResult("fixture-token"), new(AllowLoopbackHttp: true)));
        }
        finally { await app.StopAsync(); }
    }

    private static IdentityBaselinePageV1 Page(IdentityRawSnapshotV1 raw, long count, IdentityVersion[] items, string? next = null)
        => new(IdentityBaselineVerifier.Contract, raw.Source, raw.Boundary, count, items, next);

    [Fact]
    public async Task A_valid_snapshot_for_another_requested_item_is_not_silently_swapped()
    {
        var runtime = IdentityBaselineTests.Runtime(); var first = IdentityBaselineTests.Snapshot(IdentityBaselineTests.Source(runtime));
        var second = Permission(first); var calls = 0;
        using var handler = new Reply(request => Json(request, calls++ switch
        { 0 or 3 => Page(first, 2, [first.Item, second.Item]), 1 => second, _ => first }));
        using var collector = Create(runtime, first.Source, handler);
        await Assert.ThrowsAsync<IdentityBaselineException>(() => collector.CollectAsync()); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Cancellation_does_not_wait_for_an_uncooperative_async_token_provider()
    {
        var runtime = IdentityBaselineTests.Runtime(); var source = IdentityBaselineTests.Source(runtime);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var cancellation = new CancellationTokenSource();
        using var collector = new IdentityBaselineCollector(runtime, source, new("https://identity.example.test/"),
            _ => { started.TrySetResult(); return new(pending.Task); }, null, new Reply(_ => throw new Xunit.Sdk.XunitException("Must not send")));
        var task = collector.CollectAsync(cancellation.Token); await started.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancellation.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(3))); }
        finally { pending.TrySetResult("fixture-token"); try { await task; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task Operation_deadline_cancels_an_unfinished_body_and_disposes_the_response()
    {
        var runtime = IdentityBaselineTests.Runtime(); var source = IdentityBaselineTests.Source(runtime); var stream = new WaitingStream();
        using var handler = new Reply(request =>
        {
            var response = Json(request, new { }); response.Content.Dispose(); response.Content = new StreamContent(stream);
            response.Content.Headers.ContentType = new("application/json"); return response;
        });
        using var collector = Create(runtime, source, handler, new(OperationTimeout: TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.CollectAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stream.Started); Assert.True(stream.Disposed);
    }
    private static IdentityRawSnapshotV1 Permission(IdentityRawSnapshotV1 raw)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"tenantId\":\"" + raw.Source.TenantId + "\",\"subjectId\":\"role:777\",\"enabled\":true,\"version\":1,\"grants\":[],\"deleted\":false}");
        return raw with { Item = new("permission:role:777", IdentityEventContracts.PermissionChanged, 1, IdentityEventContracts.Hash(bytes), false), PayloadBase64 = Convert.ToBase64String(bytes) };
    }
    private static IdentityBaselineCollector Create(CrmIdentityRuntime runtime, IdentityBaselineSourceV1 source, HttpMessageHandler handler, IdentityBaselineCollectorOptions? options = null)
        => new(runtime, source, new("https://identity.example.test/"), _ => ValueTask.FromResult("fixture-token"), options, handler);

    private sealed class ChunkedStream(byte[] bytes) : Stream
    {
        private int _position;
        internal int ReadBytes => _position;
        internal bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { var size = Math.Min(count, bytes.Length - _position); bytes.AsSpan(_position, size).CopyTo(buffer.AsSpan(offset)); _position += size; return size; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); var size = Math.Min(buffer.Length, bytes.Length - _position); bytes.AsMemory(_position, size).CopyTo(buffer); _position += size; return ValueTask.FromResult(size); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class WaitingStream : Stream
    {
        internal bool Started { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Started = true; await Task.Delay(Timeout.Infinite, ct); return 0; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static HttpResponseMessage Json(HttpRequestMessage request, object value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = JsonContent.Create(value, value.GetType()) };
        response.Headers.CacheControl = new() { NoStore = true }; return response;
    }
    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
