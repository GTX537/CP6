using System.Text.Json;
using System.Web;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using CP6.Platform.EntityFramework;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using CP6.Core.Services.Common;
using Npgsql;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests.Platform;

public sealed partial class AuthenticationSsoPostgreSqlTests
{
    private static CrmIdentityRuntime IdentityRuntime(Guid tenant)
    {
        const string issuer = "https://identity-writers.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        options.Tenants.Add(tenant, "us");
        return new(options, new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer));
    }
    private static async Task Approve(Harness h, bool link = true, bool jit = true, int role = 71)
    {
        h.Db.Sys_Roles.Add(new Sys_Role { TenantId = h.User.TenantId, RoleId = role, RoleName = "Approved default" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await h.Db.Sys_TenantSsoConfigs.Where(x => x.TenantId == h.User.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultRoleId, role));
        var p = new SsoIdentityAdmissionPolicyV1(Guid.NewGuid(), Guid.NewGuid(), "https://issuer.example.test", "https://issuer.example.test",
            "cp6", "email", "verified-email", true, link, jit, [role], Guid.NewGuid(), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(p);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_authentication_v1.sso_admission_policies(tenant_id,document) VALUES({h.User.TenantId},{json})");
    }
    private static async Task<(string Reference, Func<Task<AuthenticationProofV1>> Callback, string Key)> Start(Harness h)
    {
        var url = await h.Sso.BuildAuthorizeUrlAsync(h.Organization.TenantCode, "https://cp6.test/callback", null);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query); h.Nonce = query["nonce"]!;
        var key = AuthenticationInteractionStoreV1.NewChallenge(); var binding = AuthenticationEntryServiceV1.WebBinding(key);
        await h.Sso.BindStateAsync(query["state"]!, "Web", binding);
        return (AuthenticationInteractionStoreV1.Hash(query["state"]!),
            () => h.Sso.HandleVerifiedCallbackAsync("code", query["state"]!, "https://cp6.test/callback", "Web", binding), key);
    }

    [TenantManagementPostgresTheory]
    [InlineData(0)]
    [InlineData(int.MinValue)]
    public async Task Approved_first_SSO_identity_keeps_zero_and_negative_default_roles(int role)
    {
        await using var h=await Harness.Create(server,0,false,false,true);await Approve(h,role:role);
        h.Subject="integer-subject";h.EmailOverride="integer@example.test";
        var operation=await Start(h);var proof=await operation.Callback();
        var user=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==proof.UserId);
        Assert.Equal(role,user.RoleId);Assert.False(user.IsPlatformAdmin);
        var snapshot=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId=="user:"+user.Id.ToString("D"));
        Assert.Equal(IdentityEventContracts.UserChangedV2,snapshot.EventType);
        Assert.Equal(IdentityEventContracts.RoleSubject(role),JsonDocument.Parse(snapshot.PayloadJson).RootElement.GetProperty("roles")[0].GetString());
        Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x=>x.UserId==user.Id));
        Assert.Equal(user.Id,(await (await Start(h)).Callback()).UserId);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_first_identity_commits_Main_version_outbox_audit_and_receipt_without_a_session(bool jit)
    {
        await using var h = await Harness.Create(server, 0, false, false, true); await Approve(h);
        if (jit) { h.Subject = "new-subject"; h.EmailOverride = "new@example.test"; }
        else await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null).SetProperty(x => x.IsPlatformAdmin, true));
        var before = jit ? 0 : await h.Db.CrmIdentitySnapshots.Where(x => x.AggregateId == "user:" + h.User.Id.ToString("D")).Select(x => x.Version).SingleAsync();
        var operation = await Start(h); var proof = await operation.Callback();
        var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == proof.UserId);
        Assert.Equal(h.Subject, user.ExternalSubject); Assert.Equal("https://issuer.example.test", user.ExternalProvider);
        Assert.Equal(!jit, user.IsPlatformAdmin); Assert.Equal(jit ? 71 : null, user.RoleId); Assert.False(user.MustChangePassword);
        var aggregate = "user:" + user.Id.ToString("D");
        var snapshot = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.AggregateId == aggregate);
        Assert.Equal(before + 1, snapshot.Version);
        Assert.True(await h.Db.Set<Cp6OutboxMessage>().AnyAsync(x => x.TenantId == user.TenantId && x.AggregateId == aggregate && x.AggregateVersion == snapshot.Version));
        Assert.True(await h.Db.Sys_SecurityLogs.AnyAsync(x => x.UserId == user.Id && x.EventType == (int)(jit ? SecurityEventType.SsoUserProvisioned : SecurityEventType.SsoIdentityLinked)));
        Assert.Equal(jit ? "USER_PROVISIONED" : "IDENTITY_BOUND", await h.Db.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_authentication_v1.sso_mapping_receipts WHERE operation_ref={operation.Reference}").SingleAsync());
        Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync(x => x.UserId == user.Id));
        Assert.Equal(user.Id, (await (await Start(h)).Callback()).UserId); // A fresh SSO reuses the exact committed binding.
        Assert.Equal(before + 1, await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.AggregateId == aggregate).Select(x => x.Version).SingleAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("multiple")]
    [InlineData("bound")]
    [InlineData("partial-provider")]
    [InlineData("unverified")]
    [InlineData("link-denied")]
    [InlineData("jit-denied")]
    [InlineData("role-disabled")]
    [InlineData("username")]
    [InlineData("disabled")]
    public async Task First_identity_conflicts_never_fall_back_or_create_an_account(string condition)
    {
        await using var h = await Harness.Create(server, 0, false, false, true);
        await Approve(h, condition != "link-denied", condition != "jit-denied");
        h.Subject = "new-subject";
        if (condition != "bound") await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null));
        if (condition == "partial-provider") await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalProvider, "https://other.test"));
        if (condition == "unverified") h.EmailVerified = null;
        if (condition == "disabled") await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        if (condition == "multiple")
        {
            h.Db.Sys_Users.Add(new Sys_User { UserName = "duplicate", Email = "USER@example.test", Enable = true });
            await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        }
        if (condition is "jit-denied" or "role-disabled" or "username") h.EmailOverride = "new@example.test";
        if (condition == "role-disabled") await h.Db.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
        if (condition == "username") await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserName, h.EmailOverride!));
        var count = await h.Db.Sys_Users.CountAsync(); var operation = await Start(h);
        await Assert.ThrowsAsync<InvalidOperationException>(operation.Callback);
        Assert.Equal(count, await h.Db.Sys_Users.CountAsync());
        Assert.False(await h.Db.Sys_Users.AnyAsync(x => x.ExternalSubject == "new-subject"));
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x => x.EventType == (int)SecurityEventType.SsoIdentityLinked || x.EventType == (int)SecurityEventType.SsoUserProvisioned));
    }

    [TenantManagementPostgresTheory]
    [InlineData("user-aba")]
    [InlineData("role-aba")]
    [InlineData("browser-close")]
    public async Task Actual_writers_between_intent_and_final_commit_invalidate_original_mapping(string condition)
    {
        var hook = new MappingCommitHook();
        await using var h = await Harness.Create(server, 0, false, false, true, hook); await Approve(h);
        if (condition == "role-aba") { h.Subject = "new-subject"; h.EmailOverride = "new@example.test"; }
        else await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null));
        var operation = await Start(h);
        hook.Action = async count =>
        {
            if (count != 1) return;
            await using var writer = server.Context(new TenantContext { CurrentTenantId = h.User.TenantId });
            if (condition == "browser-close") await new BrowserAuthenticationRecoveryV1(writer).ResolveAsync(operation.Key, true, default);
            else if (condition == "role-aba")
            {
                await writer.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
                await writer.Sys_Roles.Where(x => x.RoleId == 71).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, true));
            }
            else
            {
                await writer.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false));
                await writer.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, true));
            }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(operation.Callback);
        Assert.Equal("CLOSED", await h.Db.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_authentication_v1.sso_mapping_receipts WHERE operation_ref={operation.Reference}").SingleAsync());
        Assert.False(await h.Db.Sys_Users.AnyAsync(x => x.ExternalSubject == h.Subject));
    }

    [TenantManagementPostgresTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Commit_ack_loss_uses_original_receipt_and_never_replays_or_recreates_identity(int lostCommit)
    {
        var hook = new MappingCommitHook();
        await using var h = await Harness.Create(server, 0, false, false, true, hook); await Approve(h);
        h.Subject = "new-subject"; h.EmailOverride = "new@example.test"; var operation = await Start(h);
        hook.Action = count => count == lostCommit ? Task.FromException(new IOException("fixture commit acknowledgement lost")) : Task.CompletedTask;
        var error = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(operation.Callback);
        Assert.Equal(operation.Reference, error.OperationRef); hook.Action = null;
        var status = await new BrowserAuthenticationRecoveryV1(h.Db).ResolveAsync(operation.Key, false, default);
        Assert.Equal(lostCommit == 1 ? "UNKNOWN" : "USER_PROVISIONED", status.IdentityState);
        Assert.Equal(operation.Reference, status.IdentityOperationRef); Assert.NotEqual("ACTIVE", status.State);
        var next = await Start(h);
        if (lostCommit == 1)
        {
            var blocked = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(next.Callback);
            Assert.Equal(operation.Reference, blocked.OperationRef);
        }
        else Assert.NotEqual(h.User.Id, (await next.Callback()).UserId);
        var closed = await new BrowserAuthenticationRecoveryV1(h.Db).ResolveAsync(operation.Key, true, default);
        Assert.Equal(lostCommit == 1 ? "CLOSED" : "USER_PROVISIONED", closed.IdentityState);
        Assert.Equal(2, await h.Db.Sys_Users.CountAsync() + (lostCommit == 1 ? 1 : 0));
        await (await Start(h)).Callback();
        Assert.Equal(2, await h.Db.Sys_Users.CountAsync());
    }

    [TenantManagementPostgresTheory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Required_audit_or_Main_outbox_failure_rolls_back_identity_and_success_receipt(string boundary)
    {
        var fault = new MappingWriteFault();
        await using var h = await Harness.Create(server, 0, false, false, true, fault); await Approve(h);
        h.Subject = "new-subject"; h.EmailOverride = "new@example.test"; var operation = await Start(h);
        fault.Boundary = boundary;
        await Assert.ThrowsAnyAsync<Exception>(operation.Callback); Assert.True(fault.Fired);
        Assert.Equal(1, await h.Db.Sys_Users.CountAsync());
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x => x.EventType == (int)SecurityEventType.SsoUserProvisioned));
        Assert.Equal("CLOSED", await h.Db.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_authentication_v1.sso_mapping_receipts WHERE operation_ref={operation.Reference}").SingleAsync());
        Assert.False(await h.Db.CrmIdentitySnapshots.AnyAsync(x => x.TenantId == h.User.TenantId && x.AggregateId != "user:" + h.User.Id.ToString("D") && x.AggregateId.StartsWith("user:")));
    }

    [TenantManagementPostgresTheory]
    [InlineData("federated")]
    [InlineData("username")]
    public async Task Raw_writers_cannot_duplicate_current_tenant_identity_keys(string key)
    {
        await using var h = await Harness.Create(server, 0, false, false);
        h.Db.Sys_Users.Add(new Sys_User { UserName = "second", Email = "other@example.test", Enable = true }); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var failure = await Assert.ThrowsAsync<PostgresException>(() => key == "federated"
            ? h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"ExternalProvider\"={h.User.ExternalProvider},\"ExternalSubject\"={h.User.ExternalSubject} WHERE \"TenantId\"={h.User.TenantId} AND \"Id\"<>{h.User.Id}")
            : h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"UserName\"='SSO-USER' WHERE \"TenantId\"={h.User.TenantId} AND \"Id\"<>{h.User.Id}"));
        Assert.Equal("23505", failure.SqlState);
        Assert.Equal(1, await h.Db.Sys_Users.CountAsync(x => x.ExternalSubject != null));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_mapping_survives_later_MFA_or_remediation_without_an_active_session(bool remediation)
    {
        await using var h = await Harness.Create(server, remediation ? 0 : 2, false, remediation, true); await Approve(h);
        await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null));
        var operation = await Start(h); var proof = await operation.Callback();
        var policies = new AuthenticationPolicyServiceV1(h.Db, Microsoft.Extensions.Options.Options.Create(new SecurityOptions()));
        var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == h.User.Id);
        var authority = new AuthenticationAuthorityV1(proof, await policies.ReadAsync(h.User.TenantId), user);
        if (remediation) Assert.Equal("ACCOUNT_REMEDIATION_REQUIRED", Assert.Throws<InvalidOperationException>(() => authority.Decide()).Message);
        else Assert.Equal(AuthenticationDecisionV1.EnrollmentRequired, authority.Decide());
        Assert.Equal("bound-subject", user.ExternalSubject); Assert.False(await h.Db.Sys_RefreshTokens.AnyAsync());
        Assert.Equal("IDENTITY_BOUND", (await new BrowserAuthenticationRecoveryV1(h.Db).ResolveAsync(operation.Key, true, default)).IdentityState);
    }

    private sealed class MappingCommitHook : DbTransactionInterceptor
    {
        public Func<int, Task>? Action; private int count;
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (Action != null) await Action(++count); }
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_original_SSO_operations_have_one_binding_and_same_subject_cannot_bypass_pending(bool sameSubject)
    {
        var hook = new MappingCommitHook();
        await using var h = await Harness.Create(server, 0, false, false, true, hook); await Approve(h);
        await h.Db.Sys_Users.Where(x => x.Id == h.User.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExternalSubject, (string?)null));
        var operation = await Start(h); var competing = sameSubject ? "bound-subject" : "competing-subject";
        hook.Action = async count =>
        {
            if (count != 1) return;
            if (sameSubject)
            {
                var blocked = await Assert.ThrowsAsync<SsoMappingRecoveryRequiredV1>(() => OtherOriginalCallback(h, competing));
                Assert.Equal(operation.Reference, blocked.OperationRef);
            }
            else Assert.Equal(h.User.Id, (await OtherOriginalCallback(h, competing)).UserId);
        };
        if (sameSubject) Assert.Equal(h.User.Id, (await operation.Callback()).UserId);
        else await Assert.ThrowsAsync<InvalidOperationException>(operation.Callback);
        Assert.Equal(competing, await h.Db.Sys_Users.AsNoTracking().Select(x => x.ExternalSubject).SingleAsync());
        Assert.Equal(1, await h.Db.Sys_SecurityLogs.CountAsync(x => x.EventType == (int)SecurityEventType.SsoIdentityLinked));
        Assert.Equal(1, await h.Db.Sys_Users.CountAsync());
    }
    private async Task<AuthenticationProofV1> OtherOriginalCallback(Harness h, string subject)
    {
        var tenant = new TenantContext { CurrentTenantId = h.User.TenantId };
        await using var db = server.Context(tenant, IdentityRuntime(tenant.CurrentTenantId));
        var cfg = await db.Sys_TenantSsoConfigs.AsNoTracking().SingleAsync(); var options = Options.Create(new SecurityOptions()); options.Value.Password.ExpiryDays = 1;
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())); var states = new SsoStateStore(cache, options);
        var configs = new Mock<ITenantSsoConfigService>(); configs.Setup(x => x.GetByTenantCodeAsync(It.IsAny<string>())).ReturnsAsync(cfg);
        configs.Setup(x => x.DecryptClientSecret(It.IsAny<Sys_TenantSsoConfig>())).Returns("fixture-secret");
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var discovery = new Mock<IOidcDiscovery>(); discovery.Setup(x => x.GetAsync(cfg.Authority)).ReturnsAsync(new OidcEndpoints(cfg.Authority + "/authorize", cfg.Authority + "/token", cfg.Authority, [key]));
        var tokens = new Mock<ISsoTokenClient>();
        var service = new SsoService(configs.Object, states, discovery.Object, tokens.Object, db, new SecurityAuditService(db), tenant,
            new BCryptPasswordHasher(4), new AuthenticationPolicyServiceV1(db, options), options);
        var query = HttpUtility.ParseQueryString(new Uri(await service.BuildAuthorizeUrlAsync(h.Organization.TenantCode, "https://cp6.test/callback", null)).Query);
        tokens.Setup(x => x.ExchangeCodeForIdTokenAsync(It.IsAny<OidcEndpoints>(), It.IsAny<Sys_TenantSsoConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(cfg.Authority, cfg.ClientId,
                [new Claim("sub", subject), new Claim("email", h.User.Email!), new Claim("email_verified", "true"), new Claim("nonce", query["nonce"]!)],
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256))));
        var binding = AuthenticationEntryServiceV1.WebBinding(AuthenticationInteractionStoreV1.NewChallenge());
        await service.BindStateAsync(query["state"]!, "Web", binding);
        return await service.HandleVerifiedCallbackAsync("code", query["state"]!, "https://cp6.test/callback", "Web", binding);
    }

    [CurrentPermissionPostgresFact]
    public async Task Concurrent_actual_writers_serialize_and_cannot_claim_the_same_federated_key()
    {
        await using var h = await Harness.Create(server, 0, false, false);
        var second = new Sys_User { UserName = "second", Enable = true }; h.Db.Sys_Users.Add(second); await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        await using var writer = server.Context(new TenantContext { CurrentTenantId = h.User.TenantId });
        await using var observer = server.Context(new TenantContext { CurrentTenantId = h.User.TenantId });
        await using var tx = await h.Db.Database.BeginTransactionAsync();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"ExternalProvider\"='https://new.test',\"ExternalSubject\"='shared-subject' WHERE \"Id\"={h.User.Id}");
        await writer.Database.OpenConnectionAsync(); var pid = await writer.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        var pending = writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"ExternalProvider\"='https://new.test',\"ExternalSubject\"='shared-subject' WHERE \"Id\"={second.Id}");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(deadline.Token)) await Task.Delay(20, deadline.Token);
            Assert.False(pending.IsCompleted);
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(async () => await pending)).SqlState);
        Assert.Equal(h.User.Id, (await SsoIdentityQueriesV1.ExactAsync(observer, h.User.TenantId, "https://new.test", "shared-subject"))!.Id);
    }
    private sealed class MappingWriteFault : DbCommandInterceptor
    {
        public string? Boundary; public bool Fired;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && (Boundary == "audit" && command.Parameters.Cast<DbParameter>().Any(x => x.Value is string s && s == "Approved first SSO identity mapping")
                || Boundary == "outbox" && command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("outbox", StringComparison.OrdinalIgnoreCase)))
            { Fired = true; throw new IOException("fixture required " + Boundary + " write rejected"); }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Tenant_expiry_during_required_identity_writes_rolls_back_before_success_receipt()
    {
        var delay = new MappingExpiryDelay();
        await using var h = await Harness.Create(server, 0, false, false, true, delay); await Approve(h);
        var expires = DateTime.Now.AddSeconds(3);
        await h.Db.Sys_Tenants.Where(x => x.Id == h.User.TenantId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpireDate, expires));
        h.Subject = "new-subject"; h.EmailOverride = "new@example.test"; var operation = await Start(h); delay.Until = expires;
        Assert.Equal("E-SEC-027", (await Assert.ThrowsAsync<InvalidOperationException>(operation.Callback)).Message);
        Assert.True(delay.Fired); Assert.Equal(1, await h.Db.Sys_Users.CountAsync());
        Assert.Equal("CLOSED", await h.Db.Database.SqlQuery<string>($"SELECT state AS \"Value\" FROM cp6_authentication_v1.sso_mapping_receipts WHERE operation_ref={operation.Reference}").SingleAsync());
    }
    private sealed class MappingExpiryDelay : DbCommandInterceptor
    {
        public DateTime? Until; public bool Fired;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && Until is { } until && command.CommandText.Contains("INSERT INTO \"Sys_Users\"", StringComparison.Ordinal))
            {
                Fired = true; var remaining = until.AddMilliseconds(30) - DateTime.Now;
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            }
            return result;
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forward_migration_preserves_exact_legacy_identifiers_and_refuses_existing_duplicate_bindings(bool duplicate)
    {
        var tenant = new TenantContext { CurrentTenantId = Guid.NewGuid() };
        await using var template = server.Context(tenant);
        var connection = new NpgsqlConnectionStringBuilder(template.Database.GetConnectionString()) { Database = "sso_upgrade_" + Guid.NewGuid().ToString("N") };
        // This database belongs to the already-owned ephemeral fixture cluster; existing environments are never used.
        await template.Database.ExecuteSqlRawAsync("CREATE DATABASE " + connection.Database);
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql); var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, connection.ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
        await using var db = new CP6Context(options, tenant);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261019152000_SsoIdentityAdmissionPolicyV1");
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant.CurrentTenantId, TenantCode = "upgrade", TenantName = "owned upgrade" });
        var subjects = duplicate ? new[] { "same", "same" } : new[] { "same", "SAME", "same " };
        var users = subjects.Select(s => new Sys_User { UserName = "legacy-duplicate-name", ExternalProvider = "https://issuer.test", ExternalSubject = s, Enable = true }).ToArray();
        db.Sys_Users.AddRange(users); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (duplicate)
        {
            Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync())).SqlState);
            Assert.DoesNotContain("20261019155500_SsoIdentityMappingV1", await db.Database.GetAppliedMigrationsAsync());
            Assert.Equal(2, await db.Sys_Users.CountAsync());
        }
        else
        {
            await migrator.MigrateAsync();
            for (var i = 0; i < subjects.Length; i++) Assert.Equal(users[i].Id, (await SsoIdentityQueriesV1.ExactAsync(db, tenant.CurrentTenantId, "https://issuer.test", subjects[i]))!.Id);
            // A pre-existing duplicated username is not erased or renamed by installation or unrelated edits.
            Assert.Equal(3, await db.Sys_Users.Where(x => x.UserName == "legacy-duplicate-name").ExecuteUpdateAsync(s => s.SetProperty(x => x.Enable, false)));
            db.Sys_Users.Add(new Sys_User { UserName = "LEGACY-DUPLICATE-NAME", Enable = true });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
    }
}
