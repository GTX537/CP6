using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record ReturnApprovalFixture(RmaFormalOwnerV1 Applicant, RmaFormalOwnerV1 Approver, Harness Reviewer,
        IDataProtectionProvider Keys, RmaFormalApplicationV1 Application, RmaReturnSimulationCaseV1 Source, RmaReturnSimulationOptionsV1 Simulation);
    private async Task<ReturnApprovalFixture> ReturnApprovalSetup(Harness h)
    {
        var shipment = await FormalShipment(h);
        foreach (var action in new[] { "policy-publish", "authorization-decide", "authorization-revoke" }) await EngineeringAction(h,"wms-rma",action);
        var keys = new EphemeralDataProtectionProvider(); var applicant = FormalRma(h,keys); var context = await applicant.ContextAsync();
        await applicant.PublishPolicyAsync(Guid.NewGuid(),PolicyInput(context));
        var source = (await applicant.SourcesAsync(FormalSearch(context))).Items[0]; var customer = (await RmaApplications(h).FulfilmentAsync(shipment.Id)).CustomerRef!;
        var app = await applicant.CreateAsync(Guid.NewGuid(),FormalCreate(context,source,customer));
        app = await applicant.SubmitAsync(app.ApplicationId,Guid.NewGuid(),FormalTransition(app),FormalEtag(app));
        var reviewer = Guid.NewGuid(); h.Db.Sys_Users.Add(new() { Id = reviewer, UserName = "simulated-return-reviewer", Password = new CP6.Core.Services.Sys.BCryptPasswordHasher(4).Hash("Price1!Password"), RoleId = 72 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear(); var actor = DecisionActor(h,reviewer);
        var proof = new FulfilmentRefV1("Simulation.Wms.DeliveryProof.v1",Guid.NewGuid().ToString(),"1",new string('a',64));
        var invoice = new FulfilmentRefV1("Simulation.Erp.InvoiceLine.v1",Guid.NewGuid().ToString(),"1",new string('b',64));
        var item = new RmaReturnSimulationCaseV1(h.Tenant,source.Original.ShipmentRef,source.Original.ShipmentLineId,"1",DateTimeOffset.UtcNow.AddDays(-2),proof,invoice,null,
            source.Original.SourceShareRanges.Select(x => new RmaDeliveryRangeV1(x.From,x.To)).ToArray(),[],[],[]);
        var simulation = new RmaReturnSimulationOptionsV1 { Enabled = true, CasesJson = FormalJson(new[] { item }) };
        var sources = new RmaSimulatedReturnSourcesV1(Options.Create(simulation));
        return new(FormalRma(h,keys,sources:sources),FormalRma(actor,keys,sources:sources),actor,keys,app,item,simulation);
    }
    private static RmaAuthorizationDecideV1 ApprovalInput(RmaAuthorizationContextV1 context) => new(context.ApplicationRef,
        context.Lines.Select(x => x.Entitlement.CurrentHeadRef).Distinct().ToArray(),"APPROVED",
        context.Lines.Select(x => new RmaAuthorizedShareInputV1(x.RequestedLineId,x.Entitlement.Reference,
            x.AvailableRanges.Select(r => new RmaDeliveryRangeV1(r.From,r.To)).ToArray(),
            new(RmaRangeSetV1.Text(RmaRangeSetV1.Measure(RmaRangeSetV1.Read(x.AvailableRanges))),x.Coordinate.BaseUom))).ToArray(),
        context.AuthorityRef,["ACCEPTED_RETURN"],DateTimeOffset.UtcNow,null,null);
    private static Task<int> ApprovalCount(Harness h) => h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.authorization_versions WHERE tenant_id={h.Tenant}").SingleAsync();
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_simulated_sources_drive_independent_approval_exact_claims_revoke_and_original_recovery()
    {
        await using var h = await Seed(); var f = await ReturnApprovalSetup(h); var app = f.Application.ApplicationId;
        var stock = await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync(); var stockTransactions = await h.Db.StockTransactions.CountAsync();
        var self = ApprovalInput(await f.Applicant.AuthorizationContextAsync(app));
        Assert.Equal("INDEPENDENT_APPROVER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Applicant.DecideAuthorizationAsync(app,Guid.NewGuid(),self))).Message);
        var context = await f.Approver.AuthorizationContextAsync(app); Assert.Equal("SIMULATED",Assert.Single(context.Lines).EvidenceMode);
        var command = Guid.NewGuid(); var input = ApprovalInput(context); var saved = await f.Approver.DecideAuthorizationAsync(app,command,input);
        Assert.Equal("APPROVED",saved.Outcome); Assert.Equal("1.12345678",Assert.Single(saved.AuthorizedShares).Quantity.Value);
        Assert.Equal(f.Source.DeliveryProofRef,saved.AuthorizedShares[0].Original.DeliveryProofRef); Assert.NotNull(saved.AuthorizedShares[0].RangeClaimRef);
        Assert.Equal("SUBMITTED",(await f.Applicant.ReadAsync(app)).State);
        Assert.Equal(FormalJson(saved),FormalJson(await f.Approver.DecideAuthorizationAsync(app,command,input)));
        Assert.Equal("AUTHORIZATION_ACTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Applicant.WithdrawAsync(app,Guid.NewGuid(),FormalTransition(f.Application),FormalEtag(f.Application)))).Message);
        var after = await f.Approver.AuthorizationContextAsync(app); Assert.Equal("1.12345678",after.Lines[0].Entitlement.ActiveClaimQty.Value); Assert.Empty(after.Lines[0].AvailableRanges);
        var revokeCommand = Guid.NewGuid(); var revoke = new RmaAuthorizationRevokeV1(saved.DecisionRef,after.CurrentShareRefs,after.AuthorityRef,"Customer withdrew request");
        var revoked = await f.Approver.RevokeAuthorizationAsync(app,revokeCommand,revoke); Assert.Equal("REVOKED",revoked.Outcome); Assert.Empty(revoked.AuthorizedShares); Assert.Equal(saved.DecisionRef,revoked.PreviousDecisionRef);
        Assert.Equal(FormalJson(saved),FormalJson(await f.Approver.ReadAuthorizationAsync(app,1)));
        Assert.Equal(FormalJson(saved),FormalJson((await f.Approver.QueryAuthorizationCommandAsync(new(app,command))).Value));
        Assert.Equal(FormalJson(revoked),FormalJson(await f.Approver.RevokeAuthorizationAsync(app,revokeCommand,revoke)));
        Assert.Equal("0",(await f.Approver.AuthorizationContextAsync(app)).Lines[0].Entitlement.ActiveClaimQty.Value);
        Assert.Equal(stock,await h.Db.Stocks.Select(x => x.PhysicalQty).SingleAsync()); Assert.Equal(stockTransactions,await h.Db.StockTransactions.CountAsync());
        Assert.Equal(2,await ApprovalCount(h)); Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.authorization_outbox WHERE tenant_id={h.Tenant}").SingleAsync());
        await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.authorization_versions WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_explicit_history_is_counted_once_and_source_versions_cannot_reset_claims()
    {
        await using var h = await Seed(); var f = await ReturnApprovalSetup(h); var app = f.Application.ApplicationId;
        var scenario = f.Source with { HistoricalOccupiedRanges = [new("0","0.5")], HistoricalReceivedRanges = [new("0","0.25")], CreditedRanges = [new("0","0.1")] };
        RmaFormalOwnerV1 With(RmaReturnSimulationCaseV1 c) => FormalRma(f.Reviewer,f.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(new RmaReturnSimulationOptionsV1 { Enabled = true, CasesJson = FormalJson(new[] { c }) })));
        var owner = With(scenario); var context = await owner.AuthorizationContextAsync(app); var line = context.Lines[0];
        Assert.Equal("0.5",line.Entitlement.AuthorizedQty.Value); Assert.Equal("0.25",line.Entitlement.ReceivedQty.Value); Assert.Equal("0.1",line.Entitlement.CreditedQty.Value);
        Assert.Equal("0.5",line.AvailableRanges[0].From); var saved = await owner.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(context)); Assert.Equal("0.62345678",saved.AuthorizedShares[0].Quantity.Value);
        var changed = With(scenario with { CutVersion = "2", InvoiceLineRef = scenario.InvoiceLineRef! with { Version = "2", Digest = new string('c',64) } });
        var latest = await changed.AuthorizationContextAsync(app); Assert.Equal("1.12345678",latest.Lines[0].Entitlement.ActiveClaimQty.Value); Assert.Empty(latest.Lines[0].AvailableRanges);
        Assert.NotEqual(line.Entitlement.CurrentHeadRef,latest.Lines[0].Entitlement.CurrentHeadRef);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_rejection_needs_no_simulated_source_and_current_policy_permission_and_scope_are_enforced()
    {
        await using var h = await Seed(); var f = await ReturnApprovalSetup(h); var app = f.Application.ApplicationId;
        var withoutSource = FormalRma(f.Reviewer,f.Keys); var context = await withoutSource.AuthorizationContextAsync(app,includeEntitlements:false);
        var reject = new RmaAuthorizationDecideV1(context.ApplicationRef,[],"REJECTED",[],context.AuthorityRef,["UNSUPPORTED_REASON"],DateTimeOffset.UtcNow,null,null);
        var command = Guid.NewGuid(); var saved = await withoutSource.DecideAuthorizationAsync(app,command,reject); Assert.Equal("REJECTED",saved.Outcome); Assert.Empty(saved.AuthorizedShares);
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == 426 && x.ActionCode == "authorization-decide").ExecuteDeleteAsync();
        Assert.Equal(FormalJson(saved),FormalJson(await withoutSource.DecideAuthorizationAsync(app,command,reject)));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => withoutSource.DecideAuthorizationAsync(app,Guid.NewGuid(),reject))).Status);
        await h.Db.WmsRoleScopes.ExecuteDeleteAsync(); Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => withoutSource.QueryAuthorizationCommandAsync(new(app,command)))).Status);
    }
    private sealed class FailReturnAuthorizationJournal : DbCommandInterceptor
    {
        public bool Armed,Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations",StringComparison.Ordinal)) { Reached=true; throw new InvalidOperationException("INJECTED_AUTHORIZATION_JOURNAL_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_failure_rolls_back_decision_claim_share_outbox_and_original_in_one_transaction()
    {
        var fault = new FailReturnAuthorizationJournal(); await using var h = await Seed(fault); var f = await ReturnApprovalSetup(h); var app = f.Application.ApplicationId;
        var context = await f.Approver.AuthorizationContextAsync(app); var command = Guid.NewGuid(); fault.Armed=true;
        Assert.Equal("INJECTED_AUTHORIZATION_JOURNAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(() => f.Approver.DecideAuthorizationAsync(app,command,ApprovalInput(context)))).Message); Assert.True(fault.Reached);
        Assert.Equal(0,await ApprovalCount(h));
        foreach (var table in new[] { "return_claims","return_claim_segments","return_shares","authorization_outbox" })
            Assert.Equal(0,await h.Db.Database.SqlQueryRaw<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_rma_formal_v1.{table} WHERE tenant_id={{0}}",h.Tenant).SingleAsync());
        Assert.Equal("NOT_OBSERVED",(await f.Approver.QueryAuthorizationCommandAsync(new(app,command))).Status);
        Assert.Equal(context.Lines[0].Entitlement.CurrentHeadRef,(await f.Approver.AuthorizationContextAsync(app)).Lines[0].Entitlement.CurrentHeadRef);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_lost_commit_ack_and_changed_command_recover_original_without_second_claim()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await ReturnApprovalSetup(h); var app=f.Application.ApplicationId;
        var context=await f.Approver.AuthorizationContextAsync(app); var input=ApprovalInput(context); var command=Guid.NewGuid(); fault.Armed=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Approver.DecideAuthorizationAsync(app,command,input))).Message);
        var original=(await f.Approver.QueryAuthorizationCommandAsync(new(app,command))).Value!;
        Assert.Equal(FormalJson(original),FormalJson(await f.Approver.DecideAuthorizationAsync(app,command,input)));
        Assert.Equal("COMMAND_BINDING_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Approver.DecideAuthorizationAsync(app,command,input with { ReasonCodes=["CHANGED"] }))).Message);
        Assert.Equal(1,await ApprovalCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_two_connections_competing_applications_cannot_claim_the_same_source_ranges()
    {
        await using var h=await Seed();var f=await ReturnApprovalSetup(h);var context=await f.Applicant.ContextAsync();var source=(await f.Applicant.SourcesAsync(FormalSearch(context))).Items[0];
        var secondApp=await f.Applicant.CreateAsync(Guid.NewGuid(),FormalCreate(context,source,f.Application.CustomerRef));
        secondApp=await f.Applicant.SubmitAsync(secondApp.ApplicationId,Guid.NewGuid(),FormalTransition(secondApp),FormalEtag(secondApp));
        var a=ApprovalInput(await f.Approver.AuthorizationContextAsync(f.Application.ApplicationId));var b=ApprovalInput(await f.Approver.AuthorizationContextAsync(secondApp.ApplicationId));
        await using var secondDb=server.Context(new TenantContext { CurrentTenantId=h.Tenant });
        var second=FormalRma(f.Reviewer with { Db=secondDb },f.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(f.Simulation)));
        async Task<string> Approve(RmaFormalOwnerV1 owner,Guid id,RmaAuthorizationDecideV1 input) { try { await owner.DecideAuthorizationAsync(id,Guid.NewGuid(),input);return "APPROVED"; } catch(OrderPriceCorrectionException e) { return e.Message; } }
        var results=await Task.WhenAll(Approve(f.Approver,f.Application.ApplicationId,a),Approve(second,secondApp.ApplicationId,b));
        Assert.Single(results,x=>x=="APPROVED");Assert.Single(results,x=>x=="ENTITLEMENT_HEAD_STALE");Assert.Equal(1,await ApprovalCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_invalid_ranges_current_policy_and_missing_simulation_fail_without_claims()
    {
        await using var h=await Seed();var f=await ReturnApprovalSetup(h);var app=f.Application.ApplicationId;
        var context=await f.Approver.AuthorizationContextAsync(app);var input=ApprovalInput(context);var share=input.AuthorizedShares[0];
        foreach(var bad in new[] {
            share with { Ranges=[new("0","0.8"),new("0.7","1")],Quantity=new("1.1",share.Quantity.Uom) },
            share with { Ranges=[new("0","1.123456789")] },
            share with { Ranges=[new("0","2")],Quantity=new("2",share.Quantity.Uom) },
            share with { Quantity=new("1",share.Quantity.Uom) } })
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),input with { AuthorizedShares=[bad] }));
        var publish=PolicyInput(await f.Applicant.ContextAsync());
        await f.Applicant.PublishPolicyAsync(Guid.NewGuid(),publish with { ExpectedPolicyRef=context.Policy.Reference,Policy=publish.Policy with { Enabled=false } });
        Assert.Equal("AUTHORIZATION_CONTEXT_STALE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),input))).Message);
        var fresh=ApprovalInput(await f.Approver.AuthorizationContextAsync(app));
        Assert.Equal("RETURN_POLICY_DOES_NOT_ALLOW_REQUEST",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),fresh))).Message);
        var unavailable=FormalRma(f.Reviewer,f.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(new RmaReturnSimulationOptionsV1 { Enabled=true })));
        Assert.Equal("RMA_SIMULATION_SOURCE_NOT_CONFIGURED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>unavailable.AuthorizationContextAsync(app))).Message);
        var badHistory=f.Source with { HistoricalReceivedRanges=[new("0","0.1")] };
        Assert.Equal("RMA_SIMULATION_HISTORY_INCONSISTENT",Assert.Throws<InvalidOperationException>(()=>new RmaSimulatedReturnSourcesV1(Options.Create(new RmaReturnSimulationOptionsV1 { Enabled=true,CasesJson=FormalJson(new[]{badHistory}) }))).Message);
        Assert.Equal(0,await ApprovalCount(h));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_expiry_preserves_original_and_consumed_unknown_ranges_during_closure()
    {
        await using var h=await Seed();var f=await ReturnApprovalSetup(h);var app=f.Application.ApplicationId;
        var context=await f.Approver.AuthorizationContextAsync(app);var command=Guid.NewGuid();
        var input=ApprovalInput(context) with { ValidTo=DateTimeOffset.UtcNow.AddSeconds(3) };
        var saved=await f.Approver.DecideAuthorizationAsync(app,command,input);var claim=Guid.Parse(saved.AuthorizedShares[0].RangeClaimRef.Id);
        // Explicit simulated downstream receipt/unknown states exercise closure; this is not a receipt implementation.
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_rma_formal_v1.return_claim_segments SET range_to=0.25,state='CONSUMED_BY_RECEIPT'
            WHERE tenant_id={h.Tenant} AND claim_id={claim} AND ordinal=0;
            INSERT INTO cp6_rma_formal_v1.return_claim_segments(tenant_id,claim_id,ordinal,coordinate_key,range_from,range_to,state)
            SELECT tenant_id,claim_id,1,coordinate_key,0.25,0.5,'UNKNOWN_HOLD' FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={h.Tenant} AND claim_id={claim} AND ordinal=0;
            INSERT INTO cp6_rma_formal_v1.return_claim_segments(tenant_id,claim_id,ordinal,coordinate_key,range_from,range_to,state)
            SELECT tenant_id,claim_id,2,coordinate_key,0.5,1.12345678,'ACTIVE_RESERVED' FROM cp6_rma_formal_v1.return_claim_segments WHERE tenant_id={h.Tenant} AND claim_id={claim} AND ordinal=0;
            """);
        var delay=input.ValidTo!.Value-DateTimeOffset.UtcNow+TimeSpan.FromMilliseconds(50);if(delay>TimeSpan.Zero)await Task.Delay(delay);
        Assert.Equal(FormalJson(saved),FormalJson(await f.Approver.DecideAuthorizationAsync(app,command,input)));
        var current=await f.Approver.AuthorizationContextAsync(app,includeEntitlements:false);
        var closed=await f.Approver.RevokeAuthorizationAsync(app,Guid.NewGuid(),new(saved.DecisionRef,current.CurrentShareRefs,current.AuthorityRef,"Expired simulated authorization"));
        Assert.Equal("EXPIRED",closed.Outcome);
        var after=await f.Approver.AuthorizationContextAsync(app);Assert.Equal("0.5",after.Lines[0].Entitlement.ActiveClaimQty.Value);Assert.Equal("0.25",after.Lines[0].Entitlement.ReceivedQty.Value);
        Assert.Equal("1.62345678",after.Lines[0].Entitlement.RemainingAuthorizableQty.Value);Assert.Equal("0.5",after.Lines[0].AvailableRanges[0].From);
        Assert.Equal("0.62345678",RmaRangeSetV1.Text(RmaRangeSetV1.Measure(RmaRangeSetV1.Read(after.Lines[0].AvailableRanges))));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaAuthorization_actual_HTTP_simulated_source_approval_closed_input_and_read_only_recovery()
    {
        await using var h=await Seed();var f=await ReturnApprovalSetup(h);var app=f.Application.ApplicationId;var coordinates=Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"]="DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"]=coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"]=coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"]=coordinates.SiteId.ToString(),["RmaReturnSimulation__Enabled"]="true",["RmaReturnSimulation__CasesJson"]=f.Simulation.CasesJson });
        var actor=await h.Db.Sys_Users.Where(x=>x.UserName=="simulated-return-reviewer").Select(x=>x.Id).SingleAsync();var cookies=await DocumentReviewerLogin(host,h,actor);
        async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode expected=HttpStatusCode.OK) {
            using var response=await host.Send(request);var text=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==expected,response.StatusCode+": "+text);
            Assert.True(response.Headers.CacheControl?.NoStore);return JsonSerializer.Deserialize<T>(text,RmaJson)!;
        }
        var path=$"api/wms/rma/v1/applications/{app}/authorization";
        Assert.Null(await Receive<RmaAuthorizationDecisionV1?>(FormalRequest(cookies,HttpMethod.Get,path)));
        var context=await Receive<RmaAuthorizationContextV1>(FormalRequest(cookies,HttpMethod.Get,path+"/context?includeEntitlements=true"));Assert.Equal("SIMULATED",context.Lines[0].EvidenceMode);
        var input=ApprovalInput(context);var command=Guid.NewGuid();
        var extra=JsonSerializer.SerializeToNode(input,RmaJson)!.AsObject();extra["skipPolicy"]=true;
        var invalid=FormalRequest(cookies,HttpMethod.Post,path+"/decide",command:Guid.NewGuid());invalid.Content=new StringContent(extra.ToJsonString(),System.Text.Encoding.UTF8,"application/json");
        await Receive<JsonElement>(invalid,HttpStatusCode.BadRequest);
        var noCsrf=FormalRequest(cookies,HttpMethod.Post,path+"/decide",input,command);noCsrf.Headers.Remove("X-CSRF-TOKEN");
        using(var denied=await host.Send(noCsrf))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var saved=await Receive<RmaAuthorizationDecisionV1>(FormalRequest(cookies,HttpMethod.Post,path+"/decide",input,command));Assert.Equal("APPROVED",saved.Outcome);
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==426 && x.ActionCode=="authorization-decide").ExecuteDeleteAsync();
        var recovered=await Receive<RmaAuthorizationCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,"api/wms/rma/v1/authorization/commands/query",new RmaAuthorizationQueryV1(app,command)));
        Assert.Equal(FormalJson(saved),FormalJson(recovered.Value));Assert.False(recovered.CanInferNoEffect);
        var current=await Receive<RmaAuthorizationContextV1>(FormalRequest(cookies,HttpMethod.Get,path+"/context"));Assert.Empty(current.Lines);
        var closed=await Receive<RmaAuthorizationDecisionV1>(FormalRequest(cookies,HttpMethod.Post,path+"/revoke",new RmaAuthorizationRevokeV1(saved.DecisionRef,current.CurrentShareRefs,current.AuthorityRef,"SIMULATED CUSTOMER WITHDRAWAL"),Guid.NewGuid()));Assert.Equal("REVOKED",closed.Outcome);
        Assert.Equal(FormalJson(saved),FormalJson(await Receive<RmaAuthorizationDecisionV1>(FormalRequest(cookies,HttpMethod.Get,path+"?version=1"))));
    }
}
