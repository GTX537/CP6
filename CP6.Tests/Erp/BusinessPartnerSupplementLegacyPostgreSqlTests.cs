using System.Data;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DomainModels.Erp;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static BusinessPartner HistoricalCustomer(Harness h, string code, string? currency, int status) => new()
    {
        Id = Guid.NewGuid(), TenantId = h.Tenant, BpCd = code, BpName = "模拟历史 " + code, BaseCd = "BPA",
        Creator = "price-owner", CurrencyCd = currency, Status = status, IsDeleted = status == 9,
        CustomerFlg = true, AccountsReceivableFlg = true, BillingFlg = true, ReceiptFlg = true,
        SalesStaffCd = "STA", BusinessStaffCd = "STB", PurchaseFractionDiv = null, PurchaseTaxFractionDiv = null,
        PurchaseTaxCd = null, SupplierCalendarCd = null, SupplyConsignDiv = null, PurchaseLotSplitDiv = null, PurchasePostingDiv = null
    };

    // 注入已审批采用的旧档起点，使用 Main 相同密钥环和物理 PostgreSQL 外键。
    private static async Task ImportHistoricalPartner(Harness h, IDataProtectionProvider protection, BusinessPartner bp,
        string registration, Dictionary<string, Guid>? slotTargets = null)
    {
        const string legal = "JP-A";
        var payload = BusinessPartnerWorkspaceV1.Canonical(new
        {
            bpId = bp.Id, bpCd = bp.BpCd, legalEntityKey = legal, revision = "1", name = bp.BpName,
            commercialCurrency = bp.CurrencyCd, financeLinks = Array.Empty<object>(), profile = new { bp.PaymentScheduleCd },
            state = new { registration, lifecycle = bp.IsDeleted ? "RETIRED" : "ACTIVE", hold = "CLEAR" }
        });
        var digest = MappingSha(payload);
        var cipher = protection.CreateProtector(BusinessPartnerWorkspaceV1.ProfileOwner, h.Tenant.ToString("D"), "revision",
            bp.Id.ToString("D")).Protect(payload);
        var name = BusinessPartnerUnicode15.Normalize(bp.BpName);
        var recorded = DateTimeOffset.UtcNow.AddDays(-1);
        var initial = Guid.NewGuid();
        if (slotTargets is null)
            slotTargets = new[] { "CUSTOMER_AR", "AR_BILLING", "BILLING_RECEIPT" }.ToDictionary(slot => slot, _ => bp.Id);
        var targets = WorkspaceSlots.Select(slot => slotTargets.TryGetValue(slot, out var id) ? (Guid?)id : null).ToArray();
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.heads(tenant_id,bp_id,legal_entity_key,registration,revision)
                VALUES({h.Tenant},{bp.Id},{legal},{registration},1);
            INSERT INTO main_bp.profile_revisions VALUES({h.Tenant},{legal},{bp.Id},1,{bp.RowVersion!},{digest},{cipher},{recorded});
            INSERT INTO main_bp.normalized_identity(tenant_id,legal_entity_key,bp_id,norm_version,revision,name_norm,name_hash)
                VALUES({h.Tenant},{legal},{bp.Id},{BusinessPartnerUnicode15.Version},1,{name},sha256(convert_to({name},'UTF8')));
            INSERT INTO main_bp.commands(tenant_id,command_id,actor_id,legal_entity_key,bp_id,operation,fingerprint,receipt)
                VALUES({h.Tenant},{initial},{h.User},{legal},{bp.Id},'ADOPT_LEGACY',sha256(convert_to({digest},'UTF8')),{"{}"});
            INSERT INTO main_bp.audit VALUES({h.Tenant},{initial},{legal},{bp.Id},1,{h.User},'ADOPT_LEGACY','APPLIED',NULL,
                ARRAY['commercialCurrency','paymentScheduleCd'],{recorded});
            INSERT INTO main_bp.state_history VALUES({h.Tenant},{legal},{bp.Id},1,{initial},{registration},
                {(bp.IsDeleted ? "RETIRED" : "ACTIVE")},'CLEAR');
            INSERT INTO main_bp.relations(tenant_id,legal_entity_key,bp_id,slot,target_id)
                SELECT {h.Tenant},{legal},{bp.Id},slot,target FROM unnest({WorkspaceSlots},{targets}) AS supplied(slot,target);
            """);
        await tx.CommitAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_keeps_legacy_currency_and_registration_history_under_current_authority()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        var active = HistoricalCustomer(h, "LEG-NULL", null, 1);
        var retired = HistoricalCustomer(h, "LEG-DELETED", "JPY", 9);
        h.Db.BusinessPartners.AddRange(active, retired);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await h.Db.Orders.ExecuteUpdateAsync(set => set.SetProperty(row => row.CurrencyCd, "USD"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.option_values VALUES({h.Tenant},'JP-A','commercialCurrency','EUR','模拟欧元',
                {BusinessPartnerWorkspaceV1.DirectoryOwner},'commercialCurrency/EUR','1',{new string('a', 64)},true)
            """);
        await using var keys = MappingProtection(h);
        var protection = keys.GetRequiredService<IDataProtectionProvider>();
        await ImportHistoricalPartner(h, protection, active, "REGISTERED");
        await ImportHistoricalPartner(h, protection, retired, "UNKNOWN_LEGACY");
        await EnablePartnerUse(h.Db, h.Tenant);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var original = await WorkspaceCall(host, cookies, $"/{active.Id:D}");
        Assert.Equal(JsonValueKind.Null, original.Body.GetProperty("commercialCurrency").ValueKind);
        Assert.True(HasIssue(original, "commercialCurrency", "REFERENCE_UNPROVEN"));
        Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", await RejectedPartnerUse(h, (active.Id, active.BpCd), "SALES_NEW_SOURCE", 1));
        var currency = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post,
            ProfilePatch(active.Id, "1", "commercialCurrency", "EUR"));
        await ApplyWorkspace(host, cookies, currency, Guid.NewGuid(), path: "/profile-apply");
        var current = await WorkspaceCall(host, cookies, $"/{active.Id:D}");
        Assert.Equal("EUR", current.Body.GetProperty("commercialCurrency").GetString());
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var permit = await new BpUseParticipant(h.Db, protection, TimeProvider.System).EnlistAsync(tx.GetDbTransaction(),
                DirectUse(h, (active.Id, active.BpCd), revision: 2), default);
            Assert.Equal("EUR", permit.Receipt.Currency);
            await tx.CommitAsync();
        }
        var oldMoney = await h.Db.Orders.AsNoTracking().SingleAsync();
        Assert.Equal("USD", oldMoney.CurrencyCd.TrimEnd());
        var currencyHistory = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post, HistoryQuery(active.Id));
        var versions = currencyHistory.Body.GetProperty("items");
        Assert.Equal("EUR", Assert.Single(versions[0].GetProperty("changes").EnumerateArray()).GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, Assert.Single(versions[1].GetProperty("changes").EnumerateArray(),
            field => field.GetProperty("field").GetString() == "commercialCurrency").GetProperty("value").ValueKind);

        var deleted = await WorkspaceStatus(host, cookies, retired.Id);
        Assert.Equal("UNKNOWN_LEGACY", deleted.Body.GetProperty("state").GetProperty("registration").GetString());
        var refusedRegister = await StateCommand(host, cookies, retired.Id, deleted, "register", Guid.NewGuid(), 422);
        Assert.Equal("REGISTRATION_NOT_ALLOWED", refusedRegister.Body.GetProperty("code").GetString());
        var refusedRestore = await StateCommand(host, cookies, retired.Id, deleted, "restore", Guid.NewGuid(), 422);
        Assert.Equal("REGISTRATION_CONFIRMATION_REQUIRED", refusedRestore.Body.GetProperty("code").GetString());
        var restore = new { bpId = retired.Id, expectedRevision = "1", reason = "模拟当次明确确认", confirmRegistration = true };
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 212 && x.RoleId == 72 && x.ActionCode == "register").ExecuteDeleteAsync();
        await WorkspaceCall(host, cookies, "/restore", HttpMethod.Post, restore, deleted.ETag, Guid.NewGuid(), 403);
        Assert.Equal("1", CurrentRevision(await WorkspaceStatus(host, cookies, retired.Id)));
        await GrantPartnerActions(h, "register");
        await WorkspaceCall(host, cookies, "/restore", HttpMethod.Post, restore, deleted.ETag, Guid.NewGuid());
        var recovered = await WorkspaceStatus(host, cookies, retired.Id);
        Assert.Equal("REGISTERED", recovered.Body.GetProperty("state").GetProperty("registration").GetString());
        Assert.Equal("ACTIVE", recovered.Body.GetProperty("state").GetProperty("lifecycle").GetString());
        var registrationHistory = await h.Db.Database.SqlQuery<string>($"""
            SELECT registration AS "Value" FROM main_bp.state_history WHERE tenant_id={h.Tenant} AND bp_id={retired.Id} ORDER BY revision
            """).ToArrayAsync();
        Assert.Equal(new[] { "UNKNOWN_LEGACY", "REGISTERED" }, registrationHistory);

        var options = await WorkspaceCall(host, cookies, "/options?legalEntityKey=JP-A");
        var pre = await WorkspacePreview(host, cookies, WorkspaceInput(options, "DIR-LOSS", "模拟字典不可用", complete: true));
        var preId = PartnerId(await ApplyWorkspace(host, cookies, pre, Guid.NewGuid()));
        await using (var revoke = await h.Db.Database.BeginTransactionAsync())
        {
            await BpUseScopeGate.LockAsync(h.Db, "JP-A", true, default);
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE main_bp.option_values SET enabled=false
                WHERE tenant_id={h.Tenant} AND legal_entity_key='JP-A' AND field='salesStaffCd' AND code='STA'
                """);
            await revoke.CommitAsync();
        }
        var edit = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, ProfilePatch(active.Id, "2", "bpName", "字典未知时改名称"));
        await ApplyWorkspace(host, cookies, edit, Guid.NewGuid(), path: "/profile-apply");
        var noRegistration = await StateCommand(host, cookies, preId, await WorkspaceStatus(host, cookies, preId), "register", Guid.NewGuid(), 422);
        Assert.Equal("REFERENCE_UNPROVEN", noRegistration.Body.GetProperty("code").GetString());
        Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", await RejectedPartnerUse(h, (active.Id, active.BpCd), "SALES_NEW_SOURCE", 3));
        Assert.Equal(1, await UseReceiptCount(h));
        var history = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post, HistoryQuery(active.Id));
        Assert.Equal(3, history.Body.GetProperty("items").GetArrayLength());
        await DeviceEvidence("wp04-38-legacy-and-directory.json", new
        {
            originalCurrency = (string?)null, currentCurrency = "EUR", priorOrderCurrency = "USD", registrationHistory,
            noConfirmation = refusedRestore.Status, noRegister = refusedRegister.Status, dictionaryRegister = noRegistration.Status,
            dictionaryUse = "OWNER_PARTICIPANT_UNPROVEN", unrelatedNameEdit = true, history = 3, originalUseReceipts = 1
        });
    }

    private static object HistoryQuery(Guid bp) =>
        new { legalEntityKey = "JP-A", bpId = bp, kind = "PROFILE", beforeSequence = (string?)null, limit = 50 };

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_38_requires_two_independent_payment_relations_for_legacy_supplier()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        var supplier = HistoricalCustomer(h, "SUP-LEG", "JPY", 1);
        supplier.CustomerFlg = supplier.AccountsReceivableFlg = supplier.BillingFlg = supplier.ReceiptFlg = false;
        supplier.SupplierFlg = supplier.AccountsPayableFlg = true;
        supplier.SupplierPattern = "5";
        supplier.PurchaseFractionDiv = supplier.PurchaseTaxFractionDiv = "3";
        supplier.SupplyConsignDiv = supplier.PurchaseLotSplitDiv = "1";
        supplier.PaymentScheduleCd = "LEG-ONE";
        var schedule = HistoricalCustomer(h, "PAY-TRUE", "JPY", 1);
        schedule.PaymentScheduleFlg = schedule.PaymentFlg = schedule.PaidEachTimeFlg = true;
        schedule.PaymentScheduleDeptCd = "DEP";
        h.Db.BusinessPartners.AddRange(supplier, schedule);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        foreach (var (field, value) in new[] { ("purchaseFractionDiv", "3"), ("purchaseTaxFractionDiv", "3"),
                     ("supplyConsignDiv", "1"), ("purchaseLotSplitDiv", "1") })
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.option_values VALUES({h.Tenant},'JP-A',{field},{value},{value},
                    {BusinessPartnerWorkspaceV1.DirectoryOwner},{field + "/" + value},'1',{new string('a', 64)},true)
                """);
        await using var keys = MappingProtection(h);
        var protection = keys.GetRequiredService<IDataProtectionProvider>();
        await ImportHistoricalPartner(h, protection, supplier, "REGISTERED", new());
        await ImportHistoricalPartner(h, protection, schedule, "REGISTERED", new()
        {
            ["CUSTOMER_AR"] = schedule.Id, ["AR_BILLING"] = schedule.Id, ["BILLING_RECEIPT"] = schedule.Id,
            ["SCHEDULE_PAYEE"] = schedule.Id
        });
        await EnablePartnerUse(h.Db, h.Tenant);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        var detail = await WorkspaceCall(host, cookies, $"/{supplier.Id:D}");
        var oldField = Assert.Single(detail.Body.GetProperty("legacyFields").EnumerateArray(),
            field => field.GetProperty("field").GetString() == "paymentScheduleCd");
        Assert.Equal("LEG-ONE", oldField.GetProperty("value").GetString());
        foreach (var slot in new[] { "SUPPLIER_AP", "AP_SCHEDULE" })
            Assert.Equal(JsonValueKind.Null, Assert.Single(detail.Body.GetProperty("relations").EnumerateArray(),
                item => item.GetProperty("slot").GetString() == slot).GetProperty("target").ValueKind);
        var subject = (supplier.Id, supplier.BpCd);
        Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", await RejectedPartnerUse(h, subject, "PURCHASE_NEW_SOURCE", 1));
        Assert.Equal(0, await UseReceiptCount(h));
        var edit = new
        {
            bpId = supplier.Id, expectedRevision = "1", changes = Array.Empty<object>(),
            relationChanges = new[]
            {
                new { slot = "SUPPLIER_AP", target = (object)new { kind = "SELF" } },
                new { slot = "AP_SCHEDULE", target = (object)new { kind = "BP", bpId = schedule.Id } }
            },
            financeLinkChanges = Array.Empty<object>(), reason = "模拟分别确认供应商应付和应付付款计划"
        };
        var preview = await WorkspaceCall(host, cookies, "/profile-previews", HttpMethod.Post, edit);
        await ApplyWorkspace(host, cookies, preview, Guid.NewGuid(), path: "/profile-apply");
        var current = await WorkspaceCall(host, cookies, $"/{supplier.Id:D}");
        foreach (var (slot, target) in new[] { ("SUPPLIER_AP", supplier.Id), ("AP_SCHEDULE", schedule.Id) })
            Assert.Equal(target, Assert.Single(current.Body.GetProperty("relations").EnumerateArray(),
                item => item.GetProperty("slot").GetString() == slot).GetProperty("target").GetProperty("bpId").GetGuid());
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var result = await new BpUseParticipant(h.Db, protection, TimeProvider.System).EnlistAsync(tx.GetDbTransaction(),
                DirectUse(h, subject, "PURCHASE_NEW_SOURCE", 2), default);
            Assert.Equal(schedule.Id.ToString("D"), Assert.Single(result.Receipt.DependencyRefs).Id);
            await tx.CommitAsync();
        }
        var history = await WorkspaceCall(host, cookies, "/history", HttpMethod.Post, HistoryQuery(supplier.Id));
        Assert.Contains("LEG-ONE", history.Body.GetProperty("items")[1].GetProperty("changes").GetRawText());
        await DeviceEvidence("wp04-38-payment-slots.json", new
        {
            legacyValue = "LEG-ONE", originalSlots = new[] { "NO_VALUE", "NO_VALUE" }, initialUse = "OWNER_PARTICIPANT_UNPROVEN",
            currentSlots = new[] { "SUPPLIER_AP:SELF", "AP_SCHEDULE:OTHER" }, originalRetained = true, receipts = await UseReceiptCount(h)
        });
    }
}
