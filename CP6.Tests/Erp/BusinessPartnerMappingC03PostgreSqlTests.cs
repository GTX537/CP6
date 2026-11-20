using System.Data;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task WriteUseGuard(Harness h, MappingOwnerFixture bridge, Guid account, long revision, string semantic, bool active)
    {
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await BpUseScopeGate.LockAsync(h.Db, MappingLegal, true, default);
        var authority = BusinessPartnerWorkspaceV1.MappingAuthorityHash(bridge.Binding("ACCOUNT_IDENTITY"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.mapping_use_guards(tenant_id,legal_entity_key,account_id,revision,semantic_hash,account_business_version,authority_hash,active)
            VALUES({h.Tenant},{MappingLegal},{account},{revision},{semantic},{"9007199254740993"},{authority},{active})
            ON CONFLICT (tenant_id,legal_entity_key,account_id) DO UPDATE
            SET revision=EXCLUDED.revision,semantic_hash=EXCLUDED.semantic_hash,account_business_version=EXCLUDED.account_business_version,
                authority_hash=EXCLUDED.authority_hash,active=EXCLUDED.active
            """);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_03_actual_C03_and_I02_hold_authoritative_claim_and_recover_original_after_replace()
    {
        await using var h = await Seed();
        await using var bridge = await MappingOwnerFixture.Start(h.Tenant);
        await EnableMapping(h, bridge);
        var account = Guid.NewGuid();
        bridge.Account(account, "00004103");
        await using var main = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, bridge.Environment());
        var cookies = await Login(main, h);
        var first = await AdoptMappingPartner(main, cookies, "000041A", "模拟订单原关系", "REGISTERED");
        var second = await AdoptMappingPartner(main, cookies, "000041B", "模拟订单新关系", "REGISTERED");
        var guard = await SignedAccountObservation(main, cookies, account);
        var context = await ReadMappingContext(main, cookies, account, guard, MappingCriteria("000041A", "模拟订单原关系"));
        var firstReceipt = MappingReceipt(await ApplyMap(main, cookies, await GoodMapPreview(main, cookies,
            MappingInput(context, "LINK", first, "模拟订单首版关系")), Guid.NewGuid())).GetProperty("mapping");
        await WriteUseGuard(h, bridge, account, 1, firstReceipt.GetProperty("semanticHash").GetString()!, true);

        var readyForReplace = await ReadMappingContext(main, cookies, account, guard, MappingCriteria("000041B", "模拟订单新关系"));
        const string reason = "模拟保留旧订单并换指";
        var evidence = await BindMapping(h, bridge, main, cookies, account, MappingSubject(account, "REPLACE", "1", second, reason));
        var preview = await GoodMapPreview(main, cookies, MappingInput(readyForReplace, "REPLACE", second, reason,
            binding: evidence.Binding, evidence: [evidence.Evidence]));
        var queue = await PrepareC03Queue(h);
        using var protectionServices = MappingProtection(h);
        var protection = protectionServices.GetRequiredService<IDataProtectionProvider>();
        using var ownerTransport = new HttpClient();
        var realSource = new ErpOrderAuthoritativePartnerUseSource(bridge.Adapter(ownerTransport), TimeProvider.System);
        var accepted = await PrepareUseOrder(h, (first, "000041A"), authoritativeAccount: account);
        await h.Db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_walinspect SCHEMA public");
        var beforeLsn = await h.Db.Database.SqlQueryRaw<string>("SELECT pg_current_wal_insert_lsn()::text AS \"Value\"").SingleAsync();
        var envelope = UseOrderEnvelope(accepted);
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = UseHandler(h, queue, protection, async (stage, ct) =>
        {
            Assert.Equal("order-staged", stage);
            staged.TrySetResult();
            await allowCommit.Task.WaitAsync(TimeSpan.FromSeconds(12), ct);
        }, realSource);

        var consume = ConsumeUseOrder(handler, envelope);
        Task<WorkspaceResponse>? swap = null;
        try
        {
            await staged.Task.WaitAsync(TimeSpan.FromSeconds(10));
            swap = ApplyMap(main, cookies, preview, Guid.NewGuid());
            await WaitForUseLock(h.Db, h.Tenant, "ExclusiveLock");
            Assert.False(swap.IsCompleted);
            Assert.Empty(await UseEffectXids(h.Db, accepted));
        }
        finally
        {
            allowCommit.TrySetResult();
            await consume;
            if (swap is not null) await swap;
        }
        Assert.Equal(Cp6InboxDisposition.Applied, (await consume).Disposition);
        var swapReceipt = MappingReceipt((await swap!)!);
        Assert.Equal("2", swapReceipt.GetProperty("mapping").GetProperty("mappingRevision").GetString());
        Assert.Equal(second, swapReceipt.GetProperty("bp").GetProperty("bpId").GetGuid());
        var xids = await UseEffectXids(h.Db, accepted);
        Assert.Equal(7, xids.Length);
        var outer = await UseOuterCommit(h.Db, beforeLsn, xids);
        Assert.Equal(Cp6InboxDisposition.Duplicate, (await ConsumeUseOrder(UseHandler(h, queue, protection, source: realSource), envelope)).Disposition);
        Assert.Equal(xids.Order(), (await UseEffectXids(h.Db, accepted)).Order());
        var retry = new VerifiedBpUseRequest(h.Tenant, MappingLegal, first, account, "SALES_NEW_SOURCE",
            new(BpUseParticipant.SalesOwner, accepted.OpportunityId.ToString("D"), "1", ErpEventContracts.OrderInputSha256(accepted),
                "SALES_NEW_SOURCE"), 1, 1, accepted.RequestId, "crm-integration", accepted.RequestId.ToString("D"));
        BpUseReceipt original;
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var recovered = await new BpUseParticipant(h.Db, protection, TimeProvider.System).EnlistAsync(tx.GetDbTransaction(), retry, default);
            Assert.True(recovered.Replayed);
            Assert.Equal(first, recovered.Receipt.Bp.BpId);
            Assert.Equal("1", recovered.Receipt.Mapping?.MappingRevision);
            Assert.Equal("000041A", recovered.Receipt.Mapping?.BusinessPartnerKey);
            original = recovered.Receipt;
            await tx.CommitAsync();
        }

        var oldShape = await PrepareUseOrder(h, (first, "000041A"), authoritativeAccount: account);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: realSource), UseOrderEnvelope(oldShape));
        Assert.Equal("C03_OWNER_PARTICIPANT_UNPROVEN", await UseFailure(queue, oldShape.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, oldShape));
        var newShape = await PrepareUseOrder(h, (second, "000041B"), authoritativeAccount: account);
        var latestHash = swapReceipt.GetProperty("mapping").GetProperty("semanticHash").GetString()!;
        await WriteUseGuard(h, bridge, account, 2, latestHash, false);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: realSource), UseOrderEnvelope(newShape));
        Assert.Equal("C03_OWNER_PARTICIPANT_UNPROVEN", await UseFailure(queue, newShape.RequestId));
        Assert.Equal(1, await UseReceiptCount(h));

        await WriteUseGuard(h, bridge, account, 2, latestHash, true);
        var after = await PrepareUseOrder(h, (second, "000041B"), authoritativeAccount: account);
        var secondEffect = await ConsumeUseOrder(UseHandler(h, queue, protection, source: realSource), UseOrderEnvelope(after));
        Assert.Equal(Cp6InboxDisposition.Applied, secondEffect.Disposition);
        Assert.Equal(2, await UseReceiptCount(h));
        var beforeRetire = await StoredEvents(h, account);
        await StateCommand(main, cookies, second, await WorkspaceStatus(main, cookies, second), "freeze", Guid.NewGuid());
        await StateCommand(main, cookies, second, await WorkspaceStatus(main, cookies, second), "retire", Guid.NewGuid());
        Assert.Equal("2", (await MappingHead(main, cookies, account)).Body.GetProperty("head").GetProperty("mappingRevision").GetString());
        Assert.Equal(beforeRetire.Select(x => x.Hash), (await StoredEvents(h, account)).Select(x => x.Hash));
        var retainedUse = await WorkspaceCall(main, cookies, "/history", HttpMethod.Post, new
        {
            legalEntityKey = MappingLegal, bpId = second, kind = "USE", beforeSequence = (string?)null, limit = 10
        });
        Assert.Equal(after.RequestId, Assert.Single(retainedUse.Body.GetProperty("items").EnumerateArray()).GetProperty("reference").GetProperty("id").GetGuid());
        await StateCommand(main, cookies, second, await WorkspaceStatus(main, cookies, second), "restore", Guid.NewGuid());
        var afterRestore = await PrepareUseOrder(h, (second, "000041B"), authoritativeAccount: account);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: realSource), UseOrderEnvelope(afterRestore));
        Assert.Equal("C03_BP_FROZEN", await UseFailure(queue, afterRestore.RequestId));
        Assert.Equal(2, await UseReceiptCount(h));
        var stored = await h.Db.Database.SqlQuery<string>($"""
            SELECT receipt AS "Value" FROM main_bp.use_receipts WHERE tenant_id={h.Tenant} AND use_key={accepted.RequestId}
            """).SingleAsync();
        Assert.Equal(JsonSerializer.Serialize(original, UseJson), stored);
        await DeviceEvidence("wp04-03-c03-i02.json", new
        {
            authoritativeClaimHeldUntilC03Commit = true, c03OriginalAfterRestartAndReplace = true, oldNewUseRejected = true,
            currentExternalGuardInactiveRejects = true, newMappingUseAfterVerifiedGuard = true, account, oldBp = first, currentBp = second,
            retirementRetainedMappingAndOrderHistory = true, restoredPartnerRemainsFrozen = true,
            original, effectTransactions = xids, outer, legacyRequestVersion = accepted.RequestVersion
        });
    }
}
