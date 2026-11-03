using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PublicationImpact_HTTP_reports_actual_scoped_runtime_without_changing_pins_or_activation()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);
        await using var other=await Seed();await NativeFormSetup(other);
        var active=(await f.Flow.ActivationPreviewAsync("native-inbox")).Current;
        var next=await f.Flow.CreateNextDraftAsync("native-inbox",NextDraftCommand((await f.Flow.ReadHeadAsync("native-inbox")).Version,active.PublishedVersionId!.Value));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await DocumentReviewerLogin(host,h,h.User);
        using var response=await host.Send(BoundRequest(cookies,"/api/oa/v2/definitions/FLOW/native-inbox/publish-preview",new FlowPublicationPreviewInputV2(next.Result!.Draft!.DraftId,next.ResourceVersion!)));
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var json=doc.RootElement.GetProperty("data");
        Assert.Equal("PINNED_RUNTIME_ADOPTED",json.GetProperty("consumerState").GetString());
        var impact=json.GetProperty("impact");Assert.Equal(1,impact.GetProperty("runningInstances").GetInt32());Assert.Equal(1,impact.GetProperty("rootInstances").GetInt32());Assert.Equal(1,impact.GetProperty("personalEntryBindings").GetInt32());
        Assert.False(impact.TryGetProperty("instanceIds",out _));Assert.False(impact.TryGetProperty("subjects",out _));
        var preview=json.Deserialize<FlowPublicationPreviewV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await f.Flow.PublishAsync("native-inbox",PublicationCommand(preview,id:"publish-next"));
        Assert.Equal(active,(await f.Flow.ActivationPreviewAsync("native-inbox")).Current);
        Assert.Equal(active.PublishedVersionId,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).FlowDefVersionId);
        Assert.Equal(1,await h.Db.Wf_FlowTasks.CountAsync());
        var saved=await f.Flow.CreateNextDraftAsync("native-inbox",NextDraftCommand((await f.Flow.ReadHeadAsync("native-inbox")).Version,next.Result.Draft.DraftId,id:"next-again"));
        await h.Db.Sys_RoleActions.Where(a=>a.ActionCode=="publish").ExecuteDeleteAsync();
        using var denied=await host.Send(BoundRequest(cookies,"/api/oa/v2/definitions/FLOW/native-inbox/publish-preview",new FlowPublicationPreviewInputV2(saved.Result!.Draft!.DraftId,saved.ResourceVersion!)));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("runningInstances",await denied.Content.ReadAsStringAsync());
    }
}
