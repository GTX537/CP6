# 09 · 与 CP6 集成：IntegrationEvent / BridgeHook / SQL Server JSON

> OA 不是孤岛。它的价值在于"审批通过后，自动驱动 ERP/MES/WMS 的真实业务"。本章讲怎么把 OA 引擎接进 CP6 已有的骨架，**最大化复用，最小化新造**。

## 📍 学习目标

1. 审批通过后回写业务，为什么要走"集成事件"而不是同步直接写库？
2. CP6 的 `IntegrationEvent` + 重试 + 死信，怎么变成 OA 的可靠后盾？
3. `IErpBridgeHook` / `IMesBridgeHook` / `IWmsBridgeHook` 怎么当 OA 的"执行末端"？
4. 已有的 `IPowerEggWorkflowService` 桩怎么升级成"外部 OA 连接器"？
5. OA 和 CP6 的认证、菜单、SignalR 怎么对接？

---

## 🔎 核心集成点：`OnApproved` → 集成事件 → BridgeHook

第02章 `AdvanceAsync` 走到 end 时调了 `OnApproved`。**这就是 OA 和业务系统的接口**。关键设计：审批完成 ≠ 立刻同步写业务，而是**发一个集成事件**，由 CP6 已有的事件机制可靠地驱动落地：

```csharp
async Task OnApproved(FlowInstance inst)
{
    // 不直接调业务！发一个集成事件，复用 Phase 6 的持久化+重试+死信
    await _events.PublishAsync(new IntegrationEvent {
        SourceModule = "OA",
        Type         = $"OaApproved.{inst.FlowKey}",  // 如 OaApproved.stock_adjust
        Payload      = JsonSerializer.Serialize(new { inst.BizId, inst.FlowKey }),
    });
    // 立即返回，单子标记 approved。业务落地由事件 dispatcher 异步、可重试地完成。
}
```

然后在 CP6 的 `IntegrationEventDispatcher` 路由表里，把 OA 事件接到对应的 BridgeHook：

```
OaApproved.stock_adjust  → IWmsBridgeHook.OnStockAdjustApproved(bizId)
OaApproved.price_correct → IErpBridgeHook / 现有单价订正回写
OaApproved.plan_change   → IMesBridgeHook.OnPlanChangeApproved(bizId)
```

**OA 引擎完全不需要知道 WMS/MES 怎么扣库存、改计划**——它只管"审批完了，发个事件"。落地交给各模块的 Hook。这就是第06章 Bridge Hook 模式的二次复用。

---

## 🔎 为什么必须走事件，不能同步写？

| 同步直接写库 | 走集成事件（推荐） |
|---|---|
| 审批完→立刻调 WMS 扣库存 | 审批完→发事件→异步落地 |
| WMS 临时挂了 → 审批也失败/回滚 | WMS 挂了 → 事件进重试，稍后自动补 |
| OA 强耦合 WMS 实现 | OA 只认事件，模块解耦 |
| 失败了人工查日志补 | 失败进死信，健康看板可见、可人工补偿 |

CP6 的 Phase 6 已经把"持久化 + 指数退避重试 + 死信告警 + 健康看板"全做好了（见 `docs/learning/06-bridge-hook-pattern.md`）。**OA 直接搭这趟车，可靠性是白捡的。** 这是"在 CP6 里做 OA"相比"独立做 OA"的最大红利。

---

## 🔎 反向：业务系统也能触发 OA

集成是双向的。业务里某些动作要"先审批再执行"，就反过来调 OA 发起流程：

```csharp
// WMS 库存调整：不直接改库存，先发起 OA 审批
public async Task RequestStockAdjust(StockAdjustDto dto, string user)
{
    var bizId = await SaveFormData("stock_adjust", dto);     // 存表单数据
    await _flowEngine.StartAsync("stock_adjust_flow", bizId, user); // 发起审批
    // 真正改库存的逻辑，放到 OnApproved 后的 BridgeHook 里
}
```

各业务模块只依赖一个 `IApprovalService.Submit(formKey, flowKey, data)`，**底层走 OA 自带引擎还是外部 OA，由配置决定**。

---

## 🔎 升级 PowerEgg 桩为外部 OA 连接器

CP6 已有 `IPowerEggWorkflowService`（NoOp 桩，对接日系 OA 起票）。抽象成通用连接器，让"用自带引擎"和"对接客户已有 OA"统一接口：

```csharp
public interface IExternalOaConnector
{
    Task<string> SubmitAsync(string flowKey, string bizId, object payload, string applicant);
    Task<ApprovalResult> QueryStatusAsync(string externalId);
}
// 实现：PowerEggConnector / DingTalkConnector / WeComConnector / WeaverConnector ...
// 自带引擎则用 InternalEngineConnector 包一层 FlowEngine
```

客户没 OA → 用自带引擎；客户已有钉钉/企微/泛微 → 配对应连接器。**这正是商业化多客户复制的关键开关**，也呼应第10章多租户。

---

## 🔎 和 CP6 现有基础设施对接清单

| 能力 | CP6 现成的 | OA 怎么用 |
|---|---|---|
| 登录/JWT | `AuthController` + JWT | OA 接口直接受同一套认证保护 |
| 用户/角色 | `Sys_User` / `Sys_Role` | 审批人 role 解析、登录人识别（第04章） |
| 菜单/权限 | `Sys_Menu` / `Sys_RoleMenu` | OA 的待办中心/设计器作为菜单项接入 |
| 实时推送 | SignalR Hub | 新待办角标、审批结果通知 |
| 审计日志 | `OperLogFilter` | 审批操作自动留痕 |
| 数据存储 | SQL Server JSON | 表单数据 JSON 列（第08章） |
| 事件/重试/死信 | `IntegrationEvent`（Phase 6） | 审批回写业务的可靠通道 |

**新造的只有：组织模型、表单引擎、流程引擎、设计器。其余全是复用。** 这就是为什么"在 CP6 里长出 OA"比"从零做 OA"省一大半。

---

## 💡 资深视角

**OA 应该是 CP6 的"模块"还是"独立服务"？**
学习/一期阶段做成 CP6 内的模块（同库、同进程）最快，复用一切。商业化做大后，OA（尤其设计器、流程引擎）有独立部署、多系统共用的价值，可演进为独立服务、通过 API/事件和 CP6 交互。**架构上用 `IApprovalService` 接口隔离，模块化起步、服务化预留**——和第00章对 Space 3D 的思路一致。

**审批的事务边界**
"存表单 + 发起流程"要在一个事务里（要么都成要么都不成），但"审批通过 + 回写业务"必须跨事务（异步事件）。想清楚哪里是事务、哪里是最终一致，是 OA 集成最易错的地方。

---

## ⚠️ 踩坑记录

1. **审批完同步写业务**：耦合 + 不可靠。务必走 IntegrationEvent，享受重试/死信。
2. **事件 payload 塞整张表单**：payload 只放 `bizId + flowKey`，落地时按 bizId 读 FormData，避免事件体臃肿和数据不一致。
3. **没做幂等**：事件可能重试多次，`OnStockAdjustApproved` 要幂等（同一 bizId 只扣一次库存）。复用 CP6 既有幂等约定。
4. **OA 直接读写各模块的表**：破坏模块边界。OA 只发事件，落地由各模块自己的 Service/Hook 做。
5. **外部 OA 状态不回流**：对接钉钉后，钉钉审批结果没回调更新 CP6 实例状态，两边脱节。连接器要处理回调/轮询。

---

## 🧪 自检题

1. 审批通过后回写业务，为什么走集成事件而不是同步调用？CP6 哪个机制做后盾？
2. OA 事件怎么路由到 WMS/MES/ERP 的 BridgeHook？OA 需要知道它们的实现吗？
3. 事件 payload 为什么只放 bizId 而不是整张表单？
4. "存表单+发起流程"和"审批通过+回写业务"的事务边界有何不同？
5. `IExternalOaConnector` 抽象解决了什么商业化问题？

---

## 🔗 延伸阅读 / 动手清单

**动手清单：**
- [ ] `OnApproved` 发 IntegrationEvent，不直接写业务
- [ ] 在 IntegrationEventDispatcher 注册 `OaApproved.*` → 对应 BridgeHook
- [ ] 选 1 个场景（如 WMS 库存调整）跑通：发起→审批→事件→Hook 扣库存，含幂等
- [ ] 定义 `IApprovalService`，各模块只依赖它
- [ ] 把 `IPowerEggWorkflowService` 重构为 `IExternalOaConnector` 的一个实现

**下一章** → [10. 多租户与商业化](./10-multi-tenant.md)，把它从"能用"做成"能卖给不同客户"。
