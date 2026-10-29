using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ReturnQualityFixture(PhysicalReturnFixture Physical,ReturnQualityOwnerV1 Applicant,
        ReturnQualityOwnerV1 Reviewer,ReturnQualitySimulationOptionsV1 Simulation,ReturnQualityViewV1 Inspection);
    private static ReturnQualityOwnerV1 QualityReturn(Harness h,PhysicalReturnFixture f,ReturnQualitySimulationOptionsV1 options)
        => new(h.Db,FormalRmaAuthority(h),PhysicalReturnOwner(h,f.Approval,f.Case),new(Options.Create(options)));
    private async Task<ReturnQualityFixture> QualityReturnSetup(Harness h)
    {
        var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        foreach (var action in new[] { "query","start","decide","withdraw" }) await EngineeringAction(h,"quality-return-inspection",action);
        foreach (var action in new[] { "inspection-start","quality-observe" }) await EngineeringAction(h,"wms-rma",action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="quality-return-inspection",ScopeType =5 });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="wms-inbound-receipt",ScopeType =5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var prepared = await PreparePhysical(f,"0","1");
        await f.Owner.CommitPhysicalReturnAsync(app,prepared.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared));
        var evidence = new FulfilmentRefV1("Simulation.Quality.Inspection",Guid.NewGuid().ToString(),"1",new string('c',64));
        var simulation = new ReturnQualitySimulationOptionsV1 { Enabled = true,CasesJson = FormalJson(new[] {
            new ReturnQualitySimulationCaseV1(h.Tenant,f.Case.Dimensions.SiteId,f.Case.QualityBasisRef,"1",true,[evidence]) }) };
        var applicant = QualityReturn(h,f,simulation); var reviewer = QualityReturn(f.Approval.Reviewer,f,simulation);
        var choice = Assert.Single((await applicant.ContextAsync(app)).Receipts);
        var created = await applicant.StartAsync(app,Guid.NewGuid(),new(choice.Source.LineId,choice.Source.Reference,choice.Source.PhysicalHeadRef,choice.Policy!.Reference,"Request inspection of returned goods"));
        return new(f,applicant,reviewer,simulation,created.Value);
    }
    private static ReturnQualityDecideV1 QualityDecisionInput(ReturnQualityChoiceV1 choice)
        => new(choice.Inspection!.HeadRef,choice.Policy!.Reference,new[] { "ACCEPTED","REJECTED","HOLD","PENDING" }.Select((state,i) =>
            new ReturnQualityPartitionInputV1(state,[new(RmaRangeSetV1.Text(i*0.25m),RmaRangeSetV1.Text((i+1)*0.25m))],choice.Policy.EvidenceRefs,["SIMULATED_INSPECTION"])).ToArray(),"Complete inspection with explicit simulated measurements");
    private static async Task<ReturnQualityDecideV1> QualityDecisionInput(ReturnQualityFixture f)
        => QualityDecisionInput(Assert.Single((await f.Reviewer.ContextAsync(f.Physical.Approval.Application.ApplicationId)).Receipts));
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_independent_decision_exact_partitions_observation_correction_and_withdrawal_preserve_stock()
    {
        await using var h = await Seed(); var f = await QualityReturnSetup(h); var app = f.Physical.Approval.Application.ApplicationId; var id = f.Inspection.Inspection.Id;
        var count = await h.Db.StockTransactions.CountAsync(); var command = Guid.NewGuid(); var input = await QualityDecisionInput(f);
        var saved = await f.Reviewer.DecideAsync(app,id,command,input); var decision = saved.Value.Decision!.Result;
        Assert.Equal(new[] { "ACCEPTED","REJECTED","HOLD","PENDING" },decision.Partitions.Select(x => x.Quality));
        Assert.All(decision.Partitions,x => Assert.Equal("0.25",x.Quantity.Value)); Assert.Null(decision.PreviousDecisionRef);
        var source = Assert.Single((await f.Applicant.ContextAsync(app)).Receipts).Source;
        var observed = await f.Applicant.ObserveAsync(app,id,Guid.NewGuid(),new(decision.DecisionRef,source.PhysicalHeadRef)); Assert.True(observed.Value.ObservationCurrent);
        var correction = await QualityDecisionInput(f); correction = correction with { Partitions = correction.Partitions.Select(x => x with { Quality = x.Quality =="ACCEPTED" ? "REJECTED" : x.Quality =="REJECTED" ? "ACCEPTED" : x.Quality }).ToArray() };
        var changed = await f.Reviewer.DecideAsync(app,id,Guid.NewGuid(),correction); Assert.Equal(decision.DecisionRef,changed.Value.Decision!.Result.PreviousDecisionRef); Assert.False(changed.Value.ObservationCurrent);
        Assert.Equal(FormalJson(saved),FormalJson(await f.Reviewer.DecideAsync(app,id,command,input)));
        Assert.Equal(FormalJson(decision),FormalJson((await f.Reviewer.ReadAsync(app,id,1)).Decision!.Result));
        Assert.Equal("CAS_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Applicant.ObserveAsync(app,id,Guid.NewGuid(),new(decision.DecisionRef,source.PhysicalHeadRef)))).Message);
        var closed = await f.Reviewer.WithdrawAsync(app,id,Guid.NewGuid(),new(changed.Value.HeadRef,"Withdraw incorrect professional decision"));
        Assert.Equal("WITHDRAWN",closed.Value.Decision!.Result.DecisionState); Assert.False(closed.Value.ObservationCurrent);
        Assert.Equal(count,await h.Db.StockTransactions.CountAsync()); Assert.Equal(1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        var wire = Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE"); if (wire !=null) await File.WriteAllTextAsync(wire,FormalJson(new { f.Inspection.Inspection.Request,Decision = decision,Withdrawn = closed.Value.Decision.Result }));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_rejects_nonindependent_actor_gaps_overlaps_wrong_evidence_and_current_revocation()
    {
        await using var h = await Seed(); var f = await QualityReturnSetup(h); var app = f.Physical.Approval.Application.ApplicationId; var id = f.Inspection.Inspection.Id;
        var input = await QualityDecisionInput(f);
        Assert.Equal("QUALITY_INDEPENDENT_DECIDER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Applicant.DecideAsync(app,id,Guid.NewGuid(),input))).Message);
        foreach (var to in new[] { "0.2","0.3" })
        {
            var invalid = input with { Partitions = input.Partitions.Select((x,i) => i ==0 ? x with { Ranges = [new("0",to)] } : x).ToArray() };
            Assert.Equal("QUALITY_PARTITION_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.DecideAsync(app,id,Guid.NewGuid(),invalid))).Message);
        }
        var forged = input with { Partitions = input.Partitions.Select((x,i) => i ==0 ? x with { BasisRefs = [x.BasisRefs[0] with { Id = "forged" }] } : x).ToArray() };
        Assert.Equal("QUALITY_EVIDENCE_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.DecideAsync(app,id,Guid.NewGuid(),forged))).Message);
        await RevokePhysicalGrant(h,"quality-return-inspection","decide");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.DecideAsync(app,id,Guid.NewGuid(),input))).Status);
        Assert.Null((await f.Applicant.ReadAsync(app,id)).Decision);
    }
    private sealed class FailReturnQualityJournal : DbCommandInterceptor
    {
        public bool Armed,Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations",StringComparison.Ordinal)) { Reached =true; throw new InvalidOperationException("INJECTED_RETURN_QUALITY_JOURNAL_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_decision_outbox_and_head_rollback_with_original_journal_then_retry_once()
    {
        var fault =new FailReturnQualityJournal(); await using var h =await Seed(fault); var f =await QualityReturnSetup(h); var app =f.Physical.Approval.Application.ApplicationId; var id =f.Inspection.Inspection.Id;
        var input =await QualityDecisionInput(f); var command =Guid.NewGuid(); fault.Armed =true;
        Assert.Equal("INJECTED_RETURN_QUALITY_JOURNAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reviewer.DecideAsync(app,id,command,input))).Message); Assert.True(fault.Reached);
        fault.Armed =false; Assert.Null((await f.Reviewer.ReadAsync(app,id)).Decision);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_return_quality_v1.outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("NOT_OBSERVED",(await f.Reviewer.QueryAsync(new(app,command))).Status);
        var saved =await f.Reviewer.DecideAsync(app,id,command,input); Assert.Equal(FormalJson(saved),FormalJson(await f.Reviewer.DecideAsync(app,id,command,input)));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_lost_ack_recovers_original_after_write_revocation_and_preserves_binding()
    {
        var fault =new CommitFault(); await using var h =await Seed(fault); var f =await QualityReturnSetup(h); var app =f.Physical.Approval.Application.ApplicationId; var id =f.Inspection.Inspection.Id;
        var input =await QualityDecisionInput(f); var command =Guid.NewGuid(); fault.Armed =true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.DecideAsync(app,id,command,input))).Message);
        await RevokePhysicalGrant(h,"quality-return-inspection","decide");
        var result =(await f.Reviewer.QueryAsync(new(app,command))).Value!;
        Assert.Equal(FormalJson(result),FormalJson(await f.Reviewer.DecideAsync(app,id,command,input)));
        Assert.Equal("COMMAND_BINDING_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.DecideAsync(app,id,command,input with { Reason ="Changed" }))).Message);
        await RevokePhysicalGrant(h,"quality-return-inspection","query");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.QueryAsync(new(app,command)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_two_connections_serialize_decisions_and_current_quality_scope_is_independent()
    {
        await using var h =await Seed(); var f =await QualityReturnSetup(h); var app =f.Physical.Approval.Application.ApplicationId; var id =f.Inspection.Inspection.Id;
        await using var db2 =server.Context(new TenantContext { CurrentTenantId =h.Tenant });
        var second =QualityReturn(f.Physical.Approval.Reviewer with { Db =db2 },f.Physical,f.Simulation); var input =await QualityDecisionInput(f);
        async Task<(ReturnQualityCommandResultV1? Result,string? Error)> Attempt(ReturnQualityOwnerV1 service)
        { try { return (await service.DecideAsync(app,id,Guid.NewGuid(),input),null); } catch (OrderPriceCorrectionException e) { return (null,e.Message); } }
        var outcomes =await Task.WhenAll(Attempt(f.Reviewer),Attempt(second)); Assert.Single(outcomes,x => x.Result !=null); Assert.Equal("CAS_CONFLICT",Assert.Single(outcomes,x => x.Error !=null).Error);
        await h.Db.Sys_RoleDataScopes.Where(x => x.RoleId ==72 && x.ResourceKey =="quality-return-inspection").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType,1));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Reviewer.ReadAsync(app,id))).Status);
        Assert.NotNull((await f.Applicant.ReadAsync(app,id)).Decision);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_tenant_inspections_and_original_results_remain_isolated()
    {
        await using var first =await Seed(); var f =await QualityReturnSetup(first);
        await using var second =await Seed(); var other =await QualityReturnSetup(second);
        var app =f.Physical.Approval.Application.ApplicationId; var ownApp =other.Physical.Approval.Application.ApplicationId;
        var command =Guid.NewGuid(); var id =f.Inspection.Inspection.Id;
        await f.Reviewer.DecideAsync(app,id,command,await QualityDecisionInput(f));
        Assert.Equal("RMA_APPLICATION_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => other.Reviewer.ReadAsync(app,id))).Message);
        Assert.Equal("QUALITY_INSPECTION_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => other.Reviewer.ReadAsync(ownApp,id))).Message);
        Assert.Equal("NOT_OBSERVED",(await other.Reviewer.QueryAsync(new(ownApp,command))).Status);
        Assert.Equal(other.Inspection.Inspection.Id,(await other.Reviewer.ReadAsync(ownApp,other.Inspection.Inspection.Id)).Inspection.Id);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnQuality_actual_HTTP_decides_and_observes_with_csrf_and_original_recovery()
    {
        await using var h =await Seed(); var f =await QualityReturnSetup(h); var app =f.Physical.Approval.Application.ApplicationId; var id =f.Inspection.Inspection.Id;
        var coordinates =Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host =await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] =ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"] ="DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] =coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"] =coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] =coordinates.SiteId.ToString(),["ReturnQualitySimulation__Enabled"] ="true",["ReturnQualitySimulation__CasesJson"] =f.Simulation.CasesJson });
        var cookies =await DocumentReviewerLogin(host,h,f.Physical.Approval.Reviewer.User);
        async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode status =HttpStatusCode.OK)
        {
            using var response =await host.Send(request); var text =await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode ==status,response.StatusCode+": "+text); Assert.True(response.Headers.CacheControl?.NoStore);
            return JsonSerializer.Deserialize<T>(text,RmaJson)!;
        }
        var path =$"api/wms/rma/v1/applications/{app}/quality";
        var context =await Receive<ReturnQualityContextV1>(FormalRequest(cookies,HttpMethod.Get,path+"/context")); var input =QualityDecisionInput(Assert.Single(context.Receipts)); var command =Guid.NewGuid();
        var noCsrf =FormalRequest(cookies,HttpMethod.Post,path+$"/inspections/{id}/decisions",input,command); noCsrf.Headers.Remove("X-CSRF-TOKEN");
        using (var denied =await host.Send(noCsrf)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var saved =await Receive<ReturnQualityCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,path+$"/inspections/{id}/decisions",input,command));
        var source =context.Receipts[0].Source;
        var observed =await Receive<ReturnQualityCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,path+$"/inspections/{id}/observations",new RmaObserveQualityV1(saved.Value.Decision!.Result.DecisionRef,source.PhysicalHeadRef),Guid.NewGuid()));
        Assert.True(observed.Value.ObservationCurrent);
        await RevokePhysicalGrant(h,"quality-return-inspection","decide");
        var recovered =await Receive<ReturnQualityQueryResultV1>(FormalRequest(cookies,HttpMethod.Post,"api/wms/rma/v1/quality/commands/query",new ReturnQualityQueryV1(app,command)));
        Assert.Equal(FormalJson(saved),FormalJson(recovered.Value));
        await h.Db.Sys_RoleDataScopes.Where(x => x.RoleId ==72 && x.ResourceKey =="wms-inbound-receipt").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType,1));
        await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Get,path+"/context"),HttpStatusCode.Forbidden);
        await h.Db.Sys_RoleDataScopes.Where(x => x.RoleId ==72 && x.ResourceKey =="wms-inbound-receipt").ExecuteUpdateAsync(s => s.SetProperty(x => x.ScopeType,5));
        await RevokePhysicalGrant(h,"quality-return-inspection","query");
        await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Get,path+"/context"),HttpStatusCode.Forbidden);
    }
}
