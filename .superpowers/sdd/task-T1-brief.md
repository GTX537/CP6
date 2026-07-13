## Task T1: Local.json 配置优先级修正（env vars 覆盖连接串被静默吞）

> **票1。** 缺陷：`appsettings.Local.json` 通过 `AddJsonFile` 追加到配置源链**末尾**——而 `WebApplication.CreateBuilder(args)` 已经把环境变量源加在前面。ASP.NET 后加的源优先级更高，故 **Local.json 覆盖了环境变量**，与注释宣称的「env vars 最后（覆盖 Local）」相反。结果：生产/容器里用 `ConnectionStrings__DefaultConnection` 环境变量覆盖连接串会被 Local.json 静默吞掉。修法=把 Local.json 源**插到环境变量源之前**，恢复标准 ASP.NET 优先级（env vars 最高），并同步改注释。

**Files:**
- Modify: `CP6.WebApi/Program.cs:16-20`（`AddJsonFile("appsettings.Local.json", ...)` 处）

**说明（为何不能只写测试）：** 配置源顺序在 `Program.cs` 顶层构建期生效，无法用 xUnit 对 `CP6.WebApi` 主机做单元断言（无 `WebApplicationFactory` 脚手架，且引入它会拖起全量 DI）。本 Task 用**可复现的手工验证脚本**替代自动化测试——构造一个最小 `ConfigurationBuilder` 复刻真实源顺序，断言 env 胜出。

- [ ] **Step 1: 写验证脚本（复刻源顺序，先证明当前顺序 env 落败）** — 在 scratchpad 建 `verify-config-order.csx`（或临时控制台），复刻「先 env、后 Local.json」的错误顺序，断言 Local.json 值胜出（即缺陷成立）：

```csharp
// 复刻当前（错误）顺序：CreateBuilder 已加 env vars，再 AddJsonFile(Local) 追加到末尾 → Local 胜出
using Microsoft.Extensions.Configuration;
System.Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "FROM_ENV");
System.IO.File.WriteAllText("appsettings.Local.json",
    "{\"ConnectionStrings\":{\"DefaultConnection\":\"FROM_LOCAL\"}}");
var wrong = new ConfigurationBuilder()
    .AddEnvironmentVariables()                               // CreateBuilder 顺序：env 先
    .AddJsonFile("appsettings.Local.json", optional: true)   // 当前代码：Local 追加在后 → 覆盖 env
    .Build();
System.Console.WriteLine($"WRONG order winner = {wrong.GetConnectionString("DefaultConnection")}");
// 预期输出 FROM_LOCAL —— 证明缺陷（env 被吞）
```

  跑 `dotnet script`（或 `dotnet run` 临时控制台），确认打印 `FROM_LOCAL`（缺陷成立）。

- [ ] **Step 2: 实现修法** — `Program.cs:16-20` 改为把 Local.json 源**插到环境变量源之前**（不要简单 `AddJsonFile`，那样只会追加到末尾）。当前代码：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 加载顺序：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
```

  替换为：

```csharp
var builder = WebApplication.CreateBuilder(args);

// 本地凭证覆盖（appsettings.Local.json 在 .gitignore，绝不入仓库）。
// 优先级（低→高）：appsettings.json → appsettings.{Env}.json → appsettings.Local.json → env vars → 命令行。
// 关键：CreateBuilder 已把 env vars/命令行源加在链尾（高优先级）。若用 AddJsonFile 追加，Local.json 会落到
// 更后、反而覆盖 env vars —— 容器里 ConnectionStrings__* 环境变量会被静默吞。故把 Local.json 源**插到 env vars 源之前**，
// 恢复标准 ASP.NET 优先级（env vars 最高）。
var localJsonSource = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
{
    Path = "appsettings.Local.json",
    Optional = true,
    ReloadOnChange = true,
};
localJsonSource.ResolveFileProvider();
// 注意：Sources 是 IList<IConfigurationSource>，没有 List<T>.FindIndex——手写循环找 env vars 源下标。
var envVarIdx = -1;
for (var i = 0; i < builder.Configuration.Sources.Count; i++)
    if (builder.Configuration.Sources[i] is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource)
    { envVarIdx = i; break; }
if (envVarIdx >= 0)
    builder.Configuration.Sources.Insert(envVarIdx, localJsonSource);   // 插到 env vars 之前 → env 仍最高
else
    builder.Configuration.Sources.Add(localJsonSource);                 // 兜底（理论不达）
```

- [ ] **Step 3: 验证修法** — 在 Step 1 的脚本追加「正确顺序」断言并跑，确认 env 胜出：

```csharp
var srcs = new ConfigurationBuilder();
srcs.Sources.Add(new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
    { Path = "appsettings.Local.json", Optional = true });
srcs.Sources.Add(new Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource());
var fixedCfg = srcs.Build();
System.Console.WriteLine($"FIXED order winner = {fixedCfg.GetConnectionString("DefaultConnection")}");
// 预期输出 FROM_ENV —— env vars 恢复最高优先级
```

- [ ] **Step 4: 编译闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "fix(wfs-service-task): T1 Local.json 配置源插到 env vars 之前，恢复环境变量最高优先级（修复容器连接串被静默吞）"
```

---

## Global Constraints（每个 Task 都遵守）

- **测试基线不回归：**
  - 后端：`dotnet test CP6.Tests/CP6.Tests.csproj` 全绿——基线 **1509 测试**（5 skip = SQLite 既知限制）。`--filter Wf` 既有 Wf 测试字节等价（除本计划显式改动的测试断言外）。
  - 前端：`npm run test`（vitest run）**320 全绿** + `npm run type-check` 通过。**type-check 须大堆**（vue-tsc 内存密集）：
    - Bash 工具：`NODE_OPTIONS=--max-old-space-size=8192 npm run type-check`
    - PowerShell：`$env:NODE_OPTIONS='--max-old-space-size=8192'; npm run type-check`
- **EF 迁移 clean：**`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 报无 pending（本计划**不新增迁移**——无实体/DbSet 改动）。
- **零跨模块污染：**只碰 `CP6.Core/Services/Wf/**`、`CP6.WebApi/{Program.cs,Middleware,Seed}`、`cp6.web/src/views/oa/designer/**`、`cp6.web/src/utils/signalr.ts`、对应 `CP6.Tests/Wf/**`。**绝不碰** `views/space/**`、`Services/*Space*`、任何 Space 迁移/DbSet。每 Task 完成 `git show --stat` 复核 diff。
- **零硬编码色：**前端一切颜色走 Design System token（`var(--cp-danger)` 等，见 `cp6.web/src/styles/tokens.css`），禁十六进制字面量。
- **i18n 五语齐全：**任何新增文案键必须五语齐全 `ZhCN/ZhTW/En/Ja/Ko`，加进 `I18nOaServiceTaskScreenSeed.cs`，运行期 SeedLangs 幂等去重。
- **TDD 节奏：**先写失败测试→跑验证 FAIL→最小实现→跑验证 PASS→本地 commit（**不 push**）。提交信息风格：`fix(wfs-service-task): <中文描述>`。
- **独立性：**11 个 Task 互不依赖，可任意顺序 / 并行执行。建议顺序见文末「执行顺序」。

