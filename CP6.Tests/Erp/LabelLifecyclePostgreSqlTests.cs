using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> LabelSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuId==461))db.Sys_Menus.Add(new(){MenuId=461,MenuKey="wms-mobile",MenuName="Mobile"});
        foreach(var action in new[]{"label-manage","label-print"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==461&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=461,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=461,ActionCode=action});
        }
        db.WmsFeatureFlags.Add(new(){WarehouseCd="W1",SerialLpnEnabled=true});
        db.ClientDevices.Add(new(){DeviceId=LabelDevice(h),Platform="Windows",Status=ClientDeviceStatus.Active,WarehouseCd="W1"});
        db.LabelTemplates.Add(new(){TemplateName=LabelTemplateName(h),Format="ZPL",TemplateBody="^XA^FDPRIVATE-LABEL-ORIGINAL^FS^XZ",IsEnabled=true,Creator="price-owner"});
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation is "claim" or "complete" or "fail" or "jobs-read")
            foreach(var suffix in new[]{"A","B"})db.LabelJobs.Add(new(){JobNo=LabelJobNo(h,suffix),OperationId=Guid.NewGuid(),TemplateName=LabelTemplateName(h),
                WarehouseCd="W1",PayloadJson="{\"kind\":\"SIMULATED\",\"qty\":\"1.23456789\"}",Status=operation is "complete" or "fail"?LabelJobStatus.Printing:LabelJobStatus.Pending,
                AttemptCount=operation is "complete" or "fail"?1:0,RequestedDeviceId=LabelDevice(h),RequestedBy="price-owner",Creator="price-owner"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string LabelDevice(Harness h)=>"GW"+h.Tenant.ToString("N")[..12];
    private static string LabelTemplateName(Harness h)=>"LT"+h.Tenant.ToString("N");
    private static string LabelJobNo(Harness h,string suffix)=>"LJ"+h.Tenant.ToString("N")[..12]+suffix;
    private static CreateLabelJobRequest LabelCreateInput(Harness h,Guid operation)=>new(){OperationId=operation,WarehouseCd="W1",TemplateName=LabelTemplateName(h),
        DeviceId=LabelDevice(h),PrinterName="MOCK-ONLY",PayloadJson="{\"kind\":\"SIMULATED\",\"qty\":\"1.23456789\"}"};
    private static async Task<LabelJobCommand> LabelCommandInput(Harness h,string suffix)
    {
        var bytes=await h.Db.LabelJobs.Where(j=>j.JobNo==LabelJobNo(h,suffix)).Select(j=>j.RowVersion).SingleAsync();Assert.NotNull(bytes);Assert.NotEmpty(bytes!);
        return new(){OperationId=Guid.NewGuid(),DeviceId=LabelDevice(h),RowVersion=Convert.ToBase64String(bytes!),ResultMessage="SIMULATED-OBSERVATION"};
    }

    [TenantManagementPostgresTheory]
    [InlineData("template")][InlineData("create")][InlineData("claim")][InlineData("complete")][InlineData("fail")][InlineData("jobs-read")][InlineData("templates-read")]
    public async Task Label_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await LabelSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var creates=new Dictionary<string,CreateLabelJobRequest>{{"A",LabelCreateInput(h,Guid.NewGuid())},{"B",LabelCreateInput(h,Guid.NewGuid())}};
        var commands=new Dictionary<string,LabelJobCommand>();
        if(operation is "claim" or "complete" or "fail")foreach(var suffix in new[]{"A","B"})commands[suffix]=await LabelCommandInput(h,suffix);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        var originalResult=await normal.Content.ReadAsStringAsync();
        if(operation is "claim" or "complete" or "fail")
        {
            var row=await db.LabelJobs.AsNoTracking().SingleAsync(j=>j.JobNo==LabelJobNo(h,"A"));
            Assert.Equal(operation=="claim"?LabelJobStatus.Printing:operation=="complete"?LabelJobStatus.Completed:LabelJobStatus.Failed,row.Status);
            Assert.Equal(1,row.AttemptCount);Assert.Equal("price-owner",row.Modifier);Assert.Single(await db.TaskCommandReceipts.Where(r=>r.TaskNo==row.JobNo).ToListAsync());
        }
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await LabelWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            var table=operation is "template" or "templates-read"?"T_LabelTemplate":"T_LabelJob";
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\""+table+"\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("label-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"label-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("label-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await LabelWriteSet(h)});
        Assert.True(denied.StatusCode==HttpStatusCode.Unauthorized,await OperationsFailure(denied));Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await LabelWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await LabelWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("label-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        if(operation is "create" or "claim" or "complete" or "fail")
        {
            using var replay=await Send("A");Assert.True(replay.StatusCode==HttpStatusCode.OK,await OperationsFailure(replay));Assert.Equal(originalResult,await replay.Content.ReadAsStringAsync());
        }
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));
        if(operation is "create" or "claim" or "complete" or "fail")
        {
            using var replay=await Send("B");Assert.Equal(await recovered.Content.ReadAsStringAsync(),await replay.Content.ReadAsStringAsync());
        }

        Task<HttpResponseMessage> Send(string suffix)
        {
            if(operation=="jobs-read")return host.Send(OperationsRequest(actor,HttpMethod.Get,"api/v2/wms/label-jobs?warehouseCd=W1"));
            if(operation=="templates-read")return host.Send(OperationsRequest(actor,HttpMethod.Get,"api/v2/wms/label-jobs/templates"));
            if(operation=="template")return host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/templates",new UpsertLabelTemplateRequest{TemplateName=LabelTemplateName(h)+suffix,Format="zpl",TemplateBody="^XA^FDMOCK-"+suffix+"^FS^XZ"}));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",creates[suffix]));
            return host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+LabelJobNo(h,suffix)+"/"+operation,commands[suffix]));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await LabelSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_LabelJob\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",LabelCreateInput(h,Guid.NewGuid())));
                businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"label-business-wait",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("label-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"label-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("label-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.True(committed.StatusCode==HttpStatusCode.OK,await OperationsFailure(committed));Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.LabelJobs.ToListAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_late_receipt_failure_cannot_create_a_false_printing_receipt()
    {
        await using var h=await LabelSeed("claim");var connection=h.Db.Database.GetConnectionString()!;var command=await LabelCommandInput(h,"A");
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);await using var fault=new NpgsqlConnection(connection);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_label_once;
            CREATE FUNCTION public.cp6_test_label_receipt() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_label_once')=1 THEN RAISE EXCEPTION 'simulated label late receipt failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_label_receipt BEFORE INSERT ON public."T_TaskCommandReceipt" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid AND NEW."CommandName"='label-claim') EXECUTE FUNCTION public.cp6_test_label_receipt();
            """,fault);await install.ExecuteNonQueryAsync();var before=await LabelWriteSet(h);
        try
        {
            using var failed=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+LabelJobNo(h,"A")+"/claim",command));
            await DeviceEvidence("label-receipt-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await LabelWriteSet(h)});
            Assert.True(failed.StatusCode==HttpStatusCode.ServiceUnavailable,await OperationsFailure(failed));Assert.Equal(before,await LabelWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_label_receipt ON public.\"T_TaskCommandReceipt\"; DROP FUNCTION public.cp6_test_label_receipt(); DROP SEQUENCE public.cp6_test_label_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+LabelJobNo(h,"A")+"/claim",command));
        var result=await LabelResponse<LabelJobDto>(restored);Assert.Equal(LabelJobStatus.Printing,result.Status);Assert.Equal(1,result.AttemptCount);Assert.Single(await h.Db.TaskCommandReceipts.ToListAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_create_replay_keeps_original_template_and_rejects_changed_input()
    {
        await using var h=await LabelSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        var input=LabelCreateInput(h,Guid.NewGuid());using var first=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",input));var original=await LabelResponse<LabelJobDto>(first);var bytes=await first.Content.ReadAsStringAsync();
        var template=await h.Db.LabelTemplates.AsNoTracking().SingleAsync();
        using var updated=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/templates",new UpsertLabelTemplateRequest{Id=template.Id,TemplateName=template.TemplateName,Format="ZPL",TemplateBody="^XA^FDCHANGED-LATER^FS^XZ",RowVersion=Convert.ToBase64String(template.RowVersion!)}));
        Assert.True(updated.StatusCode==HttpStatusCode.OK,await OperationsFailure(updated));
        using var replay=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",input));
        await DeviceEvidence("label-create-replay.json",new{Status=(int)replay.StatusCode,Original=bytes,Replayed=await replay.Content.ReadAsStringAsync()});
        Assert.Equal(bytes,await replay.Content.ReadAsStringAsync());var before=await LabelWriteSet(h);
        var changed=LabelCreateInput(h,input.OperationId);changed.PayloadJson="{\"different\":true}";
        using var conflict=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",changed));Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);Assert.Equal(before,await LabelWriteSet(h));
        var claim=new LabelJobCommand{OperationId=Guid.NewGuid(),RowVersion=original.RowVersion,DeviceId=LabelDevice(h)};
        using var claimed=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+original.JobNo+"/claim",claim));
        var print=await LabelResponse<LabelJobDto>(claimed);Assert.Equal(original.TemplateBody,print.TemplateBody);
        before=await LabelWriteSet(h);claim.RowVersion=Convert.ToBase64String(new byte[]{9,9,9});
        using var wrong=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+original.JobNo+"/claim",claim));Assert.Equal(HttpStatusCode.Conflict,wrong.StatusCode);Assert.Equal(before,await LabelWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_replay_requires_the_current_device_and_keeps_old_receipt_bytes()
    {
        await using var h=await LabelSeed("claim");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var input=await LabelCommandInput(h,"A");
        using var first=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+LabelJobNo(h,"A")+"/claim",input));var result=await LabelResponse<LabelJobDto>(first);
        var legacyOperation=Guid.NewGuid();var raw=JsonSerializer.Serialize(result);
        h.Db.TaskCommandReceipts.Add(new(){OperationId=legacyOperation,TaskNo=result.JobNo,CommandName="label-claim",ResultJson=raw});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var legacy=new LabelJobCommand{OperationId=legacyOperation,RowVersion=input.RowVersion,DeviceId=LabelDevice(h)};
        using var history=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+result.JobNo+"/claim",legacy));Assert.Equal(await first.Content.ReadAsStringAsync(),await history.Content.ReadAsStringAsync());
        Assert.Equal(raw,await h.Db.TaskCommandReceipts.Where(r=>r.OperationId==legacyOperation).Select(r=>r.ResultJson).SingleAsync());
        await h.Db.ClientDevices.Where(d=>d.DeviceId==LabelDevice(h)).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Status,ClientDeviceStatus.Disabled));var before=await LabelWriteSet(h);
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs/"+result.JobNo+"/claim",input));
        await DeviceEvidence("label-device-replay.json",new{Status=(int)denied.StatusCode,Before=before,After=await LabelWriteSet(h)});
        Assert.Equal(HttpStatusCode.Conflict,denied.StatusCode);Assert.Contains("WM-LABEL-GATEWAY-NOT-ACTIVE",await denied.Content.ReadAsStringAsync());Assert.Equal(before,await LabelWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_current_action_revocation_wins_before_create()
    {
        await using var h=await LabelSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await LabelWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_LabelJob\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=461 AND "ActionCode"='label-print'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",LabelCreateInput(h,Guid.NewGuid())));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"label-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("label-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await LabelWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await LabelWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Label_gateway_actual_Main_cold_recovery_does_not_print_twice()
    {
        var probe=Environment.GetEnvironmentVariable("CP6_LABEL_GATEWAY_PROBE");Assert.True(!string.IsNullOrWhiteSpace(probe)&&File.Exists(probe),"Compiled actual client probe is required for this scenario.");
        await using var h=await LabelSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/v2/wms/label-jobs",LabelCreateInput(h,Guid.NewGuid())));var job=await LabelResponse<LabelJobDto>(created);
        var root=Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,"gateway-cold");Directory.CreateDirectory(root);
        var user=await h.Db.Sys_Users.Where(u=>u.UserName=="price-owner").Select(u=>u.Id).SingleAsync();
        var first=await RunProbe("lose-completion");Assert.Equal(1,first.GetProperty("PrintCalls").GetInt32());Assert.Equal(0,first.GetProperty("FailCalls").GetInt32());
        var completed=await h.Db.LabelJobs.AsNoTracking().SingleAsync(j=>j.JobNo==job.JobNo);Assert.Equal(LabelJobStatus.Completed,completed.Status);
        var second=await RunProbe("resume");Assert.Equal(1,second.GetProperty("PrintCalls").GetInt32());Assert.Equal(0,second.GetProperty("FailCalls").GetInt32());Assert.Equal("Completed",second.GetProperty("JournalStage").GetString());
        Assert.Equal(first.GetProperty("CompletionOperationId").GetGuid(),second.GetProperty("CompletionOperationId").GetGuid());
        var receipts=await h.Db.TaskCommandReceipts.AsNoTracking().Where(r=>r.TaskNo==job.JobNo).ToListAsync();Assert.Equal(3,receipts.Count);Assert.Equal(1,completed.AttemptCount);
        foreach(var path in Directory.GetFiles(root,"*.json",SearchOption.AllDirectories))
        {
            var saved=await File.ReadAllTextAsync(path);Assert.DoesNotContain("PRIVATE-LABEL-ORIGINAL",saved);Assert.DoesNotContain("SIMULATED",saved);
            Assert.All(actor.Values,value=>Assert.DoesNotContain(value,saved));
        }
        await DeviceEvidence("label-gateway-cold.json",new{First=first,Second=second,Receipts=receipts.Select(r=>new{r.OperationId,r.CommandName}),completed.Status,completed.AttemptCount});

        async Task<JsonElement> RunProbe(string phase)
        {
            var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(probe!);start.Environment["CP6_LABEL_PHASE"]=phase;start.Environment["CP6_LABEL_ORIGIN"]=host.Origin.ToString();start.Environment["CP6_LABEL_TENANT"]=h.Tenant.ToString();start.Environment["CP6_LABEL_ACTOR"]=user.ToString();
            start.Environment["CP6_LABEL_DEVICE"]=LabelDevice(h);start.Environment["CP6_LABEL_COOKIE"]=string.Join("; ",actor.Select(p=>p.Key+"="+p.Value));start.Environment["CP6_LABEL_CSRF"]=actor["cp6_csrf"];start.Environment["CP6_LABEL_ROOT"]=root;
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try{await process.WaitForExitAsync(deadline.Token);}
            finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
            var text=await output;var stderr=await error;Assert.True(process.ExitCode==0,"Actual client probe failed: "+stderr+text);using var document=JsonDocument.Parse(text);return document.RootElement.Clone();
        }
    }

    private static async Task<T> LabelResponse<T>(HttpResponseMessage response)
    {Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));return (await response.Content.ReadFromJsonAsync<T>())!;}
    private static Task<string> LabelWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Jobs',(SELECT coalesce(jsonb_agg(to_jsonb(j) ORDER BY j."Id"),'[]') FROM public."T_LabelJob" j WHERE j."TenantId"={h.Tenant}),
          'Templates',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_LabelTemplate" t WHERE t."TenantId"={h.Tenant}),
          'Receipts',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."T_TaskCommandReceipt" r WHERE r."TenantId"={h.Tenant}),
          'Devices',(SELECT coalesce(jsonb_agg(to_jsonb(d) ORDER BY d."Id"),'[]') FROM public."T_ClientDevice" d WHERE d."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
