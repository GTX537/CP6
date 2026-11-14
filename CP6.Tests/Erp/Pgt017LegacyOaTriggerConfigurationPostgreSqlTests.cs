using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("create", true)]
    [InlineData("create", false)]
    [InlineData("update", true)]
    [InlineData("update", false)]
    [InlineData("enable", true)]
    [InlineData("enable", false)]
    [InlineData("reset-key", true)]
    [InlineData("reset-key", false)]
    public async Task PGT017_legacy_OA_trigger_configuration_uses_current_authority(string operation, bool revokeFirst)
    {
        const string flow = "simulated-trigger-fence";
        const string action = "FlowTrigger.Edit";
        const string path = "api/oa/flow-triggers";
        const string oldKey = "simulated-original-key";
        const string initial = """{"varsSchema":["orderNo"]}""";
        const string updated = """{"varsSchema":["orderNo","amount"]}""";
        var trigger = Guid.NewGuid();
        await using var fixture = await Seed();
        await SeedFenceAdministrator(fixture);
        await GrantLegacyAuthorityAction(fixture, "oa-flow-admin", action);
        var db = fixture.Db;
        db.Wf_FlowDefs.Add(new Wf_FlowDef { FlowKey = flow, FlowName = "Simulated PGT-017 flow", Enable = true });
        db.Wf_FlowTriggers.Add(new Wf_FlowTrigger
        {
            Id = trigger, FlowKey = flow, TriggerType = WfTriggerType.Message, ConfigJson = initial,
            Enabled = false, StarterUserId = fixture.User, ApiKeyHash = WfApiKeyHelper.HashOf(oldKey)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var connection = db.Database.GetConnectionString()!;
        var tenant = fixture.Tenant;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var actor = await Login(host, fixture);
        var manager = await Login(host, "fence-manager", "Fence1!Password", tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, action);
        var config = new FlowTriggerSaveReq(flow, WfTriggerType.Message, updated, false, null, fixture.User);
        (HttpMethod Method, string Route, object? Body) request = operation switch
        {
            "create" => (HttpMethod.Post, path, config),
            "update" => (HttpMethod.Put, $"{path}/{trigger}", config),
            "enable" => (HttpMethod.Post, $"{path}/{trigger}/enable", new { Enabled = true }),
            "reset-key" => (HttpMethod.Post, $"{path}/{trigger}/reset-key", null),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        var before = await Effects();
        var existing = Assert.Single(before);
        var beforePolicy = await ReadPolicyWrites(fixture, revoke.OperationId);

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> revocation;
        Task<HttpResponseMessage> mutation;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = revokeFirst
                ? """LOCK TABLE public."Sys_RoleAction" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_FlowTrigger" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (revokeFirst)
            {
                revocation = Revoke();
                var iam = await WaitForFence(observer, barrier.ProcessID, "%DELETE%Sys_RoleAction%", null, revocation);
                mutation = Mutate();
                var waiting = await WaitForFence(observer, iam.Backend, "%cp6_authentication_v1.acquire()%",
                    null, StatusOnly(mutation));
                output.WriteLine($"Revocation backend {iam.Backend}; {operation} backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            else
            {
                mutation = Mutate();
                var business = await WaitForFence(observer, barrier.ProcessID, "%Wf_FlowTrigger%", null, StatusOnly(mutation));
                revocation = Revoke();
                var waiting = await WaitForFence(observer, business.Backend, "%cp6_authentication_v1.acquire()%", null, revocation);
                output.WriteLine($"{operation} backend {business.Backend}; revocation backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await Effects());
            Assert.Equal(beforePolicy, await ReadPolicyWrites(fixture, revoke.OperationId));
            await hold.RollbackAsync();
        }

        using var changed = await revocation;
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var afterPolicy = await ReadPolicyWrites(fixture, revoke.OperationId);
        Assert.NotEqual(beforePolicy.RoleRevision, afterPolicy.RoleRevision);
        Assert.Equal(beforePolicy.Generation + 1, afterPolicy.Generation);
        Assert.Equal((1L, 1L, 1L), (afterPolicy.CurrentOutbox, afterPolicy.Audits, afterPolicy.Receipts));

        using var result = await mutation;
        var committed = await Effects();
        Assert.Equal(revokeFirst ? HttpStatusCode.Forbidden : HttpStatusCode.OK, result.StatusCode);
        if (revokeFirst)
            Assert.Equal(before, committed);
        else if (operation is "create" or "reset-key")
        {
            var envelope = await result.Content.ReadFromJsonAsync<JsonElement>();
            var data = envelope.GetProperty("data");
            var key = data.GetProperty("apiKeyPlain").GetString();
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.NotEqual(oldKey, key);
            var hash = WfApiKeyHelper.HashOf(key!);
            if (operation == "create")
            {
                var added = data.GetProperty("id").GetGuid();
                Assert.NotEqual(trigger, added);
                Assert.Equal(2, committed.Length);
                Assert.Equal(existing, Assert.Single(committed, t => t.Id == trigger));
                Assert.Equal(new TriggerConfig(added, false, updated, hash), Assert.Single(committed, t => t.Id == added));
            }
            else
                Assert.Equal(existing with { KeyHash = hash }, Assert.Single(committed));
        }
        else
            Assert.Equal(operation == "enable" ? existing with { Enabled = true } : existing with { Config = updated },
                Assert.Single(committed));

        using var stale = await Mutate();
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Equal(committed, await Effects());

        Task<HttpResponseMessage> Revoke() => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke));
        Task<HttpResponseMessage> Mutate() => host.Send(OperationsRequest(actor, request.Method, request.Route, request.Body));
        static async Task<HttpStatusCode> StatusOnly(Task<HttpResponseMessage> request) => (await request).StatusCode;
        Task<TriggerConfig[]> Effects() => db.Wf_FlowTriggers.AsNoTracking().Where(t => t.FlowKey == flow && t.TenantId == tenant)
            .OrderBy(t => t.Id).Select(t => new TriggerConfig(t.Id, t.Enabled, t.ConfigJson, t.ApiKeyHash)).ToArrayAsync();
    }

    private sealed record TriggerConfig(Guid Id, bool Enabled, string Config, string? KeyHash);
}
