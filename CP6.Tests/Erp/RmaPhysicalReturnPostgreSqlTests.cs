using System.Data.Common;
using System.Net;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed record PhysicalReturnFixture(ReturnApprovalFixture Approval, RmaFormalOwnerV1 Owner, RmaInboundSimulationCaseV1 Case);
    private async Task<PhysicalReturnFixture> PhysicalReturnSetup(Harness h, bool originalRoot = true)
    {
        var approval = await ReturnApprovalSetup(h); var app = approval.Application.ApplicationId;
        foreach (var action in new[] { "physical-register","physical-prepare","physical-post","physical-seal" }) await EngineeringAction(h,"wms-rma",action);
        foreach (var action in new[] { "query","post" }) await EngineeringAction(h,"wms-inbound-receipt",action);
        h.Db.Locations.Add(new() { WarehouseCd = "W01",LocationCd = "RMA-Q",AreaCd = "A",LocationName = "Return pending inspection",IsPickable = false });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var decision = await approval.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(await approval.Approver.AuthorizationContextAsync(app)));
        var share = decision.AuthorizedShares[0]; var original = share.Original;
        var shipmentLine = await h.Db.OutboundOrderDetails.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(original.ShipmentLineId));
        var native = await h.Db.StockTransactions.AsNoTracking().SingleAsync(x => x.TxnNo == shipmentLine.ShipTxnNo);
        FulfilmentRefV1 Evidence(string kind) => new("Simulation."+kind,Guid.NewGuid().ToString(),"1",new string('a',64));
        var dims = new ReturnStockDimensionsV1(FormalCoordinates(h).WarehouseSites[0].SiteId,original.ProductRef,new("NOT_APPLICABLE",null,Evidence("TechnicalPolicy")),
            new(native.OwnerType,Evidence("StockOwner")),new("RETURN","1"),new(original.BaseQty.Uom.Code,original.BaseQty.Uom.Version),new("DECIMAL_8","1"),
            native.LotNo == "" ? new("NOT_TRACKED",null,null,Evidence("LotPolicy")) : new("TRACKED",native.LotNo,native.LotNo,null),null,null);
        var scenario = new RmaInboundSimulationCaseV1(h.Tenant,original.ShipmentRef,original.ShipmentLineId,"1",dims,
            originalRoot ? [new("0",original.BaseQty.Value,Guid.NewGuid(),"10",RmaRangeSetV1.Text(10+decimal.Parse(original.BaseQty.Value,System.Globalization.CultureInfo.InvariantCulture)))] : [],[],
            originalRoot ? JsonSerializer.SerializeToElement(new { kind = "EXTERNAL",custodyReceiptRef = Evidence("ExternalCustody") },RmaJson) : null,
            originalRoot ? null : Evidence("NewPhysicalSource"),Evidence("ReturnQuality"));
        return new(approval,PhysicalReturnOwner(h,approval,scenario),scenario);
    }
    private static RmaFormalOwnerV1 PhysicalReturnOwner(Harness h, ReturnApprovalFixture approval, params RmaInboundSimulationCaseV1[] scenarios)
        => FormalRma(h,approval.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(approval.Simulation)),
            inboundSources:new RmaSimulatedInboundSourcesV1(h.Db,Options.Create(new RmaInboundSimulationOptionsV1 { Enabled = true,CasesJson = FormalJson(scenarios) })));
    private static RmaRegisterPhysicalReturnV1 PhysicalInput(RmaPhysicalReturnContextV1 c, string from = "0", string to = "0.5")
    {
        var share = c.Shares[0];
        return new(c.Authorization!.DecisionRef,c.HeadRef,[new(Guid.NewGuid(),share.ShareRef,[new(from,to)],
            new(RmaRangeSetV1.Text(decimal.Parse(to,System.Globalization.CultureInfo.InvariantCulture)-decimal.Parse(from,System.Globalization.CultureInfo.InvariantCulture)),share.Share.Quantity.Uom),
            c.Locations.Single().OptionRef,share.Share.Original.LotRefs.ToArray(),share.Share.Original.SerialRefs.ToArray(),share.Share.Original.LpnRefs.ToArray(),DateTimeOffset.UtcNow)],null);
    }
    private static RmaPhysicalWorkCommandV1 PhysicalAction(RmaPhysicalWorkViewV1 work) => new(work.WorkRef,work.RmaHeadRef,"Simulated acceptance receipt");
    private static async Task RevokePhysicalGrant(Harness h, string resource, string action)
    {
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == resource).Select(x => x.MenuId).SingleAsync();
        Assert.Equal(1,await h.Db.Sys_RoleActions.Where(x => x.RoleId == 72 && x.MenuId == menu && x.ActionCode == action).ExecuteDeleteAsync());
    }
    private static async Task<RmaPhysicalWorkViewV1> PreparePhysical(PhysicalReturnFixture f, string from = "0", string to = "0.5")
    {
        var app = f.Approval.Application.ApplicationId; var context = await f.Owner.PhysicalReturnContextAsync(app);
        var work = await f.Owner.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),PhysicalInput(context,from,to));
        return await f.Owner.PreparePhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),PhysicalAction(work));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_original_root_partial_receipt_updates_actual_stock_inbound_claims_and_preserves_original_recovery()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var before = await h.Db.StockTransactions.CountAsync(); var work = await PreparePhysical(f,"0","0.12345678");
        Assert.Equal(before,await h.Db.StockTransactions.CountAsync()); var prep = Assert.Single(work.Preparations);
        Assert.Equal("RETURN",prep.StockPostingPlanRequest.Use.Action); Assert.Equal("RETURN",prep.StockPostingPlanRequest.Data.GetProperty("kind").GetString());
        var command = Guid.NewGuid(); var input = PhysicalAction(work);
        var result = await f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input);
        Assert.Equal("SUCCEEDED",result.Work.State); var receipt = Assert.Single(Assert.Single(result.Results).PhysicalReceipts);
        Assert.Equal("0.12345678",receipt.Quantity.Value); Assert.Equal("POSTED",receipt.ReceiptState);
        Assert.Equal(0.12345678m,await h.Db.Stocks.Where(x => x.LocationCd == "RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        Assert.Equal("INBOUND-RMA",(await h.Db.StockTransactions.AsNoTracking().SingleAsync(x => x.Id == Guid.Parse(receipt.MovementRef.Id))).RelatedType);
        Assert.Equal("RMA",(await h.Db.InboundReceipts.AsNoTracking().SingleAsync()).SourceType);
        Assert.Equal(0.12345678m,(await h.Db.InboundReceiptDetails.AsNoTracking().SingleAsync()).ReceivedQty);
        Assert.Equal("0.12345678",(await f.Approval.Approver.AuthorizationContextAsync(app)).Lines[0].Entitlement.ReceivedQty.Value);
        var context = await f.Owner.PhysicalReturnContextAsync(app); Assert.Equal("0.12345678",context.Shares[0].AvailableRanges[0].From);
        await RevokePhysicalGrant(h,"wms-rma","physical-post");
        Assert.Equal(FormalJson(result),FormalJson(await f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input)));
        Assert.Equal(FormalJson(result),FormalJson((await f.Owner.QueryPhysicalCommandAsync(new(app,command))).Value));
        Assert.Equal(before+1,await h.Db.StockTransactions.CountAsync());
        var evidence = Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE");
        if (evidence != null) await File.WriteAllTextAsync(evidence,FormalJson(result));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_new_source_receipt_and_no_effect_seal_are_actual_durable_terminals()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h,false); var app = f.Approval.Application.ApplicationId;
        var work = await PreparePhysical(f); Assert.Equal("RECEIVE",work.Preparations[0].StockPostingPlanRequest.Use.Action);
        var sealedWork = await f.Owner.SealPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),PhysicalAction(work));
        Assert.Equal("SEALED_NO_EFFECT",sealedWork.Work.State); Assert.NotNull(sealedWork.NoEffectProofRef); Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());
        Assert.Equal("RMA_PHYSICAL_STATE_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),PhysicalAction(sealedWork)))).Message);
        var replacement = await PreparePhysical(f); var applied = await f.Owner.CommitPhysicalReturnAsync(app,replacement.Work.WorkId,Guid.NewGuid(),PhysicalAction(replacement));
        Assert.Single(applied.Results[0].StockResult.Result!.Roots); Assert.Equal("0.5",applied.Results[0].StockResult.Result!.Roots[0].Quantity);
        Assert.Equal(0.5m,await h.Db.Stocks.Where(x => x.LocationCd == "RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_receiving_preserves_full_authenticated_account_name()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h);
        var name = new string('x',100);
        await h.Db.Sys_Users.Where(x => x.Id == f.Approval.Reviewer.User).ExecuteUpdateAsync(x => x.SetProperty(u => u.UserName,name));
        f = f with { Owner = PhysicalReturnOwner(f.Approval.Reviewer,f.Approval,f.Case) };
        var work = await PreparePhysical(f);
        var result = await f.Owner.CommitPhysicalReturnAsync(f.Approval.Application.ApplicationId,work.Work.WorkId,Guid.NewGuid(),PhysicalAction(work));
        Assert.Equal("SUCCEEDED",result.Work.State);
        Assert.Equal(name,(await h.Db.InboundReceipts.AsNoTracking().SingleAsync()).OperatorCd);
        Assert.Equal(name,(await h.Db.StockTransactions.AsNoTracking().SingleAsync(x => x.RelatedType == "INBOUND-RMA")).OperatorCd);
    }
    private sealed class FailPhysicalJournal : DbCommandInterceptor
    {
        public bool Armed,Reached;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations",StringComparison.Ordinal)) { Reached = true; throw new InvalidOperationException("INJECTED_PHYSICAL_JOURNAL_FAILURE"); }
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_failure_after_stock_rolls_back_every_participant_then_original_retry_applies_once()
    {
        var fault = new FailPhysicalJournal(); await using var h = await Seed(fault); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var work = await PreparePhysical(f); var command = Guid.NewGuid(); var input = PhysicalAction(work); var before = await h.Db.StockTransactions.CountAsync(); fault.Armed = true;
        Assert.Equal("INJECTED_PHYSICAL_JOURNAL_FAILURE",(await Assert.ThrowsAsync<InvalidOperationException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input))).Message); Assert.True(fault.Reached);
        Assert.Equal(before,await h.Db.StockTransactions.CountAsync()); Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());
        Assert.Equal("NOT_OBSERVED",(await f.Owner.QueryPhysicalCommandAsync(new(app,command))).Status);
        Assert.Equal("PREPARED",(await f.Owner.ReadPhysicalWorkAsync(app,work.Work.WorkId)).Work.State);
        Assert.Equal("0",(await f.Approval.Approver.AuthorizationContextAsync(app)).Lines[0].Entitlement.ReceivedQty.Value);
        fault.Armed = false; Assert.Equal("SUCCEEDED",(await f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input)).Work.State);
        Assert.Equal(before+1,await h.Db.StockTransactions.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_lost_commit_ack_recovers_original_and_revocation_retains_received_ranges()
    {
        var fault = new CommitFault(); await using var h = await Seed(fault); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var work = await PreparePhysical(f); var command = Guid.NewGuid(); var input = PhysicalAction(work); fault.Armed = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input))).Message);
        var saved = (await f.Owner.QueryPhysicalCommandAsync(new(app,command))).Value!;
        Assert.Equal("SUCCEEDED",saved.Work.State); Assert.Equal(FormalJson(saved),FormalJson(await f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,command,input)));
        var context = await f.Approval.Approver.AuthorizationContextAsync(app);
        await f.Approval.Approver.RevokeAuthorizationAsync(app,Guid.NewGuid(),new(context.CurrentDecision!.DecisionRef,context.CurrentShareRefs,context.AuthorityRef,"Customer withdrew remainder"));
        var after = await f.Approval.Approver.AuthorizationContextAsync(app);
        Assert.Equal("0.5",after.Lines[0].Entitlement.ActiveClaimQty.Value); Assert.Equal("0.5",after.Lines[0].Entitlement.ReceivedQty.Value);
        Assert.Equal(FormalJson(saved),FormalJson((await f.Owner.QueryPhysicalCommandAsync(new(app,command))).Value));
        Assert.Single(await h.Db.InboundReceipts.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_two_connections_serialize_range_registration_and_commit_versus_seal()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        await using var db2 = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var second = PhysicalReturnOwner(h with { Db = db2 },f.Approval,f.Case);
        var input = PhysicalInput(await f.Owner.PhysicalReturnContextAsync(app));
        async Task<(RmaPhysicalWorkViewV1? Work,string? Error)> Attempt(Func<Task<RmaPhysicalWorkViewV1>> apply)
        { try { return (await apply(),null); } catch (OrderPriceCorrectionException e) { return (null,e.Message); } }
        var registration = await Task.WhenAll(Attempt(() => f.Owner.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),input)),
            Attempt(() => second.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),input with { ReceiptLines = [input.ReceiptLines[0] with { LineId = Guid.NewGuid() }] })));
        var saved = Assert.Single(registration,x => x.Work != null).Work!; Assert.Equal("CAS_CONFLICT",Assert.Single(registration,x => x.Error != null).Error);
        var fresh = await f.Owner.PhysicalReturnContextAsync(app);
        Assert.Equal("RETURN_RANGE_NOT_AVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),PhysicalInput(fresh)))).Message);
        var prepared = await f.Owner.PreparePhysicalReturnAsync(app,saved.Work.WorkId,Guid.NewGuid(),new(saved.WorkRef,fresh.HeadRef,"Prepare"));
        var terminals = await Task.WhenAll(Attempt(() => f.Owner.CommitPhysicalReturnAsync(app,saved.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared))),
            Attempt(() => second.SealPhysicalReturnAsync(app,saved.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared))));
        var terminal = Assert.Single(terminals,x => x.Work != null).Work!; Assert.Equal("CAS_CONFLICT",Assert.Single(terminals,x => x.Error != null).Error);
        Assert.Equal(terminal.Work.State == "SUCCEEDED" ? 1 : 0,await h.Db.InboundReceipts.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_current_location_capacity_rights_and_revoked_authorization_prevent_effects()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var work = await PreparePhysical(f); var input = PhysicalAction(work);
        await h.Db.Locations.Where(x => x.LocationCd == "RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPickable,true));
        Assert.Equal("RETURN_LOCATION_NOT_QUARANTINED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),input))).Message);
        await h.Db.Locations.Where(x => x.LocationCd == "RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPickable,false).SetProperty(x => x.CapacityQty,0.2m));
        Assert.Equal("RETURN_LOCATION_CAPACITY_EXCEEDED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),input))).Message);
        await h.Db.Locations.Where(x => x.LocationCd == "RMA-Q").ExecuteUpdateAsync(s => s.SetProperty(x => x.CapacityQty,0m));
        await RevokePhysicalGrant(h,"wms-inbound-receipt","post");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),input))).Status);
        await EngineeringAction(h,"wms-inbound-receipt","post");
        var context = await f.Approval.Approver.AuthorizationContextAsync(app);
        await f.Approval.Approver.RevokeAuthorizationAsync(app,Guid.NewGuid(),new(context.CurrentDecision!.DecisionRef,context.CurrentShareRefs,context.AuthorityRef,"Revoke before receipt"));
        Assert.Equal("AUTHORIZATION_NOT_EFFECTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.CommitPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),input))).Message);
        Assert.Empty(await h.Db.InboundReceipts.ToArrayAsync());
        Assert.Equal("SEALED_NO_EFFECT",(await f.Owner.SealPhysicalReturnAsync(app,work.Work.WorkId,Guid.NewGuid(),input)).Work.State);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_actual_HTTP_runs_native_receipt_and_recovers_with_current_read_rights()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h,false); var app = f.Approval.Application.ApplicationId;
        var coordinates = Assert.Single(FormalCoordinates(h).WarehouseSites);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string> {
            ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"] = "DEMO",
            ["RmaApplication__WarehouseSites__0__TenantId"] = coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"] = coordinates.WarehouseId.ToString(),
            ["RmaApplication__WarehouseSites__0__SiteId"] = coordinates.SiteId.ToString(),["RmaReturnSimulation__Enabled"] = "true",["RmaReturnSimulation__CasesJson"] = f.Approval.Simulation.CasesJson,
            ["RmaInboundSimulation__Enabled"] = "true",["RmaInboundSimulation__CasesJson"] = FormalJson(new[] { f.Case }) });
        var cookies = await DocumentReviewerLogin(host,h,h.User);
        async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode status = HttpStatusCode.OK)
        {
            using var response = await host.Send(request); var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == status,response.StatusCode+": "+body); Assert.True(response.Headers.CacheControl?.NoStore);
            return JsonSerializer.Deserialize<T>(body,RmaJson)!;
        }
        var path = $"api/wms/rma/v1/applications/{app}/physical";
        var context = await Receive<RmaPhysicalReturnContextV1>(FormalRequest(cookies,HttpMethod.Get,path+"/context")); var input = PhysicalInput(context);
        var extra = JsonSerializer.SerializeToNode(input,RmaJson)!.AsObject(); extra["skipStock"] = true;
        var invalid = FormalRequest(cookies,HttpMethod.Post,path+"/registrations",command:Guid.NewGuid()); invalid.Content = new StringContent(extra.ToJsonString(),System.Text.Encoding.UTF8,"application/json");
        await Receive<JsonElement>(invalid,HttpStatusCode.BadRequest);
        var registered = await Receive<RmaPhysicalWorkViewV1>(FormalRequest(cookies,HttpMethod.Post,path+"/registrations",input,Guid.NewGuid()));
        var workPath = path+"/works/"+registered.Work.WorkId;
        var pending = await Receive<JsonElement[]>(FormalRequest(cookies,HttpMethod.Get,workPath+"/inbound")); Assert.Equal("PENDING",pending[0].GetProperty("status").GetString());
        var prepared = await Receive<RmaPhysicalWorkViewV1>(FormalRequest(cookies,HttpMethod.Post,workPath+"/prepare",PhysicalAction(registered),Guid.NewGuid()));
        var notObserved = await Receive<JsonElement[]>(FormalRequest(cookies,HttpMethod.Get,workPath+"/inbound")); Assert.Equal("UNKNOWN",notObserved[0].GetProperty("status").GetString()); Assert.False(notObserved[0].GetProperty("canInferNoEffect").GetBoolean());
        var command = Guid.NewGuid(); var noCsrf = FormalRequest(cookies,HttpMethod.Post,workPath+"/commit",PhysicalAction(prepared),command); noCsrf.Headers.Remove("X-CSRF-TOKEN");
        using(var denied = await host.Send(noCsrf)) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var saved = await Receive<RmaPhysicalWorkViewV1>(FormalRequest(cookies,HttpMethod.Post,workPath+"/commit",PhysicalAction(prepared),command)); Assert.Equal("SUCCEEDED",saved.Work.State);
        var inbound = await Receive<JsonElement[]>(FormalRequest(cookies,HttpMethod.Get,workPath+"/inbound")); Assert.Equal("FOUND",inbound[0].GetProperty("status").GetString());
        var wire = Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE");
        if (wire != null) await File.WriteAllTextAsync(wire.Replace(".json","-http.json",StringComparison.Ordinal),FormalJson(new { Saved = saved,Outcomes = new[] { pending[0],notObserved[0],inbound[0] } }));
        await RevokePhysicalGrant(h,"wms-rma","physical-post");
        var recovered = await Receive<RmaPhysicalCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,"api/wms/rma/v1/physical/commands/query",new RmaPhysicalCommandQueryV1(app,command)));
        Assert.Equal(FormalJson(saved),FormalJson(recovered.Value)); Assert.False(recovered.CanInferNoEffect); Assert.Single(await h.Db.InboundReceipts.ToArrayAsync());
        await RevokePhysicalGrant(h,"wms-inbound-receipt","query");
        await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Get,workPath),HttpStatusCode.Forbidden);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_receipt_department_scope_uses_all_actual_original_shipments()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        // The native shipment fixture already created and used these actual departments for Stock ownership.
        var permissions = new CurrentPermissionContext(new OperationsHttp { HttpContext = h.Http },new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),h.Db,new PermissionAggregator(h.Db));
        var authority = new IamAdministrationV1(h.Db,permissions,new EphemeralDataProtectionProvider());
        var departments = await h.Db.Sys_Depts.AsNoTracking().ToDictionaryAsync(x => x.DeptCode,x => x.Id);
        var admin = new MappingHarness(new(h.Db,authority),authority,departments["IAM-root"],departments["NOT-SALES"],departments["Other"]);
        await SaveMapping(h,admin,await MappingCommand(h,admin));
        await h.Db.Orders.ExecuteUpdateAsync(s => s.SetProperty(x => x.OrderDepartment,"SALES"));
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeptId,admin.Child));
        h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72,ResourceKey = "wms-inbound-receipt",ScopeType = 2 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var prepared = await PreparePhysical(f);
        var result = await f.Owner.CommitPhysicalReturnAsync(app,prepared.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared));
        Assert.Equal("SUCCEEDED",(await f.Owner.ReadPhysicalWorkAsync(app,prepared.Work.WorkId)).Work.State);
        await h.Db.Sys_Users.Where(x => x.Id == h.User).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeptId,admin.Other));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => f.Owner.ReadPhysicalWorkAsync(app,prepared.Work.WorkId))).Status);
        Assert.Single(result.Results[0].PhysicalReceipts);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_multiple_lines_share_one_stock_pool_and_keep_disjoint_root_lineage()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var context = await f.Owner.PhysicalReturnContextAsync(app); var first = PhysicalInput(context,"0","0.12345678");
        var second = PhysicalInput(context,"0.5","1").ReceiptLines[0];
        var registered = await f.Owner.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),first with { ReceiptLines = [first.ReceiptLines[0],second] });
        var prepared = await f.Owner.PreparePhysicalReturnAsync(app,registered.Work.WorkId,Guid.NewGuid(),PhysicalAction(registered));
        var result = await f.Owner.CommitPhysicalReturnAsync(app,registered.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared));
        Assert.Equal(2,result.Results[0].PhysicalReceipts.Length); Assert.Equal(2,result.Results[0].StockResult.Result!.RelatedMovementIds.Length);
        Assert.Equal(0.62345678m,await h.Db.Stocks.Where(x => x.LocationCd == "RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        Assert.Equal("0.62345678",(await f.Approval.Approver.AuthorizationContextAsync(app)).Lines[0].Entitlement.ReceivedQty.Value);
        var available = (await f.Owner.PhysicalReturnContextAsync(app)).Shares[0].AvailableRanges;
        Assert.Equal(new[] { new FulfilmentRangeV1("0.12345678","0.5"),new FulfilmentRangeV1("1","1.12345678") },available);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaPhysical_changed_source_map_cannot_receive_previously_returned_root_spans_again()
    {
        await using var h = await Seed(); var f = await PhysicalReturnSetup(h); var app = f.Approval.Application.ApplicationId;
        var prepared = await PreparePhysical(f);
        await f.Owner.CommitPhysicalReturnAsync(app,prepared.Work.WorkId,Guid.NewGuid(),PhysicalAction(prepared));
        var map = f.Case.RootMap[0];
        var changed = f.Case with { Version = "2",RootMap = [map with { RootFrom = "9.5",RootTo = RmaRangeSetV1.Text(StockRmaPostingV1.Number(map.RootTo)-0.5m) }] };
        var owner = PhysicalReturnOwner(h,f.Approval,changed);
        var context = await owner.PhysicalReturnContextAsync(app);
        var registered = await owner.RegisterPhysicalReturnAsync(app,Guid.NewGuid(),PhysicalInput(context,"0.5","1"));
        Assert.Equal("RETURN_ROOT_ALREADY_RECEIVED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.PreparePhysicalReturnAsync(app,registered.Work.WorkId,Guid.NewGuid(),PhysicalAction(registered)))).Message);
        Assert.Equal(0.5m,await h.Db.Stocks.Where(x => x.LocationCd == "RMA-Q").Select(x => x.PhysicalQty).SingleAsync());
        Assert.Equal(1,await h.Db.InboundReceipts.CountAsync());
    }
}
