using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class ConnAppWitness(string name) : IWfConnector
    {
        public string Name => name;
        public string DisplayName => "同名隔离应用";
        public int Calls {get;private set;}
        public Task<ServiceTaskResult> CallAsync(string pathTemplate,string? parameters,ServiceTaskContext job)
        {
            Calls++;
            return Task.FromResult(ServiceTaskResult.Ok());
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task OaConn_connector_exact_pin_stale_real_job_dispatch_retire_and_immutable_credential()
    {
        await using var h=await Seed();
        await RegisterScope(h);
        await ConnPeople(h);
        var tenantProfile=await ConnRegistration(h,"CONNECTOR",ConnFixture());
        var appProfile=await ConnRegistration(h,"CONNECTOR",ConnFixture("APP"));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var checker=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var selector=new ConnSelectionV1("same-connector","TENANT",tenantProfile);
        var tenant=await ConnCreate(maker,"CONNECTOR",selector,"租户目录");
        var app=await ConnCreate(maker,"CONNECTOR",selector with {SourceKind="APP",ProfileId=appProfile},"应用目录");
        tenant=await ConnActivate(maker,checker,tenant);
        app=await ConnActivate(maker,checker,app);
        var current=await maker.Data(tenant.Route+"/context");
        Assert.DoesNotContain("authJson",current.GetRawText(),StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opaque-binding",current.GetRawText(),StringComparison.Ordinal);
        var revised=await maker.Data(tenant.Route+"/versions",ConnCmd(tenant.Head,new ConnDraftV1(selector,"仅调整显示名")));
        var newVersion=revised.GetProperty("versionId").GetGuid();
        Assert.NotEqual(tenant.Version,newVersion);
        tenant=tenant with {Head=revised.GetProperty("result").GetProperty("revision").GetString()!};
        var old=await maker.Data(tenant.Route+"/versions/"+tenant.Version);
        Assert.Equal("租户目录",old.GetProperty("displayName").GetString());
        var fresh=await maker.Data(tenant.Route+"/versions/"+newVersion);
        Assert.Equal("generation-1",fresh.GetProperty("profile").GetProperty("credentialGeneration").GetString());
        Assert.True(fresh.GetProperty("profile").GetProperty("hasAuth").GetBoolean());
        var otherGeneration=await ConnRegistration(h,"CONNECTOR",ConnFixture() with {CredentialGeneration="generation-2"});
        await maker.Reject(tenant.Route+"/versions",ConnCmd(tenant.Head,new ConnDraftV1(selector with{ProfileId=otherGeneration},"普通元数据变更")),
            HttpStatusCode.Forbidden,"CREDENTIAL_OWNER_REQUIRED");
        using var withSecret=await maker.Send(tenant.Route+"/versions",ConnCmd(tenant.Head,
            new { selection=selector,displayName="不合规的元数据",authJson="simulated-sensitive-input" }));
        Assert.Equal(HttpStatusCode.BadRequest,withSecret.StatusCode);
        var stale=await ConnImpact(maker,tenant,"DISABLE");
        var job=new Wf_ServiceJob { InstanceId=Guid.NewGuid(),TokenId=Guid.NewGuid(),Kind="webApi",NodeId="registered-operation",
            DueAtUtc=DateTime.UtcNow,NextAttemptAtUtc=DateTime.UtcNow,MaxAttempts=2,ActionRefJson=ServiceTaskActionRef.Snapshot(new FlowNode{
                ServiceKind="webApi",ServiceConnectorName=selector.Name,ServiceConnectorPin=new("TENANT",tenant.Id,tenant.Version,"lookup-v1")}) };
        await using(var other=server.Context(new CP6.Core.Services.Common.TenantContext {CurrentTenantId=h.Tenant}))
        {
            await using var tx=await other.Database.BeginTransactionAsync();
            other.Wf_FlowInstances.Add(new(){Id=job.InstanceId,FlowKey="isolated-connector-job",CurrentNode=job.NodeId,StarterId=h.User});
            other.Wf_FlowTokens.Add(new(){Id=job.TokenId,InstanceId=job.InstanceId,NodeId=job.NodeId});
            other.Wf_ServiceJobs.Add(job); await other.SaveChangesAsync(); await tx.CommitAsync();
        }
        var originalHead=(await maker.Data(tenant.Route+"/context")).GetProperty("revision").GetString();
        Assert.Equal(tenant.Head,originalHead);
        object Stop(JsonElement p)=>ConnCmd(tenant.Head,new ConnControlInputV1(tenant.Version,p.GetProperty("previewId").GetGuid(),null,"独立复核停用"));
        await maker.Reject(tenant.Route+"/disable",Stop(stale),HttpStatusCode.PreconditionFailed,"IMPACT_CHANGED");
        var next=await ConnImpact(maker,tenant,"DISABLE");
        Assert.Equal(1,next.GetProperty("manifest").GetProperty("jobs").GetInt32());
        await using(var other=new NpgsqlConnection(h.Db.Database.GetConnectionString()))
        {
            await other.OpenAsync();
            await using var tx=await other.BeginTransactionAsync();
            await using var insert=new NpgsqlCommand("""
                INSERT INTO cp6_oa_conn_v1.runtime_members(tenant_id,environment_id,root_id,version_id,kind,id,state)
                VALUES($1,$2,$3,$4,'DISPATCH',$5,'SENT')
                """,other,tx);
            foreach(var value in new[]{h.Tenant,ScopeEnvironment,tenant.Id,tenant.Version,job.Id}) insert.Parameters.Add(new NpgsqlParameter{Value=value});
            await insert.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        await maker.Reject(tenant.Route+"/disable",Stop(next),HttpStatusCode.PreconditionFailed,"IMPACT_CHANGED");
        var accepted=await ConnImpact(checker,tenant,"DISABLE");
        Assert.Contains(accepted.GetProperty("entries").EnumerateArray(),entry=>entry.GetProperty("kind").GetString()=="DISPATCH"
            && entry.GetProperty("state").GetString()=="SENT");
        var stopped=await checker.Data(tenant.Route+"/disable",Stop(accepted));
        Assert.Equal(1,stopped.GetProperty("result").GetProperty("notRevokedInFlight").GetInt32());
        tenant=tenant with {Head=stopped.GetProperty("result").GetProperty("revision").GetString()!};
        var runtime=new ConnectorControlRuntimeV1(h.Db,ScopeFence(h),Options.Create(new OaConnectorControlOptionsV1 { AllowSimulatedParticipants=true }),[]);
        await using(var tx=await h.Db.Database.BeginTransactionAsync())
        {
            await ScopeFence(h).AcquireAsync();
            Assert.Null(await runtime.Resolve(new("TENANT",tenant.Id,tenant.Version,"lookup-v1"),default));
            var exact=await runtime.Resolve(new("APP",app.Id,app.Version,"lookup-v1"),default);
            Assert.Equal("fixture-APP",exact!.Implementation);
            Assert.Null(await runtime.Resolve(new("APP",tenant.Id,tenant.Version,"lookup-v1"),default));
            await tx.CommitAsync();
        }
        var witness=new ConnAppWitness(selector.Name);
        var exec=new CP6.Core.Services.Wf.Executors.WebApiExecutor(
            new CP6.Core.Services.Wf.Executors.TenantConnectorResolver(null,[witness]),runtime);
        var ctx=new ServiceTaskContext{ JobId=job.Id,InstanceId=job.InstanceId,TokenId=job.TokenId,NodeId=job.NodeId,
            StarterId=h.User,ActorId=h.User,AttemptNo=1,NowUtc=DateTime.UtcNow,ActionRefJson=job.ActionRefJson };
        Assert.Equal("BLOCKED_CONNECTOR",(await exec.ExecuteAsync(ctx)).Error);
        var unpinned=new ServiceTaskContext{JobId=job.Id,InstanceId=job.InstanceId,TokenId=job.TokenId,NodeId=job.NodeId,
            ActorId=h.User,StarterId=h.User,AttemptNo=1,NowUtc=DateTime.UtcNow,
            ActionRefJson=ServiceTaskActionRef.Snapshot(new FlowNode{ServiceKind="webApi",ServiceConnectorName=selector.Name,ServicePath="/old-path"})};
        Assert.Equal("BLOCKED_CONNECTOR",(await exec.ExecuteAsync(unpinned)).Error);
        Assert.Equal(0,witness.Calls);
        var worker=new WfServiceJobService(h.Db,new FlowEngine(h.Db,new ApproverResolver(h.Db)),[exec]);
        Assert.Equal(1,await worker.ScanOnceAsync(DateTime.UtcNow,"wp12-13-isolated"));
        var blocked=await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j=>j.Id==job.Id);
        Assert.Equal("BLOCKED_CONNECTOR",blocked.LastError);
        Assert.Equal(ServiceJobStatus.Pending,blocked.Status);
        Assert.Equal(0,blocked.AttemptCount);
        Assert.Equal(FlowTokenStatus.Active,await h.Db.Wf_FlowTokens.Where(t=>t.Id==job.TokenId).Select(t=>t.Status).SingleAsync());
        var retire=await ConnImpact(checker,tenant,"RETIRE");
        var id=retire.GetProperty("previewId").GetGuid();
        var before=await maker.Data(tenant.Route+"/versions/"+tenant.Version);
        var activateVote=before.GetProperty("approvals").EnumerateArray().Single(a=>a.GetProperty("kind").GetString()=="CONNECTOR_ACTIVATE").GetProperty("decisionRef").GetGuid();
        await maker.Reject(tenant.Route+"/retire",ConnCmd(tenant.Head,new ConnControlInputV1(tenant.Version,id,activateVote,"退役")),HttpStatusCode.Conflict,"REVIEW_SUBJECT_MISMATCH");
        var retireVote=await ConnReview(maker,checker,tenant,"RETIRE",id);
        var retired=await maker.Data(tenant.Route+"/retire",ConnCmd(tenant.Head,new ConnControlInputV1(tenant.Version,id,retireVote,"退役")));
        Assert.Equal("RETIRED",retired.GetProperty("result").GetProperty("state").GetString());
        output.WriteLine($"TENANT {tenant.Id}/{tenant.Version}, APP {app.Id}/{app.Version}, Job {job.Id}; 412×2、SENT=1、APP 调用 {witness.Calls}。");
    }
    [TenantManagementPostgresTheory]
    [InlineData("UNPROVEN",10,"EXTERNAL_CONTRACT_UNPROVEN")]
    [InlineData("ADOPTED",10,"EXTERNAL_CONTRACT_UNPROVEN")]
    [InlineData("SIMULATED",null,"CALL_DURATION_UNPROVEN")]
    [InlineData("SIMULATED",300,"CALL_DURATION_UNPROVEN")]
    public async Task OaConn_unproven_provider_or_total_duration_cannot_activate(string adoption,int? duration,string code)
    {
        await using var h=await Seed(); await RegisterScope(h); await ConnPeople(h);
        var p=await ConnRegistration(h,"CONNECTOR",ConnFixture(duration:duration),adoption);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var checker=new ConnApi(host,await Login(host,"conn-reviewer","Reviewer1!Test",h.Tenant.ToString("N")));
        var target=await ConnCreate(maker,"CONNECTOR",new("unproven","TENANT",p),"未证连接器");
        var preview=await ConnImpact(maker,target,"ACTIVATE");
        var id=preview.GetProperty("previewId").GetGuid();
        var vote=await ConnReview(maker,checker,target,"ACTIVATE",id);
        await maker.Reject(target.Route+"/activate",ConnCmd(target.Head,new ConnControlInputV1(target.Version,id,vote,"验证准入")),
            code=="EXTERNAL_CONTRACT_UNPROVEN"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.UnprocessableEntity,code);
        var unchanged=await maker.Data(target.Route+"/context");
        Assert.Equal("DRAFT",unchanged.GetProperty("state").GetString());
        Assert.Equal(adoption=="SIMULATED"?"SIMULATED":"UNPROVEN",unchanged.GetProperty("profile").GetProperty("adoption").GetString());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_conn_v1.audit WHERE tenant_id={h.Tenant} AND operation='CONNECTOR_ACTIVATE'
            """).SingleAsync());
    }
}
