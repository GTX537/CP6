using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task NativeBrowser_actual_SFS_task_edit_conflict_exact_numbers_dictionary_files_and_lost_commit_recovery()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);
        var schema=JsonSerializer.Serialize(new{fields=new object[]{new{name="amount",type="number"},new{name="total",type="number"},new{name="locked",type="input"},
            new{name="choice",type="select",optionSourceV1=new{owner="Main.Sys.DictionaryOptions.v1",scope="SHARED",typeId=type}},new{name="files",type="upload"},
            new{name="rows",type="table",columns=new[]{new{name="quantity",type="number"}}}},rules=new[]{new{then=new[]{new{action="compute",target="total",expr="amount + 0.0001"}}}}});
        var f=await NativeFormSetup(h,true,formSchema:schema,values:"{\"amount\":1,\"locked\":\"pinned readonly\",\"choice\":\"A\",\"rows\":[{\"quantity\":1}]}",
            configure:g=>{foreach(var node in g.Nodes.Where(n=>n.Type=="approval"))node.FieldPerms=new(){["locked"]="readonly",["total"]="readonly"};},files:new(h.Db,new ContentStore(),new ContentInspector()));
        await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,"/oa/inbox"));
        var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().OrderBy(t=>t.Id).Select(t=>t.Id).ToArrayAsync();
        using var result=await BrowserScenario(h,h.User,"oa-sfs-task-edit-browser.mjs","sfs-task-edit-browser",FormFileHostSettings(),
            new(){["CP6_EDIT_TASK"]=tasks[0].ToString("D"),["CP6_PEER_TASK"]=tasks[1].ToString("D")});
        var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Contains("12345678901234567890.0002",data.DataJson);Assert.Contains("9007199254740993.01",data.DataJson);
        Assert.Contains("\"choice\":\"1.0\"",data.DataJson);Assert.Contains("cp6-form-file-v2/",data.DataJson);Assert.Contains("pinned readonly",data.DataJson);
        Assert.Equal(1,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.commands WHERE root_id={f.Instance} AND operation='SFS_TASK_DECISION' AND completed").SingleAsync());
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.bindings WHERE target_id={data.Id}").SingleAsync());
    }
}
