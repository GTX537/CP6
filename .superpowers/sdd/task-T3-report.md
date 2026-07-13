# Task T3 报告：启动期校验连接器调用时长 < 租约时长

**Status: DONE** · commit `251c99c` (branch `feat/wfs-cleanup-tickets`, pushed)

## 核实证据（动手前）
- `IWfConnector.cs`：契约仅有 `Name`/`DisplayName`/`CallAsync`，**不暴露超时** — 符合票面前提，需新增 `MaxCallDuration`。
- 租约时长源：`WfServiceJobService.cs:30` — `public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);`（reaper 复位逻辑用同一常量 `:85`）。
- `Program.cs:662` — `var app = builder.Build();`（其后紧跟 `IsDevelopment()` 块）。行号与 brief 预估 ~520 有漂移，用 Grep 精确定位后插入，未触碰 T1 改过的顶部 Local.json 源段。
- 启动校验**尚不存在**；`WfConnectorLeaseGuard.cs` 全新。
- `ServiceTaskResult.Ok(...)` 参数可选（`IServiceTaskExecutor.cs:39`），测试 `Ok()` 无参调用合法。
- DI 扩展 `GetServices<T>` 可用：`Program.cs:673` 已用 `GetRequiredService<T>`（隐式 using 覆盖），无需新增 using。

## TDD 红绿
- **红**：新增 `WfConnectorLeaseGuardTests.cs`（票面字节等价）→ `dotnet test --filter WfConnectorLeaseGuardTests` 编译失败 `CS0103: WfConnectorLeaseGuard 不存在`。
- **实现**：①`IWfConnector` 加默认接口成员 `TimeSpan? MaxCallDuration => null;`（DisplayName 后、CallAsync 前）；②新建 `WfConnectorLeaseGuard.Validate`（`MaxCallDuration is TimeSpan d && d >= lease` → 抛 `InvalidOperationException` 列出违规连接器名+时长）；③`Program.cs` build 后 CreateScope 调 guard 校验已注册 `IWfConnector`。
- **绿**：`--filter WfConnectorLeaseGuardTests` → 2 passed。

## 验证结果
- `dotnet build CP6.WebApi` → 0 Warning / 0 Error。
- `dotnet test --filter Wf` → **197 passed**。
- 全量 `dotnet test` → **1835 passed / 5 skipped**（基线 1833 + 本票 2 新测，零回归）。
- 零迁移（无实体/DbSet 改动）。`git status` 仅 4 文件，全在 Wf/Program.cs 允许范围，零跨模块污染。

## 疑虑
- 无。`GetServices<IWfConnector>()` 目前仅返回已注册的 EchoConnector（`MaxCallDuration=null` → 通过），guard 在真实 HTTP 连接器接入时才会实际拦截，符合设计意图（未来强约束）。
