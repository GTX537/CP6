# Task E-T1: FlowSchemaValidator serviceTask 规则 + DesignerService.save 注册校验

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；spec 章节 §6.1/§6.2）

> **E-WF-016/017/018 + P2-3(非 end 须成功出边)落点。**

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs`
- Modify: DesignerService(Glob 确认)— save 时注册名校验
- Test: `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Wf/ServiceTaskValidatorTests.cs
[Fact] public void WebApi_MissingConnector_E_WF_016() { /* serviceTask webApi 无 connector -> 抛/返回含 E-WF-016 */ }
[Fact] public void DataWriteback_MissingAction_E_WF_016() { }
[Fact] public void Timer_MissingDelay_E_WF_016() { }
[Fact] public void ServiceTask_NonEnd_NoSuccessEdge_E_WF_016() { /* 仅 IsError 出边或无出边 -> E-WF-016(P2-3) */ }
[Fact] public void MoreThanOneErrorEdge_E_WF_017() { /* 一节点 2 条 IsError 出边 -> E-WF-017 */ }
[Fact] public void ErrorEdge_FromNonServiceTask_E_WF_017() { /* IsError 边出自 approval 节点 -> E-WF-017 */ }
[Fact] public void ValidServiceTask_Passes() { /* webApi 全配齐 + 1 成功边 + ≤1 错误边 -> 通过 */ }
```

- [ ] **Step 2: 跑验证 FAIL**(`--filter ServiceTaskValidatorTests`)。
- [ ] **Step 3: 实现**(spec §6.1)— `FlowSchemaValidator` 加 serviceTask 分支:
  - `ServiceKind` 非法 / dataWriteback 缺 ActionName / webApi 缺 Connector|Path / timer 缺 DelayMode|DelayValue → E-WF-016。
  - 非 end 的 serviceTask 无非错误出边 → E-WF-016(P2-3)。
  - `IsError` 出边 >1 / `IsError` 边来源节点非 serviceTask → E-WF-017。
  - 沿用既有抛错/收集风格(参既有 E-WF-010/011 写法)。
- [ ] **Step 4: 写 DesignerService.save 注册校验测试 + 实现**(spec §6.2)— save 时引用的 `ServiceActionName`(在 dataWriteback executor 目录)/`ServiceConnectorName`(在连接器目录)未注册 → E-WF-018。测试注入 fake 目录,引用不存在名 → 抛 E-WF-018。
- [ ] **Step 5: PASS + Wf 闸 + commit** — `git commit -m "feat(wfs-service-task): E-T1 FlowSchemaValidator+save 校验 E-WF-016/017/018"`

## 重要背景（架构审查 2026-07-05）
本任务是 D 波的**正确性依赖而非润色**：当前 FlowSchemaValidator 对 serviceTask 零规则；引擎 AdvanceToken 成功路径跳过 IsError 边，若 serviceTask 只配错误出边会"无后继→自动 Approved"误结流程——`ServiceTask_NonEnd_NoSuccessEdge_E_WF_016` 这条测试就是堵这个洞的，优先级最高。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- Wf 闸：`dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf` 既有测试字节等价全绿。
- 零 Space 污染。TDD 节奏。不重新设计。
