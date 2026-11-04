using System.Text.Json;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_document_old_revision_hold_recovers_after_refresh_without_mutating_current_content()
    {
        await using var h=await Seed();var f=await DocumentReviewSetup(h);await EngineeringAction(h,"plm-controlled-document","availability");
        var owner=Documents(h,f.Files);var first=await owner.ReadAsync(f.Id);await owner.CommandAsync(DocumentRevise(first));
        var second=await owner.ReadAsync(f.Id);
        await h.Db.Sys_Menus.Where(x=>x.MenuKey=="plm-controlled-document").ExecuteUpdateAsync(x=>x.SetProperty(m=>m.RoutePath,"/plm/controlled-document"));
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-controlled-document");
        if(!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))h.Db.Sys_RoleMenus.Add(new(){MenuId=menu.MenuId,RoleId=72});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        using var browser=await BrowserScenario(h,h.User,"document-availability-browser.mjs","document-availability-browser",await DocumentHostSettings(f.Files),new(){["CP6_OA_BROWSER_DOCUMENT"]=f.Id.ToString("D")});
        var accepted=browser.RootElement.GetProperty("accepted").Deserialize<ControlledDocumentResultV1>(PersonalFormOwnerV2.Json)!;
        Assert.Equal("1",accepted.Version);Assert.Equal("HELD",accepted.Availability!.State);
        Assert.Equal(accepted,(await owner.OperationAsync(accepted.OperationId,false)).Result);
        Assert.Equal(first.OriginalJson,(await owner.ReadAsync(f.Id,"1")).OriginalJson);
        Assert.Equal(second.OriginalJson,(await owner.ReadAsync(f.Id,"2")).OriginalJson);
        Assert.Equal("AVAILABLE",(await owner.ReadAsync(f.Id,"2")).Availability!.State);
        Assert.Equal(2,(await owner.AvailabilityHistoryAsync(f.Id,"1")).Length);
    }
}
