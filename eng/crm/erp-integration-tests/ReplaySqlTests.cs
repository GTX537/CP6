using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.ErpIntegration.SqlTests;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReplaySqlTests(SqlDatabaseFixture database)
{
    private async Task<(ErpScenario Scenario, ErpOrderRequested Request, Cp6OutboxEnvelope Envelope)> DeadLetterAsync()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync(maxInboxAttempts: 1);
        var request = await s.ReadyOrderAsync();
        var envelope = s.Envelope(request);
        var reachedSave = false;
        var fail = s.Handler((stage, _) =>
        {
            Assert.Equal("order-staged", stage);
            reachedSave = true;
            throw new TimeoutException("C03 SQL dependency unavailable");
        });
        var result = await fail.ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.True(reachedSave);
        Assert.Equal(Cp6InboxDisposition.DeadLettered, result.Disposition);
        await s.AssertNoOrderAsync();
        var receipt = await ReceiptAsync(s, envelope.MessageId);
        Assert.Equal(ErpInboxStatus.DeadLettered, receipt.Status);
        Assert.Equal(1, receipt.AttemptCount);
        // Ordinary delivery cannot bypass the administrative replay boundary.
        var blocked = await s.Handler().ConsumeAsync(envelope.Payload, envelope.TopicName, envelope.PartitionKey);
        Assert.Equal(Cp6InboxDisposition.DeadLettered, blocked.Disposition);
        return (s, request, envelope);
    }

    [Fact]
    public async Task Deadletter_replay_preserves_original_message_and_audits_one_operation_then_creates_one_order()
    {
        var (s, request, envelope) = await DeadLetterAsync();
        var original = await ReceiptAsync(s, envelope.MessageId);
        var input = new ErpReplayRequest(Guid.NewGuid(), original.RowVersion.ToArray(), original.PayloadSha256, "dependency-recovered");
        var replay = new ErpInboxReplayService(database, s.Runtime);
        s.Clock.Advance(TimeSpan.FromSeconds(3));
        var scheduled = await replay.ScheduleAsync(s.Tenant, envelope.MessageId, input, "erp-replay-operator");
        Assert.Equal(input.OperationId, scheduled.OperationId);
        Assert.Equal(envelope.MessageId, scheduled.MessageId);
        Assert.Equal(s.Clock.GetUtcNow(), scheduled.ReplayedAtUtc);
        Assert.Equal(scheduled, await replay.ScheduleAsync(s.Tenant, envelope.MessageId, input, "erp-replay-operator"));
        var reset = await ReceiptAsync(s, envelope.MessageId);
        Assert.Equal(ErpInboxStatus.Processing, reset.Status);
        Assert.Equal(0, reset.AttemptCount);
        Assert.Equal(s.Clock.GetUtcNow(), reset.RetryAtUtc);
        Assert.Equal(original.MessageId, reset.MessageId);
        Assert.Equal(original.Payload, reset.Payload);
        Assert.Equal(original.PayloadSha256, reset.PayloadSha256);
        await using (var queue = s.Queue())
        {
            var audit = await queue.ReplayAudits.SingleAsync(x => x.TenantId == s.Tenant);
            Assert.Equal(input.OperationId, audit.OperationId);
            Assert.Equal(envelope.MessageId, audit.MessageId);
            Assert.Equal(original.PayloadSha256, audit.PayloadSha256);
            Assert.Equal("erp-replay-operator", audit.ActorId);
            Assert.Equal("dependency-recovered", audit.ReasonCode);
            Assert.Equal(1, audit.PreviousAttemptCount);
            Assert.Equal(s.Clock.GetUtcNow(), audit.ReplayedAtUtc);
        }
        await s.ConsumeAsync(envelope);
        await s.ConsumeAsync(envelope);
        Assert.Equal(scheduled, await replay.ScheduleAsync(s.Tenant, envelope.MessageId, input, "erp-replay-operator"));
        await using (var db = s.Db())
        {
            var order = await db.Orders.SingleAsync();
            Assert.Equal(request.OpportunityId, order.CrmOpportunityId);
            Assert.Equal(request.RequestId, order.CrmRequestId);
            Assert.Equal(request.RequestVersion, order.CrmRequestVersion);
            Assert.Single(await db.OrderDetails.ToArrayAsync());
        }
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderFailed));
        await using (var queue = s.Queue())
        {
            Assert.Single(await queue.ReplayAudits.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
            Assert.Single(await queue.OrderBridges.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
            Assert.Equal(ErpInboxStatus.Processed, (await queue.Inbox.SingleAsync(x => x.MessageId == envelope.MessageId)).Status);
        }
        Assert.Equal(0, s.ExternalCalls.Count);
    }

    [Theory]
    [InlineData("cross-tenant", "C03_REPLAY_NOT_FOUND")]
    [InlineData("hash", "C03_REPLAY_PRECONDITION_FAILED")]
    [InlineData("stale-rowversion", "C03_REPLAY_PRECONDITION_FAILED")]
    [InlineData("invalid-reason", "C03_REPLAY_INPUT_INVALID")]
    [InlineData("disabled-tenant", "C03_REPLAY_TENANT_DISABLED")]
    [InlineData("expired-tenant", "C03_REPLAY_TENANT_DISABLED")]
    public async Task Rejected_replay_does_not_modify_deadletter_or_create_audit(string fault, string code)
    {
        var (s, _, envelope) = await DeadLetterAsync();
        var original = await ReceiptAsync(s, envelope.MessageId);
        var input = new ErpReplayRequest(Guid.NewGuid(), original.RowVersion.ToArray(), original.PayloadSha256, "dependency-recovered");
        if (fault == "hash") input = input with { PayloadSha256 = new string('0', 64) };
        if (fault == "stale-rowversion") input = input with { RowVersion = new byte[8] };
        if (fault == "invalid-reason") input = input with { ReasonCode = "arbitrary-override" };
        if (fault is "disabled-tenant" or "expired-tenant")
        {
            await using var business = s.Db();
            var tenant = await business.Sys_Tenants.SingleAsync(x => x.Id == s.Tenant);
            if (fault == "disabled-tenant") tenant.Enable = false;
            else tenant.ExpireDate = s.Clock.GetUtcNow().AddMinutes(-1).UtcDateTime;
            await business.SaveChangesAsync();
        }
        var replay = new ErpInboxReplayService(database, s.Runtime);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => replay.ScheduleAsync(
            fault == "cross-tenant" ? s.OtherTenant : s.Tenant, envelope.MessageId, input, "erp-replay-operator"));
        Assert.Equal(code, error.Code);
        var after = await ReceiptAsync(s, envelope.MessageId);
        Assert.Equal(original.RowVersion, after.RowVersion);
        Assert.Equal(original.Status, after.Status);
        Assert.Equal(original.AttemptCount, after.AttemptCount);
        Assert.Equal(original.Payload, after.Payload);
        Assert.Equal(original.PayloadSha256, after.PayloadSha256);
        await using var queue = s.Queue();
        Assert.Empty(await queue.ReplayAudits.Where(x => x.TenantId == s.Tenant || x.TenantId == s.OtherTenant).ToArrayAsync());
        await s.AssertNoOrderAsync();
    }

    [Fact]
    public async Task Processed_receipt_cannot_be_replayed_as_a_new_operation()
    {
        var s = new ErpScenario(database);
        await s.InitializeAsync();
        var request = await s.ReadyOrderAsync();
        var envelope = s.Envelope(request);
        await s.ConsumeAsync(envelope);
        var original = await ReceiptAsync(s, envelope.MessageId);
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => new ErpInboxReplayService(database, s.Runtime)
            .ScheduleAsync(s.Tenant, envelope.MessageId,
                new(Guid.NewGuid(), original.RowVersion.ToArray(), original.PayloadSha256, "contract-verified"), "erp-replay-operator"));
        Assert.Equal("C03_REPLAY_NOT_DEADLETTERED", error.Code);
        Assert.Equal(original.RowVersion, (await ReceiptAsync(s, envelope.MessageId)).RowVersion);
        await using var queue = s.Queue();
        Assert.Empty(await queue.ReplayAudits.Where(x => x.TenantId == s.Tenant).ToArrayAsync());
        Assert.Single(await s.ResultsAsync(ErpEventContracts.OrderCreated));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("actor")]
    [InlineData("hash")]
    [InlineData("rowversion")]
    public async Task Reused_replay_operation_with_changed_input_is_conflict_and_cannot_rewrite_audit(string changed)
    {
        var (s, _, envelope) = await DeadLetterAsync();
        var original = await ReceiptAsync(s, envelope.MessageId);
        var input = new ErpReplayRequest(Guid.NewGuid(), original.RowVersion.ToArray(), original.PayloadSha256, "dependency-recovered");
        var replay = new ErpInboxReplayService(database, s.Runtime);
        await replay.ScheduleAsync(s.Tenant, envelope.MessageId, input, "erp-replay-operator");
        var reset = await ReceiptAsync(s, envelope.MessageId);
        var conflict = changed switch
        {
            "reason" => input with { ReasonCode = "contract-verified" },
            "hash" => input with { PayloadSha256 = new string('0', 64) },
            "rowversion" => input with { RowVersion = new byte[8] },
            _ => input
        };
        var error = await Assert.ThrowsAsync<ErpCommerceException>(() => replay.ScheduleAsync(s.Tenant,
            envelope.MessageId, conflict, changed == "actor" ? "another-operator" : "erp-replay-operator"));
        Assert.Equal("C03_REPLAY_OPERATION_CONFLICT", error.Code);
        Assert.Equal(reset.RowVersion, (await ReceiptAsync(s, envelope.MessageId)).RowVersion);
        await using var queue = s.Queue();
        var audit = await queue.ReplayAudits.SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Equal("dependency-recovered", audit.ReasonCode);
        Assert.Equal("erp-replay-operator", audit.ActorId);
        await s.AssertNoOrderAsync();
    }

    private static async Task<ErpInboxReceipt> ReceiptAsync(ErpScenario s, string messageId)
    {
        await using var queue = s.Queue();
        return await queue.Inbox.AsNoTracking().SingleAsync(x => x.MessageId == messageId);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task Persisted_replay_audit_is_append_only(string mutation)
    {
        var (s, _, envelope) = await DeadLetterAsync();
        var receipt = await ReceiptAsync(s, envelope.MessageId);
        await new ErpInboxReplayService(database, s.Runtime).ScheduleAsync(s.Tenant, envelope.MessageId,
            new(Guid.NewGuid(), receipt.RowVersion.ToArray(), receipt.PayloadSha256, "dependency-recovered"), "erp-replay-operator");
        await using (var queue = s.Queue())
        {
            var audit = await queue.ReplayAudits.SingleAsync(x => x.TenantId == s.Tenant);
            if (mutation == "delete") queue.ReplayAudits.Remove(audit);
            else audit.ReasonCode = "contract-verified";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => queue.SaveChangesAsync());
            Assert.Equal("C03_REPLAY_AUDIT_APPEND_ONLY", error.Message);
        }
        await using var verify = s.Queue();
        var persisted = await verify.ReplayAudits.SingleAsync(x => x.TenantId == s.Tenant);
        Assert.Equal("dependency-recovered", persisted.ReasonCode);
        Assert.Equal("erp-replay-operator", persisted.ActorId);
        Assert.Equal(receipt.PayloadSha256, persisted.PayloadSha256);
        await s.AssertNoOrderAsync();
    }
}
