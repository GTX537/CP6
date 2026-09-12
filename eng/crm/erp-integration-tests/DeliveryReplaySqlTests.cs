using System.Text;
using System.Text.Json;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class DeliveryReplaySqlTests(SqlDatabaseFixture database)
{
    private const string Actor = "erp-delivery-operator";
    private static readonly Cp6TransactionalMessagingOptions TransportOptions = new()
    {
        DispatchBatchSize = 1000, MaxOutboxAttempts = 10, OutboxLeaseDuration = TimeSpan.FromMinutes(5),
        InitialOutboxRetryDelay = TimeSpan.FromSeconds(1), MaximumOutboxRetryDelay = TimeSpan.FromSeconds(8)
    };

    [Fact]
    public async Task Exhausted_result_requeues_original_event_and_audits_one_operation_without_recreating_order()
    {
        var (s, original) = await ExhaustedResultAsync();
        var input = Input(original.RowVersion, original.PayloadSha256);
        var service = new ErpDeliveryReplayService(database, s.Runtime);
        var scheduled = await service.ScheduleResultAsync(s.Tenant, original.Id, input, Actor);
        Assert.Equal(scheduled, await service.ScheduleResultAsync(s.Tenant, original.Id, input, Actor));
        Assert.Equal(original.MessageId, scheduled.MessageId);
        Assert.Equal(original.Id.ToString("D"), scheduled.TargetId);
        Assert.Equal("result", scheduled.Kind);
        var reset = await ResultAsync(s, original.Id);
        Assert.Equal(Cp6OutboxStatus.Pending, reset.Status);
        Assert.Equal(0, reset.AttemptCount);
        Assert.Equal(s.Clock.GetUtcNow(), reset.AvailableAtUtc);
        Assert.Equal(original.MessageId, reset.MessageId);
        Assert.Equal(original.Payload, reset.Payload);
        Assert.Equal(original.PayloadSha256, reset.PayloadSha256);
        Assert.Equal(original.TenantId, reset.TenantId);
        Assert.Equal(original.TopicName, reset.TopicName);
        Assert.Equal(original.PartitionKey, reset.PartitionKey);
        Assert.Equal(original.AggregateId, reset.AggregateId);
        Assert.Equal(original.AggregateVersion, reset.AggregateVersion);
        Assert.Equal(original.CorrelationId, reset.CorrelationId);
        Assert.Equal(original.CausationId, reset.CausationId);
        Assert.Null(reset.DeadLetteredAtUtc);
        Assert.Null(reset.LastErrorCode);
        Assert.Null(reset.LeaseOwner);
        Assert.Null(reset.LeaseToken);
        Assert.Null(reset.LeaseExpiresAtUtc);
        await using (var queue = s.Queue())
        {
            var audit = await queue.Set<ErpDeliveryReplayAudit>().SingleAsync(x => x.TenantId == s.Tenant);
            Assert.Equal(input.OperationId, audit.OperationId);
            Assert.Equal("result", audit.Kind);
            Assert.Equal(original.Id.ToString("D"), audit.TargetId);
            Assert.Equal(original.MessageId, audit.MessageId);
            Assert.Equal(original.PayloadSha256, audit.PayloadSha256);
            Assert.Equal(original.RowVersion, audit.InputRowVersion);
            Assert.Equal(Actor, audit.ActorId);
            Assert.Equal(input.ReasonCode, audit.ReasonCode);
            Assert.Equal(10, audit.PreviousAttemptCount);
            var dead = await queue.Set<Cp6DeadLetterRecord>().SingleAsync(x => x.MessageId == original.MessageId);
            Assert.Equal(scheduled.ReplayedAtUtc, dead.ReplayedAtUtc);
            Assert.Equal(input.ReasonCode, dead.ReplayReasonCode);
        }
        // Real Platform claim/publish state transition proves the original row is dispatchable again.
        await using (var queue = s.Queue())
        {
            var store = new Cp6OutboxStore<ErpIntegrationContext>(queue, s.Runtime.Validator, s.Clock);
            var claim = Assert.Single(await store.ClaimBatchAsync("c03-recovered-result", TransportOptions),
                x => x.Message.Id == original.Id);
            await store.MarkPublishedAsync(claim);
        }
        Assert.Equal(scheduled, await service.ScheduleResultAsync(s.Tenant, original.Id, input, Actor));
        Assert.Equal(Cp6OutboxStatus.Published, (await ResultAsync(s, original.Id)).Status);
        await AssertSingleOrderAsync(s);
    }

    [Fact]
    public async Task Exhausted_bridge_requeues_only_original_committed_order_and_audits_identity()
    {
        var (s, bridge) = await ExhaustedBridgeAsync();
        var hash = BridgeHash(s.Tenant, bridge.OrderKey);
        var input = Input(bridge.RowVersion, hash);
        var service = new ErpDeliveryReplayService(database, s.Runtime);
        var scheduled = await service.ScheduleBridgeAsync(s.Tenant, bridge.OrderKey, input, Actor);
        Assert.Equal(scheduled, await service.ScheduleBridgeAsync(s.Tenant, bridge.OrderKey, input, Actor));
        Assert.Equal("bridge", scheduled.Kind);
        Assert.Equal(bridge.OrderKey, scheduled.TargetId);
        Assert.Null(scheduled.MessageId);
        await using var queue = s.Queue();
        var reset = await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Equal(bridge.OrderKey, reset.OrderKey);
        Assert.Equal(0, reset.AttemptCount);
        Assert.Equal(s.Clock.GetUtcNow(), reset.AvailableAtUtc);
        Assert.Null(reset.CompletedAtUtc);
        Assert.Null(reset.LastErrorCode);
        Assert.Null(reset.LeaseOwner);
        Assert.Null(reset.LeaseExpiresAtUtc);
        var audit = await queue.Set<ErpDeliveryReplayAudit>().SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Equal("bridge", audit.Kind);
        Assert.Equal(hash, audit.PayloadSha256);
        Assert.Equal(bridge.RowVersion, audit.InputRowVersion);
        Assert.Equal(10, audit.PreviousAttemptCount);
        Assert.Equal(Actor, audit.ActorId);
        await AssertSingleOrderAsync(s);
        // Dispatch belongs to ErpOrderBridgeWorker after this transaction, never to the administrative command.
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Theory]
    [InlineData("result", "cross-tenant", "C03_DELIVERY_REPLAY_NOT_FOUND")]
    [InlineData("bridge", "cross-tenant", "C03_DELIVERY_REPLAY_NOT_FOUND")]
    [InlineData("result", "hash", "C03_DELIVERY_REPLAY_PRECONDITION_FAILED")]
    [InlineData("bridge", "hash", "C03_DELIVERY_REPLAY_PRECONDITION_FAILED")]
    [InlineData("result", "rowversion", "C03_DELIVERY_REPLAY_PRECONDITION_FAILED")]
    [InlineData("bridge", "rowversion", "C03_DELIVERY_REPLAY_PRECONDITION_FAILED")]
    [InlineData("result", "reason", "C03_DELIVERY_REPLAY_INPUT_INVALID")]
    [InlineData("bridge", "reason", "C03_DELIVERY_REPLAY_INPUT_INVALID")]
    [InlineData("result", "disabled", "C03_DELIVERY_REPLAY_TENANT_DISABLED")]
    [InlineData("bridge", "disabled", "C03_DELIVERY_REPLAY_TENANT_DISABLED")]
    public async Task Invalid_replay_does_not_mutate_original_or_append_audit(string kind, string fault, string expected)
    {
        var (s, result, bridge) = await ExhaustedBothAsync();
        var input = Input(kind == "result" ? result.RowVersion : bridge.RowVersion,
            kind == "result" ? result.PayloadSha256 : BridgeHash(s.Tenant, bridge.OrderKey));
        if (fault == "hash") input = input with { PayloadSha256 = new string('0', 64) };
        if (fault == "rowversion") input = input with { RowVersion = new byte[8] };
        if (fault == "reason") input = input with { ReasonCode = "override-payload" };
        if (fault == "disabled")
        {
            await using var business = s.Db();
            (await business.Sys_Tenants.SingleAsync(x => x.Id == s.Tenant)).Enable = false;
            await business.SaveChangesAsync();
        }
        var tenant = fault == "cross-tenant" ? s.OtherTenant : s.Tenant;
        var service = new ErpDeliveryReplayService(database, s.Runtime);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => kind == "result"
            ? service.ScheduleResultAsync(tenant, result.Id, input, Actor)
            : service.ScheduleBridgeAsync(tenant, bridge.OrderKey, input, Actor));
        Assert.Equal(expected, error.Code);
        Assert.Equal(result.RowVersion, (await ResultAsync(s, result.Id)).RowVersion);
        await using var queue = s.Queue();
        Assert.Equal(bridge.RowVersion, (await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant)).RowVersion);
        Assert.Empty(await queue.Set<ErpDeliveryReplayAudit>().Where(x => x.TenantId == s.Tenant || x.TenantId == s.OtherTenant).ToArrayAsync());
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("metadata")]
    [InlineData("deadletter-hash")]
    [InlineData("deadletter-tenant")]
    [InlineData("missing-deadletter")]
    public async Task Corrupt_result_or_unbound_deadletter_fails_closed(string fault)
    {
        var (s, original) = await ExhaustedResultAsync();
        await using (var queue = s.Queue())
        {
            var result = await queue.Set<Cp6OutboxMessage>().SingleAsync(x => x.Id == original.Id);
            var dead = await queue.Set<Cp6DeadLetterRecord>().SingleAsync(x => x.MessageId == original.MessageId);
            if (fault == "payload") queue.Entry(result).Property(x => x.Payload).CurrentValue = Encoding.UTF8.GetBytes("{}");
            if (fault == "metadata") queue.Entry(result).Property(x => x.PartitionKey).CurrentValue = "wrong-partition";
            if (fault == "deadletter-hash") queue.Entry(dead).Property(x => x.PayloadSha256).CurrentValue = new string('0', 64);
            if (fault == "deadletter-tenant") queue.Entry(dead).Property(x => x.TenantId).CurrentValue = s.OtherTenant;
            if (fault == "missing-deadletter") queue.Remove(dead);
            await queue.SaveChangesAsync();
        }
        var corrupt = await ResultAsync(s, original.Id);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => new ErpDeliveryReplayService(database, s.Runtime)
            .ScheduleResultAsync(s.Tenant, corrupt.Id, Input(corrupt.RowVersion, corrupt.PayloadSha256), Actor));
        Assert.Equal("C03_DELIVERY_REPLAY_EVIDENCE_INVALID", error.Code);
        Assert.Equal(corrupt.RowVersion, (await ResultAsync(s, original.Id)).RowVersion);
        await using var verify = s.Queue();
        Assert.Empty(await verify.Set<ErpDeliveryReplayAudit>().Where(x => x.TenantId == s.Tenant).ToArrayAsync());
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("not-exhausted")]
    [InlineData("active-lease")]
    [InlineData("missing-order")]
    public async Task Bridge_cannot_revive_completed_live_or_uncommitted_work(string fault)
    {
        var (s, bridge) = await ExhaustedBridgeAsync();
        await using (var queue = s.Queue())
        {
            var row = await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant);
            if (fault == "completed") row.CompletedAtUtc = s.Clock.GetUtcNow();
            if (fault == "not-exhausted") row.AttemptCount = 9;
            if (fault == "active-lease") { row.LeaseOwner = "active-worker"; row.LeaseExpiresAtUtc = s.Clock.GetUtcNow().AddMinutes(1); }
            if (fault == "missing-order")
            {
                queue.Remove(row);
                await queue.SaveChangesAsync();
                row = new() { TenantId = s.Tenant, OrderKey = "MISSING-ORDER", AttemptCount = 10,
                    AvailableAtUtc = s.Clock.GetUtcNow(), LastErrorCode = "C03_BRIDGE_REPLAY_REQUIRED" };
                queue.OrderBridges.Add(row);
            }
            await queue.SaveChangesAsync();
        }
        await using var verify = s.Queue();
        var current = await verify.OrderBridges.AsNoTracking().SingleAsync(x => x.TenantId == s.Tenant);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => new ErpDeliveryReplayService(database, s.Runtime)
            .ScheduleBridgeAsync(s.Tenant, current.OrderKey,
                Input(current.RowVersion, BridgeHash(s.Tenant, current.OrderKey)), Actor));
        Assert.Equal(fault == "missing-order" ? "C03_DELIVERY_REPLAY_EVIDENCE_INVALID" : "C03_DELIVERY_REPLAY_NOT_DEADLETTERED", error.Code);
        await verify.Entry(current).ReloadAsync();
        Assert.Empty(await verify.Set<ErpDeliveryReplayAudit>().Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        if (fault == "completed") Assert.NotNull(current.CompletedAtUtc);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("bridge")]
    public async Task Concurrent_same_operation_has_one_audit_and_changed_operation_input_cannot_reset_again(string kind)
    {
        var (s, result, bridge) = await ExhaustedBothAsync();
        var input = Input(kind == "result" ? result.RowVersion : bridge.RowVersion,
            kind == "result" ? result.PayloadSha256 : BridgeHash(s.Tenant, bridge.OrderKey));
        var service = new ErpDeliveryReplayService(database, s.Runtime);
        Task<ErpDeliveryReplayScheduled> Schedule(ErpReplayRequest value, string actor = Actor) => kind == "result"
            ? service.ScheduleResultAsync(s.Tenant, result.Id, value, actor)
            : service.ScheduleBridgeAsync(s.Tenant, bridge.OrderKey, value, actor);
        var scheduled = await Task.WhenAll(Schedule(input), Schedule(input));
        Assert.Equal(scheduled[0], scheduled[1]);
        var conflict = await Assert.ThrowsAsync<ErpCommerceException>(() => Schedule(input, "other-operator"));
        Assert.Equal("C03_DELIVERY_REPLAY_OPERATION_CONFLICT", conflict.Code);
        var stale = await Assert.ThrowsAsync<ErpCommerceException>(() => Schedule(input with { OperationId = Guid.NewGuid() }));
        Assert.Equal("C03_DELIVERY_REPLAY_PRECONDITION_FAILED", stale.Code);
        await using var queue = s.Queue();
        Assert.Single(await queue.Set<ErpDeliveryReplayAudit>().Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        await AssertSingleOrderAsync(s);
    }

    [Fact]
    public async Task List_is_tenant_scoped_bounded_stable_and_returns_only_recovery_metadata()
    {
        var (s, result, bridge) = await ExhaustedBothAsync();
        var (other, _) = await ExhaustedResultAsync();
        var service = new ErpDeliveryReplayService(database, s.Runtime);
        var first = await service.ListAsync(s.Tenant);
        Assert.Equal(2, first.Count);
        Assert.Contains(first, x => x.Kind == "result" && x.TargetId == result.Id.ToString("D") && x.PayloadSha256 == result.PayloadSha256);
        Assert.Contains(first, x => x.Kind == "bridge" && x.TargetId == bridge.OrderKey && x.PayloadSha256 == BridgeHash(s.Tenant, bridge.OrderKey));
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(await service.ListAsync(s.Tenant)));
        var json = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("\"Payload\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaseToken", json, StringComparison.Ordinal);
        Assert.DoesNotContain(other.Tenant.ToString("D"), json, StringComparison.Ordinal);
        Assert.True(first.Count <= 100);
        Assert.Empty(await service.ListAsync(s.OtherTenant));
    }

    [Theory]
    [InlineData("result")]
    [InlineData("bridge")]
    public async Task Audit_storage_failure_rolls_back_requeue_and_original_deadletter(string kind)
    {
        var (s, result, bridge) = await ExhaustedBothAsync();
        var input = Input(kind == "result" ? result.RowVersion : bridge.RowVersion,
            kind == "result" ? result.PayloadSha256 : BridgeHash(s.Tenant, bridge.OrderKey));
        await using var queue = s.Queue();
        // SQL DDL cannot parameterize a CHECK definition. The only interpolated value is a generated Guid.
        var faultDdl = $"ALTER TABLE erp_integration.DeliveryReplayAudit WITH NOCHECK ADD CONSTRAINT C03_DeliveryReplayAuditFault CHECK (TenantId <> '{s.Tenant:D}')";
        await queue.Database.ExecuteSqlRawAsync(faultDdl);
        try
        {
            var service = new ErpDeliveryReplayService(database, s.Runtime);
            await Assert.ThrowsAsync<DbUpdateException>(() => kind == "result"
                ? service.ScheduleResultAsync(s.Tenant, result.Id, input, Actor)
                : service.ScheduleBridgeAsync(s.Tenant, bridge.OrderKey, input, Actor));
        }
        finally { await queue.Database.ExecuteSqlRawAsync("ALTER TABLE erp_integration.DeliveryReplayAudit DROP CONSTRAINT C03_DeliveryReplayAuditFault"); }
        Assert.Equal(result.RowVersion, (await ResultAsync(s, result.Id)).RowVersion);
        Assert.Equal(bridge.RowVersion, (await queue.OrderBridges.AsNoTracking().SingleAsync(x => x.TenantId == s.Tenant)).RowVersion);
        Assert.Null((await queue.Set<Cp6DeadLetterRecord>().SingleAsync(x => x.MessageId == result.MessageId)).ReplayedAtUtc);
        Assert.Empty(await queue.Set<ErpDeliveryReplayAudit>().Where(x => x.TenantId == s.Tenant).ToArrayAsync());
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Delivery_replay_audit_is_append_only(string change)
    {
        var (s, result) = await ExhaustedResultAsync();
        await new ErpDeliveryReplayService(database, s.Runtime).ScheduleResultAsync(s.Tenant, result.Id,
            Input(result.RowVersion, result.PayloadSha256), Actor);
        await using var queue = s.Queue();
        var audit = await queue.Set<ErpDeliveryReplayAudit>().SingleAsync(x => x.TenantId == s.Tenant);
        if (change == "update") audit.ReasonCode = "contract-verified";
        else queue.Remove(audit);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => queue.SaveChangesAsync());
        Assert.Equal("C03_DELIVERY_REPLAY_AUDIT_APPEND_ONLY", error.Message);
    }

    private async Task<(ErpScenario Scenario, Cp6OutboxMessage Result)> ExhaustedResultAsync()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        Guid id;
        await using (var queue = s.Queue())
            id = await queue.Set<Cp6OutboxMessage>().Where(x => x.TenantId == s.Tenant && x.AggregateId == request.OpportunityId.ToString("D"))
                .Select(x => x.Id).SingleAsync();
        // Ten real Platform claim/failure transactions; transport itself is covered by the separate live runner.
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            await using var queue = s.Queue();
            var store = new Cp6OutboxStore<ErpIntegrationContext>(queue, s.Runtime.Validator, s.Clock);
            var claim = Assert.Single(await store.ClaimBatchAsync("c03-delivery-failure", TransportOptions), x => x.Message.Id == id);
            Assert.Equal(attempt, claim.Message.AttemptCount);
            Assert.Equal(attempt == 10, await store.MarkFailedAsync(claim, "C03_TRANSPORT_UNAVAILABLE", true, TransportOptions));
            s.Clock.Advance(TimeSpan.FromSeconds(8));
        }
        var result = await ResultAsync(s, id);
        Assert.Equal(Cp6OutboxStatus.DeadLettered, result.Status);
        return (s, result);
    }

    private async Task<(ErpScenario Scenario, ErpOrderBridgeDispatch Bridge)> ExhaustedBridgeAsync()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        await s.ConsumeAsync(s.Envelope(request));
        return (s, await SetBridgeExhaustedAsync(s));
    }

    private async Task<(ErpScenario Scenario, Cp6OutboxMessage Result, ErpOrderBridgeDispatch Bridge)> ExhaustedBothAsync()
    {
        var (s, result) = await ExhaustedResultAsync();
        return (s, result, await SetBridgeExhaustedAsync(s));
    }

    private static async Task<ErpOrderBridgeDispatch> SetBridgeExhaustedAsync(ErpScenario s)
    {
        await using var queue = s.Queue();
        var bridge = await queue.OrderBridges.SingleAsync(x => x.TenantId == s.Tenant);
        // Isolate administrative recovery from WMS/MES acceptance: seed only the worker's exhausted metadata.
        bridge.AttemptCount = 10; bridge.LastErrorCode = "C03_BRIDGE_REPLAY_REQUIRED";
        bridge.AvailableAtUtc = s.Clock.GetUtcNow();
        await queue.SaveChangesAsync();
        return bridge;
    }

    private static ErpReplayRequest Input(byte[] rowVersion, string hash) =>
        new(Guid.NewGuid(), rowVersion.ToArray(), hash, "dependency-recovered");

    private static string BridgeHash(Guid tenant, string key) =>
        ErpEventContracts.Hash(Encoding.UTF8.GetBytes($"c03:bridge:{tenant:D}:{key}"));

    private static async Task<Cp6OutboxMessage> ResultAsync(ErpScenario s, Guid id)
    {
        await using var queue = s.Queue();
        return await queue.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private static async Task AssertSingleOrderAsync(ErpScenario s)
    {
        await using var business = s.Db();
        Assert.Single(await business.Orders.ToArrayAsync());
        Assert.Single(await business.OrderDetails.ToArrayAsync());
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        await using var queue = s.Queue();
        Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
    }
}
