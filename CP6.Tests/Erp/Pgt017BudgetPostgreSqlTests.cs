using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Fin;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string BudgetMenu = "fin-budget";
    private const string BudgetBizType = "A5_Budget";
    private const int BudgetYear = 2039;
    private static readonly decimal[] OriginalBudgetAmounts = Enumerable.Range(1, 12).Select(n => n * 2.13m).ToArray();
    private static readonly decimal[] EditedBudgetAmounts = [1234567890123456.78m, 0.21m, -0.09m, 17.23m];
    private static readonly decimal[] FirstBudgetImportAmounts = [12345.67m, 0.12m, -0.31m, 8.47m];
    private static readonly decimal[] SecondBudgetImportAmounts = [17.89m, 194.61m, 0m, 2.01m];

    [TenantManagementPostgresTheory]
    [InlineData("budget-create", false)]
    [InlineData("budget-create", true)]
    [InlineData("budget-update", false)]
    [InlineData("budget-update", true)]
    [InlineData("budget-deactivate", false)]
    [InlineData("budget-deactivate", true)]
    [InlineData("version-create", false)]
    [InlineData("version-create", true)]
    [InlineData("version-update", false)]
    [InlineData("version-update", true)]
    [InlineData("version-submit", false)]
    [InlineData("version-submit", true)]
    [InlineData("version-activate", false)]
    [InlineData("version-activate", true)]
    [InlineData("version-delete", false)]
    [InlineData("version-delete", true)]
    [InlineData("line-upsert", false)]
    [InlineData("line-upsert", true)]
    [InlineData("line-delete", false)]
    [InlineData("line-delete", true)]
    [InlineData("line-import", false)]
    [InlineData("line-import", true)]
    public async Task PGT017_budget_write_and_pause_share_commit_order(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var fixture = await SeedBudget(h, operation);
        var command = BudgetWriteFor(operation, fixture);
        await GrantLegacyAuthorityAction(h, BudgetMenu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("budget-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.Table, command.SqlVerb, "Sys_Tenants", "UPDATE", pauseFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        using var result = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var state = await ReadBudgetWrites(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(before, state);
        }
        else
        {
            await CheckBudgetSuccess(result, operation, fixture, before, state);
        }
        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(state, await ReadBudgetWrites(observer, h.Tenant));
        if (pauseFirst)
        {
            var resume = new TenantManagementCommandV1("budget-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var activeAgain = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), resume));
            Assert.Equal(HttpStatusCode.OK, activeAgain.StatusCode);
            actor = await Login(host, h);
            using var accepted = await Write();
            await CheckBudgetSuccess(accepted, operation, fixture, before, await ReadBudgetWrites(observer, h.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, PauseFirst = pauseFirst, race.FirstWait,
            race.RootWait, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(BudgetRequest(actor, command));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_budget_submit_and_action_only_withdrawal_share_commit_order(bool revokeFirst)
    {
        const string operation = "version-submit";
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var fixture = await SeedBudget(h, operation);
        var command = BudgetWriteFor(operation, fixture);
        await GrantLegacyAuthorityAction(h, BudgetMenu, command.Action);
        await GrantLegacyAuthorityAction(h, BudgetMenu, "edit");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, command.Action);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var race = await ObserveAssetRace(connection, observer, command.Table, command.SqlVerb, "Sys_RoleAction", "DELETE",
            revokeFirst, Write, () => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)),
            async () =>
            {
                Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
                Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            });

        using var changed = race.Authority;
        using var result = race.Business;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, policy.RoleRevision);
        Assert.Equal((authority.Generation + 1, authority.Snapshot + 1, authority.Outbox + 1, 1L, 1L, 1L),
            (policy.Generation, policy.Snapshot, policy.Outbox, policy.CurrentOutbox, policy.Audits, policy.Receipts));
        Assert.Equal(authority.Actions.Split(',').Where(p => p != command.Action).Order(), policy.Actions.Split(',').Order());
        Assert.Contains("edit", policy.Actions.Split(','));
        var committed = await ReadBudgetWrites(observer, h.Tenant);
        if (revokeFirst)
        {
            await AssertBudgetDenial(result, "IAM_FORBIDDEN");
            Assert.Equal(before, committed);
        }
        else
        {
            await CheckBudgetSuccess(result, operation, fixture, before, committed);
        }
        using var stale = await Write();
        await AssertBudgetDenial(stale, "无权限：fin-budget:submit");
        Assert.Equal(committed, await ReadBudgetWrites(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, RevokeFirst = revokeFirst, race.FirstWait,
            race.RootWait, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(BudgetRequest(actor, command));
    }

    [TenantManagementPostgresTheory]
    [InlineData("tenant")]
    [InlineData("center")]
    [InlineData("copy")]
    [InlineData("copy-center")]
    public async Task PGT017_budget_new_rows_and_explicit_dimensions_use_current_tenant(string kind)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var f = await SeedBudget(h, "isolation");
        var other = await SeedBudget(foreign, "isolation");
        await GrantLegacyAuthorityAction(h, BudgetMenu, "add");
        await GrantLegacyAuthorityAction(h, BudgetMenu, "edit");
        if (kind == "copy-center")
            await h.Db.BudgetLines.Where(line => line.Id == f.Line).ExecuteUpdateAsync(set => set
                .SetProperty(line => line.CostCenterId, other.Center).SetProperty(line => line.CostCenterKey, other.Center));
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var foreignBefore = await ReadBudgetWrites(observer, foreign.Tenant);

        if (kind == "tenant")
        {
            var create = BudgetWriteFor("budget-create", f) with { Body = BudgetCreateData(f, foreign.Tenant) };
            using var result = await host.Send(BudgetRequest(actor, create));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(foreignBefore, await ReadBudgetWrites(observer, foreign.Tenant));
            await CheckBudgetSuccess(result, "budget-create", f, before, await ReadBudgetWrites(observer, h.Tenant));
        }
        else if (kind == "center")
        {
            var upsert = BudgetWriteFor("line-upsert", f) with { Body = BudgetLineInput(f, other.Center) };
            using var rejected = await host.Send(BudgetRequest(actor, upsert));
            await AssertBudgetError(rejected, "E-A5-LINE-005");
            Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
            Assert.Equal(foreignBefore, await ReadBudgetWrites(observer, foreign.Tenant));
        }
        else
        {
            var source = kind == "copy" ? other.Version : f.Version;
            var copy = BudgetWriteFor("version-create", f) with
            {
                Body = new { Name = "Copy budget", CopyFromVersionId = source }
            };
            using var rejected = await host.Send(BudgetRequest(actor, copy));
            await AssertBudgetError(rejected, kind == "copy" ? "E-A5-VERSION-404" : "E-A5-LINE-005");
            Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
            Assert.Equal(foreignBefore, await ReadBudgetWrites(observer, foreign.Tenant));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_existing_legacy_bucket_and_empty_optional_keys_keep_their_contract()
    {
        await using var h = await Seed();
        var f = await SeedBudget(h, "line-upsert");
        var historic = Guid.NewGuid();
        await h.Db.BudgetLines.Where(l => l.Id == f.Line).ExecuteUpdateAsync(set => set
            .SetProperty(l => l.CostCenterId, historic).SetProperty(l => l.CostCenterKey, historic));
        var lineToken = await h.Db.BudgetLines.Where(l => l.Id == f.Line).Select(l => l.RowVersion).SingleAsync();
        await GrantLegacyAuthorityAction(h, BudgetMenu, "edit");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var edit = BudgetLineInput(f, historic);
        edit.RowVersion = lineToken;
        using var updated = await host.Send(BudgetRequest(actor, BudgetWriteFor("line-upsert", f) with { Body = edit }));
        var committed = await ReadBudgetWrites(observer, h.Tenant);
        await CheckBudgetSuccess(updated, "line-upsert", f, before, committed);
        Assert.Equal(historic, AssetId(committed.Row("lines", f.Line), "CostCenterId"));

        var current = await h.Db.BudgetVersions.Where(v => v.Id == f.Version).Select(v => v.RowVersion).SingleAsync();
        var optional = BudgetLineInput(f with { VersionToken = current! }, Guid.Empty);
        optional.RowVersion = null;
        optional.CostObjectType = "";
        optional.CostObjectId = "";
        using var accepted = await host.Send(BudgetRequest(actor, BudgetWriteFor("line-upsert", f) with { Body = optional }));
        await BudgetPayload(accepted);
        var final = await ReadBudgetWrites(observer, h.Tenant);
        var wildcard = Assert.Single(final.Rows("lines"), row => AssetId(row) != f.Line);
        Assert.Equal(Guid.Empty, AssetId(wildcard, "CostCenterId"));
        Assert.Equal(Guid.Empty, AssetId(wildcard, "CostCenterKey"));
        Assert.Equal("", wildcard.GetProperty("CostObjectTypeKey").GetString());
        Assert.Equal("", wildcard.GetProperty("CostObjectIdKey").GetString());
        Assert.Equal(committed.Row("lines", f.Line).GetRawText(), final.Row("lines", f.Line).GetRawText());
        CheckPeriods(final, wildcard, EditedBudgetAmounts);
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_valid_workbook_preview_is_read_only_and_stale_confirm_changes_nothing()
    {
        await using var h = await Seed();
        var f = await SeedBudget(h, "line-import");
        await GrantLegacyAuthorityAction(h, BudgetMenu, "import");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var command = BudgetWriteFor("line-import", f);
        using var preview = await host.Send(BudgetRequest(actor, command with { Path = $"api/fin/budget/lines/import/preview?versionId={f.Version:D}" }));
        var data = await BudgetPayload(preview);
        Assert.False(data.GetProperty("hasFatal").GetBoolean());
        Assert.Equal(2, data.GetProperty("rows").GetArrayLength());
        Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));

        var staleVersion = f.VersionToken.ToArray();
        staleVersion[^1] ^= 1;
        using var rejected = await host.Send(BudgetRequest(actor, BudgetWriteFor("line-import", f with { VersionToken = staleVersion })));
        await AssertBudgetError(rejected, "E-A5-CONCURRENCY-001");
        Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
        using var confirmed = await host.Send(BudgetRequest(actor, command));
        await CheckBudgetSuccess(confirmed, "line-import", f, before, await ReadBudgetWrites(observer, h.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_line_and_version_tokens_are_checked_as_independent_native_values()
    {
        await using var h = await Seed();
        var f = await SeedBudget(h, "line-delete");
        Assert.Equal(8, f.VersionToken.Length);
        Assert.Equal(8, f.LineToken.Length);
        await GrantLegacyAuthorityAction(h, BudgetMenu, "edit");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var staleLine = f.LineToken.ToArray();
        var staleVersion = f.VersionToken.ToArray();
        staleLine[^1] ^= 1;
        staleVersion[^1] ^= 1;

        foreach (var changed in new[] { f with { LineToken = staleLine }, f with { VersionToken = staleVersion } })
        {
            foreach (var operation in new[] { "line-upsert", "line-delete" })
            {
                using var denied = await host.Send(BudgetRequest(actor, BudgetWriteFor(operation, changed)));
                await AssertBudgetError(denied, "E-A5-CONCURRENCY-001");
                Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_immediate_published_approval_preserves_callback_result()
    {
        await using var h = await Seed();
        var fixture = await SeedBudget(h, "version-submit", """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"end"}]}
            """);
        await GrantLegacyAuthorityAction(h, BudgetMenu, "submit");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        using var result = await host.Send(BudgetRequest(actor, BudgetWriteFor("version-submit", fixture)));
        await BudgetPayload(result);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var state = await ReadBudgetWrites(observer, h.Tenant);
        var instance = Assert.Single(state.Rows("workflows"));
        Assert.Equal((int)FlowInstanceStatus.Approved, AssetInt(instance, "Status"));
        Assert.Equal(fixture.Definition, AssetId(instance, "FlowDefVersionId"));
        var target = state.Row("versions", fixture.Version);
        Assert.Equal(AssetId(instance), AssetId(target, "ApprovalInstanceId"));
        Assert.Equal((int)BudgetVersionStatus.Approved, AssetInt(target, "Status"));
        Assert.True(target.GetProperty("IsActive").GetBoolean());
        Assert.Equal((int)BudgetVersionStatus.Archived, AssetInt(state.Row("versions", fixture.ActiveVersion), "Status"));
        Assert.Empty(state.Rows("tasks"));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_actual_copy_rejects_a_late_invalid_bucket_with_full_rollback()
    {
        await using var h = await Seed();
        var f = await SeedBudget(h, "version-create");
        var invalidAccount = new GlAccount
        {
            Code = "W606-HISTORICAL", Name = "Old group account", Type = AccountType.Expense, IsLeaf = false,
        };
        var period = new FiscalPeriod
        {
            FiscalYear = BudgetYear - 1, Year = BudgetYear - 1, Month = 1, PeriodNo = 1, Status = PeriodStatus.Open,
            PeriodStart = new(BudgetYear - 1, 1, 1), PeriodEnd = new(BudgetYear - 1, 1, 31),
        };
        h.Db.GlAccounts.Add(invalidAccount);
        h.Db.FiscalPeriods.Add(period);
        await h.Db.SaveChangesAsync();
        // A previously posted line may refer to an account that later became a group.
        h.Db.JournalEntries.Add(new JournalEntry
        {
            No = "W606-000001", PeriodId = period.Id, VoucherDate = period.PeriodStart, Status = JournalStatus.Posted,
            Lines = [new() { LineNo = 1, AccountId = f.Expense, Debit = 92.37m },
                new() { LineNo = 2, AccountId = invalidAccount.Id, Debit = 25.17m },
                new() { LineNo = 3, AccountId = f.Revenue, Credit = 117.54m }],
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await GrantLegacyAuthorityAction(h, BudgetMenu, "add");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var copy = BudgetWriteFor("version-create", f) with
        {
            Body = new { Name = "Prior year actual", CopyFromActualFiscalYear = BudgetYear - 1 }
        };
        using var failed = await host.Send(BudgetRequest(actor, copy));
        await AssertBudgetError(failed, "E-A5-LINE-002");
        Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_native_start_defers_the_connector_and_rolls_back_the_linked_graph()
    {
        await using var h = await Seed();
        var f = await SeedBudget(h, "version-submit", """
            {"start":"start","nodes":[{"id":"start","type":"start"},
             {"id":"connector","type":"serviceTask","serviceKind":"webApi","serviceMode":"sync",
              "serviceConnectorName":"budget-test","servicePath":"observe","serviceHttpMethod":"POST",
              "serviceParamsJson":"{\"purpose\":\"budget\"}"},
             {"id":"end","type":"end"}],
             "edges":[{"from":"start","to":"connector"},{"from":"connector","to":"end"}]}
            """);
        var requests = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var connector = builder.Build();
        connector.MapPost("/observe", () =>
        {
            Interlocked.Increment(ref requests);
            return Results.Json(new { received = true });
        });
        await connector.StartAsync();
        var address = connector.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        h.Db.Wf_Connectors.Add(new() { Name = "budget-test", BaseUrl = address, TimeoutSec = 5, Enabled = true });
        await h.Db.SaveChangesAsync();
        await GrantLegacyAuthorityAction(h, BudgetMenu, "submit");
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var command = BudgetWriteFor("version-submit", f);

        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.cp6_test_budget_commit_reject() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'budget_fixture_commit_rejected' USING ERRCODE='23514'; END $$;
            CREATE CONSTRAINT TRIGGER cp6_test_budget_commit_reject
            AFTER INSERT ON public."Wf_FlowInstance" DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION public.cp6_test_budget_commit_reject();
            """);
        try
        {
            using var failed = await host.Send(BudgetRequest(actor, command));
            Assert.Equal(0, Volatile.Read(ref requests));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER cp6_test_budget_commit_reject ON public."Wf_FlowInstance";
                DROP FUNCTION public.cp6_test_budget_commit_reject();
                """);
        }

        using var accepted = await host.Send(BudgetRequest(actor, command));
        await BudgetPayload(accepted);
        Assert.Equal(0, Volatile.Read(ref requests));
        var committed = await ReadBudgetWrites(observer, h.Tenant);
        var pending = committed.Row("versions", f.Version);
        var instance = Assert.Single(committed.Rows("workflows"));
        var job = Assert.Single(committed.Rows("jobs"));
        Assert.Equal((int)BudgetVersionStatus.PendingApproval, AssetInt(pending, "Status"));
        Assert.Equal(AssetId(instance), AssetId(pending, "ApprovalInstanceId"));
        Assert.Equal(AssetId(instance), AssetId(job, "InstanceId"));
        Assert.Equal(f.Tenant, AssetId(job, "TenantId"));
        Assert.Equal("deferredWebApiStart", job.GetProperty("Kind").GetString());
        Assert.Equal(0, AssetInt(job, "AttemptCount"));
        Assert.Equal((int)ServiceJobStatus.Pending, AssetInt(job, "Status"));
        Assert.Empty(committed.Rows("tasks"));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_budget_subflow_followup_observes_the_committed_financial_submission()
    {
        await using var h = await Seed();
        var childDefinition = await SeedBudgetDefinition(h, "w606-instant-child", """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"end"}]}
            """);
        var f = await SeedBudget(h, "version-submit", """
            {"start":"start","nodes":[{"id":"start","type":"start"},
             {"id":"child","type":"subFlow","subFlowKey":"w606-instant-child"},
             {"id":"approve","type":"approval","approverStrategy":"Specified","approverUserId":"__APPROVER__"},
             {"id":"end","type":"end"}],
             "edges":[{"from":"start","to":"child"},{"from":"child","to":"approve"},{"from":"approve","to":"end"}]}
            """.Replace("__APPROVER__", h.User.ToString("D"), StringComparison.Ordinal));
        h.Db.Wf_FlowDefVersionDependencies.Add(new()
        {
            FlowDefVersionId = f.Definition,
            NodeId = "child",
            DependencyType = "SubFlow",
            TargetFlowDefVersionId = childDefinition,
        });
        await h.Db.SaveChangesAsync();
        await GrantLegacyAuthorityAction(h, BudgetMenu, "submit");
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> submit;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            await using var lockTask = new NpgsqlCommand("""LOCK TABLE public."Wf_FlowTask" IN SHARE MODE""", barrier, hold);
            await lockTask.ExecuteNonQueryAsync();
            submit = host.Send(BudgetRequest(actor, BudgetWriteFor("version-submit", f)));
            var wait = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_FlowTask%", null, submit);
            var committed = await ReadBudgetWrites(observer, h.Tenant);
            var version = committed.Row("versions", f.Version);
            Assert.Equal((int)BudgetVersionStatus.PendingApproval, AssetInt(version, "Status"));
            var parent = committed.Row("workflows", AssetId(version, "ApprovalInstanceId"));
            var child = Assert.Single(committed.Rows("workflows"), row => AssetId(row) != AssetId(parent));
            Assert.Equal(AssetId(parent), AssetId(child, "ParentInstanceId"));
            Assert.Equal((int)FlowInstanceStatus.Approved, AssetInt(child, "Status"));
            Assert.Empty(committed.Rows("tasks"));
            Assert.Equal((int)ServiceJobStatus.Pending, AssetInt(Assert.Single(committed.Rows("jobs")), "Status"));
            output.WriteLine(JsonSerializer.Serialize(new { Operation = "budget-subflow", AfterFinancialCommit = wait }));
            await hold.RollbackAsync();
        }
        using var result = await submit;
        await BudgetPayload(result);
        var state = await ReadBudgetWrites(observer, h.Tenant);
        var task = Assert.Single(state.Rows("tasks"));
        Assert.Equal(f.User, AssetId(task, "AssigneeId"));
        Assert.Equal((int)ServiceJobStatus.Succeeded, AssetInt(Assert.Single(state.Rows("jobs")), "Status"));
        Assert.Contains(state.Rows("history"), entry => entry.GetProperty("Action").GetString() == "subFlowResumed");
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_budget_linked_legacy_OA_decision_and_pause_share_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var f = await SeedBudget(h, "version-submit");
        await GrantLegacyAuthorityAction(h, BudgetMenu, "submit");
        await GrantLegacyAuthorityAction(h, "oa-inbox", "approve");
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartBudgetApi(h);
        var actor = await Login(host, h);
        using var submit = await host.Send(BudgetRequest(actor, BudgetWriteFor("version-submit", f)));
        await BudgetPayload(submit);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var initial = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("budget-decision-pause", initial.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadBudgetWrites(observer, h.Tenant);
        var task = AssetId(Assert.Single(before.Rows("tasks")));
        var race = await ObserveAssetRace(connection, observer, "Fin_BudgetVersion", "UPDATE", "Sys_Tenants", "UPDATE", pauseFirst,
            Decide, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadBudgetWrites(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var result = race.Business;
        using var paused = race.Authority;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var after = await ReadBudgetWrites(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(before, after);
        }
        else
        {
            await BudgetPayload(result);
            var approved = after.Row("versions", f.Version);
            Assert.Equal((int)BudgetVersionStatus.Approved, AssetInt(approved, "Status"));
            Assert.True(approved.GetProperty("IsActive").GetBoolean());
            Assert.Equal(f.User.ToString(), approved.GetProperty("ApprovedBy").GetString());
            Assert.Equal((int)BudgetVersionStatus.Archived, AssetInt(after.Row("versions", f.ActiveVersion), "Status"));
            Assert.Equal((int)FlowInstanceStatus.Approved, AssetInt(Assert.Single(after.Rows("workflows")), "Status"));
            Assert.Equal((int)FlowTaskStatus.Approved, AssetInt(after.Row("tasks", task), "Status"));
            Assert.Contains(after.Rows("history"), history => history.GetProperty("Action").GetString() == "approve"
                && AssetId(history, "ActorId") == f.User);
        }
        output.WriteLine(JsonSerializer.Serialize(new { Operation = "legacy-oa-budget-decision", PauseFirst = pauseFirst,
            race.FirstWait, race.RootWait, Status = (int)result.StatusCode }));

        Task<HttpResponseMessage> Decide() => host.Send(OperationsRequest(actor, HttpMethod.Post, $"api/oa/tasks/{task:D}/decision",
            new { Decision = "approve", Comment = "Approved budget", DataPatch = new { } }));
    }

    private sealed record BudgetFixture(Guid Tenant, Guid User, Guid Budget, Guid Version, Guid ActiveVersion, Guid Line,
        Guid Expense, Guid Revenue, Guid Center, Guid CreatedBudget, Guid Definition, byte[] VersionToken, byte[] LineToken);
    private sealed record BudgetMutation(string Action, HttpMethod Method, string Path, object? Body, string Table, string SqlVerb,
        byte[]? Workbook = null);
    private sealed record BudgetWrites(string Data)
    {
        internal JsonElement[] Rows(string table)
        {
            using var json = JsonDocument.Parse(Data);
            return json.RootElement.GetProperty(table).EnumerateArray().Select(row => row.Clone()).ToArray();
        }

        internal JsonElement Row(string table, Guid id) => Assert.Single(Rows(table), row => AssetId(row) == id);
    }

    private static async Task<BudgetFixture> SeedBudget(Harness h, string operation, string? schema = null)
    {
        var db = h.Db;
        var plan = new Budget { Id = Guid.NewGuid(), No = "W606-ORIGINAL", Name = "Original budget", FiscalYear = BudgetYear };
        var active = new BudgetVersion
        {
            Id = Guid.NewGuid(), BudgetId = plan.Id, VersionNo = 1, Name = "Previous approved",
            Status = BudgetVersionStatus.Approved, IsActive = true,
        };
        var version = new BudgetVersion
        {
            Id = Guid.NewGuid(), BudgetId = plan.Id, VersionNo = 2, Name = "Original version",
            Status = operation == "version-activate" ? BudgetVersionStatus.Approved : BudgetVersionStatus.Draft,
        };
        var expense = new GlAccount
        {
            Id = Guid.NewGuid(), Code = "W606-EXP", Name = "Expense", Type = AccountType.Expense, NormalSide = AccountSide.Debit,
        };
        var revenue = new GlAccount
        {
            Id = Guid.NewGuid(), Code = "W606-REV", Name = "Revenue", Type = AccountType.Revenue, NormalSide = AccountSide.Credit,
        };
        var center = new CostCenter { Id = Guid.NewGuid(), Code = "W606-CENTER", Name = "Budget center", Type = CostCenterType.Department };
        var line = new BudgetLine
        {
            Id = Guid.NewGuid(), VersionId = version.Id, AccountId = expense.Id, AnnualAmount = OriginalBudgetAmounts.Sum(),
            ControlMode = BudgetControlMode.Warn, ControlBasis = BudgetControlBasis.Period, Memo = "original line",
        };
        line.NormalizeKeys();
        db.Budgets.Add(plan);
        db.BudgetVersions.AddRange(version, active);
        db.GlAccounts.AddRange(expense, revenue);
        db.CostCenters.Add(center);
        db.BudgetLines.Add(line);
        db.BudgetLinePeriods.AddRange(OriginalBudgetAmounts.Select((amount, index) => new BudgetLinePeriod
        {
            BudgetLineId = line.Id, PeriodNo = index + 1, Amount = amount,
        }));
        var definition = operation == "version-submit"
            ? await SeedBudgetWorkflow(h, schema)
            : Guid.Empty;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new(h.Tenant, h.User, plan.Id, version.Id, active.Id, line.Id, expense.Id, revenue.Id, center.Id,
            Guid.NewGuid(), definition, version.RowVersion!, line.RowVersion!);
    }

    private static async Task<Guid> SeedBudgetWorkflow(Harness h, string? schema)
    {
        schema ??= """
            {"start":"start","nodes":[{"id":"start","type":"start"},{"id":"approve","type":"approval",
            "approverStrategy":"Specified","approverUserId":"__APPROVER__"},{"id":"end","type":"end"}],
            "edges":[{"from":"start","to":"approve"},{"from":"approve","to":"end"}]}
            """.Replace("__APPROVER__", h.User.ToString("D"), StringComparison.Ordinal);
        const string flowKey = "budget-approve";
        h.Db.Wf_ApprovalBindings.Add(new Wf_ApprovalBinding { BizType = BudgetBizType, FlowKey = flowKey, Enable = true });
        return await SeedBudgetDefinition(h, flowKey, schema);
    }

    private static async Task<Guid> SeedBudgetDefinition(Harness h, string flowKey, string schema)
    {
        var head = new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = "Fixture budget approval", FormKey = "BudgetApproval",
            SchemaJson = schema, Version = 1, Enable = true,
        };
        var version = new Wf_FlowDefVersion
        {
            Id = Guid.NewGuid(), FlowDefId = head.Id, Version = 1, Status = WfDefinitionVersionStatus.Published,
            FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson,
        };
        h.Db.Wf_FlowDefs.Add(head);
        h.Db.Wf_FlowDefVersions.Add(version);
        await h.Db.SaveChangesAsync();
        return version.Id;
    }

    private static Budget BudgetCreateData(BudgetFixture f, Guid tenant = default) => new()
    {
        Id = f.CreatedBudget, TenantId = tenant, FiscalYear = BudgetYear + 1, Name = "New budget", Description = "from fixture",
        IsActive = false,
    };

    private static BudgetLineDto BudgetLineInput(BudgetFixture f, Guid? costCenter = null) => new()
    {
        VersionId = f.Version, AccountId = f.Expense, CostCenterId = costCenter, SpreadMode = "manual",
        Periods = EditedBudgetAmounts, ControlMode = BudgetControlMode.Block, ControlBasis = BudgetControlBasis.Ytd,
        Memo = "revised with exact amount", RowVersion = costCenter is null || costCenter == Guid.Empty ? f.LineToken : null,
        VersionRowVersion = f.VersionToken,
    };

    private static BudgetMutation BudgetWriteFor(string operation, BudgetFixture f) => operation switch
    {
        "budget-create" => new("add", HttpMethod.Post, "api/fin/budget", BudgetCreateData(f), "Fin_Budget", "INSERT"),
        "budget-update" => new("edit", HttpMethod.Put, $"api/fin/budget/{f.Budget:D}",
            new { Name = "Updated budget", Description = "changed" }, "Fin_Budget", "UPDATE"),
        "budget-deactivate" => new("edit", HttpMethod.Post, $"api/fin/budget/{f.Budget:D}/deactivate", null, "Fin_Budget", "UPDATE"),
        "version-create" => new("add", HttpMethod.Post, $"api/fin/budget/{f.Budget:D}/versions",
            new { Name = "Cloned version", CopyFromVersionId = f.Version }, "Fin_BudgetLinePeriod", "INSERT"),
        "version-update" => new("edit", HttpMethod.Put, $"api/fin/budget/versions/{f.Version:D}",
            new { Name = "Updated version", ControlMode = BudgetControlMode.Block, ControlBasis = BudgetControlBasis.Period }, "Fin_BudgetVersion", "UPDATE"),
        "version-submit" => new("submit", HttpMethod.Post, $"api/fin/budget/versions/{f.Version:D}/submit", null, "Fin_BudgetVersion", "UPDATE"),
        "version-activate" => new("activate", HttpMethod.Post, $"api/fin/budget/versions/{f.Version:D}/activate", null, "Fin_BudgetVersion", "UPDATE"),
        "version-delete" => new("delete", HttpMethod.Delete, $"api/fin/budget/versions/{f.Version:D}", null, "Fin_BudgetVersion", "DELETE"),
        "line-upsert" => new("edit", HttpMethod.Post, "api/fin/budget/lines", BudgetLineInput(f), "Fin_BudgetLinePeriod", ""),
        "line-delete" => new("edit", HttpMethod.Delete, $"api/fin/budget/lines/{f.Line:D}?lineRowVersion={BudgetToken(f.LineToken)}&versionRowVersion={BudgetToken(f.VersionToken)}",
            null, "Fin_BudgetLine", "DELETE"),
        "line-import" => new("import", HttpMethod.Post, $"api/fin/budget/lines/import/confirm?versionId={f.Version:D}&versionRowVersion={BudgetToken(f.VersionToken)}",
            null, "Fin_BudgetLinePeriod", "", FixtureWorkbook()),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static string BudgetToken(byte[] token) => Uri.EscapeDataString(Convert.ToBase64String(token));

    private static byte[] FixtureWorkbook()
    {
        using var stream = BudgetExcelFixture.Build(new[]
        {
            ("W606-EXP", "", "", FirstBudgetImportAmounts),
            ("W606-REV", "W606-CENTER", "", SecondBudgetImportAmounts),
        });
        using var data = new MemoryStream();
        stream.CopyTo(data);
        return data.ToArray();
    }

    private static HttpRequestMessage BudgetRequest(Dictionary<string, string> actor, BudgetMutation command)
    {
        var request = OperationsRequest(actor, command.Method, command.Path, command.Body);
        if (command.Workbook != null)
        {
            var multipart = new MultipartFormDataContent();
            multipart.Add(new ByteArrayContent(command.Workbook), "file", "budget.xlsx");
            request.Content = multipart;
        }
        return request;
    }

    private static Task<AuthenticationApiProcess> StartBudgetApi(Harness h) =>
        AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));

    private static async Task<JsonElement> BudgetPayload(HttpResponseMessage result)
    {
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("code").GetInt32());
        Assert.Equal("OK", json.GetProperty("message").GetString());
        return json.GetProperty("data");
    }

    private static async Task AssertBudgetError(HttpResponseMessage result, string code)
    {
        Assert.True(result.StatusCode == HttpStatusCode.BadRequest, await OperationsFailure(result));
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, json.GetProperty("code").GetInt32());
        Assert.Equal(code, json.GetProperty("message").GetString());
        Assert.Empty(json.GetProperty("args").EnumerateArray());
    }

    private static async Task AssertBudgetDenial(HttpResponseMessage result, string code)
    {
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        var json = await result.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, json.GetProperty("code").GetInt32());
        Assert.Equal(code, json.GetProperty("message").GetString());
    }

    private static async Task<BudgetWrites> ReadBudgetWrites(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'budgets', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Budget" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'versions', ARRAY(SELECT to_jsonb(r) FROM public."Fin_BudgetVersion" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_BudgetLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_BudgetLinePeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'workflows', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowInstance" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'tasks', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowTask" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'history', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowHistory" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'tokens', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowToken" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'notifications', ARRAY(SELECT to_jsonb(r) FROM public."Wf_Notification" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'jobs', ARRAY(SELECT to_jsonb(r) FROM public."Wf_ServiceJob" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'form-tos', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowFormTo" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'flow-data', ARRAY(SELECT to_jsonb(r) FROM public."Wf_FlowData" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('Budget','BudgetVersion','BudgetLine','BudgetLinePeriod','Wf_FlowInstance','Wf_FlowTask')
                ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static async Task CheckBudgetSuccess(HttpResponseMessage result, string operation, BudgetFixture f,
        BudgetWrites before, BudgetWrites after)
    {
        var data = await BudgetPayload(result);
        if (operation is not ("budget-create" or "version-create")) Assert.Equal(JsonValueKind.Null, data.ValueKind);
        var sourceLine = before.Row("lines", f.Line);
        switch (operation)
        {
            case "budget-create":
                Assert.Equal(f.CreatedBudget, data.GetProperty("id").GetGuid());
                var created = after.Row("budgets", f.CreatedBudget);
                Assert.Equal(f.Tenant, AssetId(created, "TenantId"));
                Assert.Equal(BudgetYear + 1, AssetInt(created, "FiscalYear"));
                Assert.Equal($"BUD-{BudgetYear + 1}-00001", created.GetProperty("No").GetString());
                Assert.True(created.GetProperty("IsActive").GetBoolean());
                Assert.Equal("price-owner", created.GetProperty("Creator").GetString());
                Assert.Equal(1, AssetInt(Assert.Single(after.Rows("numbers")), "CurrentValue"));
                break;
            case "budget-update":
                Assert.Equal("Updated budget", after.Row("budgets", f.Budget).GetProperty("Name").GetString());
                Assert.Equal("changed", after.Row("budgets", f.Budget).GetProperty("Description").GetString());
                break;
            case "budget-deactivate":
                Assert.False(after.Row("budgets", f.Budget).GetProperty("IsActive").GetBoolean());
                break;
            case "version-create":
                var copied = after.Row("versions", data.GetProperty("id").GetGuid());
                Assert.Equal(f.Budget, AssetId(copied, "BudgetId"));
                Assert.Equal(3, AssetInt(copied, "VersionNo"));
                Assert.Equal(8, data.GetProperty("rowVersion").GetBytesFromBase64().Length);
                var clone = Assert.Single(after.Rows("lines"), row => AssetId(row, "VersionId") == AssetId(copied));
                Assert.Equal(f.Expense, AssetId(clone, "AccountId"));
                Assert.Equal(sourceLine.GetProperty("Memo").GetString(), clone.GetProperty("Memo").GetString());
                Assert.Equal(AssetInt(sourceLine, "ControlMode"), AssetInt(clone, "ControlMode"));
                Assert.Equal(AssetInt(sourceLine, "ControlBasis"), AssetInt(clone, "ControlBasis"));
                CheckPeriods(after, clone, OriginalBudgetAmounts);
                break;
            case "version-update":
                var updated = after.Row("versions", f.Version);
                Assert.Equal("Updated version", updated.GetProperty("Name").GetString());
                Assert.Equal((int)BudgetControlMode.Block, AssetInt(updated, "DefaultControlMode"));
                Assert.Equal((int)BudgetControlBasis.Period, AssetInt(updated, "DefaultControlBasis"));
                break;
            case "version-submit":
                CheckBudgetApproval(f, after);
                break;
            case "version-activate":
                Assert.True(after.Row("versions", f.Version).GetProperty("IsActive").GetBoolean());
                var archived = after.Row("versions", f.ActiveVersion);
                Assert.False(archived.GetProperty("IsActive").GetBoolean());
                Assert.Equal((int)BudgetVersionStatus.Archived, AssetInt(archived, "Status"));
                break;
            case "version-delete":
                Assert.DoesNotContain(after.Rows("versions"), v => AssetId(v) == f.Version);
                Assert.Empty(after.Rows("lines"));
                Assert.Empty(after.Rows("periods"));
                break;
            case "line-upsert":
                var adjusted = after.Row("lines", f.Line);
                Assert.Equal("revised with exact amount", adjusted.GetProperty("Memo").GetString());
                Assert.Equal((int)BudgetControlMode.Block, AssetInt(adjusted, "ControlMode"));
                CheckPeriods(after, adjusted, EditedBudgetAmounts);
                break;
            case "line-delete":
                Assert.Empty(after.Rows("lines"));
                Assert.Empty(after.Rows("periods"));
                break;
            case "line-import":
                CheckPeriods(after, after.Row("lines", f.Line), FirstBudgetImportAmounts);
                var added = Assert.Single(after.Rows("lines"), row => AssetId(row) != f.Line);
                Assert.Equal(f.Revenue, AssetId(added, "AccountId"));
                Assert.Equal(f.Center, AssetId(added, "CostCenterId"));
                Assert.Equal(f.Center, AssetId(added, "CostCenterKey"));
                CheckPeriods(after, added, SecondBudgetImportAmounts);
                break;
        }
        CheckBudgetWriteSet(operation, f, before, after);
    }

    private static void CheckBudgetApproval(BudgetFixture f, BudgetWrites state)
    {
        var version = state.Row("versions", f.Version);
        var instance = Assert.Single(state.Rows("workflows"));
        var task = Assert.Single(state.Rows("tasks"));
        var history = Assert.Single(state.Rows("history"));
        var token = Assert.Single(state.Rows("tokens"));
        var notification = Assert.Single(state.Rows("notifications"));
        Assert.Equal((int)BudgetVersionStatus.PendingApproval, AssetInt(version, "Status"));
        Assert.Equal("price-owner", version.GetProperty("SubmittedBy").GetString());
        Assert.Equal(AssetId(instance), AssetId(version, "ApprovalInstanceId"));
        Assert.Equal(AssetId(instance).ToString(), version.GetProperty("ApprovalRef").GetString());
        Assert.Equal(f.User, AssetId(instance, "StarterId"));
        Assert.Equal(f.Definition, AssetId(instance, "FlowDefVersionId"));
        Assert.Equal(BudgetBizType, instance.GetProperty("BizType").GetString());
        Assert.Equal(f.Version.ToString(), instance.GetProperty("BizId").GetString());
        Assert.Equal((int)FlowInstanceStatus.Running, AssetInt(instance, "Status"));
        Assert.Equal(f.User, AssetId(task, "AssigneeId"));
        Assert.Equal(AssetId(instance), AssetId(task, "InstanceId"));
        Assert.Equal(AssetId(token), AssetId(task, "TokenId"));
        Assert.Equal((int)FlowTaskStatus.Pending, AssetInt(task, "Status"));
        Assert.Equal(f.User, AssetId(history, "ActorId"));
        Assert.Equal(AssetId(instance), AssetId(history, "InstanceId"));
        Assert.Equal("submit", history.GetProperty("Action").GetString());
        Assert.Equal(AssetId(task), AssetId(notification, "TaskId"));
        var formTo = Assert.Single(state.Rows("form-tos"));
        var snapshot = Assert.Single(state.Rows("flow-data"));
        Assert.Equal(AssetId(instance), AssetId(formTo, "InstanceId"));
        Assert.Equal(AssetId(instance), AssetId(snapshot, "InstanceId"));
        using var vars = JsonDocument.Parse(instance.GetProperty("VarsJson").GetString()!);
        Assert.Equal(BudgetYear, vars.RootElement.GetProperty("fiscalYear").GetInt32());
        Assert.Equal(2, vars.RootElement.GetProperty("versionNo").GetInt32());
        Assert.Equal(OriginalBudgetAmounts.Sum(), vars.RootElement.GetProperty("totalAmount").GetDecimal());
    }

    private static void CheckPeriods(BudgetWrites state, JsonElement line, decimal[] original)
    {
        var monthly = state.Rows("periods").Where(p => AssetId(p, "BudgetLineId") == AssetId(line))
            .OrderBy(p => AssetInt(p, "PeriodNo")).ToArray();
        Assert.Equal(Enumerable.Range(1, 12), monthly.Select(p => AssetInt(p, "PeriodNo")));
        Assert.Equal(original.Concat(Enumerable.Repeat(0m, 12 - original.Length)), monthly.Select(p => AssetDecimal(p, "Amount")));
        Assert.Equal(original.Sum(), AssetDecimal(line, "AnnualAmount"));
    }

    private static void CheckBudgetWriteSet(string operation, BudgetFixture f, BudgetWrites before, BudgetWrites after)
    {
        (string Table, int Added, int Updated, int Deleted)[] expected = operation switch
        {
            "budget-create" => [("budgets", 1, 0, 0), ("numbers", 1, 0, 0)],
            "budget-update" or "budget-deactivate" => [("budgets", 0, 1, 0)],
            "version-create" => [("versions", 1, 0, 0), ("lines", 1, 0, 0), ("periods", 12, 0, 0)],
            "version-update" => [("versions", 0, 1, 0)],
            "version-submit" => [("versions", 0, 1, 0), ("workflows", 1, 0, 0), ("tasks", 1, 0, 0),
                ("history", 1, 0, 0), ("tokens", 1, 0, 0), ("notifications", 1, 0, 0), ("form-tos", 1, 0, 0), ("flow-data", 1, 0, 0)],
            "version-activate" => [("versions", 0, 2, 0)],
            "version-delete" => [("versions", 0, 0, 1), ("lines", 0, 0, 1), ("periods", 0, 0, 12)],
            "line-upsert" => [("versions", 0, 1, 0), ("lines", 0, 1, 0), ("periods", 12, 0, 12)],
            "line-delete" => [("versions", 0, 1, 0), ("lines", 0, 0, 1), ("periods", 0, 0, 12)],
            "line-import" => [("versions", 0, 1, 0), ("lines", 1, 1, 0), ("periods", 24, 0, 12)],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        foreach (var table in new[] { "budgets", "versions", "lines", "periods", "numbers", "workflows", "tasks", "history",
                     "tokens", "notifications", "jobs", "form-tos", "flow-data", "audit" })
        {
            var initial = before.Rows(table).ToDictionary(r => AssetId(r));
            var final = after.Rows(table).ToDictionary(r => AssetId(r));
            var added = final.Keys.Except(initial.Keys).ToArray();
            var deleted = initial.Keys.Except(final.Keys).ToArray();
            var updated = initial.Keys.Intersect(final.Keys).Where(id => initial[id].GetRawText() != final[id].GetRawText()).ToArray();
            var row = expected.SingleOrDefault(r => r.Table == table);
            Assert.Equal((table, row.Added, row.Updated, row.Deleted), (table, added.Length, updated.Length, deleted.Length));
            foreach (var id in added.Concat(updated))
            {
                var value = final[id];
                Assert.Equal(f.Tenant, AssetId(value, "TenantId"));
                if (!value.TryGetProperty("RowVersion", out var token)) continue;
                Assert.Matches(@"^\\x[0-9a-f]{16}$", token.GetString()!);
                if (initial.TryGetValue(id, out var prior)) Assert.NotEqual(prior.GetProperty("RowVersion").GetString(), token.GetString());
            }
        }
    }
}
