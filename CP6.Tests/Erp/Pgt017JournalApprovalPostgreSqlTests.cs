using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using CP6.Tests.Wf;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string W618BizType = "FinJournalPost";
    private const string W618FlowKey = "w618-historical-journal";
    private const string W618CheckerName = "journal-checker";
    private const string W618CheckerPassword = "Journal1!Checker";
    private const string W618Comment = "Approve the linked historical journal";
    private static readonly DateTime W618Date = new(2026, 6, 15);
    private const decimal W618Amount = 873.24m;

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W618_historical_name_only_maker_cannot_approve_as_self_or_delegate(bool delegated)
    {
        await using var h = await Seed();
        var other = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var assignee = delegated ? other : h.User;
        var f = await SeedW618JournalAndWorkflow(h, host, maker, assignee, starter: other);
        if (delegated)
            await GrantW618Delegate(h, assignee, h.User);

        // Historical rows lack the new maker account column value.
        Assert.Equal(1, await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Fin_JournalEntry" SET "MakerUserId"=NULL WHERE "TenantId"={h.Tenant} AND "Id"={f.Journal}
            """));
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        CheckW618Pending(before, f);
        Assert.Equal(JsonValueKind.Null, before.Row("journals", f.Journal).GetProperty("MakerUserId").ValueKind);

        using var direct = await host.Send(OperationsRequest(maker, HttpMethod.Post, $"/api/fin/journal/{f.Journal}/post"));
        await CheckApError(direct, "E-FIN-111", []);
        Assert.Equal(before, await ReadW618(observer, h.Tenant));

        using var result = await DecideW618(host, maker, f.Task, delegated ? assignee : null);
        await CheckW618Denied(result, "E-FIN-111");
        var after = await ReadW618(observer, h.Tenant);
        Assert.Equal(before, after);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "historical name-only maker", h.Tenant, f.Journal, f.Task, f.Instance,
            Maker = h.User, f.MakerName, Starter = other, Assignee = assignee, Delegated = delegated,
            DirectCode = "E-FIN-111", OaDecision = (int)result.StatusCode, Before = W618State(before, f), After = W618State(after, f),
        }));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W618_distinct_approver_and_Main_pause_share_the_journal_commit(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var checker = await SeedW618Checker(h);
        Assert.NotEqual(h.User, checker);
        await PrepareW618Finance(h);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var actor = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
        var f = await SeedW618JournalAndWorkflow(h, host, maker, checker);
        var manager = await Login(host, admin);

        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);
        var suspend = new TenantManagementCommandV1("w618-suspend", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        CheckW618Pending(before, f);

        var race = await ObserveAssetRace(connection, observer, "Fin_JournalEntry", "UPDATE", "Sys_Tenants", "UPDATE", pauseFirst,
            () => DecideW618(host, actor, f.Task),
            () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadW618(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var business = race.Business;
        using var pause = race.Authority;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(pause);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var after = await ReadW618(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.True(business.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(business));
            Assert.Equal(before, after);
        }
        else
        {
            await CheckW618DecisionReceipt(business, f);
            CheckW618Approved(after, before, f, checker);
        }

        using var stale = await DecideW618(host, actor, f.Task);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await ReadW618(observer, h.Tenant));
        var final = after;
        if (pauseFirst)
        {
            var resume = new TenantManagementCommandV1("w618-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), resume));
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
            actor = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
            using var accepted = await DecideW618(host, actor, f.Task);
            await CheckW618DecisionReceipt(accepted, f);
            final = await ReadW618(observer, h.Tenant);
            CheckW618Approved(final, before, f, checker);
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "different maker and approver", Tenant = h.Tenant,
            Maker = new { Guid = h.User, Name = f.MakerName }, Approver = new { Guid = checker, Name = W618CheckerName },
            f.Journal, f.Task, f.Instance, f.Definition, PauseFirst = pauseFirst,
            FirstStatement = pauseFirst ? "UPDATE Sys_Tenants" : "UPDATE Fin_JournalEntry",
            RootStatement = "cp6_authentication_v1.acquire()", race.FirstWait, race.RootWait,
            Status = (int)business.StatusCode, Stale = (int)stale.StatusCode,
            Before = W618State(before, f), After = W618State(after, f), Final = W618State(final, f),
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W618_distinct_approver_rejected_by_journal_leaves_linked_OA_uncommitted()
    {
        await using var h = await Seed();
        var checker = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var actor = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
        var f = await SeedW618JournalAndWorkflow(h, host, maker, checker);
        var change = await h.Db.FiscalPeriods.Where(p => p.Id == f.Period)
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.Status, PeriodStatus.Closed));
        Assert.Equal(1, change);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        CheckW618Pending(before, f);
        using var denied = await DecideW618(host, actor, f.Task);
        var body = await denied.Content.ReadFromJsonAsync<JsonElement>();
        var after = await ReadW618(observer, h.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "different approver with closed period", Tenant = h.Tenant, f.Journal, f.Task, f.Instance,
            Approver = checker, Status = (int)denied.StatusCode, Body = body, Before = W618State(before, f), After = W618State(after, f),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        Assert.Contains("E-FIN-112", body.GetProperty("message").GetString());
        Assert.Equal(before, after);
    }

    [CurrentPermissionPostgresFact]
    public async Task W618_renamed_maker_stays_distinct_from_the_actual_delegated_approver()
    {
        await using var h = await Seed();
        var checker = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await GrantLegacyAuthorityAction(h, "user", "query");
        await GrantLegacyAuthorityAction(h, "user", "edit");
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var other = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
        var f = await SeedW618JournalAndWorkflow(h, host, maker, h.User, submittedMakerId: checker);
        await GrantW618Delegate(h, h.User, checker);

        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        CheckW618Pending(before, f);
        Assert.Equal(h.User, AssetId(before.Row("journals", f.Journal), "MakerUserId"));

        const string renamed = "journal-maker-renamed";
        using var read = await host.Send(OperationsRequest(other, HttpMethod.Get, $"/api/iam/users/editor?id={h.User}"));
        Assert.True(read.StatusCode == HttpStatusCode.OK, await OperationsFailure(read));
        var editor = (await read.Content.ReadFromJsonAsync<IamUserEditorV1>())!;
        Assert.Equal(checker, editor.ActorId);
        Assert.Equal(h.User, editor.User!.Id);
        var rename = new IamUserCommandV1
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = editor.User.Version, ExpectedReferencesVersion = editor.ReferencesVersion,
            UserName = renamed, NickName = editor.User.NickName, RoleId = editor.User.RoleId, RoleIds = editor.User.RoleIds,
            DeptId = editor.User.DeptId, ManagerId = editor.User.ManagerId, Email = editor.User.Email, Reason = "rename maker",
        };
        using var updated = await host.Send(OperationsRequest(other, HttpMethod.Put, $"/api/iam/users/{h.User}", rename));
        Assert.True(updated.StatusCode == HttpStatusCode.OK, await OperationsFailure(updated));
        var renamedUser = (await updated.Content.ReadFromJsonAsync<IamOperationV1>())!;
        Assert.Equal(h.User, renamedUser.EntityId);
        Assert.Equal("COMMITTED", renamedUser.State);
        maker = await Login(host, renamed, "Price1!Password", h.Tenant.ToString("N"));

        using var ownPost = await host.Send(OperationsRequest(maker, HttpMethod.Post, $"/api/fin/journal/{f.Journal}/post"));
        await CheckApError(ownPost, "E-FIN-111", []);
        using var ownDecision = await DecideW618(host, maker, f.Task);
        await CheckW618Denied(ownDecision, "E-FIN-111");
        Assert.Equal(before, await ReadW618(observer, h.Tenant));

        using var approved = await DecideW618(host, other, f.Task, h.User);
        await CheckW618DecisionReceipt(approved, f);
        var after = await ReadW618(observer, h.Tenant);
        CheckW618Approved(after, before, f, checker, h.User);
        using var readJournal = await host.Send(OperationsRequest(other, HttpMethod.Get, $"/api/fin/journal/{f.Journal}"));
        var visible = await ReadApSuccessData(readJournal);
        Assert.Equal(f.MakerName, visible.GetProperty("makerId").GetString());
        Assert.Equal(checker.ToString("D"), visible.GetProperty("checkerId").GetString());
        Assert.False(visible.TryGetProperty("makerUserId", out _));

        using var reversed = await host.Send(OperationsRequest(maker, HttpMethod.Post, $"/api/fin/journal/{f.Journal}/reverse",
            new { Reason = "manual counter-entry" }));
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(reversed)).ValueKind);
        var reversing = await ReadW618(observer, h.Tenant);
        var original = reversing.Row("journals", f.Journal);
        var reversal = AssetId(original, "ReversedById");
        var counter = reversing.Row("journals", reversal);
        Assert.Equal((int)JournalStatus.Reversed, AssetInt(original, "Status"));
        Assert.Equal(f.Journal, AssetId(counter, "ReverseOfId"));
        Assert.Equal(h.User, AssetId(counter, "MakerUserId"));
        Assert.Equal(renamed, counter.GetProperty("MakerId").GetString());
        Assert.Equal((int)JournalStatus.PendingReview, AssetInt(counter, "Status"));
        Assert.False(counter.GetProperty("AutoPosted").GetBoolean());

        using var ownReversal = await host.Send(OperationsRequest(maker, HttpMethod.Post, $"/api/fin/journal/{reversal}/post"));
        await CheckApError(ownReversal, "E-FIN-111", []);
        Assert.Equal(reversing, await ReadW618(observer, h.Tenant));
        using var otherPost = await host.Send(OperationsRequest(other, HttpMethod.Post, $"/api/fin/journal/{reversal}/post"));
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(otherPost)).ValueKind);
        var final = await ReadW618(observer, h.Tenant);
        var posted = final.Row("journals", reversal);
        Assert.Equal(W618CheckerName, posted.GetProperty("CheckerId").GetString());
        Assert.Equal((int)JournalStatus.Posted, AssetInt(posted, "Status"));
        Assert.Equal(h.User, AssetId(posted, "MakerUserId"));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "actual account rename and delegated approver", h.Tenant, Maker = h.User, f.MakerName,
            Renamed = renamed, SpoofedBodyMaker = checker, f.Journal, f.Task, f.Instance, Checker = checker,
            OwnPost = (int)ownPost.StatusCode, OwnDecision = (int)ownDecision.StatusCode,
            OaDecision = (int)approved.StatusCode, Before = W618State(before, f), After = W618State(after, f), Reversal = posted,
        }));
    }

    [CurrentPermissionPostgresFact]
    public async Task W618_timeout_cannot_supply_a_second_human_and_real_approver_can_recover()
    {
        await using var h = await Seed();
        var checker = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var f = await SeedW618JournalAndWorkflow(h, host, maker, checker, automaticTimeout: true);
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.Id == f.Task);
        var due = task.DueAt!.Value;
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        var timeout = new WfTimeoutService(h.Db, BuildW618Engine(h.Db));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => timeout.ScanOnceAsync(due.AddSeconds(1)));
        Assert.Equal("凭证过账失败：E-FIN-111", error.Message);
        h.Db.ChangeTracker.Clear(); // The worker disposes the failed tenant scope.
        Assert.Equal(before, await ReadW618(observer, h.Tenant));

        var actor = await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"));
        using var approved = await DecideW618(host, actor, f.Task);
        await CheckW618DecisionReceipt(approved, f);
        var after = await ReadW618(observer, h.Tenant);
        CheckW618Approved(after, before, f, checker);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "legacy workflow timeout", h.Tenant, f.Journal, f.Task, f.Instance, DueAt = due,
            Error = error.Message, Recovery = (int)approved.StatusCode, Before = W618State(before, f), After = W618State(after, f),
        }));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task W618_deferred_service_keeps_the_actual_prior_human_vote(bool distinctChecker)
    {
        await using var h = await Seed();
        var checker = await SeedW618Checker(h);
        await PrepareW618Finance(h);
        await using var host = await StartW618Api(h);
        var maker = await Login(host, h);
        var actor = distinctChecker ? checker : h.User;
        var starter = distinctChecker ? h.User : checker;
        var session = distinctChecker
            ? await Login(host, W618CheckerName, W618CheckerPassword, h.Tenant.ToString("N"))
            : maker;
        var f = await SeedW618JournalAndWorkflow(h, host, maker, actor, starter: starter, deferred: true);
        using var decision = await DecideW618(host, session, f.Task);
        await CheckW618DecisionReceipt(decision, f);

        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadW618(observer, h.Tenant);
        Assert.Equal((int)JournalStatus.PendingReview, AssetInt(before.Row("journals", f.Journal), "Status"));
        Assert.Equal(FlowInstanceStatus.Running, AssetInt(before.Row("instances", f.Instance), "Status"));
        var vote = Assert.Single(before.Rows("history"), row => row.GetProperty("Action").GetString() == "approve");
        Assert.Equal(actor, AssetId(vote, "ActorId"));
        var job = await h.Db.Wf_ServiceJobs.AsNoTracking().SingleAsync(j => j.InstanceId == f.Instance);
        Assert.Equal(ServiceJobStatus.Pending, job.Status);
        Assert.Equal(ServiceKind.Timer, job.Kind);

        var worker = new WfServiceJobService(h.Db, BuildW618Engine(h.Db), []);
        var processed = await worker.ScanOnceAsync(job.NextAttemptAtUtc.AddSeconds(1), "journal-fixture");
        var after = await ReadW618(observer, h.Tenant);
        var finalJournal = after.Row("journals", f.Journal);
        var finalJob = after.Row("jobs", job.Id);
        Assert.Equal(1, AssetInt(finalJob, "AttemptCount"));
        if (distinctChecker)
        {
            Assert.Equal(1, processed);
            Assert.Equal(ServiceJobStatus.Succeeded, AssetInt(finalJob, "Status"));
            Assert.Equal((int)JournalStatus.Posted, AssetInt(finalJournal, "Status"));
            Assert.Equal(checker.ToString("D"), finalJournal.GetProperty("CheckerId").GetString());
            Assert.Equal(FlowInstanceStatus.Approved, AssetInt(after.Row("instances", f.Instance), "Status"));
        }
        else
        {
            Assert.Equal(0, processed);
            Assert.Equal(ServiceJobStatus.Running, AssetInt(finalJob, "Status"));
            foreach (var table in new[] { "journals", "instances", "tasks", "history", "tokens", "form-tos", "notifications", "lines", "periods", "numbers", "audit" })
                Assert.Equal(before.Rows(table).Select(row => row.GetRawText()), after.Rows(table).Select(row => row.GetRawText()));
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Scenario = "actual decision followed by deferred timer", h.Tenant, f.Journal, f.Task, f.Instance,
            Maker = h.User, Actor = actor, Starter = starter, job.Id, Processed = processed,
            Before = W618State(before, f), After = W618State(after, f), Job = finalJob,
        }));
    }

    private sealed record W618Fixture(Guid Journal, Guid Period, Guid Instance, Guid Task, Guid Definition, Guid Assignee, string MakerName);

    private static async Task PrepareW618Finance(Harness h)
    {
        await new GlAccountService(h.Db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        foreach (var action in new[] { "add", "submit", "post", "reverse" })
            await GrantLegacyAuthorityAction(h, "fin-journal", action);
        await GrantLegacyAuthorityAction(h, "oa-inbox", "approve");
    }

    private static async Task<Guid> SeedW618Checker(Harness h)
    {
        var checker = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = W618CheckerName, RoleId = 72,
            Password = new BCryptPasswordHasher(4).Hash(W618CheckerPassword),
        };
        h.Db.Sys_Users.Add(checker);
        await h.Db.SaveChangesAsync();
        return checker.Id;
    }

    private static async Task<W618Fixture> SeedW618JournalAndWorkflow(Harness h, AuthenticationApiProcess host,
        Dictionary<string, string> maker, Guid assignee, Guid? starter = null, bool automaticTimeout = false,
        Guid? submittedMakerId = null, bool deferred = false)
    {
        var account = await h.Db.GlAccounts.Where(a => a.Code == "1401" || a.Code == "1001")
            .ToDictionaryAsync(a => a.Code, a => a.Id);
        var draft = new JournalEntry
        {
            Id = Guid.NewGuid(), VoucherDate = W618Date, Source = VoucherSource.Manual, Description = "Historical OA-linked voucher",
            Lines =
            [
                new JournalLine { AccountId = account["1401"], Debit = W618Amount, Memo = "Native material charge" },
                new JournalLine { AccountId = account["1001"], Credit = W618Amount, Memo = "Native cash outflow" },
            ],
        };
        var body = new { draft.Id, draft.VoucherDate, draft.Source, draft.Description, draft.Lines, MakerUserId = submittedMakerId };
        using var created = await host.Send(OperationsRequest(maker, HttpMethod.Post, "/api/fin/journal", body));
        var id = (await ReadApSuccessData(created)).GetProperty("id").GetGuid();
        Assert.Equal(draft.Id, id);
        using var submitted = await host.Send(OperationsRequest(maker, HttpMethod.Post, $"/api/fin/journal/{id}/submit"));
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(submitted)).ValueKind);
        var recorded = await h.Db.JournalEntries.AsNoTracking().SingleAsync(e => e.Id == id);
        var name = await h.Db.Sys_Users.AsNoTracking().Where(u => u.Id == h.User).Select(u => u.UserName).SingleAsync();
        Assert.Equal(name, recorded.MakerId);

        var definition = new FlowSchema
        {
            Start = "start",
            Nodes =
            [
                new() { Id = "start", Type = "start" },
                new() { Id = "approval", ApproverStrategy = "Specified", ApproverUserId = assignee,
                    TimeoutHours = automaticTimeout ? 1 : null, TimeoutAction = automaticTimeout ? "approve" : null },
                new() { Id = "end", Type = "end" },
            ],
            Edges = [new() { From = "start", To = "approval" }, new() { From = "approval", To = deferred ? "timer" : "end" }],
        };
        if (deferred)
        {
            definition.Nodes.Add(new()
            {
                Id = "timer", Type = "serviceTask", ServiceKind = ServiceKind.Timer,
                ServiceDelayMode = "duration", ServiceDelayValue = "PT1H",
            });
            definition.Edges.Add(new() { From = "timer", To = "end" });
        }
        var head = await LegacyDefinitionFixture.Flow(h.Db, W618FlowKey, "Simulated historical journal approval", null,
            JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var version = await h.Db.Wf_FlowDefVersions.AsNoTracking().Where(v => v.FlowDefId == head && v.Status == WfDefinitionVersionStatus.Published)
            .Select(v => v.Id).SingleAsync();
        h.Db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { FlowKey = W618FlowKey, BizType = W618BizType, Enable = true });
        await h.Db.SaveChangesAsync();
        IApprovalService approval = new ApprovalService(h.Db, BuildW618Engine(h.Db));
        var instance = await approval.SubmitAsync(W618BizType, id.ToString("D"), starter ?? h.User, new { journal = id });
        var task = await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync(t => t.InstanceId == instance);
        Assert.Equal(assignee, task.AssigneeId);
        h.Db.ChangeTracker.Clear();
        return new(id, recorded.PeriodId, instance, task.Id, version, assignee, name);
    }

    private static FlowEngine BuildW618Engine(CP6Context db)
    {
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db));
        return new(db, new ApproverResolver(db), dispatcher: new ApprovalDispatcher([new JournalApprovalCallback(journal, db)]));
    }

    private static Task<AuthenticationApiProcess> StartW618Api(Harness h)
    {
        var environment = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant);
        if (Environment.GetEnvironmentVariable("CP6_JOURNAL_APPROVAL_HTTP_PORT") is { Length: > 0 } port)
            environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment);
    }

    private static async Task<HttpResponseMessage> DecideW618(AuthenticationApiProcess host, Dictionary<string, string> actor,
        Guid task, Guid? actingAs = null)
    {
        using var request = OperationsRequest(actor, HttpMethod.Post, $"/api/oa/tasks/{task}/decision",
            new { Decision = "approve", Comment = W618Comment, DataPatch = new { } });
        if (actingAs is Guid assignee)
            request.Headers.Add("X-Acting-As", assignee.ToString("D"));
        return await host.Send(request);
    }

    private static async Task CheckW618Denied(HttpResponseMessage response, string code)
    {
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await OperationsFailure(response));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"凭证过账失败：{code}", body.GetProperty("message").GetString());
    }

    private static async Task GrantW618Delegate(Harness h, Guid assignee, Guid actor)
    {
        var now = DateTime.Now;
        await new DelegateService(h.Db).AddDelegateAsync(assignee, actor, now.AddMinutes(-5), now.AddDays(1), null, null);
        h.Db.ChangeTracker.Clear();
    }

    private static async Task CheckW618DecisionReceipt(HttpResponseMessage response, W618Fixture f)
    {
        var data = await ReadApSuccessData(response);
        Assert.Equal(f.Task, data.GetProperty("taskId").GetGuid());
        Assert.Equal(f.Instance, data.GetProperty("instanceId").GetGuid());
        Assert.Equal((int)FlowTaskStatus.Approved, data.GetProperty("taskStatus").GetInt32());
    }

    private static async Task<AssetRows> ReadW618(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'instances', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowInstance" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'tasks', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowTask" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'history', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowHistory" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'tokens', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowToken" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'form-tos', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowFormTo" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'notifications', ARRAY(SELECT to_jsonb(r) FROM public."Wf_Notification" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'jobs', ARRAY(SELECT to_jsonb(r) FROM public."Wf_ServiceJob" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('JournalEntry','JournalLine','FiscalPeriod','FinSequence','Wf_FlowInstance','Wf_FlowTask','Wf_ApprovalBinding')
                ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static object W618State(AssetRows rows, W618Fixture f) => new
    {
        Journal = rows.Row("journals", f.Journal),
        Task = rows.Row("tasks", f.Task),
        Instance = rows.Row("instances", f.Instance),
        History = rows.Rows("history"),
        FormTos = rows.Rows("form-tos"),
        Notifications = rows.Rows("notifications"),
        Totals = new
        {
            journals = rows.Rows("journals").Length, lines = rows.Rows("lines").Length, periods = rows.Rows("periods").Length,
            sequences = ApSequences(rows), jobs = rows.Rows("jobs").Length, audit = rows.Rows("audit").Length,
        },
    };

    private static void CheckW618Pending(AssetRows state, W618Fixture f)
    {
        var journal = state.Row("journals", f.Journal);
        Assert.Equal(f.MakerName, journal.GetProperty("MakerId").GetString());
        Assert.Equal((int)JournalStatus.PendingReview, AssetInt(journal, "Status"));
        Assert.Equal(JsonValueKind.Null, journal.GetProperty("CheckerId").ValueKind);
        Assert.Equal((int)VoucherSource.Manual, AssetInt(journal, "Source"));
        var lines = state.Rows("lines");
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.Equal(f.Journal, AssetId(line, "EntryId")));
        Assert.Equal(W618Amount, lines.Sum(l => AssetDecimal(l, "Debit")));
        Assert.Equal(W618Amount, lines.Sum(l => AssetDecimal(l, "Credit")));
        var flow = state.Row("instances", f.Instance);
        Assert.Equal(f.Definition, AssetId(flow, "FlowDefVersionId"));
        Assert.Equal(W618BizType, flow.GetProperty("BizType").GetString());
        Assert.Equal(f.Journal.ToString("D"), flow.GetProperty("BizId").GetString());
        Assert.Equal((int)FlowInstanceStatus.Running, AssetInt(flow, "Status"));
        var task = state.Row("tasks", f.Task);
        Assert.Equal(f.Assignee, AssetId(task, "AssigneeId"));
        Assert.Equal((int)FlowTaskStatus.Pending, AssetInt(task, "Status"));
        Assert.Equal("submit", Assert.Single(state.Rows("history")).GetProperty("Action").GetString());
        Assert.Empty(state.Rows("notifications"));
    }

    private static void CheckW618Approved(AssetRows after, AssetRows before, W618Fixture f, Guid approver, Guid? actingAs = null)
    {
        var journal = Assert.Single(after.Rows("journals"));
        Assert.Equal(f.Journal, AssetId(journal));
        Assert.Equal(f.MakerName, journal.GetProperty("MakerId").GetString());
        Assert.Equal((int)JournalStatus.Posted, AssetInt(journal, "Status"));
        Assert.Equal(approver.ToString("D"), journal.GetProperty("CheckerId").GetString());
        Assert.NotEqual(JsonValueKind.Null, journal.GetProperty("CheckerAt").ValueKind);
        Assert.False(journal.GetProperty("AutoPosted").GetBoolean());
        var fields = new[] { "Status", "CheckerId", "CheckerAt", "ModifyDate" };
        Assert.Equal(before.Row("journals", f.Journal).EnumerateObject().Where(p => !fields.Contains(p.Name)).Select(p => p.ToString()),
            journal.EnumerateObject().Where(p => !fields.Contains(p.Name)).Select(p => p.ToString()));

        foreach (var table in new[] { "lines", "periods", "numbers", "jobs", "audit" })
            Assert.Equal(before.Rows(table).Select(x => x.GetRawText()), after.Rows(table).Select(x => x.GetRawText()));
        Assert.Equal((int)FlowInstanceStatus.Approved, AssetInt(Assert.Single(after.Rows("instances")), "Status"));
        var task = after.Row("tasks", f.Task);
        Assert.Equal((int)FlowTaskStatus.Approved, AssetInt(task, "Status"));
        Assert.Equal(f.Assignee, AssetId(task, "AssigneeId"));
        Assert.Equal(approver.ToString("D"), task.GetProperty("Modifier").GetString());
        var history = after.Rows("history");
        Assert.Equal(new[] { "approve", "end", "submit" },
            history.Select(entry => entry.GetProperty("Action").GetString()).Order(StringComparer.Ordinal));
        var ended = Assert.Single(history, entry => entry.GetProperty("Action").GetString() == "end");
        Assert.Equal(AssetId(Assert.Single(after.Rows("instances")), "StarterId"), AssetId(ended, "ActorId"));
        var approval = Assert.Single(after.Rows("history"), entry => entry.GetProperty("Action").GetString() == "approve");
        Assert.Equal((approver, W618Comment), (AssetId(approval, "ActorId"), approval.GetProperty("Comment").GetString()));
        Assert.Equal(before.Row("history", AssetId(Assert.Single(before.Rows("history")))).GetRawText(),
            after.Row("history", AssetId(Assert.Single(before.Rows("history")))).GetRawText());
        var formTo = Assert.Single(after.Rows("form-tos"));
                Assert.Equal((approver, f.Assignee, 1),
            (AssetId(formTo, "ActualHandlerId"), AssetId(formTo, "ExpectedHandlerId"), AssetInt(formTo, "Status")));
        var onBehalfOf = formTo.GetProperty("OnBehalfOfId");
        Assert.Equal(actingAs, onBehalfOf.ValueKind == JsonValueKind.Null ? (Guid?)null : onBehalfOf.GetGuid());
        var notification = Assert.Single(after.Rows("notifications"));
        Assert.Equal(WfNotificationType.FlowApproved, AssetInt(notification, "Type"));
        Assert.Equal(f.Instance, AssetId(notification, "InstanceId"));
        Assert.Equal(AssetId(Assert.Single(after.Rows("instances")), "StarterId"), AssetId(notification, "UserId"));
    }
}
