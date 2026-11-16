using System.Globalization;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DTOs.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task W689_cost_version_resolve_never_substitutes_an_older_visible_rate()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        await SetW689RateScopeOwn(h);
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.LaborRate == 9m)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidTo, CostCollectEnd));
        await h.Db.ProcessCostRates.Where(x => x.WgCd == "W686-A" && x.LaborRate == 123.45678901m)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "outside-operator"));

        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        var day = Uri.EscapeDataString(CostCollectEnd.ToString("s", CultureInfo.InvariantCulture));
        var uri = $"/api/mes/process-cost-rate/owner-v1/resolve?wgCd=W686-A&onDate={day}";
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, uri));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        using var collect = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
        Assert.Equal(HttpStatusCode.Forbidden, collect.StatusCode);
        Assert.False(await h.Db.CostSheets.AnyAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task W689_cost_version_is_immutable_and_settlement_binds_the_saved_policy_and_rate()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        await GrantLegacyAuthorityAction(h, "fin-cost", "view");
        await SetW689RateScopeOwn(h);
        await new GlAccountService(h.Db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        h.Db.ChangeTracker.Clear();
        var nativeRate = await h.Db.ProcessCostRates.AsNoTracking().SingleAsync(x => x.WgCd == "W686-A" && x.ValidTo == CostCollectEnd);
        var centerId = await h.Db.WorkCenters.Where(x => x.WgCd == "W686-A").Select(x => x.Id).SingleAsync();
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        var path = $"/api/fin/cost/{number}";
        await PostOk(path + "/collect", new { });
        var sheet = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal((18.23m, 263.16m, 56.96m), (sheet.MaterialActual, sheet.LaborActual, sheet.OverheadActual));
        var original = await ReadW689Basis(h, sheet.Id, 1);
        using var parsed = JsonDocument.Parse(original);
        Assert.Equal("CP6.A2.v1", parsed.RootElement.GetProperty("PolicyVersion").GetString());
        Assert.Equal(CostCollectEnd, parsed.RootElement.GetProperty("RateDate").GetDateTime());
        var appliedRate = parsed.RootElement.GetProperty("Rates").EnumerateArray()
            .Single(item => item.GetProperty("RateId").GetGuid() == nativeRate.Id);
        Assert.Equal(centerId, appliedRate.GetProperty("WorkCenterId").GetGuid());
        Assert.Equal(nativeRate.RowVersion, appliedRate.GetProperty("SourceRowVersion").GetBytesFromBase64());
        Assert.Equal((nativeRate.ValidFrom, nativeRate.ValidTo, 123.45678901m, 45.12345678m),
            (appliedRate.GetProperty("ValidFrom").GetDateTime(), (DateTime?)appliedRate.GetProperty("ValidTo").GetDateTime(),
                appliedRate.GetProperty("LaborRate").GetDecimal(), appliedRate.GetProperty("OverheadRate").GetDecimal()));

        await AmendRate("100");
        using var history = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var observed = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Equal(263.16m, observed.RootElement.GetProperty("data").GetProperty("laborActual").GetDecimal());

        await PostOk(path + "/collect", new { });
        var recollected = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal((sheet.Id, sheet.No, 213.35m), (recollected.Id, recollected.No, recollected.LaborActual));
        Assert.Equal(original, await ReadW689Basis(h, sheet.Id, 1));
        using var latest = JsonDocument.Parse(await ReadW689Basis(h, sheet.Id, 2));
        Assert.Equal(100m, latest.RootElement.GetProperty("Rates")[0].GetProperty("LaborRate").GetDecimal());
        Assert.False(nativeRate.RowVersion!.SequenceEqual(latest.RootElement.GetProperty("Rates")[0].GetProperty("SourceRowVersion").GetBytesFromBase64()));
        await AmendRate("1");

        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/settle"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(await h.Db.JournalEntries.AnyAsync());
        await GrantLegacyAuthorityAction(h, "fin-cost", "settle");

        await h.Db.CostSheets.ExecuteUpdateAsync(s => s.SetProperty(x => x.LaborActual, recollected.LaborActual + 0.01m));
        using var conflict = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/settle"));
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
        Assert.Contains("COST_COLLECTION_VERSION_CONFLICT", await conflict.Content.ReadAsStringAsync());
        Assert.False(await h.Db.JournalEntries.AnyAsync());
        await h.Db.CostSheets.ExecuteUpdateAsync(s => s.SetProperty(x => x.LaborActual, recollected.LaborActual));
        await PostOk(path + "/settle");

        var settled = await h.Db.CostSheets.AsNoTracking().SingleAsync();
        Assert.Equal(CostSheetStatus.Settled, settled.Status);
        var journals = await h.Db.JournalEntries.Include(x => x.Lines).AsNoTracking()
            .Where(x => x.Source == VoucherSource.Cost).ToListAsync();
        Assert.Equal(3, journals.Count);
        Assert.All(journals, row => Assert.Equal(JournalStatus.Posted, row.Status));
        var wip = journals.Single(row => row.SourceDocNo == sheet.No + "#WIP");
        var fg = journals.Single(row => row.SourceDocNo == sheet.No + "#FG");
        var variance = journals.Single(row => row.SourceDocNo == sheet.No + "#VAR");
        Assert.Equal(recollected.TotalActual, wip.Lines.Sum(row => row.Debit));
        Assert.Contains(wip.Lines, row => row.Credit == 213.35m);
        Assert.Equal(recollected.StandardCost, fg.Lines.Sum(row => row.Debit));
        Assert.Equal(Math.Abs(recollected.Variance), variance.Lines.Sum(row => row.Debit));
        Assert.True(await h.Db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM cp6_fin_cost_basis_v1.settlements
              WHERE tenant_id={h.Tenant} AND cost_sheet_id={sheet.Id} AND basis_version=2 AND actor='price-owner'
                AND wip_journal_id={wip.Id} AND fg_journal_id={fg.Id} AND variance_journal_id={variance.Id}) AS "Value"
            """).SingleAsync());
        using var duplicate = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/settle"));
        Assert.Contains("E-FIN-402", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(3, await h.Db.JournalEntries.CountAsync());
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_fin_cost_basis_v1.versions SET basis=basis WHERE tenant_id={h.Tenant} AND cost_sheet_id={sheet.Id}
            """));
        Assert.Equal("55000", immutable.SqlState);
        Assert.Equal(original, await ReadW689Basis(h, sheet.Id, 1));

        await h.Db.ProcessCostRates.Where(x => x.Id == nativeRate.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Creator, "outside-operator"));
        using var outside = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path));
        Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
        Assert.DoesNotContain("213.35", await outside.Content.ReadAsStringAsync());

        async Task AmendRate(string value)
        {
            var row = await h.Db.ProcessCostRates.AsNoTracking().SingleAsync(x => x.Id == nativeRate.Id);
            await PostOk("/api/mes/process-cost-rate/owner-v1/upsert", new ProcessCostRateOwnerV1
            {
                Id = row.Id, RowVersion = row.RowVersion, WgCd = row.WgCd, ValidFrom = row.ValidFrom,
                ValidTo = row.ValidTo, LaborRate = value,
            });
        }
        async Task PostOk(string uri, object? request = null)
        {
            using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Post, uri, request));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task W689_cost_version_rejects_conflicting_latest_rates_before_writing()
    {
        await using var h = await Seed();
        var number = await SeedNativeCostCollect(h);
        await GrantNativeCostCollect(h);
        h.Db.ProcessCostRates.Add(new()
        {
            WgCd = "W686-A", LaborRate = 200m, OverheadRate = 50m, Creator = "price-owner",
            ValidFrom = new(2026, 6, 1), ValidTo = CostCollectEnd,
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await using var host = await StartAssetApi(h);
        var cookies = await Login(host, h);
        var day = Uri.EscapeDataString(CostCollectEnd.ToString("s", CultureInfo.InvariantCulture));
        using var resolve = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            $"/api/mes/process-cost-rate/owner-v1/resolve?wgCd=W686-A&onDate={day}"));
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode);
        using var collect = await host.Send(OperationsRequest(cookies, HttpMethod.Post, $"/api/fin/cost/{number}/collect", new { }));
        Assert.Equal(HttpStatusCode.Conflict, collect.StatusCode);
        Assert.Contains("RATE_PERIOD_CONFLICT", await collect.Content.ReadAsStringAsync());
        Assert.False(await h.Db.CostSheets.AnyAsync());
        Assert.False(await h.Db.FinSequences.AnyAsync());
    }

    private static Task<string> ReadW689Basis(Harness h, Guid sheetId, long version)
        => h.Db.Database.SqlQuery<string>($"""
            SELECT basis::text AS "Value" FROM cp6_fin_cost_basis_v1.versions
            WHERE tenant_id={h.Tenant} AND cost_sheet_id={sheetId} AND version={version}
            """).SingleAsync();

    private static Task SetW689RateScopeOwn(Harness h)
        => h.Db.Sys_RoleDataScopes.Where(x => x.ResourceKey == "mes-process-cost-rate")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType, 1));
}
