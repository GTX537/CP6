# Task T1 报告：Local.json 配置优先级修正

**Status:** DONE
**Commit:** `0f2a1a5`（已 push 到 `feat/wfs-cleanup-tickets`）
**改动文件:** `CP6.WebApi/Program.cs`（1 file, +20 / -2）——仅此一处，零跨模块，零迁移。

---

## 缺陷核实（现状）

`Program.cs:21` 原代码：
```csharp
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
```
`WebApplication.CreateBuilder(args)` 已把 env vars 源加在配置链尾（高优先级）。`AddJsonFile` 把 Local.json **追加到更后**，故 Local.json 反而覆盖环境变量——与原注释「env vars 最后覆盖 Local」相反。生产/容器里 `ConnectionStrings__DefaultConnection` 环境变量被 Local.json 静默吞。缺陷成立。

## 修法

把 Local.json 构造为 `JsonConfigurationSource`，手写循环定位 `EnvironmentVariablesConfigurationSource` 下标，`Sources.Insert(envVarIdx, localJsonSource)` **插到 env vars 源之前**（env 仍最高）；找不到时兜底 `Add`（理论不达）。同步改注释为「优先级（低→高）：appsettings.json → {Env}.json → Local.json → env vars → 命令行」。

## 验证脚本输出（最小 ConfigurationBuilder 复刻源顺序；脚本不入库）

脚本用临时控制台项目复刻真实源顺序，两处断言：

```
BaseDir = ...\scratchpad\cfgverify\bin\Debug\net10.0\
Cwd     = ...\scratchpad\cfgverify
WRONG order winner = FROM_LOCAL     ← 复刻当前(错误)顺序：env 先、Local 追加在后 → Local 胜出，证明 env 被吞（缺陷成立）
FIXED order winner = FROM_ENV       ← 修法顺序：Local 源插到 env vars 之前 → env 胜出（环境变量恢复最高优先级）
```

注：首轮脚本因 JSON 文件写到 cwd、而 file provider 根在 `AppContext.BaseDirectory`（bin 输出目录）导致 Local.json 未被加载（两行都打 FROM_ENV，假阴性）。修正为 `SetBasePath(AppContext.BaseDirectory)` + 文件写到同一目录后，源顺序对比即真实生效，输出如上。

## 验证结果

- 编译闸：`dotnet build CP6.WebApi/CP6.WebApi.csproj` → 0 Error（1 处既有 InboundService 空引用 warning，与本票无关）。
- 全量测试：`dotnet test CP6.Tests/CP6.Tests.csproj` → **1833 passed / 5 skipped / 0 failed**（1m2s），与基线一致，零回归。
- `git diff --stat` 复核：仅 `CP6.WebApi/Program.cs`，无 Space/其他模块污染。

## 部署流程副作用（不归本票，仅注明）

修好后，历次容器部署被迫的「publish 后手工 `rm appsettings.Local.json` 绕过此缺陷」手工步骤**可退役**——env vars 现已正确压过 Local.json。部署文档更新不在本票范畴。
