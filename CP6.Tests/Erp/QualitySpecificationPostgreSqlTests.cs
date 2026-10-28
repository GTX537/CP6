using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 QualityAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"quality-specification","plm-controlled-document","file-content","plm-engineering-revision","plm-engineering-product","space-site","uom-profile"})
        {
            catalog.RegisterScope(key,key,key=="space-site"?[5]:[1,2,3,4,5],5);
            catalog.RegisterFields(key,key=="quality-specification"?[new("Metadata","Metadata"),new("Requirements","Requirements")]:
                key=="file-content"?[new("Content","Content"),new("FileName","Name")]:key=="plm-controlled-document"?[new("Content","Content"),new("Metadata","Metadata")]:[]);
        }
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static QualitySpecificationAuthorityService Specifications(Harness h,ContentStore files,string mode="DEMO")
        =>new(h.Db,QualityAuthority(h),ScopeFence(h),new(mode),Documents(h,files));
    private sealed record QualityFixture(ContentStore Files,Guid First,Guid Second,ControlledDocumentSnapshotV1 Document,QualitySpecificationCommandV1 Create);
    private async Task<QualityFixture> QualitySetup(Harness h)
    {
        // Actual native document policy, FLOW decisions and Owner application; only FILE bytes/scanner are explicit doubles.
        var f=await DocumentApplicationSetup(h);var d=f.Document;FactAck(await f.Receiver.ReceiveAsync(f.Payload));
        var docs=DocumentReviews(h,d.Files);var ready=await docs.SubmissionAsync(d.Document,d.Submission);
        await docs.ApplyAsync(d.Document,ready.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Apply exact source approval"));
        var document=await Documents(h,d.Files).ReadAsync(d.Document);
        foreach(var action in new[]{"query","add","edit","review-policy","review-submit","review-decide","withdraw"})await EngineeringAction(h,"quality-specification",action);
        await EngineeringAction(h,"space-site","query");
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="quality-specification",ScopeType=5});
        foreach(var actor in new[]{d.First,d.Second})
            if(!await h.Db.Sys_UserRoles.AnyAsync(x=>x.UserId==actor&&x.RoleId==72))h.Db.Sys_UserRoles.Add(new(){UserId=actor,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var site=await PhysicalSite(h,"QUALITY");var siteRef=(await EngineeringMappings(h).SiteChoicesAsync("QUALITY")).Single(x=>x.Reference.Id==site).Reference;
        var requirement=new QualityRequirementV1("label-original","Original label","Compare approved original","All required label fields match the approved original",document.Reference,
            null,null,null,null,"APPLIES",null,true,false,null);
        var body=new QualitySpecificationBodyV1("Independent label standard",document.Body.ProductRef,siteRef,"IQC",["INCOMING"],document.Reference,
            "Professional review covers all requirements in this source; no automatic technical NA",[requirement]);
        return new(d.Files,d.First,d.Second,document,new(Guid.NewGuid(),Guid.NewGuid(),"CREATE",null,"Register independent quality source",body));
    }
    private static QualitySpecificationCommandV1 QualityCommand(QualitySpecificationViewV1 view,string action)
        =>new(Guid.NewGuid(),view.Version.Reference.Id,action,view.Etag,"Explicit quality professional action");
    private static async Task<QualitySpecificationViewV1> QualityReady(QualitySpecificationAuthorityService owner,QualitySpecificationCommandV1 create)
    {
        await owner.CommandAsync(create);var view=await owner.ReadAsync(create.SpecificationId);
        var policy=new QualityReviewPolicyInputV1("ENABLED",[new("PROFESSIONAL",74,2)],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1));
        await owner.CommandAsync(QualityCommand(view,"POLICY") with{Policy=policy});view=await owner.ReadAsync(create.SpecificationId);
        await owner.CommandAsync(QualityCommand(view,"SUBMIT"));return await owner.ReadAsync(create.SpecificationId);
    }
    private static async Task<QualitySpecificationViewV1> QualityApprove(Harness h,QualityFixture f)
    {
        var owner=Specifications(h,f.Files);await QualityReady(owner,f.Create);
        foreach(var actor in new[]{f.First,f.Second})
        {
            var reviewer=Specifications(DecisionActor(h,actor),f.Files);
            await reviewer.CommandAsync(QualityCommand(await reviewer.ReadAsync(f.Create.SpecificationId),"APPROVE"));
        }
        return await owner.ReadAsync(f.Create.SpecificationId);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_independent_signatures_exact_source_history_replay_and_withdrawal()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);
        var first=await QualityReady(owner,f.Create);Assert.Null(first.CurrentApproval);Assert.Equal("IN_REVIEW",first.Control.State);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(QualityCommand(first,"APPROVE")))).Status);
        var one=Specifications(DecisionActor(h,f.First),f.Files);var command=QualityCommand(await one.ReadAsync(f.Create.SpecificationId),"APPROVE");
        var signed=await one.CommandAsync(command);Assert.Equal(signed,await one.CommandAsync(command));
        Assert.Null((await owner.ReadAsync(f.Create.SpecificationId)).CurrentApproval);
        var two=Specifications(DecisionActor(h,f.Second),f.Files);await two.CommandAsync(QualityCommand(await two.ReadAsync(f.Create.SpecificationId),"APPROVE"));
        var approved=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal("APPROVED",approved.Control.State);Assert.Equal(2,approved.CurrentApproval!.Body.Votes.Length);
        var proof=await owner.SourceAsync(approved.Version.Reference);Assert.Equal("AVAILABLE",proof.Availability);Assert.Equal("READ_ONLY_OBSERVATION",proof.Observation);
        Assert.Equal("label-original",Assert.Single(proof.Requirements).Id);
        await owner.CommandAsync(QualityCommand(approved,"REVISE") with{Body=approved.Version.Body with{Name="Next working revision"}});
        var next=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal("2",next.Version.Reference.Version);Assert.Equal("DRAFT",next.Control.State);
        Assert.Equal("AVAILABLE",(await owner.SourceAsync(approved.Version.Reference)).Availability);
        Assert.Equal("UNAVAILABLE",(await owner.SourceAsync(next.Version.Reference)).Availability);
        Assert.Equal(approved.Version.OriginalJson,(await owner.ReadAsync(f.Create.SpecificationId,"1")).Version.OriginalJson);
        await owner.CommandAsync(QualityCommand(next,"WITHDRAW"));Assert.Contains("QUALITY_SPEC_WITHDRAWN",(await owner.SourceAsync(approved.Version.Reference)).Missing);
        Assert.Equal(signed,(await one.OperationAsync(command.OperationId)).Result);
        foreach(var sql in new[]{"UPDATE cp6_quality_specification_v1.versions SET body=body","DELETE FROM cp6_quality_specification_v1.records","TRUNCATE cp6_quality_specification_v1.heads CASCADE"})
            await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql));
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_unknown_applicability_cannot_submit_and_empty_complete_list_requires_independent_approval()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);
        var unknown=f.Create with{Body=f.Create.Body! with{Requirements=[f.Create.Body.Requirements[0] with{Applicability="UNKNOWN"}]}};
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>QualityReady(owner,unknown));Assert.Equal("QUALITY_SPEC_SOURCE_INCOMPLETE",error.Message);
        var view=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal("DRAFT",view.Control.State);Assert.Null(view.Packet);
        var other=f with{Create=f.Create with{OperationId=Guid.NewGuid(),SpecificationId=Guid.NewGuid(),Body=f.Create.Body with{Requirements=[],CompletenessStatement="Explicit reviewed empty set for the declared purpose; controlled source provides basis"}}};
        var approved=await QualityApprove(h,other);Assert.Empty((await owner.SourceAsync(approved.Version.Reference)).Requirements);Assert.NotNull(approved.CurrentApproval);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_engineering_adopts_approved_source_before_freeze_and_observes_withdrawal()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var approved=await QualityApprove(h,f);var owner=Specifications(h,f.Files);
        var revisions=new EngineeringRevisionOwnerV1(h.Db,QualityAuthority(h),Documents(h,f.Files),owner);
        var parent=await revisions.ReadAsync(f.Document.Body.RevisionOrigin.Id,null);Assert.Equal("DRAFT",parent.State);
        var component=new EngineeringComponentDraftV1(Guid.NewGuid(),"QUALITY_SPEC","Independent requirements",0,"APPLIES","","",approved.Version.Reference);
        await revisions.CommandAsync(PatchRevision(parent,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=component}));
        var bound=await revisions.ReadAsync(parent.Reference.Id,null);Assert.DoesNotContain(bound.Missing!,x=>x.StartsWith("QUALITY_SPEC:"));
        await owner.CommandAsync(QualityCommand(approved,"WITHDRAW"));
        bound=await revisions.ReadAsync(parent.Reference.Id,null);Assert.Contains(bound.Missing!,x=>x.EndsWith("QUALITY_SPEC_WITHDRAWN"));
        var command=PatchRevision(bound,new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=component});
        Assert.Equal("QUALITY_SPEC_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>revisions.CommandAsync(command))).Message);
        Assert.Equal("UNKNOWN",(await revisions.OperationAsync(command.OperationId,false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_omitted_original_source_or_null_policy_role_is_rejected_without_persistence()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);
        Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create with{Body=f.Create.Body! with{Requirements=[f.Create.Body.Requirements[0] with{StandardDocument=null!}]}}))).Status);
        await owner.CommandAsync(f.Create);var view=await owner.ReadAsync(f.Create.SpecificationId);
        var policy=new QualityReviewPolicyInputV1("ENABLED",[null!],DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(QualityCommand(view,"POLICY") with{Policy=policy}))).Status);
        Assert.Null((await owner.ReadAsync(f.Create.SpecificationId)).Policy);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_return_and_resubmit_keep_prior_vote_separate_and_freeze_review_content()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);var ready=await QualityReady(owner,f.Create);
        Assert.Equal("QUALITY_REVIEW_FROZEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(QualityCommand(ready,"REVISE") with{Body=ready.Version.Body})) ).Message);
        var first=Specifications(DecisionActor(h,f.First),f.Files);await first.CommandAsync(QualityCommand(await first.ReadAsync(f.Create.SpecificationId),"RETURN"));
        var returned=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal("RETURNED",returned.Control.State);Assert.False(Assert.Single(returned.Votes).Body.Approved);
        await owner.CommandAsync(QualityCommand(returned,"SUBMIT"));var resubmitted=await owner.ReadAsync(f.Create.SpecificationId);
        Assert.NotEqual(ready.Packet!.Reference,resubmitted.Packet!.Reference);Assert.Empty(resubmitted.Votes);Assert.Null(resubmitted.CurrentApproval);
        foreach(var actor in new[]{f.First,f.Second})
        {var reviewer=Specifications(DecisionActor(h,actor),f.Files);await reviewer.CommandAsync(QualityCommand(await reviewer.ReadAsync(f.Create.SpecificationId),"APPROVE"));}
        var approved=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal("APPROVED",approved.Control.State);Assert.Equal(2,approved.CurrentApproval!.Body.Votes.Length);
        Assert.Equal(3,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_quality_specification_v1.records WHERE tenant_id={h.Tenant} AND kind='VOTE'").SingleAsync());
    }
}
