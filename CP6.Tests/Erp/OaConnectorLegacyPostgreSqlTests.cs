using System.Net;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task OaConn_legacy_disabled_source_handoff_fences_old_control_and_marks_unproven_inflight()
    {
        await using var h=await Seed();
        await RegisterScope(h); await ConnPeople(h);
        await EngineeringAction(h,"oa-flow-admin","Connector.Edit");
        var legacy=new Wf_Connector{Name="legacy-local",DisplayName="旧登记",BaseUrl="https://example.invalid",Enabled=false};
        h.Db.Wf_Connectors.Add(legacy); await h.Db.SaveChangesAsync();
        var profile=await ConnRegistration(h,"CONNECTOR",ConnFixture());
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var maker=new ConnApi(host,await Login(host,h));
        var selector=new ConnSelectionV1(legacy.Name,"TENANT",profile);
        await maker.Reject("creation-contexts?kind=CONNECTOR",selector,HttpStatusCode.Conflict,"LEGACY_ROOT_SELECTION_REQUIRED");
        var options=await maker.Data("options?kind=CONNECTOR");
        Assert.Contains(options.GetProperty("legacySources").EnumerateArray(),e=>e.GetProperty("id").GetGuid()==legacy.Id);
        var target=await ConnCreate(maker,"CONNECTOR",selector with {LegacyRootId=legacy.Id},"接管旧来源");
        Assert.Equal(legacy.Id,target.Id);
        using var refused=await host.Send(OperationsRequest(maker.Cookies,HttpMethod.Post,"api/oa/wf-connector/"+legacy.Id+"/enabled",new {enabled=true}));
        Assert.Equal(HttpStatusCode.Gone,refused.StatusCode);
        var failure=await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Wf_Connectors.Where(r=>r.Id==legacy.Id)
            .ExecuteUpdateAsync(s=>s.SetProperty(r=>r.Enabled,true)));
        Assert.Equal("TRUSTED_CONN_V1_REQUIRED",failure.MessageText);
        var job=new Wf_ServiceJob{InstanceId=Guid.NewGuid(),TokenId=Guid.NewGuid(),Kind="webApi",NodeId="legacy",
            Status=ServiceJobStatus.Running,AttemptCount=1,DueAtUtc=DateTime.UtcNow,NextAttemptAtUtc=DateTime.UtcNow,
            ActionRefJson=ServiceTaskActionRef.Snapshot(new FlowNode{ServiceKind="webApi",ServiceConnectorName=legacy.Name,ServicePath="/legacy"})};
        h.Db.Wf_ServiceJobs.Add(job); await h.Db.SaveChangesAsync();
        var preview=await ConnImpact(maker,target,"ACTIVATE");
        Assert.Equal("PARTIAL_UNPROVEN",preview.GetProperty("coverage").GetString());
        Assert.Equal("UNVERIFIED_LEGACY",preview.GetProperty("entries")[0].GetProperty("state").GetString());
        await maker.Reject(target.Route+"/submit-review",ConnCmd(target.Head,new ConnReviewInputV1(target.Version,"CONNECTOR_ACTIVATE",
                preview.GetProperty("previewId").GetGuid())),HttpStatusCode.PreconditionFailed,"IMPACT_COVERAGE_INCOMPLETE");
        Assert.Equal("DRAFT",(await maker.Data(target.Route+"/context")).GetProperty("state").GetString());
    }
}
