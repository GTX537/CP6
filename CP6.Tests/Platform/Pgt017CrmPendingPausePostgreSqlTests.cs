using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Tests.Erp;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    [Pgt017MainFact]
    public Task PGT017_versioned_pause_retains_original_when_first_CRM_connection_fails()
        => InquiryProducerHttp("configuration:write", crossDatabasePause: true, pendingPause: "versioned");

    [Pgt017MainFact]
    public Task PGT017_legacy_pause_retains_original_when_first_CRM_connection_fails()
        => InquiryProducerHttp("configuration:write", crossDatabasePause: true, pendingPause: "legacy");

    private async Task<PendingPauseServiceProbe> ConfigurePendingPauseService(Harness target, InquiryPermissionSigningOptionsV1 options,
        Dictionary<string, string> environment)
    {
        var config = ServiceRegistration(target, Guid.NewGuid());
        config.Oidc.Issuer = options.Issuer;
        config.Source.IdentityPublicationEnabled = true;
        config.Source.IdentityPublicationRevision = 1;
        var service = config.Source.Registrations.Single();
        service.AuthenticationEpoch = Guid.NewGuid();
        var runtime = ContextRuntime(target.Tenant, options.Issuer);
        var registry = new InquiryServicePrincipalRegistryV1(config.Source, config.Oidc);
        var tenant = new TenantContext();
        await using (var publisher = server.Context(tenant, runtime))
            await new InquiryServiceIdentityPublisherV1(publisher, tenant, registry, runtime).PublishAsync(target.Tenant);
        environment["CrmOidc__ServiceClients__1__ClientId"] = ServiceClient;
        environment["CrmOidc__ServiceClients__1__SecretSha256"] = CrmOidcCrypto.Hash(ServiceSecret);
        environment["CrmOidc__ServiceClients__1__TenantId"] = target.Tenant.ToString("D");
        environment["CrmOidc__ServiceClients__1__Enabled"] = "true";
        environment["CrmOidc__ServiceClients__1__AllowedScopes__0"] = "cp6.services";
        environment["InquiryServicePrincipals__Enabled"] = "true";
        environment["InquiryServicePrincipals__Version"] = config.Source.Version;
        environment["InquiryServicePrincipals__Registrations__0__ClientId"] = ServiceClient;
        environment["InquiryServicePrincipals__Registrations__0__SubjectId"] = service.SubjectId.ToString("D");
        environment["InquiryServicePrincipals__Registrations__0__DepartmentId"] = target.Parent.ToString("D");
        environment["InquiryServicePrincipals__Registrations__0__RoleIds__0"] = "71";
        var binding = new CrmInquiryTenantPauseV1(new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>
        {
            [target.Tenant] = new()
            {
                StoreId = Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!),
                ConnectionString = Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION")!
            }
        });
        var db = server.Context(tenant, runtime);
        var key = InquiryPermissionSigningTests.Signer(options);
        return new(new(db, tenant, registry, key, () => IntakeCatalog(), runtime, binding), db, binding, key);
    }

    private sealed record PendingPauseServiceProbe(InquiryServicePermissionIssuerV1 Issuer, CP6Context Db,
        CrmInquiryTenantPauseV1 Binding, InquiryPermissionSigningKeyV1 Key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Binding.DisposeAsync();
            Key.Dispose();
        }
    }

    private async Task ExercisePendingCrmTenantPause(AuthenticationApiProcess host, Func<Task<AuthenticationApiProcess>> startHost,
        Harness target, string bridge, InquiryPermissionRequestV1 permission, string token, string mode,
        PendingPauseServiceProbe service, StockPostgresCommitRelay? sealAck, StockPostgresCommitRelay? mainAck)
    {
        await WaitConfigurationBridge(bridge, "ready");
        await using var admin = await Seed();
        var actor = await admin.Db.Sys_Users.SingleAsync(user => user.Id == admin.ActorId);
        actor.IsPlatformAdmin = true;
        await admin.Db.SaveChangesAsync();
        var manager = await Login(host, admin);
        await using var otherAdmin = await Seed();
        var otherActor = await otherAdmin.Db.Sys_Users.SingleAsync(user => user.Id == otherAdmin.ActorId);
        otherActor.IsPlatformAdmin = true;
        await otherAdmin.Db.SaveChangesAsync();
        var path = $"api/platform/tenant/{target.Tenant}";
        var management = path + "/management-v1";
        using var current = await host.Send(Request(HttpMethod.Get, management, manager));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var initial = (await current.Content.ReadFromJsonAsync<TenantManagementSnapshotV1>())!;
        var command = new TenantManagementCommandV1("pgt017-pending-pause", initial.Revision, "suspend", null);
        var legacy = mode == "legacy";
        var before = await Effects();
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        Assert.Equal("127.0.0.1", seal.Host);
        Assert.Equal(seal.Database, crm.Database);
        Assert.Equal(crm.Port, seal.Port);
        Assert.Equal("w649_main_seal", seal.Username);
        var closedBefore = await ClosedVersion();
        using var originalPermission = await Project(host, permission with { Purpose = "intake:release" });
        Assert.Equal(HttpStatusCode.OK, originalPermission.StatusCode);
        using var originalService = await ServiceProject(host);
        Assert.Equal(HttpStatusCode.OK, originalService.StatusCode);
        await RequireContextFence(host, expected: true);
        await SealLogin(false);
        try
        {
            await using var denied = new NpgsqlConnection(seal.ConnectionString);
            var loginFailure = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, loginFailure.SqlState);
            using var failed = await Suspend(host, manager);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", await Error(failed));
            Assert.Equal(closedBefore, await ClosedVersion());
            Assert.Equal(before, await Effects());
            await RequireNoNewFence(host);
            await RequirePendingRead(host, manager);
            var originalIntent = await Intent();
            using var pending = JsonDocument.Parse(originalIntent);
            Assert.Equal(admin.ActorId, pending.RootElement.GetProperty("actor_id").GetGuid());
            Assert.Equal(initial.Revision, pending.RootElement.GetProperty("expected_revision").GetInt64());
            Assert.Equal(Guid.Parse(Environment.GetEnvironmentVariable("CP6_PGT017_STORE_ID")!), pending.RootElement.GetProperty("store_id").GetGuid());
            Assert.Equal(closedBefore + 1, pending.RootElement.GetProperty("source_version").GetInt32());
            Assert.Equal(legacy, pending.RootElement.GetProperty("is_legacy").GetBoolean());

            var pid = host.ProcessId;
            await host.StopAsync();
            await using var restarted = await startHost();
            Assert.NotEqual(pid, restarted.ProcessId);
            var currentManager = await Login(restarted, admin);
            var otherManager = await Login(restarted, otherAdmin);
            await RequireNoNewFence(restarted);
            await RequirePendingRead(restarted, currentManager);
            Assert.Equal(originalIntent, await Intent());
            await File.WriteAllTextAsync(Path.Combine(bridge, "check-pending"), "PASS");
            await WaitConfigurationBridge(bridge, "pending-verified");

            using var other = await restarted.Send(Request(HttpMethod.Post, management + "/commands", currentManager,
                command with { OperationRef = "other-pause" }));
            Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
            using var wrongActor = await Suspend(restarted, otherManager);
            Assert.Equal(HttpStatusCode.Forbidden, wrongActor.StatusCode);
            using var reactivation = await restarted.Send(Request(HttpMethod.Post, path + "/reactivate", currentManager));
            Assert.Equal(HttpStatusCode.Conflict, reactivation.StatusCode);
            using var changed = await restarted.Send(Request(HttpMethod.Put, path, currentManager,
                new { Name = "conflicting revision", Expire = initial.ExpireDate }));
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            var native = await Assert.ThrowsAsync<PostgresException>(() => target.Db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE public."Sys_Tenants" SET "Enable"=false WHERE "Id"={target.Tenant}
                """));
            Assert.Equal("TENANT_SUSPENSION_PENDING", native.MessageText);
            Assert.Equal(before, await Effects());
            Assert.Equal(closedBefore, await ClosedVersion());
            Assert.Equal(originalIntent, await Intent());

            await SealLogin(true);
            if (sealAck is not null)
            {
                sealAck.Arm();
                using var unconfirmed = await Suspend(restarted, currentManager);
                await sealAck.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
                Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE", await Error(unconfirmed));
                Assert.Equal(closedBefore + 1, await ClosedVersion());
                Assert.Equal(before, await Effects());
                Assert.Equal(originalIntent, await Intent());
                await RequirePendingRead(restarted, currentManager);
            }

            string receipt;
            if (mainAck is not null)
            {
                await using var barrier = new NpgsqlConnection(target.Db.Database.GetConnectionString());
                await barrier.OpenAsync();
                await using var tx = await barrier.BeginTransactionAsync();
                await using var hold = new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@id FOR UPDATE", barrier, tx);
                hold.Parameters.AddWithValue("id", target.Tenant);
                Assert.Equal(1, await hold.ExecuteScalarAsync());
                await using var observer = new NpgsqlConnection(target.Db.Database.GetConnectionString());
                await observer.OpenAsync();
                var completion = Suspend(restarted, currentManager);
                try
                {
                    await WaitForMainBlock(observer, barrier.ProcessID, "%UPDATE%Sys_Tenants%", completion);
                    mainAck.Arm();
                }
                finally { await tx.RollbackAsync(); }
                using var unconfirmed = await completion;
                await mainAck.Dropped.WaitAsync(TimeSpan.FromSeconds(8));
                Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfirmed.StatusCode);
                Assert.Equal("TENANT_OUTCOME_UNKNOWN", await Error(unconfirmed));
                using var known = await restarted.Send(Request(HttpMethod.Post, management + "/commands/read", currentManager, new { command.OperationRef }));
                Assert.Equal(HttpStatusCode.OK, known.StatusCode);
                receipt = await known.Content.ReadAsStringAsync();
            }
            else
            {
                using var completed = await Suspend(restarted, currentManager);
                Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
                receipt = await completed.Content.ReadAsStringAsync();
            }
            var after = await Effects();
            Assert.NotEqual(before, after);
            Assert.Equal(closedBefore + 1, await ClosedVersion());
            Assert.Equal("", await Intent());
            using var final = await restarted.Send(Request(HttpMethod.Get, management, currentManager));
            Assert.Equal(HttpStatusCode.OK, final.StatusCode);
            var disabled = (await final.Content.ReadFromJsonAsync<TenantManagementSnapshotV1>())!;
            Assert.False(disabled.Enable);
            Assert.Equal(initial.Revision + 1, disabled.Revision);
            using var replay = await Suspend(restarted, currentManager);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(receipt, await replay.Content.ReadAsStringAsync());
            if (legacy)
            {
                using var alreadyDisabled = await Suspend(restarted, otherManager);
                Assert.Equal(HttpStatusCode.OK, alreadyDisabled.StatusCode);
                Assert.Equal(receipt, await alreadyDisabled.Content.ReadAsStringAsync());
            }
            Assert.Equal(after, await Effects());
            await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
            await WaitConfigurationBridge(bridge, "finished");
        }
        finally { await SealLogin(true); }

        Task<HttpResponseMessage> Suspend(AuthenticationApiProcess api, Dictionary<string, string> cookies) => legacy
            ? api.Send(Request(HttpMethod.Post, path + "/suspend", cookies))
            : api.Send(Request(HttpMethod.Post, management + "/commands", cookies, command));

        async Task RequireNoNewFence(AuthenticationApiProcess api)
        {
            using var write = await Project(api, permission);
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
            Assert.False(write.Headers.Contains("X-CP6-Inquiry-Tenant-Fence"));
            using var read = await Project(api, permission with { Purpose = "configuration:read" });
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var direct = await Project(api, permission with { Purpose = "intake:release" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, direct.StatusCode);
            using var intakeRead = await Project(api, permission with { Purpose = "intake:list" });
            Assert.Equal(HttpStatusCode.OK, intakeRead.StatusCode);
            using var serviceDirect = await ServiceProject(api);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, serviceDirect.StatusCode);
            await RequireContextFence(api, expected: false);
        }

        Task<HttpResponseMessage> Project(AuthenticationApiProcess api, InquiryPermissionRequestV1 request)
            => Projection(api, "connect/inquiry-permission", request, "Bearer " + token);

        Task<HttpResponseMessage> ServiceProject(AuthenticationApiProcess api)
            => Projection(api, "connect/inquiry-service-permission", permission with { Purpose = "intake:release" }, ServiceBasic().ToString());

        async Task RequireContextFence(AuthenticationApiProcess api, bool expected)
        {
            using var human = await Projection(api, "connect/inquiry-human-context", new InquiryHumanContextRequestV1("intake:release",
                permission.SiteId, permission.FormId, ServiceNonce()), "Bearer " + token);
            Assert.True(human.StatusCode == HttpStatusCode.OK, await human.Content.ReadAsStringAsync());
            Assert.Equal(expected, human.Headers.Contains("X-CP6-Inquiry-Tenant-Fence"));
            var serviceRequest = new InquiryServiceContextRequestV1("public:receive", permission.SiteId, permission.FormId, ServiceNonce());
            var issued = await service.Issuer.IssueContextWithFenceAsync(serviceRequest, ServiceBasic(), default);
            using var json = JsonDocument.Parse(issued.Json);
            Assert.Equal(serviceRequest.RequestNonce, json.RootElement.GetProperty("requestNonce").GetString());
            Assert.Equal(expected, issued.TenantFence is not null);
        }

        static async Task<HttpResponseMessage> Projection(AuthenticationApiProcess api, string route, object body, string authorization)
        {
            using var call = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
            call.Headers.Add("Authorization", authorization);
            call.Headers.Add("X-Forwarded-Proto", "https");
            return await api.Send(call);
        }

        async Task RequirePendingRead(AuthenticationApiProcess api, Dictionary<string, string> cookies)
        {
            if (legacy) return;
            using var outcome = await api.Send(Request(HttpMethod.Post, management + "/commands/read", cookies, new { command.OperationRef }));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, outcome.StatusCode);
            Assert.Equal("TENANT_OUTCOME_UNKNOWN", await Error(outcome));
        }

        static async Task<string?> Error(HttpResponseMessage response)
            => (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString();

        async Task<int> ClosedVersion()
        {
            await using var query = new NpgsqlCommand("SELECT closed_through FROM crm_inquiry_tenant_pause_v1.fences WHERE tenant_id=@tenant", crm);
            query.Parameters.AddWithValue("tenant", target.Tenant);
            return Assert.IsType<int>(await query.ExecuteScalarAsync());
        }

        async Task SealLogin(bool enabled)
        {
            await using var command = new NpgsqlCommand(enabled ? "ALTER ROLE w649_main_seal LOGIN" : "ALTER ROLE w649_main_seal NOLOGIN", crm);
            await command.ExecuteNonQueryAsync();
        }

        Task<string> Intent() => target.Db.Database.SqlQuery<string>($"""
            SELECT coalesce((SELECT row_to_json(p)::text FROM cp6_tenant_management_v1.pending_suspensions p WHERE p.tenant_id={target.Tenant}), '') AS "Value"
            """).SingleAsync();

        Task<string> Effects() => target.Db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_array(t."Enable",t."AuthenticationPolicyRevision",v.revision,
              (SELECT count(*) FROM cp6_tenant_management_v1.commands WHERE tenant_id=t."Id"),
              (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "RequestTenantCode"=t."TenantCode"),
              (SELECT max("Version") FROM crm_identity."Snapshot" WHERE "TenantId"=t."Id" AND "AggregateId"='tenant:'||t."Id"::text),
              (SELECT count(*) FROM crm_identity_priority."Cp6_OutboxMessage" WHERE "TenantId"=t."Id"),
              (SELECT count(*) FROM crm_identity."Cp6_OutboxMessage" WHERE "TenantId"=t."Id"))::text AS "Value"
            FROM public."Sys_Tenants" t JOIN cp6_tenant_management_v1.versions v ON v.tenant_id=t."Id"
            WHERE t."Id"={target.Tenant}
            """).SingleAsync();
    }
}
