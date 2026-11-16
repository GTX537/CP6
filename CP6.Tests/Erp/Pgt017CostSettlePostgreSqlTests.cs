using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string CostSettlePermission = "fin-cost";
    private const string CostSettlementWorkOrder = "WO-PGT-W612";
    private const string CostSettlementPath = "/api/fin/cost/" + CostSettlementWorkOrder + "/settle";

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_cost_settle_and_pause_share_commit_order(bool pauseFirst)
    {
        await using var writer = await Seed();
        await using var other = await Seed();
        await using var administrator = await TenantManagementSeed();
        var sheet = await SeedCostSettlement(writer);
        await SeedCostSettlement(other);
        await GrantLegacyAuthorityAction(writer, CostSettlePermission, "settle");
        var connection = writer.Db.Database.GetConnectionString()!;
        await using var host = await StartAssetApi(writer);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var tenant = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var suspend = new TenantManagementCommandV1("cost-settle-pause", tenant.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(writer, administrator.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, writer.Tenant);
        var otherBefore = await ReadCostSettlement(observer, other.Tenant);

        var race = await ObserveAssetRace(connection, observer, "Fin_JournalEntry", "INSERT", "Sys_Tenants", "UPDATE", pauseFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadCostSettlement(observer, writer.Tenant));
                Assert.Equal(otherBefore, await ReadCostSettlement(observer, other.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(writer, administrator.User));
            });
        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(writer, administrator.User), receipt, suspend);
        var after = await ReadCostSettlement(observer, writer.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, after);
        }
        else
        {
            await CheckCostSettlement(business, sheet, before, after);
        }

        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await ReadCostSettlement(observer, writer.Tenant));
        Assert.Equal(otherBefore, await ReadCostSettlement(observer, other.Tenant));
        if (pauseFirst)
        {
            await ResumeAssetTenant(host, manager, writer.Tenant, receipt);
            actor = await Login(host, writer);
            using var resumed = await Write();
            await CheckCostSettlement(resumed, sheet, before, await ReadCostSettlement(observer, writer.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new { PauseFirst = pauseFirst, race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, CostSettlementPath));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_cost_settle_and_action_revocation_share_commit_order(bool revokeFirst)
    {
        await using var writer = await Seed();
        await SeedFenceAdministrator(writer);
        var sheet = await SeedCostSettlement(writer);
        await GrantLegacyAuthorityAction(writer, CostSettlePermission, "settle");
        var connection = writer.Db.Database.GetConnectionString()!;
        await using var host = await StartAssetApi(writer);
        var actor = await Login(host, writer);
        var manager = await Login(host, "fence-manager", "Fence1!Password", writer.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "settle");
        var authority = await ReadPolicyWrites(writer, revoke.OperationId);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, writer.Tenant);

        var race = await ObserveAssetRace(connection, observer, "Fin_JournalEntry", "INSERT", "Sys_RoleAction", "DELETE", revokeFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)),
            async () =>
            {
                Assert.Equal(before, await ReadCostSettlement(observer, writer.Tenant));
                Assert.Equal(authority, await ReadPolicyWrites(writer, revoke.OperationId));
            });
        using var revoked = race.Authority;
        using var business = race.Business;
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var current = await ReadPolicyWrites(writer, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, current.RoleRevision);
        Assert.Equal((authority.Generation + 1, authority.Snapshot + 1, authority.Outbox + 1, 1L, 1L, 1L),
            (current.Generation, current.Snapshot, current.Outbox, current.CurrentOutbox, current.Audits, current.Receipts));
        Assert.DoesNotContain("settle", current.Actions.Split(','));
        var after = await ReadCostSettlement(observer, writer.Tenant);
        if (revokeFirst)
        {
            await CheckApDenied(business, "IAM_FORBIDDEN");
            Assert.Equal(before, after);
        }
        else
        {
            await CheckCostSettlement(business, sheet, before, after);
        }
        using var stale = await Write();
        await CheckApDenied(stale, "无权限：fin-cost:settle");
        Assert.Equal(after, await ReadCostSettlement(observer, writer.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst, race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, CostSettlementPath));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_cost_settle_third_leg_failure_rolls_back_all_cost_writes()
    {
        await using var writer = await Seed();
        var sheet = await SeedCostSettlement(writer);
        await GrantLegacyAuthorityAction(writer, CostSettlePermission, "settle");
        var cogs = await writer.Db.GlAccounts.SingleAsync(account => account.Role == "COGS");
        cogs.IsLeaf = false;
        await writer.Db.SaveChangesAsync();
        await using var host = await StartAssetApi(writer);
        var actor = await Login(host, writer);
        await using var observer = new NpgsqlConnection(writer.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, writer.Tenant);

        using var rejected = await host.Send(OperationsRequest(actor, HttpMethod.Post, CostSettlementPath));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("E-FIN-104", await rejected.Content.ReadAsStringAsync());
        var after = await ReadCostSettlement(observer, writer.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new { Before = before.Data, After = after.Data }));
        Assert.Equal(before, after);

        cogs.IsLeaf = true;
        await writer.Db.SaveChangesAsync();
        using var accepted = await host.Send(OperationsRequest(actor, HttpMethod.Post, CostSettlementPath));
        await CheckCostSettlement(accepted, sheet, before, await ReadCostSettlement(observer, writer.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_cost_collect_without_work_order_query_rejects_before_any_native_write()
    {
        await using var writer = await Seed();
        await SeedCostSettlement(writer);
        await GrantLegacyAuthorityAction(writer, CostSettlePermission, "collect");
        await using var host = await StartAssetApi(writer);
        var actor = await Login(host, writer);
        await using var observer = new NpgsqlConnection(writer.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, writer.Tenant);

        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post,
            $"/api/fin/cost/{CostSettlementWorkOrder}/collect", new { LaborStd = 18.34m, OverheadStd = 6.50m }));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("ACTION_FORBIDDEN", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await ReadCostSettlement(observer, writer.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_cost_settle_zero_value_preserves_original_no_voucher_behavior()
    {
        await using var writer = await Seed();
        var sheet = await SeedCostSettlement(writer, zero: true);
        await GrantLegacyAuthorityAction(writer, CostSettlePermission, "settle");
        await using var host = await StartAssetApi(writer);
        var actor = await Login(host, writer);
        await using var observer = new NpgsqlConnection(writer.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadCostSettlement(observer, writer.Tenant);

        using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, CostSettlementPath));
        await ReadApSuccessData(response);
        var after = await ReadCostSettlement(observer, writer.Tenant);
        var saved = after.Row("sheets", sheet.Id);
        Assert.Equal((int)CostSheetStatus.Settled, AssetInt(saved, "Status"));
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("JournalEntryId").ValueKind);
        Assert.Equal(before.Rows("sheetLines").Select(row => row.GetRawText()), after.Rows("sheetLines").Select(row => row.GetRawText()));
        foreach (var table in new[] { "journals", "lines", "periods", "numbers" })
            Assert.Empty(after.Rows(table));
    }

    private static async Task<CostSheet> SeedCostSettlement(Harness writer, bool zero = false)
    {
        await new GlAccountService(writer.Db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        var sheet = new CostSheet
        {
            No = "CS-W612-0001",
            WorkOrderNo = CostSettlementWorkOrder,
            CompletedQty = 2.34567891m,
            Status = CostSheetStatus.Collected,
            MaterialActual = zero ? 0m : 63.12m,
            MaterialStandard = zero ? 0m : 53.14m,
            LaborActual = zero ? 0m : 18.34m,
            LaborStandard = zero ? 0m : 18.34m,
            OverheadActual = zero ? 0m : 6.50m,
            OverheadStandard = zero ? 0m : 6.50m,
        };
        sheet.Lines.Add(new CostSheetLine
        {
            LineNo = 1, Element = CostElement.Material, ActualAmount = sheet.MaterialActual, StandardAmount = sheet.MaterialStandard,
        });
        writer.Db.CostSheets.Add(sheet);
        await writer.Db.SaveChangesAsync();
        return sheet;
    }

    private static async Task CheckCostSettlement(HttpResponseMessage response, CostSheet sheet, AssetRows before, AssetRows after)
    {
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(response)).ValueKind);
        var persisted = Assert.Single(after.Rows("sheets"));
        Assert.Equal(sheet.Id, AssetId(persisted));
        Assert.Equal((int)CostSheetStatus.Settled, AssetInt(persisted, "Status"));
        Assert.Equal(2.34567891m, AssetDecimal(persisted, "CompletedQty"));
        Assert.Equal("price-owner", persisted.GetProperty("Modifier").GetString());
        Assert.Equal(before.Rows("sheetLines").Select(row => row.GetRawText()), after.Rows("sheetLines").Select(row => row.GetRawText()));
        var journals = after.Rows("journals").ToDictionary(row => row.GetProperty("SourceDocNo").GetString()!, row => AssetId(row));
        Assert.Equal(3, journals.Count);
        Assert.Equal(AssetId(persisted, "JournalEntryId"), journals[sheet.No + "#FG"]);
        var wip = CheckApJournal(after, journals[sheet.No + "#WIP"], sheet.No + "#WIP", 87.96m, VoucherSource.Cost);
        var fg = CheckApJournal(after, journals[sheet.No + "#FG"], sheet.No + "#FG", 77.98m, VoucherSource.Cost);
        var variance = CheckApJournal(after, journals[sheet.No + "#VAR"], sheet.No + "#VAR", 9.98m, VoucherSource.Cost);
        var wipAccount = AssetId(Assert.Single(wip, line => AssetDecimal(line, "Debit") > 0), "AccountId");
        Assert.Equal(wipAccount, AssetId(Assert.Single(fg, line => AssetDecimal(line, "Credit") > 0), "AccountId"));
        Assert.Equal(wipAccount, AssetId(Assert.Single(variance, line => AssetDecimal(line, "Credit") > 0), "AccountId"));
        var sequence = Assert.Single(after.Rows("numbers"));
        Assert.Equal("GL", sequence.GetProperty("SeqKey").GetString());
        Assert.Equal(3, AssetInt(sequence, "CurrentValue"));
        Assert.Equal(8, after.Rows("lines").Length);
    }

    private static async Task<AssetRows> ReadCostSettlement(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'sheets', ARRAY(SELECT to_jsonb(r) FROM public."Fin_CostSheet" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'sheetLines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_CostSheetLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('CostSheet','CostSheetLine','JournalEntry','JournalLine','FiscalPeriod','FinSequence') ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }
}
