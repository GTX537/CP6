using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string PolicyEditorPath = "api/iam/role-policies/actions/editor?roleId=72";
    private const string PolicyWritePath = "api/iam/role-policies/actions/72";
    private const string PriceWritePath = "api/orders/price-correction/commands/v1";

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_current_IAM_revocation_and_new_price_effect_serialize_both_commit_orders(bool revokeFirst)
    {
        await using var h = await Seed();
        await GrantQuery(h);
        await SeedFenceAdministrator(h);
        var tenant = h.Tenant;
        var db = h.Db;
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var writer = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, "correct");
        var command = Command(h, price: 123456.12345678m);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();

        var before = await ReadPriceWrites();
        var beforeAuthority = await ReadPolicyWrites(h, revoke.OperationId);
        Task<HttpResponseMessage> write;
        Task<HttpResponseMessage> change;
        FenceWait firstWait;
        FenceWait rootWait;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            if (revokeFirst)
            {
                await using var lockActions = new NpgsqlCommand(
                    """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE""", barrier, hold);
                await lockActions.ExecuteNonQueryAsync();
                change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, change);
                write = host.Send(OperationsRequest(writer, HttpMethod.Put, PriceWritePath, command));
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, write);
            }
            else
            {
                await using var lockRow = new NpgsqlCommand(
                    """SELECT 1 FROM public."T_Order" WHERE "TenantId"=@tenant FOR UPDATE""", barrier, hold);
                lockRow.Parameters.AddWithValue("tenant", tenant);
                Assert.Equal(1, await lockRow.ExecuteScalarAsync());
                write = host.Send(OperationsRequest(writer, HttpMethod.Put, PriceWritePath, command));
                firstWait = await WaitForFence(observer, barrier.ProcessID, "%FROM%T_Order%FOR UPDATE%", null, write);
                change = host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
                rootWait = await WaitForFence(observer, firstWait.Backend, "%cp6_authentication_v1.acquire()%", null, change);
            }
            Assert.Equal(before, await ReadPriceWrites());
            Assert.Equal(beforeAuthority, await ReadPolicyWrites(h, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await change;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var revocation = (await changed.Content.ReadFromJsonAsync<IamOperationV1>())!;
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        using var business = await write;
        var after = await ReadPriceWrites();
        Assert.Equal(revoke.OperationId, revocation.OperationId);
        Assert.Equal("COMMITTED", revocation.State);
        Assert.Equal(revocation.Version, authority.RoleRevision);
        Assert.NotEqual(beforeAuthority.RoleRevision, authority.RoleRevision);
        Assert.Equal(beforeAuthority.Snapshot + 1, authority.Snapshot);
        Assert.Equal(beforeAuthority.Generation + 1, authority.Generation);
        Assert.Equal(beforeAuthority.Outbox + 1, authority.Outbox);
        Assert.Equal(1, authority.CurrentOutbox);
        Assert.Equal(1, authority.Audits);
        Assert.Equal(1, authority.Receipts);
        Assert.DoesNotContain("correct", authority.Actions);
        Assert.Contains("query", authority.Actions);

        if (revokeFirst)
        {
            await AssertActionRevoked(business);
            Assert.Equal(before, after);
        }
        else
        {
            Assert.True(business.StatusCode == HttpStatusCode.OK, await OperationsFailure(business));
            using var saved = JsonDocument.Parse(after);
            var rows = saved.RootElement;
            var detail = rows.GetProperty("details")[0];
            Assert.Equal(tenant, detail.GetProperty("TenantId").GetGuid());
            Assert.Equal(123456.12345678m, detail.GetProperty("IndividualUnitPrice").GetDecimal());
            var intent = Assert.Single(rows.GetProperty("intents").EnumerateArray());
            Assert.Equal(command.OperationId, intent.GetProperty("batch_id").GetGuid());
            Assert.Equal(h.User, intent.GetProperty("actor_id").GetGuid());
            var receipt = Assert.Single(rows.GetProperty("receipts").EnumerateArray());
            Assert.Equal(command.OperationId, receipt.GetProperty("operation_id").GetGuid());
            Assert.Equal("COMMITTED", receipt.GetProperty("state").GetString());
            var audit = receipt.GetProperty("audit_json");
            Assert.Equal(h.User, audit.GetProperty("SubjectId").GetGuid());
            Assert.Equal(intent.GetProperty("source_version").GetString(), audit.GetProperty("SourcePermissionVersion").GetString());
            Assert.Single(audit.GetProperty("changes").EnumerateArray());

            using var original = await host.Send(OperationsRequest(writer, HttpMethod.Get,
                $"api/orders/price-correction/operations/v1/{command.OperationId}"));
            Assert.True(original.StatusCode == HttpStatusCode.OK, await OperationsFailure(original));
            using var replay = await host.Send(OperationsRequest(writer, HttpMethod.Put, PriceWritePath, command));
            Assert.True(replay.StatusCode == HttpStatusCode.OK, await OperationsFailure(replay));
            using var originalResult = JsonDocument.Parse(await business.Content.ReadAsStringAsync());
            using var replayedResult = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(originalResult.RootElement.GetProperty("data").GetRawText(),
                replayedResult.RootElement.GetProperty("data").GetRawText());
        }

        using var stale = await host.Send(OperationsRequest(writer, HttpMethod.Put, PriceWritePath, Command(h)));
        await AssertActionRevoked(stale);
        Assert.Equal(after, await ReadPriceWrites());
        Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));

        output.WriteLine(JsonSerializer.Serialize(new
        {
            Case = "PGT-017", RevokeFirst = revokeFirst, FirstWait = firstWait, RootWait = rootWait,
            BusinessStatus = (int)business.StatusCode, StaleStatus = (int)stale.StatusCode,
            command.OperationId, RevocationOperationId = revoke.OperationId, Before = beforeAuthority, After = authority
        }));

        Task<string> ReadPriceWrites() => db.Database.SqlQuery<string>($"""
            SELECT jsonb_build_object(
              'details', (SELECT jsonb_agg(to_jsonb(d) ORDER BY d."WebOrderDetailNo")
                          FROM public."T_OrderDetail" d WHERE d."TenantId"={tenant}),
              'intents', COALESCE((SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id)
                          FROM cp6_order_price_v1.workflow_intents i WHERE i.tenant_id={tenant}), '[]'::jsonb),
              'receipts', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY r.operation_id)
                          FROM cp6_main_owner_v1.operations r WHERE r.tenant_id={tenant}), '[]'::jsonb)
            )::text AS "Value"
            """).SingleAsync();
    }

    private static async Task SeedFenceAdministrator(Harness h)
    {
        var db = h.Db;
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(m => m.MenuKey == "pub-role-perm");
        if (menu == null)
        {
            menu = new() { MenuId = 32722, MenuKey = "pub-role-perm", MenuName = "role policy" };
            db.Sys_Menus.Add(menu);
        }

        db.Sys_Roles.Add(new() { RoleId = 71, RoleName = "fence administrator" });
        db.Sys_Users.Add(new()
        {
            UserName = "fence-manager", RoleId = 71,
            Password = new BCryptPasswordHasher(4).Hash("Fence1!Password")
        });
        foreach (var action in new[] { "query", "edit" })
            db.Sys_RoleActions.Add(new() { RoleId = 71, MenuId = menu.MenuId, ActionCode = action });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static async Task<IamRolePolicyCommandV1> RevokeActions(AuthenticationApiProcess host,
        Dictionary<string, string> manager, params string[] actions)
    {
        using var response = await host.Send(OperationsRequest(manager, HttpMethod.Get, PolicyEditorPath));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        var editor = (await response.Content.ReadFromJsonAsync<IamRolePolicyEditorV1>())!;
        var remaining = editor.Function.Actions.Where(a => !actions.Contains(a.ActionCode)).ToList();
        return new()
        {
            OperationId = Guid.NewGuid(), ExpectedVersion = editor.Version, ExpectedReferencesVersion = editor.ReferencesVersion,
            ExpectedCatalogVersion = editor.Catalog.Version, Reason = "PGT-017 simulated action revocation",
            MenuIds = editor.Function.MenuIds.Concat(remaining.Select(a => a.MenuId)).Distinct().ToList(),
            Actions = remaining
        };
    }

    private static async Task AssertActionRevoked(HttpResponseMessage response)
    {
        var body = await OperationsFailure(response);
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{response.StatusCode}: {body}");
        Assert.Contains("ACTION_FORBIDDEN", body);
    }

    private sealed record PolicyWrites(string? RoleRevision, long Snapshot, long Generation, long Outbox,
        long CurrentOutbox, long Audits, long Receipts, string Actions);

    private static async Task<PolicyWrites> ReadPolicyWrites(Harness h, Guid operation)
    {
        const string aggregate = "permission:role:72";
        var json = await h.Db.Database.SqlQuery<string>($"""
            WITH message AS (
                SELECT "AggregateVersion" FROM crm_identity."Cp6_OutboxMessage"
                    WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}
                UNION ALL
                SELECT "AggregateVersion" FROM crm_identity_priority."Cp6_OutboxMessage"
                    WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}
            ), snapshot AS (
                SELECT "Version" FROM crm_identity."Snapshot" WHERE "TenantId"={h.Tenant} AND "AggregateId"={aggregate}
            )
            SELECT jsonb_build_object(
                'RoleRevision', (SELECT revision FROM cp6_authentication_v1.identity_admission_revisions
                                  WHERE tenant_id={h.Tenant} AND object_key='role:72'),
                'Snapshot', COALESCE((SELECT "Version" FROM snapshot), 0),
                'Generation', COALESCE((SELECT "Generation" FROM public."CrmIdentityTenantGenerations" WHERE "TenantId"={h.Tenant}), 0),
                'Outbox', (SELECT count(*) FROM message),
                'CurrentOutbox', (SELECT count(*) FROM message WHERE "AggregateVersion"=(SELECT "Version" FROM snapshot)),
                'Audits', (SELECT count(*) FROM public."Sys_SecurityLogs" WHERE "TenantId"={h.Tenant}
                          AND "EventType"={(int)SecurityEventType.IamAdministrationChanged}
                          AND "ContextJson"::jsonb->>'operationId'={operation.ToString("D")}),
                'Receipts', (SELECT count(*) FROM cp6_authentication_v1.iam_operations WHERE tenant_id={h.Tenant} AND operation_id={operation}),
                'Actions', COALESCE((SELECT string_agg("ActionCode", ',' ORDER BY "MenuId","ActionCode")
                            FROM public."Sys_RoleAction" WHERE "TenantId"={h.Tenant} AND "RoleId"=72), '')
            )::text AS "Value"
            """).SingleAsync();
        return JsonSerializer.Deserialize<PolicyWrites>(json)!;
    }
}
