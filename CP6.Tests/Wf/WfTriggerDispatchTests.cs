// CP6.Tests/Wf/WfTriggerDispatchTests.cs
// dispatcher 用 NoOp 六 hook + 可断言的 FakeWfTriggerHook（记录收到的参数、可控返回）构造。
// NoOp 类名照 CP6.Core/Services/Integration 实际（六个家族各有 NoOp——侦察已核）。
using System;
using System.Text.Json;
using System.Threading.Tasks;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Integration;
using Moq;
using Xunit;

namespace CP6.Tests;

public class WfTriggerDispatchTests
{
    private sealed class FakeWfTriggerHook : IWfTriggerBridgeHook
    {
        public string? LastMethod, LastEventKey, LastEventId, LastPayload, LastUser;
        public bool NextSuccess = true;

        public Task<WfTriggerBridgeResult> OnEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
            => Record("OnEventAsync", eventKey, eventId, payloadJson, userName);
        public Task<WfTriggerBridgeResult> ReplayEventAsync(string eventKey, string eventId, string payloadJson, string? userName)
            => Record("ReplayEventAsync", eventKey, eventId, payloadJson, userName);

        private Task<WfTriggerBridgeResult> Record(string method, string k, string id, string p, string? u)
        {
            LastMethod = method; LastEventKey = k; LastEventId = id; LastPayload = p; LastUser = u;
            return Task.FromResult(NextSuccess ? WfTriggerBridgeResult.Ok(1, 1) : WfTriggerBridgeResult.Failed("boom"));
        }
    }

    private static (IntegrationEventDispatcher Dispatcher, FakeWfTriggerHook Fake) NewDispatcher()
    {
        var fake = new FakeWfTriggerHook();
        // Space 家族无 NoOp（其余五家族有）——沿用既有 dispatcher 测试的 Mock.Of<ISpaceBridgeHook>() 口径。
        var d = new IntegrationEventDispatcher(
            new NoOpMesBridgeHook(), new NoOpWmsBridgeHook(), new NoOpErpBridgeHook(),
            new NoOpOrderCancelBridgeHook(), new NoOpFinBridgeHook(), Mock.Of<ISpaceBridgeHook>(),
            fake);
        return (d, fake);
    }

    private static IntegrationEvent Evt(string source, string target, string hook, string payloadJson) => new()
    {
        Id = Guid.NewGuid(), SourceModule = source, TargetModule = target,
        HookName = hook, PayloadJson = payloadJson, Status = IntegrationEventStatus.Failed, Attempts = 1,
    };

    [Fact]
    public async Task Dispatch_TargetWF_OnEventAsync_RoutesToReplay_AnySource()
    {
        var (d, fake) = NewDispatcher();
        var payload = JsonSerializer.Serialize(
            new WfTriggerEventPayload("SPACE|OnLocationPublishedAsync", "EV-7", "{\"binNo\":\"B1\"}", "u"));

        var ok = await d.DispatchAsync(Evt("SPACE", "WF", "OnEventAsync", payload));

        Assert.True(ok);
        Assert.Equal("ReplayEventAsync", fake.LastMethod);          // 重放入口，不是 OnEventAsync（映射表⑦）
        Assert.Equal("SPACE|OnLocationPublishedAsync", fake.LastEventKey);
        Assert.Equal("EV-7", fake.LastEventId);                     // eventId 原样复用（spec §2.2）
        Assert.Equal("{\"binNo\":\"B1\"}", fake.LastPayload);
        Assert.Equal("u", fake.LastUser);
    }

    [Fact]
    public async Task Dispatch_TargetWF_OtherHookName_Still404()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("SPACE", "WF", "OnSomethingElse", "{}")));
        Assert.Contains("DISPATCH-404", ex.Message);                // fallback 只认 OnEventAsync
    }

    [Fact]
    public async Task Dispatch_ExistingRoutes_Unchanged_Unknown404()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("X", "Y", "Z", "{}")));
        Assert.Contains("DISPATCH-404", ex.Message);                // 既有语义不变
    }

    [Fact]
    public async Task Dispatch_TargetWF_BadPayload_Throws400()
    {
        var (d, _) = NewDispatcher();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.DispatchAsync(Evt("SPACE", "WF", "OnEventAsync", "null")));
        Assert.Contains("DISPATCH-400", ex.Message);                // 对齐 GetPayload 空负载语义
    }
}
