using System.Data;
using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using CP6.Platform.EntityFramework;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static VerifiedBpUseRequest DirectUse(Harness h, (Guid Id, string Code) partner, string purpose = "SALES_NEW_SOURCE",
        long revision = 1)
    {
        var id = Guid.NewGuid();
        var owner = purpose.StartsWith("SALES_", StringComparison.Ordinal) ? BpUseParticipant.SalesOwner : BpUseParticipant.PurchaseOwner;
        return new(h.Tenant, UseLegal, partner.Id, null, purpose,
            new(owner, id.ToString("D"), "1", new string('a', 64), purpose),
            revision, null, id, h.User.ToString("D"), id.ToString("D"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_checks_native_identity_and_rejects_reverse_EST_lock()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var request = DirectUse(h, partner);
        var participant = new BpUseParticipant(h.Db, protection, TimeProvider.System);

        var foreign = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        await using (var other = new NpgsqlConnection(foreign.ConnectionString))
        {
            await other.OpenAsync();
            await using var external = await other.BeginTransactionAsync(IsolationLevel.Serializable);
            var rejected = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => participant.EnlistAsync(external, request, default));
            Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", rejected.Message);
        }

        await using (var own = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await h.Db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."T_EstimateCalc" IN SHARE MODE""");
            var rejected = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                participant.EnlistAsync(own.GetDbTransaction(), request, default));
            Assert.Equal("LOCK_PLAN_UNPROVEN", rejected.Message);
            Assert.False(await h.Db.Database.SqlQuery<bool>($"""
                SELECT main_bp.use_scope_held({h.Tenant},{UseLegal},false) AS "Value"
                """).SingleAsync());
        }
        Assert.Equal(0, await UseReceiptCount(h));
    }

    private static Task<int> UseReceiptCount(Harness h) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM main_bp.use_receipts WHERE tenant_id={h.Tenant}
        """).SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_legacy_crud_commerce_and_C03_cannot_change_qualification()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h, alternate: true, admin: true);
        var account = Guid.NewGuid();
        var partner = new BusinessPartner
        {
            TenantId = h.Tenant, BpCd = "LEG" + Guid.NewGuid().ToString("N")[..12], BpName = "模拟旧伙伴",
            BaseCd = "BPA", CrmAccountId = account, Status = 0, CustomerFlg = true,
            AccountsReceivableCd = "AR", SalesStaffCd = "STA", BusinessStaffCd = "STB"
        };
        h.Db.BusinessPartners.Add(partner);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var ws = UseWorkspace(h, h.Db, protection);
        var fenced = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            CreateUsePartner(h, protection, legal: "JP-B"));
        Assert.Equal("BP_SCOPE_READ_ONLY", fenced.Message);
        var legacy = new BusinessPartnerService(h.Db, OwnedUseAuthority(h, h.Db), ws);
        var input = new BusinessPartnerDto { BpCd = partner.BpCd, BpName = "模拟绕门", BaseCd = "BPA" };
        Assert.Equal("BP_CREATE_PREVIEW_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            legacy.CreateAsync(input, "fixture"))).Message);
        Assert.Equal("BP_PROFILE_PREVIEW_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            legacy.UpdateAsync(partner.BpCd, input, "fixture"))).Message);
        Assert.Equal("BP_LEGAL_SCOPE_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            legacy.DeleteAsync(partner.BpCd, partner.RowVersion, "fixture"))).Message);
        Assert.Equal("C03_BP_PROFILE_PREVIEW_REQUIRED", (await Assert.ThrowsAsync<ErpCommerceException>(() =>
            new ErpCommerceAuthority(h.Db).SetBusinessPartnerProfileAsync(partner.BpCd,
                new(partner.RowVersion!, account, "JPY", true), "fixture"))).Code);

        var queue = await PrepareC03Queue(h);
        var original = new ErpBusinessPartnerRequested(h.Tenant, Guid.NewGuid(), account, 1);
        var delivery = ErpEventContracts.Create(h.Tenant, account.ToString("D"), 1, ErpEventContracts.BusinessPartnerRequested,
            original, "local", Guid.NewGuid().ToString("N"), "fixture-legacy");
        Assert.Equal(Cp6InboxDisposition.Applied, (await ConsumeUseOrder(UseHandler(h, queue, protection), delivery)).Disposition);
        Assert.Equal("C03_BP_PROFILE_PREVIEW_REQUIRED", await UseFailure(queue, original.RequestId));

        var current = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(row => row.Id == partner.Id);
        Assert.Equal(0, current.Status);
        Assert.False(current.IsFrozen);
        Assert.False(current.IsDeleted);
        Assert.Equal(partner.RowVersion, current.RowVersion);
        Assert.Equal(partner.BpName, (await legacy.GetByCdAsync(partner.BpCd))!.BpName);
        Assert.Equal(0, await UseReceiptCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_original_command_survives_new_action_revocation_but_requires_current_read()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var ws = UseWorkspace(h, h.Db, protection);
        var status = await PrepareUseStatus(ws, partner.Id, "FROZEN");
        var key = Guid.NewGuid();
        var committed = UseElement((await ApplyUseStatus(ws, status, key)).Body).GetProperty("receipt");
        var before = await FormalCounts(h);

        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 && row.ActionCode == "status").ExecuteDeleteAsync();
        var replayed = UseElement((await ApplyUseStatus(ws, status, key)).Body);
        Assert.True(replayed.GetProperty("replayed").GetBoolean());
        Assert.Equal(committed.GetRawText(), replayed.GetProperty("receipt").GetRawText());
        var original = UseElement((await ws.ReadAsync("COMMAND_READ", key, null, null, default)).Body);
        Assert.Equal("FOUND", original.GetProperty("knowledge").GetString());
        Assert.Equal(committed.GetRawText(), original.GetProperty("receipt").GetRawText());
        var standNow = await PrepareUseStatus(ws, partner.Id, "CLEAR");
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            ApplyUseStatus(ws, standNow, Guid.NewGuid()))).Status);

        await h.Db.Sys_RoleActions.Where(row => row.MenuId == 212 && row.RoleId == 72 &&
            row.ActionCode == "command-result.read").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => ApplyUseStatus(ws, status, key))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            ws.ReadAsync("COMMAND_READ", key, null, null, default))).Status);
        Assert.Equal(before, await FormalCounts(h));
        Assert.Equal(0, await UseReceiptCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_current_dependency_freeze_blocks_sales_without_changes()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var receivable = await CreateUsePartner(h, protection, ["AccountsReceivable", "Billing", "Receipt"],
            ["AR_BILLING", "BILLING_RECEIPT"]);
        var partner = await CreateUsePartner(h, protection, ["Customer"], [], new Dictionary<string, Guid>
        {
            ["CUSTOMER_AR"] = receivable.Id
        });
        var ws = UseWorkspace(h, h.Db, protection);
        await ApplyUseStatus(ws, await PrepareUseStatus(ws, receivable.Id, "FROZEN"), Guid.NewGuid());
        var queue = await PrepareC03Queue(h);
        var order = await PrepareUseOrder(h, partner);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: new UseMapping()), UseOrderEnvelope(order));
        Assert.Equal("C03_BP_FROZEN", await UseFailure(queue, order.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, order));
        Assert.False(await h.Db.BusinessPartners.AsNoTracking().Where(row => row.Id == partner.Id)
            .Select(row => row.IsFrozen).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_restore_keeps_hold_and_denies_the_next_use()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection);
        var ws = UseWorkspace(h, h.Db, protection);
        await ApplyUseStatus(ws, await PrepareUseStatus(ws, partner.Id, "FROZEN"), Guid.NewGuid());
        foreach (var operation in new[] { "RETIRE", "RESTORE" })
        {
            var status = await PrepareUseStatus(ws, partner.Id, "FROZEN");
            var body = new Dictionary<string, object?>
            {
                ["bpId"] = partner.Id, ["expectedRevision"] = status.Body.GetProperty("expectedRevision").GetString(),
                ["reason"] = "模拟状态复验"
            };
            if (operation == "RESTORE") body["confirmRegistration"] = false;
            await ws.PostAsync(operation, UseElement(body), status.Context.ETag, Guid.NewGuid().ToString("D"), null, default);
        }
        var root = await h.Db.BusinessPartners.AsNoTracking().SingleAsync(row => row.Id == partner.Id);
        Assert.False(root.IsDeleted);
        Assert.True(root.IsFrozen);
        var queue = await PrepareC03Queue(h);
        var request = await PrepareUseOrder(h, partner);
        await ConsumeUseOrder(UseHandler(h, queue, protection, source: new UseMapping()), UseOrderEnvelope(request));
        Assert.Equal("C03_BP_FROZEN", await UseFailure(queue, request.RequestId));
        Assert.Empty(await UseEffectXids(h.Db, request));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_I02_checks_actual_finance_revision_in_the_outer_transaction()
    {
        const string legal = "SIM-LEGAL";
        await using var h = await Seed();
        await ProvisionWorkspace(h, primaryLegal: legal);
        await GrantPartnerActions(h, BusinessPartnerFinanceReferenceReader.ReadAction);
        var bank = await CreateLegacyFundsBank(h, "4111222233334042");
        var actors = await SeedGlBoard(h, "bp-use-finance");
        var environmentId = ScopeEnvironment.ToString("D");
        var scope = new ManufacturingFinanceScopeV1(h.Db, environmentId);
        var financeContext = await scope.IssueContextAsync(actors.Ledger, h.User, default);
        await EnablePartnerUse(h.Db, h.Tenant, legal);
        var protection = new EphemeralDataProtectionProvider();
        var partner = await CreateUsePartner(h, protection, legal: legal);
        var source = await NativeFinanceReference(h, BusinessPartnerFinanceReferenceReader.LegacyBankOwner, bank);
        await RegisterFinanceScope(h, actors.Ledger, source, null);
        await StoreFinanceLink(h, partner.Id, actors.Ledger, BusinessPartnerFinanceReferenceReader.ReceiptBank, source);

        var workspace = UseWorkspace(h, h.Db, protection, environmentId);
        var profile = await workspace.PostAsync("PROFILE_PREVIEW", UseElement(new
        {
            bpId = partner.Id, expectedRevision = "1", changes = Array.Empty<object>(), relationChanges = Array.Empty<object>(),
            financeLinkChanges = new[] { new { kind = "RECEIPT_BANK", reference = source } }, reason = "模拟绑定准确银行原件"
        }), null, null, financeContext, default);
        Assert.True(UseElement(profile.Body).GetProperty("canApply").GetBoolean(), UseElement(profile.Body).GetRawText());
        await workspace.PostAsync("PROFILE_APPLY", UseElement(new { preview = UseElement(profile.Body).GetProperty("reference") }),
            profile.ETag, Guid.NewGuid().ToString("D"), financeContext, default);

        var authority = OwnedUseAuthority(h, h.Db);
        var finance = new BusinessPartnerFinanceReferenceReader(h.Db, authority, scope);
        var participant = new BpUseParticipant(h.Db, protection, TimeProvider.System);
        var intent = DirectUse(h, partner, revision: 2) with { LegalEntityKey = legal };
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await BpUseScopeGate.HoldIamAsync(h.Db, default);
            var access = new OperationsAccessV1(h.Db, authority, await authority.AuthorityAsync("erp-business-partner", "query", default));
            var result = await participant.EnlistAsync(tx.GetDbTransaction(), intent with { Finance = new(access, finance, financeContext) }, default);
            Assert.Equal(source.Digest, Assert.Single(result.Receipt.DependencyRefs).Digest);
            Assert.False(result.Receipt.IsReusablePermit);
            await authority.ValidateFinalAuthorityAsync(access, h.User, default);
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await UseReceiptCount(h));

        await h.Db.BankAccounts.Where(a => a.Id == bank).ExecuteUpdateAsync(set => set.SetProperty(a => a.IsActive, false));
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await BpUseScopeGate.HoldIamAsync(h.Db, default);
            var access = new OperationsAccessV1(h.Db, authority, await authority.AuthorityAsync("erp-business-partner", "query", default));
            var denied = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => participant.EnlistAsync(tx.GetDbTransaction(),
                intent with { Finance = new(access, finance, financeContext) }, default));
            Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", denied.Message);
        }
        Assert.Equal(0, await UseReceiptCount(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Business_partner_wp04_02_requires_exact_supplier_purpose_and_no_account()
    {
        await using var h = await Seed();
        await ProvisionWorkspace(h);
        foreach (var field in new[] { "purchaseFractionDiv", "purchaseTaxFractionDiv", "supplyConsignDiv", "purchaseLotSplitDiv" })
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(field))).ToLowerInvariant();
            await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO main_bp.option_values VALUES({h.Tenant},{UseLegal},{field},'1','模拟采购值',
                  {BusinessPartnerWorkspaceV1.DirectoryOwner},{field},'1',{hash},true)
                """);
        }
        await EnablePartnerUse(h.Db, h.Tenant);
        var protection = new EphemeralDataProtectionProvider();
        var supplier = await CreateUsePartner(h, protection, ["Supplier", "AccountsPayable", "PaymentSchedule", "Payment"],
            ["SUPPLIER_AP", "AP_SCHEDULE", "SCHEDULE_PAYEE"], values: new()
            {
                ["supplierPattern"] = "5", ["deliveryConfirmFlg"] = true, ["purchaseFractionDiv"] = "1",
                ["purchaseTaxFractionDiv"] = "1", ["supplyConsignDiv"] = "1", ["purchaseLotSplitDiv"] = "1",
                ["paymentScheduleDeptCd"] = "DEP", ["paymentClosingDay1"] = 99
            });
        var sale = DirectUse(h, supplier);
        var purchase = DirectUse(h, supplier, "PURCHASE_NEW_SOURCE");
        var participant = new BpUseParticipant(h.Db, protection, TimeProvider.System);
        await using (var tx = await h.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Assert.Equal("BP_ROLE_MISSING", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                participant.EnlistAsync(tx.GetDbTransaction(), sale, default))).Message);
            Assert.Equal("OWNER_PARTICIPANT_UNPROVEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
                participant.EnlistAsync(tx.GetDbTransaction(), purchase with { AccountId = Guid.NewGuid() }, default))).Message);
            var staged = await participant.EnlistAsync(tx.GetDbTransaction(), purchase, default);
            Assert.False(staged.Replayed);
            Assert.Null(staged.Receipt.AccountId);
            Assert.Null(staged.Receipt.Mapping);
            Assert.True(staged.Receipt.Roles["Supplier"]);
            await tx.RollbackAsync();
        }
        Assert.Equal(0, await UseReceiptCount(h));
    }
}
