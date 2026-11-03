using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_SFS_comment_Unicode_limit_preserves_1000_and_blocks_1001()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        using var result=await BrowserScenario(h,h.User,"oa-sfs-comment-browser.mjs","sfs-comment-browser",new(){["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")},new(){["CP6_COMMENT_TASK"]=f.Task.ToString("D")});
        using var vote=JsonDocument.Parse(await h.Db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Equal(string.Concat(Enumerable.Repeat("🙂",1000)),vote.RootElement.GetProperty("comment").GetString());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
    }
}
