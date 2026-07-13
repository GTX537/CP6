## Task T11: SignalR 通知 hub 被 CSRF 中间件 403 拦截 → 放行 hub 路径

> **票11。** 缺陷：`CsrfMiddleware`（`:25-41`）对所有非安全方法（POST/PUT/PATCH/DELETE）校验双提交 token，仅豁免 `/api/auth/login`（`:31`）。SignalR 连接的 **negotiate 是 POST**（`/hubs/notify/negotiate`），浏览器 SignalR 客户端（`cp6.web/src/utils/signalr.ts:18` `.withUrl('/hubs/notify')`）默认不带 `X-CSRF-Token` 头 → negotiate 收 403（`E-SEC-010`）→ 实时通知（`NotificationBell`/dashboard 推送）连不上。hub 路由注册于 `Program.cs:2520-2522`（`/hubs/notify`、`/hubs/mes`、`/hubs/wms`）。
>
> **本 Task 允许诊断分支**：先按 Step A 现场诊断确认 403 来源，再据结果走 **B1（豁免 hub 路径，推荐）** 或 **B2（negotiate 携带 CSRF token）**。两方案均在下方写实；执行时二选一。

**Files:**
- （诊断）无
- （B1）Modify: `CP6.WebApi/Middleware/CsrfMiddleware.cs:29-38`
- （B1）Test: `CP6.Tests/Wf/CsrfHubExemptionTests.cs`（或 `CP6.Tests` 现有中间件测试目录，执行时 Glob `**/*Csrf*Tests.cs` 确认；无则新建于 `CP6.Tests/Wf/`）
- （B2）Modify: `cp6.web/src/utils/signalr.ts:15-24`

### Step A — 现场诊断（确认 403 来自 CSRF、且发生在 negotiate）

- [ ] **A1** 起后端 + 前端（隔离库；QA 登录 admin/123456）。浏览器开发者工具 Network 过滤 `negotiate`，观察 `/hubs/notify/negotiate` 请求：
  - 若状态 **403** 且响应体含 `E-SEC-010` → CSRF 拦截确认，进 B1 或 B2。
  - 若 403 但非 `E-SEC-010`（如 401 认证）→ 非本票范畴，另查认证。
  - 若 `Security:Csrf:Enabled=false`（开发默认可能关）→ 在开启 CSRF 的环境（QA/生产配置）复现后再修。
- [ ] **A2** 确认 negotiate 是唯一被拦的请求（WS upgrade 是 GET=安全方法，不被 CSRF 拦）。据此定：**放行 hub 路径的 negotiate 即足够**。

### Step B1 —（推荐）CsrfMiddleware 豁免 hub 路径

> 理由：hub negotiate 不改服务端业务状态（仅协商传输），且 hub 自身经 JWT/cookie 认证；放行 negotiate 安全。复用既有 `PathMatches` 段边界匹配，避免 `/hubs/notifyxxx` 误豁免。

- [ ] **B1-1: 写失败测试** — `CsrfHubExemptionTests.cs`：

```csharp
using CP6.WebApi.Middleware;
using Xunit;

public class CsrfHubExemptionTests
{
    [Theory]
    [InlineData("/hubs/notify", true)]
    [InlineData("/hubs/notify/negotiate", true)]
    [InlineData("/hubs/mes/negotiate", true)]
    [InlineData("/hubs/wms", true)]
    [InlineData("/api/oa/designer/save", false)]   // 业务写请求仍受 CSRF 约束
    [InlineData("/hubsxxx/notify", false)]          // 段边界：非 /hubs/ 前缀不豁免
    public void HubPaths_AreExempt(string path, bool expectExempt)
        => Assert.Equal(expectExempt, CsrfMiddleware.IsExempt(path));
}
```

- [ ] **B1-2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter CsrfHubExemptionTests`（`IsExempt` 不存在）。

- [ ] **B1-3: 实现** — `CsrfMiddleware.cs:29-38` 把内联豁免判断抽成可测的 `IsExempt`，并加 `/hubs` 前缀：

```csharp
        if (_enabled)
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!IsExempt(path) && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
            {
                var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];
                var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrEmpty(cookie) || cookie != header)
                    throw new BizException("E-SEC-010", 403);   // 403 Forbidden：CSRF 校验失败（spec §5.3）
            }
        }
        await _next(ctx);
    }

    /// <summary>CSRF 豁免路径（段边界匹配，杜绝同前缀误豁免）：
    /// ① 登录端点（登录时尚无 csrf cookie）；② SignalR hub 路径（negotiate 是 POST 但不改业务状态，
    /// hub 自身经 JWT/cookie 认证；票11：否则实时通知 negotiate 被 403 拦）。</summary>
    internal static bool IsExempt(string path)
        => PathMatches(path, "/api/auth/login")
           || PathMatches(path, "/hubs");
```

  > 保留原 `PathMatches`（`:44-46`）。`/hubs` 前缀经 `PathMatches` 段边界匹配覆盖 `/hubs`、`/hubs/notify`、`/hubs/mes/negotiate` 等，但不误豁免 `/hubsxxx`。

- [ ] **B1-4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter CsrfHubExemptionTests`。

- [ ] **B1-5: 现场复验** — 重起后端，浏览器确认 `/hubs/notify/negotiate` 返回 200、SignalR `[SignalR] Connected`、通知角标实时更新。

- [ ] **B1-6: 编译 + 全量 CSRF 相关闸 + commit**
```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter Csrf
git add -A && git commit -m "fix(wfs-service-task): T11 CsrfMiddleware 豁免 /hubs 路径（修复 SignalR negotiate 被 CSRF 403 拦截）"
```

### Step B2 —（备选）SignalR negotiate 携带 CSRF token

> 仅当团队要求 hub 也走 CSRF（不豁免）时选此。`@microsoft/signalr` 浏览器客户端的 `headers` 选项作用于 negotiate 的 XHR（WS upgrade 是 GET 不需）。从非 httpOnly 的 `cp6_csrf` cookie 读值注入头。

- [ ] **B2-1: 实现** — `cp6.web/src/utils/signalr.ts:15-24` 的 `getConnection` 改为读 csrf cookie 注入 negotiate 头：

```typescript
function readCookie(name: string): string {
  const m = document.cookie.match(new RegExp('(?:^|; )' + name.replace(/([.$?*|{}()[\]\\/+^])/g, '\\$1') + '=([^;]*)'))
  return m ? decodeURIComponent(m[1]!) : ''
}

export function getConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      // 票11-B2：negotiate 是 POST，须带 X-CSRF-Token 头过 CsrfMiddleware 双提交校验（cp6_csrf 非 httpOnly，可读）。
      .withUrl('/hubs/notify', { headers: { 'X-CSRF-Token': readCookie('cp6_csrf') } })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}
```

- [ ] **B2-2: 验证** — `cd cp6.web && NODE_OPTIONS=--max-old-space-size=8192 npm run type-check && npm run build`；浏览器复验 negotiate 200 + Connected。
- [ ] **B2-3: commit** — `git commit -m "fix(wfs-service-task): T11 SignalR negotiate 携带 X-CSRF-Token 头（过 CSRF 双提交校验）"`

> **注：** `cp6_csrf` cookie 名以 `AuthCookieWriter.CsrfCookie` 常量为权威（执行时 Grep 确认字面值，勿硬猜）。若两 hub（mes/wms）也报同问题，B1 已一并覆盖；B2 需在 `mesHub.ts`/`wmsHub.ts` 同法各加。

---

## DoD / 验收

逐票完成后跑全量闸，全绿方可交付：

- [ ] **后端全量：** `dotnet test CP6.Tests/CP6.Tests.csproj` — 1509 测试全绿（5 skip=SQLite 既知），含本计划新增：`Reaper_ClaimedButNeverExecuted_DoesNotBurnAttempt`、`WfConnectorLeaseGuardTests`、`ContainsUnsupportedSubscript_*`、`WebApi_PathWithArraySubscript_E_WF_016`、`ServiceMode_*`、`UnknownConnector_Fails_WithStructuredCode_NoProse`、`CsrfHubExemptionTests`。
- [ ] **后端 Wf 闸：** `dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf` 全绿（`Reaper_ResetsExpiredLease_Only` 断言已由 T2 更新为 `AttemptCount==1`）。
- [ ] **EF clean：** `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` — 报无 pending（本计划零实体改动）。
- [ ] **前端：**
```bash
cd cp6.web
npm run test                                               # vitest 320 全绿（+ designerModel.serviceTask.spec.ts 新增 error edge 用例）
NODE_OPTIONS=--max-old-space-size=8192 npm run type-check  # 无 TS 错
npm run build                                              # 构建成功
```
- [ ] **零硬编码色：** `git diff <base>..HEAD -- 'cp6.web/**'` 无十六进制颜色字面量（T9 用 `var(--cp-danger)`）。
- [ ] **零跨模块污染：** `git diff --stat <base>..HEAD` 无 `views/space`、`*Space*`、Space 迁移文件。
- [ ] **i18n 五语齐全：** 新增键 `oa.designer.svc.reloadCatalog`（T7）、`oa.designer.svc.timerActionKind[.none|.write|.api]`（T8）五语齐全；T10 Ko 润色只动 Ko。
- [ ] **T11 现场复验：** SignalR `/hubs/notify/negotiate` 返回 200，浏览器控制台 `[SignalR] Connected`，通知角标实时更新。

### 执行顺序（建议；11 票互不依赖，可并行）

后端纯逻辑先行（T2/T4/T5/T6 同属 `CP6.Core/Services/Wf`，注意避免同文件并发编辑：T4/T5 均改 `FlowSchemaValidator.cs`，T2/T6 均改 `WfServiceJobService.cs`——同文件的票串行做）→ T1（Program.cs 配置）→ T3（连接器护栏，改 IWfConnector + Program.cs）→ 前端 T7/T8/T9（T7/T8 同改 `NodePropertyPanel.vue` 与 seed，串行）→ T10（seed Ko）→ T11（CSRF，独立）。收尾跑 DoD 全量闸。

> **同文件冲突提示：** `FlowSchemaValidator.cs`（T4+T5）、`WfServiceJobService.cs`（T2+T6）、`NodePropertyPanel.vue`（T7+T8）、`I18nOaServiceTaskScreenSeed.cs`（T7+T8+T10）各被多票触碰——这些票**必须串行**（一票 commit 后再起下一票），不可并发子代理同时改。
>
> **T4/T5 互保提示：** 两票都改 `FlowSchemaValidator.cs` 的同一个 `bool bad = ...` 表达式，且各自代码块只展示了"本票视角"的最终形态——**后跑的票不可整块照抄**，必须在当前文件实际内容上**追加自己那一行**并保留先跑票已加的行（T4=两行 `ContainsUnsupportedSubscript`，T5=一行 `KnownServiceModes`）。
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

