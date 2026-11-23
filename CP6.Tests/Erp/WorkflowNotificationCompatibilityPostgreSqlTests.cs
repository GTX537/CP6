using System.Collections.Concurrent;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_legacy_deadline_receipts_resume_scoped_online_delivery_without_repeating_old_results()
    {
        await using var first = await Seed();
        await using var second = await Seed();
        var (firstOwner, _, firstInstance, firstTask) = await NoticeCalendarTask(first);
        var (secondOwner, _, secondInstance, secondTask) = await NoticeCalendarTask(second);
        await using var host = await AuthenticationApiProcess.Start(first.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var firstLogin = await Login(host, first);
        var firstApi = new DeadlineApi(host, firstLogin);
        var secondApi = new DeadlineApi(host, await Login(host, second));
        var completed = await RecalculateNotice(first, firstApi, firstOwner, firstInstance, firstTask);
        var uncertain = await RecalculateNotice(first, firstApi, firstOwner, firstInstance, firstTask);
        var pending = await RecalculateNotice(first, firstApi, firstOwner, firstInstance, firstTask);
        var foreign = await RecalculateNotice(second, secondApi, secondOwner, secondInstance, secondTask);

        // 用旧 worker 的正常字段形状模拟已完成和中途退出的历史记录。
        var oldTime = DateTime.UtcNow.AddMinutes(-10);
        await first.Db.Wf_Notifications.Where(x => x.Id == completed.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DispatchStatus, 1).SetProperty(x => x.DispatchAttempts, 1)
            .SetProperty(x => x.DispatchedAtUtc, oldTime).SetProperty(x => x.NextAttemptAtUtc, (DateTime?)null));
        await first.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Wf_Notification" SET "NotificationInsertXid"=NULL
            WHERE "TenantId"={first.Tenant} AND "Id"={completed.Id}
            """);
        await first.Db.Wf_Notifications.Where(x => x.Id == uncertain.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.DispatchStatus, 2).SetProperty(x => x.DispatchAttempts, 1)
            .SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(5)));

        // 继续走既有引擎通知入口。同一个旧生产者原键只生成一条通知。
        var flowKey = (await first.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(x => x.Id == firstInstance)).FlowKey;
        var storage = new NotificationService(first.Db);
        var engine = new PersistentWfNotifier(storage, new PrefService(first.Db));
        await engine.TodoCreatedAsync(first.User, firstInstance, firstTask, flowKey);
        await engine.TodoCreatedAsync(first.User, firstInstance, firstTask, flowKey);
        await storage.CreateAsync(first.User, WfNotificationType.TodoCreated, "历史样本", "历史样本", firstInstance, firstTask, flowKey);
        await first.Db.SaveChangesAsync();
        var oldKey = $"1:{firstInstance:N}:{firstTask:N}:{first.User:N}:-";
        var engineNotice = await first.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.EventKey == oldKey);
        var unproven = await first.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.Title == "历史样本");

        var sourceKinds = await first.Db.Database.SqlQuery<string>($"""
            SELECT kind AS "Value" FROM cp6_oa_notification_v1.owner_sources
            WHERE (tenant_id,environment_id) = ({first.Tenant},{ScopeEnvironment})
              AND notification_id IN ({completed.Id},{uncertain.Id},{pending.Id},{engineNotice.Id},{unproven.Id})
            ORDER BY kind COLLATE "C"
            """).ToListAsync();
        Assert.Equal(new[] { "DEADLINE_RECALCULATED", "DEADLINE_RECALCULATED", "DEADLINE_RECALCULATED",
            "LEGACY_ENGINE_ASSIGNMENT" }, sourceKinds);

        var tenants = new Mock<ITenantEnumerator>();
        tenants.Setup(x => x.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[first.Tenant, second.Tenant]);
        var groups = new ConcurrentQueue<string>();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync("WfNotification", It.Is<object?[]>(args => args.Length == 0), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(It.IsAny<string>())).Returns((string group) =>
        {
            groups.Enqueue(group);
            return proxy.Object;
        });
        var hub = new Mock<IHubContext<NotifyHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var mail = new NoticeMailbox(true);
        await using var services = new ServiceCollection()
            .AddSingleton(tenants.Object)
            .AddScoped<ITenantContext, TenantContext>()
            .AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>()))
            .AddScoped(sp => new ScopeAuthorizationFenceV1(sp.GetRequiredService<CP6Context>(),
                Options.Create(new ScopeAuthorizationOptionsV1 { EnvironmentId = ScopeEnvironment })))
            .AddScoped<WorkflowNotificationDeliveryService>()
            .AddSingleton<IWorkflowNotificationEmailV1>(mail)
            .AddSingleton(hub.Object)
            .BuildServiceProvider();
        var worker = new WfNotificationDispatchWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WfNotificationDispatchWorker>.Instance);
        await Task.WhenAll(worker.DispatchBatchAsync(default), worker.DispatchBatchAsync(default));
        await worker.DispatchBatchAsync(default);

        var firstGroup = NotifyHub.WorkflowRecipientGroup(first.Tenant, ScopeEnvironment, first.User);
        var secondGroup = NotifyHub.WorkflowRecipientGroup(second.Tenant, ScopeEnvironment, second.User);
        Assert.Equal(3, groups.Count(group => group == firstGroup));
        Assert.Equal(2, groups.Count(group => group == secondGroup));
        Assert.Equal(5, groups.Count);
        proxy.Verify(x => x.SendCoreAsync("WfNotification", It.Is<object?[]>(args => args.Length == 0), It.IsAny<CancellationToken>()),
            Times.Exactly(5));
        clients.Verify(x => x.All, Times.Never);
        clients.Verify(x => x.User(It.IsAny<string>()), Times.Never);
        Assert.Empty(mail.Sends);
        Assert.Empty(mail.Lookups);

        Assert.Equal("ACCEPTED_BY_PROVIDER", await NoticeState(first, pending.Id, "IN_APP"));
        Assert.Equal("ACCEPTED_BY_PROVIDER", await NoticeState(second, foreign.Id, "IN_APP"));
        Assert.Equal("ACCEPTED_BY_PROVIDER", await NoticeState(first, engineNotice.Id, "IN_APP"));
        Assert.Equal("UNKNOWN", await NoticeState(first, engineNotice.Id, "EMAIL"));
        Assert.Equal("LEGACY_IN_APP_COMPLETED", await LegacyNoticeReason(first, completed.Id));
        Assert.Equal("LEGACY_IN_APP_OUTCOME_UNKNOWN", await LegacyNoticeReason(first, uncertain.Id));
        Assert.Equal(0, await first.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_notification_v1.deliveries WHERE notification_id={unproven.Id}
            """).SingleAsync());
        Assert.Equal(1, await first.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_oa_notification_v1.deliveries
            WHERE notification_id={pending.Id} AND channel='IN_APP'
            """).SingleAsync());
        Assert.Equal(FlowTaskStatus.Pending, (await first.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(x => x.Id == firstTask)).Status);
        Assert.Empty((await NativeInbox(first, firstOwner).DetailAsync(firstInstance)).Votes);

        var originalStates = await first.Db.Wf_Notifications.AsNoTracking()
            .Where(x => x.Id == completed.Id || x.Id == uncertain.Id).OrderBy(x => x.DispatchStatus)
            .Select(x => new { x.DispatchStatus, x.DispatchAttempts, x.DispatchedAtUtc }).ToArrayAsync();
        Assert.Equal(new[] { 1, 2 }, originalStates.Select(x => x.DispatchStatus));
        Assert.All(originalStates, row => Assert.Equal(1, row.DispatchAttempts));
        Assert.NotNull(originalStates[0].DispatchedAtUtc);
        Assert.Null(originalStates[1].DispatchedAtUtc);

        var list = await NoticeData(host, firstLogin, HttpMethod.Get, Notices);
        var visiblePending = Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == pending.Id);
        Assert.Equal("待办期限已重算", visiblePending.GetProperty("title").GetString());
        Assert.Equal(firstTask, visiblePending.GetProperty("taskId").GetGuid());
        var historical = Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == unproven.Id);
        Assert.Equal(JsonValueKind.Null, historical.GetProperty("taskId").ValueKind);
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == foreign.Id);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            pendingId = pending.Id, completedId = completed.Id, uncertainId = uncertain.Id, foreignId = foreign.Id,
            firstGroupRefreshes = 3, secondGroupRefreshes = 2, providerSends = 0, providerLookups = 0,
            matchedLegacyDeliveryReceipts = 6, unprovenDeliveries = 0, approvalVotes = 0,
        }));
    }

    private async Task<(PersonalFormOwnerV2 Owner, FlowAuthoringOwnerV2 Flow, Guid Instance, Guid Task)> NoticeCalendarTask(Harness h)
    {
        await TimeoutZone(h);
        await CalendarPermissions(h, edit: true, recalculate: true);
        await NoticePermissions(h);
        // 初始原生任务只走站内，后续旧引擎配置单独恢复默认以验证旧邮件不外送。
        await new PrefService(h.Db).SaveMergeAsync(h.User, """{"notify":{"todoCreated":{"inApp":true,"email":false},"formSubmitted":{"inApp":false}}}""");
        var fixture = await NativeFormSetup(h, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutAction = "remind";
            node.TimeoutHours = 24;
            node.TimeoutWorkday = new(1, new TimeOnly(12, 0));
        });
        await new PrefService(h.Db).SaveMergeAsync(h.User, """{"notify":null}""");
        return fixture;
    }

    private async Task<Wf_Notification> RecalculateNotice(Harness h, DeadlineApi api, PersonalFormOwnerV2 owner, Guid instance, Guid task)
    {
        var deadline = Assert.Single((await NativeInbox(h, owner).DetailAsync(instance)).Timeout!.Deadlines);
        var day = DateOnly.FromDateTime(deadline.Policy.LocalDueAt!.Value);
        var year = await api.Data(VersionCalendarApi + "?year=" + day.Year);
        var changed = await api.Data(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = year.GetProperty("revision").GetInt64(),
            date = day, isWorkday = false, note = "合成期限重算",
        });
        var preview = await api.Data(VersionCalendarApi + "/deadline-previews", new
        {
            calendarId = h.Tenant, expectedCalendarRevision = changed.GetProperty("revision").GetInt64(), taskIds = new[] { task },
        });
        var item = Assert.Single(preview.GetProperty("items").EnumerateArray());
        var command = new
        {
            commandId = Guid.NewGuid(), previewId = preview.GetProperty("previewId").GetGuid(), taskId = task,
            expectedDeadlineRevision = deadline.Revision, expectedTaskVersion = item.GetProperty("taskVersion").GetString(),
            expectedInstanceVersion = item.GetProperty("instanceVersion").GetString(), reason = "确认合成新截止",
        };
        var receipt = await api.Data(VersionCalendarApi + "/deadline-recalculations", command);
        Assert.Equal(receipt.GetRawText(), (await api.Data(VersionCalendarApi + "/deadline-recalculations", command)).GetRawText());
        Assert.Equal(deadline.Revision + 1, receipt.GetProperty("after").GetProperty("revision").GetInt64());
        var key = "OA_DEADLINE_RECALCULATED/" + command.commandId.ToString("D");
        return await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.EventKey == key);
    }

    private static Task<string> LegacyNoticeReason(Harness h, Guid notice) => h.Db.Database.SqlQuery<string>($"""
        SELECT reason AS "Value" FROM cp6_oa_notification_v1.deliveries
        WHERE tenant_id={h.Tenant} AND environment_id={ScopeEnvironment} AND notification_id={notice} AND channel='IN_APP'
        """).SingleAsync();
}
