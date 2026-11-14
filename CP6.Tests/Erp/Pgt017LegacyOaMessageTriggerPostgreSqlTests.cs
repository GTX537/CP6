using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task PGT017_legacy_OA_message_trigger_orders_rotation_and_pause(bool pause, bool authorityFirst)
    {
        const string originalKey = "simulated-manual-fixture";
        const string idem = "simulated-message-1";
        await using var h = await Seed();
        await using var administrator = pause ? await TenantManagementSeed() : null;
        var trigger = await SeedManualTrigger(h);
        var db = h.Db;
        var tenant = h.Tenant;
        var config = """{"varsSchema":["orderNo"]}""";
        await db.Wf_FlowTriggers.Where(t => t.Id == trigger).ExecuteUpdateAsync(t => t.SetProperty(x => x.ConfigJson, config));
        var connection = db.Database.GetConnectionString()!;
        await using var host = await AuthenticationApiProcess.Start(connection,
            IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(tenant));
        var manager = await Login(host, administrator ?? h);
        TenantManagementCommandV1? pauseCommand = null;
        if (pause)
        {
            using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(tenant)));
            var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(current);
            pauseCommand = new("message-trigger-pause", snapshot.Revision, "suspend", null);
        }
        var before = await ManualEffects(h, trigger);
        var authorityBefore = pause ? await ReadPauseAuthority(h, administrator!.User) : null;

        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await barrier.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> change;
        Task<HttpResponseMessage> fire;
        await using (var hold = await barrier.BeginTransactionAsync())
        {
            var blocker = authorityFirst
                ? pause ? """LOCK TABLE public."Sys_Tenants" IN SHARE MODE"""
                        : """LOCK TABLE public."Wf_FlowTrigger" IN SHARE MODE"""
                : """LOCK TABLE public."Wf_TriggerFire" IN SHARE MODE""";
            await using var block = new NpgsqlCommand(blocker, barrier, hold);
            await block.ExecuteNonQueryAsync();
            if (authorityFirst)
            {
                change = Change();
                var authority = await WaitForFence(observer, barrier.ProcessID,
                    pause ? "%UPDATE%Sys_Tenants%" : "%UPDATE%Wf_FlowTrigger%", null, SafeStatus(change));
                fire = Send(originalKey, idem);
                var waiting = await WaitForFence(observer, authority.Backend, "%cp6_authentication_v1.acquire()%", null, fire);
                output.WriteLine($"Authority backend {authority.Backend}; message backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            else
            {
                fire = Send(originalKey, idem);
                var message = await WaitForFence(observer, barrier.ProcessID, "%INSERT%Wf_TriggerFire%", null, fire);
                change = Change();
                var waiting = await WaitForFence(observer, message.Backend, "%cp6_authentication_v1.acquire()%", null, SafeStatus(change));
                output.WriteLine($"Message backend {message.Backend}; authority backend {waiting.Backend}; {waiting.WaitEvent}");
            }
            Assert.Equal(before, await ManualEffects(h, trigger));
            if (pause)
                Assert.Equal(authorityBefore, await ReadPauseAuthority(h, administrator!.User));
            await hold.RollbackAsync();
        }
        using var changed = await change;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        string? newKey = null;
        if (pause)
        {
            var receipt = await NumberHttpData<TenantManagementReceiptV1>(changed);
            CheckPause(authorityBefore!, await ReadPauseAuthority(h, administrator!.User), receipt, pauseCommand!);
        }
        else
        {
            var key = await changed.Content.ReadFromJsonAsync<JsonElement>();
            newKey = key.GetProperty("data").GetProperty("apiKeyPlain").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(newKey));
            Assert.NotEqual(originalKey, newKey);
        }

        using var response = await fire;
        var rejected = pause ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
        Assert.True(response.StatusCode == (authorityFirst ? rejected : HttpStatusCode.Created), await OperationsFailure(response));
        var committed = await ManualEffects(h, trigger);
        Guid? originalInstance = null;
        if (authorityFirst)
            Assert.Equal(before, committed);
        else
        {
            originalInstance = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
            Assert.Equal(new ManualTriggerEffects(1, 1, 1, 1, 1, 1, 0, committed.Watermark), committed);
            Assert.NotNull(committed.Watermark);
        }
        using var retryOld = await Send(originalKey, idem);
        Assert.Equal(rejected, retryOld.StatusCode);
        Assert.Equal(committed, await ManualEffects(h, trigger));
        if (pause)
            return;

        using var currentKey = await Send(newKey!, idem);
        Assert.Equal(authorityFirst ? HttpStatusCode.Created : HttpStatusCode.OK, currentKey.StatusCode);
        var result = (await currentKey.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("instanceId").GetGuid();
        if (originalInstance is not null)
            Assert.Equal(originalInstance, result);
        var vars = await db.Wf_FlowInstances.Where(x => x.Id == result).Select(x => x.VarsJson).SingleAsync();
        using var variables = JsonDocument.Parse(vars);
        Assert.Equal("SIM-1", variables.RootElement.GetProperty("orderNo").GetString());
        Assert.False(variables.RootElement.TryGetProperty("discardMe", out _));
        var row = await db.Wf_TriggerFires.AsNoTracking().SingleAsync(x => x.TriggerId == trigger);
        Assert.Equal((idem, result), (row.IdempotencyKey, row.InstanceId!.Value));

        Task<HttpResponseMessage> Change() => pause
            ? host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(tenant, "/commands"), pauseCommand))
            : host.Send(OperationsRequest(manager, HttpMethod.Post, $"api/oa/flow-triggers/{trigger}/reset-key"));
        Task<HttpResponseMessage> Send(string key, string requestId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"api/oa/flow-triggers/{trigger}/fire")
            {
                Content = JsonContent.Create(new { orderNo = "SIM-1", discardMe = 1 })
            };
            request.Headers.Add("X-Api-Key", key);
            request.Headers.Add("Idempotency-Key", requestId);
            return host.Send(request);
        }
        static async Task<HttpStatusCode> SafeStatus(Task<HttpResponseMessage> request) => (await request).StatusCode;
    }
}
