using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wf;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> FeatureRequestSeed()
    {
        var h=await PalletSeed("create");var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-mobile"))db.Sys_Menus.Add(new(){MenuId=461,MenuKey="wms-mobile",MenuName="Mobile"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-mobile").Select(m=>m.MenuId).SingleAsync();
        if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode=="device-manage"))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode="device-manage",ActionName="Manage devices"});
        db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode="device-manage"});
        db.Sys_Roles.Add(new(){RoleId=73,RoleName="SIM-SEPARATE-REVIEWER",Enable=true});
        db.Sys_Users.Add(new(){Id=Guid.NewGuid(),UserName="sim-feature-reviewer",Password="unused-fixture",RoleId=73});
        var head=new Wf_FlowDef{Id=Guid.NewGuid(),FlowKey="SIM-FEATURE-LEGACY",FlowName="SIM feature approval",SchemaJson=StartSchema,Enable=true};
        db.Wf_FlowDefs.Add(head);
        db.Wf_FlowDefVersions.Add(new(){Id=Guid.NewGuid(),FlowDefId=head.Id,Version=1,Status=WfDefinitionVersionStatus.Published,SchemaJson=StartSchema,FlowNameSnapshot=head.FlowName,PublishedAtUtc=DateTime.UtcNow});
        db.Wf_ApprovalBindings.Add(new(){BizType=WmsFeatureFlagChangeService.ApprovalBizType,FlowKey=head.FlowKey,Enable=true});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private const string FeatureRequestPath="api/v2/admin/wms-feature-changes";
    private static CreateWmsFeatureFlagChangeRequest FeatureRequestInput()=>new(){OperationId=Guid.NewGuid(),WarehouseCd="W1",ProductionMoveEnabled=true,SerialLpnEnabled=false,ScanRetentionDays=365,
        Reason="SIM controlled move request",ChangeTicket="SIM-CHANGE-451"};
    private static async Task<WmsFeatureFlagChangeDto> FeatureAccepted(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.Accepted,await OperationsFailure(response));
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("change").Deserialize<WmsFeatureFlagChangeDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [TenantManagementPostgresTheory]
    [InlineData("get")][InlineData("submit")][InlineData("cancel")]
    public async Task FeatureRequest_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await FeatureRequestSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);Guid change=Guid.Empty;
        if(operation!="submit"){using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,FeatureRequestInput()));change=(await FeatureAccepted(created)).Id;}
        using var normal=await host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureRequestPath));Assert.Equal(HttpStatusCode.OK,normal.StatusCode);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await FeatureRequestWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlagChange\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-request-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"feature-request-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("feature-request-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await FeatureRequestWriteSet(h));
        using var again=await Send();Assert.Equal(HttpStatusCode.Unauthorized,again.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();
        Assert.True(recovered.StatusCode==(operation=="get"?HttpStatusCode.OK:operation=="submit"?HttpStatusCode.Accepted:HttpStatusCode.NoContent),await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,operation=="get"?HttpMethod.Get:HttpMethod.Post,
            operation=="cancel"?FeatureRequestPath+"/"+change+"/cancel":FeatureRequestPath,operation=="submit"?FeatureRequestInput():null));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_current_action_revocation_wins_before_submit()
    {
        await using var h=await FeatureRequestSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-mobile").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await FeatureRequestWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlagChange\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"='device-manage'",control,revoked);
                change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();pending=host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,FeatureRequestInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"feature-request-revoked",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("feature-request-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await FeatureRequestSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_WmsFeatureFlagChange\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,FeatureRequestInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"feature-request-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("feature-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"feature-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("feature-request-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.Accepted,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.WmsFeatureFlagChanges.ToArrayAsync());Assert.Single(await h.Db.Wf_FlowInstances.ToArrayAsync());
    }
}
