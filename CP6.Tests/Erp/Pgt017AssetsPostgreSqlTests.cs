using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static readonly DateTime AssetMonthEnd = new(2026, 6, 30);
    private const decimal AssetWorkload = 123.4567m;
    private const decimal AssetDepreciation = 1000m;
    private const decimal AssetOpening = 8000m;
    private const string AssetCardMenu = "fin-asset-card";
    private const string AssetDeprecMenu = "fin-asset-deprec";
    private const string AssetDisposalMenu = "fin-asset-disposal";

    [TenantManagementPostgresTheory]
    [InlineData("card-create", false)]
    [InlineData("card-create", true)]
    [InlineData("card-update", false)]
    [InlineData("card-update", true)]
    [InlineData("card-activate", false)]
    [InlineData("card-activate", true)]
    [InlineData("deprec-run", false)]
    [InlineData("deprec-run", true)]
    [InlineData("deprec-workload", false)]
    [InlineData("deprec-workload", true)]
    [InlineData("deprec-post", false)]
    [InlineData("deprec-post", true)]
    [InlineData("deprec-reverse", false)]
    [InlineData("deprec-reverse", true)]
    [InlineData("disposal-create", false)]
    [InlineData("disposal-create", true)]
    [InlineData("disposal-confirm", false)]
    [InlineData("disposal-confirm", true)]
    [InlineData("disposal-reverse", false)]
    [InlineData("disposal-reverse", true)]
    public async Task PGT017_asset_HTTP_write_and_pause_share_commit_order(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var assets = await SeedAssets(h, operation);
        var command = AssetCommand(operation, assets);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("asset-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.WaitTable, command.DbVerb, "Sys_Tenants", "UPDATE", pauseFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadAssets(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, await ReadAssets(observer, h.Tenant));
        }
        else
        {
            await CheckAssetSuccess(business, operation, assets, before, await ReadAssets(observer, h.Tenant));
        }
        var committed = await ReadAssets(observer, h.Tenant);
        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(committed, await ReadAssets(observer, h.Tenant));

        if (pauseFirst)
        {
            await ResumeAssetTenant(host, manager, h.Tenant, receipt);
            actor = await Login(host, h);
            using var current = await Write();
            await CheckAssetSuccess(current, operation, assets, before, await ReadAssets(observer, h.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, PauseFirst = pauseFirst,
            race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_asset_disposal_confirm_and_role_removal_share_commit_order(bool revokeFirst)
    {
        const string operation = "disposal-confirm";
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var assets = await SeedAssets(h, operation);
        var command = AssetCommand(operation, assets);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, command.Action);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);

        var race = await ObserveAssetRace(h.Db.Database.GetConnectionString()!, observer,
            command.WaitTable, command.DbVerb, "Sys_RoleAction", "DELETE", revokeFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)),
            async () =>
            {
                Assert.Equal(before, await ReadAssets(observer, h.Tenant));
                Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            });
        using var changed = race.Authority;
        using var business = race.Business;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var finalAuthority = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, finalAuthority.RoleRevision);
        Assert.Equal((authority.Generation + 1, authority.Snapshot + 1, authority.Outbox + 1, 1L, 1L, 1L),
            (finalAuthority.Generation, finalAuthority.Snapshot, finalAuthority.Outbox, finalAuthority.CurrentOutbox,
                finalAuthority.Audits, finalAuthority.Receipts));
        Assert.DoesNotContain(command.Action, finalAuthority.Actions.Split(','));
        var completed = await ReadAssets(observer, h.Tenant);
        if (revokeFirst)
        {
            Assert.Equal(HttpStatusCode.Forbidden, business.StatusCode);
            var denial = await business.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(403, denial.GetProperty("code").GetInt32());
            Assert.Equal("IAM_FORBIDDEN", denial.GetProperty("message").GetString());
            Assert.Equal(before, completed);
        }
        else
        {
            await CheckAssetSuccess(business, operation, assets, before, completed);
        }
        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        var preflight = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, preflight.GetProperty("code").GetInt32());
        Assert.Equal("无权限：fin-asset-disposal:confirm", preflight.GetProperty("message").GetString());
        Assert.Equal(completed, await ReadAssets(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, RevokeFirst = revokeFirst,
            race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, command.Method, command.Path));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_asset_month_end_worker_and_pause_share_commit_order(bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var assets = await SeedAssets(h, "worker");
        await using var services = AssetWorkerServices(h.Tenant);
        using var worker = new AssetDepreciationWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AssetDepreciationWorker>.Instance);
        await using var host = await StartAssetApi(h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("asset-worker-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        await worker.ProcessOnceAsync(AssetMonthEnd.AddDays(-1));
        Assert.Equal(before, await ReadAssets(observer, h.Tenant));

        var race = await ObserveAssetRace(h.Db.Database.GetConnectionString()!, observer,
            "Fin_DepreciationRun", "INSERT", "Sys_Tenants", "UPDATE", pauseFirst,
            Run, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadAssets(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var pausedState = await ReadAssets(observer, h.Tenant);
        if (pauseFirst) Assert.Equal(before, pausedState);
        else CheckAssetChanges("worker", assets, before, pausedState);
        await Run();
        Assert.Equal(pausedState, await ReadAssets(observer, h.Tenant));

        await ResumeAssetTenant(host, manager, h.Tenant, receipt);
        await Run();
        var restored = await ReadAssets(observer, h.Tenant);
        CheckAssetChanges("worker", assets, before, restored);
        await Run();
        Assert.Equal(restored, await ReadAssets(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { Operation = "worker", PauseFirst = pauseFirst,
            race.FirstWait, race.RootWait, DraftsDuringPause = pausedState.Rows("runs").Length }));

        async Task<bool> Run()
        {
            await worker.ProcessOnceAsync(AssetMonthEnd);
            return true;
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_asset_worker_rolls_back_when_the_captured_tenant_expiry_passes_before_commit()
    {
        await using var h = await Seed();
        await SeedAssets(h, "worker");
        var clock = new AssetWorkerClock(new DateTimeOffset(AssetMonthEnd.AddHours(12), TimeSpan.Zero));
        var expiry = clock.GetLocalNow().AddMinutes(1);
        await h.Db.Sys_Tenants.Where(tenant => tenant.Id == h.Tenant)
            .ExecuteUpdateAsync(set => set.SetProperty(tenant => tenant.ExpireDate, expiry.DateTime));
        await using var services = AssetWorkerServices(h.Tenant);
        using var worker = new AssetDepreciationWorker(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AssetDepreciationWorker>.Instance, clock);
        var connection = h.Db.Database.GetConnectionString();
        await using var observer = new NpgsqlConnection(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await barrier.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        Task writing;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            await using var command = new NpgsqlCommand(
                """LOCK TABLE public."Fin_DepreciationRun" IN SHARE MODE""", barrier, hold);
            await command.ExecuteNonQueryAsync();
            writing = worker.ProcessOnceAsync();
            var wait = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Fin_DepreciationRun%", null, writing);
            Assert.Equal(before, await ReadAssets(observer, h.Tenant));
            clock.AdvanceTo(expiry.AddSeconds(1));
            output.WriteLine(JsonSerializer.Serialize(new { Wait = wait, Expiry = expiry, Current = clock.GetLocalNow() }));
            await hold.RollbackAsync();
        }
        await writing;
        Assert.Equal(before, await ReadAssets(observer, h.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_asset_worker_rolls_back_rejected_preparation_and_recovers_an_unknown_commit()
    {
        await using var h = await Seed();
        var assets = await SeedAssets(h, "worker");
        var fault = new AssetWorkerCommitFault();
        var log = new AssetWorkerLog();
        await using var services = AssetWorkerServices(h.Tenant, fault);
        using var worker = new AssetDepreciationWorker(services.GetRequiredService<IServiceScopeFactory>(), log);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var missing = Guid.NewGuid();
        await h.Db.AssetCards.Where(card => card.Id == assets.Card)
            .ExecuteUpdateAsync(set => set.SetProperty(card => card.CategoryId, missing));
        var rejected = await ReadAssets(observer, h.Tenant);
        await worker.ProcessOnceAsync(AssetMonthEnd);
        Assert.Equal(rejected, await ReadAssets(observer, h.Tenant));
        Assert.Contains("FA001", Assert.Single(log.Warnings));

        await h.Db.AssetCards.Where(card => card.Id == assets.Card)
            .ExecuteUpdateAsync(set => set.SetProperty(card => card.CategoryId, assets.Category));
        var before = await ReadAssets(observer, h.Tenant);
        fault.Armed = true;
        await worker.ProcessOnceAsync(AssetMonthEnd);
        var committed = await ReadAssets(observer, h.Tenant);
        CheckAssetChanges("worker", assets, before, committed);
        Assert.Equal("ASSET_DEPRECIATION_COMMIT_UNKNOWN", Assert.Single(log.Errors).Message);
        await worker.ProcessOnceAsync(AssetMonthEnd);
        Assert.Equal(committed, await ReadAssets(observer, h.Tenant));
        Assert.Single(log.Errors);
    }

    [TenantManagementPostgresTheory]
    [InlineData("deprec-run", "FA001")]
    [InlineData("disposal-confirm", "FA001")]
    [InlineData("disposal-reverse", "E-FIN-120")]
    public async Task PGT017_asset_late_business_rejection_rolls_back_numbers_vouchers_and_asset_rows(string operation, string code)
    {
        await using var h = await Seed();
        var assets = await SeedAssets(h, operation, failLate: true);
        var command = AssetCommand(operation, assets);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);

        using var rejected = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        await CheckAssetRejection(rejected, code);
        Assert.Equal(before, await ReadAssets(observer, h.Tenant));
    }

    [TenantManagementPostgresTheory]
    [InlineData("card-create")]
    [InlineData("disposal-create")]
    public async Task PGT017_asset_creates_bind_current_tenant_and_disposal_lifecycle_links(string operation)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var assets = await SeedAssets(h, operation);
        await SeedAssets(foreign, "card-create");
        var command = AssetCommand(operation, assets, foreign.Tenant, supplyLifecycle: true);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        var foreignBefore = await ReadAssets(observer, foreign.Tenant);

        using var result = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        Assert.True(result.StatusCode == HttpStatusCode.OK, await OperationsFailure(result));
        Assert.Equal(foreignBefore, await ReadAssets(observer, foreign.Tenant));
        await CheckAssetSuccess(result, operation, assets, before, await ReadAssets(observer, h.Tenant));
    }

    [TenantManagementPostgresTheory]
    [InlineData("card-create", "FA001")]
    [InlineData("card-update", "FA001")]
    [InlineData("disposal-create", "FA010")]
    public async Task PGT017_asset_writes_reject_foreign_financial_accounts(string operation, string code)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var assets = await SeedAssets(h, operation);
        var other = await SeedAssets(foreign, "card-create");
        var command = AssetCommand(operation, assets, gl: other.BankGlId);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        var foreignBefore = await ReadAssets(observer, foreign.Tenant);

        using var denied = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        await CheckAssetRejection(denied, code);
        Assert.Equal(before, await ReadAssets(observer, h.Tenant));
        Assert.Equal(foreignBefore, await ReadAssets(observer, foreign.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_asset_card_dimensions_accept_local_legacy_alias_and_preserve_unchanged_or_empty_values()
    {
        await using var h = await Seed();
        var assets = await SeedAssets(h, "card-create");
        var local = await SeedAssetDimensions(h, legacyAlias: true);
        await GrantLegacyAuthorityAction(h, AssetCardMenu, "add");
        await GrantLegacyAuthorityAction(h, AssetCardMenu, "edit");
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        var create = AssetCommand("card-create", assets, dimensions: local);
        using var created = await host.Send(OperationsRequest(actor, create.Method, create.Path, create.Body));
        var issued = await ReadAssets(observer, h.Tenant);
        await CheckAssetSuccess(created, "card-create", assets, before, issued);
        CheckDimensions(Assert.Single(issued.Rows("cards"), row => AssetId(row) != assets.Card), local);

        var historical = new AssetDimensions(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await h.Db.AssetCards.Where(card => card.Id == assets.Card).ExecuteUpdateAsync(setters => setters
            .SetProperty(card => card.CostCenterId, historical.CostCenter)
            .SetProperty(card => card.MachineId, historical.Machine)
            .SetProperty(card => card.DeptId, historical.Dept));
        var original = await ReadAssets(observer, h.Tenant);
        var edit = AssetCommand("card-update", assets, dimensions: historical);
        using var edited = await host.Send(OperationsRequest(actor, edit.Method, edit.Path, edit.Body));
        var kept = await ReadAssets(observer, h.Tenant);
        await CheckAssetSuccess(edited, "card-update", assets, original, kept);
        CheckDimensions(kept.Row("cards", assets.Card), historical);

        var optional = new AssetDimensions(Guid.Empty, Guid.Empty, Guid.Empty);
        var clear = AssetCommand("card-update", assets, dimensions: optional);
        using var cleared = await host.Send(OperationsRequest(actor, clear.Method, clear.Path, clear.Body));
        var empty = await ReadAssets(observer, h.Tenant);
        await CheckAssetSuccess(cleared, "card-update", assets, kept, empty);
        CheckDimensions(empty.Row("cards", assets.Card), optional);

        var machine = new Machine { MachineCd = "W603-OWN", MachineName = "Local machine" };
        h.Db.Machines.Add(machine);
        await h.Db.SaveChangesAsync();
        var assigned = local with { Machine = machine.Id };
        var assign = AssetCommand("card-update", assets, dimensions: assigned);
        using var updated = await host.Send(OperationsRequest(actor, assign.Method, assign.Path, assign.Body));
        var associated = await ReadAssets(observer, h.Tenant);
        await CheckAssetSuccess(updated, "card-update", assets, empty, associated);
        CheckDimensions(associated.Row("cards", assets.Card), assigned);

        static void CheckDimensions(JsonElement row, AssetDimensions expected) =>
            Assert.Equal(expected, new(AssetId(row, "CostCenterId"), AssetId(row, "MachineId"), AssetId(row, "DeptId")));
    }

    [TenantManagementPostgresTheory]
    [InlineData("card-create", "cost-center")]
    [InlineData("card-create", "machine")]
    [InlineData("card-create", "department")]
    [InlineData("card-update", "cost-center")]
    [InlineData("card-update", "machine")]
    [InlineData("card-update", "department")]
    public async Task PGT017_asset_card_dimensions_reject_foreign_targets(string operation, string dimension)
    {
        await using var h = await Seed();
        await using var other = await Seed();
        var assets = await SeedAssets(h, operation);
        var foreign = await SeedAssetDimensions(other);
        var supplied = dimension switch
        {
            "cost-center" => new AssetDimensions(CostCenter: foreign.CostCenter),
            "machine" => new AssetDimensions(Machine: foreign.Machine),
            "department" => new AssetDimensions(Dept: foreign.Dept),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
        var command = AssetCommand(operation, assets, dimensions: supplied);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        var otherBefore = await ReadAssets(observer, other.Tenant);

        using var rejected = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
        await CheckAssetRejection(rejected, "FA001");
        Assert.Equal(before, await ReadAssets(observer, h.Tenant));
        Assert.Equal(otherBefore, await ReadAssets(observer, other.Tenant));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_asset_disposal_confirm_replaces_a_legacy_draft_link_when_monthly_accrual_already_exists()
    {
        await using var h = await Seed();
        var assets = await SeedAssets(h, "disposal-confirm", priorBatch: true);
        await GrantLegacyAuthorityAction(h, AssetDisposalMenu, "confirm");
        await GrantLegacyAuthorityAction(h, AssetDisposalMenu, "reverse");
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAssets(observer, h.Tenant);
        var originalBatch = Assert.Single(before.Rows("runs"));
        var originalJournal = before.Row("journals", AssetId(originalBatch, "JournalEntryId"));

        foreach (var operation in new[] { "disposal-confirm", "disposal-reverse" })
        {
            var command = AssetCommand(operation, assets);
            using var result = await host.Send(OperationsRequest(actor, command.Method, command.Path, command.Body));
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var current = await ReadAssets(observer, h.Tenant);
            Assert.Equal(originalBatch.GetRawText(), Assert.Single(current.Rows("runs")).GetRawText());
            Assert.Equal(originalJournal.GetRawText(), current.Row("journals", AssetId(originalJournal)).GetRawText());
            Assert.Equal(JsonValueKind.Null, current.Row("disposals", assets.Disposal).GetProperty("FinalDeprecEntryId").ValueKind);
        }
        Assert.Equal(AssetOpening + AssetDepreciation, AssetDecimal((await ReadAssets(observer, h.Tenant)).Row("cards", assets.Card), "AccumulatedDepreciation"));
    }

    private sealed record AssetFixture(Guid Tenant, Guid Period, Guid Category, Guid Card, Guid ExpenseGl, Guid BankGlId,
        Guid Batch, Guid Entry, Guid Disposal);
    private sealed record AssetDimensions(Guid? CostCenter = null, Guid? Machine = null, Guid? Dept = null);
    private sealed record AssetWrite(string Menu, string Action, HttpMethod Method, string Path, object? Body,
        string WaitTable, string DbVerb);
    private sealed record AssetRace<T>(T Business, HttpResponseMessage Authority, FenceWait FirstWait, FenceWait RootWait);
    private sealed class AssetTenantEnumeration(TenantEnumerator active, Guid tenant) : ITenantEnumerator
    {
        public async Task<IReadOnlyList<Guid>> ListActiveAsync(CancellationToken ct = default) =>
            (await active.ListActiveAsync(ct)).Where(id => id == tenant).ToArray();
    }

    private sealed class AssetWorkerClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void AdvanceTo(DateTimeOffset value) => Interlocked.Exchange(ref _ticks, value.UtcTicks);
    }

    private sealed class AssetWorkerCommitFault : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return Task.CompletedTask;
            Armed = false;
            throw new IOException("Owned lost asset commit acknowledgement");
        }
    }

    private sealed class AssetWorkerLog : ILogger<AssetDepreciationWorker>
    {
        internal List<string> Warnings { get; } = [];
        internal List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Warning or LogLevel.Error;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
            if (logLevel == LogLevel.Error && exception != null) Errors.Add(exception);
        }
    }

    private ServiceProvider AssetWorkerServices(Guid tenant, params IInterceptor[] interceptors) => new ServiceCollection()
        .AddScoped<ITenantContext, TenantContext>()
        .AddScoped(sp => server.Context(sp.GetRequiredService<ITenantContext>(), null, interceptors))
        .AddScoped<TenantEnumerator>()
        .AddScoped<ITenantEnumerator>(sp => new AssetTenantEnumeration(sp.GetRequiredService<TenantEnumerator>(), tenant))
        .AddScoped<IFiscalPeriodService>(sp => new FiscalPeriodService(sp.GetRequiredService<CP6Context>(), 1))
        .AddScoped<IFinSequenceService, FinSequenceService>()
        .AddScoped<IJournalEntryService, JournalEntryService>()
        .AddSingleton<IDepreciationCalculator, DepreciationCalculator>()
        .AddScoped<IAssetDepreciationService, AssetDepreciationService>()
        .BuildServiceProvider();

    private static async Task<AssetRace<T>> ObserveAssetRace<T>(string connection, NpgsqlConnection observer, string businessTable, string businessVerb,
        string authorityTable, string authorityVerb, bool authorityFirst, Func<Task<T>> write,
        Func<Task<HttpResponseMessage>> change, Func<Task> inspect)
    {
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        Task<T> writing;
        Task<HttpResponseMessage> changing;
        FenceWait first;
        FenceWait root;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var table = authorityFirst ? authorityTable : businessTable;
            await using var cmd = new NpgsqlCommand($"LOCK TABLE public.\"{table}\" IN SHARE MODE", barrier, hold);
            await cmd.ExecuteNonQueryAsync();
            if (authorityFirst)
            {
                changing = change();
                first = await WaitForFence(observer, barrier.ProcessID, $"%{authorityVerb}%{authorityTable}%", null, changing);
                writing = write();
                root = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, writing);
            }
            else
            {
                writing = write();
                first = await WaitForFence(observer, barrier.ProcessID, $"%{businessVerb}%{businessTable}%", null, writing);
                changing = change();
                root = await WaitForFence(observer, first.Backend, "%cp6_authentication_v1.acquire()%", null, changing);
            }
            await inspect();
            await hold.RollbackAsync();
        }
        return new(await writing, await changing, first, root);
    }

    private static Task<AuthenticationApiProcess> StartAssetApi(Harness h) =>
        AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(h.Tenant));

    private static async Task ResumeAssetTenant(AuthenticationApiProcess host, Dictionary<string, string> manager, Guid tenant,
        TenantManagementReceiptV1 receipt)
    {
        var resume = new TenantManagementCommandV1("asset-resume", receipt.Snapshot.Revision, "reactivate", null);
        using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), resume));
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    }

    private static async Task<AssetFixture> SeedAssets(Harness h, string operation, bool failLate = false, bool priorBatch = false)
    {
        var db = h.Db;
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        var accounts = await db.GlAccounts.ToDictionaryAsync(a => a.Code);
        var fiscal = new FiscalPeriodService(db, 1);
        var period = await fiscal.EnsureOpenAsync(AssetMonthEnd, "fixture");
        var seq = new FinSequenceService(db);
        var journal = new JournalEntryService(db, fiscal, seq);
        var dep = new AssetDepreciationService(db, new DepreciationCalculator(), journal, fiscal, seq);
        var disposal = new AssetDisposalService(db, journal, fiscal, seq, dep);
        var cat = new AssetCategory
        {
            Id = Guid.NewGuid(), Code = "W603", Name = "Machinery", DefaultUsefulLifeMonths = 12,
            DefaultMethod = DepreciationMethod.StraightLine, DefaultSalvageRate = 0.0345m,
            AssetAccountId = accounts["1601"].Id, AccumDeprecAccountId = accounts["1602"].Id,
            DeprecExpenseAccountId = accounts["5101.01"].Id,
        };
        var card = new AssetCard
        {
            Id = Guid.NewGuid(), AssetNo = "FIXTURE-FA", Name = "Fixture lathe", AcquisitionDate = new(2025, 9, 16),
            CategoryId = failLate && operation == "deprec-run" ? Guid.NewGuid() : cat.Id,
            OriginalValue = 12000m, UsefulLifeMonths = 12, DepreciationStartPeriod = "2025-10",
            AccumulatedDepreciation = AssetOpening, DepreciatedPeriods = 8, IsOpeningImport = true,
            Status = operation == "card-activate" ? AssetStatus.Draft : AssetStatus.InUse, Custodian = "Original",
        };
        if (operation == "deprec-workload")
        {
            card.Method = DepreciationMethod.UnitsOfProduction;
            card.TotalWorkload = 10000m;
        }
        db.AssetCategories.Add(cat);
        db.AssetCards.Add(card);
        await db.SaveChangesAsync();
        var state = new AssetFixture(h.Tenant, period.Id, cat.Id, card.Id, cat.DeprecExpenseAccountId, accounts["1002"].Id,
            Guid.Empty, Guid.Empty, Guid.NewGuid());

        if (operation is "deprec-workload" or "deprec-post" or "deprec-reverse" || priorBatch)
        {
            Assert.True((await dep.RunAsync(period.Id, "fixture", DepreciationRunMode.Manual)).Ok);
            var run = await db.DepreciationRuns.SingleAsync();
            var entry = await db.DepreciationEntries.SingleAsync();
            state = state with { Batch = run.Id, Entry = entry.Id };
            if (operation == "deprec-reverse" || priorBatch) Assert.True((await dep.PostAsync(run.Id, "fixture")).Ok);
        }
        if (operation is "disposal-confirm" or "disposal-reverse")
        {
            var draft = AssetDisposalDraft(state);
            Assert.True((await disposal.CreateAsync(draft, "fixture")).Ok);
            if (priorBatch) draft.FinalDeprecEntryId = state.Entry;
            if (failLate && operation == "disposal-confirm") accounts["6115"].IsActive = false;
            await db.SaveChangesAsync();
            if (operation == "disposal-reverse")
            {
                Assert.True((await disposal.ConfirmAsync(draft.Id, "fixture")).Ok);
                if (failLate)
                {
                    var final = await db.DepreciationRuns.SingleAsync();
                    Assert.True((await journal.ReverseAsync(final.JournalEntryId!.Value, "fixture", "already reversed", autoPost: true)).Ok);
                }
            }
        }
        db.ChangeTracker.Clear();
        return state;
    }

    private static AssetDisposal AssetDisposalDraft(AssetFixture s, Guid tenant = default, Guid? gl = null, bool supplyLifecycle = false) => new()
    {
        Id = s.Disposal, No = "request", TenantId = tenant, AssetCardId = s.Card, DisposalDate = AssetMonthEnd, FiscalPeriodId = s.Period,
        DisposalType = AssetDisposalType.Sale, Proceeds = 5000.25m, TaxAmount = 650.03m, DisposalExpense = 90.40m,
        ReceiptBankAccountId = gl ?? s.BankGlId, Reason = "fixture disposal",
        JournalEntryId = supplyLifecycle ? Guid.NewGuid() : null,
        FinalDeprecEntryId = supplyLifecycle ? Guid.NewGuid() : null,
        PriorStatus = supplyLifecycle ? AssetStatus.Disposed : null,
        ConfirmedBy = supplyLifecycle ? "body" : null,
    };

    private static async Task<AssetDimensions> SeedAssetDimensions(Harness h, bool legacyAlias = false)
    {
        var machine = Guid.NewGuid();
        var center = new CostCenter { Code = "W603-DIM", Name = "Fixture center", Type = CostCenterType.Machine,
            LinkMachineId = legacyAlias ? machine.ToString() : null };
        var dept = new Sys_Dept { DeptCode = "W603-DIM", DeptName = "Fixture department" };
        h.Db.CostCenters.Add(center);
        h.Db.Sys_Depts.Add(dept);
        if (!legacyAlias) h.Db.Machines.Add(new Machine { Id = machine, MachineCd = "W603-DIM", MachineName = "Fixture machine" });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return new(center.Id, machine, dept.Id);
    }

    private static AssetWrite AssetCommand(string operation, AssetFixture s, Guid tenant = default, Guid? gl = null,
        bool supplyLifecycle = false, AssetDimensions? dimensions = null)
        => operation switch
    {
        "card-create" => new(AssetCardMenu, "add", HttpMethod.Post, "api/fin/asset-card", new AssetCard
        {
            TenantId = tenant, AssetNo = "request", CategoryId = s.Category, Name = "Opening machine", OriginalValue = 12345.67m,
            AcquisitionDate = new(2026, 5, 15), Method = DepreciationMethod.UnitsOfProduction, UsefulLifeMonths = 24,
            TotalWorkload = 10000.1234m, WorkloadUnit = "pieces", SalvageRate = 0.1234m, IsOpeningImport = true,
            AccumulatedDepreciation = 123.45m, DepreciatedPeriods = 2, DeprecExpenseAccountId = gl,
            CostCenterId = dimensions?.CostCenter, MachineId = dimensions?.Machine, DeptId = dimensions?.Dept,
        }, "Fin_AssetCard", "INSERT"),
        "card-update" => new(AssetCardMenu, "edit", HttpMethod.Put, $"api/fin/asset-card/{s.Card:D}",
            new { Name = "Fixture lathe", AssetNo = "FIXTURE-FA", Custodian = "Changed", Location = "Lab 603", Remarks = "reassigned",
                DeprecExpenseAccountId = gl ?? s.ExpenseGl, CostCenterId = dimensions?.CostCenter,
                MachineId = dimensions?.Machine, DeptId = dimensions?.Dept },
            "Fin_AssetCard", "UPDATE"),
        "card-activate" => new(AssetCardMenu, "activate", HttpMethod.Post, $"api/fin/asset-card/{s.Card:D}/activate", null,
            "Fin_AssetCard", "UPDATE"),
        "deprec-run" => new(AssetDeprecMenu, "run", HttpMethod.Post, $"api/fin/asset-deprec/run?periodId={s.Period:D}", null,
            "Fin_DepreciationRun", "INSERT"),
        "deprec-workload" => new(AssetDeprecMenu, "run", HttpMethod.Put, $"api/fin/asset-deprec/entry/{s.Entry:D}/workload",
            new { Workload = AssetWorkload }, "Fin_DepreciationEntry", "UPDATE"),
        "deprec-post" => new(AssetDeprecMenu, "post", HttpMethod.Post, $"api/fin/asset-deprec/{s.Batch:D}/post", null,
            "Fin_DepreciationRun", "UPDATE"),
        "deprec-reverse" => new(AssetDeprecMenu, "reverse", HttpMethod.Post, $"api/fin/asset-deprec/{s.Batch:D}/reverse",
            new { Reason = "fixture reversal" }, "Fin_DepreciationRun", "UPDATE"),
        "disposal-create" => new(AssetDisposalMenu, "add", HttpMethod.Post, "api/fin/asset-disposal",
            AssetDisposalDraft(s, tenant, gl, supplyLifecycle), "Fin_AssetDisposal", "INSERT"),
        "disposal-confirm" => new(AssetDisposalMenu, "confirm", HttpMethod.Post, $"api/fin/asset-disposal/{s.Disposal:D}/confirm", null,
            "Fin_AssetDisposal", "UPDATE"),
        "disposal-reverse" => new(AssetDisposalMenu, "reverse", HttpMethod.Post, $"api/fin/asset-disposal/{s.Disposal:D}/reverse",
            new { Reason = "fixture reversal" }, "Fin_AssetDisposal", "UPDATE"),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static async Task CheckAssetSuccess(HttpResponseMessage response, string operation, AssetFixture s, AssetRows before, AssetRows after)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, result.GetProperty("code").GetInt32());
        Assert.Equal("OK", result.GetProperty("message").GetString());
        var data = result.GetProperty("data");
        if (operation == "card-create")
        {
            var created = after.Row("cards", data.GetProperty("id").GetGuid());
            Assert.Equal(created.GetProperty("AssetNo").GetString(), data.GetProperty("assetNo").GetString());
        }
        else if (operation == "disposal-create")
        {
            Assert.Equal(s.Disposal, data.GetProperty("id").GetGuid());
            Assert.Equal("FAD-2026-06-00001", data.GetProperty("no").GetString());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, data.ValueKind);
        }
        CheckAssetChanges(operation, s, before, after);
    }

    private static async Task CheckAssetRejection(HttpResponseMessage response, string code)
    {
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await OperationsFailure(response));
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, result.GetProperty("code").GetInt32());
        Assert.Equal(code, result.GetProperty("message").GetString());
        Assert.Empty(result.GetProperty("args").EnumerateArray());
    }

    private sealed record AssetRows(string Data)
    {
        internal JsonElement[] Rows(string key)
        {
            using var parsed = JsonDocument.Parse(Data);
            return parsed.RootElement.GetProperty(key).EnumerateArray().Select(row => row.Clone()).ToArray();
        }

        internal JsonElement Row(string key, Guid id) => Assert.Single(Rows(key), row => AssetId(row) == id);
    }

    private static async Task<AssetRows> ReadAssets(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'cards', ARRAY(SELECT to_jsonb(r) FROM public."Fin_AssetCard" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'runs', ARRAY(SELECT to_jsonb(r) FROM public."Fin_DepreciationRun" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'entries', ARRAY(SELECT to_jsonb(r) FROM public."Fin_DepreciationEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'disposals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_AssetDisposal" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('AssetCard','DepreciationRun','DepreciationEntry','AssetDisposal',
                  'JournalEntry','JournalLine','FiscalPeriod','FinSequence','GlAccount') ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static Guid AssetId(JsonElement row, string field = "Id") => row.GetProperty(field).GetGuid();
    private static decimal AssetDecimal(JsonElement row, string field) => row.GetProperty(field).GetDecimal();
    private static int AssetInt(JsonElement row, string field) => row.GetProperty(field).GetInt32();

    private static void CheckAssetChanges(string operation, AssetFixture s, AssetRows before, AssetRows after)
    {
        (string Table, int Added, int Updated)[] effects = operation switch
        {
            "card-create" => [("cards", 1, 0), ("numbers", 1, 0)],
            "card-update" or "card-activate" => [("cards", 0, 1)],
            "deprec-run" or "worker" => [("runs", 1, 0), ("entries", 1, 0), ("numbers", 1, 0)],
            "deprec-workload" => [("runs", 0, 1), ("entries", 0, 1)],
            "deprec-post" => [("cards", 0, 1), ("runs", 0, 1), ("journals", 1, 0), ("lines", 2, 0), ("numbers", 1, 0)],
            "deprec-reverse" => [("cards", 0, 1), ("runs", 0, 1), ("journals", 1, 1), ("lines", 2, 0), ("numbers", 0, 1)],
            "disposal-create" => [("disposals", 1, 0), ("numbers", 1, 0)],
            "disposal-confirm" => [("cards", 0, 1), ("runs", 1, 0), ("entries", 1, 0), ("disposals", 0, 1),
                ("journals", 2, 0), ("lines", 12, 0), ("numbers", 2, 0)],
            "disposal-reverse" => [("cards", 0, 1), ("runs", 0, 1), ("disposals", 0, 1), ("journals", 2, 2),
                ("lines", 12, 0), ("numbers", 0, 1)],
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        foreach (var name in new[] { "cards", "runs", "entries", "disposals", "journals", "lines", "periods", "numbers", "audit" })
        {
            var initial = before.Rows(name).ToDictionary(row => AssetId(row));
            var current = after.Rows(name).ToDictionary(row => AssetId(row));
            var added = current.Keys.Except(initial.Keys).ToArray();
            Assert.Empty(initial.Keys.Except(current.Keys));
            var updated = initial.Keys.Where(id => initial[id].GetRawText() != current[id].GetRawText()).ToArray();
            var expectation = effects.SingleOrDefault(x => x.Table == name);
            Assert.Equal((name, expectation.Added, expectation.Updated), (name, added.Length, updated.Length));
            foreach (var id in added.Concat(updated))
            {
                var row = current[id];
                Assert.Equal(s.Tenant, AssetId(row, "TenantId"));
                if (row.TryGetProperty("RowVersion", out var version))
                {
                    Assert.Matches(@"^\\x[0-9a-f]{16}$", version.GetString()!);
                    if (initial.TryGetValue(id, out var old)) Assert.NotEqual(old.GetProperty("RowVersion").GetString(), version.GetString());
                }
            }
        }

        var card = after.Row("cards", s.Card);
        switch (operation)
        {
            case "card-create":
                var addedCard = Assert.Single(after.Rows("cards"), row => AssetId(row) != s.Card);
                Assert.Equal("FA-2026-05-00001", addedCard.GetProperty("AssetNo").GetString());
                Assert.Equal(s.Category, AssetId(addedCard, "CategoryId"));
                Assert.Equal("2026-06", addedCard.GetProperty("DepreciationStartPeriod").GetString());
                Assert.Equal((12345.67m, 0.1234m, 1523.46m, 10000.1234m, 123.45m, 2, (int)AssetStatus.Draft),
                    (AssetDecimal(addedCard, "OriginalValue"), AssetDecimal(addedCard, "SalvageRate"), AssetDecimal(addedCard, "SalvageValue"),
                        AssetDecimal(addedCard, "TotalWorkload"), AssetDecimal(addedCard, "AccumulatedDepreciation"),
                        AssetInt(addedCard, "DepreciatedPeriods"), AssetInt(addedCard, "Status")));
                break;
            case "card-update":
                Assert.Equal("Changed", card.GetProperty("Custodian").GetString());
                Assert.Equal("Lab 603", card.GetProperty("Location").GetString());
                Assert.Equal("reassigned", card.GetProperty("Remarks").GetString());
                Assert.Equal(s.ExpenseGl, AssetId(card, "DeprecExpenseAccountId"));
                Assert.Equal(AssetOpening, AssetDecimal(card, "AccumulatedDepreciation"));
                break;
            case "card-activate":
                Assert.Equal((int)AssetStatus.InUse, AssetInt(card, "Status"));
                Assert.Equal(AssetOpening, AssetDecimal(card, "AccumulatedDepreciation"));
                break;
            case "deprec-run":
            case "worker":
                var run = Assert.Single(after.Rows("runs"));
                Assert.Equal((int)DepreciationRunStatus.Draft, AssetInt(run, "Status"));
                Assert.Equal((int)(operation == "worker" ? DepreciationRunMode.Worker : DepreciationRunMode.Manual), AssetInt(run, "RunMode"));
                Assert.Equal(operation == "worker" ? "worker" : "price-owner", run.GetProperty("RunBy").GetString());
                Assert.Equal(JsonValueKind.Null, run.GetProperty("JournalEntryId").ValueKind);
                CheckAssetDepreciationEntry(after, s, run);
                break;
            case "deprec-workload":
                var entry = after.Row("entries", s.Entry);
                Assert.Equal(s.Batch, AssetId(entry, "RunId"));
                Assert.Equal((AssetWorkload, 148.15m, 8148.15m, 3851.85m),
                    (AssetDecimal(entry, "WorkloadThisPeriod"), AssetDecimal(entry, "DepreciationAmount"),
                        AssetDecimal(entry, "ClosingAccumulated"), AssetDecimal(entry, "ClosingNetValue")));
                Assert.Equal(148.15m, AssetDecimal(after.Row("runs", s.Batch), "TotalAmount"));
                break;
            case "deprec-post":
            case "deprec-reverse":
                var posted = after.Row("runs", s.Batch);
                var reversed = operation == "deprec-reverse";
                Assert.Equal((int)(reversed ? DepreciationRunStatus.Reversed : DepreciationRunStatus.Posted), AssetInt(posted, "Status"));
                Assert.Equal(reversed ? AssetOpening : AssetOpening + AssetDepreciation, AssetDecimal(card, "AccumulatedDepreciation"));
                Assert.Equal(reversed ? 8 : 9, AssetInt(card, "DepreciatedPeriods"));
                Assert.Equal("price-owner", posted.GetProperty(reversed ? "ReversedBy" : "PostedBy").GetString());
                CheckAssetVoucher(after, AssetId(posted, "JournalEntryId"), s.Period, VoucherSource.Depreciation, AssetDepreciation, reversed);
                break;
            default:
                CheckAssetDisposalChanges(after, s, operation, card);
                break;
        }

        var allocated = after.Rows("numbers").Sum(row => AssetInt(row, "CurrentValue"))
            - before.Rows("numbers").Sum(row => AssetInt(row, "CurrentValue"));
        Assert.Equal(operation switch
        {
            "card-create" or "deprec-run" or "worker" or "deprec-post" or "deprec-reverse" or "disposal-create" => 1,
            "disposal-confirm" => 3,
            "disposal-reverse" => 2,
            _ => 0,
        }, allocated);
    }

    private static void CheckAssetDepreciationEntry(AssetRows state, AssetFixture s, JsonElement run)
    {
        var entry = Assert.Single(state.Rows("entries"));
        Assert.Equal(s.Period, AssetId(run, "FiscalPeriodId"));
        Assert.Equal(s.Period, AssetId(entry, "FiscalPeriodId"));
        Assert.Equal(AssetId(run), AssetId(entry, "RunId"));
        Assert.Equal(s.Card, AssetId(entry, "AssetCardId"));
        Assert.Equal(s.ExpenseGl, AssetId(entry, "DeprecExpenseAccountId"));
        Assert.Equal(AssetDepreciation, AssetDecimal(run, "TotalAmount"));
        Assert.Equal((AssetDepreciation, AssetOpening, AssetOpening + AssetDepreciation),
            (AssetDecimal(entry, "DepreciationAmount"), AssetDecimal(entry, "OpeningAccumulated"), AssetDecimal(entry, "ClosingAccumulated")));
    }

    private static void CheckAssetDisposalChanges(AssetRows state, AssetFixture s, string operation, JsonElement card)
    {
        var disposal = state.Row("disposals", s.Disposal);
        Assert.Equal(s.Card, AssetId(disposal, "AssetCardId"));
        Assert.Equal(s.Period, AssetId(disposal, "FiscalPeriodId"));
        Assert.Equal(s.BankGlId, AssetId(disposal, "ReceiptBankAccountId"));
        Assert.Equal((5000.25m, 650.03m, 90.40m), (AssetDecimal(disposal, "Proceeds"), AssetDecimal(disposal, "TaxAmount"),
            AssetDecimal(disposal, "DisposalExpense")));
        if (operation == "disposal-create")
        {
            Assert.Equal((int)AssetDisposalStatus.Draft, AssetInt(disposal, "Status"));
            foreach (var field in new[] { "FinalDeprecEntryId", "JournalEntryId", "PriorStatus", "ConfirmedBy" })
                Assert.Equal(JsonValueKind.Null, disposal.GetProperty(field).ValueKind);
            Assert.Equal("fixture disposal", disposal.GetProperty("Reason").GetString());
            Assert.Equal((4000m, 909.85m), (AssetDecimal(disposal, "NetBookValue"), AssetDecimal(disposal, "NetGainLoss")));
            return;
        }
        var reversed = operation == "disposal-reverse";
        var entry = state.Row("entries", AssetId(disposal, "FinalDeprecEntryId"));
        var final = state.Row("runs", AssetId(entry, "RunId"));
        Assert.Equal((int)DepreciationRunMode.DisposalFinal, AssetInt(final, "RunMode"));
        Assert.Equal((int)(reversed ? DepreciationRunStatus.Reversed : DepreciationRunStatus.Posted), AssetInt(final, "Status"));
        Assert.Equal((int)(reversed ? AssetDisposalStatus.Reversed : AssetDisposalStatus.Confirmed), AssetInt(disposal, "Status"));
        Assert.Equal((int)AssetStatus.InUse, AssetInt(disposal, "PriorStatus"));
        Assert.Equal("price-owner", disposal.GetProperty(reversed ? "ReversedBy" : "ConfirmedBy").GetString());
        Assert.Equal((int)(reversed ? AssetStatus.InUse : AssetStatus.Disposed), AssetInt(card, "Status"));
        Assert.Equal(reversed ? AssetOpening : AssetOpening + AssetDepreciation, AssetDecimal(card, "AccumulatedDepreciation"));
        Assert.Equal(reversed ? 8 : 9, AssetInt(card, "DepreciatedPeriods"));
        Assert.Equal((3000m, 1909.85m), (AssetDecimal(disposal, "NetBookValue"), AssetDecimal(disposal, "NetGainLoss")));
        CheckAssetDepreciationEntry(state, s, final);
        CheckAssetVoucher(state, AssetId(final, "JournalEntryId"), s.Period, VoucherSource.Depreciation, AssetDepreciation, reversed);
        CheckAssetVoucher(state, AssetId(disposal, "JournalEntryId"), s.Period, VoucherSource.AssetDisposal, 19650.53m, reversed);
    }

    private static void CheckAssetVoucher(AssetRows state, Guid id, Guid period, VoucherSource source, decimal amount, bool reversed)
    {
        var voucher = state.Row("journals", id);
        var lines = state.Rows("lines").Where(row => AssetId(row, "EntryId") == id).ToArray();
        Assert.Equal(period, AssetId(voucher, "PeriodId"));
        Assert.Equal((int)source, AssetInt(voucher, "Source"));
        Assert.Equal((int)(reversed ? JournalStatus.Reversed : JournalStatus.Posted), AssetInt(voucher, "Status"));
        Assert.Equal(amount, lines.Sum(row => AssetDecimal(row, "Debit")));
        Assert.Equal(amount, lines.Sum(row => AssetDecimal(row, "Credit")));
        if (!reversed) return;
        var reversal = state.Row("journals", AssetId(voucher, "ReversedById"));
        Assert.Equal(id, AssetId(reversal, "ReverseOfId"));
        Assert.Equal((int)VoucherSource.Reversal, AssetInt(reversal, "Source"));
        Assert.Equal((int)JournalStatus.Posted, AssetInt(reversal, "Status"));
        var reversedLines = state.Rows("lines").Where(row => AssetId(row, "EntryId") == AssetId(reversal));
        Assert.Equal(lines.Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Debit"), AssetDecimal(row, "Credit"))).Order(),
            reversedLines.Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Credit"), AssetDecimal(row, "Debit"))).Order());
    }
}
