using System.Data.Common;
using CP6.Core.EFDbContext;
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

namespace CP6.Oidc.IntegrationTests;

public sealed partial class GrantStoreSqlTests
{
    [Fact]
    public async Task Real_refresh_read_before_logout_rechecks_family_inside_transaction()
    {
        var logins = await CreateLoginsAsync();
        var grant = await SaveBrowserGrantAsync(logins);
        var pause = new PauseRefresh(afterUpdate: false);
        var tenant = new TenantContext();
        await using var context = CreateContext(tenant, pause);
        var rotation = CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { await new SqlCrmOidcGrantStore(_connection).RevokeSourceFamilyAsync(grant); }
        finally { pause.Release.TrySetResult(); }

        var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(async () => { await rotation; }));
        Assert.Equal("E-SEC-007", error.Message);
        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
    }

    [Fact]
    public async Task Real_rotation_lock_excludes_logout_until_successor_is_committed()
    {
        var logins = await CreateLoginsAsync();
        var grant = await SaveBrowserGrantAsync(logins);
        var pause = new PauseRefresh(afterUpdate: true);
        var tenant = new TenantContext();
        await using var context = CreateContext(tenant, pause);
        var rotation = CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var logout = new SqlCrmOidcGrantStore(_connection).RevokeSourceFamilyAsync(grant);
        try { await WaitForSqlBlockingAsync(pause.ConnectionId); }
        finally { pause.Release.TrySetResult(); }
        var (successor, _) = await rotation;
        await logout;

        await AssertOnlyOriginalFamilyLoggedOutAsync(logins);
        await using var check = new SqlConnection(_connection);
        Assert.NotNull(await check.QuerySingleAsync<DateTime?>(
            "SELECT RevokedAt FROM dbo.Sys_RefreshTokens WHERE TokenHash=@hash", new { hash = CrmOidcCrypto.Hash(successor) }));
    }

    [Fact]
    public async Task Real_refresh_family_remains_revocable_after_seventy_rotations()
    {
        var logins = await CreateLoginsAsync();
        var firstGrant = await SaveBrowserGrantAsync(logins);
        var tenant = new TenantContext();
        await using var context = CreateContext(tenant);
        var refresh = CreateRefresh(context, tenant);
        var current = logins.Original;
        for (var i = 0; i < 70; i++) current = (await refresh.RotateAsync(current, null, null)).newToken;
        var secondGrant = await SaveBrowserGrantAsync(logins, current);
        var store = new SqlCrmOidcGrantStore(_connection);

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
        await using var context = CreateContext(tenant);
        var refresh = CreateRefresh(context, tenant);
        await refresh.RotateAsync(logins.Original, null, null);

        var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() => refresh.RotateAsync(logins.Original, null, null)));

        Assert.Equal("E-SEC-008", error.Message);
        await using var check = new SqlConnection(_connection);
        Assert.Equal(0, await check.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM dbo.Sys_RefreshTokens WHERE UserId=@Id AND RevokedAt IS NULL", new { logins.User.Id }));
    }

    [Theory]
    [InlineData("epoch")]
    [InlineData("password")]
    public async Task Real_refresh_cannot_upgrade_original_authentication_after_security_change(string change)
    {
        var logins = await CreateLoginsAsync();
        await using (var changed = CreateContext(new TenantContext()))
        {
            var user = await changed.Sys_Users.IgnoreQueryFilters().SingleAsync(u => u.Id == logins.User.Id);
            if (change == "epoch") user.AuthenticationEpoch = Guid.NewGuid();
            else { user.Password = "changed-password-hash"; user.PasswordChangedAt = DateTime.Now; }
            await changed.SaveChangesAsync();
        }
        var tenant = new TenantContext();
        await using var context = CreateContext(tenant);

        var error = Assert.IsType<InvalidOperationException>(await Record.ExceptionAsync(() =>
            CreateRefresh(context, tenant).RotateAsync(logins.Original, null, null)));

        Assert.Equal("E-SEC-007", error.Message);
    }

    private sealed record BrowserLogins(Sys_User User, string Original, string OtherBrowser, string Native);

    private CP6Context CreateContext(TenantContext tenant, DbCommandInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<CP6Context>().UseSqlServer(_connection);
        if (interceptor != null) builder.AddInterceptors(interceptor);
        return new CP6Context(builder.Options, tenant);
    }

    private static RefreshTokenService CreateRefresh(CP6Context context, TenantContext tenant) => new(context,
        Options.Create(new SecurityOptions()), tenant, new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CrmOidc:Enabled"] = "true" }).Build());

    private async Task<BrowserLogins> CreateLoginsAsync()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var context = CreateContext(tenant);
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = tenant.CurrentTenantId, UserName = "browser-" + Guid.NewGuid().ToString("N"), Password = "original-hash" };
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
        var store = new SqlCrmOidcGrantStore(_connection);
        await store.SaveAsync(grant);
        Assert.NotNull(await store.ConsumeAsync(grant.CodeHash, grant.ClientId, grant.RedirectUri, grant.Challenge));
        return grant;
    }

    private async Task AssertOnlyOriginalFamilyLoggedOutAsync(BrowserLogins logins)
    {
        await using var check = new SqlConnection(_connection);
        var active = (await check.QueryAsync<string>(
            "SELECT TokenHash FROM dbo.Sys_RefreshTokens WHERE UserId=@Id AND RevokedAt IS NULL", new { logins.User.Id })).Order().ToArray();
        Assert.Equal(new[] { CrmOidcCrypto.Hash(logins.OtherBrowser), CrmOidcCrypto.Hash(logins.Native) }.Order(), active);
    }

    private async Task WaitForSqlBlockingAsync(int blocker)
    {
        await using var check = new SqlConnection(_connection);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await check.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id=@blocker", new { blocker }) > 0)
                return;
            await Task.Delay(20);
        }
        throw new TimeoutException("The logout request did not reach the held SQL rotation lock.");
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
            ConnectionId = ((SqlConnection)command.Connection!).ServerProcessId;
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!afterUpdate && command.CommandText.Contains("FROM [Sys_Users]", StringComparison.Ordinal)) await PauseAsync(command, cancellationToken);
            return result;
        }
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (afterUpdate && command.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("[ReplacedByTokenHash]", StringComparison.Ordinal)) await PauseAsync(command, cancellationToken);
            return result;
        }
    }
}
