using System.Data;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string VersionCalendarApi = "api/oa/work-calendar/versions";

    private sealed class DeadlineCreationAt(DateTimeOffset instant) : DbCommandInterceptor
    {
        internal bool Enabled;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("SELECT clock_timestamp() AS \"Value\""))
            {
                command.CommandText = command.CommandText.Replace("SELECT clock_timestamp() AS \"Value\"",
                    "SELECT @deadline_fixture_utc AS \"Value\"", StringComparison.Ordinal);
                var parameter = command.CreateParameter();
                parameter.ParameterName = "deadline_fixture_utc";
                parameter.DbType = DbType.DateTimeOffset;
                parameter.Value = instant;
                command.Parameters.Add(parameter);
            }
            return ValueTask.FromResult(result);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_original_revision_survives_day_override_and_clear()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await CalendarPermissions(h, edit: true);
        await TimeoutZone(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var original = await api.Data(VersionCalendarApi + "?year=2026");
        var id = original.GetProperty("calendarId").GetGuid();
        var initial = original.GetProperty("revision").GetInt64();
        Assert.Equal(h.Tenant, id);
        Assert.Equal("MON_FRI", original.GetProperty("weekRule").GetString());
        Assert.Equal("America/New_York", original.GetProperty("timeZoneId").GetString());
        await api.Data("api/oa/work-calendar/toggle", new { date = "2026-11-26", isWorkday = false, note = "旧入口" });
        var fromLegacy = await api.Data(VersionCalendarApi + "?year=2026");
        Assert.Equal(++initial, fromLegacy.GetProperty("revision").GetInt64());
        Assert.Contains(fromLegacy.GetProperty("items").EnumerateArray(), x => x.GetProperty("note").GetString() == "旧入口");

        var saturday = "2026-11-28";
        var add = await api.Data(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = id, expectedRevision = initial,
            date = saturday, isWorkday = true, note = "隔离补班"
        });
        var revision = add.GetProperty("revision").GetInt64();
        Assert.True(revision > initial);
        var changed = await api.Data(VersionCalendarApi + "?year=2026&calendarId=" + id + "&revision=" + revision);
        Assert.Contains(changed.GetProperty("items").EnumerateArray(), x => x.GetProperty("date").GetString() == saturday && x.GetProperty("isWorkday").GetBoolean());
        await api.Reject(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = id, expectedRevision = initial,
            date = saturday, isWorkday = false, note = "旧版本不能覆盖"
        }, HttpStatusCode.PreconditionFailed, "CALENDAR_REVISION_CHANGED");

        var clear = await api.Data(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = id, expectedRevision = revision,
            date = saturday, isWorkday = (bool?)null
        });
        Assert.False(clear.GetProperty("isWorkday").GetBoolean());
        Assert.Equal("DEFAULT_WEEK", clear.GetProperty("source").GetString());
        var history = await api.Data(VersionCalendarApi + "?year=2026&calendarId=" + id + "&revision=" + revision);
        Assert.Contains(history.GetProperty("items").EnumerateArray(), x => x.GetProperty("date").GetString() == saturday);

        await using var foreign = await Seed();
        await CalendarPermissions(foreign);
        var foreignApi = new DeadlineApi(host, await Login(host, foreign));
        await foreignApi.Reject(VersionCalendarApi + "?year=2026&calendarId=" + id + "&revision=" + revision,
            null, HttpStatusCode.NotFound, "CALENDAR_NOT_FOUND");
        Assert.Equal(foreign.Tenant, (await foreignApi.Data(VersionCalendarApi + "?year=2026")).GetProperty("calendarId").GetGuid());
        output.WriteLine($"实际 Main 年历按租户读取，修订 {initial} → {revision} → {clear.GetProperty("revision")}。周六补班与清除后的默认休息均可解释。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_import_replays_one_batch_after_host_restart_and_preserves_manual_exception()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await CalendarPermissions(h, edit: true);
        await TimeoutZone(h);
        var id = Guid.NewGuid();
        long expectedRevision;
        string receipt;
        await using (var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings()))
        {
            var api = new DeadlineApi(host, await Login(host, h));
            var year = await api.Data(VersionCalendarApi + "?year=2027");
            var manual = await api.Data(VersionCalendarApi + "/changes", new
            {
                commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = year.GetProperty("revision").GetInt64(),
                date = "2027-01-01", isWorkday = true, note = "保留人工补班",
            });
            expectedRevision = manual.GetProperty("revision").GetInt64();
            var first = await api.Data(VersionCalendarApi + "/import-jp", new
            {
                commandId = id, calendarId = h.Tenant, expectedRevision, year = 2027
            });
            receipt = first.GetRawText();
            Assert.Equal(expectedRevision + 1, first.GetProperty("revision").GetInt64());
            Assert.Contains(first.GetProperty("skipped").EnumerateArray(), x => x.GetString() == "2027-01-01");
            Assert.True(first.GetProperty("inserted").GetArrayLength() > 0);
            Assert.Matches("^[0-9a-f]{64}$", first.GetProperty("package").GetProperty("sha256").GetString()!);
        }
        await using var restart = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var reconnected = new DeadlineApi(restart, await Login(restart, h));
        var command = new { commandId = id, calendarId = h.Tenant, expectedRevision, year = 2027 };
        var replays = await Task.WhenAll(reconnected.Data(VersionCalendarApi + "/import-jp", command),
            reconnected.Data(VersionCalendarApi + "/import-jp", command));
        Assert.All(replays, replay => Assert.Equal(receipt, replay.GetRawText()));
        await reconnected.Reject(VersionCalendarApi + "/import-jp", command with { year = 2026 },
            HttpStatusCode.Conflict, "CALENDAR_COMMAND_CONFLICT");
        await reconnected.Reject(VersionCalendarApi + "/import-jp", command with { commandId = Guid.NewGuid(), year = 2028 },
            HttpStatusCode.UnprocessableEntity, "CALENDAR_YEAR_UNSUPPORTED");
        var current = await reconnected.Data(VersionCalendarApi + "?year=2027");
        var exception = Assert.Single(current.GetProperty("items").EnumerateArray(), x => x.GetProperty("date").GetString() == "2027-01-01");
        Assert.True(exception.GetProperty("isWorkday").GetBoolean());
        Assert.Equal("保留人工补班", exception.GetProperty("note").GetString());
        Assert.Equal(expectedRevision + 1, current.GetProperty("revision").GetInt64());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND operation_id={id} AND contract='OA_WORK_CALENDAR_IMPORT_JP/1'
            """).SingleAsync());
        output.WriteLine($"重启 Main HTTP 进程并发重放原导入 {id}。持久凭证 {receipt}。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_workday_bound_and_both_DST_discontinuities_are_explicit()
    {
        await using var h = await Seed();
        await RegisterScope(h);
        await CalendarPermissions(h, edit: true);
        await TimeoutZone(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var year = await api.Data(VersionCalendarApi + "?year=2027");
        var rev = year.GetProperty("revision").GetInt64();
        var tzVersion = year.GetProperty("timeZoneRulesVersion").GetString();
        Assert.Matches("^[0-9a-f]{64}$", tzVersion!);
        string Route(string from, int days, string time, int? offset = null) => VersionCalendarApi +
            $"/calculate?calendarId={h.Tenant:D}&revision={rev}&from={from}&workdays={days}&localTime={time}" +
            $"&expectedTimeZoneRulesVersion={tzVersion}" + (offset == null ? "" : $"&ambiguousOffsetMinutes={offset}");
        async Task<JsonElement> Calculate(string from, int days, string time, int? offset = null) =>
            (await api.Data(Route(from, days, time, offset))).GetProperty("deadline");
        async Task MarkWorkday(string date)
        {
            var r = await api.Data(VersionCalendarApi + "/changes", new
            {
                commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = rev,
                date, isWorkday = true, note = "隔离时区取证",
            });
            rev = r.GetProperty("revision").GetInt64();
        }

        Assert.Equal("2026-11-30T12:00:00", (await Calculate("2026-11-27", 1, "12:00:00")).GetProperty("localDueAt").GetString());
        await MarkWorkday("2026-11-28");
        Assert.Equal("2026-11-28T12:00:00", (await Calculate("2026-11-27", 1, "12:00:00")).GetProperty("localDueAt").GetString());
        Assert.Equal("CALENDAR_RANGE_EXCEEDED", (await Calculate("2026-11-27", 366, "12:00:00")).GetProperty("failureCode").GetString());
        await api.Reject(Route("2026-11-27", 1, "12:00:00").Replace(tzVersion!, new string('0', 64)), null,
            HttpStatusCode.PreconditionFailed, "TIMEZONE_VERSION_CHANGED");

        await MarkWorkday("2027-03-14");
        var gap = await Calculate("2027-03-13", 1, "02:30:00");
        Assert.Equal("AMBIGUOUS_OR_INVALID_LOCAL_TIME", gap.GetProperty("failureCode").GetString());
        Assert.Equal(JsonValueKind.Null, gap.GetProperty("dueAt").ValueKind);
        Assert.Equal("2027-03-14T02:30:00", gap.GetProperty("localDueAt").GetString());
        await MarkWorkday("2027-11-07");
        var fold = await Calculate("2027-11-06", 1, "01:30:00");
        Assert.Equal("AMBIGUOUS_OR_INVALID_LOCAL_TIME", fold.GetProperty("failureCode").GetString());
        Assert.Equal(new[] { -240, -300 }, fold.GetProperty("availableOffsets").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(JsonValueKind.Null, (await Calculate("2027-11-06", 1, "01:30:00", 0)).GetProperty("dueAt").ValueKind);
        var earlier = await Calculate("2027-11-06", 1, "01:30:00", -240);
        var later = await Calculate("2027-11-06", 1, "01:30:00", -300);
        Assert.Equal(DateTimeOffset.Parse("2027-11-07T05:30:00Z"), earlier.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2027-11-07T06:30:00Z"), later.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal(-240, earlier.GetProperty("offsetMinutes").GetInt32());
        Assert.Equal(-300, later.GetProperty("offsetMinutes").GetInt32());
        Assert.Equal("2027-11-07T01:30:00", later.GetProperty("localDueAt").GetString());
        output.WriteLine($"实际 Main 计算步长上限 366，时区版本 {tzVersion}，DST 缺失 {gap}，重复 {earlier} 和 {later}。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_real_task_persists_DST_fold_and_records_gap_without_an_effective_deadline()
    {
        foreach (var (anchor, local, time, selection, expectedUtc) in new (string, string, TimeOnly, int?, string?)[]
        {
            ("2027-03-13T15:00:00Z", "2027-03-14", new(2, 30), null, null),
            ("2027-11-06T15:00:00Z", "2027-11-07", new(1, 30), -300, "2027-11-07T06:30:00Z")
        })
        {
            // 只为可复验地命中夏令时转换，夹具固定事实的创建时刻。原生表单和持久化沿真实实现。
            var clock = new DeadlineCreationAt(DateTimeOffset.Parse(anchor));
            await using var h = await Seed(clock);
            await TimeoutZone(h);
            await new WorkCalendarService(h.Db).SetDayAsync(DateOnly.Parse(local).ToDateTime(TimeOnly.MinValue), true, "DST 转换日", CancellationToken.None);
            var f = await NativeFormSetup(h, configure: flow =>
            {
                var node = flow.Nodes.Single(n => n.Id == "a");
                node.TimeoutWorkday = new(1, time, selection);
                node.TimeoutAction = "approve";
            }, beforePublish: () => { clock.Enabled = true; return Task.CompletedTask; });
            clock.Enabled = false;
            var deadline = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
            Assert.Equal(DateTimeOffset.Parse(anchor), deadline.CreatedAt);
            Assert.Equal(DateOnly.Parse(local).ToDateTime(time), deadline.Policy.LocalDueAt);
            Assert.Equal("America/New_York", deadline.Policy.TimeZoneId);
            Assert.Matches("^[0-9a-f]{64}$", deadline.Policy.TimeZoneRulesVersion!);
            Assert.Equal("1", deadline.Policy.CalendarRevision);
            Assert.Equal(h.Tenant, deadline.Policy.CalendarId);
            Assert.Equal("a", deadline.Policy.NodeId);
            Assert.Equal((await h.Db.Wf_FlowInstances.SingleAsync(i => i.Id == f.Instance)).FlowDefVersionId, deadline.Policy.FlowVersionId);
            if (expectedUtc == null)
            {
                Assert.Equal("INVALID_POLICY", deadline.State);
                Assert.Equal("AMBIGUOUS_OR_INVALID_LOCAL_TIME", deadline.Policy.FailureCode);
                Assert.Null(deadline.DueAt);
            }
            else
            {
                Assert.Equal(selection!.Value, deadline.Policy.OffsetMinutes);
                Assert.Equal(DateTimeOffset.Parse(expectedUtc), deadline.DueAt);
                Assert.Equal("READY", deadline.State);
            }
            output.WriteLine($"任务 {f.Task} 固定的转换日期证据 {JsonSerializer.Serialize(deadline, PersonalFormOwnerV2.Json)}");
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_recalculation_requires_independent_permission_and_exact_preview_with_both_versions()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        await CalendarPermissions(h, edit: true);
        var f = await NativeFormSetup(h, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutAction = "errorEdge";
            node.TimeoutHours = 24;
            node.TimeoutWorkday = new(1, new TimeOnly(12, 0));
            flow.Nodes.Add(new() { Id = "recover", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = h.User });
            flow.Edges.Add(new() { From = "a", To = "recover", IsError = true });
            flow.Edges.Add(new() { From = "recover", To = "e" });
        });
        var deadline = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal("WORKDAY_LOCAL", deadline.Policy.Basis);
        Assert.Equal(h.Tenant, deadline.Policy.CalendarId);
        Assert.NotNull(deadline.Policy.LocalDueAt);
        Assert.NotNull(deadline.Policy.TimeZoneRulesVersion);
        var zoneVersion = deadline.Policy.TimeZoneRulesVersion;
        Assert.Equal(zoneVersion, await h.Db.Database.SqlQuery<string>($"""
            SELECT encode(sha256(convert_to(rules,'UTF8')),'hex') AS "Value" FROM cp6_work_calendar_v1.time_zone_rules
            WHERE zone_id={deadline.Policy.TimeZoneId} AND version={zoneVersion}
            """).SingleAsync());
        var day = DateOnly.FromDateTime(deadline.Policy.LocalDueAt!.Value);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings());
        var api = new DeadlineApi(host, await Login(host, h));
        var year = await api.Data(VersionCalendarApi + "?year=" + day.Year);
        var change = await api.Data(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = year.GetProperty("revision").GetInt64(),
            date = day, isWorkday = false, note = "改版只影响新任务",
        });
        Assert.Equal(deadline, Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines));
        var previewRequest = new { calendarId = h.Tenant, expectedCalendarRevision = change.GetProperty("revision").GetInt64(), taskIds = new[] { f.Task } };
        await api.Reject(VersionCalendarApi + "/deadline-previews", previewRequest, HttpStatusCode.Forbidden, "403");
        await CalendarPermissions(h, recalculate: true);
        api = new DeadlineApi(host, await Login(host, h));
        var preview = await api.Data(VersionCalendarApi + "/deadline-previews", previewRequest);
        var item = Assert.Single(preview.GetProperty("items").EnumerateArray());
        var before = item.GetProperty("before");
        var after = item.GetProperty("after");
        Assert.Equal(deadline.DueAt, before.GetProperty("dueAt").GetDateTimeOffset());
        Assert.True(after.GetProperty("dueAt").GetDateTimeOffset() > deadline.DueAt);
        Assert.Equal(deadline.Policy.FlowVersionId, after.GetProperty("policy").GetProperty("flowVersionId").GetGuid());
        var cmd = new
        {
            commandId = Guid.NewGuid(), previewId = preview.GetProperty("previewId").GetGuid(), taskId = f.Task, reason = "独立确认受影响原待办",
            expectedDeadlineRevision = deadline.Revision, expectedTaskVersion = item.GetProperty("taskVersion").GetString(),
            expectedInstanceVersion = item.GetProperty("instanceVersion").GetString(),
        };
        await h.Db.Sys_RoleActions.Where(a => a.RoleId == 72 && a.ActionCode == "Calendar.Recalculate").ExecuteDeleteAsync();
        api = new DeadlineApi(host, await Login(host, h));
        await api.Reject(VersionCalendarApi + "/deadline-recalculations", cmd, HttpStatusCode.Forbidden, "403");
        await CalendarPermissions(h, recalculate: true);
        api = new DeadlineApi(host, await Login(host, h));
        await api.Reject(VersionCalendarApi + "/deadline-recalculations", cmd with { expectedTaskVersion = "stale" },
            HttpStatusCode.PreconditionFailed, "DEADLINE_PREVIEW_CHANGED");
        await api.Reject(VersionCalendarApi + "/deadline-recalculations", cmd with { expectedInstanceVersion = "stale" },
            HttpStatusCode.PreconditionFailed, "DEADLINE_PREVIEW_CHANGED");
        await using (var foreign = await Seed())
        {
            await RegisterScope(foreign);
            await TimeoutZone(foreign);
            await CalendarPermissions(foreign, recalculate: true);
            var cross = new DeadlineApi(host, await Login(host, foreign));
            await cross.Reject(VersionCalendarApi + "/deadline-recalculations", cmd,
                HttpStatusCode.NotFound, "DEADLINE_PREVIEW_NOT_FOUND");
            var foreignYear = await cross.Data(VersionCalendarApi + "?year=" + day.Year);
            await cross.Reject(VersionCalendarApi + "/deadline-previews", new
            {
                calendarId = foreign.Tenant, expectedCalendarRevision = foreignYear.GetProperty("revision").GetInt64(), taskIds = new[] { f.Task }
            }, HttpStatusCode.NotFound, "DEADLINE_TASK_NOT_AVAILABLE");
        }
        var changedAgain = await api.Data(VersionCalendarApi + "/changes", new
        {
            commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = previewRequest.expectedCalendarRevision,
            date = day.AddDays(30), isWorkday = false, note = "确认前的新日历版本",
        });
        await api.Reject(VersionCalendarApi + "/deadline-recalculations", cmd,
            HttpStatusCode.PreconditionFailed, "CALENDAR_REVISION_CHANGED");
        Assert.Equal(deadline, Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines));
        preview = await api.Data(VersionCalendarApi + "/deadline-previews",
            previewRequest with { expectedCalendarRevision = changedAgain.GetProperty("revision").GetInt64() });
        item = Assert.Single(preview.GetProperty("items").EnumerateArray());
        after = item.GetProperty("after");
        cmd = cmd with { commandId = Guid.NewGuid(), previewId = preview.GetProperty("previewId").GetGuid() };
        var receipt = await api.Data(VersionCalendarApi + "/deadline-recalculations", cmd);
        Assert.Equal(receipt.GetRawText(), (await api.Data(VersionCalendarApi + "/deadline-recalculations", cmd)).GetRawText());
        Assert.Equal(h.User, receipt.GetProperty("actorId").GetGuid());
        var relisted = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal(deadline.Revision + 1, relisted.Revision);
        Assert.Equal(after.GetProperty("dueAt").GetDateTimeOffset(), relisted.DueAt);
        Assert.Equal(deadline.DueAt, await h.Db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT due_at AS "Value" FROM cp6_native_timeout_v2.deadlines WHERE task_id={f.Task} AND revision={deadline.Revision}
            """).SingleAsync());
        Assert.Equal(1, await h.Db.Wf_Notifications.CountAsync(n => n.EventKey == "OA_DEADLINE_RECALCULATED/" + cmd.commandId.ToString("D")));
        var worker = await TimeoutOwner(h, f.Owner, deadline.DueAt!.Value.AddSeconds(1));
        Assert.Empty(await worker.ClaimAsync());
        worker.Clock = _ => Task.FromResult(relisted.DueAt!.Value.AddSeconds(1));
        var claim = Assert.Single(await worker.ClaimAsync());
        Assert.Equal(relisted.Revision, claim.Revision);
        Assert.Equal("COMPLETED", (await worker.ExecuteAsync(claim)).State);
        var instance = await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i => i.Id == f.Instance);
        using var vars = JsonDocument.Parse(instance.VarsJson!);
        Assert.Equal(relisted.DueAt, vars.RootElement.GetProperty("timeoutError").GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal(0, (await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.InstanceId == f.Instance && t.NodeId == "recover")).Status);
        output.WriteLine($"独立重算的实际回执 {receipt}。旧截止读回未改，worker 按新截止领取，错误边采用同一 UTC 事实。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_old_claim_cannot_commit_after_native_transfer_changes_both_versions()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        var f = await NativeFormSetup(h, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 1;
            node.TimeoutAction = "remind";
        });
        await NativeOperationPermissions(h);
        var target = await GrantDelegate(h);
        var original = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        var due = original.DueAt!.Value;
        var worker = await TimeoutOwner(h, f.Owner, due.AddSeconds(1));
        var claim = Assert.Single(await worker.ClaimAsync());
        var before = await f.Owner.TaskContextAsync(f.Task);
        await f.Owner.TransferTaskAsync(f.Task, PersonalCommand(new NativeTransferInputV2(f.Task, target, "领取后改派",
            before.Task.TaskRowVersion), "transfer-after-claim", before.Task.InstanceRowVersion));
        var versionsStillPinned = await h.Db.Database.SqlQuery<bool>($"""
            SELECT (c.task_version=decode({before.Task.TaskRowVersion},'base64') AND c.instance_version=decode({before.Task.InstanceRowVersion},'base64')
              AND c.task_version<>t."ApprovalRowVersion" AND c.instance_version<>i."RowVersion") AS "Value"
            FROM cp6_native_timeout_v2.claims c JOIN public."Wf_FlowTask" t ON t."Id"=c.task_id
            JOIN public."Wf_FlowInstance" i ON i."Id"=t."InstanceId" WHERE c.execution_id={claim.ExecutionId}
            """).SingleAsync();
        Assert.True(versionsStillPinned);
        Assert.Equal("TIMEOUT_CLAIM_VERSION_CHANGED",
            (await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(() => worker.ExecuteAsync(claim))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_native_timeout_v2.results WHERE execution_id={claim.ExecutionId}
            """).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_personal_form_v2.outbox WHERE command_id={claim.ExecutionId}
            """).SingleAsync());
        worker.Clock = _ => Task.FromResult(due.AddSeconds(62));
        var next = Assert.Single(await worker.ClaimAsync());
        Assert.Equal(claim.ExecutionId, next.ExecutionId);
        Assert.Equal(claim.Epoch + 1, next.Epoch);
        var result = await worker.ExecuteAsync(next);
        Assert.Equal("COMPLETED", result.State);
        Assert.Equal(target, await h.Db.Wf_FlowTasks.Where(t => t.Id == f.Task).Select(t => t.AssigneeId).SingleAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_personal_form_v2.outbox WHERE command_id={claim.ExecutionId}
            """).SingleAsync());
        output.WriteLine($"原执行 {claim.ExecutionId} 的旧任务及实例版本零效果，租约到期后同槽 {next.Epoch} 代仅一条提醒。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_missing_pin_and_unknown_action_are_visible_and_never_executed()
    {
        var legacy = new StartWriteHook();
        await using var old = await Seed(legacy);
        await TimeoutZone(old);
        // 仅模拟切换前已存在 DueAt 的旧行。收件箱和 worker 用真实读写路径。
        legacy.Before = command =>
        {
            if (command.CommandText.StartsWith("INSERT INTO cp6_native_timeout_v2.deadlines", StringComparison.Ordinal) ||
                command.CommandText.StartsWith("INSERT INTO cp6_native_timeout_v2.pending", StringComparison.Ordinal))
                command.CommandText = "SELECT 1";
            return Task.CompletedTask;
        };
        var oldTask = await NativeFormSetup(old, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 1;
            node.TimeoutAction = "reject";
        });
        legacy.Before = null;
        Assert.NotNull(await old.Db.Wf_FlowTasks.AsNoTracking().Where(t => t.Id == oldTask.Task).Select(t => t.DueAt).SingleAsync());
        var original = (await NativeInbox(old, oldTask.Owner).DetailAsync(oldTask.Instance)).Timeout!;
        Assert.Equal(new[] { oldTask.Task }, original.MissingEvidenceTaskIds);
        Assert.Empty(original.Deadlines);
        Assert.Empty(await (await TimeoutOwner(old, oldTask.Owner, DateTimeOffset.UtcNow.AddDays(1))).ClaimAsync());

        await using var h = await Seed();
        await TimeoutZone(h);
        var unknown = await NativeFormSetup(h, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 1;
            node.TimeoutAction = "prior-unknown";
        });
        var deadline = Assert.Single((await NativeInbox(h, unknown.Owner).DetailAsync(unknown.Instance)).Timeout!.Deadlines);
        Assert.Equal("INVALID_POLICY", deadline.State);
        Assert.Equal("DEADLINE_POLICY_INVALID", deadline.Policy.FailureCode);
        Assert.Equal("prior-unknown", deadline.Policy.Action);
        Assert.Null(deadline.DueAt);
        Assert.Empty(await (await TimeoutOwner(h, unknown.Owner, DateTimeOffset.UtcNow.AddDays(1))).ClaimAsync());
        output.WriteLine($"旧行 {oldTask.Task} 缺 pin 可读，未知策略任务 {unknown.Task} 保留原动作。两者领取及执行均为零。");
    }

    [CurrentPermissionPostgresFact]
    public async Task WorkflowDeadlineCalendar_bad_tenant_zone_creates_queryable_blocked_fact_without_new_due()
    {
        foreach (var (zone, failure) in new (string?, string)[]
        {
            ("Missing/Calendar_Zone", "TENANT_TIMEZONE_INVALID"), (null, "TENANT_TIMEZONE_REQUIRED")
        })
        {
            await using var h = await Seed();
            await h.Db.Sys_Tenants.Where(t => t.Id == h.Tenant).ExecuteUpdateAsync(rows => rows.SetProperty(t => t.TimeZoneId, zone));
            var f = await NativeFormSetup(h, configure: flow =>
            {
                var node = flow.Nodes.Single(n => n.Id == "a");
                node.TimeoutHours = 1;
                node.TimeoutAction = "remind";
            });
            var detail = await NativeInbox(h, f.Owner).DetailAsync(f.Instance);
            var deadline = Assert.Single(detail.Timeout!.Deadlines);
            Assert.Equal("INVALID_POLICY", deadline.State);
            Assert.Equal(failure, deadline.Policy.FailureCode);
            Assert.Null(deadline.Policy.TimeZoneRulesVersion);
            Assert.Null(deadline.DueAt);
            Assert.Null(await h.Db.Wf_FlowTasks.AsNoTracking().Where(t => t.Id == f.Task).Select(t => t.DueAt).SingleAsync());
            Assert.Empty(await (await TimeoutOwner(h, f.Owner, DateTimeOffset.UtcNow.AddDays(1))).ClaimAsync());
            output.WriteLine($"{failure} 留下 INVALID_POLICY 原事实，任务 {f.Task} 没有 UTC 截止或 worker 领取。");
        }
    }

    [OaNativeBrowserPostgresFact]
    public async Task WorkflowDeadlineCalendar_actual_Vue_calendar_and_inbox_show_recoverable_import_and_recalculation()
    {
        await using var h = await Seed();
        await TimeoutZone(h);
        await CalendarPermissions(h, edit: true, recalculate: true);
        var f = await NativeFormSetup(h, configure: flow =>
        {
            var node = flow.Nodes.Single(n => n.Id == "a");
            node.TimeoutHours = 24;
            node.TimeoutAction = "remind";
            node.TimeoutWorkday = new(1, new TimeOnly(12, 0));
        });
        var original = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        foreach (var (menu, route) in new[] { ("oa-work-calendar", "/oa/work-calendar"), ("oa-inbox", "/oa/inbox") })
            await h.Db.Sys_Menus.Where(m => m.MenuKey == menu).ExecuteUpdateAsync(rows => rows.SetProperty(m => m.RoutePath, route));
        await using (var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, CalendarHostSettings()))
        {
            var api = new DeadlineApi(host, await Login(host, h));
            var day = DateOnly.FromDateTime(original.Policy.LocalDueAt!.Value);
            var calendar = await api.Data(VersionCalendarApi + "?year=" + day.Year);
            await api.Data(VersionCalendarApi + "/changes", new
            {
                commandId = Guid.NewGuid(), calendarId = h.Tenant, expectedRevision = calendar.GetProperty("revision").GetInt64(),
                date = day, isWorkday = false, note = "浏览器核对已发布的工作日例外",
            });
        }
        using var evidence = await BrowserScenario(h, h.User, "oa-work-calendar-browser.mjs", "calendar-browser", CalendarHostSettings(),
            new()
            {
                ["CP6_TASK_ID"] = f.Task.ToString("D"), ["CP6_TASK_INSTANCE"] = f.Instance.ToString("D"),
                ["CP6_CALENDAR_PRIOR_DUE"] = original.DueAt!.Value.ToString("O"),
            });
        var receipt = evidence.RootElement.GetProperty("receipt");
        var command = receipt.GetProperty("commandId").GetGuid();
        var final = Assert.Single((await NativeInbox(h, f.Owner).DetailAsync(f.Instance)).Timeout!.Deadlines);
        Assert.Equal(receipt.GetProperty("after").GetProperty("dueAt").GetDateTimeOffset(), final.DueAt);
        Assert.Equal(original.Revision + 1, final.Revision);
        Assert.Equal(1, await h.Db.Wf_Notifications.CountAsync(n => n.EventKey == "OA_DEADLINE_RECALCULATED/" + command.ToString("D")));
    }

    private static Dictionary<string, string> CalendarHostSettings() =>
        new() { ["ScopeAuthorization__EnvironmentId"] = ScopeEnvironment.ToString("D") };

    private static async Task CalendarPermissions(Harness h, bool edit = false, bool recalculate = false)
    {
        const string menu = "oa-work-calendar";
        await EngineeringAction(h, menu, "Calendar.View");
        if (edit) await EngineeringAction(h, menu, "Calendar.Edit");
        if (recalculate) await EngineeringAction(h, menu, "Calendar.Recalculate");
        var menuId = await h.Db.Sys_Menus.Where(m => m.MenuKey == menu).Select(m => m.MenuId).SingleAsync();
        if (!await h.Db.Sys_RoleMenus.AnyAsync(g => g.RoleId == 72 && g.MenuId == menuId))
        {
            h.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menuId });
            await h.Db.SaveChangesAsync();
            h.Db.ChangeTracker.Clear();
        }
    }

    private sealed record DeadlineApi(AuthenticationApiProcess Host, Dictionary<string, string> Cookies)
    {
        private Task<HttpResponseMessage> Send(string route, object? body) =>
            Host.Send(OperationsRequest(Cookies, body == null ? HttpMethod.Get : HttpMethod.Post, route, body));

        internal async Task<JsonElement> Data(string route, object? body = null)
        {
            using var response = await Send(route, body);
            Assert.True(response.IsSuccessStatusCode, route + " " + response.StatusCode + " " + await OperationsFailure(response));
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        }

        internal async Task Reject(string route, object? body, HttpStatusCode status, string code)
        {
            using var response = await Send(route, body);
            Assert.Equal(status, response.StatusCode);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(code, json.GetRawText(), StringComparison.Ordinal);
        }
    }
}
