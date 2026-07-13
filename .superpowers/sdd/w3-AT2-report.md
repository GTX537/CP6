# Task A-T2 报告：WfTriggerConfig 分型解析 + IFlowTriggerService.FireAsync（幂等闸+占坑复用+运行时双检+SubmitAsync 接缝）+ DI

**状态：DONE**
**Commit：** 见文末（feat/wfs-event-trigger）

## 做了什么
严格 TDD 落地本波最关键的正确性任务：
- `CP6.Core/Services/Wf/WfTriggerConfig.cs`（新建）：三型 config POCO（WfTimerTriggerConfig{Cron,VarsJson} / WfEventTriggerConfig{VarsMap} / WfMessageTriggerConfig{VarsSchema}）+ `WfTriggerConfig` 静态分型解析器；坏 JSON/空串 → 空配置不抛（PropertyNameCaseInsensitive）。
- `CP6.Core/Services/Wf/FlowTriggerService.cs`（新建）：`TriggerFireResult`（Ok/Fail 工厂 + Replayed 幂等标记）+ `IFlowTriggerService`（FireAsync + ScanTimersOnceAsync）+ 实现类。FireAsync 五段：① Enabled 检查（先于幂等闸）② 幂等闸（Local+库双查、占坑落库、并发撞唯一索引让位既有行）③ 运行时双检 E-WF-022/023 ④ 显式事务包 SubmitAsync+流水回填+LastFiredUtc 水位（映射⑥原子接缝）⑤ 异常→ChangeTracker.Clear+重查回填 E-WF-024。ScanTimersOnceAsync(CancellationToken) 委托 nowUtc 重载，后者先抛 NotImplementedException（B-T2 实现）。
- `CP6.WebApi/Program.cs`（改）：`AddScoped<IFlowTriggerService, FlowTriggerService>()`，紧随 IFlowEngine 注册块（实际 line 127 之后；brief 标的 :107-108 为陈旧行号，落点语义一致=FlowEngine 注册同块）。
- `CP6.Tests/Wf/FlowTriggerTestHarness.cs`（新建，本波共享基座）+ `FlowTriggerConfigTests.cs` + `FlowTriggerFireTests.cs`（brief 逐字转写）。

引擎零改动（未触碰 FlowEngine*/NodeHandlers/FlowSchemaValidator）。零新迁移。

## RED 证据（实现前）
`dotnet test --filter "FlowTriggerConfigTests|FlowTriggerFireTests"` → 编译失败：
- `error CS0246: The type or namespace name 'FlowTriggerService' could not be found`（harness 引用未建类型；WfTriggerConfig 同缺）。

## GREEN 证据（实现后）
- 定向：`--filter "FlowTriggerConfigTests|FlowTriggerFireTests"` → `Passed! Failed: 0, Passed: 13, Skipped: 0`（4 config + 9 fire）。
- Wf 闸：`--filter Wf` → `Passed! Failed: 0, Passed: 258, Skipped: 0`。
- 全量：`dotnet test CP6.slnx` → `Passed! Failed: 0, Passed: 1906, Skipped: 5, Total: 1911`（= 基线 1893 + 新 13）。

## 迁移验证
`dotnet ef migrations has-pending-model-changes` → `No changes have been made to the model since the last migration.`（clean，零模型漂移）。

## 自审
- 撞键语义（共享契约末条）逐条覆盖：InstanceId!=null→Ok(replayed:true)（Fire_SameKey）；InstanceId==null 占坑→补跑回填（Fire_ResumesUnfinishedSlot）；失败行同 key 重发→清 Error 回填（Fire_RetriesFailedSlot）。
- Enabled 先于幂等闸（Fire_Disabled 断言零流水行）符合 spec §3.1 顺序。
- E-WF-022/023 用 FailFireAsync 保留失败流水供排障；E-WF-024 在事务回滚后经 ChangeTracker.Clear 重查回填（占坑行第一段事务外先落库→回滚后仍存）。
- PayloadHash：非 timer 走 SHA-256 hex（64 长度），timer 置 null（spec §2.2），Fire_PayloadHash 双断言。
- 第二段用库内跟踪 trackedTrigger 回写水位（防上游 ChangeTracker.Clear 令入参 trigger 失联）。
- 测试基座照 FlowConcurrencyTests 逐字口径，额外给 Wf_FlowTrigger 建 AFTER UPDATE rowversion 触发器（B-T2 双 worker 抢占复用）。SQLite :memory: 共享连接支持显式事务，第二段原子提交在基座真实生效。

## 偏差
- DI 落点行号：brief 写 `:107-108`，仓库实际 FlowEngine 注册在 line 126-127（brief 行号陈旧）。已按语义要求置于 FlowEngine/IFlowEngine 注册同块之后，无功能差异。
