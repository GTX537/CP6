using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static LocationRuleOwnerV1 StockRules(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var(key,fields) in new[]{("wms-warehouse",new[]{"WarehouseName","AddressText","ManagerCd","Remarks"}),
            ("wms-location",new[]{"LocationName","Barcode"}),("wms-location-rule",LocationRuleValuesV1.Fields),("wms-stock",new[]{"UnitPrice"})})
        {catalog.RegisterScope(key,key,[1,2,3,4,5],1);catalog.RegisterFields(key,fields.Select(f=>new FieldRegistry.Field(f,f)));}catalog.Seal();
        return new(h.Db,new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture));
    }
    private static async Task GrantStockEvidence(Harness h)
    {
        await EngineeringAction(h,"wms-stock","query");
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="wms-stock",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static async Task<(Guid Location,string Warehouse,LocationRuleResultV1 Proposal)> StockEvidenceSetup(Harness h)
    {
        await GrantMaster(h);var native=await SeedMaster(h);await GrantLocationRule(h);await GrantStockEvidence(h);
        var proposal=await StockRules(h).CreateAsync(native.location,new(Guid.NewGuid(),RuleDraft()));
        return(native.location,native.code,proposal);
    }
    private static Stock EvidenceStock(string warehouse,string code="P",decimal physical=60m,decimal allocated=40m)=>new()
    {WarehouseCd=warehouse,LocationCd="L0000",ProductCd=code,LotNo="LOT",UnitCd="EA",PhysicalQty=physical,AllocatedQty=allocated,AvailableQty=physical-allocated,UnitPrice=999m,Creator="price-owner"};
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_exact_actual_facts_do_not_double_count_allocations_or_mix_units_and_warehouses()
    {
        await using var h=await Seed();var f=await StockEvidenceSetup(h);var o=StockRules(h);
        h.Db.Stocks.AddRange(EvidenceStock(f.Warehouse),EvidenceStock(f.Warehouse,"MAX",9999999999999.99999999m,0),
            EvidenceStock("OTHER","OTHER"));await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Stocks.Where(x=>x.ProductCd=="MAX").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.UnitCd,"KG"));
        var r=await o.StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference));
        Assert.Equal("COMPLETE_CURRENT_ROWS",r.StockCoverage);Assert.Equal(2,r.Facts.Length);
        var fact=r.Facts.Single(x=>x.ProductCode=="P");Assert.Equal("60.00000000",fact.PhysicalQuantity);Assert.Equal("40.00000000",fact.AllocatedQuantity);Assert.Equal("20.00000000",fact.AvailableQuantity);
        Assert.Equal("9999999999999.99999999",r.Facts.Single(x=>x.ProductCode=="MAX").PhysicalQuantity);
        Assert.Equal("UNKNOWN",r.OccupancyCoverage);Assert.Null(r.CapacityQuantity);Assert.False(r.AdmissionGrant);
        Assert.Contains("STOCK_CAPACITY_MEASUREMENT_UNPROVEN",r.MissingEvidence);
        var json=JsonSerializer.Serialize(r);Assert.DoesNotContain("UnitPrice",json);Assert.DoesNotContain("OTHER",json);
        Assert.Equal("0",Assert.Single((await o.ContextAsync(f.Location)).Selectors).Revision);
        Assert.Equal(3,await h.Db.Stocks.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_quarantined_empty_stock_is_not_complete_empty_occupancy_or_finite_zero_authority()
    {
        await using var h=await Seed();var id=await RuleSetup(h);await GrantStockEvidence(h);var o=StockRules(h);
        var p=await o.CreateAsync(id,new(Guid.NewGuid(),RuleDraft("0")));
        var r=await o.StockAssessmentAsync(id,p.Reference.Id,p.Etag,new(p.Reference));
        Assert.Empty(r.Facts);Assert.Equal("COMPLETE_CURRENT_ROWS",r.StockCoverage);Assert.Null(r.CapacityQuantity);
        Assert.Equal("UNKNOWN",r.OccupancyCoverage);Assert.Contains("NON_STOCK_OBLIGATION_COVERAGE_UNPROVEN",r.MissingEvidence);
        Assert.True((await h.Db.Locations.IgnoreQueryFilters().SingleAsync(x=>x.Id==id)).IsInitializationQuarantined);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_scoped_empty_and_hidden_archived_rows_never_disclose_whole_location_coverage()
    {
        await using var h=await Seed();var f=await StockEvidenceSetup(h);var o=StockRules(h);
        var hidden=EvidenceStock(f.Warehouse);hidden.Creator="other";var archived=EvidenceStock(f.Warehouse,"OLD");archived.IsDeleted=true;
        h.Db.Stocks.AddRange(hidden,archived);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-stock").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        var r=await o.StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference));
        Assert.Empty(r.Facts);Assert.Equal("VISIBLE_ROWS_ONLY",r.StockCoverage);Assert.DoesNotContain("ARCHIVED_BALANCE_UNRESOLVED",r.MissingEvidence);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="wms-stock").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,5));
        r=await o.StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference));
        Assert.Single(r.Facts);Assert.Equal("ARCHIVED_BALANCE_UNRESOLVED",r.StockCoverage);
        await h.Db.Stocks.Where(x=>x.ProductCd=="OLD").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.PhysicalQty,0).SetProperty(s=>s.AllocatedQty,0).SetProperty(s=>s.AvailableQty,0));
        r=await o.StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference));Assert.Equal("COMPLETE_CURRENT_ROWS",r.StockCoverage);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_truncation_and_inconsistent_native_balances_never_become_capacity_totals()
    {
        await using var h=await Seed();var f=await StockEvidenceSetup(h);var o=StockRules(h);
        for(var i=0;i<101;i++)h.Db.Stocks.Add(EvidenceStock(f.Warehouse,"P"+i));
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Stocks.ExecuteUpdateAsync(x=>x.SetProperty(s=>s.AvailableQty,100));
        var r=await o.StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference));
        Assert.True(r.Truncated);Assert.Equal(100,r.Facts.Length);Assert.Equal("TRUNCATED",r.StockCoverage);
        Assert.Contains("NATIVE_STOCK_BALANCE_INCONSISTENT",r.MissingEvidence);Assert.Null(r.CapacityQuantity);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_current_independent_sources_fields_and_candidate_predecessors_are_required()
    {
        await using var h=await Seed();var f=await StockEvidenceSetup(h);var o=StockRules(h);var p=f.Proposal;
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.StockAssessmentAsync(f.Location,p.Reference.Id,"\"old\"",new(p.Reference)))).Status);
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.StockAssessmentAsync(f.Location,p.Reference.Id,p.Etag,new(p.Reference with{Digest=new string('0',64)})))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-location-rule",FieldName="Capacity",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.StockAssessmentAsync(f.Location,p.Reference.Id,p.Etag,new(p.Reference)))).Message);
        await h.Db.Sys_RoleFieldPerms.Where(x=>x.ResourceKey=="wms-location-rule").ExecuteDeleteAsync();await RevokeTransferAction(h,"wms-stock","query");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.StockAssessmentAsync(f.Location,p.Reference.Id,p.Etag,new(p.Reference)))).Status);
        await using var foreign=await Seed();await GrantMaster(foreign);await GrantLocationRule(foreign);await GrantStockEvidence(foreign);
        Assert.Equal(404,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockRules(foreign).StockAssessmentAsync(f.Location,p.Reference.Id,p.Etag,new(p.Reference)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_final_source_revocation_prevents_disclosure_of_already_read_facts()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await StockEvidenceSetup(h);var hit=false;
        h.Db.Stocks.Add(EvidenceStock(f.Warehouse));await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        hook.Before=async command=>
        {
            if(!command.CommandText.Contains("SELECT clock_timestamp()"))return;hook.Before=null;hit=true;
            await using var revoke=new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"ActionCode\"='query' AND \"MenuId\" IN(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='wms-stock')",(NpgsqlConnection)command.Connection!,(NpgsqlTransaction)command.Transaction!);
            revoke.Parameters.AddWithValue("tenant",h.Tenant);await revoke.ExecuteNonQueryAsync();
        };
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>StockRules(h).StockAssessmentAsync(f.Location,f.Proposal.Reference.Id,f.Proposal.Etag,new(f.Proposal.Reference)))).Status);
        Assert.True(hit);Assert.Single(await h.Db.Stocks.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LocationStock_actual_Program_HTTP_enforces_exact_DTO_current_source_and_no_store()
    {
        await using var h=await Seed();var f=await StockEvidenceSetup(h);h.Db.Stocks.Add(EvidenceStock(f.Warehouse));await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);
        var uri=$"api/wms/locations/{f.Location}/rules-v1/{f.Proposal.Reference.Id}/stock-assessment";
        async Task<HttpResponseMessage> Send(object body,bool etag=true)
        {var request=OperationsRequest(cookies,HttpMethod.Post,uri,body);if(etag)request.Headers.TryAddWithoutValidation("If-Match",f.Proposal.Etag);return await host.Send(request);}
        using var ok=await Send(new{proposal=f.Proposal.Reference});Assert.Equal(HttpStatusCode.OK,ok.StatusCode);Assert.True(ok.Headers.CacheControl?.NoStore);
        var doc=JsonDocument.Parse(await ok.Content.ReadAsStringAsync());Assert.Equal("60.00000000",doc.RootElement.GetProperty("data").GetProperty("facts")[0].GetProperty("physicalQuantity").GetString());
        using var forged=await Send(new{proposal=f.Proposal.Reference,coverage="COMPLETE"});Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);
        using var noEtag=await Send(new{proposal=f.Proposal.Reference},false);Assert.Equal((HttpStatusCode)428,noEtag.StatusCode);
        await RevokeTransferAction(h,"wms-stock","query");using var denied=await Send(new{proposal=f.Proposal.Reference});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }
}
