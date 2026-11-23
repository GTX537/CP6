using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string Notices = "api/oa/v2/notifications";
    private const string NoticePreferences = "api/oa/v2/notification-preferences";

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_preferences_keep_legacy_bits_and_top_level_keys_under_concurrent_commands()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await NoticePermissions(h);
        const string legacy = """{"pageSize":35,"rowMode":"expanded","notify":{"todo":false,"approved":true,"rejected":true,"email":false}}""";
        await new PrefService(h.Db).SaveAsync(h.User, legacy);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, ConnSettings());
        var cookies = await Login(host,h);
        var previous = await NoticeData(host,cookies,HttpMethod.Get,NoticePreferences);
        var oldVersion = previous.GetProperty("version").GetGuid();
        var effective = previous.GetProperty("effectivePrefs");
        Assert.False(effective.GetProperty("todoCreated").GetProperty("inApp").GetBoolean());
        Assert.True(effective.GetProperty("flowApproved").GetProperty("inApp").GetBoolean());
        Assert.False(effective.GetProperty("flowApproved").GetProperty("email").GetBoolean());
        var timeout = Assert.Single(previous.GetProperty("support").EnumerateArray(), r => r.GetProperty("typeKey").GetString() == "timeout");
        Assert.False(timeout.GetProperty("inAppSupported").GetBoolean());
        Assert.False(timeout.GetProperty("emailSupported").GetBoolean());

        var one = PreferenceEdit("left", oldVersion, """{"notify":{"todoCreated":{"inApp":true,"email":false}}}""");
        var two = PreferenceEdit("right", oldVersion, """{"notify":{"todoCreated":{"inApp":false,"email":true}}}""");
        var replies = await Task.WhenAll(host.Send(OperationsRequest(cookies,HttpMethod.Put,NoticePreferences,one)),
            host.Send(OperationsRequest(cookies,HttpMethod.Put,NoticePreferences,two)));
        using var left = replies[0];
        using var right = replies[1];
        Assert.Equal(new[]{ HttpStatusCode.OK,HttpStatusCode.PreconditionFailed }, replies.Select(r => r.StatusCode).Order());
        var winner = left.IsSuccessStatusCode ? one : two;
        var receipt = await NoticeData(host,cookies,HttpMethod.Put,NoticePreferences,winner);
        var replay = await NoticeData(host,cookies,HttpMethod.Get,NoticePreferences+"/commands/"+winner.commandId);
        Assert.Equal(receipt.GetRawText(),replay.GetRawText());
        var currentVersion = replay.GetProperty("result").GetProperty("version").GetGuid();
        Assert.NotEqual(oldVersion,currentVersion);
        using var conflict = await host.Send(OperationsRequest(cookies,HttpMethod.Put,NoticePreferences,
            PreferenceEdit(winner.commandId,oldVersion,"""{"notify":null}""")));
        Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);

        foreach (var patch in new[] { """{"notify":[]}""", """{"notify":{"made-up":{"email":true}}}""",
            """{"notify":{"todoCreated":{"sms":true}}}""", """{"notify":{"todoCreated":{"inApp":"true"}}}""" })
        {
            using var rejected = await host.Send(OperationsRequest(cookies,HttpMethod.Put,NoticePreferences,
                PreferenceEdit(Guid.NewGuid().ToString("N"),currentVersion,patch)));
            Assert.Equal(HttpStatusCode.UnprocessableEntity,rejected.StatusCode);
        }
        using var oversized = await host.Send(OperationsRequest(cookies,HttpMethod.Put,NoticePreferences,
            PreferenceEdit("long",currentVersion,JsonSerializer.Serialize(new { notify=new { todoCreated=new{email=true} }, metadata=new string('x',40000) }))));
        Assert.Contains(oversized.StatusCode,new[]{ HttpStatusCode.RequestEntityTooLarge,HttpStatusCode.BadRequest });
        Assert.Equal(1,await NoticeReceiptCount(h));
        using var invalidLegacy = await host.Send(OperationsRequest(cookies,HttpMethod.Post,"api/oa/pref/save",new { prefsJson="{", merge=true }));
        Assert.Equal(HttpStatusCode.BadRequest,invalidLegacy.StatusCode);

        var reset = await NoticeData(host,cookies,HttpMethod.Put,NoticePreferences,PreferenceEdit("reset",currentVersion,"""{"notify":null}"""));
        var cell = reset.GetProperty("result").GetProperty("effectivePrefs").GetProperty("todoCreated");
        Assert.True(cell.GetProperty("inApp").GetBoolean());
        Assert.True(cell.GetProperty("email").GetBoolean());
        using var merged = JsonDocument.Parse(await new PrefService(h.Db).GetAsync(h.User));
        Assert.Equal(35,merged.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal("expanded",merged.RootElement.GetProperty("rowMode").GetString());
        Assert.False(merged.RootElement.TryGetProperty("notify",out _));
        Assert.Equal(2,await NoticeReceiptCount(h));

        await using (var fixture = server.Context(new TenantContext { CurrentTenantId=h.Tenant }))
            await fixture.Wf_InboxPrefs.Where(p => p.UserId==h.User).ExecuteUpdateAsync(s => s.SetProperty(p => p.PrefsJson,"{"));
        var malformed = await NoticeData(host,cookies,HttpMethod.Get,NoticePreferences);
        Assert.Equal("LegacyInvalidDefaulted",malformed.GetProperty("readState").GetString());
        Assert.True(malformed.GetProperty("effectivePrefs").GetProperty("todoCreated").GetProperty("email").GetBoolean());
        Assert.Equal("{",await new PrefService(h.Db).GetAsync(h.User));
        Assert.Equal(2,await NoticeReceiptCount(h));
        output.WriteLine("Legacy flat/matrix compatibility, scoped WFC original, one concurrent winner, no rejected receipt, top-level preservation, notify:null reset and read-only malformed fallback.");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_atomic_watermark_ignores_later_commits_and_other_actual_recipients()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await NoticePermissions(h);
        await using var f = await Seed();
        await RegisterScope(f);
        await NoticePermissions(f);
        var early = new[] { NewNotice(h.User),NewNotice(h.User) };
        h.Db.Wf_Notifications.AddRange(early);
        var hidden = NewNotice(h.User);
        hidden.InAppRequested = false;
        hidden.EmailRequested = true;
        h.Db.Wf_Notifications.Add(hidden);
        f.Db.Wf_Notifications.Add(NewNotice(f.User));
        await h.Db.SaveChangesAsync();
        await f.Db.SaveChangesAsync();

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,ConnSettings());
        var cookies = await Login(host,h);
        var foreign = await Login(host,f);
        await using var writer = server.Context(new TenantContext { CurrentTenantId=h.Tenant });
        await using var transaction = await writer.Database.BeginTransactionAsync();
        var late = NewNotice(h.User);
        late.CreateDate = DateTime.Now.AddDays(-1);
        writer.Wf_Notifications.Add(late);
        await writer.SaveChangesAsync();
        var page = await NoticeData(host,cookies,HttpMethod.Get,Notices+"?take=1");
        var watermark = page.GetProperty("watermark").GetString()!;
        var cursor = page.GetProperty("nextCursor").GetString()!;
        var first = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("您有新的待办",first.GetProperty("title").GetString());
        Assert.DoesNotContain("synthetic-private-subject",first.GetRawText());
        await transaction.CommitAsync();

        using var request = OperationsRequest(foreign,HttpMethod.Get,Notices);
        request.Headers.TryAddWithoutValidation("X-Acting-As",h.User.ToString("D"));
        using var foreignList = await host.Send(request);
        Assert.Equal(HttpStatusCode.OK,foreignList.StatusCode);
        var only = Assert.Single((await foreignList.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(only.GetProperty("id").GetGuid(),early.Select(x => x.Id));

        var next = await NoticeData(host,cookies,HttpMethod.Get,Notices+"?take=1&cursor="+Uri.EscapeDataString(cursor));
        var second = Assert.Single(next.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(early.Select(x => x.Id).Order(),new[]{ first.GetProperty("id").GetGuid(),second }.Order());
        using var foreignWatermark = await host.Send(OperationsRequest(foreign,HttpMethod.Post,Notices+"/read-through",new{watermark}));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,foreignWatermark.StatusCode);
        Assert.Equal(2,(await NoticeData(host,cookies,HttpMethod.Post,Notices+"/read-through",new{watermark})).GetProperty("changed").GetInt32());
        Assert.Equal(0,(await NoticeData(host,cookies,HttpMethod.Post,Notices+"/read-through",new{watermark})).GetProperty("changed").GetInt32());
        var firstId = first.GetProperty("id").GetGuid();
        var firstRead = (await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==firstId)).ReadAt;
        Assert.NotNull(firstRead);
        Assert.Equal(0,(await NoticeData(host,cookies,HttpMethod.Post,Notices+"/"+firstId+"/read",new{})).GetProperty("changed").GetInt32());
        Assert.Equal(firstRead,(await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==firstId)).ReadAt);
        Assert.False((await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==late.Id)).IsRead);
        Assert.False((await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==hidden.Id)).IsRead);

        const string legacyList = "api/oa/notification/list?unreadOnly=true";
        const string legacyReadAll = "api/oa/notification/read-all";
        using var firstPage = await host.Send(OperationsRequest(cookies,HttpMethod.Get,legacyList));
        var firstObservation = await firstPage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK,firstPage.StatusCode);
        Assert.Equal(late.Id,Assert.Single(firstObservation.GetProperty("data").EnumerateArray()).GetProperty("id").GetGuid());
        var firstWatermark = firstObservation.GetProperty("watermark").GetString()!;
        Assert.False(firstPage.Headers.TryGetValues("Set-Cookie",out var setCookies) &&
            setCookies.Any(c=>c.StartsWith("cp6_oa_notice_observed=",StringComparison.Ordinal)));

        var betweenPages = NewNotice(h.User);
        h.Db.Wf_Notifications.Add(betweenPages);
        await h.Db.SaveChangesAsync();
        using var secondPage = await host.Send(OperationsRequest(cookies,HttpMethod.Get,legacyList));
        var secondObservation = await secondPage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK,secondPage.StatusCode);
        Assert.Equal(2,secondObservation.GetProperty("data").GetArrayLength());
        var secondWatermark = secondObservation.GetProperty("watermark").GetString()!;
        var afterBoth = NewNotice(h.User);
        h.Db.Wf_Notifications.Add(afterBoth);
        await h.Db.SaveChangesAsync();

        using var unbound = await host.Send(OperationsRequest(cookies,HttpMethod.Post,legacyReadAll));
        Assert.Equal((HttpStatusCode)428,unbound.StatusCode);
        Assert.Equal("NOTIFICATION_WATERMARK_REQUIRED",(await unbound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        using var fromFirst = await host.Send(OperationsRequest(cookies,HttpMethod.Post,legacyReadAll,new{watermark=firstWatermark}));
        Assert.Equal(HttpStatusCode.OK,fromFirst.StatusCode);
        var firstLegacyReadAt = (await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==late.Id)).ReadAt;
        Assert.NotNull(firstLegacyReadAt);
        Assert.False((await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==betweenPages.Id)).IsRead);
        using var firstReplay = await host.Send(OperationsRequest(cookies,HttpMethod.Post,legacyReadAll,new{watermark=firstWatermark}));
        Assert.Equal(HttpStatusCode.OK,firstReplay.StatusCode);
        Assert.Equal(firstLegacyReadAt,(await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==late.Id)).ReadAt);

        using var fromSecond = await host.Send(OperationsRequest(cookies,HttpMethod.Post,legacyReadAll,new{watermark=secondWatermark}));
        Assert.Equal(HttpStatusCode.OK,fromSecond.StatusCode);
        Assert.NotNull((await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==betweenPages.Id)).ReadAt);
        Assert.False((await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x=>x.Id==afterBoth.Id)).IsRead);
        Assert.False((await f.Db.Wf_Notifications.AsNoTracking().SingleAsync()).IsRead);
        output.WriteLine("同一真实登录的两页面各自使用列表水位。页面一只读旧通知，重放保留原 ReadAt；页面二只到自己的观察点，之后提交及其他租户不变。无水位返回 428。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_Form_and_APP_outboxes_record_commit_snapshot_and_keep_provider_outcomes_separate()
    {
        var hook = new StartWriteHook();
        await using var h = await Seed(hook);
        var f = await NativeFormSetup(h);
        await NoticePermissions(h);
        await h.Db.Sys_Users.Where(u => u.Id==h.User).ExecuteUpdateAsync(s => s.SetProperty(u => u.Email,"form-recipient@example.test"));
        var initial = await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n => n.TaskId == f.Task);
        Assert.Equal(1, await NoticeSourceCount(h,initial.Id));
        var version = await h.Db.Database.SqlQuery<Guid>($"""
            SELECT preference_version AS "Value" FROM cp6_oa_notification_v1.owner_sources WHERE notification_id={initial.Id}
            """).SingleAsync();
        Assert.Equal(WorkflowNotificationPreferencesV1.DefaultVersion,version);

        var mail = new NoticeMailbox(true) { Result = WorkflowNotificationEmailOutcomeV1.RetryableFailure };
        var refreshes = new List<(Guid,Guid,Guid)>();
        Task Invalidate(Guid tenant,Guid environment,Guid recipient,CancellationToken ct)
        { refreshes.Add((tenant,environment,recipient)); return Task.CompletedTask; }
        var delivery = new WorkflowNotificationDeliveryService(h.Db,ScopeFence(h),[mail]);
        await delivery.ScanAsync(Invalidate,default);
        Assert.Equal("ACCEPTED_BY_PROVIDER",await NoticeState(h,initial.Id,"IN_APP"));
        Assert.Equal("RETRYABLE_FAILED",await NoticeState(h,initial.Id,"EMAIL"));
        Assert.Equal(2,refreshes.Count);
        Assert.All(refreshes,row=>Assert.Equal((h.Tenant,ScopeEnvironment,h.User),row));

        await NoticeDue(h,initial.Id);
        mail.Result = WorkflowNotificationEmailOutcomeV1.Unknown;
        await delivery.ScanAsync(Invalidate,default);
        Assert.Equal("UNKNOWN",await NoticeState(h,initial.Id,"EMAIL"));
        Assert.Equal(2,mail.Sends.Count);
        Assert.Single(mail.Sends.Select(m => m.DeliveryKey).Distinct());
        Assert.All(mail.Sends,m => Assert.Equal("form-recipient@example.test",m.Address));
        Assert.Equal(2,refreshes.Count);

        await NoticeDue(h,initial.Id);
        mail.Result = WorkflowNotificationEmailOutcomeV1.Accepted;
        await delivery.ScanAsync(Invalidate,default);
        Assert.Equal("ACCEPTED_BY_PROVIDER",await NoticeState(h,initial.Id,"EMAIL"));
        Assert.Equal(mail.Sends[0].DeliveryKey,Assert.Single(mail.Lookups));
        Assert.Equal(2,mail.Sends.Count);
        Assert.Equal(2,refreshes.Count);
        Assert.False(await h.Db.Wf_Notifications.AsNoTracking().AnyAsync(n=>n.IsRead));

        var task = await f.Owner.TaskContextAsync(f.Task);
        var approval = NativeVote(task,"notification-atomic","approve");
        hook.Before = sql =>
        {
            if (!sql.CommandText.Contains("INSERT INTO cp6_personal_form_v2.outbox",StringComparison.Ordinal)) return Task.CompletedTask;
            hook.Before = null;
            throw new InvalidOperationException("SYNTHETIC_NOTIFICATION_WRITE_FAILURE");
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => DecideNative(f.Owner,f.Task,approval));
        Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(2,await h.Db.Wf_Notifications.CountAsync());
        Assert.False(await h.Db.Wf_Notifications.AnyAsync(n=>n.Type==WfNotificationType.FlowApproved));
        await using var parallelDb = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var parallelOwner = PersonalForm(h with { Db = parallelDb });
        var completions = await Task.WhenAll(DecideNative(f.Owner,f.Task,approval),DecideNative(parallelOwner,f.Task,approval));
        Assert.Equal(completions[0],completions[1]);
        var terminal = await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n=>n.Type==WfNotificationType.FlowApproved);
        Assert.Equal(1,await NoticeSourceCount(h,terminal.Id));
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        var failing = new NoticeMailbox(false) { Result=WorkflowNotificationEmailOutcomeV1.Unknown };
        var noLookup = new WorkflowNotificationDeliveryService(h.Db,ScopeFence(h),[failing]);
        await noLookup.ScanAsync(Invalidate,default);
        await noLookup.ScanAsync(Invalidate,default);
        Assert.Equal("UNKNOWN",await NoticeState(h,terminal.Id,"EMAIL"));
        Assert.Single(failing.Sends);
        Assert.Empty(failing.Lookups);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
        Assert.Single((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes);

        await using var app = await Seed();
        var (_,actor,_) = await DecisionSetup(app);
        await NativeOperationPermissions(app);
        var reviewer = Guid.NewGuid();
        app.Db.Sys_Users.Add(new() { Id=reviewer,UserName="synthetic-notice-reviewer",Password="fixture",RoleId=73 });
        await app.Db.SaveChangesAsync();
        await TaskPermission(app,reviewer);
        app.Db.ChangeTracker.Clear();
        var principal = DecisionActor(app,actor);
        var owner = Decisions(principal);
        var input = await DecisionInput(principal);
        var before = await owner.ContextAsync(input.Task);
        var raw = PersonalCommand(new NativeTransferInputV2(input.Task,reviewer,"合成任务转交",before.TaskRowVersion),
            "oa-notification-transfer",before.InstanceRowVersion);
        var transfer = await owner.TransferTaskAsync(input.Task,raw);
        Assert.Equal(ApprovalCanonicalV2.Bytes(transfer),ApprovalCanonicalV2.Bytes(await owner.TransferTaskAsync(input.Task,raw)));
        var appNotice = await app.Db.Wf_Notifications.AsNoTracking().SingleAsync(n => n.TaskId==input.Task && n.UserId==reviewer);
        Assert.Equal(reviewer,appNotice.UserId);
        Assert.Equal(1,await NoticeSourceCount(app,appNotice.Id));
        Assert.Equal(0,await app.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_notification_v1.deliveries WHERE tenant_id={app.Tenant}
            """).SingleAsync());
        output.WriteLine("FORM assignment/decision and APP transfer are real Owner transactions. Original replay adds zero notifications. In-app accepted once; email failure and recovery share one delivery key; unknown without lookup is held.");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_dispatch_blocks_missing_address_and_suppresses_current_preference_without_business_writes()
    {
        await using var h = await Seed();
        var f = await NativeFormSetup(h);
        await NoticePermissions(h);
        var pending = await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n => n.TaskId == f.Task);
        var fake = new NoticeMailbox(true);
        var delivery = new WorkflowNotificationDeliveryService(h.Db,ScopeFence(h),[fake]);
        await new PrefService(h.Db).SaveMergeAsync(h.User,"""{"notify":{"todoCreated":{"inApp":false,"email":true},"formSubmitted":{"inApp":false}}}""");
        await delivery.ScanAsync((_,_,_,_) => throw new InvalidOperationException("Suppressed notice must not show an online hint"),default);
        Assert.Equal("SUPPRESSED",await NoticeState(h,pending.Id,"IN_APP"));
        Assert.Equal("BLOCKED_ADDRESS",await NoticeState(h,pending.Id,"EMAIL"));
        Assert.Empty(fake.Sends);
        Assert.False(await h.Db.Wf_Notifications.AsNoTracking().AnyAsync(n=>n.IsRead));
        Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x=>x.Id==f.Task)).Status);
        Assert.Empty((await NativeInbox(h,f.Owner).DetailAsync(f.Instance)).Votes);

        await new PrefService(h.Db).SaveMergeAsync(h.User,"""{"notify":null}""");
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,"synthetic-new@example.test"));
        var context = await f.Owner.ContextAsync("personal");
        var submitted = PersonalResult<PersonalFormSubmissionV2>(await f.Owner.SubmitDirectAsync("personal",
            PersonalCommand(PersonalDirect(context,"{\"a\":\"second fixture\"}"),"notice-contact-change")));
        var second = await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n=>n.InstanceId==submitted.FlowInstanceId && n.TaskId!=null);
        fake.Result = WorkflowNotificationEmailOutcomeV1.RetryableFailure;
        await delivery.ScanAsync((_,_,_,_) => Task.CompletedTask,default);
        Assert.Equal("RETRYABLE_FAILED",await NoticeState(h,second.Id,"EMAIL"));
        await NoticeDue(h,second.Id);
        await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,(string?)null));
        await delivery.ScanAsync((_,_,_,_) => Task.CompletedTask,default);
        Assert.Equal("BLOCKED_ADDRESS",await NoticeState(h,second.Id,"EMAIL"));
        Assert.Single(fake.Sends);

        var additional = PersonalResult<PersonalFormSubmissionV2>(await f.Owner.SubmitDirectAsync("personal",
            PersonalCommand(PersonalDirect(context,"{\"a\":\"third fixture\"}"),"notice-revoked")));
        var third = await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(n=>n.InstanceId==additional.FlowInstanceId && n.TaskId!=null);
        var menu = await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="oa-inbox");
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId && x.RoleId==72 && x.ActionCode=="read").ExecuteDeleteAsync();
        await delivery.ScanAsync((_,_,_,_) => throw new InvalidOperationException("Revoked reader must not receive an online hint"),default);
        Assert.Equal("SUPPRESSED",await NoticeState(h,third.Id,"IN_APP"));
        Assert.Equal("SUPPRESSED",await NoticeState(h,third.Id,"EMAIL"));
        Assert.Single(fake.Sends);
        Assert.Equal(3,await h.Db.Wf_FlowInstances.CountAsync(n=>n.Status==FlowInstanceStatus.Running));
        output.WriteLine("Queued optional delivery reflects current preference and revoked read action. No address is BLOCKED_ADDRESS. There is no new provider call after missing address or revocation, and no user read or approval vote.");
    }

    private static Wf_Notification NewNotice(Guid user) => new()
    {
        Id=Guid.NewGuid(), UserId=user, Type=WfNotificationType.TodoCreated,
        Title="synthetic-private-subject", Body="synthetic-private-subject", InAppRequested=true
    };

    private static async Task NoticePermissions(Harness h)
    {
        await EngineeringAction(h,"oa-inbox","read");
        await EngineeringAction(h,"oa-settings","edit");
        foreach (var key in new[]{"oa-inbox","oa-settings"})
        {
            var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey==key);
            if (!await h.Db.Sys_RoleMenus.AnyAsync(x=>x.MenuId==menu.MenuId&&x.RoleId==72))
                h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=menu.MenuId});
        }
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task<JsonElement> NoticeData(AuthenticationApiProcess host,Dictionary<string,string> cookies,
        HttpMethod method,string route,object? body=null)
    {
        using var response = await host.Send(OperationsRequest(cookies,method,route,body));
        Assert.True(response.IsSuccessStatusCode,route+": "+response.StatusCode+" "+await OperationsFailure(response));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private sealed record NoticeCommand(string commandId,string expectedVersion,object body);

    private static NoticeCommand PreferenceEdit(string key,Guid expected,string patch) => new(
        key,expected.ToString("D"),
        new{expectedPreferenceVersion=expected,patchMode="TOP_LEVEL_MERGE",patch=JsonSerializer.Deserialize<JsonElement>(patch)});

    private static Task<int> NoticeReceiptCount(Harness h) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM cp6_oa_notification_v1.preference_commands WHERE tenant_id={h.Tenant}
        """).SingleAsync();

    private static Task<int> NoticeSourceCount(Harness h,Guid id) => h.Db.Database.SqlQuery<int>($"""
        SELECT count(*)::int AS "Value" FROM cp6_oa_notification_v1.owner_sources
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND notification_id={id} AND preference_version IS NOT NULL
        """).SingleAsync();

    private static Task<string> NoticeState(Harness h,Guid id,string channel) => h.Db.Database.SqlQuery<string>($"""
        SELECT state AS "Value" FROM cp6_oa_notification_v1.deliveries
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND notification_id={id} AND channel={channel}
        """).SingleAsync();

    private static Task NoticeDue(Harness h,Guid id) => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_oa_notification_v1.deliveries SET next_attempt_at=clock_timestamp()-interval '1 second'
        WHERE tenant_id={h.Tenant} AND notification_id={id} AND channel='EMAIL'
        """);

    private sealed class NoticeMailbox(bool lookup) : IWorkflowNotificationEmailV1
    {
        public bool SupportsOriginalKeyAndLookup { get; } = lookup;
        public WorkflowNotificationEmailOutcomeV1 Result { get; set; }
        public List<WorkflowNotificationEmailV1> Sends { get; } = [];
        public List<string> Lookups { get; } = [];

        public Task<WorkflowNotificationEmailOutcomeV1> SendAsync(WorkflowNotificationEmailV1 message,CancellationToken ct)
        {
            Sends.Add(message);
            return Task.FromResult(Result);
        }

        public Task<WorkflowNotificationEmailOutcomeV1> OriginalAsync(string deliveryKey,CancellationToken ct)
        {
            Lookups.Add(deliveryKey);
            return Task.FromResult(Result);
        }
    }
}
