using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017LockoutOwnerOrderFact]
    public async Task PGT017_password_lockout_orders_with_the_original_Crm_intake_owner()
    {
        const int threshold = 2;
        const int lockoutMinutes = 15;
        const string kind = "login-password-lockout";
        var order = Environment.GetEnvironmentVariable("CP6_PGT017_ORDER");
        Assert.True(order is "owner-first" or "pause-first", "Select one owned CRM order.");
        var crmFirst = order == "owner-first";
        var bridge = Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!;
        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var password = TestPassword();
        var rejectedPassword = TestPassword();
        var options = InquiryPermissionSigningTests.Options();

        await using var h = await Seed();
        var pair = await GrantIntake(h);
        var tenantCode = $"iam_{h.Tenant:N}";
        var account = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        var subject = account.Id;
        var userName = account.UserName;
        var tenant = await h.Db.Sys_Tenants.SingleAsync(row => row.Id == h.Tenant);
        tenant.TwoFactorMode = 1;
        account.Password = new BCryptPasswordHasher(4).Hash(password);
        account.AuthenticationEpoch = Guid.NewGuid();
        account.MustChangePassword = false;
        account.TwoFactorEnabled = false;
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, options.Issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        var environment = IdentityEnvironment(h.Tenant);
        var clientSecret = CrmOidcCrypto.RandomToken();
        environment["CrmOidc__Issuer"] = options.Issuer;
        environment["CrmOidc__Clients__0__SecretSha256"] = CrmOidcCrypto.Hash(clientSecret);
        environment["CrmOidc__Clients__0__RedirectUris__0"] = PrivacyCrmRedirect;
        environment["CrmOidc__TrustedTokenProxyAddresses__0"] = "127.0.0.1";
        environment["InquiryPermissionSigning__Enabled"] = "true";
        environment["InquiryPermissionSigning__Issuer"] = options.Issuer;
        environment["InquiryPermissionSigning__KeyId"] = options.KeyId;
        environment["InquiryPermissionSigning__PrivateKeyBase64"] = options.PrivateKeyBase64;
        environment["InquiryPermissionSigning__PublicKeyBase64"] = options.PublicKeyBase64;
        environment["InquiryPermissionSigning__ActiveFromUtc"] = options.ActiveFromUtc.ToString("O");
        environment["Security__Password__BcryptWorkFactor"] = "4";
        environment["Security__Lockout__MaxFailedAttempts"] = threshold.ToString();
        environment["Security__Lockout__LockoutMinutes"] = lockoutMinutes.ToString();
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "StoreId"] = store.ToString("D");
        environment[prefix + "ConnectionString"] = Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION")!;

        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!, environment, allowAutoRedirect: false);
        var cookies = await SignIn(host, userName, password, tenantCode);
        var grant = await ExchangePrivacyGrant(host, cookies, clientSecret);
        var grantId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(grant.Access).Id);
        var userAggregate = $"user:{subject:D}";
        var grantAggregate = IdentityEventContracts.TokenAggregate(options.Issuer, grantId.ToString("D"));
        string[] affected = [userAggregate, grantAggregate];
        Task<HttpResponseMessage> RejectPassword() => host.Send(new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { UserName = userName, Password = rejectedPassword, TenantCode = tenantCode })
        });

        using (var belowThreshold = await RejectPassword()) await AssertError(belowThreshold, "E-SEC-001");
        var before = await ReadMain();
        Assert.Equal(threshold - 1, before.Count);
        Assert.NotNull(before.LastFailure);
        Assert.Null(before.LockedUntil);
        Assert.False(before.GrantRevoked);
        Assert.Equal(0, before.GrantVersion);
        Assert.Equal(1, before.LoginFailures);
        Assert.Equal(0, before.LockAudits);
        Assert.Null(await ReadReceipt());
        using (var active = await host.Profile(cookies["cp6_at"])) Assert.Equal(HttpStatusCode.OK, active.StatusCode);

        // Start the frozen CRM Owner with the actual browser grant and original Main C02 rows.
        var sources = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant)
            .OrderBy(row => row.AggregateId).ToArrayAsync();
        var session = Path.Combine(bridge, "session.json");
        var temporary = session + ".tmp";
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
            {
                marker = "CP6-owned-inquiry-configuration-interop-v1", issuer = options.Issuer, origin = host.Origin.ToString(),
                accessToken = grant.Access, idToken = grant.Identity, tenantId = h.Tenant, subjectId = subject,
                departmentId = h.Parent, siteId = pair.SiteId, formId = pair.FormId, keyId = options.KeyId,
                publicKeyBase64 = options.PublicKeyBase64, activeFromUtc = options.ActiveFromUtc, authenticationEpoch = account.AuthenticationEpoch,
                sourceRows = sources.Select(row => new { row.AggregateId, row.EventType, row.Version, row.PayloadSha256, row.IsDeleted, row.PayloadJson })
            }));
            File.Move(temporary, session);
            await WaitConfigurationBridge(bridge, "ready");
            await WaitConfigurationBridge(bridge, crmFirst ? "owner-held" : "permission-held");
            var thresholdAttempt = RejectPassword();
            int? waiting = null;
            Guid? originalOperation = null;
            if (crmFirst)
            {
                try
                {
                    await using var blocked = new NpgsqlCommand("""
                        SELECT pid FROM pg_stat_activity WHERE datname=current_database()
                          AND application_name='cp6-main-inquiry-tenant-pause-v1' AND wait_event_type='Lock'
                          AND query LIKE '%advance_identity_v1%' AND cardinality(pg_blocking_pids(pid)) > 0
                        """, crm);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    while (waiting is null)
                    {
                        waiting = (int?)await blocked.ExecuteScalarAsync(timeout.Token);
                        if (waiting is not null) break;
                        Assert.False(thresholdAttempt.IsCompleted, "Main must wait for the authenticated CRM Owner's transaction.");
                        await Task.Delay(25, timeout.Token);
                    }
                    Assert.Equal(before, await ReadMain());
                    Assert.Null(await ReadReceipt());
                    var pending = await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, default, subject);
                    Assert.NotNull(pending);
                    Assert.Equal((subject, store, kind, Guid.Empty, true),
                        (pending.UserId, pending.StoreId, pending.Kind, pending.ActorId, pending.SealStarted));
                    Assert.Equal(affected.Order(StringComparer.Ordinal), pending.ReadAdvances().Select(row => row.Aggregate).Order(StringComparer.Ordinal));
                    originalOperation = pending.OperationId;
                }
                finally { await Signal("release-owner"); }
            }

            using var rejected = await thresholdAttempt;
            await AssertError(rejected, "E-SEC-001");
            var after = await ReadMain();
            Assert.Equal(threshold, after.Count);
            var failedAt = Assert.IsType<DateTime>(after.LastFailure);
            Assert.True(failedAt > before.LastFailure!.Value);
            Assert.InRange(Assert.IsType<DateTime>(after.LockedUntil), failedAt.AddMinutes(lockoutMinutes), failedAt.AddMinutes(lockoutMinutes).AddMilliseconds(20));
            Assert.Equal((before.Credential, before.Epoch, before.LastLogin), (after.Credential, after.Epoch, after.LastLogin));
            Assert.Equal(before.UserVersion + 1, after.UserVersion);
            Assert.NotEqual(before.UserHash, after.UserHash);
            Assert.True(after.GrantRevoked);
            Assert.Equal(1, after.GrantVersion);
            Assert.Equal(before.Outbox + 2, after.Outbox);
            Assert.Equal(before.LoginFailures, after.LoginFailures);
            Assert.Equal(before.LockAudits + 1, after.LockAudits);
            Assert.Equal(after.UserVersion, await ReadFloor(userAggregate));
            Assert.Equal(after.GrantVersion, await ReadFloor(grantAggregate));
            var receipt = await ReadReceipt();
            Assert.NotNull(receipt);
            if (originalOperation is { } original) Assert.Equal(original, receipt.Value);
            Assert.Null(await IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, default, subject));
            using (var locked = await host.Profile(cookies["cp6_at"])) Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
            await Signal("paused");
            await WaitConfigurationBridge(bridge, "finished");
            await File.WriteAllTextAsync(Path.Combine(bridge, "lockout-crm-order.json"), JsonSerializer.Serialize(new
            {
                Order = order, h.Tenant, Subject = subject, Grant = grantId, Store = store, WaitingMainSealPid = waiting,
                ThresholdHttp = (int)rejected.StatusCode, Receipt = receipt, Before = before, After = after
            }));
        }
        finally { File.Delete(session); File.Delete(temporary); }

        async Task<LockoutCrmOrderFacts> ReadMain()
        {
            var user = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.TenantId == h.Tenant && row.Id == subject);
            var snapshots = await h.Db.CrmIdentitySnapshots.AsNoTracking().Where(row => row.TenantId == h.Tenant && affected.Contains(row.AggregateId)).ToArrayAsync();
            var userSnapshot = snapshots.Single(row => row.AggregateId == userAggregate);
            var grantRevoked = await h.Db.Database.SqlQuery<bool>($"""
                SELECT "Revoked" AS "Value" FROM public."CrmOidcGrant"
                WHERE "OrganizationId"={h.Tenant} AND "SubjectId"={subject} AND "Id"={grantId} AND "Consumed"
                """).SingleAsync();
            var outbox = await h.Db.Database.SqlQuery<int>($"""
                SELECT ((SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({affected}))
                    +(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"=ANY({affected})))::integer AS "Value"
                """).SingleAsync();
            var audits = await h.Db.Sys_SecurityLogs.AsNoTracking().Where(row => row.TenantId == h.Tenant && row.UserId == subject)
                .Select(row => row.EventType).ToArrayAsync();
            return new(user.FailedLoginCount, user.LastFailedLoginAt, user.LockedUntil, user.LastLoginTime, user.CredentialRevision,
                user.AuthenticationEpoch, userSnapshot.Version, userSnapshot.PayloadSha256,
                snapshots.SingleOrDefault(row => row.AggregateId == grantAggregate)?.Version ?? 0, grantRevoked, outbox,
                audits.Count(type => type == (int)SecurityEventType.LoginFailed), audits.Count(type => type == (int)SecurityEventType.AccountLocked));
        }

        Task<Guid?> ReadReceipt() => h.Db.Database.SqlQuery<Guid?>($"""
            SELECT operation_id AS "Value" FROM cp6_authentication_v1.iam_operations
            WHERE tenant_id={h.Tenant} AND actor_id={Guid.Empty} AND kind={kind} AND state='COMMITTED'
              AND result_json::jsonb->>'UserId'={subject.ToString("D")}
              AND (result_json::jsonb->>'Count')::integer={threshold}
              AND (result_json::jsonb->>'ExpiredWithoutSeal')::boolean=false
            """).SingleOrDefaultAsync();

        async Task<int> ReadFloor(string aggregate)
        {
            await using var sql = new NpgsqlCommand("""
                SELECT coalesce((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.identity_fences
                    WHERE tenant_id=@tenant AND aggregate_id=@aggregate),0)
                """, crm);
            sql.Parameters.AddWithValue("tenant", h.Tenant);
            sql.Parameters.AddWithValue("aggregate", aggregate);
            return (int)(await sql.ExecuteScalarAsync())!;
        }

        async Task Signal(string stage)
        {
            var path = Path.Combine(bridge, stage);
            await File.WriteAllTextAsync(path + ".tmp", "PASS");
            File.Move(path + ".tmp", path);
        }
    }

    private sealed record LockoutCrmOrderFacts(int Count, DateTime? LastFailure, DateTime? LockedUntil, DateTime? LastLogin,
        Guid Credential, Guid Epoch, int UserVersion, string UserHash, int GrantVersion, bool GrantRevoked, int Outbox,
        int LoginFailures, int LockAudits);
}

internal sealed class Pgt017LockoutOwnerOrderFactAttribute : FactAttribute
{
    public Pgt017LockoutOwnerOrderFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_W672_CANDIDATE_EVIDENCE") != "1" ||
            Environment.GetEnvironmentVariable("CP6_PGT017_ORDER") is not ("owner-first" or "pause-first"))
            Skip = "Requires the installed W672 candidate and one owned native CRM submission order.";
    }
}
