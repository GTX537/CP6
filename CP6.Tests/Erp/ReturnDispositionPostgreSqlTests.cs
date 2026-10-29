using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ReturnDispositionFixture(ReturnQualityFixture Quality,ReturnDispositionSimulationOptionsV2 Options,Guid Application);
    private static ReturnDispositionOwnerV2 Disposition(Harness h,ReturnDispositionFixture f,string kind)
    {
        var owner =FormalRmaAuthority(h); var quality =QualityReturn(h,f.Quality.Physical,f.Quality.Simulation);
        var policy =new ReturnDispositionPolicyV2(Options.Create(f.Options)); var stock =new StockMovementService(h.Db,new WmsSequenceService(h.Db),owner:owner);
        var qc =new StockQcService(h.Db,NullLogger<StockQcService>.Instance,owner);
        return kind switch { "RESALE_STOCK" =>new ReturnResaleOwnerV2(h.Db,owner,quality,policy,stock,qc),
            "REPAIR" =>new ReturnRepairTransferOwnerV2(h.Db,owner,quality,policy,stock,qc),"SCRAP" =>new ReturnScrapOwnerV2(h.Db,owner,quality,policy,stock,qc),
            "SUPPLIER_RETURN" =>new SupplierReturnShipmentOwnerV2(h.Db,owner,quality,policy,stock,qc),_ =>new CustomerReturnShipmentOwnerV2(h.Db,owner,quality,policy,stock,qc) };
    }
    private async Task<ReturnDispositionFixture> DispositionSetup(Harness h,bool originalRoot =true)
    {
        var f =await QualityReturnSetup(h,originalRoot); var app =f.Physical.Approval.Application.ApplicationId;
        foreach (var definition in ReturnDispositionOwnersV2.All)
        {
            foreach (var action in new[] { "query","prepare","post","seal" }) await EngineeringAction(h,definition.Resource,action);
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey =definition.Resource,ScopeType =5 });
        }
        await EngineeringAction(h,"wms-stock-qc","return-release"); h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="wms-stock-qc",ScopeType =5 });
        foreach (var action in new[] { "disposition-prepare","disposition-apply","disposition-seal" }) await EngineeringAction(h,"wms-rma",action);
        var sale =new Location { WarehouseCd ="W01",LocationCd ="RMA-SALE",LocationName ="Resale",AreaCd ="A",IsPickable =true };
        var repair =new Location { WarehouseCd ="W01",LocationCd ="RMA-REPAIR",LocationName ="Repair",AreaCd ="A",IsPickable =false };
        h.Db.Locations.AddRange(sale,repair); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var input =await QualityDecisionInput(f);
        input =input with { Partitions =input.Partitions.Select(p => p with { Ranges =p.Quality switch {
            "ACCEPTED" =>[new("0","0.2"),new("0.8","1")],"REJECTED" =>[new("0.2","0.8")],_ =>[] } }).ToArray() };
        var decision =await f.Reviewer.DecideAsync(app,f.Inspection.Inspection.Id,Guid.NewGuid(),input);
        var source =Assert.Single((await f.Applicant.ContextAsync(app)).Receipts).Source;
        await f.Applicant.ObserveAsync(app,f.Inspection.Inspection.Id,Guid.NewGuid(),new(decision.Value.Decision!.Result.DecisionRef,source.PhysicalHeadRef));
        FulfilmentRefV1 Evidence(string kind) =>new("Simulation."+kind,Guid.NewGuid().ToString(),"1",new string('d',64));
        var cases =ReturnDispositionOwnersV2.All.Select(d => new ReturnDispositionSimulationCaseV2(h.Tenant,source.SiteId,source.QualityPolicyRef,d.Kind,"1",d.Kind+" approved destination",
            d.Kind =="RESALE_STOCK" ? ["ACCEPTED"] : ["ACCEPTED","REJECTED"],d.Kind =="RESALE_STOCK" ? sale.Id : d.Kind =="REPAIR" ? repair.Id : null,
            Evidence(d.Kind+".Approval"),Evidence(d.Kind+".Authority"),d.PostingKind =="SHIP" ? Evidence(d.Kind+".Party") : null,
            d.PostingKind =="SHIP" ? Evidence(d.Kind+".ExternalCustody") : null,[Evidence(d.Kind+".Evidence")])).ToArray();
        return new(f,new() { Enabled =true,CasesJson =FormalJson(cases) },app);
    }
    private static ReturnDispositionPrepareV2 DispositionInput(ReturnDispositionContextV2 context,string from,string to)
    {
        var f =decimal.Parse(from,System.Globalization.CultureInfo.InvariantCulture); var t =decimal.Parse(to,System.Globalization.CultureInfo.InvariantCulture);
        var choice =context.Choices.Single(x => x.Options.Length >0 && RmaRangeSetV1.Subset([new(f,t)],RmaRangeSetV1.Read(x.Proposal.EligibleRanges)));
        return new(choice.Proposal.ProposalRef,choice.Proposal.SelectionRef,choice.Options[0].Reference,[new(from,to)],"Explicit simulated business approval for actual disposition");
    }
    private static ReturnDispositionActionV2 DispositionAction(ReturnDispositionViewV2 view) =>new(view.WorkRef,"Execute exact prepared return disposition");
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_five_owners_apply_actual_move_loss_ship_and_precise_quality_release()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var results =new List<ReturnDispositionViewV2>();
        var definitions =ReturnDispositionOwnersV2.All;
        for (var i =0;i <definitions.Length;i++)
        {
            var d =definitions[i]; var service =Disposition(h,f,d.Kind); var context =await service.ContextAsync(f.Application);
            var input =DispositionInput(context,RmaRangeSetV1.Text(i*0.2m),RmaRangeSetV1.Text((i+1)*0.2m));
            var prepared =await service.PrepareAsync(f.Application,Guid.NewGuid(),input);
            Assert.Equal(d.PostingKind,prepared.Value.Preparation.Stock.Request.Data.GetProperty("kind").GetString());
            var applied =await service.CommitAsync(f.Application,prepared.Value.Preparation.WorkId,Guid.NewGuid(),DispositionAction(prepared.Value));
            Assert.Equal("APPLIED",applied.Value.State); Assert.Equal(d.Owner,applied.Value.Result!.Reference.Owner);
            Assert.Equal(d.PostingKind =="MOVE" ? 2 : 1,applied.Value.Result.StockResult.Result!.RelatedMovementIds.Length);
            Assert.Equal("FOUND",(await service.OutcomeAsync(f.Application,prepared.Value.Preparation.WorkId)).Status); results.Add(applied.Value);
        }
        Assert.Equal(0m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        var sale =await h.Db.Stocks.AsNoTracking().SingleAsync(x => x.LocationCd =="RMA-SALE"); var repair =await h.Db.Stocks.AsNoTracking().SingleAsync(x => x.LocationCd =="RMA-REPAIR");
        Assert.Equal(0.2m,sale.PhysicalQty); Assert.Equal(StockQcStatus.Passed,sale.QcStatus); Assert.Equal(0.2m,repair.PhysicalQty); Assert.Equal(StockQcStatus.Pending,repair.QcStatus);
        Assert.NotNull(results[0].Result!.QualityReleaseRef); Assert.All(results.Skip(1),x => Assert.Null(x.Result!.QualityReleaseRef));
        Assert.Equal(5,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_return_disposition_v2.receipts WHERE tenant_id={h.Tenant}").SingleAsync());
        var wire =Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE"); if (wire !=null) await File.WriteAllTextAsync(wire,FormalJson(results));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_cross_owner_claims_serialize_and_only_durable_seal_releases_ranges()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var first =Disposition(h,f,"SCRAP");
        await using var db2 =server.Context(new TenantContext { CurrentTenantId =h.Tenant });
        var second =Disposition(h with { Db =db2 },f,"SUPPLIER_RETURN");
        var a =DispositionInput(await first.ContextAsync(f.Application),"0.2","0.4"); var b =DispositionInput(await second.ContextAsync(f.Application),"0.2","0.4");
        async Task<(ReturnDispositionOwnerV2 Owner,ReturnDispositionViewV2? Value,string? Error)> Attempt(ReturnDispositionOwnerV2 owner,ReturnDispositionPrepareV2 input)
        { try { return (owner,(await owner.PrepareAsync(f.Application,Guid.NewGuid(),input)).Value,null); } catch (OrderPriceCorrectionException e) { return (owner,null,e.Message); } }
        var outcomes =await Task.WhenAll(Attempt(first,a),Attempt(second,b)); var winner =Assert.Single(outcomes,x => x.Value !=null);
        Assert.Equal("DISPOSITION_PROPOSAL_CHANGED",Assert.Single(outcomes,x => x.Error !=null).Error);
        var work =winner.Value!; var pending =await winner.Owner.OutcomeAsync(f.Application,work.Preparation.WorkId); Assert.Equal("PENDING",pending.Status); Assert.False(pending.CanInferNoEffect);
        var before =await h.Db.StockTransactions.CountAsync(); var seal =await winner.Owner.SealAsync(f.Application,work.Preparation.WorkId,Guid.NewGuid(),DispositionAction(work));
        Assert.Equal("SEALED_NO_EFFECT",seal.Value.State); Assert.True((await winner.Owner.OutcomeAsync(f.Application,work.Preparation.WorkId)).CanInferNoEffect);
        Assert.Equal(before,await h.Db.StockTransactions.CountAsync()); Assert.NotNull(DispositionInput(await first.ContextAsync(f.Application),"0.2","0.4"));
        Assert.Equal("DISPOSITION_ALREADY_TERMINAL",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => winner.Owner.CommitAsync(f.Application,work.Preparation.WorkId,Guid.NewGuid(),DispositionAction(seal.Value)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_quality_correction_invalidates_preparation_and_hold_pending_never_offer_execution()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var service =Disposition(h,f,"RESALE_STOCK");
        var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0","0.2"))).Value;
        var q =f.Quality; var decision =await q.Reviewer.DecideAsync(f.Application,q.Inspection.Inspection.Id,Guid.NewGuid(),await QualityDecisionInput(q));
        Assert.False((await service.ReadAsync(f.Application,p.Preparation.WorkId)).QualityCurrent);
        Assert.Equal("DISPOSITION_QUALITY_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p)))).Message);
        var source =Assert.Single((await q.Applicant.ContextAsync(f.Application)).Receipts).Source;
        await q.Applicant.ObserveAsync(f.Application,q.Inspection.Inspection.Id,Guid.NewGuid(),new(decision.Value.Decision!.Result.DecisionRef,source.PhysicalHeadRef));
        var current =await service.ContextAsync(f.Application); Assert.All(current.Choices.Where(x => x.Proposal.Quality is "HOLD" or "PENDING"),x => Assert.Empty(x.Options));
        await service.SealAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p));
        Assert.Equal(1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_new_source_exact_spans_preserve_other_reservations_and_detect_pool_changes()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h,false);
        await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.AllocatedQty,0.25m).SetProperty(x => x.AvailableQty,0.75m));
        foreach (var (kind,from,to) in new[] { ("REPAIR","0.2","0.20000001"),("SUPPLIER_RETURN","0.20000001","0.4"),("SCRAP","0.4","0.6") })
        {
            var service =Disposition(h,f,kind); var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),from,to))).Value;
            var span =Assert.Single(p.Preparation.Stock.Spans); Assert.Equal(from,span.From); Assert.Equal(to,span.To);
            await service.CommitAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p));
            Assert.Equal(0.25m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.AllocatedQty).SingleAsync());
        }
        var sale =Disposition(h,f,"RESALE_STOCK"); var prepared =(await sale.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await sale.ContextAsync(f.Application),"0","0.2"))).Value;
        await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.AllocatedQty,0.3m).SetProperty(x => x.AvailableQty,0.3m));
        Assert.Equal("STOCK_POOL_VERSION_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => sale.CommitAsync(f.Application,prepared.Preparation.WorkId,Guid.NewGuid(),DispositionAction(prepared)))).Message);
        Assert.Equal(0.6m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_stock_qc_release_receipt_and_outbox_rollback_with_original_journal()
    {
        var fault =new FailReturnQualityJournal(); await using var h =await Seed(fault); var f =await DispositionSetup(h); var service =Disposition(h,f,"RESALE_STOCK");
        var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0","0.2"))).Value;
        var count =await h.Db.StockTransactions.CountAsync(); var command =Guid.NewGuid(); fault.Armed =true;
        Assert.Equal("INJECTED_RETURN_QUALITY_JOURNAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p)))).Message); Assert.True(fault.Reached); fault.Armed =false;
        Assert.Equal(count,await h.Db.StockTransactions.CountAsync()); Assert.False(await h.Db.Stocks.AnyAsync(x => x.LocationCd =="RMA-SALE"));
        Assert.Equal("PREPARED",(await service.ReadAsync(f.Application,p.Preparation.WorkId)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT (SELECT count(*) FROM cp6_return_disposition_v2.quality_releases WHERE tenant_id={h.Tenant})::integer+(SELECT count(*) FROM cp6_return_disposition_v2.receipts WHERE tenant_id={h.Tenant})::integer+(SELECT count(*) FROM cp6_return_disposition_v2.outbox WHERE tenant_id={h.Tenant})::integer AS \"Value\"").SingleAsync());
        Assert.Equal("NOT_OBSERVED",(await service.QueryAsync(new(f.Application,command))).Status);
        var applied =await service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p));
        Assert.Equal(FormalJson(applied),FormalJson(await service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p)))); Assert.Equal(count+2,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_lost_ack_recovers_after_write_revocation_and_current_reads_remain_authorized()
    {
        var fault =new CommitFault(); await using var h =await Seed(fault); var f =await DispositionSetup(h); var service =Disposition(h,f,"SUPPLIER_RETURN");
        var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0.2","0.4"))).Value;
        var command =Guid.NewGuid(); var count =await h.Db.StockTransactions.CountAsync(); fault.Armed =true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p)))).Message);
        await RevokePhysicalGrant(h,service.Definition.Resource,"post"); var recovered =(await service.QueryAsync(new(f.Application,command))).Value!;
        Assert.Equal("APPLIED",recovered.Value.State); Assert.Equal(FormalJson(recovered),FormalJson(await service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p))));
        Assert.Equal("COMMAND_BINDING_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p) with { Reason ="Different intent" }))).Message);
        Assert.Equal(count+1,await h.Db.StockTransactions.CountAsync());
        await RevokePhysicalGrant(h,"quality-return-inspection","query"); Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.QueryAsync(new(f.Application,command)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_qc_authority_and_destination_capacity_are_current_and_atomic()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var service =Disposition(h,f,"RESALE_STOCK");
        var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0","0.2"))).Value;
        await h.Db.Locations.Where(x => x.LocationCd =="RMA-SALE").ExecuteUpdateAsync(s => s.SetProperty(x => x.CapacityQty,0.1m));
        Assert.Equal("RETURN_LOCATION_CAPACITY_EXCEEDED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p)))).Message);
        await h.Db.Locations.Where(x => x.LocationCd =="RMA-SALE").ExecuteUpdateAsync(s => s.SetProperty(x => x.CapacityQty,0m));
        await RevokePhysicalGrant(h,"wms-stock-qc","return-release");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.CommitAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p)))).Status);
        Assert.Equal(1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        Assert.False(await h.Db.Stocks.AnyAsync(x => x.LocationCd =="RMA-SALE"));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_actual_HTTP_executes_owner_with_csrf_and_original_read_recovery()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var app =f.Application;
        var coordinates =Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host =await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] =ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"] ="DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] =coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"] =coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] =coordinates.SiteId.ToString(),["ReturnQualitySimulation__Enabled"] ="true",["ReturnQualitySimulation__CasesJson"] =f.Quality.Simulation.CasesJson,
            ["ReturnDispositionSimulation__Enabled"] ="true",["ReturnDispositionSimulation__CasesJson"] =f.Options.CasesJson });
        var cookies =await DocumentReviewerLogin(host,h,f.Quality.Physical.Approval.Reviewer.User);
        async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode status =HttpStatusCode.OK)
        {
            using var response =await host.Send(request); var text =await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode ==status,response.StatusCode+": "+text); Assert.True(response.Headers.CacheControl?.NoStore); return JsonSerializer.Deserialize<T>(text,RmaJson)!;
        }
        var path =$"api/wms/rma/v2/dispositions/RETURN_TO_CUSTOMER/applications/{app}";
        var context =await Receive<ReturnDispositionContextV2>(FormalRequest(cookies,HttpMethod.Get,path+"/context"));
        var input =DispositionInput(context,"0.8","1"); var command =Guid.NewGuid();
        var noCsrf =FormalRequest(cookies,HttpMethod.Post,path+"/works",input,command); noCsrf.Headers.Remove("X-CSRF-TOKEN");
        using (var denied =await host.Send(noCsrf)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var p =(await Receive<ReturnDispositionCommandResultV2>(FormalRequest(cookies,HttpMethod.Post,path+"/works",input,command))).Value;
        var appliedCommand =Guid.NewGuid(); var saved =await Receive<ReturnDispositionCommandResultV2>(FormalRequest(cookies,HttpMethod.Post,path+$"/works/{p.Preparation.WorkId}/commit",DispositionAction(p),appliedCommand));
        Assert.Equal("APPLIED",saved.Value.State); await RevokePhysicalGrant(h,"wms-customer-return-shipment","post");
        var query ="api/wms/rma/v2/dispositions/RETURN_TO_CUSTOMER/commands/query";
        var recovered =await Receive<ReturnDispositionQueryResultV2>(FormalRequest(cookies,HttpMethod.Post,query,new ReturnDispositionQueryV2(app,appliedCommand))); Assert.Equal(FormalJson(saved),FormalJson(recovered.Value));
        await h.Db.Sys_RoleDataScopes.Where(x => x.RoleId ==72 && x.ResourceKey =="wms-inbound-receipt").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType,1));
        await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Post,query,new ReturnDispositionQueryV2(app,appliedCommand)),HttpStatusCode.Forbidden);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnDisposition_tenant_and_current_destination_scope_protect_original_results()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var service =Disposition(h,f,"RESALE_STOCK");
        var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0","0.2"))).Value;
        var command =Guid.NewGuid(); await service.CommitAsync(f.Application,p.Preparation.WorkId,command,DispositionAction(p));
        await using var other =await Seed(); var second =await DispositionSetup(other); var secondOwner =Disposition(other,second,"RESALE_STOCK");
        Assert.Equal("RMA_APPLICATION_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => secondOwner.ReadAsync(f.Application,p.Preparation.WorkId))).Message);
        Assert.Equal("DISPOSITION_WORK_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => secondOwner.ReadAsync(second.Application,p.Preparation.WorkId))).Message);
        Assert.Equal("NOT_OBSERVED",(await secondOwner.QueryAsync(new(second.Application,command))).Status);
        await h.Db.Locations.Where(x => x.LocationCd =="RMA-SALE").ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd,"B"));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd,"A"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.QueryAsync(new(f.Application,command)))).Status);
    }
}
