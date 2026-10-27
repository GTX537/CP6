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
    private sealed class ApplicationReceiptTransport(ToolApprovalDeliveryRouteV1 route,
        Func<Guid, byte[], bool, CancellationToken, Task<ToolApprovalDeliveryResponseV1>> send) : IToolApplicationTransportV2
    {
        internal ToolApprovalDeliveryRouteV1 Current = route;
        internal int Queries, Posts;
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant) => tenant == Current.Tenant ? Current : null;
        public Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 r, Guid id, byte[] bytes, bool query, CancellationToken ct)
        { if (query) Queries++; else Posts++; return send(id, bytes, query, ct); }
    }
    private static ToolApprovalDeliveryRouteV1 ApplicationReceiptRoute(Harness h, ReceiptFixture f) => new(ScopeEnvironment, h.Tenant,
        f.Authority.Identity.Principal, f.Authority.Identity.Client, "https://workflow-issuer.example", "https://tool-owner.example", "fixture-1", "DEMO");
    private static async Task<ToolApprovalDeliveryResponseV1> DeliverApplicationReceipt(ReceiptFixture f, Guid id, byte[] bytes, bool query, CancellationToken ct)
    {
        try
        {
            var result = query ? await f.Owner.OriginalAsync(id, ct) : await f.Owner.ReceiveAsync(bytes, ct);
            return new(result.Status, ApprovalCanonicalV2.Bytes(result.Body));
        }
        catch (ScopeAuthorizationExceptionV1 e) { return new(e.Status, []); }
    }
    private static Task ApplicationReceiptDue(Harness h) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_tool_application_delivery_v2.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant} AND state NOT IN('RECORDED','CONFLICT')
        """);

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_real_receiver_lost_ack_queries_original_after_receive_revocation_without_resend()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var lose = true;
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), async (id, bytes, query, ct) => {
            Assert.Null(h.Db.Database.CurrentTransaction); Assert.Equal(ReceiptBytes(f.Receipts[0]), bytes); Assert.Equal(f.Receipts[0].ReceiptEventId, id);
            var response = await DeliverApplicationReceipt(f, id, bytes, query, ct);
            if (!query && lose) { lose = false; throw new HttpRequestException("lost committed receiver ACK"); } return response;
        });
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        f.Authority.Identity = f.Authority.Identity with { CanSubmit = false }; await ApplicationReceiptDue(h);
        Assert.Equal("RECORDED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); Assert.Equal(2, transport.Queries);
        var current = await ToolApplicationDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Receipts[0].ReceiptEventId, default);
        Assert.Equal("RECORDED", current!.State); Assert.Equal("RECORDED", current.Receipt!.Value.GetProperty("kind").GetString());
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); await ReceiptCounts(h, 1, 1);
        Assert.Equal(ReceiptBytes(f.Receipts[0]), await h.Db.Database.SqlQuery<byte[]>($"SELECT payload AS \"Value\" FROM cp6_tool_application_v2.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Empty(await h.Db.StockTransactions.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_two_connections_claim_once_and_late_epoch_cannot_replace_receipt()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1);
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), (id, bytes, query, ct) => DeliverApplicationReceipt(f, id, bytes, query, ct));
        await using var peerDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant }); var peer = h with { Db = peerDb };
        var a = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport); var b = new ToolApplicationDeliveryV2(peerDb, ScopeFence(peer), transport);
        var claims = await Task.WhenAll(a.ClaimAsync(h.Tenant), b.ClaimAsync(h.Tenant)); var old = Assert.Single(claims, x => x != null)!;
        await ApplicationReceiptDue(h); var fresh = (await b.ClaimAsync(h.Tenant))!; Assert.Equal(2, fresh.Epoch); Assert.NotEqual(old.Token, fresh.Token);
        var response = await DeliverApplicationReceipt(f, f.Receipts[0].ReceiptEventId, ReceiptBytes(f.Receipts[0]), false, default);
        var ack = ToolApplicationDeliveryV2.Receipt(response.Body, f.Receipts[0]);
        Assert.Equal("LEASE_LOST", await a.FinishAsync(old, "RECORDED", null, ack));
        Assert.Equal("RECORDED", await b.FinishAsync(fresh, "RECORDED", null, ack));
        Assert.Equal("LEASE_LOST", await a.FinishAsync(old, "OUTCOME_UNKNOWN", "late timeout", null));
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_application_delivery_v2.results WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_tool_application_delivery_v2.attempts WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tool_application_delivery_v2.results WHERE tenant_id={h.Tenant}"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_forbidden_not_found_and_unavailable_queries_never_authorize_a_post()
    {
        foreach (var status in new[] { 403, 404, 503 })
        {
            await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var allowed = false;
            var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), (id, bytes, query, ct) => allowed
                ? DeliverApplicationReceipt(f, id, bytes, query, ct) : Task.FromResult(new ToolApprovalDeliveryResponseV1(status, [])));
            var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
            Assert.Equal(status == 503 ? "OUTCOME_UNKNOWN" : "BLOCKED", (await worker.RunAsync(h.Tenant)).State);
            Assert.Equal(0, transport.Posts); await ReceiptCounts(h, 0, 0); await ApplicationReceiptDue(h); allowed = true;
            if (status != 503) { Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); transport.Current = transport.Current with { Version = "explicitly-restored" }; }
            Assert.Equal("RECORDED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_actual_pending_gap_only_polls_until_predecessor_is_recorded()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 2);
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), (id, bytes, query, ct) => DeliverApplicationReceipt(f, id, bytes, query, ct));
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
        var first = (await worker.ClaimAsync(h.Tenant))!; Assert.Equal(f.Receipts[0].ReceiptEventId, first.Original.Id);
        Assert.Equal("PENDING_GAP", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); await ReceiptCounts(h, 1, 0);
        var delivered = await DeliverApplicationReceipt(f, first.Original.Id, first.Original.Payload, false, default);
        Assert.Equal("RECORDED", await worker.FinishAsync(first, "RECORDED", null, ToolApplicationDeliveryV2.Receipt(delivered.Body, first.Envelope)));
        await ApplicationReceiptDue(h); Assert.Equal("RECORDED", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal(1, transport.Posts); await ReceiptCounts(h, 2, 2); await ApplicationCounts(h, 2);
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_unknown_sender_claim_and_receipt_commits_recover_without_duplicate_send()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await ReceiptSetup(h, 1);
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), async (id, bytes, query, ct) => {
            var response = await DeliverApplicationReceipt(f, id, bytes, query, ct); if (!query) fault.Armed = true; return response;
        });
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport); fault.Armed = true;
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(0, transport.Queries); Assert.Equal(0, transport.Posts);
        await ApplicationReceiptDue(h); Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
        Assert.Equal("RECORDED", (await ToolApplicationDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Receipts[0].ReceiptEventId, default))!.State);
        Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts); await ReceiptCounts(h, 1, 1);
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_revoked_current_subject_prevents_confirmation_until_restored_read()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var revoke = true;
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), async (id, bytes, query, ct) => {
            var response = await DeliverApplicationReceipt(f, id, bytes, query, ct);
            if (!query && revoke) { revoke = false; await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}", ct); }
            return response;
        });
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_tool_application_delivery_v2.results WHERE tenant_id={h.Tenant}").ToArrayAsync());
        await ApplicationReceiptDue(h); Assert.Equal("IDLE_OR_DISABLED", (await worker.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=true,can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant}");
        Assert.Equal("RECORDED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_missing_receipt_and_late_authority_aba_cannot_commit_confirmation()
    {
        var hook = new StartWriteHook(); await using var h = await Seed(hook); var f = await ReceiptSetup(h, 1);
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), (id, bytes, query, ct) => DeliverApplicationReceipt(f, id, bytes, query, ct));
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
        hook.Before = c => { if (c.CommandText.Contains("INSERT INTO cp6_tool_application_delivery_v2.results")) c.CommandText = "SELECT 1"; return Task.CompletedTask; };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Equal("OUTCOME_UNKNOWN", (await ToolApplicationDeliveryV2.ReadAsync(h.Db, ScopeEnvironment, f.Receipts[0].ReceiptEventId, default))!.State);
        hook.Before = null; await ApplicationReceiptDue(h);
        hook.Before = async c => {
            if (!c.CommandText.Contains("INSERT INTO cp6_tool_application_delivery_v2.results")) return; hook.Before = null;
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=false WHERE tenant_id={h.Tenant}");
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_approval_receipt_v2.subject_grants SET can_query=true WHERE tenant_id={h.Tenant}");
        };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State);
        Assert.Empty(await h.Db.Database.SqlQuery<long>($"SELECT epoch AS \"Value\" FROM cp6_tool_application_delivery_v2.results WHERE tenant_id={h.Tenant}").ToArrayAsync());
        hook.Before = null; await ApplicationReceiptDue(h); Assert.Equal("RECORDED", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Posts);
    }

    [CurrentPermissionPostgresFact]
    public async Task Application_receipt_delivery_pins_destination_and_rejects_mismatched_ingestion_receipts()
    {
        await using var h = await Seed(); var f = await ReceiptSetup(h, 1); var available = false; var e = f.Receipts[0];
        var transport = new ApplicationReceiptTransport(ApplicationReceiptRoute(h, f), (_, _, _, _) => Task.FromResult(
            available ? new ToolApprovalDeliveryResponseV1(200, "{}"u8.ToArray()) : new ToolApprovalDeliveryResponseV1(503, [])));
        var worker = new ToolApplicationDeliveryV2(h.Db, ScopeFence(h), transport);
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); await ApplicationReceiptDue(h); available = true;
        var original = transport.Current; transport.Current = original with { Destination = "https://different.example", Version = "changed" };
        Assert.Equal("OUTCOME_UNKNOWN", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(1, transport.Queries);
        transport.Current = original with { Version = "restored" }; Assert.Equal("CONFLICT", (await worker.RunAsync(h.Tenant)).State); Assert.Equal(0, transport.Posts);
        var recorded = (await f.Owner.ReceiveAsync(ReceiptBytes(e))).Body;
        foreach (var change in new Action<JsonObject>[] {
            n => n["receiptEventId"] = Guid.NewGuid(), n => n["receiptStreamId"] = Guid.NewGuid(), n => n["payloadDigest"]!["value"] = new string('0',64),
            n => n["queryRef"] = "https://unregistered.example", n => n["highestContinuousRevision"] = "01", n => n["projectionReceiptId"] = Guid.NewGuid(), n => n["extra"] = true })
        {
            var json = JsonNode.Parse(ApprovalCanonicalV2.Bytes(recorded))!.AsObject(); change(json);
            Assert.Equal(409, Assert.Throws<ScopeAuthorizationExceptionV1>(() => ToolApplicationDeliveryV2.Receipt(ApprovalCanonicalV2.Bytes(json), e)).Status);
        }
        var invalidGap = new ApprovalReceiptPendingV2("PENDING_GAP", e.ReceiptEventId, e.ReceiptStreamId, e.ReceiptId, "1", e.PayloadDigest, "0", "1", ApprovalReceiptIngestionV2.QueryRoute + e.ReceiptEventId);
        Assert.Throws<ScopeAuthorizationExceptionV1>(() => ToolApplicationDeliveryV2.Receipt(ApprovalCanonicalV2.Bytes(invalidGap), e));
    }
}
