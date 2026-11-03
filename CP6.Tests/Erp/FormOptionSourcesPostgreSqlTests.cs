using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<int> OptionDictionary(Harness h)
    {
        var type=new Sys_DictType{TypeCode="option-"+Guid.NewGuid().ToString("N"),TypeName="Shared simulated reference choices"};
        h.Db.Sys_DictTypes.Add(type);await h.Db.SaveChangesAsync();
        foreach(var value in new[]{"A","a","1","1.0"})h.Db.Sys_DictDatas.Add(new(){TypeCode=type.TypeCode,Value=value,Label="Choice "+value});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return type.Id;
    }
    private static string OptionFormSchema(int type)=>JsonSerializer.Serialize(new{fields=new object[]{
        new{name="choice",type="select",required=false,optionSourceV1=new{owner="Main.Sys.DictionaryOptions.v1",scope="SHARED",typeId=type}},
        new{name="a",type="input"}}});

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_optional_external_value_is_checked_at_direct_submit_and_preserves_exact_source_string()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type));
        var context=await owner.ContextAsync("personal");
        var error=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"choice\":\"forged\"}"),"invalid-option")));
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",error.Message);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
        var result=await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"choice\":\"1.0\"}"),"valid-option"));
        Assert.Equal("SUCCEEDED",result.Outcome);Assert.Contains("\"1.0\"",(await h.Db.Wf_FormDatas.SingleAsync()).DataJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_current_source_rejects_missing_disabled_ambiguous_and_uncovered_without_empty_fallback()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type));
        var context=await owner.ContextAsync("personal");var query=new FormOptionQueryV1(new("FORM",context.FormVersionId,"personal"),["choice"]);
        Assert.Equal(new[]{"A","a","1","1.0"},(await owner.OptionsAsync(query)).Items.Select(x=>x.Value));
        var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).TypeCode;
        var duplicate=new Sys_DictData{TypeCode=code,Value="A",Label="ambiguous"};h.Db.Sys_DictDatas.Add(duplicate);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FORM_OPTION_SOURCE_AMBIGUOUS",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
        await h.Db.Sys_DictDatas.Where(d=>d.Id==duplicate.Id).ExecuteDeleteAsync();
        var secondType=new Sys_DictType{TypeCode=code,TypeName="ambiguous type"};h.Db.Sys_DictTypes.Add(secondType);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FORM_OPTION_SOURCE_AMBIGUOUS",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
        await h.Db.Sys_DictTypes.Where(t=>t.Id==secondType.Id).ExecuteDeleteAsync();
        await h.Db.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        Assert.Equal("FORM_OPTION_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{}"),"disabled-empty")))).Message);
        await h.Db.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,true));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_DictDatas\" DISABLE TRIGGER cp6_form_options_writer");
        try{Assert.Equal("FORM_OPTION_SOURCE_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"Sys_DictDatas\" ENABLE TRIGGER cp6_form_options_writer");}
        await h.Db.Sys_DictTypes.Where(t=>t.Id==type).ExecuteDeleteAsync();
        Assert.Equal("FORM_OPTION_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_draft_retains_revoked_original_while_unrelated_edit_succeeds_and_submit_rechecks()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type));var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"choice\":\"A\"}"),null))));
        var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).TypeCode;
        // Provider comparisons are case-insensitive; disable only the exact original item by its primary key.
        var item=(await h.Db.Sys_DictDatas.AsNoTracking().Where(d=>d.TypeCode==code).ToArrayAsync()).Single(x=>x.Value=="A");
        await h.Db.Sys_DictDatas.Where(d=>d.Id==item.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        var saved=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"choice\":\"A\",\"a\":\"unrelated\"}"),null),"save-retained",draft.RowVersion)));
        Assert.Equal("A",JsonDocument.Parse(saved.DataJson).RootElement.GetProperty("choice").GetString());
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"reject-stale",saved.RowVersion)))).Message);
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"choice\":\"forged\"}"),null),"reject-new",saved.RowVersion)))).Message);
        var replacement=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"choice\":\"a\"}"),null),"replace-case",saved.RowVersion)));
        var command=PersonalCommand(PersonalSubmit(context),"accepted",replacement.RowVersion);var result=await owner.SubmitDraftAsync(draft.DraftId,command);
        await h.Db.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        Assert.Equal(result.ResourceId,(await owner.SubmitDraftAsync(draft.DraftId,command)).ResourceId);
        Assert.Equal(result.ResourceId,(await owner.OriginalAsync(PersonalFormOwnerV2.Submit,draft.DraftId.ToString("D"),"accepted")).ResourceId);
        Assert.Equal("a",JsonDocument.Parse((await h.Db.Wf_FormDatas.SingleAsync()).DataJson).RootElement.GetProperty("choice").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_nested_checkbox_exact_strings_and_field_context_are_authoritative()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var source=new{owner=FormOptionSourceV1.OwnerName,scope="SHARED",typeId=type};
        var schema=JsonSerializer.Serialize(new{fields=new object[]{new{name="checks",type="checkbox",optionSourceV1=source},new{name="rows.with.dot",type="table",columns=new[]{new{name="cell.with.dot",type="select",optionSourceV1=source}}}}});
        var(owner,form,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var query=new FormOptionQueryV1(new("FORM",context.FormVersionId,"personal"),["rows.with.dot","cell.with.dot"]);
        Assert.Equal(4,(await owner.OptionsAsync(query)).Items.Length);Assert.Contains(await form.OptionSourcesAsync(),x=>x.Id==type);
        Assert.Equal("FORM_CONTEXT_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query with{Context=query.Context with{FormVersionId=Guid.NewGuid()}}))).Message);
        foreach(var invalid in new[]{"{\"checks\":[1]}","{\"checks\":true}","{\"rows.with.dot\":[{\"cell.with.dot\":1.0}]}","{\"rows.with.dot\":[{\"cell.with.dot\":\"forged\"}]}"})
            Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,invalid),Guid.NewGuid().ToString("N"))))).Message);
        var exact="{\"checks\":[\"A\",\"a\",\"1\",\"1.0\"],\"rows.with.dot\":[{\"cell.with.dot\":\"1.0\"}]}";
        await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,exact),"exact"));
        Assert.Equal(exact,(await h.Db.Wf_FormDatas.SingleAsync()).DataJson);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"rows.with.dot","hidden"}})],policy.RetentionPolicyRef,30,true,"current field revoke"));
        Assert.Equal("FIELD_OPTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
        await using var other=await Seed();await PreparePersonal(other,schema);
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>PersonalForm(other).OptionsAsync(query))).Status);
        Assert.Equal("SHARED",(await PersonalForm(other).OptionsAsync(query with{Context=query.Context with{FormVersionId=(await PersonalForm(other).ContextAsync("personal")).FormVersionId}})).Scope);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_publication_and_activation_recheck_source_and_rebase_keeps_invalid_original()
    {
        await using var h=await Seed();var oldType=await OptionDictionary(h);var newType=await OptionDictionary(h);var(owner,form,_)=await PreparePersonal(h,OptionFormSchema(oldType));var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"choice\":\"A\"}"),null))));
        var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==newType)).TypeCode;
        var item=(await h.Db.Sys_DictDatas.AsNoTracking().Where(d=>d.TypeCode==code).ToArrayAsync()).Single(d=>d.Value=="A");await h.Db.Sys_DictDatas.Where(d=>d.Id==item.Id).ExecuteDeleteAsync();
        var head=await form.ReadHeadAsync("personal");var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand(head.Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:OptionFormSchema(newType),version:next.ResourceVersion,id:"next-save"));
        var preview=await FormPreview(form,"personal",saved);
        Assert.Contains(preview.Dependencies,d=>d.Kind=="OPTION");
        await h.Db.Sys_DictTypes.Where(t=>t.Id==newType).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        Assert.False((await FormPreview(form,"personal",saved)).CanPublish);
        Assert.Equal("PREVIEW_STALE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.PublishAsync("personal",FormPublicationCommand(preview,id:"stale")))).Message);
        await h.Db.Sys_DictTypes.Where(t=>t.Id==newType).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,true));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"new-publication"))).Result!.Publication!;
        var rebase=await owner.RebasePreviewAsync(draft.DraftId,new(publication.VersionId,draft.RowVersion));Assert.Contains("FORM_OPTION_VALUE_UNAVAILABLE",rebase.ValidationErrors);
        var rebased=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormRebaseInputV2(rebase.PreviewId,rebase.Digest,publication.VersionId,false),"rebase",draft.RowVersion)));
        Assert.Equal(draft.DataJson,rebased.DataJson);
        await h.Db.Sys_DictTypes.Where(t=>t.Id==newType).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        var activation=(await form.ActivationPreviewAsync("personal")).Current;
        Assert.Equal("ACTIVATION_DEPENDENCY_CHANGED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>form.SetActivationAsync("personal",FormActivationCommand(activation,publication.VersionId,id:"new-activation")))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_stale_table_cell_cannot_be_copied_to_new_row_and_exact_whitespace_is_not_coerced()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).TypeCode;
        h.Db.Sys_DictDatas.AddRange(new Sys_DictData{TypeCode=code,Value="trail",Label="plain"},new Sys_DictData{TypeCode=code,Value="trail ",Label="trailing space"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var schema=JsonSerializer.Serialize(new{fields=new object[]{new{name="rows",type="table",columns=new[]{new{name="cell",type="select",optionSourceV1=new{owner=FormOptionSourceV1.OwnerName,scope="SHARED",typeId=type}}}},new{name="a",type="input"}}});
        var(owner,_,_)=await PreparePersonal(h,schema);var context=await owner.ContextAsync("personal");
        var options=await owner.OptionsAsync(new(new("FORM",context.FormVersionId,"personal"),["rows","cell"]));Assert.Contains(options.Items,x=>x.Value=="trail ");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"rows\":[{\"cell\":\"A\"}]}"),null))));
        var revoked=(await h.Db.Sys_DictDatas.AsNoTracking().Where(d=>d.TypeCode==code).ToArrayAsync()).Single(d=>d.Value=="A");await h.Db.Sys_DictDatas.Where(d=>d.Id==revoked.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        var retained=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"rows\":[{\"cell\":\"A\"}],\"a\":\"unrelated\"}"),null),"retain",draft.RowVersion)));
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"rows\":[{\"cell\":\"A\"},{\"cell\":\"A\"}]}"),null),"copied-stale",retained.RowVersion)))).Message);
        await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"rows\":[{\"cell\":\"trail \"}]}"),"exact-space"));
        Assert.Equal("trail ",JsonDocument.Parse((await h.Db.Wf_FormDatas.SingleAsync()).DataJson).RootElement.GetProperty("rows")[0].GetProperty("cell").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_rebase_from_static_number_retains_original_without_treating_it_as_string_identity()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var(owner,form,_)=await PreparePersonal(h,"""{"fields":[{"name":"choice","type":"select","options":[{"label":"numeric","value":1.0}]},{"name":"a","type":"input"}]}""");var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"choice\":1.0}"),null))));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:OptionFormSchema(type),version:next.ResourceVersion,id:"string-source"));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"string-publish"))).Result!.Publication!;
        var preview=await owner.RebasePreviewAsync(draft.DraftId,new(publication.VersionId,draft.RowVersion));Assert.Contains("FORM_OPTION_VALUE_UNAVAILABLE",preview.ValidationErrors);
        var result=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormRebaseInputV2(preview.PreviewId,preview.Digest,publication.VersionId,false),"number-retained",draft.RowVersion)));
        Assert.Contains("\"choice\":1.0",result.DataJson);
        var edited=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"choice\":1.0,\"a\":\"still retained\"}"),null),"unrelated",result.RowVersion)));Assert.Contains("\"choice\":1.0",edited.DataJson);
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,publication.VersionId,id:"new-source"));
        var active=await owner.ContextAsync("personal");Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(active),"numeric-denied",edited.RowVersion)))).Message);
        var corrected=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"choice\":\"1.0\"}"),null),"explicit-string",edited.RowVersion)));
        Assert.Equal("SUCCEEDED",(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(active),"string-accepted",corrected.RowVersion))).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_pages_current_source_and_preserves_hidden_draft_values_without_new_admission()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(t=>t.Id==type)).TypeCode;
        for(var i=0;i<105;i++)h.Db.Sys_DictDatas.Add(new(){TypeCode=code,Value="paged-"+i.ToString("D3"),Label="Paged "+i.ToString("D3"),OrderNo=i+1});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type));var context=await owner.ContextAsync("personal");
        var query=new FormOptionQueryV1(new("FORM",context.FormVersionId,"personal"),["choice"],"Paged");var first=await owner.OptionsAsync(query);var next=await owner.OptionsAsync(query with{Page=2});
        Assert.Equal(100,first.Items.Length);Assert.True(first.HasMore);Assert.Equal(5,next.Items.Length);Assert.False(next.HasMore);Assert.Equal("paged-104",next.Items[^1].Value);
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("{\"choice\":\"A\"}"),null))));
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"choice","hidden"}})],policy.RetentionPolicyRef,30,true,"hide stored choice"));
        await h.Db.Sys_DictTypes.Where(t=>t.Id==type).ExecuteUpdateAsync(s=>s.SetProperty(t=>t.Enable,false));
        var retained=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"unrelated\"}"),null),"hidden-retained",draft.RowVersion)));
        Assert.DoesNotContain("choice",retained.DataJson);
        var stored=await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={draft.DraftId}").SingleAsync();Assert.Contains("\"choice\":\"A\"",stored);
        Assert.Equal("FORM_OPTION_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"hidden-submit",retained.RowVersion)))).Message);
        Assert.Equal("FIELD_OPTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.OptionsAsync(query))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_legal_long_field_identity_keeps_existing_schema_contract()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var name=new string('x',250)+".part";var(owner,_,_)=await PreparePersonal(h,OptionFormSchema(type).Replace("choice",name));var context=await owner.ContextAsync("personal");
        var options=await owner.OptionsAsync(new(new("FORM",context.FormVersionId,"personal"),[name]));Assert.Contains(options.Items,x=>x.Value=="1.0");
        var data=JsonSerializer.Serialize(new Dictionary<string,string>{{name,"1.0"}});await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,data),"long-field"));
        Assert.Equal("1.0",JsonDocument.Parse((await h.Db.Wf_FormDatas.SingleAsync()).DataJson).RootElement.GetProperty(name).GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormOptions_publication_validates_column_defaults_before_any_row_is_created()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var form=await PrepareFormPublication(h);
        string Schema(string value)=>JsonSerializer.Serialize(new{fields=new[]{new{name="rows",type="table",columns=new[]{new{name="cell",type="select",@default=value,optionSourceV1=new{owner=FormOptionSourceV1.OwnerName,scope="SHARED",typeId=type}}}}}});
        var saved=await form.SaveAsync("column-default",FormAuthoringCommand(schema:Schema("forged")));var preview=await FormPreview(form,"column-default",saved);
        Assert.False(preview.CanPublish);Assert.Contains("FORM_OPTION_VALUE_UNAVAILABLE",preview.Diagnostics);
        var corrected=await form.SaveAsync("column-default",FormAuthoringCommand(schema:Schema("1.0"),version:saved.ResourceVersion,id:"correct"));var ready=await FormPreview(form,"column-default",corrected);Assert.True(ready.CanPublish);
        var publication=(await form.PublishAsync("column-default",FormPublicationCommand(ready))).Result!.Publication!;
        Assert.DoesNotContain("\"rows\":",publication.Fields["SchemaJson"]);Assert.Contains("\"default\":\"1.0\"",publication.Fields["SchemaJson"]);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }
}
