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
using Moq;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const int HeldDispatch = 3;

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_forward_migration_keeps_one_original_claim_during_overlap_and_restart()
    {
        const string baseline = "20261123132700_LegacyFormHistoryOriginV2";
        var database = await server.CreateIsolatedDatabaseAsync(baseline);
        await using var first = await SeedForDatabase(database);
        await using var second = await SeedForDatabase(database);
        var refreshed = new ConcurrentQueue<(Guid Notice, Guid User)>();
        var mail = new ConcurrentQueue<string>();
        await first.Db.Sys_Users.Where(x => x.Id == first.User).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.Email, "notification-fixture@example.test"));

        var completed = await QueueOriginalNotice(first, "完成且已读");
        await new NotificationService(first.Db).MarkReadAsync(first.User, completed.Id);
        await using var before = OriginalWorker(database, refreshed, mail);
        await Poll(before);
        var confirmed = await ReadOriginalNotice(first, completed.Id);
        Assert.Equal(1, confirmed.DispatchStatus);
        Assert.Equal(1, confirmed.DispatchAttempts);
        Assert.NotNull(confirmed.DispatchedAtUtc);
        Assert.NotNull(confirmed.ReadAt);

        var oldFailure = await QueueOriginalNotice(first, "迁移前结果未知", emailRequested: true);
        await using (var failedWorker = OriginalWorker(database, refreshed, mail, (id, _) =>
        {
            Assert.Equal(oldFailure.Id, id);
            return Task.FromException(new IOException("本地刷新替身未能确认原结果"));
        }))
        {
            await Poll(failedWorker);
        }
        var failedBefore = await ReadOriginalNotice(first, oldFailure.Id);
        Assert.Equal(0, failedBefore.DispatchStatus);
        Assert.Equal(1, failedBefore.DispatchAttempts);
        // 旧二进制在下一轮退避之前不会重新领取。此处保留已验证的原尝试，再开始前滚。
        await first.Db.Wf_Notifications.Where(x => x.Id == oldFailure.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddHours(1)));

        var oldAttempt = await QueueOriginalNotice(first, "原版本在途");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var old = OriginalWorker(database, refreshed, mail, (id, ct) =>
        {
            Assert.Equal(oldAttempt.Id, id);
            started.TrySetResult();
            return finish.Task.WaitAsync(ct);
        });
        var inFlight = Poll(old);
        Wf_Notification pending;
        Wf_Notification rolling;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var beforeMigration = await ReadOriginalNotice(first, oldAttempt.Id);
            Assert.Equal(2, beforeMigration.DispatchStatus);
            Assert.Equal(1, beforeMigration.DispatchAttempts);
            pending = await QueueOriginalNotice(second, "旧版未尝试的可信待派");
            await first.Db.Database.MigrateAsync();
            Assert.Empty(await first.Db.Database.GetPendingMigrationsAsync());
            Assert.Equal(0, (await ReadOriginalNotice(second, pending.Id)).DispatchStatus);
            Assert.Equal(HeldDispatch, (await ReadOriginalNotice(first, oldAttempt.Id)).DispatchStatus);
            var heldOldFailure = await ReadOriginalNotice(first, oldFailure.Id);
            Assert.Equal(HeldDispatch, heldOldFailure.DispatchStatus);
            Assert.Equal(failedBefore.LastDispatchError, heldOldFailure.LastDispatchError);
            Assert.Null(heldOldFailure.DispatchedAtUtc);
            Assert.Equal(1, heldOldFailure.DispatchAttempts);
            Assert.Equal(confirmed.ReadAt, (await ReadOriginalNotice(first, completed.Id)).ReadAt);
            Assert.Equal(confirmed.DispatchedAtUtc, (await ReadOriginalNotice(first, completed.Id)).DispatchedAtUtc);

            rolling = await QueueOriginalNotice(first, "新版写入");
            var oldWriterReplay = await QueueOriginalNotice(second, "旧版未尝试的可信待派");
            Assert.Equal(pending.Id, oldWriterReplay.Id);
            await using var rollingReplica = OriginalWorker(database, refreshed, mail);
            await using var concurrentReplica = OriginalWorker(database, refreshed, mail);
            await Task.WhenAll(Poll(rollingReplica), Poll(concurrentReplica));
            Assert.Equal(1, (await ReadOriginalNotice(second, pending.Id)).DispatchStatus);
            Assert.Equal(1, (await ReadOriginalNotice(first, rolling.Id)).DispatchStatus);
            Assert.Equal(HeldDispatch, (await ReadOriginalNotice(first, oldAttempt.Id)).DispatchStatus);
        }
        finally
        {
            finish.TrySetResult();
            await inFlight;
        }
        // 旧进程完成它唯一已开始的原领取，仍可落下原确认。
        var lateReceipt = await ReadOriginalNotice(first, oldAttempt.Id);
        Assert.Equal(1, lateReceipt.DispatchStatus);
        Assert.Equal(1, lateReceipt.DispatchAttempts);
        Assert.NotNull(lateReceipt.DispatchedAtUtc);

        var newFailure = await QueueOriginalNotice(first, "新版本地失败后重启", emailRequested: true);
        await using (var failedReplica = OriginalWorker(database, refreshed, mail, (id, _) =>
        {
            Assert.Equal(newFailure.Id, id);
            return Task.FromException(new IOException("替身接受结果未知"));
        }))
        {
            await Poll(failedReplica);
        }
        var heldNewFailure = await ReadOriginalNotice(first, newFailure.Id);
        Assert.Equal(HeldDispatch, heldNewFailure.DispatchStatus);
        Assert.Equal(1, heldNewFailure.DispatchAttempts);
        Assert.Equal("dispatch-failed", heldNewFailure.LastDispatchError);

        var interruptedNotice = await QueueOriginalNotice(first, "本地实例中断且没有回执", emailRequested: true);
        using var shutdown = new CancellationTokenSource();
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var interrupted = OriginalWorker(database, refreshed, mail, (id, ct) =>
        {
            Assert.Equal(interruptedNotice.Id, id);
            attempted.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }))
        {
            var interruptedPoll = Poll(interrupted, shutdown.Token);
            try
            {
                await attempted.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(HeldDispatch, (await ReadOriginalNotice(first, interruptedNotice.Id)).DispatchStatus);
            }
            finally
            {
                shutdown.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interruptedPoll);
            }
        }
        Assert.Null((await ReadOriginalNotice(first, interruptedNotice.Id)).LastDispatchError);

        // 即使到达旧退避时间，兼容进程也只领取未尝试过的业务通知。
        var heldIds = new[] { oldFailure.Id, newFailure.Id, interruptedNotice.Id };
        await first.Db.Wf_Notifications.Where(x => heldIds.Contains(x.Id)).ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        var afterRestart = await QueueOriginalNotice(second, "回退兼容的旧写入");
        await using var restarted = OriginalWorker(database, refreshed, mail);
        await Task.WhenAll(Poll(restarted), Poll(before));
        await Poll(restarted);

        var expected = new[] { (completed.Id,first.User), (oldFailure.Id,first.User), (oldAttempt.Id,first.User),
            (pending.Id,second.User), (rolling.Id,first.User), (newFailure.Id,first.User), (interruptedNotice.Id,first.User),
            (afterRestart.Id,second.User) };
        Assert.Equal(expected.OrderBy(x => x.Id), refreshed.Select(x => (Id:x.Notice,x.User)).OrderBy(x => x.Id));
        Assert.Empty(mail);
        foreach (var id in heldIds)
        {
            var row = await ReadOriginalNotice(first, id);
            Assert.Equal(HeldDispatch, row.DispatchStatus);
            Assert.Equal(1, row.DispatchAttempts);
            Assert.Null(row.DispatchedAtUtc);
            Assert.False(row.IsRead);
        }
        var recovered = await ReadOriginalNotice(second, afterRestart.Id);
        Assert.Equal(1, recovered.DispatchStatus);
        Assert.Equal(1, recovered.DispatchAttempts);
        Assert.Equal(confirmed.ReadAt, (await ReadOriginalNotice(first, completed.Id)).ReadAt);
        Assert.Equal(confirmed.DispatchedAtUtc, (await ReadOriginalNotice(first, completed.Id)).DispatchedAtUtc);
        Assert.True(await first.Db.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('cp6_oa_notification_v1.deliveries') IS NULL AS \"Value\"").SingleAsync());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            baseline, initialCompleted = completed.Id, completedDuringOverlap = pending.Id,
            previousInFlight = oldAttempt.Id, beforeFailure = oldFailure.Id, afterFailure = newFailure.Id,
            interrupted = interruptedNotice.Id, afterRestart = recovered.Id, attempts = refreshed.Count,
            repeatedAttempts = 0, held = heldIds.Length,
            confirmed = 5, mails = mail.Count, originalReadAt = confirmed.ReadAt,
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowNotice_existing_deadline_recalculation_remains_on_the_original_site_path()
    {
        var database = await server.CreateIsolatedDatabaseAsync();
        await using var first = await SeedForDatabase(database);
        await using var second = await SeedForDatabase(database);
        var (firstOwner, _, firstInstance, firstTask) = await NoticeCalendarTask(first);
        var (secondOwner, _, secondInstance, secondTask) = await NoticeCalendarTask(second);
        await using var host = await AuthenticationApiProcess.Start(first.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var firstLogin = await Login(host, first);
        var firstReceipt = await RecalculateNotice(first, new DeadlineApi(host, firstLogin), firstOwner, firstInstance, firstTask);
        var secondReceipt = await RecalculateNotice(second, new DeadlineApi(host, await Login(host, second)), secondOwner, secondInstance, secondTask);
        var prefs = new PrefService(first.Db);
        await prefs.SaveMergeAsync(first.User, """{"notify":{"todoCreated":{"email":false}}}""");
        var flowKey = await first.Db.Wf_FlowInstances.Where(x => x.Id == firstInstance).Select(x => x.FlowKey).SingleAsync();
        var engine = new PersistentWfNotifier(new NotificationService(first.Db), prefs);
        await engine.TodoCreatedAsync(first.User, firstInstance, firstTask, flowKey);
        await engine.TodoCreatedAsync(first.User, firstInstance, firstTask, flowKey);
        await first.Db.SaveChangesAsync();
        var engineKey = $"{WfNotificationType.TodoCreated}:{firstInstance:N}:{firstTask:N}:{first.User:N}:-";
        var engineNotice = await first.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.EventKey == engineKey);

        var refreshed = new ConcurrentQueue<(Guid Notice, Guid User)>();
        var mail = new ConcurrentQueue<string>();
        await using var worker = OriginalWorker(database, refreshed, mail);
        await using var replica = OriginalWorker(database, refreshed, mail);
        await Task.WhenAll(Poll(worker), Poll(replica));
        await Poll(replica);
        Assert.Equal(5, refreshed.Count);
        Assert.Equal(5, refreshed.Select(x => x.Notice).Distinct().Count());
        Assert.Contains((firstReceipt.Id, first.User), refreshed);
        Assert.Contains((engineNotice.Id, first.User), refreshed);
        Assert.Contains((secondReceipt.Id, second.User), refreshed);
        Assert.Equal(1, (await ReadOriginalNotice(first, firstReceipt.Id)).DispatchAttempts);
        Assert.Equal(1, (await ReadOriginalNotice(second, secondReceipt.Id)).DispatchAttempts);
        Assert.Empty(mail);
        Assert.Empty((await NativeInbox(first, firstOwner).DetailAsync(firstInstance)).Votes);
        Assert.Empty((await NativeInbox(second, secondOwner).DetailAsync(secondInstance)).Votes);
        var list = await NoticeData(host, firstLogin, HttpMethod.Get, Notices);
        var own = Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == firstReceipt.Id);
        Assert.Equal("待办期限已重算", own.GetProperty("title").GetString());
        Assert.Equal(firstTask, own.GetProperty("taskId").GetGuid());
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == secondReceipt.Id);
        output.WriteLine(JsonSerializer.Serialize(new { first = firstReceipt.Id, second = secondReceipt.Id, legacy = engineNotice.Id,
            refreshes = refreshed.Count, repeated = 0, mails = mail.Count, votes = 0 }));
    }

    private static Task<Wf_Notification> ReadOriginalNotice(Harness h, Guid id)
        => h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.Id == id);

    private static async Task<Wf_Notification> QueueOriginalNotice(Harness h, string eventKey, bool emailRequested = false)
    {
        await new NotificationService(h.Db).CreateOutboxAsync(h.User, WfNotificationType.TodoCreated,
            "合成通知", "合成通知", null, null, null, eventKey, inAppRequested: true, emailRequested: emailRequested);
        await h.Db.SaveChangesAsync();
        return await h.Db.Wf_Notifications.AsNoTracking().SingleAsync(x => x.EventKey == eventKey);
    }

    private ServiceProvider OriginalWorker(string database, ConcurrentQueue<(Guid Notice, Guid User)> refreshed,
        ConcurrentQueue<string> email, Func<Guid, CancellationToken, Task>? onRefresh = null)
    {
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        clients.Setup(x => x.User(It.IsAny<string>())).Returns((string user) =>
        {
            var proxy = new Mock<IClientProxy>(MockBehavior.Strict);
            proxy.Setup(x => x.SendCoreAsync("WfNotification", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns((string _, object?[] arguments, CancellationToken ct) =>
                {
                    var payload = JsonSerializer.SerializeToElement(Assert.Single(arguments));
                    var id = payload.GetProperty("notificationId").GetGuid();
                    Assert.Equal(payload.GetProperty("userId").GetGuid(), Guid.Parse(user));
                    refreshed.Enqueue((id, Guid.Parse(user)));
                    return onRefresh?.Invoke(id, ct) ?? Task.CompletedTask;
                });
            return proxy.Object;
        });
        var hub = new Mock<IHubContext<NotifyHub>>(MockBehavior.Strict);
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var mail = new Mock<IEmailSender>(MockBehavior.Strict);
        mail.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string to, string _, string _) => { email.Enqueue(to); return Task.CompletedTask; });
        return new ServiceCollection()
            .AddScoped<CP6Context>(_ => server.ContextForDatabase(database, new TenantContext()))
            .AddSingleton(hub.Object)
            .AddSingleton(mail.Object)
            .BuildServiceProvider();
    }

    private static Task Poll(ServiceProvider proc, CancellationToken ct = default)
        => new WfNotificationDispatchWorker(proc.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WfNotificationDispatchWorker>.Instance).DispatchBatchAsync(ct);

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
}
