# Task B-T3 Report: WfTriggerWorker（BackgroundService）+ DI

**STATUS: DONE**
**Commit: 6b5ef08** — `feat(wfs-trigger): B-T3 WfTriggerWorker 逐租户扫描(照 TenantScopeRunner 现状口径)+DI`
**Branch: feat/wfs-event-trigger** (已 push)

## 交付内容
5th of 14, closes wave H-B (timer).

- **Create** `CP6.WebApi/BackgroundServices/WfTriggerWorker.cs` — 照 `WfServiceJobScanWorker.cs` 逐字克隆骨架（Interval while 循环 + `TenantScopeRunner.ForEachTenantAsync` 逐租户 scope），差异按 brief：
  - 无 `_workerId`（抢占靠 `Wf_FlowTrigger.RowVersion` 乐观并发 + `NextDueUtc` 前移 + 占坑唯一键，无 lease）
  - `Interval = 30s`（cron 最小粒度 1min）
  - 每租户 `sp.GetRequiredService<IFlowTriggerService>().ScanTimersOnceAsync(ct)`
  - 日志文案「Wf 触发器扫描 Worker 启动/停止」「Wf 触发器扫描处理租户 {Tenant} {Count} 条」「Wf 触发器扫描异常」
- **Modify** `CP6.WebApi/Program.cs` — 语义定位在 `WfServiceJobScanWorker` 注册（第157行）紧邻下一行追加 `AddHostedService<WfTriggerWorker>()`（brief 行号已 stale，按语义就近放置）。

## 前置校验
- `IFlowTriggerService` 已在 Program.cs:128 注册为 Scoped（A-T1/A-T2 落地），worker 可解析。
- `ScanTimersOnceAsync(CancellationToken)` 接口签名确认存在于 `FlowTriggerService.cs:114`，委托 `DateTime.UtcNow` 重载；B-T2 已 complete。

## Gates
1. **Tests** — `dotnet test CP6.slnx`：**Passed 1923 / Skipped 5 / Total 1928**（== baseline；wiring 任务 brief 未指定新测试，符合预期）。
2. **Migrations** — `dotnet ef migrations has-pending-model-changes`：No changes（clean，零迁移）。
3. **git show --stat HEAD** — 仅 brief 两文件（WfTriggerWorker.cs +44, Program.cs +1），无 add -A 泄漏。
4. Build — `dotnet build CP6.WebApi`：succeeded, 0 errors（唯一 warning 为既有 InboundService.cs 无关项）。

## Concerns
- 无。Engine zero-diff、零迁移、surgical add 全部满足。
- brief Program.cs 锚点行号确有 stale，已按语义就近 `WfServiceJobScanWorker` 注册下方放置。
