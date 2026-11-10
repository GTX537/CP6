using System.Net;
using System.Text.Json;
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
    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_current_warehouse_loss_wins_before_workflow_submission()
    {
        await using var h=await FeatureRequestSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await FeatureRequestWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Wf_ApprovalBinding\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); UPDATE public.\"T_Warehouse\" SET \"IsDeleted\"=true WHERE \"TenantId\"=@tenant AND \"WarehouseCd\"='W1'",control,changed);
                change.Parameters.AddWithValue("tenant",h.Tenant);await change.ExecuteNonQueryAsync();pending=host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,FeatureRequestInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"feature-request-warehouse",pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("feature-request-warehouse-loss.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await FeatureRequestWriteSet(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("submit")][InlineData("cancel")]
    public async Task FeatureRequest_late_workflow_fault_rolls_back_the_whole_request_and_recovers(string operation)
    {
        await using var h=await FeatureRequestSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=FeatureRequestInput();Guid change=Guid.Empty;
        if(operation=="cancel"){using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));change=(await FeatureAccepted(created)).Id;}
        await using var fault=new NpgsqlConnection(connection);await fault.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_feature_once;
            CREATE FUNCTION public.cp6_test_feature_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_feature_once')=1 THEN RAISE EXCEPTION 'simulated feature workflow write failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_feature_failure BEFORE INSERT ON public."Wf_FlowHistory" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_feature_failure();
            """,fault);await install.ExecuteNonQueryAsync();var before=await FeatureRequestWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("feature-request-"+operation+"-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await FeatureRequestWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_feature_failure ON public.\"Wf_FlowHistory\"; DROP FUNCTION public.cp6_test_feature_failure(); DROP SEQUENCE public.cp6_test_feature_once",fault);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();Assert.True(recovered.StatusCode==(operation=="submit"?HttpStatusCode.Accepted:HttpStatusCode.NoContent),await OperationsFailure(recovered));
        var row=await h.Db.WmsFeatureFlagChanges.AsNoTracking().SingleAsync();Assert.Equal(operation=="submit"?WmsFeatureFlagChangeStatus.Pending:WmsFeatureFlagChangeStatus.Cancelled,row.Status);
        Assert.Equal(operation=="submit"?FlowInstanceStatus.Running:FlowInstanceStatus.Withdrawn,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);Assert.Empty(await h.Db.WmsFeatureFlags.ToArrayAsync());
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,operation=="submit"?FeatureRequestPath:FeatureRequestPath+"/"+change+"/cancel",operation=="submit"?input:null));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_missing_workflow_binding_leaves_no_orphan_and_recovers()
    {
        await using var h=await FeatureRequestSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var input=FeatureRequestInput();
        await h.Db.Wf_ApprovalBindings.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,false));var before=await FeatureRequestWriteSet(h);
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));
        await DeviceEvidence("feature-request-missing-binding.json",new{Status=(int)missing.StatusCode,Before=before,After=await FeatureRequestWriteSet(h)});
        Assert.Equal(HttpStatusCode.ServiceUnavailable,missing.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
        await h.Db.Wf_ApprovalBindings.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,true));
        using var recovered=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));var dto=await FeatureAccepted(recovered);
        Assert.Equal(WmsFeatureFlagChangeStatus.Pending,dto.Status);Assert.Single(await h.Db.Wf_FlowInstances.ToArrayAsync());Assert.Single(await h.Db.WmsFeatureFlagChanges.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_competing_new_requests_keep_one_complete_workflow_graph()
    {
        await using var h=await FeatureRequestSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_feature_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726451001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_feature_gate BEFORE INSERT ON public."Wf_FlowHistory" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_feature_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726451001)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    first=Send();firstWait=await WaitForFence(observer,control.ProcessID,"%INSERT%Wf_FlowHistory%",null,first);
                    second=Send();secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"feature-second-submit",second);
                }
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;
            var requests=await h.Db.WmsFeatureFlagChanges.AsNoTracking().ToArrayAsync();var instances=await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync();var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().ToArrayAsync();
            await DeviceEvidence("feature-request-two-submissions.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,firstWait,secondWait,Requests=requests.Length,Instances=instances.Length,Tasks=tasks.Length});
            Assert.Equal(HttpStatusCode.Accepted,a.StatusCode);Assert.Equal(HttpStatusCode.Conflict,b.StatusCode);Assert.Single(requests);Assert.Single(instances);Assert.Single(tasks);Assert.Equal(requests[0].FlowInstanceId,instances[0].Id);Assert.Equal(instances[0].Id,tasks[0].InstanceId);Assert.Empty(await h.Db.WmsFeatureFlags.ToArrayAsync());
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_feature_gate ON public.\"Wf_FlowHistory\"; DROP FUNCTION public.cp6_test_feature_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,FeatureRequestInput()));
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_normal_cycle_preserves_replay_query_withdrawal_and_retired_decisions()
    {
        await using var h=await FeatureRequestSeed();
        if(!await h.Db.Sys_Menus.AnyAsync(m=>m.MenuKey=="oa-inbox"))h.Db.Sys_Menus.Add(new(){MenuId=9074,MenuKey="oa-inbox",MenuName="SIM inbox"});
        await h.Db.SaveChangesAsync();var inbox=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="oa-inbox").Select(m=>m.MenuId).SingleAsync();
        if(!await h.Db.Sys_MenuActions.AnyAsync(a=>a.MenuId==inbox&&a.ActionCode=="approve"))h.Db.Sys_MenuActions.Add(new(){MenuId=inbox,ActionCode="approve",ActionName="Approve"});
        h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=inbox,ActionCode="approve"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var input=FeatureRequestInput();input.WarehouseCd=" w1 ";var stock=await PalletStockRows(h);
        using var first=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));var dto=await FeatureAccepted(first);
        Assert.Equal("W1",dto.WarehouseCd);Assert.Equal(WmsFeatureFlagChangeStatus.Pending,dto.Status);Assert.Equal(h.User,dto.RequestedById);
        using var replay=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));Assert.Equal(dto.Id,(await FeatureAccepted(replay)).Id);
        using var query=await host.Send(OperationsRequest(actor,HttpMethod.Get,FeatureRequestPath+"?warehouseCd=w1&status=pending"));Assert.Equal(HttpStatusCode.OK,query.StatusCode);
        var list=JsonSerializer.Deserialize<List<WmsFeatureFlagChangeDto>>(await query.Content.ReadAsStringAsync(),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;Assert.Equal(dto.Id,Assert.Single(list).Id);
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();using var retired=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wf/task/"+task.Id+"/act",new{approve=true}));
        Assert.Equal(HttpStatusCode.Gone,retired.StatusCode);
        using var cancelled=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath+"/"+dto.Id+"/cancel"));Assert.Equal(HttpStatusCode.NoContent,cancelled.StatusCode);
        using var original=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));Assert.Equal(WmsFeatureFlagChangeStatus.Cancelled,(await FeatureAccepted(original)).Status);
        Assert.Equal(FlowInstanceStatus.Withdrawn,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);Assert.All(await h.Db.Wf_FlowTasks.ToArrayAsync(),t=>Assert.Equal(FlowTaskStatus.Cancelled,t.Status));
        Assert.Empty(await h.Db.WmsFeatureFlags.ToArrayAsync());Assert.Equal(stock,await PalletStockRows(h));
        await DeviceEvidence("feature-request-normal-cycle.json",new{SubmitStatus=(int)first.StatusCode,CancelStatus=(int)cancelled.StatusCode,RetiredDecisionStatus=(int)retired.StatusCode,Requests=await h.Db.WmsFeatureFlagChanges.CountAsync(),Instances=await h.Db.Wf_FlowInstances.CountAsync()});
    }

    [CurrentPermissionPostgresFact]
    public async Task FeatureRequest_target_and_retention_contracts_remain_owned_at_the_request_boundary()
    {
        await using var h=await FeatureRequestSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await FeatureRequestWriteSet(h);
        foreach(var kind in new[]{"retention-low","retention-high","serial-without-move","serial-evidence","invalid-version"})
        {
            var input=FeatureRequestInput();
            if(kind=="retention-low")input.ScanRetentionDays=29;if(kind=="retention-high")input.ScanRetentionDays=3651;
            if(kind=="serial-without-move"){input.ProductionMoveEnabled=false;input.SerialLpnEnabled=true;}
            if(kind=="serial-evidence")input.SerialLpnEnabled=true;if(kind=="invalid-version")input.RowVersion="!!";
            using var invalid=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);Assert.Equal(before,await FeatureRequestWriteSet(h));
        }
        foreach(var days in new[]{30,3650})
        {
            var input=FeatureRequestInput();input.ScanRetentionDays=days;using var accepted=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath,input));var dto=await FeatureAccepted(accepted);Assert.Equal(days,dto.TargetScanRetentionDays);
            using var cancelled=await host.Send(OperationsRequest(actor,HttpMethod.Post,FeatureRequestPath+"/"+dto.Id+"/cancel"));Assert.Equal(HttpStatusCode.NoContent,cancelled.StatusCode);
        }
        Assert.Empty(await h.Db.WmsFeatureFlags.ToArrayAsync());
    }

    private static Task<string> FeatureRequestWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Requests',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsFeatureFlagChange" s WHERE s."TenantId"={h.Tenant}),
          'Flags',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsFeatureFlag" s WHERE s."TenantId"={h.Tenant}),
          'Instances',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_FlowInstance" s WHERE s."TenantId"={h.Tenant}),
          'Tasks',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_FlowTask" s WHERE s."TenantId"={h.Tenant}),
          'Tokens',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_FlowToken" s WHERE s."TenantId"={h.Tenant}),
          'History',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_FlowHistory" s WHERE s."TenantId"={h.Tenant}),
          'Forms',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_FlowFormTo" s WHERE s."TenantId"={h.Tenant}),
          'Jobs',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."Wf_ServiceJob" s WHERE s."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
