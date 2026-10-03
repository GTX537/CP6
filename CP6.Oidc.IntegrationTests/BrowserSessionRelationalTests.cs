using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CP6.Oidc.IntegrationTests;

public sealed partial class GrantStoreRelationalTests
{
    [Fact]
    public async Task Real_refresh_read_before_logout_rechecks_family_inside_transaction()
    {
        var logins = await CreateLoginsAsync();
        var grant = await SaveBrowserGrantAsync(logins);
        var pause = new PauseRefresh(afterUpdate: false);
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant, pause);
        var rotation = CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.CreateStore().RevokeSourceFamilyAsync(grant);
            pause.Release.TrySetResult();
            var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(async () => { await rotation; }));
            Assert.Equal("E-SEC-007", error.Message);
        }
        finally
        {
            pause.Release.TrySetResult();
            await DrainAsync(rotation);
        }
        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
    }

    [Fact]
    public async Task Real_rotation_lock_excludes_logout_until_successor_is_committed()
    {
        var logins = await CreateLoginsAsync();
        var grant = await SaveBrowserGrantAsync(logins);
        var pause = new PauseRefresh(afterUpdate: true);
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant, pause);
        var rotation = CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null);
        Task? logout = null;
        string successor;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            logout = fixture.CreateStore().RevokeSourceFamilyAsync(grant);
            await WaitForBlockingAsync(pause.ConnectionId, logout);
            pause.Release.TrySetResult();
            (successor, _) = await rotation;
            await logout;
        }
        finally
        {
            pause.Release.TrySetResult();
            await DrainAsync(rotation, logout);
        }
        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
        await using var check = fixture.CreateContext(new TenantContext());
        var hash = CrmOidcCrypto.Hash(successor);
        Assert.NotNull(await check.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.TokenHash == hash).Select(x => x.RevokedAt).SingleAsync());
    }

    [Fact]
    public async Task Real_refresh_family_remains_revocable_after_seventy_rotations()
    {
        var logins = await CreateLoginsAsync();
        var firstGrant = await SaveBrowserGrantAsync(logins);
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        var refresh = CreateRefresh(context, tenant);
        var current = logins.Original;
        for (var i = 0; i < 70; i++) current = (await refresh.RotateAsync(current, null, null)).newToken;
        var secondGrant = await SaveBrowserGrantAsync(logins, current);
        var store = fixture.CreateStore();
        await store.RevokeSourceFamilyAsync(firstGrant);
        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
        Assert.True((await store.FindForLogoutAsync(firstGrant.Id))!.Revoked);
        Assert.True((await store.FindForLogoutAsync(secondGrant.Id))!.Revoked);
    }

    [Fact]
    public async Task Genuine_rotated_token_reuse_still_revokes_all_user_sessions()
    {
        var logins = await CreateLoginsAsync();
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        var refresh = CreateRefresh(context, tenant);
        await refresh.RotateAsync(logins.Original, null, null);
        var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => refresh.RotateAsync(logins.Original, null, null)));
        Assert.Equal("E-SEC-008", error.Message);
        await using var check = fixture.CreateContext(new TenantContext());
        Assert.Equal(0, await check.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == logins.User.Id && x.RevokedAt == null));
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("password")]
    public async Task Real_refresh_cannot_upgrade_original_authentication_after_security_change(string change)
    {
        var logins = await CreateLoginsAsync();
        await using (var changed = fixture.CreateContext(new TenantContext()))
        {
            var user = await changed.Sys_Users.IgnoreQueryFilters().SingleAsync(u => u.Id == logins.User.Id);
            if (change == "epoch") user.AuthenticationEpoch = Guid.NewGuid();
            else { user.Password = "changed-password-hash"; user.PasswordChangedAt = DateTime.Now; }
            await changed.SaveChangesAsync();
        }
        var tenant = new TenantContext();
        await using var context = fixture.CreateContext(tenant);
        var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() =>
            CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null)));
        Assert.Equal("E-SEC-007", error.Message);
    }

    [Fact]
    public async Task Concurrent_real_rotations_write_one_successor_and_preserve_reuse_revocation()
    {
        var logins = await CreateLoginsAsync();
        var pauses = new[] { new PauseRefresh(afterUpdate: false), new PauseRefresh(afterUpdate: false) };
        var tenants = new[] { new TenantContext(), new TenantContext() };
        await using var first = fixture.CreateContext(tenants[0], pauses[0]);
        await using var second = fixture.CreateContext(tenants[1], pauses[1]);
        var rotations = new[]
        {
            ObserveRotationAsync(CreateRefresh(first, tenants[0]), logins.Original),
            ObserveRotationAsync(CreateRefresh(second, tenants[1]), logins.Original)
        };
        try
        {
            await Task.WhenAll(pauses.Select(x => x.Reached.Task)).WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var pause in pauses) pause.Release.TrySetResult();
            var results = await Task.WhenAll(rotations);
            var winner = Assert.Single(results, x => x.Error is null);
            Assert.False(string.IsNullOrEmpty(winner.Token));
            var loser = Assert.Single(results, x => x.Error is not null);
            Assert.Equal("E-SEC-008", Assert.IsType<InvalidOperationException>(loser.Error).Message);
        }
        finally
        {
            foreach (var pause in pauses) pause.Release.TrySetResult();
            await DrainAsync(rotations);
        }
        await using var check = fixture.CreateContext(new TenantContext());
        var originalHash = CrmOidcCrypto.Hash(logins.Original);
        var root = await check.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == originalHash);
        Assert.Equal(2, await check.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.BrowserSessionId == root.BrowserSessionId));
        Assert.NotNull(root.ReplacedByTokenHash);
        // Existing reuse handling intentionally revokes even the winner's successor and other user sessions.
        Assert.Equal(0, await check.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.UserId == logins.User.Id && x.RevokedAt == null));
    }

    [Fact]
    public async Task Replacement_insert_failure_rolls_back_original_token_and_can_be_retried()
    {
        var logins = await CreateLoginsAsync();
        var originalHash = CrmOidcCrypto.Hash(logins.Original);
        var constraint = "WP4_RejectReplacement_" + Guid.NewGuid().ToString("N");
        // The literals are fixture-generated GUID/hex values. Existing other sessions are deliberately unvalidated.
        var predicate = $"{C("UserId")} <> '{logins.User.Id:D}' OR {C("TokenHash")} = '{originalHash}'";
        var add = fixture.IsPostgreSql
            ? $"ALTER TABLE {T("Sys_RefreshTokens")} ADD CONSTRAINT {C(constraint)} CHECK ({predicate}) NOT VALID"
            : $"ALTER TABLE {T("Sys_RefreshTokens")} WITH NOCHECK ADD CONSTRAINT {C(constraint)} CHECK ({predicate})";
        await using var admin = fixture.CreateConnection();
        await admin.ExecuteAsync(add);
        try
        {
            var tenant = new TenantContext();
            await using var context = fixture.CreateContext(tenant);
            var error = await Record.ExceptionAsync(() => CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null));
            Assert.NotNull(error);
            var failure = DatabaseFailureClassifier.Classify(error);
            Assert.Equal(DatabaseFailureKind.CheckConstraint, failure.Kind);
            Assert.True(failure.MatchesConstraint(constraint));
            await using var check = fixture.CreateContext(new TenantContext());
            var original = await check.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == originalHash);
            Assert.Null(original.RevokedAt);
            Assert.Null(original.ReplacedByTokenHash);
            Assert.Equal(1, await check.Sys_RefreshTokens.IgnoreQueryFilters().CountAsync(x => x.BrowserSessionId == original.BrowserSessionId));
        }
        finally
        {
            await admin.ExecuteAsync($"ALTER TABLE {T("Sys_RefreshTokens")} DROP CONSTRAINT {C(constraint)}");
        }
        var retryTenant = new TenantContext();
        await using var retry = fixture.CreateContext(retryTenant);
        var (replacement, _) = await CreateRefresh(retry, retryTenant).RotateAsync(logins.Original, null, null);
        var replacementHash = CrmOidcCrypto.Hash(replacement);
        await using var final = fixture.CreateContext(new TenantContext());
        Assert.NotNull((await final.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == originalHash)).RevokedAt);
        Assert.Null((await final.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == replacementHash)).RevokedAt);
    }

    private sealed record BrowserLogins(Sys_User User, string Original, string OtherBrowser, string Native);
    private sealed record RotationResult(string? Token, Exception? Error);

    private static async Task<RotationResult> ObserveRotationAsync(RefreshTokenService service, string token)
    {
        try { return new((await service.RotateAsync(token, null, null)).newToken, null); }
        catch (Exception error) { return new(null, error); }
    }

    private static RefreshTokenService CreateRefresh(CP6Context context, TenantContext tenant) => new(context,
        Options.Create(new SecurityOptions()), tenant, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CrmOidc:Enabled"] = "true" }).Build());

    private async Task<BrowserLogins> CreateLoginsAsync()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var context = fixture.CreateContext(tenant);
        var user = new Sys_User
        {
            Id = Guid.NewGuid(), TenantId = tenant.CurrentTenantId,
            UserName = "browser-" + Guid.NewGuid().ToString("N"), Password = "original-hash"
        };
        context.Sys_Users.Add(user);
        await context.SaveChangesAsync();
        var refresh = CreateRefresh(context, tenant);
        return new(user, await refresh.IssueAsync(user, null, null), await refresh.IssueAsync(user, null, null),
            await refresh.IssueAsync(user, null, null, new RefreshTokenClientContext("Windows", "unrelated-device", "1")));
    }

    private async Task<CrmOidcGrant> SaveBrowserGrantAsync(BrowserLogins logins, string? source = null)
    {
        var grant = Grant();
        grant.SubjectId = logins.User.Id;
        grant.OrganizationId = logins.User.TenantId;
        grant.SourceRefreshHash = CrmOidcCrypto.Hash(source ?? logins.Original);
        grant.SecurityStamp = CrmOidcDirectory.SecurityStamp(logins.User);
        var store = fixture.CreateStore();
        await store.SaveAsync(grant);
        Assert.NotNull(await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
        return grant;
    }

    private async Task AssertOnlyOriginalFamilyLoggedOutAsync(BrowserLogins logins)
    {
        await using var check = fixture.CreateContext(new TenantContext());
        var active = await check.Sys_RefreshTokens.IgnoreQueryFilters().Where(x => x.UserId == logins.User.Id && x.RevokedAt == null)
            .Select(x => x.TokenHash).ToArrayAsync();
        Assert.Equal(new[] { CrmOidcCrypto.Hash(logins.OtherBrowser), CrmOidcCrypto.Hash(logins.Native) }.Order(), active.Order());
    }

    private async Task WaitForBlockingAsync(int blocker, Task logout)
    {
        await using var check = fixture.CreateConnection();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (logout.IsCompleted)
            {
                await logout; // Surface the actual database error, if any, rather than replacing it with a timeout.
                throw new InvalidOperationException("Logout completed before the held rotation lock was released.");
            }
            var blocked = await check.QuerySingleAsync<int>(fixture.IsPostgreSql
                ? "SELECT COUNT(*)::integer FROM pg_stat_activity WHERE datname=current_database() AND @blocker=ANY(pg_blocking_pids(pid))"
                : "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id=@blocker",
                new { blocker }, commandTimeout: 5);
            if (blocked > 0) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Logout did not reach the held database rotation lock.");
    }

    private static async Task DrainAsync(params Task?[] tasks)
    {
        foreach (var task in tasks)
        {
            if (task is null) continue;
            try { await task; }
            catch { /* The test body observes results; cleanup must join all workers even after an assertion fails. */ }
        }
    }

    private sealed class PauseRefresh(bool afterUpdate) : DbCommandInterceptor
    {
        public readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ConnectionId { get; private set; }
        private int _used;

        private async Task PauseAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _used, 1) != 0) return;
            ConnectionId = command.Connection switch
            {
                SqlConnection sql => sql.ServerProcessId,
                NpgsqlConnection postgres => postgres.ProcessID,
                _ => throw new InvalidOperationException("A native SQL Server or PostgreSQL connection is required.")
            };
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!afterUpdate && (command.CommandText.Contains("FROM [Sys_Users]", StringComparison.Ordinal)
                || command.CommandText.Contains("FROM \"Sys_Users\"", StringComparison.Ordinal)))
                await PauseAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (afterUpdate && command.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                && (command.CommandText.Contains("[ReplacedByTokenHash]", StringComparison.Ordinal)
                    || command.CommandText.Contains("\"ReplacedByTokenHash\"", StringComparison.Ordinal)))
                await PauseAsync(command, cancellationToken);
            return result;
        }
    }
}
