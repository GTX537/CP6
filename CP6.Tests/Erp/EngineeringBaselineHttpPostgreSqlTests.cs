using System.Net;
using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Pub;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_actual_Program_HTTP_freeze_closed_body_CSRF_reads_and_original_cancellation()
    {
        await using var h=await Seed();var settings=await DocumentHostSettings(new ContentStore());
        settings["RequirementEvaluation__EvidenceMode"]="DEMO";settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var cookies=await Login(host,h);
        // Login updates the native user's admission revision. The actual check/review must
        // be created afterwards, under that current identity, rather than relaxing freshness.
        var f=await BaselineSetup(h);var files=new LocalFileStore(settings["Storage__LocalRoot"]);
        foreach(var entry in f.Review.Source.Files.Bytes)await files.SaveAsync(new MemoryStream(entry.Value),entry.Key);
        const string root="/api/plm-engineering-baselines/owner-v1";
        async Task<T> Read<T>(string path){using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,root+path));Assert.True(response.Headers.CacheControl?.NoStore);return await DocumentHttpData<T>(response);}
        async Task<HttpResponseMessage> Post(string path,object body,bool csrf=true){using var request=OperationsRequest(cookies,HttpMethod.Post,root+path,body);if(!csrf)request.Headers.Remove("X-CSRF-Token");return await host.Send(request);}
        var scope=await Read<CP6.Core.Services.Mes.QualitySpecificationScopeV1>("/scope");Assert.Equal(h.Tenant,scope.TenantId);
        using(var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,"/api/engineering-source-checks/owner-v1/"+f.Command.SourceCheck.Id)))
        {
            var source=await DocumentHttpData<EngineeringCheckViewV1>(response);
            Assert.True(source.Freshness=="CURRENT",string.Join(",",source.Changes));
        }
        Assert.Single((await Read<EngineeringFreezeChoiceV1[]>("/choices/"+f.Command.IterationId)).SelectMany(x=>x.Candidates));
        using(var response=await Post("/freeze",f.Command,false))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var forged=JsonSerializer.SerializeToNode(f.Command,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();forged["approved"]=true;
        using(var response=await Post("/freeze",forged))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        EngineeringFreezeResultV1 accepted;using(var response=await Post("/freeze",f.Command))accepted=await DocumentHttpData<EngineeringFreezeResultV1>(response);
        Assert.Equal(accepted,(await Read<EngineeringFreezeOperationV1>("/operations/"+f.Command.OperationId)).Result);
        Assert.Equal(accepted.Baseline,(await Read<EngineeringBaselineViewV1>("/"+accepted.Baseline.Id)).Reference);
        var cancel=new EngineeringFreezeCancelV1(Guid.NewGuid(),f.Command.OperationId,"UNKNOWN","Cancel sees accepted original");
        using(var response=await Post("/cancel",cancel))Assert.Equal(accepted,(await DocumentHttpData<EngineeringFreezeCancellationV1>(response)).Outcome.Result);
        Assert.Single(await Read<EngineeringBaselineViewV1[]>(""));Assert.Contains(await Read<EngineeringBaselineUsageV1[]>("/"+accepted.Baseline.Id+"/where-used"),x=>x.Status=="UNKNOWN");
        Assert.Empty((await Read<EngineeringBaselineDiffV1>("/diff?before="+accepted.Baseline.Id+"&after="+accepted.Baseline.Id)).Changes);
        Assert.Equal(1,await BaselineCount(h,"baselines"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_output_binding_mapping_preserves_native_manifest_and_complete_relation_without_claiming_internal_freeze()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var relation=await OutputQualified(h,f);
        var revisions=new EngineeringRevisionOwnerV1(h.Db,OutputAuthority(h),Documents(h,f.Quality.Files),Specifications(h,f.Quality.Files));
        var revision=await revisions.ReadAsync(f.Manufacturing.Body.RevisionOrigin.Id,null);
        // This is the closed output-binding mapper only. Internal manufacturing preconditions remain blocked;
        // no baseline or review approval is inserted, and the reference is a local serialization fixture.
        var manifest=EngineeringSourceCheckValuesV1.Manifest(revision);
        var command=new EngineeringFreezeCommandV1(Guid.NewGuid(),Guid.NewGuid(),null!,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),revision.Reference,revision.CandidateRef,
            revision.Etag,f.Manufacturing.Body.ConfigurationRef,null!,manifest.Digest,new string('a',64),"Local mapper qualification only");
        var original=new EngineeringBaselineOriginalV1(EngineeringBaselineValuesV1.Schema,ScopeEnvironment,h.Tenant,Guid.NewGuid(),"1",h.User,DateTimeOffset.UtcNow,"DEMO",command,
            revision,null!,"",null!,manifest,relation.Version.Reference,relation.Version.Original.WireOriginal,"APPLIES");
        var reference=new EngineeringSourceRefV1(EngineeringBaselineValuesV1.Owner,original.Id,"1",new string('b',64));
        var binding=EngineeringBaselineValuesV1.Binding(reference,original,relation.Version);
        Assert.Equal(relation.Version.Original.Wire.BodyDigest,binding.RelationBodyDigest);Assert.Equal(manifest.Digest,JsonDocument.Parse(Convert.FromBase64String(binding.OriginalManifestDigest.CanonicalReferenceBase64)).RootElement.GetProperty("value").GetString());
        Assert.Throws<CP6.Core.Services.Erp.OrderPriceCorrectionException>(()=>EngineeringBaselineValuesV1.Binding(reference,original with{Command=command with{Configuration=command.Configuration with{Id=Guid.NewGuid()}}},relation.Version));
        if(Environment.GetEnvironmentVariable("CP6_BASELINE_BINDING_EVIDENCE") is {} path)await File.WriteAllBytesAsync(path,EngineeringBaselineValuesV1.Bytes(binding));
        Assert.Equal(0,await BaselineCount(h,"baselines"));
    }
}
