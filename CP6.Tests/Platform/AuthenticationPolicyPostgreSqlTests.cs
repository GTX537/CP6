using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Data;
using Npgsql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CP6.Tests.Platform;

[Collection("TenantManagementPostgres")]
public sealed class AuthenticationPolicyPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [TenantManagementPostgresTheory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(null)]
    public async Task Missing_or_invalid_tenant_mode_cannot_become_no_factor_policy(int? mode)
    {
        var id = Guid.NewGuid();
        await using var db = server.Context(new TenantContext { CurrentTenantId = id });
        if (mode.HasValue)
        {
            db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = "policy_" + id.ToString("N"), TenantName = "Policy", TwoFactorMode = mode.Value });
            await db.SaveChangesAsync();
        }
        var service = new TwoFactorService(db, Mock.Of<ITotpService>(), Mock.Of<IEmailSender>(),
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(), Mock.Of<ISecurityAuditService>(), Options.Create(new SecurityOptions()));
        Assert.Equal("AUTH_POLICY_UNAVAILABLE", Assert.Throws<InvalidOperationException>(() => service.ResolveTenantMode(id)).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Credential_and_policy_changes_cannot_be_hidden_by_restoring_old_values()
    {
        var user = await Seed(); await using var db = Context(user); var service = Policy(db);
        var policy = await service.ReadAsync(user.TenantId); var proof = Proof(user, policy);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 0));
        Assert.NotEqual(policy.Version, (await service.ReadAsync(user.TenantId)).Version);
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())))).Message);
        policy = await service.ReadAsync(user.TenantId); proof = Proof(user, policy);
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "replacement"));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, user.Password));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())))).Message);
        var current = await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        Assert.NotEqual(user.CredentialRevision, current.CredentialRevision);
        Assert.Equal(AuthenticationDecisionV1.SessionAllowed, await Gate(db).ExecuteAsync(Proof(current, policy), (a, _) => Task.FromResult(a.Decide())));
    }

    [TenantManagementPostgresTheory]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("sso")]
    public async Task Completion_holds_same_root_as_native_authentication_writers(string kind)
    {
        var user = await Seed(); await using var db = Context(user); await using var writer = Context(user);
        var proof = Proof(user, await Policy(db).ReadAsync(user.TenantId));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completing = Gate(db).ExecuteAsync(proof, async (a, ct) =>
        {
            Assert.Equal(AuthenticationDecisionV1.SessionAllowed, a.Decide()); entered.SetResult();
            await release.Task.WaitAsync(ct); a.User.LastLoginIp = "completed-before-writer"; return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await writer.Database.OpenConnectionAsync();
        var pid = await writer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        Task change = kind switch
        {
            "user" => writer.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Password, "new-password")),
            "tenant" => writer.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2)),
            _ => writer.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO public.\"Sys_TenantSsoConfigs\" (\"Id\",\"TenantId\",\"Authority\",\"ClientId\",\"Scopes\",\"EmailClaim\",\"Enabled\",\"Enforced\",\"AutoProvision\",\"CreateDate\") VALUES (gen_random_uuid(), {user.TenantId}, 'https://issuer.example', 'test-client', 'openid', 'email', true, false, false, LOCALTIMESTAMP)")
        };
        try
        {
            await using var observer = Context(user);
            await WaitForLock(observer, pid); Assert.False(change.IsCompleted);
        }
        finally { release.TrySetResult(); }
        Assert.True(await completing.WaitAsync(TimeSpan.FromSeconds(10))); await change.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Writer_first_rechecks_after_wait_and_repeatable_read_snapshot_cannot_pass()
    {
        var user = await Seed(); await using var db = Context(user); await using var writer = Context(user);
        var proof = Proof(user, await Policy(db).ReadAsync(user.TenantId));
        await using (var tx = await writer.Database.BeginTransactionAsync())
        {
            await AuthenticationCompletionGateV1.AcquireAsync(writer);
            await writer.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AllowPasswordFallback, true));
            var invoked = false;
            await db.Database.OpenConnectionAsync(); var pid = await db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
            var attempt = Gate(db).ExecuteAsync(proof, (a, _) => { invoked = true; return Task.FromResult(a.Decide()); });
            await using var observer = Context(user); await WaitForLock(observer, pid);
            await tx.CommitAsync(); Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => attempt)).Message); Assert.False(invoked);
        }
        await using var stale = Context(user); await using var rr = await stale.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await stale.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == user.Id);
        await writer.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 1));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => AuthenticationCompletionGateV1.AcquireAsync(stale));
        Assert.Equal(PostgresErrorCodes.SerializationFailure, Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
    }

    [CurrentPermissionPostgresFact]
    public async Task Failed_or_cancelled_completion_rolls_back_and_restores_tracking()
    {
        var user = await Seed(); await using var db = Context(user);
        var tracked = await db.Sys_Users.SingleAsync(x => x.Id == user.Id);
        var proof = Proof(user, await Policy(db).ReadAsync(user.TenantId));
        await Assert.ThrowsAsync<IOException>(() => Gate(db).ExecuteAsync<bool>(proof, async (a, ct) =>
        { a.User.LastLoginIp = "must-rollback"; await db.SaveChangesAsync(ct); throw new IOException("required receipt failed"); }));
        Assert.Null(tracked.LastLoginIp); Assert.False(db.ChangeTracker.HasChanges());
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gate(db).ExecuteAsync(proof, async (a, ct) =>
        { a.User.LastLoginIp = "cancelled"; await db.SaveChangesAsync(ct); cancel.Cancel(); return true; }, cancel.Token));
        await using var read = Context(user); Assert.Null((await read.Sys_Users.SingleAsync(x => x.Id == user.Id)).LastLoginIp);
        Assert.Equal(AuthenticationDecisionV1.SessionAllowed, await Gate(db).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())));
    }

    [CurrentPermissionPostgresFact]
    public async Task Sso_requires_local_factor_and_expired_placeholder_is_distinct_from_explicit_remediation()
    {
        var user = await Seed(); await using var db = Context(user);
        var tracked = await db.Sys_Users.SingleAsync(x => x.Id == user.Id);
        tracked.PasswordChangedAt = DateTime.Now.AddYears(-1); tracked.ExternalProvider = "https://issuer.example"; tracked.ExternalSubject = "stable-subject";
        db.Sys_TenantSsoConfigs.Add(new Sys_TenantSsoConfig { TenantId = user.TenantId, Authority = tracked.ExternalProvider, ClientId = "client", Enabled = true, AutoProvision = false });
        await db.SaveChangesAsync();
        var options = Options.Create(new SecurityOptions { Password = new PasswordPolicyOptions { ExpiryDays = 1 } });
        var policies = new AuthenticationPolicyServiceV1(db, options); var gate = new AuthenticationCompletionGateV1(db, policies);
        async Task<AuthenticationDecisionV1> Sso() => await gate.ExecuteAsync(Proof(tracked, await policies.ReadAsync(user.TenantId), AuthenticationPrimaryMethod.Sso), (a, _) => Task.FromResult(a.Decide()));
        Assert.Equal(AuthenticationDecisionV1.SessionAllowed, await Sso());
        Assert.Equal(AuthenticationDecisionV1.PasswordChangeRequired, await gate.ExecuteAsync(Proof(tracked, await policies.ReadAsync(user.TenantId)), (a, _) => Task.FromResult(a.Decide())));
        tracked.MustChangePassword = true; await db.SaveChangesAsync();
        Assert.Equal("ACCOUNT_REMEDIATION_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(Sso)).Message);
        tracked.MustChangePassword = false; await db.SaveChangesAsync();
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        Assert.Equal(AuthenticationDecisionV1.EnrollmentRequired, await Sso());
        tracked.TwoFactorEnabled = true; tracked.TwoFactorSecret = "enabled-factor"; await db.SaveChangesAsync();
        Assert.Equal(AuthenticationDecisionV1.SecondFactorPending, await Sso());
        var proof = Proof(tracked, await policies.ReadAsync(user.TenantId), AuthenticationPrimaryMethod.Sso);
        var factor = new AuthenticationFactorEvidenceV1(proof.CompletionRef, user.TenantId, user.Id, proof.ClientBinding, proof.AuthenticationVersion, DateTimeOffset.UtcNow, "local-verification");
        Assert.Equal(AuthenticationDecisionV1.SessionAllowed, await gate.ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide(factor))));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide(factor with { CompletionRef = Guid.NewGuid() }))))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Quick_switch_requires_explicit_policy_and_never_inherits_another_factor()
    {
        var user = await Seed(); await using var db = Context(user);
        async Task<AuthenticationDecisionV1> Switch() => await Gate(db).ExecuteAsync(Proof(user, await Policy(db).ReadAsync(user.TenantId), AuthenticationPrimaryMethod.QuickSwitchPin), (a, _) => Task.FromResult(a.Decide()));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(Switch)).Message);
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.QuickSwitchAllowed, true));
        Assert.Equal(AuthenticationDecisionV1.SessionAllowed, await Switch());
        await db.Sys_Tenants.Where(x => x.Id == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.TwoFactorMode, 2));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(Switch)).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Setup_preparation_does_not_change_credential_revision_but_activation_and_fallback_do()
    {
        var user = await Seed(); await using var db = Context(user); var tracked = await db.Sys_Users.SingleAsync(x => x.Id == user.Id);
        tracked.TwoFactorSecret = "first-preparation"; await db.SaveChangesAsync(); Assert.Equal(user.CredentialRevision, tracked.CredentialRevision);
        tracked.TwoFactorSecret = "replacement-preparation"; await db.SaveChangesAsync(); Assert.Equal(user.CredentialRevision, tracked.CredentialRevision);
        tracked.TwoFactorEnabled = true; await db.SaveChangesAsync(); Assert.NotEqual(user.CredentialRevision, tracked.CredentialRevision);
        var previous = tracked.CredentialRevision; tracked.AllowPasswordFallback = true; await db.SaveChangesAsync(); Assert.NotEqual(previous, tracked.CredentialRevision);
        previous = tracked.CredentialRevision; tracked.LastLoginIp = "profile-only"; await db.SaveChangesAsync(); Assert.Equal(previous, tracked.CredentialRevision);
    }

    [CurrentPermissionPostgresFact]
    public async Task Forward_migration_preserves_populated_policy_and_credentials_and_does_not_invent_old_proof()
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var owner = server.Context(tenant); var name = "w26_policy_" + Guid.NewGuid().ToString("N");
        await owner.Database.ExecuteSqlRawAsync($"CREATE DATABASE {name}"); await using var db = server.Context(tenant);
        db.Database.SetConnectionString(new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()) { Database = name }.ConnectionString);
        await db.GetService<IMigrator>().MigrateAsync("20261019101500_RefreshAuthenticationStateV1");
        var user = Guid.NewGuid(); var refresh = Guid.NewGuid();
        await using (var old = new BeforeAuthenticationPolicyContext((DbContextOptions<CP6Context>)db.GetService<IDbContextOptions>(), tenant))
        {
            old.Database.SetConnectionString(db.Database.GetConnectionString());
            old.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "legacy-policy", TenantName = "Legacy policy", TwoFactorMode = 2 });
            old.Sys_Users.Add(new Sys_User { Id = user, TenantId = tenant.CurrentTenantId, UserName = "legacy-user", Password = "original-hash", TwoFactorEnabled = true, TwoFactorSecret = "original-secret" });
            old.Sys_RefreshTokens.Add(new Sys_RefreshToken { Id = refresh, UserId = user, TokenHash = "legacy-source-hash", AuthenticationVersion = "legacy-version", ExpiresAt = DateTime.Now.AddDays(1) });
            await old.SaveChangesAsync();
        }
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        var currentTenant = await db.Sys_Tenants.SingleAsync(x => x.Id == tenant.CurrentTenantId);
        var currentUser = await db.Sys_Users.SingleAsync(x => x.Id == user);
        Assert.Equal(2, currentTenant.TwoFactorMode); Assert.Null(currentTenant.QuickSwitchAllowed);
        Assert.NotEqual(Guid.Empty, currentTenant.AuthenticationPolicyRevision); Assert.NotEqual(Guid.Empty, currentUser.CredentialRevision);
        Assert.Equal("original-hash", currentUser.Password); Assert.Equal("original-secret", currentUser.TwoFactorSecret);
        var oldRefresh = await db.Sys_RefreshTokens.SingleAsync(x => x.Id == refresh);
        Assert.Equal("legacy-version", oldRefresh.AuthenticationVersion); Assert.NotEqual(AuthSessionVersion.For(currentUser), oldRefresh.AuthenticationVersion);
        Assert.Throws<NotSupportedException>(() => db.GetService<IMigrator>().GenerateScript("20261019111000_AuthenticationPolicyGateV1", "20261019101500_RefreshAuthenticationStateV1"));
    }

    [CurrentPermissionPostgresFact]
    public async Task Current_denial_and_expiry_after_callback_prevent_new_effects()
    {
        var user = await Seed(); await using var db = Context(user); var proof = Proof(user, await Policy(db).ReadAsync(user.TenantId));
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, DateTime.Now.AddMinutes(1)));
        Assert.Equal("AUTHENTICATION_DENIED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync<bool>(proof, (a, _) => throw new Exception("must not run")))).Message);
        await db.Sys_Users.Where(x => x.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockedUntil, (DateTime?)null));
        proof = proof with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) };
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(proof, async (a, ct) =>
        {
            a.User.LastLoginIp = "expired-effect"; await db.SaveChangesAsync(ct);
            await Task.Delay(1200, ct); return true;
        }))).Message);
        await using var read = Context(user); Assert.Null((await read.Sys_Users.SingleAsync(x => x.Id == user.Id)).LastLoginIp);
    }

    [CurrentPermissionPostgresFact]
    public async Task Commit_rejection_has_no_result_and_reports_unknown_without_replaying_callback()
    {
        var user = await Seed(); await using var db = Context(user);
        var proof = Proof(user, await Policy(db).ReadAsync(user.TenantId)); var suffix = user.Id.ToString("N");
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION public.w26_fail_{suffix}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'fixture authentication commit rejection'; END $$;
            CREATE CONSTRAINT TRIGGER w26_commit_{suffix} AFTER UPDATE ON public."Sys_Users"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."Id"='{user.Id:D}'::uuid)
            EXECUTE FUNCTION public.w26_fail_{suffix}();
            """);
        var attempts = 0;
        await Assert.ThrowsAsync<RefreshCompletionUnknownException>(() => Gate(db).ExecuteAsync(proof, (a, _) =>
        { attempts++; a.User.LastLoginIp = "undelivered"; return Task.FromResult("must not be returned"); }));
        Assert.Equal(1, attempts); Assert.False(db.ChangeTracker.HasChanges()); Assert.Null(db.Database.CurrentTransaction);
        await using var read = Context(user); Assert.Null((await read.Sys_Users.SingleAsync(x => x.Id == user.Id)).LastLoginIp);
    }

    [CurrentPermissionPostgresFact]
    public async Task Sso_and_process_policy_versions_are_original_and_fallback_requires_current_permission()
    {
        var user = await Seed(); await using var db = Context(user);
        db.Sys_TenantSsoConfigs.Add(new Sys_TenantSsoConfig { TenantId = user.TenantId, Authority = "https://issuer.example", ClientId = "client", Enabled = true, Enforced = true, AutoProvision = false });
        await db.SaveChangesAsync();
        var policy = await Policy(db).ReadAsync(user.TenantId);
        Assert.Equal("SSO_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(Proof(user, policy), (a, _) => Task.FromResult(a.Decide())))).Message);
        var proof = Proof(user, policy, AuthenticationPrimaryMethod.Sso);
        await db.Sys_TenantSsoConfigs.Where(x => x.TenantId == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, "other-client"));
        await db.Sys_TenantSsoConfigs.Where(x => x.TenantId == user.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientId, "client"));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => Gate(db).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())))).Message);
        policy = await Policy(db).ReadAsync(user.TenantId); proof = Proof(user, policy, AuthenticationPrimaryMethod.Sso);
        var changed = new AuthenticationPolicyServiceV1(db, Options.Create(new SecurityOptions { TwoFactor = new TwoFactorOptions { EmailFallbackEnabled = false } }));
        Assert.Equal("REAUTH_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => new AuthenticationCompletionGateV1(db, changed).ExecuteAsync(proof, (a, _) => Task.FromResult(a.Decide())))).Message);
        var invalid = new AuthenticationPolicyServiceV1(db, Options.Create(new SecurityOptions { TwoFactor = new TwoFactorOptions { PendingTokenMinutes = 0 } }));
        Assert.Equal("AUTH_POLICY_UNAVAILABLE", (await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.ReadAsync(user.TenantId))).Message);
    }

    private CP6Context Context(Sys_User user) => server.Context(new TenantContext { CurrentTenantId = user.TenantId });
    private static AuthenticationPolicyServiceV1 Policy(CP6Context db) => new(db, Options.Create(new SecurityOptions()));
    private static AuthenticationCompletionGateV1 Gate(CP6Context db) => new(db, Policy(db));
    private static AuthenticationProofV1 Proof(Sys_User user, AuthenticationPolicyV1 policy, AuthenticationPrimaryMethod method = AuthenticationPrimaryMethod.Password)
        => policy.VerifiedPrimary(user, method, "Web", "owned-browser-binding", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
    private async Task<Sys_User> Seed()
    {
        var id = Guid.NewGuid(); await using var db = server.Context(new TenantContext { CurrentTenantId = id });
        db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = "policy_" + id.ToString("N"), TenantName = "Policy" });
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = id, UserName = "policy_" + id.ToString("N"), Password = "original", Enable = true };
        db.Sys_Users.Add(user); await db.SaveChangesAsync(); return user;
    }
    private static async Task WaitForLock(CP6Context observer, int pid)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(deadline.Token))
            await Task.Delay(20, deadline.Token);
    }
}

// Seed an actual older schema with its own model; never retrofit new columns before testing the migration.
internal sealed class BeforeAuthenticationPolicyContext(DbContextOptions<CP6Context> options, ITenantContext tenant) : CP6Context(options, tenant)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Sys_Tenant>().Ignore(x => x.AuthenticationPolicyRevision).Ignore(x => x.QuickSwitchAllowed);
        modelBuilder.Entity<Sys_User>().Ignore(x => x.CredentialRevision);
    }
}
