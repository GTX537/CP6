using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.BackgroundServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static CP6.Core.Services.ErpIntegration.IntegrationOpsContractV1;

namespace CP6.Tests.ErpIntegration;

public sealed partial class ErpHttpAuthorizationTests
{
    private const string Ops = "/api/integration-ops/v1";
    private const string SimulatedOwner = "Fixture.OtherOwner";
    private const string MissingOwner = "Fixture.Offline";
    private const string PrivateMarker = "模拟隐私: alice@fixture.invalid";
    private static readonly Guid Operator = Guid.Parse("16b01171-6f85-4f2c-882c-fc92b7229bb2");
    private readonly ITestOutputHelper opsOutput;

    public ErpHttpAuthorizationTests(ITestOutputHelper output) => opsOutput = output;

    [CurrentPermissionPostgresFact]
    public async Task Integration_ops_exposes_authenticated_main_catalog_on_postgresql()
    {
        var postgres = new CurrentPermissionPostgresFixture();
        await postgres.InitializeAsync();
        try
        {
            await using var connection = postgres.Context(First, CurrentPermissionPostgresFixture.Database);
            var connectionString = connection.Database.GetConnectionString()!;
            var clock = new OpsClock();
            var alternate = new SimulatedFailureSource(SimulatedOwner);
            var missing = new SimulatedFailureSource(MissingOwner) { Available = false };
            var gate = new SimulatedEffectOwner();
            var logs = new CapturedOpsLog();
            var protection = new EphemeralDataProtectionProvider();
            void Configure(IServiceCollection services)
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IDataProtectionProvider>(protection);
                services.AddSingleton<IIntegrationOpsSourceV1>(alternate);
                services.AddSingleton<IIntegrationOpsSourceV1>(missing);
                services.AddSingleton<IErpIntegrationEffectOwnerV1>(gate);
                services.AddSingleton<ILoggerProvider>(logs);
            }
            await using var host = await Fixture.StartPostgreSqlAsync(connectionString, Configure);
            var runtime = host.App.Services.GetRequiredService<ErpIntegrationRuntime>();
            runtime.Options.OpsOwners = [Owner, SimulatedOwner, MissingOwner];
            using var anonymous = await host.ReadAsync(Ops + "/failures", null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using var denied = await host.ReadAsync(Ops + "/failures", host.NativeToken("Admin"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            await host.SeedOpsAuthorityAsync();
            var command = await host.SeedOpsInbox(clock.GetUtcNow(), First);
            await host.SeedOpsInbox(clock.GetUtcNow(), Second);
            var legacyResult = await host.AddOpsCommittedResultAsync(clock);
            const string orderKey = "OPS-SIM-ORD-0001";
            const string competingKey = "OPS-SIM-ORD-0002";
            await host.AddOpsBridgeAsync(orderKey, clock, withResult: true);
            await host.AddOpsBridgeAsync(competingKey, clock, withResult: false);
            var started = await host.ExhaustOpsResultAsync(clock);
            var blockedCommand = await host.SeedOpsInbox(clock.GetUtcNow(), First, allow: false);
            alternate.Key = new(0, clock.GetUtcNow(), SimulatedOwner, Inbox, command.MessageId, "other-first");
            missing.Key = alternate.Key with { OwnerId = MissingOwner };

            await CheckOpsPaging(host, alternate, missing, command.MessageId);
            var listing = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, Ops + "/failures?owner=" + Owner);
            var request = Assert.Single(listing.Items, x => x.NativeId == command.MessageId);
            var blockedRequest = Assert.Single(listing.Items, x => x.NativeId == blockedCommand.MessageId);
            var result = Assert.Single(listing.Items, x => x.NativeId == started.Id.ToString("D"));
            var bridge = Assert.Single(listing.Items, x => x.NativeId == orderKey);
            var competitor = Assert.Single(listing.Items, x => x.NativeId == competingKey);
            Assert.Equal(orderKey, bridge.NativeId);
            var commandDetail = await host.OpsJson<IntegrationFailureV1>(HttpMethod.Get, Ops + "/failures/" + request.FailureRef);
            var resultDetail = await host.OpsJson<IntegrationFailureV1>(HttpMethod.Get, Ops + "/failures/" + result.FailureRef);
            var bridgeDetail = await host.OpsJson<IntegrationFailureV1>(HttpMethod.Get, Ops + "/failures/" + bridge.FailureRef);
            Assert.Equal(command.MessageId, commandDetail.OriginalMessageRef);
            Assert.Equal(ErpEventContracts.Hash(command.Payload.Span), commandDetail.ReplayEvidence!.ExpectedDigest.Value);
            Assert.Equal(started.MessageId, resultDetail.OriginalMessageRef);
            Assert.Equal(started.PayloadSha256, resultDetail.ReplayEvidence!.ExpectedDigest.Value);
            Assert.Equal("7", resultDetail.ResultVersion);
            Assert.Null(bridgeDetail.OriginalMessageRef);
            Assert.Null(bridgeDetail.EnvelopeRef);
            Assert.Equal("c03-bridge-key-v1", bridgeDetail.ReplayEvidence!.ExpectedDigest.CodecVersion);
            Assert.Equal(ErpDeliveryReplayService.BridgeHash(First, orderKey), bridgeDetail.ReplayEvidence.ExpectedDigest.Value);
            using var cross = await host.OpsRequest(HttpMethod.Get, Ops + "/failures/" + request.FailureRef, tenant: Second);
            Assert.Contains(cross.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden });

            var requestInput = await host.OpsInput(request);
            var resultInput = await host.OpsInput(result);
            var bridgeInput = await host.OpsInput(bridge);
            var notSupported = await host.OpsInput(blockedRequest);
            using (var invalid = await host.OpsPost(bridge, bridgeInput with { ExpectedDigest = resultInput.ExpectedDigest }))
                await ExpectOpsError(invalid, HttpStatusCode.Conflict, "DIGEST_DOMAIN_ACTION_MISMATCH");
            using (var forged = await host.OpsPost(bridge, bridgeInput with
                { NativeQueueRef = bridgeInput.NativeQueueRef with { OwnerId = SimulatedOwner } }))
                await ExpectOpsError(forged, HttpStatusCode.BadRequest, "REPLAY_INPUT_INVALID");
            using (var wrongVersion = await host.OpsPost(request, requestInput with { ExpectedNativeVersion = Convert.ToBase64String(new byte[8]) }))
                await ExpectOpsError(wrongVersion, HttpStatusCode.Conflict, "NATIVE_VERSION_CHANGED");
            using (var wrongCodec = await host.OpsPost(result, resultInput with { ExpectedDigest = resultInput.ExpectedDigest with
                { CodecVersion = "unregistered" } }))
                await ExpectOpsError(wrongCodec, HttpStatusCode.Conflict, "DIGEST_DOMAIN_ACTION_MISMATCH");
            using (var wrongHash = await host.OpsPost(result, resultInput with { ExpectedDigest = resultInput.ExpectedDigest with
                { Value = new string('0', 64) } }))
                await ExpectOpsError(wrongHash, HttpStatusCode.Conflict, "ORIGINAL_IDENTITY_CONFLICT");

            await host.OpsLeaseAsync(orderKey, clock.GetUtcNow().AddMinutes(1));
            using (var leased = await host.OpsPost(bridge, await host.OpsInput(bridge)))
                await ExpectOpsError(leased, HttpStatusCode.Conflict, "NATIVE_NOT_ELIGIBLE");
            await host.OpsLeaseAsync(orderKey, null);
            bridgeInput = await host.OpsInput(bridge);
            runtime.Options.OpsRouteEpoch = "C03-FIXTURE-CHANGED";
            using (var changed = await host.OpsPost(bridge, bridgeInput))
                await ExpectOpsError(changed, HttpStatusCode.Conflict, "ELIGIBILITY_CHANGED");
            runtime.Options.OpsRouteEpoch = "C03-MAIN-V1";
            var baseline = new OpsCounts(0, 0, 0, 2, 3);
            Assert.Equal(baseline, await host.CountOps());

            await using (var blocker = host.Queue.CreateDbContext())
            {
                await using var locked = await blocker.Database.BeginTransactionAsync();
                await blocker.Database.ExecuteSqlRawAsync("SELECT cp6_authentication_v1.acquire()");
                using var waiting = await host.OpsPost(result, resultInput);
                await ExpectOpsError(waiting, HttpStatusCode.ServiceUnavailable, "REPLAY_OUTCOME_UNKNOWN");
                Assert.Equal("2", waiting.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await locked.RollbackAsync();
            }
            Assert.Equal(baseline, await host.CountOps());

            await host.InstallOpsFaultAsync(resultInput.ReplayOperationId);
            using (var fault = await host.OpsPost(result, resultInput))
            {
                await ExpectOpsError(fault, HttpStatusCode.ServiceUnavailable, "REPLAY_OUTCOME_UNKNOWN");
                using var body = JsonDocument.Parse(await fault.Content.ReadAsStringAsync());
                Assert.Equal(resultInput.ReplayOperationId, body.RootElement.GetProperty("replayOperationId").GetGuid());
            }
            var unobserved = await host.OpsJson<ErpIntegrationOpsV1.OperationLookup>(HttpMethod.Get,
                Ops + "/replays/" + resultInput.ReplayOperationId);
            Assert.Equal("NOT_OBSERVED", unobserved.Status);
            using (var otherKey = await host.OpsPost(result, resultInput with { ReplayOperationId = Guid.NewGuid() }))
                await ExpectOpsError(otherKey, HttpStatusCode.Conflict, "ELIGIBILITY_CHANGED");
            Assert.Equal(baseline, await host.CountOps());
            await using (var queue = host.Queue.CreateDbContext())
            {
                var afterFault = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == started.Id);
                Assert.Equal(started.RowVersion, afterFault.RowVersion);
                Assert.Null((await queue.Set<Cp6DeadLetterRecord>().SingleAsync(x => x.TenantId == First &&
                    x.MessageId == started.MessageId)).ReplayedAtUtc);
            }
            await host.ChangeDbAsync(db => db.Database.ExecuteSqlRawAsync("DELETE FROM public.fixture_intops_fault"));

            // 请求真实提交后由 HTTP 夹具丢回包，后续从数据库回查同一个 operation。
            await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
            {
                using var _ = await host.OpsPost(result, resultInput, drop: true);
            });
            var lost = await host.OpsJson<ErpIntegrationOpsV1.OperationLookup>(HttpMethod.Get,
                Ops + "/replays/" + resultInput.ReplayOperationId);
            Assert.Equal("Scheduled", lost.Status);
            Assert.Equal(1, (await host.CountOps()).DeliveryAudits);
            var retried = await Task.WhenAll(host.OpsPost(request, requestInput), host.OpsPost(request, requestInput));
            using var first = retried[0];
            using var second = retried[1];
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Accepted }, retried.Select(r => r.StatusCode).Order().ToArray());
            var requestReceipt = await first.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>();
            Assert.NotNull(requestReceipt);
            Assert.Equal(requestReceipt, await second.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>());
            using var resumed = await host.OpsPost(bridge, bridgeInput);
            Assert.Equal(HttpStatusCode.Accepted, resumed.StatusCode);
            Assert.EndsWith("/replays/" + bridgeInput.ReplayOperationId, resumed.Headers.Location!.ToString(), StringComparison.Ordinal);
            var resumeReceipt = await resumed.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>();
            Assert.Null(resumeReceipt!.OriginalMessageRef);
            Assert.NotEqual(resumeReceipt.NativeVersionBefore, resumeReceipt.NativeVersionAfter);

            using (var pending = await host.OpsPost(blockedRequest, notSupported))
            {
                Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
                var receipt = await pending.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>();
                Assert.Equal("BLOCKED_WAITING_AUTHORITY", receipt!.SafeCode);
                Assert.Null(receipt.ScheduledAttemptRef);
            }
            using (var scheduled = await host.OpsPost(competitor, await host.OpsInput(competitor)))
                Assert.Equal(HttpStatusCode.Accepted, scheduled.StatusCode);
            await host.CompeteBridgeWorkersAsync(competingKey);
            Assert.Equal(1, await host.BridgeEffectCountAsync());

            var oldInput = new ErpReplayRequest(Guid.NewGuid(), legacyResult.RowVersion,
                legacyResult.PayloadSha256, "dependency-recovered");
            var oldUrl = DeliveryReplayPath + "/results/" + legacyResult.Id.ToString("D") + "/replay";
            using var oldSchedule = await host.OpsRequest(HttpMethod.Post, oldUrl, oldInput);
            Assert.Equal(HttpStatusCode.Accepted, oldSchedule.StatusCode);
            var oldReceipt = await oldSchedule.Content.ReadFromJsonAsync<ErpDeliveryReplayScheduled>();
            Assert.Equal(legacyResult.MessageId, oldReceipt!.MessageId);

            var originalCounts = new OpsCounts(1, 4, 6, 2, 3);
            Assert.Equal(originalCounts, await host.CountOps());
            var audit = await host.OpsJson<JsonElement>(HttpMethod.Get, Ops + "/replays/" + bridgeInput.ReplayOperationId + "/audit");
            Assert.Equal(Resume, audit.GetProperty("allowedAction").GetString());
            Assert.Equal("c03-bridge-key-v1", audit.GetProperty("expectedDigest").GetProperty("codecVersion").GetString());
            Assert.Equal(JsonValueKind.Null, audit.GetProperty("originalActor").ValueKind);
            foreach (var input in new[] { requestInput, resultInput, bridgeInput })
            {
                var evidence = await host.OpsJson<JsonElement>(HttpMethod.Get, Ops + "/replays/" + input.ReplayOperationId + "/audit");
                Assert.Equal(input.NativeQueueRef, evidence.GetProperty("nativeQueueRef").Deserialize<IntegrationQueueRefV1>(ErpEventContracts.Json));
                Assert.Equal(input.ExpectedDigest, evidence.GetProperty("expectedDigest").Deserialize<IntegrationDigestV1>(ErpEventContracts.Json));
            }
            var originalResult = await host.OpsJson<JsonElement>(HttpMethod.Get,
                Ops + "/owner-results/" + resultDetail.OriginalRequestRef!.RequestId);
            Assert.Equal("APPLIED", originalResult.GetProperty("outcome").GetString());
            Assert.Equal(started.PayloadSha256, originalResult.GetProperty("resultDigest").GetProperty("value").GetString());
            var missingResult = await host.OpsJson<JsonElement>(HttpMethod.Get, Ops + "/owner-results/" + Guid.NewGuid());
            Assert.Equal("NOT_OBSERVED", missingResult.GetProperty("outcome").GetString());

            await host.RevokeOpsAsync("deliver", "replay");
            clock.Advance(TimeSpan.FromMinutes(3));
            using (var conflict = await host.OpsPost(request, requestInput with { ReasonCode = "contract-verified" }))
                await ExpectOpsError(conflict, HttpStatusCode.Conflict, "REPLAY_OPERATION_CONFLICT");
            using (var newIntent = await host.OpsPost(result, resultInput with { ReplayOperationId = Guid.NewGuid() }))
                await ExpectOpsError(newIntent, HttpStatusCode.Forbidden, "INTEGRATION_ACTION_DENIED");
            foreach (var (row, input, receipt) in new[]
            {
                (request, requestInput, requestReceipt), (result, resultInput, lost.Receipt), (bridge, bridgeInput, resumeReceipt)
            })
            {
                using var repeated = await host.OpsPost(row, input);
                Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
                Assert.Equal(receipt, await repeated.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>());
            }
            using var legacyAgain = await host.OpsRequest(HttpMethod.Post, oldUrl, oldInput);
            Assert.Equal(HttpStatusCode.Accepted, legacyAgain.StatusCode);
            Assert.Equal(oldReceipt, await legacyAgain.Content.ReadFromJsonAsync<ErpDeliveryReplayScheduled>());
            Assert.Equal(originalCounts, await host.CountOps());
            await host.RunOpsWorkers(command, orderKey);
            await host.DispatchOpsResultsAsync(started, legacyResult, clock);
            Assert.Equal(originalCounts, await host.CountOps());
            Assert.Equal(1, await host.BridgeEffectCountAsync());
            await using (var queue = host.Queue.CreateDbContext())
            {
                var same = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == started.Id);
                Assert.Equal(started.Payload, same.Payload);
                Assert.Equal(started.AggregateVersion, same.AggregateVersion);
                Assert.Equal(started.PayloadSha256, same.PayloadSha256);
                Assert.Equal("BLOCKED_WAITING_AUTHORITY", (await queue.Inbox.SingleAsync(x => x.MessageId == command.MessageId)).ErrorCode);
                Assert.Equal("BLOCKED_WAITING_AUTHORITY", (await queue.OrderBridges.SingleAsync(x => x.TenantId == First &&
                    x.OrderKey == orderKey)).LastErrorCode);
                Assert.Equal(Cp6OutboxStatus.Published, same.Status);
            }

            if (Environment.GetEnvironmentVariable("CP6_INTOPS_BROWSER_DIST") is { Length: > 0 } dist)
                opsOutput.WriteLine(await host.RunOpsBrowserAsync(dist, resultInput.ReplayOperationId));
            // 重建实际 HTTP 宿主，原队列和审计从同一 PostgreSQL 恢复。
            await host.App.StopAsync();
            await using var restarted = await Fixture.StartPostgreSqlAsync(connectionString, Configure);
            using var recovered = await restarted.OpsPost(result, resultInput);
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            Assert.Equal(lost.Receipt, await recovered.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>());
            var completed = await restarted.OpsJson<ErpIntegrationOpsV1.OperationLookup>(HttpMethod.Get,
                Ops + "/replays/" + resultInput.ReplayOperationId);
            Assert.Equal("Completed", completed.CurrentExecution);
            Assert.Equal(lost.Receipt, completed.Receipt);
            await restarted.MaskOpsFieldsAsync();
            using var masked = await restarted.OpsPost(result, resultInput);
            Assert.Equal(HttpStatusCode.OK, masked.StatusCode);
            var projected = await masked.Content.ReadFromJsonAsync<IntegrationReplayReceiptV1>();
            Assert.Equal(lost.Receipt!.ReplayOperationId, projected!.ReplayOperationId);
            Assert.Null(projected.AcceptedBy);
            Assert.Null(projected.ExpectedDigest);
            Assert.Null(projected.NativeQueueRef);
            var maskedResult = await restarted.OpsJson<JsonElement>(HttpMethod.Get,
                Ops + "/owner-results/" + resultDetail.OriginalRequestRef.RequestId);
            Assert.Equal("APPLIED", maskedResult.GetProperty("outcome").GetString());
            Assert.Equal(JsonValueKind.Null, maskedResult.GetProperty("resultDigest").ValueKind);
            Assert.Equal(JsonValueKind.Null, maskedResult.GetProperty("originalResultNativeRef").ValueKind);
            await restarted.RevokeOpsAsync("query");
            using var noRead = await restarted.OpsPost(result, resultInput);
            Assert.Equal(HttpStatusCode.Forbidden, noRead.StatusCode);
            Assert.Equal(originalCounts, await restarted.CountOps());
            Assert.NotEmpty(logs.Messages);
            Assert.DoesNotContain(logs.Messages, line => line.Contains("alice@fixture.invalid", StringComparison.Ordinal));
            opsOutput.WriteLine(JsonSerializer.Serialize(new
            {
                originalCounts, rolledBack = baseline, unavailable = unobserved.Status,
                request = requestReceipt, result = lost.Receipt, bridge = resumeReceipt,
                outbox = new { started.MessageId, started.PayloadSha256, started.AggregateVersion, phase = "Published" },
                recoveredWithSameKey = projected.ReplayOperationId == resultInput.ReplayOperationId,
                newSchedulesOnRecovery = 0, newScheduleAuditsOnRecovery = 0, newBusinessEffectsOnRecovery = 0
            }, ErpEventContracts.Json));
        }
        finally { await postgres.DisposeAsync(); }
    }

    private static async Task CheckOpsPaging(Fixture host, SimulatedFailureSource alternate, SimulatedFailureSource missing, string id)
    {
        var first = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, Ops + "/failures?size=1");
        Assert.True(first.Partial);
        Assert.Equal(new[] { MissingOwner }, first.UnavailableOwners);
        Assert.Contains(Owner, first.SourceSet);
        Assert.Contains(SimulatedOwner, first.SourceSet);
        Assert.NotNull(first.Cursor);
        Assert.DoesNotContain(PrivateMarker, JsonSerializer.Serialize(first));
        var url = Ops + "/failures?cursor=" + Uri.EscapeDataString(first.Cursor);
        var repeat = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, url);
        Assert.Equal(first.SnapshotAt, repeat.SnapshotAt);
        var same = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, url);
        Assert.Equal(repeat.Items.Select(x => (x.OwnerId, x.NativeType, x.NativeId)),
            same.Items.Select(x => (x.OwnerId, x.NativeType, x.NativeId)));
        using (var changed = await host.OpsRequest(HttpMethod.Get, url + "&size=2"))
            await ExpectOpsError(changed, HttpStatusCode.BadRequest, "CURSOR_INVALID");
        missing.Available = true;
        var frozen = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, url);
        Assert.DoesNotContain(MissingOwner, frozen.SourceSet);
        Assert.True(frozen.Partial);
        var fresh = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, Ops + "/failures");
        Assert.False(fresh.Partial);
        Assert.Contains(MissingOwner, fresh.SourceSet);
        Assert.Equal(3, fresh.Items.Count(x => x.NativeId == id));
        alternate.ReadAllowed = false;
        var scoped = await host.OpsJson<IntegrationFailurePageV1>(HttpMethod.Get, Ops + "/failures");
        Assert.DoesNotContain(SimulatedOwner, scoped.SourceSet);
        Assert.DoesNotContain(scoped.Items, item => item.OwnerId == SimulatedOwner);
        alternate.ReadAllowed = true;
        alternate.Available = false;
        using var lost = await host.OpsRequest(HttpMethod.Get, url);
        await ExpectOpsError(lost, HttpStatusCode.ServiceUnavailable, "SOURCE_SET_UNAVAILABLE");
        missing.Available = false;
        using var unavailable = await host.OpsRequest(HttpMethod.Get, Ops + "/failures?owner=" + MissingOwner);
        await ExpectOpsError(unavailable, HttpStatusCode.ServiceUnavailable, "SOURCE_UNAVAILABLE");
        alternate.Available = true;
    }

    private static async Task ExpectOpsError(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain(PrivateMarker, text);
    }

    private sealed record OpsCounts(int InboxAudits, int DeliveryAudits, int Receipts, int Orders, int Requests);

    private sealed class OpsClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.Ticks / 10 * 10;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }

    private sealed class SimulatedFailureSource(string owner) : IIntegrationOpsSourceV1
    {
        public bool Available { get; set; } = true;
        public bool ReadAllowed { get; set; } = true;
        public string OwnerId => owner;
        public string RouteEpoch => "FIXTURE-A";
        public IntegrationFailureKeyV1? Key { get; set; }
        public Task<bool> CanReadAsync(Guid tenant, Guid actor, CancellationToken ct) =>
            Task.FromResult(ReadAllowed && tenant == First && actor == Operator);
        public Task<IReadOnlyList<IntegrationFailureRecordV1>?> ReadAsync(Guid tenant, Guid actor, IntegrationFailureWindowV1 window,
            IntegrationFailureKeyV1? after, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IntegrationFailureRecordV1>?>(!Available ? null :
                Key is { } key && (after is null || key.CompareTo(after) > 0)
                    ? [new(key, "DEAD_LETTERED", null, PrivateMarker, 2)] : []);
    }

    private sealed class CapturedOpsLog : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel < LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class SimulatedEffectOwner : IErpIntegrationEffectOwnerV1
    {
        public async Task<IntegrationEffectHoldV1?> EvaluateAndHoldAsync(CP6Context db, IntegrationEffectIntentV1 intent,
            bool executing, CancellationToken ct)
        {
            var allowed = await db.Database.SqlQuery<bool>($"""
                SELECT enabled AS "Value" FROM public.fixture_intops_gate
                WHERE tenant_id={intent.TenantId} AND native_id={intent.NativeQueueRef.NativeId}
                FOR SHARE
                """).ToListAsync(ct);
            return allowed.SingleOrDefault() ? new(db.Database.CurrentTransaction!.TransactionId, intent.RouteEpoch,
                "fixture-held-pg", "fixture-source-protection", "fixture-owner-qualification") : null;
        }

        public async Task<bool> StageBridgeAsync(CP6Context db, IntegrationEffectIntentV1 intent, CancellationToken ct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.fixture_intops_bridge_effect(replay_id,native_id)
                VALUES({intent.ReplayOperationId},{intent.NativeQueueRef.NativeId})
                """, ct);
            return true;
        }
    }

    private sealed class RecordedDelivery : ICp6OutboxPublisher
    {
        public List<Cp6OutboxDispatchMessage> Messages { get; } = [];
        public Task PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed partial class Fixture
    {
        private const int IntegrationRole = 19023;
        private static readonly string[] OpsActions = ["query", "evidence", "replay", "deliver", "audit"];

        public async Task SeedOpsAuthorityAsync()
        {
            await ChangeDbAsync(async db =>
            {
                var menu = await db.Sys_Menus.AsNoTracking().SingleAsync(x => x.MenuKey == "integration-ops");
                db.Sys_Roles.Add(new() { TenantId = First, RoleName = "模拟集成操作员", RoleId = IntegrationRole, Enable = true });
                db.Sys_Users.Add(new() { Id = Operator, TenantId = First, UserName = "fixture-ops", Password = "模拟凭据",
                    RoleId = IntegrationRole, Enable = true });
                foreach (var action in OpsActions)
                {
                    db.Sys_MenuActions.Add(new() { TenantId = First, MenuId = menu.MenuId, ActionCode = action, ActionName = action });
                    db.Sys_RoleActions.Add(new() { TenantId = First, RoleId = IntegrationRole, MenuId = menu.MenuId, ActionCode = action });
                }
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE public.fixture_intops_gate(tenant_id uuid, native_id text, enabled boolean, PRIMARY KEY(tenant_id,native_id));
                    CREATE TABLE public.fixture_intops_fault(operation_id uuid PRIMARY KEY);
                    CREATE TABLE public.fixture_intops_bridge_effect(replay_id uuid PRIMARY KEY, native_id text UNIQUE);
                    """);
            });
        }

        public async Task RevokeOpsAsync(params string[] actions) => await ChangeDbAsync(async db =>
        {
            await db.Sys_RoleActions.IgnoreQueryFilters().Where(r => r.TenantId == First && r.RoleId == IntegrationRole &&
                actions.Contains(r.ActionCode)).ExecuteDeleteAsync();
        });

        public Task MaskOpsFieldsAsync() => ChangeDbAsync(db =>
        {
            db.Sys_RoleFieldPerms.Add(new() { TenantId = First, RoleId = IntegrationRole, ResourceKey = "integration-ops",
                FieldName = "NativeEvidence", Access = 3 });
            return Task.CompletedTask;
        });

        private string OpsToken(Guid? tenant = null) => NativeToken("Admin", tenant: tenant, change: claims =>
            {
                claims.RemoveAll(c => c.Type == "sub");
                claims.Add(new Claim("sub", Operator.ToString("D")));
            });

        public async Task<HttpResponseMessage> OpsRequest(HttpMethod method, string path, object? input = null, bool drop = false,
            Guid? tenant = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new("Bearer", OpsToken(tenant));
            if (input is not null) request.Content = JsonContent.Create(input);
            if (drop) request.Headers.Add("x-fixture-drop-response", "1");
            return await http.SendAsync(request);
        }

        public async Task<T> OpsJson<T>(HttpMethod method, string path)
        {
            using var response = await OpsRequest(method, path);
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, "集成测试端点状态 " + response.StatusCode);
            Assert.DoesNotContain(PrivateMarker, content);
            return JsonSerializer.Deserialize<T>(content, ErpEventContracts.Json)!;
        }

        public async Task<IntegrationReplayRequestV1> OpsInput(IntegrationFailureV1 failure)
        {
            var eligibility = await OpsJson<IntegrationEligibilityV1>(HttpMethod.Post,
                Ops + "/failures/" + failure.FailureRef + "/eligibility");
            Assert.NotNull(eligibility.ReplayEvidence);
            return new(eligibility.ReplayOperationId!.Value, eligibility.ReplayEvidence.AllowedAction, eligibility.ReplayEvidence.NativeQueueRef,
                eligibility.NativeVersion!, eligibility.ReplayEvidence.ExpectedDigest, "dependency-recovered",
                eligibility.EligibilityRef!, Contract);
        }

        public Task<HttpResponseMessage> OpsPost(IntegrationFailureV1 failure, IntegrationReplayRequestV1 input, bool drop = false) =>
            OpsRequest(HttpMethod.Post, Ops + "/failures/" + failure.FailureRef + "/replays", input, drop);

        public async Task<Cp6OutboxEnvelope> SeedOpsInbox(DateTimeOffset at, Guid tenant, bool allow = true)
        {
            var account = Guid.NewGuid();
            var command = new ErpBusinessPartnerRequested(tenant, Guid.NewGuid(), account, 1);
            var envelope = ErpEventContracts.Create(tenant, account.ToString("D"), 1, ErpEventContracts.BusinessPartnerRequested,
                command, tenant == First ? "us" : "eu", Guid.NewGuid().ToString("N"), "fixture-pending-request");
            await using var queue = Queue.CreateDbContext();
            queue.Inbox.Add(new()
            {
                TenantId = tenant, MessageId = envelope.MessageId, AggregateId = account, AggregateVersion = 1,
                EventType = ErpEventContracts.BusinessPartnerRequested, ReceivedAtUtc = at.AddSeconds(-2), ProcessedAtUtc = at,
                Payload = envelope.Payload.ToArray(), PayloadSha256 = ErpEventContracts.Hash(envelope.Payload.Span),
                Status = ErpInboxStatus.DeadLettered, AttemptCount = 10, ErrorCode = PrivateMarker
            });
            await queue.SaveChangesAsync();
            if (allow) await AddOpsProofAsync(tenant, envelope.MessageId);
            return envelope;
        }

        private Task AddOpsProofAsync(Guid tenant, string key) => ChangeDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.fixture_intops_gate(tenant_id,native_id,enabled) VALUES({tenant},{key},true)
            """));

        public async Task<Cp6OutboxMessage> AddOpsCommittedResultAsync(TimeProvider clock)
        {
            var account = Guid.NewGuid();
            await AddPartner(account);
            await SubmitOriginal(account);
            return await ExhaustOpsResultAsync(clock);
        }

        public async Task<Cp6OutboxMessage> ExhaustOpsResultAsync(TimeProvider clock)
        {
            await using var queue = Queue.CreateDbContext();
            var runtime = App.Services.GetRequiredService<ErpIntegrationRuntime>();
            var store = new Cp6OutboxStore<ErpIntegrationContext>(queue, runtime.Validator, clock);
            var config = new Cp6TransactionalMessagingOptions { MaxOutboxAttempts = 1, DispatchBatchSize = 1 };
            var claim = Assert.Single(await store.ClaimBatchAsync("fixture-delivery", config));
            Assert.True(await store.MarkFailedAsync(claim, "C03_TRANSPORT_UNAVAILABLE", true, config));
            var result = await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == claim.Message.Id);
            Assert.Equal(Cp6OutboxStatus.DeadLettered, result.Status);
            return result;
        }

        public async Task AddOpsBridgeAsync(string key, TimeProvider clock, bool withResult)
        {
            var at = clock.GetUtcNow();
            await ChangeDbAsync(db =>
            {
                db.Orders.Add(new() { TenantId = First, WebOrderNo = key, OrderType = "10", CustomerCd = "fixture" });
                return Task.CompletedTask;
            });
            await using var queue = Queue.CreateDbContext();
            var account = Guid.NewGuid();
            var opportunity = Guid.NewGuid();
            var requestId = Guid.NewGuid();
            var result = new ErpOrderCreated(First, requestId, account, 4, opportunity, 7, key,
                "fixture-quote", "fixture-bp", 9007199254740.12345678m, "USD");
            queue.Requests.Add(new()
            {
                TenantId = First, Kind = ErpRequestKind.Order, AccountId = account, AggregateId = opportunity,
                RequestId = requestId, RequestVersion = 4, ResultVersion = 7, InputSha256 = new string('a', 64),
                Succeeded = true, Terminal = true, ResultType = ErpEventContracts.OrderCreated, UpdatedAtUtc = at,
                ResultDataJson = JsonSerializer.Serialize(result, ErpEventContracts.Json)
            });
            queue.OrderBridges.Add(new() { TenantId = First, OrderKey = key, AttemptCount = 10,
                LastErrorCode = "C03_BRIDGE_REPLAY_REQUIRED", AvailableAtUtc = at });
            if (withResult)
            {
                var runtime = App.Services.GetRequiredService<ErpIntegrationRuntime>();
                new Cp6OutboxStore<ErpIntegrationContext>(queue, runtime.Validator, clock).Enqueue(
                    ErpEventContracts.Create(First, opportunity.ToString("D"), 7, ErpEventContracts.OrderCreated, result,
                        "us", "fixture-order-result", "fixture-original-request", clock));
            }
            await queue.SaveChangesAsync();
            await AddOpsProofAsync(First, key);
        }

        public async Task OpsLeaseAsync(string key, DateTimeOffset? expires)
        {
            await using var queue = Queue.CreateDbContext();
            var bridge = await queue.OrderBridges.SingleAsync(x => x.TenantId == First && x.OrderKey == key);
            bridge.LeaseOwner = expires is null ? null : "模拟工作进程";
            bridge.LeaseExpiresAtUtc = expires;
            await queue.SaveChangesAsync();
        }

        public Task InstallOpsFaultAsync(Guid operation) => ChangeDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.fixture_intops_fault VALUES({operation})");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION public.fixture_intops_audit_fault() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS(SELECT FROM public.fixture_intops_fault WHERE operation_id=NEW.operation_id) THEN
                        RAISE EXCEPTION 'fixture_intops_audit_failure' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER fixture_intops_audit_fault BEFORE INSERT ON cp6_main_owner_v1.operations
                    FOR EACH ROW EXECUTE FUNCTION public.fixture_intops_audit_fault();
                """);
        });

        public async Task<OpsCounts> CountOps()
        {
            await using var queue = Queue.CreateDbContext();
            await using var db = new CP6Context(dbOptions, new CP6.Core.Services.Common.TenantContext { CurrentTenantId = First });
            return new(await queue.ReplayAudits.CountAsync(x => x.TenantId == First),
                await queue.Set<ErpDeliveryReplayAudit>().CountAsync(x => x.TenantId == First),
                await db.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations
                    WHERE tenant_id={First} AND owner={ErpIntegrationOpsV1.Journal}
                    """).SingleAsync(), await db.Orders.CountAsync(), await queue.Requests.CountAsync(x => x.TenantId == First));
        }

        public async Task RunOpsWorkers(Cp6OutboxEnvelope command, string orderKey)
        {
            await SendOriginal(command, command.Payload.ToArray());
            await CompeteBridgeWorkersAsync(orderKey);
        }

        public Task CompeteBridgeWorkersAsync(string orderKey)
        {
            var worker = new ErpOrderBridgeWorker(Queue, App.Services.GetRequiredService<IServiceScopeFactory>(),
                App.Services.GetRequiredService<ErpIntegrationRuntime>(), NullLogger<ErpOrderBridgeWorker>.Instance);
            return Task.WhenAll(worker.DispatchOneAsync(First, orderKey, default), worker.DispatchOneAsync(First, orderKey, default));
        }

        public async Task<int> BridgeEffectCountAsync()
        {
            await using var queue = Queue.CreateDbContext();
            return await queue.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM public.fixture_intops_bridge_effect").SingleAsync();
        }

        public async Task DispatchOpsResultsAsync(Cp6OutboxMessage order, Cp6OutboxMessage legacy, TimeProvider clock)
        {
            var runtime = App.Services.GetRequiredService<ErpIntegrationRuntime>();
            var publisher = new RecordedDelivery();
            var worker = new Cp6OutboxDispatcher<ErpIntegrationContext>(Queue, runtime.Validator, publisher,
                new() { DispatchBatchSize = 8 }, clock);
            await worker.DispatchBatchAsync("fixture-outbox-delivery");
            Assert.Equal(2, publisher.Messages.Count);
            foreach (var expected in new[] { order, legacy })
            {
                var sent = Assert.Single(publisher.Messages, x => x.MessageId == expected.MessageId);
                Assert.Equal(expected.Payload, sent.Payload.ToArray());
                Assert.Equal(expected.AggregateVersion, sent.AggregateVersion);
            }
            using var document = JsonDocument.Parse(order.Payload);
            Assert.Equal(9007199254740.12345678m, document.RootElement.GetProperty("data").GetProperty("bookedAmount").GetDecimal());
        }

        public async Task<string> RunOpsBrowserAsync(string dist, Guid operation)
        {
            var root = Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!;
            var node = Environment.GetEnvironmentVariable("CP6_INTOPS_BROWSER_NODE")!;
            var script = Environment.GetEnvironmentVariable("CP6_INTOPS_BROWSER_SCRIPT")!;
            var start = new ProcessStartInfo(node)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(script);
            using var browser = Process.Start(start)!;
            await browser.StandardInput.WriteAsync(JsonSerializer.Serialize(new
            {
                api = http.BaseAddress!.ToString(), dist, evidence = root, bearer = OpsToken(), operation
            }));
            browser.StandardInput.Close();
            var output = browser.StandardOutput.ReadToEndAsync();
            var errors = browser.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await browser.WaitForExitAsync(timeout.Token); }
            finally
            {
                if (!browser.HasExited) { browser.Kill(entireProcessTree: true); await browser.WaitForExitAsync(); }
            }
            Assert.True(browser.ExitCode == 0, (await errors) + "\n" + (await output));
            using var proof = JsonDocument.Parse(await output);
            Assert.True(proof.RootElement.GetProperty("passed").GetBoolean());
            return proof.RootElement.GetRawText();
        }
    }
}
