using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RebasePreview_HTTP_exact_classification_required_paths_and_row_limits_preserve_active_data_and_original()
    {
        const string source="""{"fields":[{"name":"amount","type":"number"},{"name":"obsolete","type":"input"},{"name":"convert","type":"input"},{"name":"rows","type":"table","maxRows":5,"columns":[{"name":"keep","type":"number"},{"name":"remove","type":"input"}]}]}""";
        const string target="""{"fields":[{"name":"amount","type":"number"},{"name":"convert","type":"number"},{"name":"defaulted","type":"input","default":"explicit default"},{"name":"requiredNow","type":"input"},{"name":"rows","type":"table","maxRows":1,"columns":[{"name":"keep","type":"number"},{"name":"requiredCell","type":"input","required":true}]}],"rules":[{"when":"amount > 0","then":[{"action":"require","target":"requiredNow"}]}]}""";
        await using var h=await Seed();var(owner,form,_)=await PreparePersonal(h,source);var context=await owner.ContextAsync("personal");
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData("""{"amount":9007199254740993.0001,"obsolete":"history","convert":"7","rows":[{"keep":1,"remove":"first"},{"keep":2,"remove":"second"}]}"""),null))));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var saved=await form.SaveAsync("personal",FormAuthoringCommand(schema:target,version:next.ResourceVersion,id:"next"));
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await FormPreview(form,"personal",saved),id:"publish-next"))).Result!.Publication!;
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,h.User);
        using var response=await host.Send(PersonalHttp(cookies,"/api/oa/v2/drafts/"+draft.DraftId+"/rebase-preview",JsonSerializer.SerializeToUtf8Bytes(new{targetVersionId=publication.VersionId,expectedVersion=draft.RowVersion})));
        var preview=await PersonalHttpResult<JsonElement>(response);
        Assert.Equal(publication.SchemaDigest,preview.GetProperty("targetSchemaDigest").GetString());Assert.Equal(draft.RowVersion,preview.GetProperty("sourceDraftVersion").GetString());
        string[] Paths(string property)=>preview.GetProperty(property).EnumerateArray().Select(x=>x.GetString()!).ToArray();
        Assert.Contains("amount",Paths("retainedPaths"));Assert.Contains("rows[1].keep",Paths("retainedPaths"));
        Assert.Contains("obsolete",Paths("removedPaths"));Assert.Contains("convert",Paths("removedPaths"));Assert.Contains("rows[1].remove",Paths("removedPaths"));
        Assert.Contains("convert",Paths("typeChangedPaths"));Assert.Contains("requiredNow",Paths("addedPaths"));Assert.Equal(new[]{"defaulted"},Paths("defaultedPaths"));
        Assert.Contains("requiredNow",Paths("requiredAfterRebase"));Assert.Contains("rows[0].requiredCell",Paths("requiredAfterRebase"));
        Assert.Contains(preview.GetProperty("diagnostics").EnumerateArray(),x=>x.GetProperty("path").GetString()=="requiredNow"&&x.GetProperty("code").GetString()=="REQUIRED");
        Assert.Contains(preview.GetProperty("diagnostics").EnumerateArray(),x=>x.GetProperty("path").GetString()=="rows[1]"&&x.GetProperty("code").GetString()=="ROW_COUNT_EXCEEDS_TARGET");
        Assert.Equal(draft.DataJson,(await owner.DraftAsync(draft.DraftId)).DataJson);
        var input=new PersonalFormRebaseInputV2(preview.GetProperty("previewId").GetGuid(),preview.GetProperty("digest").GetString()!,publication.VersionId,true);
        var raw=PersonalCommand(input,"rebase",draft.RowVersion);var result=await owner.RebaseDraftAsync(draft.DraftId,raw);var current=PersonalResult<PersonalFormDraftV2>(result);
        Assert.Equal(0,current.Status);Assert.Contains("9007199254740993.0001",current.DataJson);
        using var data=JsonDocument.Parse(current.DataJson);Assert.Equal(2,data.RootElement.GetProperty("rows").GetArrayLength());Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("convert").ValueKind);
        Assert.Equal(result.ResourceVersion,(await owner.RebaseDraftAsync(draft.DraftId,raw)).ResourceVersion);
        await form.SetActivationAsync("personal",FormActivationCommand((await form.ActivationPreviewAsync("personal")).Current,publication.VersionId,id:"activate-next"));
        var latest=await owner.ContextAsync("personal");await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDraftAsync(current.DraftId,PersonalCommand(PersonalSubmit(latest),"blocked-submit",current.RowVersion)));
        Assert.Equal(0,(await owner.DraftAsync(draft.DraftId)).Status);Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Contains("history",(await owner.OriginalAsync(PersonalFormOwnerV2.Create,"personal","create")).Result!.Value.GetRawText());
    }
}
