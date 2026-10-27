using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class DecisionTransport(ApprovalDecisionDeliveryRouteV2 route,
        Func<Guid, byte[], bool, CancellationToken, Task<ApprovalDecisionTransportResponseV2>> send) : IApprovalDecisionTransportV2
    {
        internal ApprovalDecisionDeliveryRouteV2 Current = route;
        internal int Queries, Posts;
        public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant) => tenant == Current.Tenant ? Current : null;
        public Task<ApprovalDecisionTransportResponseV2> SendAsync(ApprovalDecisionDeliveryRouteV2 r, Guid id, byte[] bytes, bool query, CancellationToken ct)
        { if (query) Queries++; else Posts++; return send(id, bytes, query, ct); }
    }
    private static ApprovalDecisionDeliveryRouteV2 DecisionRoute(Harness h, FactFixture f) => new(ScopeEnvironment, h.Tenant,
        f.Authority.Identity.Principal, f.Authority.Identity.Client, "https://workflow-issuer.example", "https://tool-owner.example", "fixture-1", "DEMO", ApprovalDecisionDeliveryV2.Consumer);
    private static async Task<ApprovalDecisionTransportResponseV2> DeliverDecision(FactFixture f, Guid id, byte[] bytes, bool query, CancellationToken ct)
    {
        try
        {
            var result = query ? await f.Owner.ReceivedAsync(id, ct) : await f.Owner.ReceiveAsync(bytes, ct);
            return new(result.Status, ApprovalCanonicalV2.Bytes(result.Body));
        }
        catch (ScopeAuthorizationExceptionV1 e) { return new(e.Status, []); }
    }
    private static Task DecisionDue(Harness h) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_approval_delivery_v2.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant}
        """);

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_real_receiver_lost_ack_queries_original_after_receive_revocation_without_resend()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var lose = true;
        var transport = new DecisionTransport(DecisionRoute(h, f), async (id, bytes, query, ct) => {
            Assert.Null(h.Db.Database.CurrentTransaction); Assert.Equal(f.Payload, bytes); Assert.Equal(f.Decision.EventId, id);
            var response = await DeliverDecision(f, id, bytes, query, ct);
            if (!query && lose) { lose = false; throw new HttpRequestException("lost committed receiver ACK"); } return response;
        });
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        f.Authority.Identity = f.Authority.Identity with { CanSubmit = false }; await DecisionDue(h);
        Assert.Equal("RECEIVED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); Assert.Equal(2, transport.Queries);
        var current = await ApprovalDecisionDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Decision.EventId, default);
        Assert.Equal("RECEIVED", current!.State); Assert.Equal("NOT_APPLIED", current.Receipt!.Value.GetProperty("application").GetString());
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); await FactCounts(h, 1, 1, 1);
        Assert.Equal(f.Payload, await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_approval_decision_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_two_connections_claim_once_and_late_epoch_cannot_replace_receipt()
    {
        await using var h = await Seed(); var f = await FactSetup(h);
        var transport = new DecisionTransport(DecisionRoute(h, f), (id, bytes, query, ct) => DeliverDecision(f, id, bytes, query, ct));
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = h with { Db = peerDb };
        var a = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport); var b = new ApprovalDecisionDeliveryV2(peerDb, ScopeFence(peer), transport);
        var claims = await Task.WhenAll(a.ClaimAsync(h.Tenant), b.ClaimAsync(h.Tenant)); var old = Assert.Single(claims, x => x != null)!;
        await DecisionDue(h); var fresh = (await b.ClaimAsync(h.Tenant))!; Assert.Equal(2, fresh.Epoch); Assert.NotEqual(old.Token, fresh.Token);
        var response = await DeliverDecision(f, f.Decision.EventId, f.Payload, false, default);
        var ack = ApprovalDecisionDeliveryV2.Receipt(response.Body, f.Decision);
        Assert.Equal("LEASE_LOST", await a.FinishAsync(old, "RECEIVED", null, ack));
        Assert.Equal("RECEIVED", await b.FinishAsync(fresh, "RECEIVED", null, ack));
        Assert.Equal("LEASE_LOST", await a.FinishAsync(old, "OUTCOME_UNKNOWN", "late timeout", null));
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_delivery_v2.results WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_approval_delivery_v2.attempts WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_approval_delivery_v2.results WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_forbidden_not_found_and_unavailable_queries_never_authorize_a_post()
    {
        foreach (var status in new[] { 403, 404, 503 })
        {
            await using var h = await Seed(); var f = await FactSetup(h); var allowed = false;
            var transport = new DecisionTransport(DecisionRoute(h, f), (id, bytes, query, ct) => allowed
                ? DeliverDecision(f, id, bytes, query, ct) : Task.FromResult(new ApprovalDecisionTransportResponseV2(status, [])));
            var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
            Assert.Equal(status == 503 ? "OUTCOME_UNKNOWN" : "BLOCKED", (await worker.RunAsync(h.Tenant)).State);
            Assert.Equal(0, transport.Posts); await FactCounts(h, 0, 0, 0); await DecisionDue(h); allowed = true;
            if (status != 503) { Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); transport.Current = transport.Current with { Version = "explicitly-restored" }; }
            Assert.Equal("RECEIVED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_pending_validation_polls_until_processed_without_reposting()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var pending = true;
        var transport = new DecisionTransport(DecisionRoute(h, f), async (id, bytes, query, ct) => {
            var response = await DeliverDecision(f, id, bytes, query, ct);
            if (!query && pending)
            {
                pending = false; var ack = JsonSerializer.Deserialize<ApprovalFactAckV2>(response.Body, ApprovalBindingOwnerV2.Json)!;
                return new(202, ApprovalCanonicalV2.Bytes(new { kind = "PENDING_VALIDATION", ack.EventId, ack.DecisionId, ack.SubmissionId,
                    ack.PayloadDigest, ack.InboxId, ack.ReceivedAt, queryRef = ToolApprovalFactReceiverV2.Route + "/" + id }));
            }
            return response;
        });
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("PENDING_VALIDATION", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal("PENDING_VALIDATION", (await ApprovalDecisionDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Decision.EventId, default))!.Receipt!.Value.GetProperty("kind").GetString());
        await DecisionDue(h); Assert.Equal("RECEIVED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); await FactCounts(h, 1, 1, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_unknown_sender_claim_and_receipt_commits_recover_without_duplicate_send()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await FactSetup(h);
        var transport = new DecisionTransport(DecisionRoute(h, f), async (id, bytes, query, ct) => {
            var response = await DeliverDecision(f, id, bytes, query, ct); if (!query) fault.Armed = true; return response;
        });
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport); fault.Armed = true;
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(0, transport.Queries); Assert.Equal(0, transport.Posts);
        await DecisionDue(h); Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        Assert.Equal("RECEIVED", (await ApprovalDecisionDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Decision.EventId, default))!.State);
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); await FactCounts(h, 1, 1, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_revoked_current_subject_prevents_confirmation_until_restored_read()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var revoke = true;
        var transport = new DecisionTransport(DecisionRoute(h, f), async (id, bytes, query, ct) => {
            var response = await DeliverDecision(f, id, bytes, query, ct);
            if (!query && revoke) { revoke = false; await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}", ct); }
            return response;
        });
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_approval_delivery_v2.results WHERE tenant_id={h.Tenant}").ToArrayAsync());
        await DecisionDue(h); Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=true,can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal("RECEIVED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_missing_receipt_and_late_authority_aba_cannot_commit_confirmation()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await FactSetup(h);
        var transport = new DecisionTransport(DecisionRoute(h, f), (id, bytes, query, ct) => DeliverDecision(f, id, bytes, query, ct));
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_approval_delivery_v2.results")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal("OUTCOME_UNKNOWN", (await ApprovalDecisionDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Decision.EventId, default))!.State);
        hook.Before = null; await DecisionDue(h);
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_approval_delivery_v2.results")) return; hook.Before = null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=false WHERE tenant_id={h.Tenant}");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_tool_approval_fact_v2.subject_grants SET can_query=true WHERE tenant_id={h.Tenant}");
        };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_approval_delivery_v2.results WHERE tenant_id={h.Tenant}").ToArrayAsync());
        hook.Before = null; await DecisionDue(h); Assert.Equal("RECEIVED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
    }

    [CurrentPermissionPostgresFact]
    public async Task Decision_delivery_original_cannot_move_destination_and_malformed_receipts_are_terminal_conflicts()
    {
        await using var h = await Seed(); var f = await FactSetup(h); var available = false;
        var transport = new DecisionTransport(DecisionRoute(h, f), (id, bytes, query, ct) => available
            ? Task.FromResult(new ApprovalDecisionTransportResponseV2(200, "{}"u8.ToArray())) : Task.FromResult(new ApprovalDecisionTransportResponseV2(503, [])));
        var worker = new ApprovalDecisionDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); await DecisionDue(h); available = true;
        var original = transport.Current; transport.Current = original with { Destination = "https://other.example", Version = "moved" };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Queries);
        transport.Current = original with { Version = "restored" };
        Assert.Equal("CONFLICT", (await worker.RunAsync(h.Tenant)).State); Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(0, transport.Posts);
        var ack = FactAck(await f.Owner.ReceiveAsync(f.Payload));
        foreach (var mutate in new Action<JsonObject>[] {
            n => n["eventId"] = Guid.NewGuid(), n => n["decisionId"] = Guid.NewGuid(), n => n["payloadDigest"]!["value"] = new string('0',64),
            n => n["receiptQueryRef"] = "https://unregistered.example/capture", n => n["unregistered"] = true })
        {
            var node = JsonSerializer.SerializeToNode(ack, ApprovalBindingOwnerV2.Json)!.AsObject(); mutate(node);
            Assert.Throws<ScopeAuthorizationExceptionV1>(() => ApprovalDecisionDeliveryV2.Receipt(ApprovalCanonicalV2.Bytes(node), f.Decision));
        }
    }
}
