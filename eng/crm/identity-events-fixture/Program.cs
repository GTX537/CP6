using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

if (args.Length == 3 && args[2] == "transport-probe")
{
    Environment.ExitCode = await IdentityTransportProbe.RunAsync(args[0], args[1]);
    return;
}
if (args.Length is not (2 or 3)) throw new ArgumentException("Usage: identity-events-fixture <public-output-directory> <private-diagnostic-directory> [exact-case-name|transport-probe]");
var evidence = new Evidence(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args.Length == 3 ? args[2] : null);
var fixture = new IdentitySqlFixture();
try
{
    await evidence.Case("forward-migration-preserves-baseline-and-creates-both-queues", fixture.InitializeAsync);
    await evidence.Case("business-save-produces-valid-versioned-snapshots", fixture.ValidSnapshotsAsync);
    await evidence.Case("unchanged-authorization-does-not-advance-version", fixture.UnchangedAsync);
    await evidence.Case("envelope-failure-rolls-back-business-version-and-outbox", fixture.EnvelopeRollbackAsync);
    await evidence.Case("snapshot-failure-rolls-back-priority-enqueue", fixture.SnapshotRollbackAsync);
    await evidence.Case("outbox-insert-failure-rolls-back-business-and-version", fixture.OutboxRollbackAsync);
    await evidence.Case("concurrent-commands-commit-a-dense-version-sequence", fixture.ConcurrentVersionsAsync);
    await evidence.Case("savechanges-false-preserves-caller-state-with-one-write", () => fixture.SaveFalseAsync(false));
    await evidence.Case("savechangesasync-false-preserves-caller-state-with-one-write", () => fixture.SaveFalseAsync(true));
    await evidence.Case("native-user-disable-revokes-actual-crm-grant-jti", fixture.NativeRevocationAsync);
    await evidence.Case("direct-grant-revocation-is-atomic-and-idempotent", fixture.DirectRevocationAsync);
    await evidence.Case("service-revocation-is-bound-to-issuer-client-and-tenant", fixture.ServiceRevocationAsync);
    await evidence.Case("bootstrap-is-idempotent-and-pagination-restarts-on-change", fixture.BootstrapAndCursorAsync);
    await evidence.Case("department-move-emits-updated-descendant-paths", fixture.DepartmentMoveAsync);
    await evidence.Case("role-membership-and-empty-permissions-are-full-snapshots", fixture.RoleChangesAsync);
    await evidence.Case("password-and-two-factor-changes-revoke-real-grants", fixture.CredentialRevocationAsync);
    await evidence.Case("refresh-rotation-preserves-grant-terminal-revoke-invalidates-family", fixture.RefreshFamilyAsync);
    await evidence.Case("direct-family-revocation-is-atomic-across-both-sessions", fixture.DirectFamilyAsync);
    await evidence.Case("tenant-disable-revokes-browser-and-service-tokens", fixture.TenantDisableAsync);
    await evidence.Case("gdpr-purge-preserves-tombstones-and-revocation-outbox", fixture.GdprPurgeAsync);
    await evidence.Case("concurrent-bootstrap-and-business-write-preserve-latest-state", fixture.ConcurrentBootstrapAsync);
    await evidence.Case("actual-http-tls-service-token-reader-authentication-and-revocation", fixture.HttpAuthorizationAsync);
    await evidence.Case("failed-command-restores-caller-owned-transaction-savepoint", fixture.ExternalTransactionRollbackAsync);
    evidence.MarkComplete();
}
catch (Exception) { Environment.ExitCode = 1; }
finally
{
    try { await fixture.DisposeAsync(); }
    catch (Exception ex) { evidence.Failure("owned-database-cleanup", ex); Environment.ExitCode = 1; }
    evidence.Save();
}

sealed class Evidence
{
    private readonly string output;
    private readonly string diagnostics;
    private readonly string? selection;
    private readonly List<CaseResult> cases = [];
    private bool complete;
    public Evidence(string output, string diagnostics, string? selection)
    {
        this.output = output;
        this.diagnostics = diagnostics;
        this.selection = selection;
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(diagnostics);
        Save();
    }
    public async Task Case(string name, Func<Task> run)
    {
        if (selection is not null && name != "forward-migration-preserves-baseline-and-creates-both-queues" && name != selection) return;
        try { await run(); cases.Add(new(name, true)); Console.WriteLine("PASS " + name); Save(); }
        catch (Exception ex) { Failure(name, ex); Save(); throw; }
    }
    public void MarkComplete()
    {
        if (cases.Count != (selection is null ? 23 : 2)) throw new InvalidOperationException("C02_CASE_SELECTION_OR_COUNT_MISMATCH");
        complete = true;
    }
    public void Failure(string name, Exception ex)
    {
        cases.Add(new(name, false));
        File.WriteAllText(Path.Combine(diagnostics, name + ".log"), ex.ToString());
        Console.WriteLine("FAIL " + name);
    }
    public void Save()
    {
        var failed = cases.Count(x => !x.Passed);
        var index = Path.Combine(AppContext.BaseDirectory, "contracts/events/platform/contract-bundle.v1.json");
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            schemaId = "cp6.c02.producer-sql-verification.v1", conclusion = complete && failed == 0 ? "success" : "failure",
            verificationKind = "working-tree SQL integration; not cross-repository transport acceptance",
            assemblySha256 = IdentityEventContracts.Hash(File.ReadAllBytes(typeof(CP6Context).Assembly.Location)),
            contractBundleSha256 = File.Exists(index) ? IdentityEventContracts.Hash(File.ReadAllBytes(index)) : null,
            scope = selection ?? "all producer SQL/HTTP cases", expectedCases = selection is null ? 23 : 2,
            total = cases.Count, passed = cases.Count(x => x.Passed), failed, skipped = 0, cases,
            observedAtUtc = DateTimeOffset.UtcNow
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        var xml = new StringBuilder($"<testsuite name=\"c02-producer-sql\" tests=\"{cases.Count}\" failures=\"{failed}\" skipped=\"0\">\n");
        foreach (var item in cases) xml.Append($"  <testcase name=\"{SecurityElement.Escape(item.Name)}\">{(item.Passed ? "" : "<failure message=\"C02 SQL verification failed.\" />")}</testcase>\n");
        File.WriteAllText(Path.Combine(output, "junit.xml"), xml.Append("</testsuite>\n").ToString());
    }
    private sealed record CaseResult(string Name, bool Passed);
}

sealed partial class IdentitySqlFixture : IAsyncDisposable
{
    private const string Issuer = "https://identity.cp6.test";
    private readonly string name = "CP6C02Test_" + Guid.NewGuid().ToString("N");
    private string connection = "";
    private string master = "";
    private bool owned;
    private readonly Cp6ContractBundle bundle = Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform"));
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CP6_C02_TEST_SQL") ?? throw new InvalidOperationException("C02_REAL_SQL_INPUT_MISSING");
        var settings = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master", TrustServerCertificate = true, MultipleActiveResultSets = false };
        var host = settings.DataSource.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase).Split('\\', ',')[0];
        Require(new[] { "localhost", "127.0.0.1", ".", "(local)", Environment.MachineName }.Contains(host, StringComparer.OrdinalIgnoreCase), "C02_REQUIRES_ISOLATED_LOCAL_SQL");
        master = settings.ConnectionString;
        settings.InitialCatalog = name;
        connection = settings.ConnectionString;
        await using (var admin = new SqlConnection(master))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE [{name}]");
            owned = true;
        }
        await using var db = Create(Guid.NewGuid(), enabled: false);
        await db.GetService<IMigrator>().MigrateAsync("20260908011000_CrmOidcBrowserSessionFamily");
        var legacyTenant = Guid.NewGuid();
        var legacyUser = Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant { Id = legacyTenant, TenantCode = "c02-before", TenantName = "Fixture baseline" });
        db.Sys_Users.Add(new Sys_User { Id = legacyUser, TenantId = legacyTenant, UserName = "c02-before", Password = "fixture-baseline-hash" });
        await db.SaveChangesAsync();
        Require(await Scalar<int>("SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_identity'") == 0, "new schema already existed in baseline");
        await db.Database.MigrateAsync();
        Require(await Scalar<int>("SELECT COUNT(*) FROM sys.tables WHERE schema_id IN (SCHEMA_ID(N'crm_identity'),SCHEMA_ID(N'crm_identity_priority'))") == 11, "both queue schemas and bootstrap must be migrated");
        Require(await Scalar<string>("SELECT Password FROM dbo.Sys_Users WHERE Id=@legacyUser", new { legacyUser }) == "fixture-baseline-hash", "baseline user changed");
        Require(!db.Database.HasPendingModelChanges(), "normal model has unmigrated changes");
        await using var priority = new IdentityMessagingContext(new DbContextOptionsBuilder<IdentityMessagingContext>().UseSqlServer(connection).Options);
        Require(!priority.Database.HasPendingModelChanges(), "priority model has unmigrated changes");
    }

    public async Task ValidSnapshotsAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        var snapshots = await db.CrmIdentitySnapshots.Where(x => x.TenantId == tenant).ToArrayAsync();
        Require(snapshots.Length == 5 && snapshots.All(x => x.Version == 1 && x.RowVersion.Length == 8), "initial snapshot/version set differs");
        var messages = await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant).ToArrayAsync();
        Require(messages.Length == 5, "initial outbox count differs");
        foreach (var row in snapshots) Require(row.PayloadSha256 == IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)), "snapshot content hash differs");
        var validator = Runtime(tenant).Validator;
        foreach (var message in messages) Require(validator.IsValid(message.Payload), "stored event is not valid");
    }

    public async Task UnchangedAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        var user = await db.Sys_Users.SingleAsync();
        user.NickName = "new fixture display";
        var scope = await db.Sys_RoleDataScopes.SingleAsync();
        scope.Modifier = "fixture metadata";
        await db.SaveChangesAsync();
        Require(await Scalar<int>("SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant", new { tenant }) == 1, "unchanged grant advanced version");
        Require(await CountOutbox(tenant) == 5, "unchanged authorization enqueued a message");
    }

    public async Task EnvelopeRollbackAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        (await db.Sys_RoleDataScopes.SingleAsync()).ScopeType = 6;
        await ExpectFailure(() => db.SaveChangesAsync(), "C02_IDENTITY_CONTRACT_INVALID");
        Require(await Scalar<int>("SELECT ScopeType FROM dbo.Sys_RoleDataScope WHERE TenantId=@tenant", new { tenant }) == 2, "business scope escaped rollback");
        Require(await CountOutbox(tenant) == 5, "invalid envelope escaped rollback");
        Require(await Scalar<int>("SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant", new { tenant }) == 1, "invalid version escaped rollback");
    }

    public async Task SnapshotRollbackAsync()
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant, interceptor: new SnapshotFailure());
        (await db.Sys_Users.SingleAsync()).Enable = false;
        await ExpectFailure(() => db.SaveChangesAsync(), "controlled snapshot failure");
        Require(await Scalar<bool>("SELECT Enable FROM dbo.Sys_Users WHERE TenantId=@tenant", new { tenant }), "disabled business row escaped rollback");
        Require(await CountOutbox(tenant, priority: true) == 0, "priority context committed independently");
        Require(await Scalar<int>("SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant", new { tenant }) == 1, "snapshot failure advanced version");
    }

    public async Task OutboxRollbackAsync()
    {
        var tenant = await SeedAsync();
        // A real insertion constraint exercises the SQL rollback without introducing triggers
        // that are incompatible with EF's normal OUTPUT-based insert statement.
        await Execute($"ALTER TABLE crm_identity.Cp6_OutboxMessage WITH NOCHECK ADD CONSTRAINT C02_FixtureRejectOutbox CHECK (TenantId <> '{tenant:D}');");
        try
        {
            await using var db = Create(tenant);
            (await db.Sys_Users.SingleAsync()).ManagerId = Guid.NewGuid();
            await ExpectFailure(() => db.SaveChangesAsync(), "C02_FixtureRejectOutbox");
            Require(await Scalar<Guid?>("SELECT ManagerId FROM dbo.Sys_Users WHERE TenantId=@tenant", new { tenant }) is null, "outbox fault committed business change");
            Require(await CountOutbox(tenant) == 5, "outbox fault left a partial message");
            Require(await Scalar<int>("SELECT MAX(Version) FROM crm_identity.Snapshot WHERE TenantId=@tenant", new { tenant }) == 1, "outbox fault left a partial snapshot");
        }
        finally { await Execute("ALTER TABLE crm_identity.Cp6_OutboxMessage DROP CONSTRAINT C02_FixtureRejectOutbox"); }
    }

    public async Task ConcurrentVersionsAsync()
    {
        var tenant = await SeedAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commands = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var db = Create(tenant);
            var user = await db.Sys_Users.SingleAsync();
            await start.Task;
            user.AuthenticationEpoch = Guid.NewGuid();
            await db.SaveChangesAsync();
        }).ToArray();
        start.SetResult();
        await Task.WhenAll(commands);
        await using var read = Create(tenant);
        var messages = await read.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId.StartsWith("user:"))
            .OrderBy(x => x.AggregateVersion).Select(x => x.AggregateVersion).ToArrayAsync();
        Require(messages.SequenceEqual(Enumerable.Range(1, 13)), "committed versions were lost, duplicated or unordered");
    }

    public async Task SaveFalseAsync(bool asynchronous)
    {
        var tenant = await SeedAsync();
        await using var db = Create(tenant);
        var user = await db.Sys_Users.SingleAsync();
        user.ManagerId = Guid.NewGuid();
        var count = asynchronous ? await db.SaveChangesAsync(false) : db.SaveChanges(false);
        Require(count == 1 && db.Entry(user).State == EntityState.Modified && db.Entry(user).OriginalValues.GetValue<Guid?>(nameof(user.ManagerId)) is null,
            "SaveChanges(false) did not preserve the caller state");
        Require(await CountOutbox(tenant) == 6, "pipeline submitted the business change twice");
        Require(await Scalar<int>("SELECT Version FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId LIKE N'user:%'", new { tenant }) == 2, "wrong version after retained-state save");
        db.ChangeTracker.AcceptAllChanges();
    }

    public async Task NativeRevocationAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        await using var db = Create(tenant);
        (await db.Sys_Users.SingleAsync()).Enable = false;
        await db.SaveChangesAsync();
        Require(await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "native disable did not revoke grant");
        var aggregate = IdentityEventContracts.TokenAggregate(Issuer, grant.Id.ToString("D"));
        Require(await db.CrmIdentitySnapshots.AnyAsync(x => x.TenantId == tenant && x.AggregateId == aggregate), "actual grant jti has no revocation fact");
        Require(!await db.CrmIdentitySnapshots.AnyAsync(x => x.AggregateId.EndsWith(grant.SourceJti)), "source login jti was substituted for CRM jti");
        Require(await CountOutbox(tenant, true) == 2, "disable and revoke did not enter priority queue");
    }

    public async Task DirectRevocationAsync()
    {
        var tenant = await SeedAsync();
        var grant = await GrantAsync(tenant);
        var store = new SqlCrmOidcGrantStore(connection, Runtime(tenant));
        await store.RevokeAsync(grant.Id);
        await store.RevokeAsync(grant.Id);
        Require(await CountOutbox(tenant, true) == 1, "direct repeated revoke was not idempotent");
        Require(await Scalar<bool>("SELECT Revoked FROM dbo.CrmOidcGrant WHERE Id=@Id", grant), "direct grant update missing");
    }

    public async Task ServiceRevocationAsync()
    {
        var tenant = await SeedAsync();
        var jti = Guid.NewGuid().ToString("D");
        var expiry = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());
        await using (var db = Create(tenant))
            await new CrmServiceTokenRecordStore(db, Runtime(tenant)).RecordAsync(Issuer, "reader-a", tenant, jti, expiry, default);
        await using (var db = Create(tenant))
            await new CrmServiceTokenRecordStore(db, Runtime(tenant)).RevokeAsync(Issuer, "reader-b", tenant, jti, default);
        Require(await CountOutbox(tenant, true) == 0, "another client revoked the token");
        foreach (var _ in Enumerable.Range(0, 2))
        {
            await using var db = Create(tenant);
            await new CrmServiceTokenRecordStore(db, Runtime(tenant)).RevokeAsync(Issuer, "reader-a", tenant, jti, default);
        }
        Require(await CountOutbox(tenant, true) == 1, "own-client idempotence failed");
        var aggregate = IdentityEventContracts.TokenAggregate(Issuer, jti);
        var payload = await Scalar<string>("SELECT PayloadJson FROM crm_identity.Snapshot WHERE TenantId=@tenant AND AggregateId=@aggregate", new { tenant, aggregate });
        using var data = JsonDocument.Parse(payload);
        Require(data.RootElement.GetProperty("subjectId").GetString() == "service:reader-a" &&
            data.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset() == expiry, "service revoke changed token identity or expiry");
    }

    public async Task BootstrapAndCursorAsync()
    {
        var tenant = await SeedAsync();
        var runtime = Runtime(tenant);
        await using (var db = Create(tenant)) await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant);
        await using (var db = Create(tenant)) await new IdentityBootstrapService(db, runtime).InitializeAsync(tenant);
        Require(await CountOutbox(tenant) == 5, "repeated bootstrap changed versions");
        IdentityVersionPage first;
        await using (var db = Create(tenant)) first = await new IdentitySnapshotReader(db, runtime, protection).ReadVersionsAsync(tenant, null, 2);
        Require(first.Items.Length == 2 && first.NextCursor is not null, "bounded first page missing");
        await using (var db = Create(tenant)) await ExpectFailure(() => new IdentitySnapshotReader(db, runtime, protection).ReadVersionsAsync(Guid.NewGuid(), first.NextCursor, 2), "C02_INVALID_CURSOR");
        await using (var db = Create(tenant)) { (await db.Sys_Users.SingleAsync()).ManagerId = Guid.NewGuid(); await db.SaveChangesAsync(); }
        await using (var db = Create(tenant))
        {
            try { await new IdentitySnapshotReader(db, runtime, protection).ReadVersionsAsync(tenant, first.NextCursor, 2); throw new InvalidOperationException("changed boundary was accepted"); }
            catch (IdentityReadException ex) { Require(ex.Message == "C02_SNAPSHOT_BOUNDARY_CHANGED", "wrong boundary failure"); }
        }
    }

    private async Task<Guid> SeedAsync()
    {
        var tenant = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var db = Create(tenant);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "c02-" + tenant.ToString("N"), TenantName = "C02 SQL fixture" });
        db.Sys_Roles.Add(new Sys_Role { TenantId = tenant, RoleId = 7, RoleName = "C02 fixture role" });
        db.Sys_Depts.AddRange(new Sys_Dept { Id = first, TenantId = tenant, DeptCode = "ROOT", DeptName = "Fixture root", Path = $"/{first:D}/" },
            new Sys_Dept { Id = second, TenantId = tenant, DeptCode = "CHILD", DeptName = "Fixture child", ParentId = first, Path = $"/{first:D}/{second:D}/" });
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), TenantId = tenant, UserName = "c02-user", Password = "fixture-only-hash", RoleId = 7, DeptId = first });
        if (!await db.Sys_Menus.AnyAsync(x => x.MenuId == 9901)) db.Sys_Menus.Add(new Sys_Menu { MenuId = 9901, MenuKey = "crm-lead", MenuName = "Fixture leads" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { TenantId = tenant, RoleId = 7, MenuId = 9901, ActionCode = "query" });
        db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { TenantId = tenant, RoleId = 7, ResourceKey = "crm-lead", ScopeType = 2 });
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<CrmOidcGrant> GrantAsync(Guid tenant)
    {
        var user = await Scalar<Guid>("SELECT Id FROM dbo.Sys_Users WHERE TenantId=@tenant", new { tenant });
        var grant = new CrmOidcGrant
        {
            Id = Guid.NewGuid(), ClientId = "CP6.Web", CodeHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            RedirectUri = "https://fixture.cp6.test/callback", Challenge = "fixture-challenge", Nonce = "fixture-nonce", SubjectId = user,
            OrganizationId = tenant, SourceJti = Guid.NewGuid().ToString("D"), SourceRefreshHash = Guid.NewGuid().ToString("N"), SecurityStamp = "fixture-stamp",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1), SourceExpiresAtUtc = DateTime.UtcNow.AddMinutes(5), AccessExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };
        await new SqlCrmOidcGrantStore(connection).SaveAsync(grant);
        return grant;
    }

    private CP6Context Create(Guid tenant, bool enabled = true, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<CP6Context>().UseSqlServer(connection, sql => sql.CommandTimeout(120));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new(options.Options, new TenantContext { CurrentTenantId = tenant }, identity: enabled ? Runtime(tenant) : null);
    }
    private CrmIdentityRuntime Runtime(Guid tenant) => new(new CrmIdentityOptions
    {
        Enabled = true, Issuer = Issuer, Tenants = { [tenant] = "us" }, ProjectionReaderClientIds = ["reader-a"]
    }, new IdentityEventValidator(bundle, Issuer));
    private async Task<T> Scalar<T>(string sql, object? args = null)
    {
        await using var db = new SqlConnection(connection);
        return await db.QuerySingleAsync<T>(sql, args);
    }
    private async Task Execute(string sql)
    {
        await using var db = new SqlConnection(connection);
        await db.ExecuteAsync(sql);
    }
    private Task<int> CountOutbox(Guid tenant, bool priority = false) => Scalar<int>(
        $"SELECT COUNT(*) FROM {(priority ? "crm_identity_priority" : "crm_identity")}.Cp6_OutboxMessage WHERE TenantId=@tenant", new { tenant });
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task ExpectFailure(Func<Task> run, string expected)
    {
        try { await run(); }
        catch (Exception ex)
        {
            if (ex.ToString().Contains(expected, StringComparison.Ordinal)) return;
            throw new InvalidOperationException("command failed with an unexpected error", ex);
        }
        throw new InvalidOperationException("expected command failure did not occur");
    }
    public async ValueTask DisposeAsync()
    {
        if (!owned) return;
        using var pool = new SqlConnection(connection);
        SqlConnection.ClearPool(pool);
        await using var admin = new SqlConnection(master);
        await admin.ExecuteAsync($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        owned = false;
    }
    private sealed class SnapshotFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<CrmIdentitySnapshot>().Any(x => x.State == EntityState.Modified))
                throw new InvalidOperationException("controlled snapshot failure");
            return ValueTask.FromResult(result);
        }
    }
}
