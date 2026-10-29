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
    private static ReturnCorrectionOwnerV1 Correction(Harness h,ReturnDispositionFixture f,ReturnCorrectionSimulationOptionsV1 options)
    {
        var owner =FormalRmaAuthority(h);
        return new(h.Db,owner,QualityReturn(h,f.Quality.Physical,f.Quality.Simulation),PhysicalReturnOwner(h,f.Quality.Physical.Approval,f.Quality.Physical.Case),
            new(Options.Create(options)),new(h.Db,new WmsSequenceService(h.Db),owner:owner),new(h.Db,NullLogger<StockQcService>.Instance,owner));
    }
    private async Task CorrectionGrants(Harness h)
    {
        foreach (var action in new[] { "query","draft","submit","bind-approval","prepare","apply","withdraw" }) await EngineeringAction(h,"wms-return-correction",action);
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="wms-return-correction",ScopeType =5 });
        foreach (var resource in ReturnDispositionOwnersV2.All.Select(x => x.Resource).Append("wms-inbound-receipt")) await EngineeringAction(h,resource,"correct");
        await EngineeringAction(h,"wms-rma","correct-effect"); await EngineeringAction(h,"wms-stock-qc","correct-return-release");
        await EngineeringAction(h,"quality-return-inspection","source-void"); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
    }
    private static ReturnCorrectionSimulationOptionsV1 CorrectionCases(Harness h,params ReturnCorrectionTargetV1[] targets)
        =>new() { Enabled =true,CasesJson =FormalJson(targets.Select(x => new ReturnCorrectionSimulationCaseV1(h.Tenant,x.Source.SiteId,x.Reference,"1","Simulated mistaken record and independent approval",Guid.NewGuid(),DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1),true,[new("Simulation.RecordError",x.Id.ToString(),"1",new string('e',64))])).ToArray()) };
    private static ReturnCorrectionDraftV1 CorrectionInput(ReturnCorrectionChoiceV1 choice,string from,string to)
        =>new(choice.Target.Axis,choice.Target.Id,choice.Target.Reference,[new(from,to)],choice.Policy!.ErrorEvidenceRefs[0],"Correct a simulated recording error; physical effect did not occur");
    private static ReturnCorrectionActionV1 CorrectionAction(ReturnCorrectionViewV1 v) =>new(v.HeadRef,"Execute independent approved correction");
    private static async Task<ReturnCorrectionViewV1> AdvanceCorrection(ReturnCorrectionOwnerV1 owner,Guid app,ReturnCorrectionViewV1 v,params string[] actions)
    {
        foreach (var action in actions) v =(await owner.ActAsync(app,v.Candidate.Id,Guid.NewGuid(),action,CorrectionAction(v))).Value;
        return v;
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_all_five_effects_reverse_exact_groups_then_source_void_preserves_history()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); await CorrectionGrants(h);
        foreach (var (d,i) in ReturnDispositionOwnersV2.All.Select((x,i) => (x,i)))
        {
            var service =Disposition(h,f,d.Kind); var p =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),RmaRangeSetV1.Text(i*0.2m),RmaRangeSetV1.Text((i+1)*0.2m)))).Value;
            await service.CommitAsync(f.Application,p.Preparation.WorkId,Guid.NewGuid(),DispositionAction(p));
        }
        var initial =await Correction(h,f,new()).ContextAsync(f.Application); var options =CorrectionCases(h,initial.Targets.Select(x => x.Target).ToArray()); var owner =Correction(h,f,options);
        var receipts =new List<ReturnCorrectionReceiptV1>();
        foreach (var choice in (await owner.ContextAsync(f.Application)).Targets.Where(x => x.Target.Axis =="DISPOSITION"))
        {
            var range =Assert.Single(choice.AvailableRanges); var v =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,range.From,range.To))).Value;
            var posted =await AdvanceCorrection(owner,f.Application,v,"submit","bind-approval","prepare","apply");
            Assert.Equal("POSTED",posted.State); Assert.Empty(posted.BudgetRemainingRanges); Assert.Equal(v.Candidate.Id,posted.Result!.StockResult.Result!.CorrectionId);
            Assert.Equal(choice.Target.MovementIds.Length,posted.Result.StockResult.Result.RelatedMovementIds.Length); receipts.Add(posted.Result);
            Assert.Equal("REVERSE_BUDGET_EXCEEDED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,range.From,range.To)))).Message);
        }
        Assert.Equal(1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        Assert.All(await h.Db.Stocks.AsNoTracking().Where(x => x.LocationCd =="RMA-SALE" || x.LocationCd =="RMA-REPAIR").ToArrayAsync(),x => Assert.Equal(0m,x.PhysicalQty));
        Assert.Equal(5,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_return_disposition_v2.works WHERE tenant_id={h.Tenant} AND state='APPLIED'").SingleAsync());
        var source =(await owner.ContextAsync(f.Application)).Targets.Single(x => x.Target.Axis =="PHYSICAL_RECEIPT");
        var draft =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(source,"0","0.5"))).Value;
        var reversed =await AdvanceCorrection(owner,f.Application,draft,"submit","bind-approval","prepare","apply");
        Assert.Equal(0.5m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        var qa =await f.Quality.Applicant.ReadAsync(f.Application,f.Quality.Inspection.Inspection.Id); Assert.False(qa.ObservationCurrent);
        Assert.Equal("0.5",Assert.Single(qa.Decision!.Result.VoidRanges).To); Assert.Equal(0.5m,qa.Decision.Result.Partitions.Sum(x => RmaRangeSetV1.Measure(RmaRangeSetV1.Read(x.Ranges))));
        Assert.Empty((await f.Quality.Applicant.ReadAsync(f.Application,f.Quality.Inspection.Inspection.Id,1)).Decision!.Result.VoidRanges);
        Assert.Equal(0.5m,await h.Db.Database.SqlQuery<decimal>($"SELECT sum(range_to-range_from) AS \"Value\" FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={h.Tenant} AND state='VOID_BY_SOURCE_CORRECTION'").SingleAsync());
        Assert.NotNull(reversed.Result!.QualityHeadRef); Assert.Single(receipts,x => x.QualityReleaseCorrectionRef !=null);
        var nextInput =await QualityDecisionInput(f.Quality);
        Assert.Equal("QUALITY_PARTITION_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Quality.Reviewer.DecideAsync(f.Application,f.Quality.Inspection.Inspection.Id,Guid.NewGuid(),nextInput))).Message);
        nextInput =nextInput with { Partitions =nextInput.Partitions.Select(x => x with { Ranges =x.Quality =="PENDING" ? [new("0.5","1")] : [] }).ToArray() };
        var nextDecision =await f.Quality.Reviewer.DecideAsync(f.Application,f.Quality.Inspection.Inspection.Id,Guid.NewGuid(),nextInput);
        var withdrawn =await f.Quality.Reviewer.WithdrawAsync(f.Application,f.Quality.Inspection.Inspection.Id,Guid.NewGuid(),new(nextDecision.Value.HeadRef,"Withdraw latest remaining-scope decision"));
        Assert.Equal(FormalJson(qa.Decision.Result.VoidRanges),FormalJson(withdrawn.Value.Decision!.Result.VoidRanges));
        var wire =Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE"); if (wire !=null) await File.WriteAllTextAsync(wire,FormalJson(new { Dispositions =receipts,Receipt =reversed.Result,Quality =qa.Decision.Result }));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_draft_versions_independent_approval_fences_withdrawal_and_frozen_execution()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h,false); await CorrectionGrants(h);
        var target =Assert.Single((await Correction(h,f,new()).ContextAsync(f.Application)).Targets).Target;
        var options =CorrectionCases(h,target); var owner =Correction(h,f,options); var choice =Assert.Single((await owner.ContextAsync(f.Application)).Targets);
        var v =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,"0","0.2"))).Value;
        var edit =new ReturnCorrectionEditV1(v.HeadRef,[new("0","0.2")],v.Candidate.ErrorEvidenceRef,v.Candidate.Reason);
        Assert.Equal(1,(await owner.EditAsync(f.Application,v.Candidate.Id,Guid.NewGuid(),edit)).Value.Candidate.Version);
        v =(await owner.EditAsync(f.Application,v.Candidate.Id,Guid.NewGuid(),edit with { Ranges =[new("0","0.1")] })).Value; Assert.Equal(2,v.Candidate.Version);
        v =await AdvanceCorrection(owner,f.Application,v,"submit");
        Assert.DoesNotContain((await Disposition(h,f,"RESALE_STOCK").ContextAsync(f.Application)).Choices,x => RmaRangeSetV1.Subset([new(0,0.1m)],RmaRangeSetV1.Read(x.Proposal.EligibleRanges)));
        var cases =JsonSerializer.Deserialize<ReturnCorrectionSimulationCaseV1[]>(options.CasesJson,RmaJson)!;
        var self =Correction(h,f,new() { Enabled =true,CasesJson =FormalJson(cases.Select(x => x with { IndependentApproverId =h.User })) });
        Assert.Equal("CORRECTION_INDEPENDENT_APPROVER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => self.ActAsync(f.Application,v.Candidate.Id,Guid.NewGuid(),"bind-approval",CorrectionAction(v)))).Message);
        v =await AdvanceCorrection(owner,f.Application,v,"bind-approval","prepare"); Assert.Equal("0.1",Assert.Single(v.Preparation!.Spans).To);
        await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.AllocatedQty,0.1m).SetProperty(x => x.AvailableQty,0.9m));
        Assert.Equal("STOCK_POOL_VERSION_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ActAsync(f.Application,v.Candidate.Id,Guid.NewGuid(),"apply",CorrectionAction(v)))).Message);
        var withdrawn =await AdvanceCorrection(owner,f.Application,v,"withdraw"); Assert.NotNull(withdrawn.NoEffectProof);
        var next =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,"0","0.1"))).Value;
        var posted =await AdvanceCorrection(owner,f.Application,next,"submit","bind-approval","prepare","apply");
        Assert.Equal("POSTED",posted.State); Assert.Equal(0.1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.AllocatedQty).SingleAsync());
    }
    private async Task<(ReturnCorrectionOwnerV1 Owner,ReturnCorrectionSimulationOptionsV1 Options,ReturnCorrectionViewV1 View)> PreparedCorrection(Harness h,ReturnDispositionFixture f,string from ="0",string to ="0.1")
    {
        await CorrectionGrants(h); var target =(await Correction(h,f,new()).ContextAsync(f.Application)).Targets.Single(x => x.Target.Axis =="PHYSICAL_RECEIPT").Target;
        var options =CorrectionCases(h,target); var owner =Correction(h,f,options); var choice =Assert.Single((await owner.ContextAsync(f.Application)).Targets);
        var draft =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,from,to))).Value;
        return (owner,options,await AdvanceCorrection(owner,f.Application,draft,"submit","bind-approval","prepare"));
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_stock_void_claim_successor_and_result_rollback_together_then_retry()
    {
        var fault =new FailReturnQualityJournal(); await using var h =await Seed(fault); var f =await DispositionSetup(h); var p =await PreparedCorrection(h,f);
        var before =await f.Quality.Applicant.ReadAsync(f.Application,f.Quality.Inspection.Inspection.Id); var count =await h.Db.StockTransactions.CountAsync(); var command =Guid.NewGuid();
        fault.Armed =true;
        Assert.Equal("INJECTED_RETURN_QUALITY_JOURNAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(() => p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View)))).Message);
        Assert.True(fault.Reached); fault.Armed =false;
        Assert.Equal(count,await h.Db.StockTransactions.CountAsync()); Assert.Equal(before.HeadRef,(await f.Quality.Applicant.ReadAsync(f.Application,f.Quality.Inspection.Inspection.Id)).HeadRef);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_return_correction_v1.successors WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={h.Tenant} AND state='VOID_BY_SOURCE_CORRECTION'").SingleAsync());
        Assert.Equal("NOT_OBSERVED",(await p.Owner.QueryAsync(new(f.Application,command))).Status);
        var saved =await p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View));
        Assert.Equal(FormalJson(saved),FormalJson(await p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View)))); Assert.Equal(count+1,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_lost_ack_recovers_after_write_revocation_but_requires_current_scope()
    {
        var fault =new CommitFault(); await using var h =await Seed(fault); var f =await DispositionSetup(h); var p =await PreparedCorrection(h,f); var command =Guid.NewGuid();
        fault.Armed =true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View)))).Message);
        await RevokePhysicalGrant(h,"wms-return-correction","apply");
        var saved =(await p.Owner.QueryAsync(new(f.Application,command))).Value!; Assert.Equal("POSTED",saved.Value.State);
        Assert.Equal(FormalJson(saved),FormalJson(await p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View))));
        Assert.Equal("COMMAND_BINDING_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => p.Owner.ActAsync(f.Application,p.View.Candidate.Id,command,"apply",CorrectionAction(p.View) with { Reason ="changed" }))).Message);
        await h.Db.Locations.Where(x => x.LocationCd =="RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd,"B"));
        await h.Db.WmsRoleScopes.ExecuteUpdateAsync(s => s.SetProperty(x => x.AreaCd,"A"));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => p.Owner.QueryAsync(new(f.Application,command)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_source_dependencies_and_concurrent_proposals_preserve_single_range_budget()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); await CorrectionGrants(h); var service =Disposition(h,f,"REPAIR");
        var work =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0.2","0.4"))).Value;
        var target =Assert.Single((await Correction(h,f,new()).ContextAsync(f.Application)).Targets).Target; var options =CorrectionCases(h,target); var owner =Correction(h,f,options);
        var choice =Assert.Single((await owner.ContextAsync(f.Application)).Targets); var input =CorrectionInput(choice,"0.2","0.4");
        var draft =(await owner.DraftAsync(f.Application,Guid.NewGuid(),input)).Value;
        Assert.Equal("CORRECTION_SUCCESSOR_DEPENDENCY",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ActAsync(f.Application,draft.Candidate.Id,Guid.NewGuid(),"submit",CorrectionAction(draft)))).Message);
        await service.SealAsync(f.Application,work.Preparation.WorkId,Guid.NewGuid(),DispositionAction(work));
        var other =(await owner.DraftAsync(f.Application,Guid.NewGuid(),input)).Value;
        await using var db2 =server.Context(new TenantContext { CurrentTenantId =h.Tenant }); var second =Correction(h with { Db =db2 },f,options);
        async Task<(ReturnCorrectionViewV1? Value,string? Error)> Attempt(ReturnCorrectionOwnerV1 o,ReturnCorrectionViewV1 v)
        { try { return ((await o.ActAsync(f.Application,v.Candidate.Id,Guid.NewGuid(),"submit",CorrectionAction(v))).Value,null); } catch (OrderPriceCorrectionException e) { return (null,e.Message); } }
        var results =await Task.WhenAll(Attempt(owner,draft),Attempt(second,other)); var winner =Assert.Single(results,x => x.Value !=null).Value!;
        Assert.Equal("CORRECTION_SCOPE_OCCUPIED",Assert.Single(results,x => x.Error !=null).Error);
        var posted =await AdvanceCorrection(owner,f.Application,winner,"bind-approval","prepare","apply"); Assert.Equal("POSTED",posted.State);
        var loser =winner.Candidate.Id ==draft.Candidate.Id ? other : draft;
        Assert.Equal("REVERSE_BUDGET_EXCEEDED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ActAsync(f.Application,loser.Candidate.Id,Guid.NewGuid(),"submit",CorrectionAction(loser)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_expired_changed_approval_current_effect_authority_and_tenant_are_checked()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var p =await PreparedCorrection(h,f);
        var cases =JsonSerializer.Deserialize<ReturnCorrectionSimulationCaseV1[]>(p.Options.CasesJson,RmaJson)!;
        var changed =Correction(h,f,new() { Enabled =true,CasesJson =FormalJson(cases.Select(x => x with { Version ="2" })) });
        Assert.Equal("CORRECTION_APPROVAL_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => changed.ActAsync(f.Application,p.View.Candidate.Id,Guid.NewGuid(),"apply",CorrectionAction(p.View)))).Message);
        var expired =Correction(h,f,new() { Enabled =true,CasesJson =FormalJson(cases.Select(x => x with { ValidTo =DateTimeOffset.UtcNow.AddMinutes(-1) })) });
        Assert.Equal("CORRECTION_APPROVAL_NOT_EFFECTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => expired.ActAsync(f.Application,p.View.Candidate.Id,Guid.NewGuid(),"apply",CorrectionAction(p.View)))).Message);
        await RevokePhysicalGrant(h,"wms-inbound-receipt","correct");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => p.Owner.ActAsync(f.Application,p.View.Candidate.Id,Guid.NewGuid(),"apply",CorrectionAction(p.View)))).Status);
        Assert.Equal(1m,await h.Db.Stocks.Where(x => x.LocationCd =="RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        await using var other =await Seed(); var second =await DispositionSetup(other); await CorrectionGrants(other); var foreign =Correction(other,second,p.Options);
        Assert.Equal("RMA_APPLICATION_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreign.ReadAsync(f.Application,p.View.Candidate.Id))).Message);
        Assert.Equal("CORRECTION_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => foreign.ReadAsync(second.Application,p.View.Candidate.Id))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_destination_root_consumption_blocks_inverse_even_with_other_stock_available()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); await CorrectionGrants(h); var service =Disposition(h,f,"RESALE_STOCK");
        var work =(await service.PrepareAsync(f.Application,Guid.NewGuid(),DispositionInput(await service.ContextAsync(f.Application),"0","0.2"))).Value;
        await service.CommitAsync(f.Application,work.Preparation.WorkId,Guid.NewGuid(),DispositionAction(work));
        var target =(await Correction(h,f,new()).ContextAsync(f.Application)).Targets.Single(x => x.Target.Axis =="DISPOSITION").Target;
        var owner =Correction(h,f,CorrectionCases(h,target)); var choice =(await owner.ContextAsync(f.Application)).Targets.Single(x => x.Target.Axis =="DISPOSITION");
        var draft =(await owner.DraftAsync(f.Application,Guid.NewGuid(),CorrectionInput(choice,"0","0.1"))).Value;
        var approved =await AdvanceCorrection(owner,f.Application,draft,"submit","bind-approval");
        // A separate movement has no root attribution; remaining aggregate stock cannot establish these roots.
        var movement =Guid.NewGuid(); var pool =target.DestinationPool!.PoolId;
        h.Db.StockTransactions.Add(new() { Id =movement,TxnNo ="SIM-"+movement.ToString("N")[..16],TxnType =WmsTxnType.ADJ,Qty =-0.01m,WarehouseCd ="W01",LocationCd ="RMA-SALE",ProductCd =target.DestinationPool.Product,LotNo =target.DestinationPool.Lot });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_stock_return_v1.movement_sequences(tenant_id,pool_id,movement_id) VALUES({h.Tenant},{pool},{movement})");
        Assert.Equal("RETURN_STOCK_LINEAGE_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ActAsync(f.Application,approved.Candidate.Id,Guid.NewGuid(),"prepare",CorrectionAction(approved)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_before_inspection_initializes_quality_with_remaining_pending_and_void()
    {
        await using var h =await Seed(); var physical =await PhysicalReturnSetup(h,false); var app =physical.Approval.Application.ApplicationId;
        foreach (var action in new[] { "query","start","decide","withdraw" }) await EngineeringAction(h,"quality-return-inspection",action);
        await EngineeringAction(h,"wms-rma","inspection-start");
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="quality-return-inspection",ScopeType =5 });
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId =72,ResourceKey ="wms-inbound-receipt",ScopeType =5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var received =await PreparePhysical(physical,"0","1"); await physical.Owner.CommitPhysicalReturnAsync(app,received.Work.WorkId,Guid.NewGuid(),PhysicalAction(received));
        var simulation =new ReturnQualitySimulationOptionsV1 { Enabled =true,CasesJson =FormalJson(new[] { new ReturnQualitySimulationCaseV1(h.Tenant,physical.Case.Dimensions.SiteId,physical.Case.QualityBasisRef,"1",true,[new("Simulation.Quality",Guid.NewGuid().ToString(),"1",new string('f',64))]) }) };
        var quality =QualityReturn(h,physical,simulation);
        var f =new ReturnDispositionFixture(new(physical,quality,QualityReturn(physical.Approval.Reviewer,physical,simulation),simulation,null!),new(),app);
        var p =await PreparedCorrection(h,f,"0","0.5"); var posted =await AdvanceCorrection(p.Owner,app,p.View,"apply"); Assert.Null(posted.Result!.QualityHeadRef);
        var choice =Assert.Single((await quality.ContextAsync(app)).Receipts);
        var inspection =(await quality.StartAsync(app,Guid.NewGuid(),new(choice.Source.LineId,choice.Source.Reference,choice.Source.PhysicalHeadRef,choice.Policy!.Reference,"Inspect remaining actual quantity"))).Value;
        Assert.Equal("0.5",Assert.Single(inspection.Decision!.Result.VoidRanges).To);
        Assert.Equal("0.5",inspection.Decision.Result.Partitions.Single(x => x.Quality =="PENDING").Quantity.Value);
        Assert.All(inspection.Decision.Result.Partitions.Where(x => x.Quality !="PENDING"),x => Assert.Empty(x.Ranges)); Assert.False(inspection.ObservationCurrent);
    }
    [CurrentPermissionPostgresFact]
    public async Task ReturnCorrection_actual_http_lifecycle_csrf_and_original_query()
    {
        await using var h =await Seed(); var f =await DispositionSetup(h); var p =await PreparedCorrection(h,f); var coordinates =Assert.Single(FormalCoordinates(h).WarehouseSites);
        // HTTP executes as the same candidate actor; configured approval remains independent.
        await using var host =await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] =ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"] ="DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] =coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"] =coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] =coordinates.SiteId.ToString(),["ReturnQualitySimulation__Enabled"] ="true",["ReturnQualitySimulation__CasesJson"] =f.Quality.Simulation.CasesJson,
            ["ReturnCorrectionSimulation__Enabled"] ="true",["ReturnCorrectionSimulation__CasesJson"] =p.Options.CasesJson });
        var cookies =await DocumentReviewerLogin(host,h,h.User);
        async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode status =HttpStatusCode.OK)
        {
            using var response =await host.Send(request); var text =await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode ==status,response.StatusCode+": "+text); Assert.True(response.Headers.CacheControl?.NoStore); return JsonSerializer.Deserialize<T>(text,RmaJson)!;
        }
        var path =$"api/wms/rma/v1/corrections/applications/{f.Application}/proposals/{p.View.Candidate.Id}/apply"; var command =Guid.NewGuid(); var input =CorrectionAction(p.View);
        var denied =FormalRequest(cookies,HttpMethod.Post,path,input,command); denied.Headers.Remove("X-CSRF-TOKEN");
        using (var response =await host.Send(denied)) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var saved =await Receive<ReturnCorrectionCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,path,input,command)); Assert.Equal("POSTED",saved.Value.State);
        await RevokePhysicalGrant(h,"wms-return-correction","apply");
        var recovered =await Receive<ReturnCorrectionQueryResultV1>(FormalRequest(cookies,HttpMethod.Post,"api/wms/rma/v1/corrections/commands/query",new ReturnCorrectionQueryV1(f.Application,command)));
        Assert.Equal(FormalJson(saved),FormalJson(recovered.Value));
    }
}
