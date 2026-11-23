using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [OaNativeBrowserPostgresFact]
    public async Task WorkflowNotice_Chrome_settings_actual_inbox_scope_and_task_link_reauthorize()
    {
        await using var h=await Seed();
        var f=await NativeFormSetup(h,parallel:true,title:"synthetic-private-form-title");
        await NativeOperationPermissions(h);
        await NoticePermissions(h);
        var delegateId=await GrantDelegate(h,"notice-delegate");
        var password=new BCryptPasswordHasher(4).Hash("Delegate1!Fixture");
        await h.Db.Sys_Users.Where(u=>u.Id==delegateId).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Password,password));
        await CreateTaskGrant(h,h.User,delegateId,"native-inbox");
        await NoticeRoutes(h);
        var before=await f.Owner.TaskContextAsync(f.Task);
        var original=await h.Db.Wf_Notifications.AsNoTracking().Where(n=>n.UserId==h.User).ToArrayAsync();
        Assert.Equal(3,original.Length);
        var revokedNotice=Assert.Single(original,n=>n.TaskId==f.Task);
        var liveNotice=Assert.Single(original,n=>n.TaskId is not null && n.TaskId!=f.Task);
        var submittedNotice=Assert.Single(original,n=>n.TaskId is null);

        await using var foreign=await Seed();
        await RegisterScope(foreign);
        await NoticePermissions(foreign);
        await NoticeRoutes(foreign);
        var foreignRow=NewNotice(foreign.User);
        foreign.Db.Wf_Notifications.Add(foreignRow);
        await foreign.Db.SaveChangesAsync();

        await using (var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings()))
        {
            var principal=await Login(host,h);
            var deputy=await Login(host,"notice-delegate","Delegate1!Fixture",h.Tenant.ToString("N"));
            var formerRoute=$"api/oa/v2/tasks/{f.Task:D}/action-context";
            using var earlier=await host.Send(OperationsRequest(principal,HttpMethod.Get,formerRoute));
            Assert.Equal(HttpStatusCode.OK,earlier.StatusCode);
            using var delegated=OperationsRequest(deputy,HttpMethod.Get,formerRoute);
            delegated.Headers.Add("X-Acting-As",h.User.ToString("D"));
            using var delegatedContext=await host.Send(delegated);
            Assert.True(delegatedContext.IsSuccessStatusCode,await OperationsFailure(delegatedContext));
            using var ownNotices=OperationsRequest(deputy,HttpMethod.Get,Notices);
            ownNotices.Headers.Add("X-Acting-As",h.User.ToString("D"));
            using var ownResponse=await host.Send(ownNotices);
            Assert.True(ownResponse.IsSuccessStatusCode,await OperationsFailure(ownResponse));
            Assert.Empty((await ownResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("data").GetProperty("items").EnumerateArray());

            var command=PersonalCommand(new NativeTransferInputV2(f.Task,delegateId,"合成任务转交",before.Task.TaskRowVersion),
                "notice-transfer",before.Task.InstanceRowVersion);
            await f.Owner.TransferTaskAsync(f.Task,command);
            using var noLonger=await host.Send(OperationsRequest(principal,HttpMethod.Get,formerRoute));
            Assert.Contains(noLonger.StatusCode,new[]{ HttpStatusCode.Forbidden,HttpStatusCode.NotFound });
            Assert.DoesNotContain("synthetic-private-form-title",await OperationsFailure(noLonger));
        }
        var delegatedNotice=await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n=>n.UserId==delegateId);

        await using var app=await Seed();
        var (_,firstReviewer,secondReviewer)=await DecisionSetup(app);
        await NativeOperationPermissions(app);
        await NoticePermissions(app);
        await NoticeRoutes(app);
        var notificationReviewer=Guid.NewGuid();
        var newReviewer=Guid.NewGuid();
        app.Db.Sys_Users.Add(new() { Id=notificationReviewer,UserName="app-notice-reviewer",Password=password,RoleId=73 });
        app.Db.Sys_Users.Add(new() { Id=newReviewer,UserName="app-notice-final-reviewer",Password=password,RoleId=73 });
        await app.Db.SaveChangesAsync();
        await TaskPermission(app,notificationReviewer);
        await TaskPermission(app,newReviewer);
        var appRevokedTask=(await DecisionInput(DecisionActor(app,firstReviewer))).Task;
        var appLiveTask=(await DecisionInput(DecisionActor(app,secondReviewer))).Task;
        await TransferAppNotice(app,firstReviewer,notificationReviewer,appRevokedTask,"notice-app-original");
        await TransferAppNotice(app,notificationReviewer,newReviewer,appRevokedTask,"notice-app-revoked");
        await TransferAppNotice(app,secondReviewer,notificationReviewer,appLiveTask,"notice-app-live");
        var appFormerNotice=await app.Db.Wf_Notifications.AsNoTracking()
            .SingleAsync(n=>n.UserId==notificationReviewer && n.TaskId==appRevokedTask);
        var appLiveNotice=await app.Db.Wf_Notifications.AsNoTracking()
            .SingleAsync(n=>n.UserId==notificationReviewer && n.TaskId==appLiveTask);

        using var result=await BrowserScenario(h,delegateId,"oa-notification-workflow-browser.mjs","workflow-notification-browser",ConnSettings(),
            new()
            {
                ["CP6_NOTICE_LIVE"]=liveNotice.Id.ToString("D"),
                ["CP6_NOTICE_SUBMITTED"]=submittedNotice.Id.ToString("D"),
                ["CP6_NOTICE_LIVE_TASK"]=liveNotice.TaskId!.Value.ToString("D"),
                ["CP6_NOTICE_REVOKED"]=revokedNotice.Id.ToString("D"),
                ["CP6_NOTICE_REVOKED_TASK"]=f.Task.ToString("D"),
                ["CP6_NOTICE_DELEGATE"]=delegatedNotice.Id.ToString("D"),
                ["CP6_NOTICE_FOREIGN_TENANT"]=foreign.Tenant.ToString("N"),
                ["CP6_NOTICE_FOREIGN"]=foreignRow.Id.ToString("D"),
                ["CP6_NOTICE_APP_TENANT"]=app.Tenant.ToString("N"),
                ["CP6_NOTICE_APP_LIVE"]=appLiveNotice.Id.ToString("D"),
                ["CP6_NOTICE_APP_LIVE_TASK"]=appLiveTask.ToString("D"),
                ["CP6_NOTICE_APP_REVOKED"]=appFormerNotice.Id.ToString("D"),
                ["CP6_NOTICE_APP_REVOKED_TASK"]=appRevokedTask.ToString("D")
            });
        Assert.Equal("PASS",result.RootElement.GetProperty("result").GetString());
        Assert.Equal(1,await NoticeReceiptCount(h));
        Assert.Empty((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes);
        Assert.True(result.RootElement.GetProperty("appLinkOpened").GetBoolean());
        Assert.True(result.RootElement.GetProperty("appRevokedLinkCleared").GetBoolean());
        Assert.Equal(0,await app.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_approval_decision_v2.votes WHERE tenant_id={app.Tenant}
            """).SingleAsync());
        output.WriteLine("Chrome 实测偏好 CAS、FORM 和 APP 原通知的可用与失权深链、实际代理及隔离租户收件。审批投票和外送均未调用。");
    }

    private static async Task TransferAppNotice(Harness h,Guid actor,Guid recipient,Guid task,string key)
    {
        var owner=Decisions(DecisionActor(h,actor));
        var before=await owner.ContextAsync(task);
        await owner.TransferTaskAsync(task,PersonalCommand(
            new NativeTransferInputV2(task,recipient,"合成 APP 收件转交",before.TaskRowVersion),key,before.InstanceRowVersion));
    }

    private static async Task NoticeRoutes(Harness h)
    {
        foreach(var (key,path) in new[]{ ("oa-inbox","/oa/inbox"),("oa-settings","/oa/settings") })
            await h.Db.Sys_Menus.Where(m=>m.MenuKey==key).ExecuteUpdateAsync(s=>s.SetProperty(m=>m.RoutePath,path));
    }
}
