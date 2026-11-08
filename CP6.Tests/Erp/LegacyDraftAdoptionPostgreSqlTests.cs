using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<LegacyFormSourceBindingV1> LegacyAdoptionMapping(Harness h,FormAuthoringOwnerV2 form,Guid legacy,Guid target)
    {
        await LegacyAdoptionAuthority(h);var preview=await form.LegacySourcePreviewAsync(legacy,"personal",target);
        return (await form.ConfigureLegacySourceAsync(new(Guid.NewGuid(),legacy,preview.SourceRevision,"personal",target,preview.Current?.Version.ToString("D")??"absent",true,"explicit simulated current source mapping"))).Result!;
    }
    private static async Task<(Guid Form,Guid Version,Guid Draft)> LegacyAdoptionSource(Harness h,string schema=FormAuthoringSchema,string data="{\"a\":\"historical private content\"}")
    {
        var head=new Wf_FormDef{Id=Guid.NewGuid(),FormKey="legacy-"+Guid.NewGuid().ToString("N"),FormName="recorded legacy",SchemaJson=schema,Version=3};h.Db.Wf_FormDefs.Add(head);await h.Db.SaveChangesAsync();
        var version=new Wf_FormDefVersion{Id=Guid.NewGuid(),FormDefId=head.Id,Version=3,Status=1,FormNameSnapshot="recorded snapshot",SchemaJson=schema,Creator="oa-p0-backfill"};h.Db.Wf_FormDefVersions.Add(version);await h.Db.SaveChangesAsync();
        var draft=new Wf_FormDraft{Id=Guid.NewGuid(),OwnerUserId=h.User,FormDefId=head.Id,FormDefVersionId=version.Id,Title="preserve original title",DataJson=data};h.Db.Wf_FormDrafts.Add(draft);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return(head.Id,version.Id,draft.Id);
    }
    private static async Task LegacyAdoptionAuthority(Harness h)
    {
        await EngineeringAction(h,"oa-designer","form-legacy-source");
        var scope=h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72&&x.ResourceKey==FormAuthoringOwnerV2.Resource);
        if(await scope.AnyAsync())await scope.ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,5));
        else{h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey=FormAuthoringOwnerV2.Resource,ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();}
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_actual_HTTP_previews_exact_source_without_promoting_recorded_legacy_publication()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);await LegacyAdoptionAuthority(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        using var response=await host.Send(BoundRequest(cookies,$"/api/oa/v2/legacy-form-sources/preview?legacyVersionId={source.Version}&targetKey=personal&targetVersionId={context.FormVersionId}"));
        Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
        var preview=await PersonalHttpResult<System.Text.Json.JsonElement>(response);Assert.Equal("UNVERIFIED_LEGACY",preview.GetProperty("sourceEvidenceState").GetString());
        Assert.True(preview.GetProperty("compatible").GetBoolean());Assert.DoesNotContain("historical private content",preview.GetRawText());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_exact_new_draft_keeps_hidden_values_numbers_old_row_and_original_after_write_revocation()
    {
        const string schema="""{"fields":[{"name":"amount","type":"number"},{"name":"private","type":"input"},{"name":"later","type":"input","required":true}]}""";
        const string data="""{ "amount":9007199254740993.0001,"private":"do-not-disclose" }""";
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h,schema,data);
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(source.Draft))).Status);
        var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["private"]="hidden"})],policy.RetentionPolicyRef,180,true,"current field authority"));
        await h.Db.Database.OpenConnectionAsync();await h.Db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'America/New_York'");
        var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);Assert.True(preview.CanAdopt);Assert.DoesNotContain("do-not-disclose",preview.DataJson);Assert.DoesNotContain("private",preview.SchemaJson);
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"explicit simulated forward copy"),"legacy-copy",preview.SourceVersion);
        var copied=PersonalResult<PersonalFormDraftV2>(await owner.AdoptLegacyDraftAsync(source.Draft,raw));Assert.NotEqual(source.Draft,copied.DraftId);Assert.Equal(context.FormVersionId,copied.FormVersionId);Assert.Contains("9007199254740993.0001",copied.DataJson);Assert.DoesNotContain("do-not-disclose",copied.DataJson);
        Assert.Equal(data,await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={copied.DraftId}").SingleAsync());
        Assert.Equal(data,await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).Select(x=>x.DataJson).SingleAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="edit"&&x.MenuId==h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-form-catalog").Select(m=>m.MenuId).Single()).ExecuteDeleteAsync();
        var original=PersonalResult<PersonalFormDraftV2>(await owner.OriginalAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"legacy-copy"));Assert.Equal(copied.DraftId,original.DraftId);Assert.DoesNotContain("do-not-disclose",original.DataJson);
        Assert.Equal(copied.DraftId,PersonalResult<PersonalFormDraftV2>(await owner.AdoptLegacyDraftAsync(source.Draft,raw)).DraftId);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.legacy_adoptions WHERE source_draft_id={source.Draft}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_source_and_schema_ABA_current_mapping_and_close_are_enforced_without_rewriting_legacy()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);
        var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.DataJson,"{\"a\":\"changed\"}"));
        await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.DataJson,"{\"a\":\"historical private content\"}"));
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"stale copy"),"stale",preview.SourceVersion);
        Assert.Equal("LEGACY_SOURCE_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,raw))).Message);
        await h.Db.Wf_FormDefVersions.Where(x=>x.Id==source.Version).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Creator,"edited"));
        await h.Db.Wf_FormDefVersions.Where(x=>x.Id==source.Version).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.Creator,"oa-p0-backfill"));
        Assert.Equal("LEGACY_SOURCE_MAPPING_STALE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(source.Draft))).Message);
        mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        await form.ConfigureLegacySourceAsync(new(Guid.NewGuid(),source.Version,mapping.SourceRevision,"personal",mapping.FormVersionId,mapping.Version.ToString("D"),false,"revoke current mapping"));
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(source.Draft))).Status);
        await owner.CloseAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"closed-copy");
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"closed late copy"),"closed-copy",preview.SourceVersion)))).Message);
        Assert.Empty((await owner.DraftsAsync()).Items);Assert.Single(await h.Db.Wf_FormDrafts.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_mapping_current_scope_schema_and_original_operation_are_separate_authorities()
    {
        await using var h=await Seed();var(_,form,_)=await PreparePersonal(h);var native=await PersonalForm(h).ContextAsync("personal");
        var source=await LegacyAdoptionSource(h,"""{"fields":[{"name":"different","type":"number"}]}""");await LegacyAdoptionAuthority(h);
        var preview=await form.LegacySourcePreviewAsync(source.Version,"personal",native.FormVersionId);Assert.False(preview.Compatible);
        var command=new LegacyFormSourceCommandV1(Guid.NewGuid(),source.Version,preview.SourceRevision,"personal",native.FormVersionId,"absent",true,"explicit mapping");
        Assert.Equal(422,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.ConfigureLegacySourceAsync(command))).Status);
        await h.Db.Wf_FormDefVersions.Where(x=>x.Id==source.Version).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SchemaJson,"""{ "layout":{"amount":12345678901234567890.0001},"rules":[],"fields":[{"label":"原文","type":"input","name":"a"}] }"""));
        preview=await form.LegacySourcePreviewAsync(source.Version,"personal",native.FormVersionId);Assert.True(preview.Compatible);
        command=command with{ExpectedSourceRevision=preview.SourceRevision};var saved=await form.ConfigureLegacySourceAsync(command);
        await h.Db.Sys_RoleActions.Where(x=>x.RoleId==72&&x.ActionCode=="form-legacy-source").ExecuteDeleteAsync();
        Assert.Equal(saved,await form.ConfigureLegacySourceAsync(command));Assert.Equal(saved,await form.LegacySourceOperationAsync(command.OperationId));
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.ConfigureLegacySourceAsync(command with{Reason="different original input"}))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.ConfigureLegacySourceAsync(command with{OperationId=Guid.NewGuid(),ExpectedVersion=saved.Result!.Version.ToString("D")}))).Status);
        var closed=await form.LegacySourceOperationAsync(Guid.NewGuid(),true);Assert.Equal("CLOSED",closed!.State);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72&&x.ResourceKey==FormAuthoringOwnerV2.Resource).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.LegacySourcePreviewAsync(source.Version,"personal",native.FormVersionId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_final_session_failure_rolls_back_and_real_commit_unknown_recovers_once()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);
        var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"explicit copy"),"lost-copy",preview.SourceVersion);var calls=0;
        var failing=PersonalForm(h,_=>++calls==1?Task.FromResult(h.User):throw new InvalidOperationException("simulated final logout"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>failing.AdoptLegacyDraftAsync(source.Draft,raw));Assert.Empty((await owner.DraftsAsync()).Items);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"lost-copy")).Outcome);
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,raw))).Message);
        await using var fresh=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var recovered=PersonalForm(h with{Db=fresh});
        var original=await recovered.OriginalAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"lost-copy");Assert.Equal("SUCCEEDED",original.Outcome);
        Assert.Equal(original.ResourceId,(await recovered.AdoptLegacyDraftAsync(source.Draft,raw)).ResourceId);
        Assert.Equal(1,await fresh.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_personal_form_v2.legacy_adoptions WHERE source_draft_id={source.Draft}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_actual_HTTP_binds_then_copies_and_rejects_foreign_owner_and_extra_payload()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);await LegacyAdoptionAuthority(h);
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});var cookies=await Login(host,h);
        using var p=await host.Send(BoundRequest(cookies,$"/api/oa/v2/legacy-form-sources/preview?legacyVersionId={source.Version}&targetKey=personal&targetVersionId={context.FormVersionId}"));
        var preview=await PersonalHttpResult<LegacyFormSourcePreviewV1>(p);
        using var binding=await host.Send(BoundRequest(cookies,"/api/oa/v2/legacy-form-sources",new LegacyFormSourceCommandV1(Guid.NewGuid(),source.Version,preview.SourceRevision,"personal",context.FormVersionId,"absent",true,"actual authenticated source mapping")));
        var saved=await PersonalHttpResult<LegacyFormSourceOperationV1>(binding);Assert.Equal("UNVERIFIED_LEGACY",saved.Result!.SourceEvidenceState);
        using var read=await host.Send(BoundRequest(cookies,$"/api/oa/v2/legacy-drafts/{source.Draft}/preview"));var content=await PersonalHttpResult<LegacyDraftAdoptionPreviewV1>(read);
        var invalid=PersonalCommand(new{mappingVersion=saved.Result.Version,reason="must not accept client replacement",data=new{a="replacement"}},"extra",content.SourceVersion);
        using var invalidResponse=await host.Send(BoundRequest(cookies,$"/api/oa/v2/legacy-drafts/{source.Draft}/adopt",JsonSerializer.Deserialize<JsonElement>(invalid)));Assert.Equal(HttpStatusCode.BadRequest,invalidResponse.StatusCode);
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(saved.Result.Version,"actual explicit forward adoption"),"http-copy",content.SourceVersion);
        using var copied=await host.Send(BoundRequest(cookies,$"/api/oa/v2/legacy-drafts/{source.Draft}/adopt",JsonSerializer.Deserialize<JsonElement>(raw)));var result=await PersonalHttpResult<PersonalFormResultV2>(copied);Assert.NotNull(result.ResourceId);
        var other=new CP6.Entity.DomainModels.Sys.Sys_User{UserName="other-legacy-reader",Password=new BCryptPasswordHasher(4).Hash("Price1!Password"),RoleId=72};h.Db.Sys_Users.Add(other);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var otherCookies=await DocumentReviewerLogin(host,h,other.Id);using var denied=await host.Send(BoundRequest(otherCookies,$"/api/oa/v2/legacy-drafts/{source.Draft}/preview"));Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);Assert.DoesNotContain("historical private content",await denied.Content.ReadAsStringAsync());
        Assert.Single(await h.Db.Wf_FormDrafts.ToArrayAsync());Assert.Single((await owner.DraftsAsync()).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_native_legacy_writer_and_copy_share_the_same_final_gate_in_both_orders()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
        await using(var tx=await writer.Database.BeginTransactionAsync())
        {
            await writer.Wf_FormDrafts.Where(x=>x.Id==source.Draft).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Title,"native writer committed first"));
            var pending=owner.AdoptLegacyDraftAsync(source.Draft,PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"first ordering"),"old-before-writer",preview.SourceVersion));
            await AssertNativeWait(h.Db,pending);await tx.CommitAsync();Assert.Equal("LEGACY_SOURCE_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>pending)).Message);
        }
        preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        var copier=PersonalForm(h,async _=>{if(++calls==2){entered.TrySetResult();await release.Task;}return h.User;});
        var copying=copier.AdoptLegacyDraftAsync(source.Draft,PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"second ordering"),"copy-before-writer",preview.SourceVersion));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var updating=writer.Wf_FormDrafts.Where(x=>x.Id==source.Draft).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Title,"native writer committed later"));
        PersonalFormResultV2 copied;
        try{await AssertNativeWait(writer,updating);release.TrySetResult();copied=await copying;await updating;}finally{release.TrySetResult();}
        Assert.Equal("native writer committed first",PersonalResult<PersonalFormDraftV2>(copied).Title);Assert.Equal("native writer committed later",await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).Select(x=>x.Title).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_missing_source_guard_or_invalid_legacy_data_never_becomes_an_empty_success()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FormDraft\" DISABLE TRIGGER legacy_source_revision");
        try{Assert.Equal("LEGACY_SOURCE_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(source.Draft))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Wf_FormDraft\" ENABLE TRIGGER legacy_source_revision");}
        await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.DataJson,"{\"a\":\"original\",\"unmapped-private-field\":\"must stay stored\"}"));
        Assert.Equal("FORM_FIELD_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(source.Draft))).Message);
        Assert.Contains("must stay stored",await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).Select(x=>x.DataJson).SingleAsync());Assert.Empty((await owner.DraftsAsync()).Items);
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_files_require_current_available_unclaimed_FORM_content_and_claim_exactly_once()
    {
        await using var h=await Seed();var(_,form,_)=await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scanner=new ContentInspector{Status="PENDING"};
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=System.Text.Encoding.UTF8.GetBytes("original simulated FILE content 原字节");
        var input=FormUpload(context,bytes);var file=(await owner.UploadFileAsync(input,new MemoryStream(bytes))).Result!;
        var source=await LegacyAdoptionSource(h,FileFormSchema,FileData(file.Reference));var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        var command=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"adopt with actual FILE authority"),"file-copy",preview.SourceVersion);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,command))).Message);Assert.Empty((await owner.DraftsAsync()).Items);
        scanner.Status="CLEAN";file=(await owner.ChangeFileAsync(new(Guid.NewGuid(),input.Scope,"files",file.Reference,file.StateVersion,"INSPECT","explicit simulated inspector"))).Result!;
        var copied=PersonalResult<PersonalFormDraftV2>(await owner.AdoptLegacyDraftAsync(source.Draft,command));
        var read=new FormFileReadV2(new("DRAFT",null,copied.DraftId,copied.FormVersionId,copied.RowVersion),"files",file.Reference);Assert.Equal(bytes,(await owner.DownloadFileAsync(read)).Bytes);
        Assert.Equal(copied.DraftId,PersonalResult<PersonalFormDraftV2>(await owner.AdoptLegacyDraftAsync(source.Draft,command)).DraftId);
        Assert.Equal("FORM_FILE_REFERENCE_SCOPE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"new command cannot reuse an already claimed file"),"second-file-copy",preview.SourceVersion)))).Message);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_form_files_v2.claims WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_form_files_v2.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
        var invalid=await LegacyAdoptionSource(h,FileFormSchema,FileData("https://legacy.invalid/private-file"));await LegacyAdoptionMapping(h,form,invalid.Version,context.FormVersionId);
        Assert.Equal("FORM_FILE_REFERENCE_INVALID",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.LegacyAdoptionPreviewAsync(invalid.Draft))).Message);
        Assert.Single((await owner.DraftsAsync()).Items);Assert.Equal(FileData(file.Reference),await h.Db.Wf_FormDrafts.Where(x=>x.Id==source.Draft).Select(x=>x.DataJson).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_current_window_disabled_policy_and_final_expiry_leave_no_copy()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);
        var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);var at=preview.AdoptUntilUtc.AddSeconds(-1);var calls=0;
        var expiring=PersonalForm(h,_=>{if(++calls==2)at=preview.AdoptUntilUtc;return Task.FromResult(h.User);});expiring.Clock=()=>at;
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"current window is final-state authority"),"expires-at-commit",preview.SourceVersion);
        Assert.Equal("LEGACY_ADOPTION_WINDOW_EXPIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>expiring.AdoptLegacyDraftAsync(source.Draft,raw))).Message);Assert.Empty((await owner.DraftsAsync()).Items);
        owner.Clock=()=>preview.AdoptUntilUtc;Assert.Contains("LEGACY_ADOPTION_WINDOW_EXPIRED",(await owner.LegacyAdoptionPreviewAsync(source.Draft)).Blockers);owner.Clock=()=>DateTimeOffset.UtcNow;
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,[])],policy.RetentionPolicyRef,30,false,"disable new legacy adoption"));
        Assert.Contains("LEGACY_ADOPTION_DISABLED",(await owner.LegacyAdoptionPreviewAsync(source.Draft)).Blockers);
        Assert.Equal("LEGACY_ADOPTION_DISABLED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.AdoptLegacyDraftAsync(source.Draft,raw))).Message);
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"expires-at-commit")).Outcome);Assert.Single(await h.Db.Wf_FormDrafts.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task LegacyDraftAdoption_missing_origin_at_real_commit_rolls_back_copy_and_original_then_recovers_same_intent()
    {
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");var source=await LegacyAdoptionSource(h);var mapping=await LegacyAdoptionMapping(h,form,source.Version,context.FormVersionId);var preview=await owner.LegacyAdoptionPreviewAsync(source.Draft);
        var raw=PersonalCommand(new LegacyDraftAdoptInputV1(mapping.Version,"origin must commit together"),"missing-origin",preview.SourceVersion);
        await h.Db.Database.ExecuteSqlRawAsync("CREATE FUNCTION public.skip_legacy_origin() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RETURN NULL; END $$; CREATE TRIGGER zz_skip_legacy_origin BEFORE INSERT ON cp6_personal_form_v2.legacy_adoptions FOR EACH ROW EXECUTE FUNCTION public.skip_legacy_origin()");
        try{var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.AdoptLegacyDraftAsync(source.Draft,raw));Assert.Equal("23514",error.SqlState);Assert.Contains("LEGACY_DRAFT_ADOPTION_ORIGIN_REQUIRED",error.MessageText);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER zz_skip_legacy_origin ON cp6_personal_form_v2.legacy_adoptions; DROP FUNCTION public.skip_legacy_origin()");}
        Assert.Empty((await owner.DraftsAsync()).Items);Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.AdoptLegacy,source.Draft.ToString("D"),"missing-origin")).Outcome);
        Assert.Equal("SUCCEEDED",(await owner.AdoptLegacyDraftAsync(source.Draft,raw)).Outcome);Assert.Single((await owner.DraftsAsync()).Items);Assert.Single(await h.Db.Wf_FormDrafts.ToArrayAsync());
    }
}
