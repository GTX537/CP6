using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Client;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using OtpNet;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    internal static Task RegisterLockoutStoreFixture(NpgsqlConnection crm, NpgsqlConnectionStringBuilder seal, Guid tenant, Guid store)
        => InstallUserSealFixture(crm, seal, tenant, store);

    [Pgt017LockoutCandidateFact]
    public async Task PGT017_login_lockout_preserves_the_original_failure_and_success_order()
    {
        const int threshold = 5;
        const string browserRoute = "api/auth/login";
        const string nativeRoute = "api/client-auth/login";

        var mode = LockoutSetting("MODE");
        var scenario = LockoutSetting("SCENARIO");
        var expiredBeforeRecovery = scenario == "cold-expired";
        var lockoutMinutes = expiredBeforeRecovery ? 1 : 15;
        var native = mode.StartsWith("native-", StringComparison.Ordinal);
        var factor = !mode.EndsWith("-password", StringComparison.Ordinal);
        var enrollment = mode.EndsWith("-enroll", StringComparison.Ordinal);
        var client = new ClientContextDto { ClientKind = "Windows", DeviceId = "w672-owned-device", AppVersion = "1.0.0" };
        var password = TestPassword();
        var incorrectPassword = TestPassword();

        await using var h = await Seed();
        var user = await h.Db.Sys_Users.SingleAsync(row => row.Id == h.ActorId);
        var subject = user.Id;
        var userName = user.UserName;
        var tenantCode = $"iam_{h.Tenant:N}";
        var tenant = await h.Db.Sys_Tenants.SingleAsync(row => row.Id == h.Tenant);
        tenant.TwoFactorMode = enrollment ? 2 : 1;
        user.Password = new BCryptPasswordHasher(4).Hash(password);
        user.AuthenticationEpoch = Guid.NewGuid();
        user.MustChangePassword = false;
        user.TwoFactorEnabled = factor && !enrollment;
        user.TwoFactorSecret = user.TwoFactorEnabled ? Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20)) : null;
        user.TwoFactorEnrolledAt = user.TwoFactorEnabled ? DateTime.Now : null;
        user.FailedLoginCount = threshold - 1;
        user.LastFailedLoginAt = DateTime.Now.AddMinutes(-1);
        user.LastLoginTime = DateTime.Now.AddDays(-1);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var issuer = InquiryPermissionSigningTests.Options().Issuer;
        await new IdentityBootstrapService(h.Db, ContextRuntime(h.Tenant, issuer)).InitializeAsync(h.Tenant);
        h.Db.ChangeTracker.Clear();

        var store = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        var crmConnection = Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER");
        await using var crm = new NpgsqlConnection(crmConnection);
        await crm.OpenAsync();
        await InstallUserSealFixture(crm, seal, h.Tenant, store);

        var environment = IdentityEnvironment(h.Tenant);
        environment["CrmOidc__Issuer"] = issuer;
        environment["Security__Password__BcryptWorkFactor"] = "4";
        environment["Security__Lockout__MaxFailedAttempts"] = threshold.ToString();
        environment["Security__Lockout__LockoutMinutes"] = lockoutMinutes.ToString();
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + Environment.GetEnvironmentVariable("CP6_PGT017_MAIN_HTTP_PORT");
        environment["JWT__Secret"] = CrmOidcCrypto.RandomToken() + CrmOidcCrypto.RandomToken();
        var prefix = $"CrmInquiryTenantPause__Tenants__{h.Tenant:D}__";
        environment[prefix + "StoreId"] = store.ToString("D");
        environment[prefix + "ConnectionString"] = seal.ConnectionString;
        var mainConnection = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString());
        await using var relay = scenario == "main-ack" ? new StockPostgresCommitRelay(mainConnection.Port, mainConnection.Username!,
            int.Parse(LockoutSetting("RELAY_PORT"))) : null;
        var apiConnection = new NpgsqlConnectionStringBuilder(mainConnection.ConnectionString);
        if (relay is not null)
        {
            apiConnection.Port = relay.Port;
            apiConnection.SslMode = SslMode.Disable;
            apiConnection.Pooling = false;
        }
        await using var host = await AuthenticationApiProcess.Start(apiConnection.ConnectionString, environment);

        Dictionary<string, string>? pendingCookies = null;
        string? challenge = null;
        string? challengeHash = null;
        if (factor)
        {
            using var begin = await Login(host, password);
            Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
            if (native)
            {
                var body = await begin.Content.ReadFromJsonAsync<NativeAuthResult>();
                Assert.True(body is not null && body.Session is null && body.State == (enrollment ? "enrollmentRequired" : "twoFactorRequired"));
                challenge = body!.ChallengeToken;
            }
            else
            {
                pendingCookies = ReadCookies(begin);
                Assert.False(pendingCookies.ContainsKey("cp6_at"));
                challenge = new JwtSecurityTokenHandler().ReadJwtToken(pendingCookies["cp6_2fa"]).Id;
            }
            Assert.True(!string.IsNullOrEmpty(challenge));
            challengeHash = AuthenticationInteractionStoreV1.Hash(challenge!);
            if (enrollment)
            {
                using var setup = await FactorRequest("setup");
                Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
            }
        }

        var source = $"user:{subject:D}";
        var before = await MainFacts();
        var floorBefore = await Floor();
        Assert.Equal(threshold - 1, before.Count);
        Assert.NotNull(before.LastFailure);
        Assert.Null(before.LockedUntil);
        if (factor)
        {
            Assert.NotNull(before.Interaction);
            Assert.Equal(("pending", (Guid?)null, false, 0, 0), LeaseFacts(before.Interaction));
        }

        if (scenario == "success-first")
        {
            await using var held = new NpgsqlConnection(mainConnection.ConnectionString);
            await using var observer = new NpgsqlConnection(mainConnection.ConnectionString);
            await held.OpenAsync();
            await observer.OpenAsync();
            await using var hold = await held.BeginTransactionAsync();
            await using var row = new NpgsqlCommand("""SELECT 1 FROM public."Sys_Users" WHERE "TenantId"=@tenant AND "Id"=@id FOR UPDATE""", held, hold);
            row.Parameters.AddWithValue("tenant", h.Tenant);
            row.Parameters.AddWithValue("id", subject);
            Assert.Equal(1, await row.ExecuteScalarAsync());
            var login = Login(host, password);
            Task<HttpResponseMessage>? rejected = null;
            try
            {
                var first = await WaitForMainBlock(observer, held.ProcessID, "%UPDATE%Sys_Users%", login);
                rejected = Reject();
                var second = await WaitForMainBlock(observer, first, "%cp6_authentication_v1.acquire()%", rejected);
                Assert.Equal(before, await MainFacts());
                await Save("success-first-wait.json", new { First = first, Second = second });
            }
            finally { await hold.RollbackAsync(); }

            using var success = await login;
            using var fail = await rejected!;
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.True(native ? (await success.Content.ReadFromJsonAsync<NativeAuthResult>())?.Session is not null
                : ReadCookies(success).ContainsKey("cp6_at"));
            await AssertError(fail, "E-SEC-001");
            var after = await MainFacts();
            Assert.Equal(1, after.Count);
            Assert.True(after.LastFailure > before.LastFailure && after.LastLogin > before.LastLogin);
            Assert.Null(after.LockedUntil);
            Assert.Equal((before.Credential, before.Epoch, before.Version, before.SourceHash, before.Outbox, before.Sessions + 1, before.SessionProofs + 1),
                (after.Credential, after.Epoch, after.Version, after.SourceHash, after.Outbox, after.Sessions, after.SessionProofs));
            Assert.Equal(floorBefore, await Floor());
            await AssertAudits((SecurityEventType.LoginSuccess, 1), (SecurityEventType.LoginFailed, 1), (SecurityEventType.AccountLocked, 0));
            Assert.Null(await Pending());
            await Save("success-first.json", new { Mode = mode, Before = before, After = after, Floor = floorBefore });
            return;
        }

        var acceptedNotBefore = DateTime.Now;
        if (scenario is "cold-restart" or "cold-expired")
        {
            var deniedBefore = CountDeniedConnections();
            await SetSealLogin(false);
            IamCrmUserSealV1 original;
            DateTime acceptedNotAfter;
            try
            {
                using var failure = await Reject();
                acceptedNotAfter = DateTime.Now;
                await AssertInitialReply(failure, unresolved: true);
                Assert.True(CountDeniedConnections() > deniedBefore);
                original = Assert.IsType<IamCrmUserSealV1>(await Pending());
                Assert.False(original.SealStarted);
                var waiting = await MainFacts();
                Assert.Equal(before with { Interaction = waiting.Interaction }, waiting);
                Assert.Equal(floorBefore, await Floor());
                await AssertAudits((ExpectedFailureAudit(), 0));
                if (factor)
                {
                    Assert.Equal(before.Interaction!.Completion, waiting.Interaction!.Completion);
                    Assert.Equal(waiting.Interaction.Completion, original.CompletionId);
                    Assert.Equal("completing", waiting.Interaction.State);
                    Assert.NotNull(waiting.Interaction.Lease);
                    Assert.Equal((false, 0, 0), (waiting.Interaction.Factor, waiting.Interaction.Consumed, waiting.Interaction.Sessions));
                }
                else Assert.Null(original.CompletionId);
                await Save("cold-accepted.json", new { Mode = mode, Before = before, Waiting = waiting, Floor = floorBefore,
                    original.OperationId, original.StoreId, original.SealStarted, original.CompletionId, Plan = OriginalSource(original),
                    AcceptedNotBefore = acceptedNotBefore, AcceptedNotAfter = acceptedNotAfter });

                await host.StopAsync();
                environment["Security__Lockout__LockoutMinutes"] = (lockoutMinutes + 7).ToString();
                if (expiredBeforeRecovery)
                {
                    var wait = acceptedNotAfter.AddMinutes(lockoutMinutes).AddSeconds(1) - DateTime.Now;
                    if (wait > TimeSpan.Zero) await Task.Delay(wait);
                }
                else await SetSealLogin(true);

                var refusalCount = CountDeniedConnections();
                await using var restarted = await AuthenticationApiProcess.Start(apiConnection.ConnectionString, environment);
                using var currentAdmission = await Login(restarted, password);
                if (expiredBeforeRecovery)
                {
                    Assert.Equal(HttpStatusCode.OK, currentAdmission.StatusCode);
                    Assert.True(native ? (await currentAdmission.Content.ReadFromJsonAsync<NativeAuthResult>())?.Session is not null
                        : ReadCookies(currentAdmission).ContainsKey("cp6_at"));
                    var afterExpiry = await MainFacts();
                    Assert.Equal(before with { Count = 0, LastFailure = null, LastLogin = afterExpiry.LastLogin,
                        Sessions = before.Sessions + 1, SessionProofs = before.SessionProofs + 1 }, afterExpiry);
                    Assert.True(afterExpiry.LastLogin > acceptedNotAfter);
                    Assert.Equal(floorBefore, await Floor());
                    Assert.Equal(refusalCount, CountDeniedConnections());
                    Assert.Null(await Pending());
                    await AssertAudits((SecurityEventType.LoginFailed, 1), (SecurityEventType.LoginSuccess, 1), (SecurityEventType.AccountLocked, 0));
                    var result = await h.Db.Database.SqlQuery<string>($"""
                        SELECT result_json AS "Value" FROM cp6_authentication_v1.iam_operations
                        WHERE tenant_id={h.Tenant} AND operation_id={original.OperationId} AND state='COMMITTED'
                        """).SingleAsync();
                    using var receipt = JsonDocument.Parse(result);
                    var deadline = receipt.RootElement.GetProperty("LockedUntil").GetDateTime();
                    Assert.True(receipt.RootElement.GetProperty("ExpiredWithoutSeal").GetBoolean());
                    Assert.Equal(threshold, receipt.RootElement.GetProperty("Count").GetInt32());
                    Assert.InRange(deadline, acceptedNotBefore.AddMinutes(lockoutMinutes).AddMilliseconds(-1), acceptedNotAfter.AddMinutes(lockoutMinutes));
                    await Save("cold-expired.json", new { Mode = mode, original.OperationId, Deadline = deadline, After = afterExpiry, Floor = await Floor() });
                }
                else
                {
                    await AssertError(currentAdmission, "E-SEC-002");
                    await AssertCommitted(acceptedNotAfter, original);
                    await Save("cold-recovered.json", new { Mode = mode, original.OperationId, RestartedPid = restarted.ProcessId,
                        After = await MainFacts(), Floor = await Floor() });
                }
                return;
            }
            finally { await SetSealLogin(true); }
        }

        if (scenario is "lockout-first" or "main-ack")
        {
            await using var barrier = new NpgsqlConnection(crmConnection);
            await barrier.OpenAsync();
            await using var hold = await barrier.BeginTransactionAsync();
            await using var holdTenant = new NpgsqlCommand("SELECT crm_identity_lock_v1.hold_v1(@tenant)", barrier, hold);
            holdTenant.Parameters.AddWithValue("tenant", h.Tenant);
            await holdTenant.ExecuteNonQueryAsync();
            var failure = Reject();
            Task<HttpResponseMessage>? admission = null;
            IamCrmUserSealV1 original;
            try
            {
                var crmWriter = await WaitForMainBlock(crm, barrier.ProcessID, "%advance_identity_v1%", failure);
                original = Assert.IsType<IamCrmUserSealV1>(await Pending());
                Assert.True(original.SealStarted);
                var waiting = await MainFacts();
                Assert.Equal(before with { Interaction = waiting.Interaction }, waiting);
                Assert.Equal(floorBefore, await Floor());
                if (relay is not null) relay.Arm();
                else
                {
                    admission = Login(host, password);
                    await using var observer = new NpgsqlConnection(mainConnection.ConnectionString);
                    await observer.OpenAsync();
                    var mainWait = await WaitForLockoutAdmission(observer, admission);
                    await Save("lockout-first-wait.json", new { CrmWriter = crmWriter, MainAdmission = mainWait, original.OperationId });
                }
            }
            finally { await hold.RollbackAsync(); }

            using var fail = await failure;
            if (relay is not null) await relay.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
            await AssertInitialReply(fail, unresolved: relay is not null);
            using var locked = await (admission ?? Login(host, password));
            await AssertError(locked, "E-SEC-002");
            await AssertCommitted(DateTime.Now, original);
            await Save("lockout-committed.json", new { Mode = mode, Scenario = scenario, original.OperationId,
                MainAckDropped = relay is not null, After = await MainFacts(), Floor = await Floor() });
            return;
        }

        using (var fail = await Reject()) await AssertInitialReply(fail);
        var acceptedUpperBound = DateTime.Now;
        using (var locked = await Login(host, password)) await AssertError(locked, "E-SEC-002");
        await AssertCommitted(acceptedUpperBound);
        await Save("lockout-committed.json", new { Mode = mode, Scenario = scenario, Before = before, After = await MainFacts(), Floor = await Floor() });

        async Task<HttpResponseMessage> Login(AuthenticationApiProcess api, string submittedPassword)
        {
            object body = native ? new NativeLoginRequest { UserName = userName, Password = submittedPassword, TenantCode = tenantCode, Client = client }
                : new { UserName = userName, Password = submittedPassword, TenantCode = tenantCode };
            using var request = new HttpRequestMessage(HttpMethod.Post, native ? nativeRoute : browserRoute) { Content = JsonContent.Create(body) };
            return await api.Send(request);
        }

        async Task<HttpResponseMessage> FactorRequest(string action)
        {
            var path = (native ? "api/client-auth/2fa/" : "api/auth/2fa/") + action;
            const string invalidFixtureCode = "invalid";
            using var request = native
                ? new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = JsonContent.Create(new NativeTwoFactorRequest { ChallengeToken = challenge!, Client = client,
                        Code = invalidFixtureCode, Method = "totp" })
                }
                : Request(HttpMethod.Post, path, pendingCookies!, new { Code = invalidFixtureCode, Method = "totp" });
            return await host.Send(request);
        }

        Task<HttpResponseMessage> Reject() => factor ? FactorRequest(enrollment ? "enroll" : "verify") : Login(host, incorrectPassword);

        async Task AssertInitialReply(HttpResponseMessage response, bool unresolved = false)
        {
            if (unresolved && response.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                await AssertError(response, "AUTHENTICATION_COMPLETION_OUTCOME_UNKNOWN", HttpStatusCode.ServiceUnavailable);
                return;
            }
            await AssertError(response, factor ? "E-SEC-011" : "E-SEC-001");
        }

        async Task AssertCommitted(DateTime upperBound, IamCrmUserSealV1? original = null)
        {
            var after = await MainFacts();
            var exactDeadline = Assert.IsType<DateTime>(after.LockedUntil);
            var failedAt = Assert.IsType<DateTime>(after.LastFailure);
            Assert.Equal(threshold, after.Count);
            Assert.InRange(failedAt, acceptedNotBefore.AddMilliseconds(-1), upperBound);
            Assert.InRange(exactDeadline, failedAt.AddMinutes(lockoutMinutes), failedAt.AddMinutes(lockoutMinutes).AddMilliseconds(20));
            Assert.Equal((before.Credential, before.Epoch, before.LastLogin, before.Sessions, before.SessionProofs),
                (after.Credential, after.Epoch, after.LastLogin, after.Sessions, after.SessionProofs));
            Assert.Equal(before.Version + 1, after.Version);
            Assert.NotEqual(before.SourceHash, after.SourceHash);
            Assert.True(after.Outbox > before.Outbox);
            Assert.Equal(after.Version, await Floor());
            Assert.Null(await Pending());
            if (factor)
            {
                Assert.Equal(before.Interaction!.Completion, after.Interaction!.Completion);
                Assert.Equal(("pending", (Guid?)null, false, 0, 0), LeaseFacts(after.Interaction));
            }
            await AssertAudits((ExpectedFailureAudit(), native || factor ? 1 : 2), (SecurityEventType.LoginSuccess, 0));
            if (native || factor) await AssertAudits((SecurityEventType.AccountLocked, 1));
            if (original is null) return;
            var plan = OriginalSource(original);
            Assert.Equal((before.Version + 1, before.SourceHash, after.SourceHash), (plan.Version, plan.PredecessorHash, plan.IntentHash));
            Assert.True(await h.Db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM cp6_authentication_v1.iam_operations
                    WHERE tenant_id={h.Tenant} AND operation_id={original.OperationId} AND state='COMMITTED') AS "Value"
                """).SingleAsync());
        }

        CrmUserSealAdvanceV1 OriginalSource(IamCrmUserSealV1 original)
        {
            Assert.Equal((subject, store), (original.UserId, original.StoreId));
            return Assert.Single(original.ReadAdvances(), row => row.Aggregate == source);
        }

        SecurityEventType ExpectedFailureAudit() => factor ? SecurityEventType.TwoFactorFailed
            : native ? SecurityEventType.LoginFailed : SecurityEventType.AccountLocked;

        async Task AssertAudits(params (SecurityEventType Type, int Count)[] expected)
        {
            var audit = await h.Db.Sys_SecurityLogs.AsNoTracking().Where(row => row.TenantId == h.Tenant && row.UserId == subject).ToListAsync();
            foreach (var (type, count) in expected) Assert.Equal(count, audit.Count(row => row.EventType == (int)type));
            if (!factor)
            {
                var failures = audit.Where(row => row.EventType is (int)SecurityEventType.LoginFailed or (int)SecurityEventType.AccountLocked).ToArray();
                Assert.All(failures, row => Assert.Equal(tenantCode, row.RequestTenantCode));
                var originalFailure = scenario == "success-first" || expiredBeforeRecovery || native ? SecurityEventType.LoginFailed : SecurityEventType.AccountLocked;
                if (expected.Any(expectation => expectation.Type == originalFailure && expectation.Count > 0))
                {
                    var reason = native ? "client=Windows;device=w672-owned-device;version=1.0.0;detail=wrong password" : "wrong password";
                    Assert.Single(failures, row => row.EventType == (int)originalFailure && row.Reason == reason);
                }
                return;
            }
            foreach (var row in audit.Where(row => row.EventType == (int)SecurityEventType.TwoFactorFailed))
            {
                using var context = JsonDocument.Parse(row.ContextJson!);
                Assert.Equal(before.Interaction!.Completion, context.RootElement.GetProperty("CompletionRef").GetGuid());
            }
        }

        async Task<LockoutMainFacts> MainFacts()
        {
            var account = await h.Db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject);
            var projection = await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(row => row.TenantId == h.Tenant && row.AggregateId == source);
            var outbox = await h.Db.Database.SqlQuery<int>($"""
                SELECT ((SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"={source})
                      +(SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"={h.Tenant} AND "AggregateId"={source}))::int AS "Value"
                """).SingleAsync();
            var sessions = await h.Db.Sys_RefreshTokens.AsNoTracking().CountAsync(row => row.TenantId == h.Tenant && row.UserId == subject);
            var sessionProofs = await h.Db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM cp6_authentication_v1.session_evidence e
                JOIN public."Sys_RefreshTokens" r ON r."TokenHash"=e.refresh_hash WHERE r."TenantId"={h.Tenant} AND r."UserId"={subject}
                """).SingleAsync();
            var interaction = challengeHash is null ? null : await h.Db.Database.SqlQuery<LockoutInteraction>($"""
                SELECT i.completion_id AS "Completion",i.state AS "State",i.lease AS "Lease",i.factor IS NOT NULL AS "Factor",
                    (SELECT count(*)::int FROM cp6_authentication_v1.consumed_totp c WHERE c.completion_id=i.completion_id) AS "Consumed",
                    (SELECT count(*)::int FROM cp6_authentication_v1.session_evidence s WHERE s.completion_id=i.completion_id) AS "Sessions"
                FROM cp6_authentication_v1.interactions i WHERE i.challenge_hash={challengeHash}
                """).SingleAsync();
            return new(account.FailedLoginCount, account.LastFailedLoginAt, account.LockedUntil, account.LastLoginTime,
                account.CredentialRevision, account.AuthenticationEpoch, projection.Version, projection.PayloadSha256, outbox, sessions, sessionProofs, interaction);
        }

        Task<IamCrmUserSealV1?> Pending() => IamCrmUserSealV1.ReadAsync(h.Db, h.Tenant, Guid.Empty, CancellationToken.None, subject);

        async Task<int> Floor()
        {
            await using var cmd = new NpgsqlCommand("SELECT coalesce((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.identity_fences WHERE tenant_id=@tenant AND aggregate_id=@source),0)", crm);
            cmd.Parameters.AddWithValue("tenant", h.Tenant);
            cmd.Parameters.AddWithValue("source", source);
            return (int)(await cmd.ExecuteScalarAsync())!;
        }

        async Task SetSealLogin(bool enabled)
        {
            using var quote = new NpgsqlCommandBuilder();
            var role = quote.QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(enabled ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }

        int CountDeniedConnections()
        {
            using var stream = new FileStream(LockoutSetting("CRM_SERVER_LOG"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var count = 0;
            while (reader.ReadLine() is { } line)
                if (line.Contains($"role \"{seal.Username}\" is not permitted to log in", StringComparison.Ordinal)) count++;
            return count;
        }

        Task Save(string name, object data) => File.WriteAllTextAsync(
            Path.Combine(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!, name), JsonSerializer.Serialize(data));
    }

    private static async Task AssertError(HttpResponseMessage response, string error, HttpStatusCode status = HttpStatusCode.BadRequest)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith("cp6_at=", StringComparison.Ordinal) && value.Length > "cp6_at=;".Length && value[7] != ';'));
        Assert.True((await response.Content.ReadAsStringAsync()).Contains(error, StringComparison.Ordinal), $"Expected the {error} authentication response.");
    }

    private static async Task<int> WaitForLockoutAdmission(NpgsqlConnection observer, Task<HttpResponseMessage> admission)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var cmd = new NpgsqlCommand("""
            SELECT pid FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%' AND cardinality(pg_blocking_pids(pid))>0
            """, observer);
        while (true)
        {
            if (await cmd.ExecuteScalarAsync(timeout.Token) is int pid) return pid;
            Assert.False(admission.IsCompleted, "The legal login should be waiting on the Main authentication root.");
            await Task.Delay(25, timeout.Token);
        }
    }

    private static string LockoutSetting(string name)
        => Environment.GetEnvironmentVariable("CP6_W672_" + name) ?? throw new InvalidOperationException($"The owned lockout fixture requires {name}.");

    private static (string State, Guid? Lease, bool Factor, int Consumed, int Sessions) LeaseFacts(LockoutInteraction snapshot)
        => (snapshot.State, snapshot.Lease, snapshot.Factor, snapshot.Consumed, snapshot.Sessions);

    private sealed record LockoutMainFacts(int Count, DateTime? LastFailure, DateTime? LockedUntil, DateTime? LastLogin,
        Guid Credential, Guid Epoch, int Version, string SourceHash, int Outbox, int Sessions, int SessionProofs, LockoutInteraction? Interaction);

    private sealed record LockoutInteraction(Guid Completion, string State, Guid? Lease, bool Factor, int Consumed, int Sessions);
}

public sealed partial class AuthenticationSsoPostgreSqlTests
{
    [Pgt017LockoutCandidateFact]
    public async Task PGT017_verified_Sso_primary_resolves_the_same_accepted_factor_lockout()
    {
        await using var external = await Harness.Create(server, 1, true, false, true);
        var subject = external.User.Id;
        var tenantId = external.Organization.Id;
        var tenant = new TenantContext { CurrentTenantId = tenantId };
        var configured = IdentityRuntime(tenantId);
        var storeId = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!);
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        await IamUserAssignmentsPostgreSqlTests.RegisterLockoutStoreFixture(crm, seal, tenantId, storeId);
        await using var fence = new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>
        {
            [tenantId] = new() { StoreId = storeId, ConnectionString = seal.ConnectionString }
        });
        var identity = new CrmIdentityRuntime(configured.Options, configured.Validator, inquiryFence: fence);
        await using var db = server.Context(tenant, identity);
        var account = await db.Sys_Users.SingleAsync(row => row.Id == subject);
        account.FailedLoginCount = 4;
        account.LastFailedLoginAt = DateTime.Now.AddMinutes(-1);
        await db.SaveChangesAsync();

        var options = Options.Create(new SecurityOptions { Password = new PasswordPolicyOptions { ExpiryDays = 1 } });
        var policy = new AuthenticationPolicyServiceV1(db, options);
        var gate = new AuthenticationCompletionGateV1(db, policy);
        var interactions = new AuthenticationInteractionStoreV1(db);
        var audit = new SecurityAuditService(db);
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var factors = new TwoFactorService(db, new TotpService(options), new LogEmailSender(NullLogger<LogEmailSender>.Instance),
            cache, audit, options);
        var owner = new LoginSecurityService(db, options, new EphemeralDataProtectionProvider());
        var entry = new AuthenticationEntryServiceV1(db, tenant, gate, interactions, Mock.Of<IAuthSessionService>(),
            new RefreshTokenService(db, options, tenant), factors, owner, Mock.Of<IClientDeviceService>());
        var start = await Start(external);
        var proof = await start.Callback();
        Assert.Equal((subject, AuthenticationPrimaryMethod.Sso), (proof.UserId, proof.PrimaryMethod));
        var challenged = await entry.BeginAsync(proof, start.Key, null, null, null, null, CancellationToken.None);
        Assert.Equal("2fa_verify", challenged.Purpose);

        await SetSealLogin(false);
        IamCrmUserSealV1 accepted;
        try
        {
            var refusal = await Assert.ThrowsAsync<RefreshCompletionUnknownException>(() => entry.CompleteFactorAsync(start.Key,
                null, false, "totp", "invalid", null, null, null, CancellationToken.None));
            Assert.True(refusal.InnerException?.InnerException is PostgresException { SqlState: "28000" });
            accepted = Assert.IsType<IamCrmUserSealV1>(await IamCrmUserSealV1.ReadAsync(db, tenantId, Guid.Empty, CancellationToken.None, subject));
            Assert.Equal(proof.CompletionRef, accepted.CompletionId);
            Assert.False(accepted.SealStarted);
            Assert.Equal(4, (await db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject)).FailedLoginCount);
        }
        finally { await SetSealLogin(true); }

        var next = await Start(external);
        var currentProof = await next.Callback();
        Assert.Equal((subject, AuthenticationPrimaryMethod.Sso), (currentProof.UserId, currentProof.PrimaryMethod));
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() => entry.BeginAsync(currentProof, next.Key,
            null, null, null, null, CancellationToken.None));
        Assert.Equal("AUTHENTICATION_DENIED", denied.Message);
        var after = await db.Sys_Users.AsNoTracking().SingleAsync(row => row.Id == subject);
        Assert.Equal(5, after.FailedLoginCount);
        Assert.True(after.LockedUntil > DateTime.Now);
        Assert.Null(await IamCrmUserSealV1.ReadAsync(db, tenantId, accepted.OperationId, CancellationToken.None));
        var lease = await db.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM cp6_authentication_v1.interactions
            WHERE completion_id={proof.CompletionRef} AND lease IS NULL AND factor IS NULL
            """).SingleAsync();
        Assert.Equal("pending", lease);
        Assert.False(await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_authentication_v1.interactions WHERE completion_id={currentProof.CompletionRef}) AS "Value"
            """).SingleAsync());
        Assert.False(await db.Sys_RefreshTokens.AnyAsync(row => row.TenantId == tenantId && row.UserId == subject));
        Assert.Equal(1, await db.Sys_SecurityLogs.CountAsync(row => row.TenantId == tenantId && row.UserId == subject
            && row.EventType == (int)SecurityEventType.TwoFactorFailed));
        var aggregate = $"user:{subject:D}";
        var snapshot = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(row => row.TenantId == tenantId && row.AggregateId == aggregate);
        var expected = Assert.Single(accepted.ReadAdvances(), row => row.Aggregate == aggregate);
        Assert.Equal((expected.Version, expected.IntentHash), (snapshot.Version, snapshot.PayloadSha256));
        await using var floorQuery = new NpgsqlCommand("SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.identity_fences WHERE tenant_id=@tenant AND aggregate_id=@source", crm);
        floorQuery.Parameters.AddWithValue("tenant", tenantId);
        floorQuery.Parameters.AddWithValue("source", aggregate);
        var floor = Assert.IsType<int>(await floorQuery.ExecuteScalarAsync());
        Assert.Equal(expected.Version, floor);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("CP6_PGT017_BRIDGE")!, "sso-recovered.json"),
            JsonSerializer.Serialize(new { accepted.OperationId, Original = proof.CompletionRef, Next = currentProof.CompletionRef, Floor = floor,
                after.LockedUntil, after.FailedLoginCount, Snapshot = snapshot.PayloadSha256, SsoRejections = 1 }));

        async Task SetSealLogin(bool enabled)
        {
            using var quote = new NpgsqlCommandBuilder();
            var role = quote.QuoteIdentifier(seal.Username!);
            await using var sql = new NpgsqlCommand($"ALTER ROLE {role} {(enabled ? "LOGIN" : "NOLOGIN")}", crm);
            await sql.ExecuteNonQueryAsync();
        }
    }
}

internal sealed class Pgt017LockoutCandidateFactAttribute : FactAttribute
{
    public Pgt017LockoutCandidateFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_W672_CANDIDATE_EVIDENCE") != "1")
            Skip = "The lockout fixture uses only the installed candidate in the owned PostgreSQL environment.";
    }
}
