using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static bool HasIssue(WorkspaceResponse response, string field, string code) =>
        response.Body.GetProperty("issues").EnumerateArray().Any(issue =>
            issue.GetProperty("field").GetString() == field && issue.GetProperty("code").GetString() == code);

    private static object ParentPatch(Guid bp, string revision, Guid target) => new
    {
        bpId = bp, expectedRevision = revision, changes = Array.Empty<object>(),
        relationChanges = new[] { new { slot = "PARENT_CUSTOMER", target = new { kind = "BP", bpId = target } } },
        financeLinkChanges = Array.Empty<object>(), reason = "模拟核对父链"
    };

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_distinguishes_candidate_limit_and_parent_chain_guards()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var first = await WorkspacePreview(host, cookies, WorkspaceInput(options, "CHK-A", "父链甲"));
        var bp = PartnerId(await ApplyWorkspace(host, cookies, first, Guid.NewGuid()));
        var other = await WorkspacePreview(host, cookies, WorkspaceInput(options, "CHK-B", "父链乙",
            useTargets: new Dictionary<string, Guid> { ["PARENT_CUSTOMER"] = bp }));
        var child = PartnerId(await ApplyWorkspace(host, cookies, other, Guid.NewGuid()));
        var sameName = Enumerable.Repeat("重复核对组", 50).ToArray();
        await CopyWorkspaceRows(h, bp, "CAND", sameName);
        var draft = WorkspaceInput(options, "AC11-CHECK", sameName[0]);
        var fifty = await WorkspacePreview(host, cookies, draft);
        Assert.Equal(50, fifty.Body.GetProperty("candidates").GetProperty("items").GetArrayLength());
        await CopyWorkspaceRows(h, bp, "CAND", [sameName[0]], 50);
        var fiftyOne = await WorkspacePreview(host, cookies, draft);
        Assert.True(HasIssue(fiftyOne, "duplicateDecision", "DUPLICATE_REVIEW_REQUIRED"));
        Assert.Equal("REVIEW_REQUIRED", fiftyOne.Body.GetProperty("candidates").GetProperty("visibility").GetString());
        Assert.Empty(fiftyOne.Body.GetProperty("candidates").GetProperty("items").EnumerateArray());

        var chain = await CopyWorkspaceRows(h, bp, "PARENT", Enumerable.Range(1, 33).Select(i => "模拟层 " + i).ToArray());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.relations(tenant_id,legal_entity_key,bp_id,slot,target_id)
            SELECT {h.Tenant},'JP-A',({chain})[j],'PARENT_CUSTOMER',({chain})[j+1]
            FROM generate_series(1,32) AS j
            """);
        var firstDepth32 = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ParentPatch(bp, "1", chain[1]));
        Assert.True(firstDepth32.Body.GetProperty("canApply").GetBoolean(), firstDepth32.Body.GetRawText());
        var before = await FormalCounts(h);
        foreach (var (target, code) in new[] { (child, "RELATION_CYCLE"), (chain[0], "REFERENCE_UNPROVEN") })
        {
            var invalid = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ParentPatch(bp, "1", target));
            Assert.True(HasIssue(invalid, "PARENT_CUSTOMER", code), invalid.Body.GetRawText());
            var apply = await ApplyWorkspace(host, cookies, invalid, Guid.NewGuid(), 422, "/profile-apply");
            Assert.Equal(code, apply.Body.GetProperty("code").GetString());
        }
        Assert.Equal(before, await FormalCounts(h));
        Assert.Equal("1", CurrentRevision(await WorkspaceStatus(host, cookies, bp)));
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM main_bp.relations WHERE tenant_id={h.Tenant} AND bp_id={bp}
                AND slot='PARENT_CUSTOMER' AND target_id IS NOT NULL) AS "Value"
            """).SingleAsync());
        await DeviceEvidence("wp04-38-candidates-parent.json", new
        {
            candidatesAt50 = 50, candidatesAt51 = 0, issue = "DUPLICATE_REVIEW_REQUIRED",
            acceptedDepth = 32, rejectedDepth = 33, cycle = "RELATION_CYCLE", writesAtRejection = 0
        });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_preserves_month_end_and_blocks_invalid_subcontract_profile()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.option_values VALUES({h.Tenant},'JP-A','supplierPattern','1','模拟一般',
                {BusinessPartnerWorkspaceV1.DirectoryOwner},'supplierPattern/1','1',{new string('a', 64)},true)
            """);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var before = await FormalCounts(h);
        foreach (var fields in new Dictionary<string, object?>[]
        {
            new() { ["supplierPattern"] = "1", ["deliveryConfirmFlg"] = true, ["subcontractTargetFlg"] = true },
            new() { ["supplierPattern"] = "1", ["deliveryConfirmFlg"] = true, ["subcontractPriceDiv"] = "A" }
        })
        {
            var invalid = await WorkspacePreview(host, cookies, WorkspaceInput(options, "OUTSOURCE", "模拟非外注资料",
                useRoles: ["Supplier"], useSelf: [], overrides: fields));
            Assert.True(HasIssue(invalid, "supplierPattern", "FIELD_INVALID"), invalid.Body.GetRawText());
            var result = await ApplyWorkspace(host, cookies, invalid, Guid.NewGuid(), 422);
            Assert.Contains("FIELD_INVALID", result.Body.GetRawText());
        }
        Assert.Equal(before, await FormalCounts(h));
        foreach (var purpose in new[] { "SUBCONTRACT_NEW_SOURCE", "MANUFACTURING_NEW_SOURCE" })
            Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", await RejectedPartnerUse(h, (Guid.NewGuid(), "OUTSOURCE"), purpose, 1));
        Assert.Equal(0, await UseReceiptCount(h));

        var roles = new[] { "Customer", "AccountsReceivable", "Billing", "Receipt", "PaymentSchedule", "Payment" };
        var slots = new[] { "CUSTOMER_AR", "AR_BILLING", "BILLING_RECEIPT", "SCHEDULE_PAYEE" };
        var due = await WorkspacePreview(host, cookies, WorkspaceInput(options, "MONTH-END", "模拟末日规则", complete: true,
            useRoles: roles, useSelf: slots, overrides: new() { ["paymentScheduleDeptCd"] = "DEP", ["paidEachTimeFlg"] = false }));
        var bp = PartnerId(await ApplyWorkspace(host, cookies, due, Guid.NewGuid()));
        var noClosingDay = await StateCommand(host, cookies, bp, await WorkspaceStatus(host, cookies, bp), "register", Guid.NewGuid(), 422);
        Assert.Equal("PROFILE_INCOMPLETE", noClosingDay.Body.GetProperty("code").GetString());
        Assert.Contains("paymentClosingDay1", noClosingDay.Body.GetRawText());
        var monthEnd = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(bp, "1", "paymentClosingDay1", 99));
        await ApplyWorkspace(host, cookies, monthEnd, Guid.NewGuid(), path: "/profile-apply");
        await StateCommand(host, cookies, bp, await WorkspaceStatus(host, cookies, bp), "register", Guid.NewGuid());
        var detail = await WorkspaceCall(host, cookies, $"/{bp:D}");
        Assert.Equal(99, detail.Body.GetProperty("profile").GetProperty("paymentClosingDay1").GetInt32());
        Assert.Equal(99, await h.Db.BusinessPartners.AsNoTracking().Where(x => x.Id == bp).Select(x => x.PaymentClosingDay1).SingleAsync());
        Assert.False(detail.Body.GetProperty("profile").GetProperty("paidEachTimeFlg").GetBoolean());
        await DeviceEvidence("wp04-38-month-end.json", new { subcontractRejected = new[] { "subcontractTargetFlg", "subcontractPriceDiv" },
            receiptsAfterRefusal = 0, missingClosingDay = noClosingDay.Status, closingDay = 99, registered = true });
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_serializes_profile_and_state_and_allows_frozen_retired_edits()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var create = await WorkspacePreview(host, cookies, WorkspaceInput(options, "CONCUR", "初始关系", complete: true));
        var bp = PartnerId(await ApplyWorkspace(host, cookies, create, Guid.NewGuid()));
        var original = await WorkspaceStatus(host, cookies, bp);
        var preview = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(bp, "1", "bpName", "竞争后的名称"));
        var editKey = Guid.NewGuid();
        var holdKey = Guid.NewGuid();
        var hold = new { bpId = bp, expectedRevision = "1", reason = "模拟相同修订竞争", hold = "FROZEN" };
        var race = await Task.WhenAll(ApplyWorkspace(host, cookies, preview, editKey, null, "/profile-apply"),
            WorkspaceCall(host, cookies, "/hold", HttpMethod.Post, hold, original.ETag, holdKey, null));
        Assert.Equal(new[] { 200, 412 }, race.Select(x => x.Status).Order());
        Assert.Equal("2", CurrentRevision(await WorkspaceStatus(host, cookies, bp)));
        var raceCounts = await FormalCounts(h);
        Assert.Equal(2, raceCounts[1]);
        Assert.Equal(2, raceCounts[3]);
        Assert.Equal(2, raceCounts[4]);
        Assert.Equal(race[1].Status == 200 ? 2 : 1, raceCounts[5]);
        var frozen = await WorkspaceStatus(host, cookies, bp);
        if (frozen.Body.GetProperty("state").GetProperty("hold").GetString() != "FROZEN")
            await StateCommand(host, cookies, bp, frozen, "freeze", Guid.NewGuid());
        var preRegister = await WorkspaceStatus(host, cookies, bp);
        Assert.Equal("PRE_REGISTERED", preRegister.Body.GetProperty("state").GetProperty("registration").GetString());
        await StateCommand(host, cookies, bp, preRegister, "register", Guid.NewGuid());
        var registered = await WorkspaceStatus(host, cookies, bp);
        Assert.Equal("REGISTERED", registered.Body.GetProperty("state").GetProperty("registration").GetString());
        Assert.Equal("FROZEN", registered.Body.GetProperty("state").GetProperty("hold").GetString());
        var revision = long.Parse(CurrentRevision(registered), System.Globalization.CultureInfo.InvariantCulture);
        var used = await RejectedPartnerUse(h, (bp, "CONCUR"), "SALES_NEW_SOURCE", revision);
        Assert.Equal("BP_FROZEN", used);
        var frozenEdit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post,
            ProfilePatch(bp, CurrentRevision(registered), "bpName", "冻结仍可维护"));
        await ApplyWorkspace(host, cookies, frozenEdit, Guid.NewGuid(), path: "/profile-apply");
        var priorRetirement = await WorkspaceStatus(host, cookies, bp);
        Assert.Equal("FROZEN", priorRetirement.Body.GetProperty("state").GetProperty("hold").GetString());
        await StateCommand(host, cookies, bp, priorRetirement, "retire", Guid.NewGuid());
        var retired = await WorkspaceStatus(host, cookies, bp);
        var retiredEdit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post,
            ProfilePatch(bp, CurrentRevision(retired), "tel", "+81-32-6789"));
        await ApplyWorkspace(host, cookies, retiredEdit, Guid.NewGuid(), path: "/profile-apply");
        var profile = await WorkspaceCall(host, cookies, $"/{bp:D}");
        Assert.Equal("冻结仍可维护", profile.Body.GetProperty("name").GetString());
        Assert.Equal("+81-32-6789", profile.Body.GetProperty("profile").GetProperty("tel").GetString());
        var final = profile.Body.GetProperty("state");
        Assert.Equal("REGISTERED", final.GetProperty("registration").GetString());
        Assert.Equal("FROZEN", final.GetProperty("hold").GetString());
        Assert.Equal("RETIRED", final.GetProperty("lifecycle").GetString());
        Assert.Equal("BP_RETIRED", await RejectedPartnerUse(h, (bp, "CONCUR"), "SALES_NEW_SOURCE",
            long.Parse(CurrentRevision(profile), System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(0, await UseReceiptCount(h));
        var profileHistory = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post,
            new { legalEntityKey = "JP-A", bpId = bp, kind = "PROFILE", beforeSequence = (string?)null, limit = 50 });
        var stateHistory = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post,
            new { legalEntityKey = "JP-A", bpId = bp, kind = "STATUS", beforeSequence = (string?)null, limit = 50 });
        Assert.Equal(1, profileHistory.Body.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("sequence").GetString() == "2"));
        Assert.Equal(race[1].Status == 200 ? 1 : 0, stateHistory.Body.GetProperty("items").EnumerateArray()
            .Count(x => x.GetProperty("sequence").GetString() == "2"));
        await DeviceEvidence("wp04-38-state-race.json", new { race = race.Select(x => x.Status), frozenUse = used,
            retiredUse = "BP_RETIRED", profile = final, originalHistoryPreserved = true, receipts = 0 });
    }

    private async Task<string> RejectedPartnerUse(Harness h, (Guid Id, string Code) partner, string purpose, long revision)
    {
        await using var keys = MappingProtection(h);
        var protection = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(keys);
        var owner = new BpUseParticipant(h.Db, protection, TimeProvider.System);
        await using var tx = await h.Db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var error = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.EnlistAsync(
            Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(tx), DirectUse(h, partner, purpose, revision), default));
        await tx.RollbackAsync();
        return error.Message;
    }
}
