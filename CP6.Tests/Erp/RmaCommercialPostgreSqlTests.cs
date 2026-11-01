using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string CommercialAmount="999999999999999999999999999999.12345678";
    private static FulfilmentRefV1 CommercialRef(string id,string owner="Simulation.ERP") => new(owner,id,"1",new string('a',64));
    private static JsonElement CommercialResult(RmaCommercialOwnerRequestV1 input,string kind="REFUND")
    {
        var request=input.Request;var line=Assert.Single(request.Lines);var share=line.Share;var outcome=CommercialRef(request.RequestId+"-outcome","ERP-CREDIT-01");
        object Money(string amount) => new{value=amount,currency="USD"};
        object credit=new{kind="CREDIT",componentRef=CommercialRef("credit"),invoiceAllocations=new[]{new{allocationId=Guid.NewGuid(),invoiceLineRef=line.Source.InvoiceLineRef,
            coveredClaimRefs=new[]{share.RangeClaimRef},coveredRanges=share.Ranges,quantity=share.Quantity,net=Money(CommercialAmount),tax=Money("0"),gross=Money(CommercialAmount)}},
            approvedQuantity=share.Quantity,approvalRef=CommercialRef("approval"),stableEffectKey=request.RequestId+":credit"};
        object[] components=kind switch
        {
            "CREDIT"=>[credit],
            "REFUND"=>[credit,new{kind="REFUND_AUTHORIZATION",componentRef=CommercialRef("refund"),creditComponentRef=CommercialRef("credit"),refundAuthorizationRef=CommercialRef("refund-authority"),authorizedAmount=Money(CommercialAmount),currency="USD",stableEffectKey=request.RequestId+":refund"}],
            "RETURN_ONLY"=>[new{kind,componentRef=CommercialRef("return"),coveredClaimRefs=new[]{share.RangeClaimRef},coveredRanges=share.Ranges,quantity=share.Quantity,reasonCode="NO_COMMERCIAL_CREDIT"}],
            "REJECTED"=>[new{kind,componentRef=CommercialRef("reject"),coveredClaimRefs=new[]{share.RangeClaimRef},coveredRanges=share.Ranges,quantity=share.Quantity,reasonCodes=new[]{"POLICY_REJECTED"}}],
            _=>[new{kind,componentRef=CommercialRef(kind),coveredClaimRefs=new[]{share.RangeClaimRef},coveredRanges=share.Ranges,quantity=share.Quantity,newDemandRef=CommercialRef("new-demand"),approvalRef=CommercialRef("approval"),stableEffectKey=request.RequestId+":"+kind}]
        };
        return JsonSerializer.SerializeToElement(new{owner="ERP-CREDIT-01",requestRef=input.RequestRef,outcomeRef=outcome,resultVersion=outcome.Version,resultDigest=outcome.Digest,
            rmaId=request.ApplicationId,components,approvalRef=CommercialRef("approval"),currentHeadProof=CommercialRef("head"),previousOutcomeRef=(object?)null,recordedAt=request.RequestedAt,terminalAt=DateTimeOffset.UtcNow},RmaJson);
    }
    private static JsonElement CommercialFinance(RmaCommercialOwnerRequestV1 request,JsonElement commercial,bool complete=false,bool inventory=false)
    {
        var outcome=commercial.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaJson);var at=DateTimeOffset.UtcNow;
        object Axis(string axis,string state,string? amount,FulfilmentRefV1? source,FulfilmentRefV1[] physical) => new{axis,owner="Simulation.Finance",state,physicalReceiptRefs=physical,
            qualityDecisionRef=(object?)null,commercialOutcomeRef=source,sourceCutRef=CommercialRef("cut"),resultRef=state=="POSTED" ? CommercialRef(axis+"-result") : null,
            amount,currency=amount==null ? null : "USD",postingReceiptRefs=state=="POSTED" ? new[]{CommercialRef(axis+"-posting")} : [],reasonCodes=Array.Empty<string>(),observedAt=at,coverage=state=="POSTED" ? "COMPLETE" : "UNKNOWN"};
        var axes=inventory ? new[]{Axis("INVENTORY_VALUE","WAITING_COST",null,null,request.Request.PhysicalReceiptRefs)}
            : commercial.GetProperty("components").GetArrayLength()==1 ? new[]{Axis("AR_CREDIT","POSTED",CommercialAmount,outcome,[])}
            : new[]{Axis("AR_CREDIT","POSTED",CommercialAmount,outcome,[]),Axis("REFUND_CASH",complete ? "POSTED" : "PENDING",complete ? CommercialAmount : null,outcome,[])};
        return JsonSerializer.SerializeToElement(new{rmaId=request.Request.ApplicationId,policyRef=CommercialRef("finance-policy"),policyVersion="1",axes,
            aggregateState=inventory ? "PENDING" : complete || axes.Length==1 ? "COMPLETE" : "PARTIAL",observedAt=at,sourceCutRef=CommercialRef("cut")},RmaJson);
    }
    // Explicit simulated external ERP/Finance service. Requests and outcomes survive service restart on disk.
    private sealed class CommercialSimulation : IAsyncDisposable
    {
        private readonly HttpListener listener=new();private readonly Task loop;private volatile bool stopped;
        public string Url {get;} public string DirectoryPath {get;}
        public bool LoseReply;public Func<RmaCommercialObservationV1,RmaCommercialObservationV1>? Transform;
        public CommercialSimulation(string? directory=null,string? url=null)
        {
            DirectoryPath=directory ?? Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,"simulated-commercial-"+Guid.NewGuid());Directory.CreateDirectory(DirectoryPath);
            if(url==null){var port=new TcpListener(IPAddress.Loopback,0);port.Start();url="http://127.0.0.1:"+((IPEndPoint)port.LocalEndpoint).Port+"/";port.Stop();}
            Url=url;listener.Prefixes.Add(Url);listener.Start();loop=Run();
        }
        public string ResultPath(Guid tenant,Guid id)=>Path.Combine(DirectoryPath,tenant+"-"+id+"-result.json");
        private async Task Run()
        {
            try{while(listener.IsListening)
            {
                var context=await listener.GetContextAsync();var input=(await JsonSerializer.DeserializeAsync<RmaCommercialOwnerRequestV1>(context.Request.InputStream,RmaJson))!;
                Assert.Equal(input.Request.TenantId.ToString("N")+":"+input.Request.RequestId.ToString("N"),context.Request.Headers["Idempotency-Key"]);
                var requestFile=Path.Combine(DirectoryPath,input.Request.TenantId+"-"+input.Request.RequestId+"-request.json");var resultFile=ResultPath(input.Request.TenantId,input.Request.RequestId);
                if(context.Request.Url!.AbsolutePath=="/requests")
                {
                    if(!File.Exists(requestFile))
                    {
                        await File.WriteAllTextAsync(requestFile,FormalJson(input));var outcome=CommercialResult(input,input.Request.RequestedOutcome=="REJECT" ? "REJECTED" : input.Request.RequestedOutcome);
                        var financial=input.Request.RequestedOutcome=="RETURN_ONLY" && input.Request.PhysicalReceiptRefs.Length>0 ? CommercialFinance(input,outcome,inventory:true)
                            : input.Request.RequestedOutcome is "CREDIT" or "REFUND" ? CommercialFinance(input,outcome) : (JsonElement?)null;
                        await File.WriteAllTextAsync(resultFile,FormalJson(new RmaCommercialObservationV1(input.Request.TenantId,input.RequestRef,"FOUND",outcome,financial,DateTimeOffset.UtcNow,[],"SIMULATED")));
                    }
                    else Assert.Equal(await File.ReadAllTextAsync(requestFile),FormalJson(input));
                    if(LoseReply){LoseReply=false;context.Response.StatusCode=503;context.Response.Close();continue;}
                }
                var value=File.Exists(resultFile) ? JsonSerializer.Deserialize<RmaCommercialObservationV1>(await File.ReadAllTextAsync(resultFile),RmaJson)!
                    : new(input.Request.TenantId,input.RequestRef,"NOT_OBSERVED",null,null,DateTimeOffset.UtcNow,[],"SIMULATED");
                if(Transform!=null)value=Transform(value);
                context.Response.ContentType="application/json";await JsonSerializer.SerializeAsync(context.Response.OutputStream,value,RmaJson);context.Response.Close();
            }}catch(HttpListenerException) when(stopped){}catch(ObjectDisposedException) when(stopped){}
        }
        public async ValueTask DisposeAsync(){stopped=true;listener.Close();await loop;}
    }
    private static async Task CommercialGrants(Harness h)
    {foreach(var action in new[]{"commercial-query","commercial-request","commercial-dispatch"}) await EngineeringAction(h,"wms-rma",action);}
    private static RmaCommercialRequestV1 CommercialInput(RmaCommercialContextV1 context,string outcome="REFUND")
        => new(context.ApplicationRef,context.Authorization!.DecisionRef,context.ShareRefs,outcome,"SIMULATED_ACCEPTANCE");
    private static RmaFormalOwnerV1 CommercialOwner(Harness h,ReturnApprovalFixture f,CommercialSimulation simulation)
        => FormalRma(h,f.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(f.Simulation)),commercial:new RmaCommercialHttpGatewayV1(new HttpClient(),Options.Create(new RmaCommercialGatewayOptionsV1{BaseUrl=simulation.Url})));
    [CurrentPermissionPostgresFact]
    public async Task RmaCommercial_native_HTTP_recovers_durable_external_result_and_independent_financial_axes_without_duplicate_effect()
    {
        await using var h=await Seed();var f=await PhysicalReturnSetup(h,false);await CommercialGrants(h);var app=f.Approval.Application.ApplicationId;
        var physical=await PreparePhysical(f);await f.Owner.CommitPhysicalReturnAsync(app,physical.Work.WorkId,Guid.NewGuid(),PhysicalAction(physical));
        var external=new CommercialSimulation{LoseReply=true};var directory=external.DirectoryPath;var url=external.Url;
        try
        {
            var coordinates=Assert.Single(FormalCoordinates(h).WarehouseSites);
            await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{
                ["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString(),["QualitySpecification__EvidenceMode"]="DEMO",
                ["RmaApplication__WarehouseSites__0__TenantId"]=coordinates.TenantId.ToString(),["RmaApplication__WarehouseSites__0__WarehouseId"]=coordinates.WarehouseId.ToString(),
                ["RmaApplication__WarehouseSites__0__SiteId"]=coordinates.SiteId.ToString(),["RmaReturnSimulation__Enabled"]="true",["RmaReturnSimulation__CasesJson"]=f.Approval.Simulation.CasesJson,
                ["RmaInboundSimulation__Enabled"]="true",["RmaInboundSimulation__CasesJson"]=FormalJson(new[]{f.Case}),["RmaCommercialOwner__BaseUrl"]=url});
            var cookies=await DocumentReviewerLogin(host,h,h.User);var path=$"api/wms/rma/v1/commercial/applications/{app}";
            async Task<T> Receive<T>(HttpRequestMessage request,HttpStatusCode status=HttpStatusCode.OK)
            {using var response=await host.Send(request);var body=await response.Content.ReadAsStringAsync();Assert.True(response.StatusCode==status,response.StatusCode+": "+body);Assert.True(response.Headers.CacheControl?.NoStore);return JsonSerializer.Deserialize<T>(body,RmaJson)!;}
            var context=await Receive<RmaCommercialContextV1>(FormalRequest(cookies,HttpMethod.Get,path+"/context"));var input=CommercialInput(context);var key=Guid.NewGuid();
            var forged=JsonSerializer.SerializeToNode(input,RmaJson)!.AsObject();forged["approvedAmount"]="0";
            await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Post,path+"/requests",forged,key),HttpStatusCode.BadRequest);
            var noCsrf=FormalRequest(cookies,HttpMethod.Post,path+"/requests",input,key);noCsrf.Headers.Remove("X-CSRF-TOKEN");using(var denied=await host.Send(noCsrf))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            var saved=await Receive<RmaCommercialWorkV1>(FormalRequest(cookies,HttpMethod.Post,path+"/requests",input,key));Assert.Equal("NOT_OBSERVED",saved.ObservationState);
            await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Post,path+"/requests",input,Guid.NewGuid()),HttpStatusCode.Conflict);
            var unknown=await Receive<RmaCommercialWorkV1>(FormalRequest(cookies,HttpMethod.Post,path+$"/requests/{key}/dispatch",new{}));Assert.Equal("UNKNOWN",unknown.ObservationState);Assert.Null(unknown.CommercialOutcome);
            await external.DisposeAsync();external=new(directory,url);
            var found=await Receive<RmaCommercialWorkV1>(FormalRequest(cookies,HttpMethod.Post,path+$"/requests/{key}/refresh",new{}));Assert.Equal("FOUND",found.ObservationState);
            Assert.Equal("PARTIAL",found.FinancialOutcome!.Value.GetProperty("aggregateState").GetString());
            Assert.Equal(CommercialAmount,found.CommercialOutcome!.Value.GetProperty("components")[0].GetProperty("invoiceAllocations")[0].GetProperty("gross").GetProperty("value").GetString());
            Assert.Equal(JsonValueKind.Null,found.FinancialOutcome.Value.GetProperty("axes")[1].GetProperty("amount").ValueKind);
            external.Transform=value=>value with{TenantId=Guid.NewGuid()};
            var invalidResult=await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Post,path+$"/requests/{key}/dispatch",new{}),HttpStatusCode.BadGateway);
            Assert.Equal("UNKNOWN",invalidResult.GetProperty("commitKnowledge").GetString());
            Assert.Equal("/"+path+$"/requests/{key}/refresh",invalidResult.GetProperty("recovery").GetProperty("queryPath").GetString());external.Transform=null;
            var response=JsonSerializer.Deserialize<RmaCommercialObservationV1>(await File.ReadAllTextAsync(external.ResultPath(h.Tenant,key)),RmaJson)!;
            response=response with{FinancialOutcome=CommercialFinance(new(saved.RequestRef,saved.Request),response.CommercialOutcome!.Value,true),ObservedAt=DateTimeOffset.UtcNow};
            await File.WriteAllTextAsync(external.ResultPath(h.Tenant,key),FormalJson(response));
            var complete=await Receive<RmaCommercialWorkV1>(FormalRequest(cookies,HttpMethod.Post,path+$"/requests/{key}/dispatch",new{}));Assert.Equal("COMPLETE",complete.FinancialOutcome!.Value.GetProperty("aggregateState").GetString());
            Assert.Single(Directory.GetFiles(directory,"*-request.json"));Assert.Single(Directory.GetFiles(directory,"*-result.json"));
            await RevokePhysicalGrant(h,"wms-rma","commercial-request");
            Assert.Equal(FormalJson(saved),FormalJson(await Receive<RmaCommercialWorkV1>(FormalRequest(cookies,HttpMethod.Post,path+"/requests",input,key))));
            var original=await Receive<RmaCommercialCommandResultV1>(FormalRequest(cookies,HttpMethod.Post,"api/wms/rma/v1/commercial/commands/query",new RmaCommercialQueryV1(app,key)));Assert.Equal(FormalJson(saved),FormalJson(original.Value));
            var evidence=Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE");if(evidence!=null)await File.WriteAllTextAsync(evidence,FormalJson(new{saved,unknown,found,complete,simulatedExternalDirectory=directory}));
            await RevokePhysicalGrant(h,"wms-rma","commercial-query");await Receive<JsonElement>(FormalRequest(cookies,HttpMethod.Get,path+"/context"),HttpStatusCode.Forbidden);
            Assert.Single(await h.Db.InboundReceipts.ToArrayAsync());
        }
        finally{await external.DisposeAsync();}
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaCommercial_boundary_rejects_bad_money_overlap_refund_tenant_and_false_financial_completion_without_overwriting_original()
    {
        await using var h=await Seed();var f=await ReturnApprovalSetup(h);await CommercialGrants(h);var app=f.Application.ApplicationId;
        await f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(await f.Approver.AuthorizationContextAsync(app)));
        await using var external=new CommercialSimulation();var owner=CommercialOwner(h,f,external);var work=await owner.RequestCommercialAsync(app,Guid.NewGuid(),CommercialInput(await owner.CommercialContextAsync(app)));
        work=await owner.ObserveCommercialAsync(app,work.Request.RequestId,true);var original=FormalJson(work);
        foreach(var mutation in new Action<JsonObject>[] {
            x=>x["tenantId"]=Guid.NewGuid().ToString(),
            x=>x["commercialOutcome"]!["components"]![0]!["invoiceAllocations"]![0]!["gross"]!["value"]="1",
            x=>x["commercialOutcome"]!["components"]![1]!["authorizedAmount"]!["value"]="999999999999999999999999999999.12345679",
            x=>x["commercialOutcome"]!["components"]!.AsArray().Add(x["commercialOutcome"]!["components"]![0]!.DeepClone()),
            x=>x["financialOutcome"]!["aggregateState"]="COMPLETE",
            x=>x["commercialOutcome"]!["components"]![0]!["invoiceAllocations"]![0]!["coveredRanges"]![0]!["to"]="9999999"
        })
        {
            external.Transform=value=>{var node=JsonSerializer.SerializeToNode(value,RmaJson)!.AsObject();mutation(node);return node.Deserialize<RmaCommercialObservationV1>(RmaJson)!;};
            Assert.Equal(502,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ObserveCommercialAsync(app,work.Request.RequestId,false))).Status);
            Assert.Equal(original,FormalJson(Assert.Single((await owner.CommercialContextAsync(app)).Requests)));
        }
        external.Transform=value=>value with{State="UNKNOWN",CommercialOutcome=null,FinancialOutcome=null};
        var unknown=await owner.ObserveCommercialAsync(app,work.Request.RequestId,false);Assert.Equal("UNKNOWN",unknown.ObservationState);Assert.Equal(work.CommercialOutcome!.Value.GetRawText(),unknown.CommercialOutcome!.Value.GetRawText());Assert.False(unknown.CanInferNoEffect);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_rma_formal_v1.commercial_history WHERE tenant_id={h.Tenant}"));
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaCommercial_request_journal_rollback_and_current_revoke_are_recoverable()
    {
        var fault=new FailFormalJournal();await using var h=await Seed(fault);var f=await ReturnApprovalSetup(h);await CommercialGrants(h);var app=f.Application.ApplicationId;
        await f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(await f.Approver.AuthorizationContextAsync(app)));
        await using var external=new CommercialSimulation();var owner=CommercialOwner(h,f,external);var input=CommercialInput(await owner.CommercialContextAsync(app));var command=Guid.NewGuid();
        fault.Enabled=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.RequestCommercialAsync(app,command,input));fault.Enabled=false;
        Assert.Empty((await owner.CommercialContextAsync(app)).Requests);Assert.Equal("NOT_OBSERVED",(await owner.QueryCommercialCommandAsync(new(app,command))).Status);
        var work=await owner.RequestCommercialAsync(app,command,input);
        Assert.Equal("COMMAND_BINDING_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.RequestCommercialAsync(app,command,input with{ReasonCode="changed"}))).Message);
        var auth=await f.Approver.AuthorizationContextAsync(app);await f.Approver.RevokeAuthorizationAsync(app,Guid.NewGuid(),new(auth.CurrentDecision!.DecisionRef,auth.CurrentShareRefs,auth.AuthorityRef,"Cancel before commercial dispatch"));
        Assert.Equal("AUTHORIZATION_NOT_EFFECTIVE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ObserveCommercialAsync(app,command,true))).Message);
        Assert.Equal(FormalJson(work),FormalJson((await owner.QueryCommercialCommandAsync(new(app,command))).Value));Assert.Empty(Directory.GetFiles(external.DirectoryPath,"*-request.json"));
        await RevokePhysicalGrant(h,"wms-rma","commercial-query");Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.QueryCommercialCommandAsync(new(app,command)))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaCommercial_lost_local_commit_ack_recovers_exact_original_and_ranges_remain_held_after_authorization_release()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var f=await ReturnApprovalSetup(h);await CommercialGrants(h);var app=f.Application.ApplicationId;
        await f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(await f.Approver.AuthorizationContextAsync(app)));
        await using var external=new CommercialSimulation();var owner=CommercialOwner(h,f,external);var input=CommercialInput(await owner.CommercialContextAsync(app));var command=Guid.NewGuid();
        fault.Armed=true;Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.RequestCommercialAsync(app,command,input))).Message);
        var recovered=await owner.RequestCommercialAsync(app,command,input);Assert.Single((await owner.CommercialContextAsync(app)).Requests);
        Assert.Equal(FormalJson(recovered),FormalJson((await owner.QueryCommercialCommandAsync(new(app,command))).Value));
        var auth=await f.Approver.AuthorizationContextAsync(app);await f.Approver.RevokeAuthorizationAsync(app,Guid.NewGuid(),new(auth.CurrentDecision!.DecisionRef,auth.CurrentShareRefs,auth.AuthorityRef,"New commercial intent must retain original range"));
        auth=await f.Approver.AuthorizationContextAsync(app);await f.Approver.DecideAuthorizationAsync(app,Guid.NewGuid(),ApprovalInput(auth));
        var replacement=CommercialInput(await owner.CommercialContextAsync(app));
        var denied=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.RequestCommercialAsync(app,Guid.NewGuid(),replacement));
        Assert.Equal("RMA_COMMERCIAL_RANGE_ALREADY_REQUESTED",denied.Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task RmaCommercial_return_only_inventory_waits_for_cost_and_other_commercial_outcomes_are_real_observations()
    {
        foreach(var kind in new[]{"RETURN_ONLY","REPLACEMENT","SUPPLEMENTARY_SHIPMENT","REJECT","CREDIT"})
        {
            await using var h=await Seed();var f=await PhysicalReturnSetup(h,false);await CommercialGrants(h);var app=f.Approval.Application.ApplicationId;
            var physical=await PreparePhysical(f);await f.Owner.CommitPhysicalReturnAsync(app,physical.Work.WorkId,Guid.NewGuid(),PhysicalAction(physical));
            await using var external=new CommercialSimulation();
            var owner=FormalRma(h,f.Approval.Keys,sources:new RmaSimulatedReturnSourcesV1(Options.Create(f.Approval.Simulation)),
                inboundSources:new RmaSimulatedInboundSourcesV1(h.Db,Options.Create(new RmaInboundSimulationOptionsV1{Enabled=true,CasesJson=FormalJson(new[]{f.Case})})),
                commercial:new RmaCommercialHttpGatewayV1(new HttpClient(),Options.Create(new RmaCommercialGatewayOptionsV1{BaseUrl=external.Url})));
            var saved=await owner.RequestCommercialAsync(app,Guid.NewGuid(),CommercialInput(await owner.CommercialContextAsync(app),kind));
            var result=await owner.ObserveCommercialAsync(app,saved.Request.RequestId,true);Assert.Equal("FOUND",result.ObservationState);
            Assert.Equal(kind=="REJECT" ? "REJECTED" : kind,result.CommercialOutcome!.Value.GetProperty("components")[0].GetProperty("kind").GetString());
            if(kind=="RETURN_ONLY")
            {
                var axis=result.FinancialOutcome!.Value.GetProperty("axes")[0];Assert.Equal("WAITING_COST",axis.GetProperty("state").GetString());
                Assert.Equal(JsonValueKind.Null,axis.GetProperty("commercialOutcomeRef").ValueKind);Assert.Equal(JsonValueKind.Null,axis.GetProperty("amount").ValueKind);
                Assert.Equal(saved.Request.PhysicalReceiptRefs.Single(),axis.GetProperty("physicalReceiptRefs")[0].Deserialize<FulfilmentRefV1>(RmaJson));
            }
            Assert.Single(Directory.GetFiles(external.DirectoryPath,"*-request.json"));
        }
    }
}
