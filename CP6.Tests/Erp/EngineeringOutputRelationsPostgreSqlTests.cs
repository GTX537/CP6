using System.Net;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 OutputAuthority(Harness h)
    {
        var catalog=new AuthorizationCatalogStoreV1();
        foreach(var key in new[]{"plm-engineering-output-relations","plm-engineering-manufacturing","plm-configuration-rule","quality-specification","plm-controlled-document","file-content","plm-engineering-revision","plm-engineering-product","space-site","uom-profile","plm-engineering-bom","plm-engineering-routing","plm-tool-specification","erp-plate-mold","mes-machine","mes-work-center","product"})
        {
            catalog.RegisterScope(key,key,key=="space-site"?[5]:[1,2,3,4,5],5);
            catalog.RegisterFields(key,key=="quality-specification"?[new("Metadata","Metadata"),new("Requirements","Requirements")]:key=="file-content"?[new("Content","Content"),new("FileName","Name")]:key=="plm-controlled-document"?[new("Content","Content"),new("Metadata","Metadata")]:[]);
        }
        catalog.Seal();return new(h.Db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture);
    }
    private static EngineeringOutputRelationsOwnerV1 Outputs(Harness h,ContentStore files)=>new(h.Db,OutputAuthority(h),ScopeFence(h),new("DEMO"),Documents(h,files),Specifications(h,files));
    private sealed record OutputFixture(QualityFixture Quality,EngineeringOutputRelationsCommandV1 Create,EngineeringManufacturingSnapshotV1 Manufacturing,ConfigurationRuleSnapshotV1 Policy);
    private async Task<OutputFixture> OutputSetup(Harness h)
    {
        var f=await QualitySetup(h);var mainSpec=await QualityApprove(h,f);var inputProduct=await MakeEngineering(h);
        var inputSpec=await QualityApprove(h,f with{Create=f.Create with{OperationId=Guid.NewGuid(),SpecificationId=Guid.NewGuid(),Body=f.Create.Body! with{Product=inputProduct.Reference,Name="Independent input material specification"}}});
        foreach(var key in new[]{"plm-engineering-output-relations","plm-engineering-manufacturing","plm-engineering-bom","plm-engineering-routing"})
        {
            foreach(var action in key=="plm-engineering-output-relations"?new[]{"query","add","edit","review-policy","qualify","withdraw"}:new[]{"query","add","edit"})await EngineeringAction(h,key,action);
            if(!await h.Db.Sys_RoleDataScopes.AnyAsync(x=>x.RoleId==72 && x.ResourceKey==key))
                h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=key,ScopeType=5});
        }
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        if(!await h.Db.Sys_RoleDataScopes.AnyAsync(x=>x.RoleId==72 && x.ResourceKey=="plm-configuration-rule"))await GrantConfiguration(h);
        if(!await h.Db.Sys_RoleDataScopes.AnyAsync(x=>x.RoleId==72 && x.ResourceKey=="uom-profile"))await GrantUomProfile(h);
        var unit=await MakeUnit(Units(h),BaseUnit(scale:30));var rules=ConfigurationRules(h);var product=await Engineering(h).ReadAsync(f.Document.Body.ProductRef.Id,null);
        var create=await NewConfiguration(rules,product.Reference);create=create with{Body=create.Body! with{Scope=EngineeringSourceCheckValuesV1.PolicyScope,
            EngineeringChecks=new("engineering-check-policy/1",product.Product.Category,"INTERNAL","APPLIES","Internal exact manufacturing",
                EngineeringSourceCheckValuesV1.Kinds.Select(k=>new EngineeringCompositionPolicyV1(k,"APPLIES",1,"Required native composition")).ToArray())}};
        await rules.CommandAsync(create);var policy=await rules.ReadAsync(create.RuleId,null);policy=await Step(rules,await Step(rules,policy,"SUBMIT"),"APPROVE");
        var revisions=new EngineeringRevisionOwnerV1(h.Db,OutputAuthority(h),Documents(h,f.Files),Specifications(h,f.Files));
        var parent=await revisions.ReadAsync(f.Document.Body.RevisionOrigin.Id,null);
        await revisions.CommandAsync(PatchRevision(parent,new EngineeringDraftPatchV1{Op="SET_CONFIGURATION",Configuration=new(ValidOptions(),"APPLIES","Approved exact configuration",policy.Reference)},
            new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=new(Guid.NewGuid(),"DOCUMENT","standard",0,"APPLIES","","",f.Document.Reference)},
            new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=new(Guid.NewGuid(),"QUALITY_SPEC","quality",1,"APPLIES","","",mainSpec.Version.Reference)}));
        parent=await revisions.ReadAsync(parent.Reference.Id,null);var site=f.Create.Body!.Site;
        var route=RoutingCreate(parent);var first=route.Body.Operations[0];route=route with{Body=route.Body with{SiteRef=site,Operations=[first,first with{OccurrenceId=Guid.NewGuid(),SemanticOrder=20,DependsOn=[first.OccurrenceId]}]}};
        await Routings(h).CommandAsync(route);var routing=await Routings(h).ReadAsync(route.RoutingId);
        var eb=BomCreate(parent);eb=eb with{Body=eb.Body with{SiteRef=site,Occurrences=[eb.Body.Occurrences[0] with{ItemRef=new(inputProduct.Reference.Owner,inputProduct.Reference.Id,inputProduct.Reference.Version,inputProduct.Reference.Digest),UnitRef=unit.Reference}]}};
        await Boms(h).CommandAsync(eb);var ebom=await Boms(h).ReadAsync(eb.BomId);
        var mb=BomCreate(parent);var row=eb.Body.Occurrences[0];mb=mb with{Body=mb.Body with{Kind="MBOM",SiteRef=site,RoutingRef=routing.Reference,Occurrences=[row,row with{OccurrenceId=Guid.NewGuid(),Path="right",SemanticOrder=20}]}};
        await Boms(h).CommandAsync(mb);var mbom=await Boms(h).ReadAsync(mb.BomId);var ids=mbom.Body.Occurrences.Select(x=>x.OccurrenceId).ToArray();
        var outputs=routing.Body.Operations.Select(op=>new ManufacturingOutputV1(Guid.NewGuid(),"PRIMARY","Repeated product",product.Reference,op.OccurrenceId,ids,[],parent.Draft.Configuration!.Reference,
            "FIXED_RATIO","1.00000000000000001",null,null,unit.Reference,"3",unit.Reference,"Per input batch","Own specification","Approved relationship document","","No production disposition")).ToArray();
        var manufacturing=new EngineeringManufacturingCommandV1(Guid.NewGuid(),Guid.NewGuid(),"CREATE",null,null,
            new("Exact manufacturing",parent.Reference,product.Reference,parent.Draft.Configuration!.Reference,ebom.Reference,mbom.Reference,routing.Reference,site,
                [new(Guid.NewGuid(),"MAP",ebom.Body.Occurrences.Select(x=>x.OccurrenceId).ToArray(),ids,routing.Body.Operations.Select(x=>x.OccurrenceId).ToArray(),"Split exact input positions","Exact rational quantity basis")],outputs),"Author native complete relationship");
        await Manufacturing(h).CommandAsync(manufacturing);var native=await Manufacturing(h).ReadAsync(manufacturing.ManufacturingId);
        var body=new EngineeringOutputRelationsBodyV1("Complete outputs",native.Reference,ids.Select(x=>new OutputInputProofV1(x,inputSpec.Version.Reference)).ToArray(),
            outputs.Select(x=>new OutputOccurrenceProofV1(x.OutputOccurrenceId,mainSpec.Version.Reference,f.Document.Reference,null)).ToArray());
        return new(f,new(Guid.NewGuid(),Guid.NewGuid(),"CREATE",null,"Bind real specifications and evidence",body),native,policy);
    }
    private static EngineeringOutputRelationsCommandV1 OutputCommand(OutputRelationsViewV1 view,string action)=>new(Guid.NewGuid(),view.Version.Reference.Id,action,view.Etag,"Confirm exact complete relations and quantity basis");
    private static async Task<OutputRelationsViewV1> OutputQualified(Harness h,OutputFixture f)
    {
        var owner=Outputs(h,f.Quality.Files);await owner.CommandAsync(f.Create);var view=await owner.ReadAsync(f.Create.RelationId);
        await owner.CommandAsync(OutputCommand(view,"POLICY") with{Policy=new(74,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),"Independent engineering professional")});
        view=await owner.ReadAsync(f.Create.RelationId);Assert.Equal(new[]{"OUTPUT_RELATIONS_QUALIFICATION_REQUIRED"},view.Missing);
        var reviewer=Outputs(DecisionActor(h,f.Quality.First),f.Quality.Files);await reviewer.CommandAsync(OutputCommand(await reviewer.ReadAsync(f.Create.RelationId),"QUALIFY"));
        return await owner.ReadAsync(f.Create.RelationId);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_complete_native_graph_repeated_product_exact_wire_and_professional_currentness()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var view=await OutputQualified(h,f);
        Assert.Empty(view.Missing);Assert.NotEmpty(view.DownstreamMissing);Assert.Equal("QUALIFIED",view.Control.State);
        var wire=view.Version.Original.Wire;Assert.Equal(2,wire.Inputs.Length);Assert.Equal(2,wire.Operations.Length);Assert.Equal(2,wire.Outputs.Length);
        Assert.Single(wire.Outputs.Select(x=>x.ProductIdentity).Distinct());Assert.Equal(2,wire.Outputs.Select(x=>x.GeneratingOperationOccurrenceId).Distinct().Count());
        Assert.Contains("1.00000000000000001",view.Version.Original.ManufacturingOriginal);
        Assert.Equal(view.Version.Original.WireOriginal,Encoding.UTF8.GetString(EngineeringOutputRelationsValuesV1.Bytes(wire)));
        if(System.Environment.GetEnvironmentVariable("CP6_OUTPUT_RELATIONS_WIRE_EVIDENCE") is {Length:>0} evidence)
            await System.IO.File.WriteAllTextAsync(evidence,view.Version.Original.WireOriginal);
        Assert.Equal(f.Create.OperationId,(await owner.OperationAsync(f.Create.OperationId)).Result!.OperationId);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create with{Reason="Different"}))).Message);
        await OutputAuthority(h).ExecuteAsync("plm-engineering-output-relations","query",false,(a,t)=>owner.HoldExact(a,view.Version.Reference,view.ControlReference,t));
        await owner.CommandAsync(OutputCommand(view,"WITHDRAW"));var after=await owner.ReadAsync(f.Create.RelationId);
        Assert.Contains("OUTPUT_RELATIONS_WITHDRAWN",after.Missing);Assert.Equal(view.Version.OriginalJson,after.Version.OriginalJson);
        Assert.Equal("OUTPUT_RELATIONS_NOT_CURRENT_QUALIFIED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OutputAuthority(h).ExecuteAsync("plm-engineering-output-relations","query",false,(a,t)=>owner.HoldExact(a,view.Version.Reference,view.ControlReference,t)))).Message);
        await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync("UPDATE cp6_output_relations_v1.versions SET body=body"));
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_independent_role_complete_inputs_spec_product_and_no_input_proof_boundaries()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var b=f.Create.Body!;
        Assert.Equal("OUTPUT_RELATIONS_COMPLETE_OCCURRENCES_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create with{Body=b with{Inputs=[]}}))).Message);
        Assert.Equal("OUTPUT_RELATIONS_NO_INPUT_PROOF_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create with{Body=b with{Outputs=b.Outputs.Select(x=>x with{NoInputDocument=f.Quality.Document.Reference}).ToArray()}}))).Message);
        await owner.CommandAsync(f.Create);var view=await owner.ReadAsync(f.Create.RelationId);
        await owner.CommandAsync(OutputCommand(view,"POLICY") with{Policy=new(74,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),"Actual professional role")});
        view=await owner.ReadAsync(f.Create.RelationId);Assert.Equal("OUTPUT_RELATIONS_AUTHOR_SEPARATION",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(OutputCommand(view,"QUALIFY")))).Message);
        await owner.CommandAsync(OutputCommand(view,"REVISE") with{Body=b with{Inputs=b.Inputs.Select(x=>x with{Specification=b.Outputs[0].Specification}).ToArray()}});
        var reviewer=Outputs(DecisionActor(h,f.Quality.First),f.Quality.Files);var invalid=await reviewer.ReadAsync(f.Create.RelationId);
        Assert.Contains(invalid.Missing,x=>x.EndsWith("QUALITY_SPEC_PRODUCT_MISMATCH"));
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviewer.CommandAsync(OutputCommand(invalid,"QUALIFY")))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_source_check_carries_separate_originals_and_keeps_physical_gates()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var relation=await OutputQualified(h,f);
        var revisions=new EngineeringRevisionOwnerV1(h.Db,OutputAuthority(h),Documents(h,f.Quality.Files),Specifications(h,f.Quality.Files));
        var revision=await revisions.ReadAsync(f.Manufacturing.Body.RevisionOrigin.Id,null);
        await revisions.CommandAsync(PatchRevision(revision,new[]{("EBOM",f.Manufacturing.Body.EbomRef),("MBOM",f.Manufacturing.Body.MbomRef),("ROUTING",f.Manufacturing.Body.RoutingRef)}
            .Select((x,i)=>new EngineeringDraftPatchV1{Op="UPSERT_COMPONENT",Component=new(Guid.NewGuid(),x.Item1,"native/"+x.Item1,i+10,"APPLIES","","",x.Item2)}).ToArray()));
        revision=await revisions.ReadAsync(revision.Reference.Id,null);var checks=new EngineeringSourceCheckService(h.Db,OutputAuthority(h),Documents(h,f.Quality.Files),Specifications(h,f.Quality.Files),owner);
        var command=new EngineeringSourceCheckCommandV1(Guid.NewGuid(),revision.Reference,revision.CandidateRef,revision.Etag,f.Policy.Reference,"Full native relations",relation.Version.Reference);
        var saved=await checks.CheckAsync(command);var check=await checks.ReadAsync(saved.CheckRunId);
        var pin=Assert.Single(check.Original.Sources,x=>x.Path=="outputRelations");using var captured=JsonDocument.Parse(pin.OriginalJson);
        Assert.Equal(relation.Version.OriginalJson,captured.RootElement.GetProperty("versionOriginalJson").GetString());
        Assert.Equal(relation.Qualification!.Reference.Id.ToString(),captured.RootElement.GetProperty("qualification").GetProperty("reference").GetProperty("id").GetString());
        Assert.Equal(relation.Policy!.Reference.Id.ToString(),captured.RootElement.GetProperty("policy").GetProperty("reference").GetProperty("id").GetString());
        Assert.Contains(check.Original.Items,x=>x.Code=="EXACT_OUTPUT_RELATIONS_QUALIFICATION");Assert.NotEqual("PASS",check.Original.State);
        Assert.Contains(check.Original.Items,x=>x.Code.Contains("RESOURCE_REQUIRED"));
        Assert.Equal(EngineeringSourceCheckValuesV1.Manifest(revision).Digest,check.Original.Manifest!.Digest);
        await owner.CommandAsync(OutputCommand(await owner.ReadAsync(f.Create.RelationId),"WITHDRAW"));
        var stale=await checks.ReadAsync(saved.CheckRunId);Assert.Equal("STALE",stale.Freshness);Assert.Equal(check.OriginalJson,stale.OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_final_source_change_rolls_back_and_revocation_denies_originals()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var view=await OutputQualified(h,f);
        var site=f.Manufacturing.Body.SiteRef!.Id;var original=await h.Db.Space_Sites.Where(x=>x.Id==site).Select(x=>x.SiteName).SingleAsync();
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>OutputAuthority(h).ExecuteAsync("plm-engineering-output-relations","query",true,async(a,t)=>{
            await owner.HoldExact(a,view.Version.Reference,view.ControlReference,t);await h.Db.Space_Sites.Where(x=>x.Id==site).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.SiteName,"Same UoW change"),t);return true;
        }));
        Assert.Equal(original,await h.Db.Space_Sites.Where(x=>x.Id==site).Select(x=>x.SiteName).SingleAsync());
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.Quality.First && x.RoleId==74).ExecuteDeleteAsync();
        Assert.Contains((await owner.ReadAsync(f.Create.RelationId)).Missing,x=>x.Contains("AUTHORITY") || x=="ACTION_FORBIDDEN");
        var menu=await h.Db.Sys_Menus.Where(x=>x.MenuKey=="quality-specification").Select(x=>x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu && x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(f.Create.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_unknown_close_CAS_history_and_guard_fail_closed()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(f.Create.OperationId)).State);Assert.Equal("CLOSED",(await owner.OperationAsync(f.Create.OperationId,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create))).Message);
        var create=f.Create with{OperationId=Guid.NewGuid()};await owner.CommandAsync(create);var view=await owner.ReadAsync(create.RelationId);
        await owner.CommandAsync(OutputCommand(view,"REVISE") with{Body=create.Body! with{Name="Second complete version"}});
        Assert.Equal(view.Version.OriginalJson,(await owner.ReadAsync(create.RelationId,"1")).Version.OriginalJson);Assert.NotEmpty(await owner.DiffAsync(create.RelationId,"1","2"));
        Assert.Equal(412,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(OutputCommand(view,"REVISE") with{Body=create.Body}))).Status);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_output_relations_v1.versions DISABLE TRIGGER output_relations_validate");
        try{Assert.Equal("OUTPUT_RELATIONS_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(create.RelationId))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_output_relations_v1.versions ENABLE TRIGGER output_relations_validate");}
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_current_configuration_and_current_native_specification_withdrawal_invalidate_use()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var view=await OutputQualified(h,f);
        var spec=Specifications(h,f.Quality.Files);var current=await spec.ReadAsync(f.Create.Body!.Outputs[0].Specification.Id);
        await spec.CommandAsync(QualityCommand(current,"WITHDRAW"));
        Assert.Contains((await owner.ReadAsync(f.Create.RelationId)).Missing,x=>x.Contains("QUALITY_SPEC_WITHDRAWN"));
        var revisions=new EngineeringRevisionOwnerV1(h.Db,OutputAuthority(h),Documents(h,f.Quality.Files),Specifications(h,f.Quality.Files));
        var revision=await revisions.ReadAsync(f.Manufacturing.Body.RevisionOrigin.Id,null);
        // The withdrawn specification is still in its accurate immutable history. Remove the
        // live component before an independent configuration edit; no fake source approval.
        var qualityComponent=revision.Draft.Components.Single(x=>x.Kind=="QUALITY_SPEC");
        await revisions.CommandAsync(PatchRevision(revision,new EngineeringDraftPatchV1{Op="REMOVE_COMPONENT",OccurrenceId=qualityComponent.OccurrenceId},
            new EngineeringDraftPatchV1{Op="SET_CONFIGURATION",Configuration=revision.Draft.Configuration!.Input with{Reason="Changed exact configuration rationale"}}));
        var stale=await owner.ReadAsync(f.Create.RelationId);Assert.Contains("OUTPUT_RELATIONS_CONFIGURATION_SOURCE_CHANGED",stale.Missing);
        Assert.Equal(view.Version.OriginalJson,stale.Version.OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_two_connection_withdrawal_CAS_and_tenant_boundary()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var view=await OutputQualified(h,f);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=Outputs(h with{Db=peer},f.Quality.Files);
        async Task<bool> Withdraw(EngineeringOutputRelationsOwnerV1 service)
        {try{await service.CommandAsync(OutputCommand(view,"WITHDRAW"));return true;}catch(OrderPriceCorrectionException e){Assert.Equal(412,e.Status);return false;}}
        Assert.Single(await Task.WhenAll(Withdraw(owner),Withdraw(other)),x=>x);
        Assert.Equal("WITHDRAWN",(await owner.ReadAsync(f.Create.RelationId)).Control.State);
        await using var tenant=await Seed();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Outputs(tenant,f.Quality.Files).ReadAsync(f.Create.RelationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_legitimate_native_no_input_basis_and_intermediate_flow_remain_legal()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var boms=Boms(h);
        var e=await boms.ReadAsync(f.Manufacturing.Body.EbomRef.Id);await boms.CommandAsync(BomRevise(e) with{Body=e.Body with{Occurrences=[]}});e=await boms.ReadAsync(e.Reference.Id);
        var m=await boms.ReadAsync(f.Manufacturing.Body.MbomRef.Id);await boms.CommandAsync(BomRevise(m) with{Body=m.Body with{Occurrences=[]}});m=await boms.ReadAsync(m.Reference.Id);
        var first=f.Manufacturing.Body.Outputs[0] with{Role="INTERMEDIATE",InputOccurrenceIds=[],NoInputBasis="",RelationEvidence=""};
        var second=f.Manufacturing.Body.Outputs[1] with{InputOccurrenceIds=[],FromOutputOccurrenceIds=[first.OutputOccurrenceId]};
        await Manufacturing(h).CommandAsync(ManufacturingRevise(f.Manufacturing) with{Body=f.Manufacturing.Body with{EbomRef=e.Reference,MbomRef=m.Reference,Mappings=[],Outputs=[first,second]}});
        var native=await Manufacturing(h).ReadAsync(f.Manufacturing.Reference.Id);
        var body=f.Create.Body! with{Manufacturing=native.Reference,Inputs=[],Outputs=f.Create.Body!.Outputs.Select(x=>x with{NoInputDocument=x.OccurrenceId==first.OutputOccurrenceId?f.Quality.Document.Reference:null}).ToArray()};
        var fixture=f with{Create=f.Create with{Body=body},Manufacturing=native};var view=await OutputQualified(h,fixture);var wire=view.Version.Original.Wire;
        Assert.Empty(view.Missing);Assert.Single(wire.Inputs);var root=Assert.Single(wire.Outputs,x=>x.OutputOccurrenceId==EngineeringOutputRelationsValuesV1.OutputId(first.OutputOccurrenceId));
        Assert.Empty(root.InputOccurrenceIds);Assert.NotNull(root.GenerationWithoutInputBasis);
        var downstream=Assert.Single(wire.Outputs,x=>x.OutputOccurrenceId==EngineeringOutputRelationsValuesV1.OutputId(second.OutputOccurrenceId));Assert.Equal(new[]{root.OutputOccurrenceId},downstream.InputOccurrenceIds);
    }
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task OutputRelations_department_scope_uses_actual_parent_binding_and_protects_originals(int type)
    {
        await using var h=await Seed();var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var admin=await MappingAdmin(h);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.DeptId,type==3?admin.Root:admin.Child));
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72 && x.ResourceKey=="plm-engineering-output-relations").ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ScopeType,type).SetProperty(v=>v.CustomDeptIds,admin.Child.ToString()));
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create))).Status);
        Assert.Empty(await owner.ListAsync());Assert.Equal("UNKNOWN",(await owner.OperationAsync(f.Create.OperationId)).State);
        await BindStandalone(h,admin,"plm-engineering-revision",f.Manufacturing.Body.RevisionOrigin.Id);
        await owner.CommandAsync(f.Create);Assert.Single(await owner.ListAsync());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={f.Manufacturing.Body.RevisionOrigin.Id}");
        Assert.Empty(await owner.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(f.Create.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_write_final_authority_failure_rolls_back_original_control_and_journal()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);var f=await OutputSetup(h);var owner=Outputs(h,f.Quality.Files);var reached=false;
        hook.Before=async command=>{
            if(!command.CommandText.Contains("WITH field_subject",StringComparison.Ordinal))return;
            await using var probe=command.Connection!.CreateCommand();probe.Transaction=command.Transaction;
            probe.CommandText=$"SELECT count(*) FROM cp6_output_relations_v1.versions WHERE tenant_id='{h.Tenant}' AND relation_id='{f.Create.RelationId}'";
            if(Convert.ToInt32(await probe.ExecuteScalarAsync())>0){reached=true;throw new IOException("Final output relation authority unavailable");}
        };
        try{Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(f.Create))).Message);}
        finally{hook.Before=null;}
        Assert.True(reached);Assert.Empty(await owner.ListAsync());Assert.Equal("UNKNOWN",(await owner.OperationAsync(f.Create.OperationId)).State);
        var controls=await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_output_relations_v1.controls WHERE tenant_id={h.Tenant}").SingleAsync();Assert.Equal(0,controls);
    }
    [CurrentPermissionPostgresFact]
    public async Task OutputRelations_Program_HTTP_CSRF_exact_original_recovery_and_professional_actions()
    {
        await using var h=await Seed();var f=await OutputSetup(h);var settings=await DocumentHostSettings(f.Quality.Files);settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var author=await Login(host,h);const string url="/api/plm-engineering-output-relations/owner-v1";
        async Task<OutputRelationsViewV1> Read(Dictionary<string,string> cookies){using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/"+f.Create.RelationId));return await DocumentHttpData<OutputRelationsViewV1>(response);}
        async Task<HttpResponseMessage> Post(Dictionary<string,string> cookies,object command,bool csrf=true){using var request=OperationsRequest(cookies,HttpMethod.Post,url+"/commands",command);if(!csrf)request.Headers.Remove("X-CSRF-Token");return await host.Send(request);}
        using(var denied=await Post(author,f.Create,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var response=await Post(author,f.Create)){await DocumentHttpData<OutputRelationsResultV1>(response);Assert.True(response.Headers.CacheControl?.NoStore);}
        using(var recovered=await host.Send(OperationsRequest(author,HttpMethod.Get,url+"/operations/"+f.Create.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<OutputRelationsOperationResultV1>(recovered)).State);
        var first=await Read(author);using(var policy=await Post(author,OutputCommand(first,"POLICY") with{Policy=new(74,DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1),"Independent reviewer")}))await DocumentHttpData<OutputRelationsResultV1>(policy);
        var reviewer=await DocumentReviewerLogin(host,h,f.Quality.First);
        using(var response=await Post(reviewer,OutputCommand(await Read(reviewer),"QUALIFY")))Assert.Equal("QUALIFIED",(await DocumentHttpData<OutputRelationsResultV1>(response)).State);
        using(var stale=await Post(author,OutputCommand(first,"WITHDRAW")))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        using(var response=await Post(author,OutputCommand(await Read(author),"WITHDRAW")))Assert.Equal("WITHDRAWN",(await DocumentHttpData<OutputRelationsResultV1>(response)).State);
    }
}
