using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamUserAssignmentsPostgreSqlTests
{
    private const string ScopeCompetitor = "w658-scope-competitor";

    private static async Task SeedCompetingScopeEditor(Harness h)
    {
        await GrantPolicyHttpPermissions(h);
        await CopyRole71ForIndependentInquiry(h);
        h.Db.Sys_Users.Add(new Sys_User
        {
            UserName = ScopeCompetitor, Enable = true, DeptId = h.Parent, RoleId = 72,
            Password = (await h.Db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == h.ActorId)).Password,
            AuthenticationEpoch = Guid.NewGuid()
        });
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
    }

    private static async Task CopyRole71ForIndependentInquiry(Harness h)
    {
        var grants = await h.Db.Sys_RoleActions.AsNoTracking().Where(x => x.RoleId == 71).ToListAsync();
        h.Db.Sys_RoleActions.AddRange(grants.Select(x => new Sys_RoleAction
            { RoleId = 72, MenuId = x.MenuId, ActionCode = x.ActionCode }));
        var scopes = await h.Db.Sys_RoleDataScopes.AsNoTracking().Where(x => x.RoleId == 71).ToListAsync();
        h.Db.Sys_RoleDataScopes.AddRange(scopes.Select(x => new Sys_RoleDataScope
            { RoleId = 72, ResourceKey = x.ResourceKey, ScopeType = x.ScopeType, CustomDeptIds = x.CustomDeptIds }));
        var fields = await h.Db.Sys_RoleFieldPerms.AsNoTracking().Where(x => x.RoleId == 71).ToListAsync();
        h.Db.Sys_RoleFieldPerms.AddRange(fields.Select(x => new Sys_RoleFieldPerm
            { RoleId = 72, ResourceKey = x.ResourceKey, FieldName = x.FieldName, Access = x.Access }));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        var pairs = await IamInquiryScopeAdministrationV1.ReadSourceAsync(h.Db, h.Tenant, 71, default);
        var scope = InquiryScopes(h);
        await scope.Service.ChangeAsync(72, await InquiryCommand(h, scope.Service, 72, pairs), h.Actor, default);
    }

    private static async Task ExercisePendingCrmInquiryScope(AuthenticationApiProcess api,
        Func<Task<AuthenticationApiProcess>> restart, Harness h, string bridge, InquiryPermissionRequestV1 target,
        string originalToken, Func<AuthenticationApiProcess, Dictionary<string,string>, Task<(string Access, string Identity)>> exchange)
    {
        await WaitConfigurationBridge(bridge, "ready");
        const string scopePath = "api/iam/inquiry-scopes/71";
        var actor = await Login(api, h);
        var competing = await Login(api, h, userName: ScopeCompetitor);
        var (unrelatedToken, _) = await exchange(api, competing);
        using var editor = await api.Send(Request(HttpMethod.Get, scopePath, actor));
        Assert.Equal(HttpStatusCode.OK, editor.StatusCode);
        var snapshot = (await editor.Content.ReadFromJsonAsync<IamInquiryScopeEditorV1>())!;
        var retained = Assert.Single(snapshot.SiteForms, pair => pair != new InquirySiteFormScopeV1(target.SiteId, target.FormId));
        var command = new IamInquiryScopeCommandV1
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = snapshot.Version, ExpectedReferencesVersion = snapshot.ReferencesVersion,
            Reason = "Withdraw one synthetic inquiry site/form", SiteForms = [retained]
        };
        var canceled = command with { OperationId = Guid.NewGuid() };
        var anchor = await C02Version();
        var seal = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CP6_PGT017_SEAL_CONNECTION"));
        await using var crm = new NpgsqlConnection(Environment.GetEnvironmentVariable("CP6_PGT017_CRM_OBSERVER"));
        await crm.OpenAsync();
        Assert.Equal("127.0.0.1", seal.Host);
        Assert.Equal((crm.Database, crm.Port, "w658_main_seal"), (seal.Database, seal.Port, seal.Username));
        var floor = await Floor();
        await SetLogin(false);
        try
        {
            await using var denied = new NpgsqlConnection(seal.ConnectionString);
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => denied.OpenAsync());
            Assert.Equal(PostgresErrorCodes.InvalidAuthorizationSpecification, refusal.SqlState);

            await CheckOtherRoleRoutes();

            using var canceledAttempt = await Write(api, actor, canceled);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, canceledAttempt.StatusCode);
            using var closed = await api.Send(Request(HttpMethod.Post, Operation(canceled) + "/close", actor));
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.Equal("CLOSED", (await closed.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
            using var closedReplay = await Write(api, actor, canceled);
            Assert.Equal(HttpStatusCode.Conflict, closedReplay.StatusCode);
            await RequireFence(api, originalToken, target, expected: true);

            using var failure = await Write(api, actor, command);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
            Assert.Contains("AUTHORITY_FENCE_UNAVAILABLE", await failure.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            var intent = await Intent();
            Assert.Equal((h.ActorId, anchor, false), intent);
            Assert.Equal(anchor, await C02Version());
            Assert.Equal(floor, await Floor());
            await RequireFence(api, originalToken, target, expected: false);

            var pid = api.ProcessId;
            await api.StopAsync();
            await using var next = await restart();
            Assert.NotEqual(pid, next.ProcessId);
            using var pending = await next.Send(Request(HttpMethod.Get, Operation(command), actor));
            Assert.Equal("UNKNOWN", (await pending.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
            using var wrongActor = await Write(next, competing, command);
            Assert.Equal(HttpStatusCode.Forbidden, wrongActor.StatusCode);
            using var conflicting = await Write(next, actor, command with { OperationId = Guid.NewGuid() });
            Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
            using var changedInput = await Write(next, actor, command with { Reason = "different original request" });
            Assert.Equal(HttpStatusCode.Conflict, changedInput.StatusCode);
            Assert.Equal(intent, await Intent());

            await RequireFence(next, originalToken, target, expected: false);
            await RequireFence(next, originalToken, target with { SiteId = retained.SiteId, FormId = retained.FormId }, expected: true);
            await RequireFence(next, unrelatedToken, target, expected: true);
            await File.WriteAllTextAsync(Path.Combine(bridge, "check-pending"), "PASS");
            await WaitConfigurationBridge(bridge, "pending-verified");

            await SetLogin(true);
            using var same = await Write(next, actor, command);
            Assert.True(same.StatusCode == HttpStatusCode.OK, await same.Content.ReadAsStringAsync());
            var receipt = (await same.Content.ReadFromJsonAsync<IamOperationV1>())!;
            Assert.Equal((command.OperationId, "COMMITTED"), (receipt.OperationId, receipt.State));
            using var replay = await Write(next, actor, command);
            Assert.Equal(receipt, await replay.Content.ReadFromJsonAsync<IamOperationV1>());
            Assert.Equal(anchor + 1, await C02Version());
            Assert.Equal(anchor + 1, await Floor());
            Assert.Equal(retained, Assert.Single(await IamInquiryScopeAdministrationV1.ReadSourceAsync(h.Db, h.Tenant, 71, default)));
            Assert.Null(await Intent());
            await File.WriteAllTextAsync(Path.Combine(bridge, "paused"), "PASS");
            await WaitConfigurationBridge(bridge, "finished");
        }
        finally { await SetLogin(true); }

        static string Operation(IamInquiryScopeCommandV1 value) => $"api/iam/inquiry-scopes/operations/{value.OperationId:D}";

        static Task<HttpResponseMessage> Write(AuthenticationApiProcess host, Dictionary<string,string> cookies,
            IamInquiryScopeCommandV1 value) => host.Send(Request(HttpMethod.Put, scopePath, cookies, value));

        static async Task RequireFence(AuthenticationApiProcess host, string bearer, InquiryPermissionRequestV1 permission, bool expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "connect/inquiry-permission") { Content = JsonContent.Create(permission) };
            request.Headers.Add("Authorization", "Bearer " + bearer);
            request.Headers.Add("X-Forwarded-Proto", "https");
            using var signed = await host.Send(request);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
            Assert.Equal(expected, signed.Headers.Contains("X-CP6-Inquiry-Tenant-Fence"));
        }

        async Task<(Guid Actor, int Version, bool Started)?> Intent()
        {
            var pending = await IamCrmRoleSealV1.ReadAsync(h.Db, h.Tenant, command.OperationId, default);
            return pending is null ? null : (pending.ActorId, pending.SourceVersion, pending.SealStarted);
        }

        Task<int> C02Version() => h.Db.CrmIdentitySnapshots.AsNoTracking()
            .Where(x => x.TenantId == h.Tenant && x.AggregateId == "permission:role:71").Select(x => x.Version).SingleAsync();

        async Task<int> Floor()
        {
            await using var query = new NpgsqlCommand("""
                SELECT COALESCE((SELECT minimum_version FROM crm_inquiry_tenant_pause_v1.role_fences
                    WHERE tenant_id=@tenant AND aggregate_id='permission:role:71'), 0)
                """, crm);
            query.Parameters.AddWithValue("tenant", h.Tenant);
            return (int)(await query.ExecuteScalarAsync())!;
        }

        async Task SetLogin(bool enabled)
        {
            await using var alter = new NpgsqlCommand(enabled ? "ALTER ROLE w658_main_seal LOGIN" : "ALTER ROLE w658_main_seal NOLOGIN", crm);
            await alter.ExecuteNonQueryAsync();
        }

        async Task CheckOtherRoleRoutes()
        {
            using var roleRead = await api.Send(Request(HttpMethod.Get, "api/iam/roles/editor", actor));
            Assert.Equal(HttpStatusCode.OK, roleRead.StatusCode);
            var roles = (await roleRead.Content.ReadFromJsonAsync<IamRoleEditorV1>())!;
            var current = Assert.Single(roles.Roles, x => x.RoleId == 71);
            Assert.True(current.Enable);
            await CheckColdWrite("api/iam/roles/71", "api/iam/roles", "role-update", new IamRoleCommandV1
            {
                OperationId = Guid.NewGuid(), ExpectedVersion = current.Version, ExpectedReferencesVersion = roles.ReferencesVersion,
                Reason = "Simulated role disable", RoleId = 71, RoleName = current.RoleName, Description = current.Description,
                Enable = false, OrderNo = current.OrderNo
            });

            var data = await ReadPolicy("scopes");
            var targetScope = Assert.Single(data.DataScopes, x => x.ResourceKey == "crm-lead");
            Assert.Equal(3, targetScope.ScopeType);
            await CheckColdWrite("api/iam/role-policies/scopes/71", "api/iam/role-policies", "role-policy-scopes",
                Command(data) with { Scopes = data.Catalog.Scopes.Select(x => new RoleDataScopeDto { ResourceKey = x.Key, ScopeType = x.Default }).ToList() });

            var fields = await ReadPolicy("fields");
            var targetField = Assert.Single(fields.Catalog.Fields, x => x.Key == InquiryIntakePermissionSourceV1.Resource);
            await CheckColdWrite("api/iam/role-policies/fields/71", "api/iam/role-policies", "role-policy-fields",
                Command(fields) with { ResourceKey = targetField.Key, Fields = targetField.Fields.Select(x => new RoleFieldPermDto
                    { ResourceKey = targetField.Key, FieldName = x.Name, Access = 3 }).ToList() });

            async Task<IamRolePolicyEditorV1> ReadPolicy(string mode)
            {
                using var reply = await api.Send(Request(HttpMethod.Get, $"api/iam/role-policies/{mode}/editor?roleId=71", actor));
                Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
                return (await reply.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;
            }

            static IamRolePolicyCommandV1 Command(IamRolePolicyEditorV1 source) => new()
            {
                OperationId = Guid.NewGuid(), ExpectedVersion = source.Version, ExpectedReferencesVersion = source.ReferencesVersion,
                ExpectedCatalogVersion = source.Catalog.Version, Reason = "Simulated CRM role policy reduction"
            };

            async Task CheckColdWrite(string route, string apiRoot, string kind, IamCommandV1 original)
            {
                using var failed = await api.Send(Request(HttpMethod.Put, route, actor, original));
                Assert.True(failed.StatusCode == HttpStatusCode.ServiceUnavailable, await failed.Content.ReadAsStringAsync());
                var pending = await IamCrmRoleSealV1.ReadAsync(h.Db, h.Tenant, original.OperationId, default);
                Assert.NotNull(pending);
                Assert.Equal((kind, 71, anchor, false), (pending.Kind, pending.RoleId, pending.SourceVersion, pending.SealStarted));
                await RequireFence(api, originalToken, target, expected: false);
                Assert.Equal(anchor, await C02Version());
                Assert.Equal(floor, await Floor());
                using var close = await api.Send(Request(HttpMethod.Post, $"{apiRoot}/operations/{original.OperationId:D}/close?kind={kind}", actor));
                Assert.Equal(HttpStatusCode.OK, close.StatusCode);
                Assert.Equal("CLOSED", (await close.Content.ReadFromJsonAsync<IamOperationV1>())!.State);
                await RequireFence(api, originalToken, target, expected: true);
            }
        }
    }
}
