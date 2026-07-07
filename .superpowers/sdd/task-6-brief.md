### Task 6: SignalR SpaceHub（发布推送 + events 页自动刷新）

**Files:**
- Create: `CP6.Core/Services/Integration/ISpaceNotifier.cs`——`Task NotifyLocationPublishedAsync(string batchNo, int count, string status)`（+ `NoOpSpaceNotifier` 内联，测试/降级用）
- Create: `CP6.WebApi/Hubs/SpaceHub.cs`（照 WmsHub：OnConnected/Disconnected 日志；无分组——Space 事件低频全播即可，YAGNI）
- Create: `CP6.WebApi/Services/SignalRSpaceNotifier.cs`（照 SignalRWmsNotifier：注入 `IHubContext<SpaceHub>`，`Clients.All.SendAsync("LocationPublished", new { batchNo, count, status })`，try/catch 吞错记日志——推送失败不影响业务）
- Modify: `CP6.Core/Services/Space/LocationPublishService.cs`——ctor 追加 `ISpaceNotifier notifier`（第 7 参）；`PublishFloorAsync` 成功 Commit 后、`DeactivateAsync` 兜底事件后各调一次 notify（**在事务 Commit 之后**，推送不进事务）；测试帮手 MakePublishSvc 加 `new NoOpSpaceNotifier()`
- Modify: `CP6.WebApi/Program.cs`——DI `AddScoped<ISpaceNotifier, SignalRSpaceNotifier>()` + `app.MapHub<CP6.WebApi.Hubs.SpaceHub>("/hubs/space");`（:2524 后）
- Create: `cp6.web/src/utils/spaceHub.ts`（照 wmsHub.ts 单例：withUrl('/hubs/space') 无 accessTokenFactory，cookie 隐式认证）
- Modify: `cp6.web/src/views/space/lifecycle/SpaceEventsView.vue`——onMounted 订阅 `LocationPublished` → `listRef.reload()`（回第 1 页可接受）；onUnmounted 取消订阅（照 IoT 轮询清理先例）
- Test: 后端 `Mock<IHubContext<SpaceHub>>` 链（照 CP6.Tests/DeadLetterNotifierTests.cs:49 范式）验证 SendCoreAsync 参数；LocationPublishServiceTests 加 1 断言（publish 后 notifier 被调——用记录桩）；前端 spec：mock spaceHub 模块，事件回调触发 reload

- [ ] Step 1: TDD → 实现 → 后端全量 + 前端三件套 → Commit `feat(space): SpaceHub 发布推送 + 事件页自动刷新（ISpaceNotifier 接口注入）`

---

