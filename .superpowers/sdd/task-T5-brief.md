## Task T5: FlowSchemaValidator 补 ServiceMode 值域校验（sync|async）

> **票5。** 缺陷：spec §6.1 明列「`ServiceMode ∈ {sync,async}`（timer 规整为 async）」，但 `FlowSchemaValidator` 的 serviceTask 分支（`:85-95`）只校验 `ServiceKind`，**从不校验 `ServiceMode`**——用户把 `serviceMode` 手填成非法值（如 `"batch"`）能通过保存，运行期 `ServiceTaskNodeHandler` 的 mode 解析按未知值走默认，行为不可预期。修法=值域检查一行 + 一测。`ServiceMode` 常量在 `WfStatus.cs:65-69`（`Sync="sync"`/`Async="async"`）。

**Files:**
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs:10-11`（加 `KnownServiceModes` 集合）、`:85-95`（分支加 mode 校验）
- Test: `CP6.Tests/Wf/ServiceTaskValidatorTests.cs`（新增 mode 非法/合法各一）

- [ ] **Step 1: 写失败测试** — `ServiceTaskValidatorTests.cs` 追加（复用该文件既有 FlowSchema 构造脚手架）：

```csharp
    [Fact]
    public void ServiceMode_Invalid_E_WF_016()
    {
        var schema = new FlowSchema {
            Nodes = {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                    ServiceActionName = "sampleWriteback", ServiceMode = "batch" },   // 非法 mode
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
        };
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(schema));
    }

    [Fact]
    public void ServiceMode_SyncOrAsync_Or_Null_Passes()
    {
        foreach (var mode in new string?[] { null, "sync", "async" })
        {
            var schema = new FlowSchema {
                Nodes = {
                    new FlowNode { Id = "s", Type = "start" },
                    new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.DataWriteback,
                        ServiceActionName = "sampleWriteback", ServiceMode = mode },
                    new FlowNode { Id = "e", Type = "end" },
                },
                Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
            };
            Assert.DoesNotContain("E-WF-016", FlowSchemaValidator.Validate(schema));
        }
    }
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests`（`ServiceMode_Invalid_E_WF_016` FAIL）。

- [ ] **Step 3: 实现**

  a. `FlowSchemaValidator.cs:10-11` 之后加常量集合：

```csharp
    // 服务任务合法 mode（spec §6.1；timer 由 handler 规整为 async，此处只校验用户显式填值）。
    // 用序数比较对齐运行期语义（ServiceMode 常量为小写 "sync"/"async"）。
    private static readonly HashSet<string> KnownServiceModes =
        new(new[] { ServiceMode.Sync, ServiceMode.Async }, StringComparer.Ordinal);
```

  b. serviceTask 分支 `bool bad = ...`（`:88-93`）追加一项（放在 kind 检查之后）：

```csharp
                || (!string.IsNullOrWhiteSpace(n.ServiceMode) && !KnownServiceModes.Contains(n.ServiceMode.Trim()))  // 票5：ServiceMode 值域
```

  > 注意：`ServiceMode` 可为 null（不填=按 kind 默认），故仅在**非空**时校验值域。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter ServiceTaskValidatorTests`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T5 FlowSchemaValidator 补 ServiceMode 值域校验（sync|async → E-WF-016）"
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

