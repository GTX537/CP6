## Task T6: E-WF-018 异步路径错误码结构化（去自由文本，仅结构化码 + 机读明细）

> **票6。** 缺陷：异步执行路径的 E-WF-018 错误把**本地化中文散文**拼进 `Error` 串——`WfServiceJobService.cs:117` `Fail($"E-WF-018 动作/连接器未注册:{key}")`、`WebApiExecutor.cs:35/40/44` 同款（`"E-WF-018 ActionRefJson 为空…"` 等）。这些串落进 `Wf_ServiceJob.LastError` 与错误路由，前端拿到无法按码 i18n（i18n seed 里 `E-WF-018` 是一个可翻译键），且中文散文与真实 detail（连接器名）混在一起不可解析。修法=统一为**结构化格式 `E-WF-018|<机读明细>`**（管道前=可翻译码，管道后=机读明细 token，无本地化散文）。前端/i18n 取 `|` 前的码翻译，`|` 后作诊断明细。

**Files:**
- Modify: `CP6.Core/Services/Wf/WfServiceJobService.cs:117`
- Modify: `CP6.Core/Services/Wf/Executors/WebApiExecutor.cs:34-44`
- Test: `CP6.Tests/Wf/WebApiExecutorTests.cs`（断言结构化格式，无中文散文）

- [ ] **Step 1: 写失败测试** — `WebApiExecutorTests.cs` 追加（该类已有 `FakeConn` 脚手架，仿之）：

```csharp
    [Fact]
    public async Task UnknownConnector_Fails_WithStructuredCode_NoProse()
    {
        // actionRef 引用未注册连接器 → 结构化 "E-WF-018|<connectorName>"，无中文散文
        var node = new FlowNode { Id = "n", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
            ServiceConnectorName = "ghost", ServicePath = "/x" };
        var ctx = new ServiceTaskContext {
            InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(), NodeId = "n",
            StarterId = System.Guid.Empty, JobId = System.Guid.NewGuid(), AttemptNo = 1,
            ActorId = System.Guid.Empty, NowUtc = System.DateTime.UtcNow,
            ActionRefJson = ServiceTaskActionRef.Snapshot(node),
        };
        var exec = new CP6.Core.Services.Wf.Executors.WebApiExecutor(System.Array.Empty<IWfConnector>());
        var r = await exec.ExecuteAsync(ctx);

        Assert.False(r.Success);
        Assert.StartsWith("E-WF-018", r.Error);        // 码在最前
        Assert.Contains("|", r.Error);                 // 结构化分隔
        Assert.Contains("ghost", r.Error!);            // 机读明细=连接器名
        Assert.DoesNotContain("未注册", r.Error!);      // 无本地化中文散文
        Assert.DoesNotContain("连接器", r.Error!);
    }

    [Fact]
    public async Task EmptyActionRef_Fails_WithStructuredCode()
    {
        var ctx = new ServiceTaskContext {
            InstanceId = System.Guid.NewGuid(), TokenId = System.Guid.NewGuid(), NodeId = "n",
            StarterId = System.Guid.Empty, JobId = System.Guid.NewGuid(), AttemptNo = 1,
            ActorId = System.Guid.Empty, NowUtc = System.DateTime.UtcNow, ActionRefJson = null,
        };
        var r = await new CP6.Core.Services.Wf.Executors.WebApiExecutor(System.Array.Empty<IWfConnector>()).ExecuteAsync(ctx);
        Assert.False(r.Success);
        Assert.StartsWith("E-WF-018|", r.Error);
        Assert.DoesNotContain("为空", r.Error!);
    }
```

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WebApiExecutorTests`。

- [ ] **Step 3: 实现**（约定：`Error = "E-WF-018|<detail>"`，detail 为无空格机读 token）

  a. `WebApiExecutor.cs:34-44`——把三处 `Fail("E-WF-018 …中文…")` 改为结构化：

```csharp
        if (string.IsNullOrEmpty(ctx.ActionRefJson))
            return ServiceTaskResult.Fail("E-WF-018|actionRefEmpty");

        ServiceTaskActionRef r;
        try { r = ServiceTaskActionRef.Parse(ctx.ActionRefJson); }
        catch (System.Exception ex)
        { return ServiceTaskResult.Fail($"E-WF-018|parseError:{ex.GetType().Name}"); }

        var connectorName = r.ConnectorName;
        if (string.IsNullOrEmpty(connectorName) || !_connectors.TryGetValue(connectorName, out var connector))
            return ServiceTaskResult.Fail($"E-WF-018|{connectorName}");
```

  b. `WfServiceJobService.cs:117`——把 `Fail($"E-WF-018 动作/连接器未注册:{key}")` 改为：

```csharp
                    result = ServiceTaskResult.Fail($"E-WF-018|{key}");
```

  > 幂等/退避/路由逻辑不变；`LastError` 现存的是结构化码而非散文，前端可按 `Error.Split('|')[0]` 取码 i18n。

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter WebApiExecutorTests`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T6 E-WF-018 异步路径改结构化码 E-WF-018|detail（去本地化散文，前端可按码翻译）"
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

