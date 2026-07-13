## Task T4: ServiceVarsHelper 点路径限制文档化 + 校验报错（含点键名/数组下标不支持）

> **票4。** 缺陷：`ServiceVarsHelper.ResolveDotPath`（`:128-158`）用 `path.Split('.')` 逐段导航，故 (a) 键名**本身含点**（如 `{"a.b":1}`）无法表达取值；(b) **数组下标**（如 `$.items[0]`）不被支持——`current["items[0]"]` 返回 null，模板静默求值为空串，用户无从察觉。方案裁定（YAGNI）：**不实现转义/下标**，改为「文档化限制 + 设计期校验报错」。修法=(1) 在 helper 补明确 XML 文档说明限制；(2) 加静态探测 `ContainsUnsupportedSubscript`；(3) 在 `FlowSchemaValidator` 的 serviceTask 分支扫描 `ServicePath`/`ServiceParamsJson` 中的模板 token，若含下标语法 `[...]` → `E-WF-016`（设计期即拦，不留到运行期静默失败）。

**Files:**
- Modify: `CP6.Core/Services/Wf/ServiceVarsHelper.cs`（补文档 + `ContainsUnsupportedSubscript`）
- Modify: `CP6.Core/Services/Wf/FlowSchemaValidator.cs:85-95`（serviceTask 分支加模板下标校验）
- Test: `CP6.Tests/Wf/ServiceVarsHelperTests.cs`（新增探测用例）、`CP6.Tests/Wf/ServiceTaskValidatorTests.cs`（新增下标→E-WF-016）

- [ ] **Step 1: 写失败测试**

  a. `ServiceVarsHelperTests.cs` 追加：

```csharp
    [Fact]
    public void ContainsUnsupportedSubscript_DetectsArrayIndex()
    {
        Assert.True(ServiceVarsHelper.ContainsUnsupportedSubscript("$.items[0]"));
        Assert.True(ServiceVarsHelper.ContainsUnsupportedSubscript("/o/{lines[2]}"));
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("$.orderId"));
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("/o/{orderId}"));
        // 字面 JSON 数组值（非模板下标）不误报：
        Assert.False(ServiceVarsHelper.ContainsUnsupportedSubscript("{\"list\":[1,2,3]}"));
    }
```

  b. `ServiceTaskValidatorTests.cs` 追加（脚手架仿该文件既有用例：构造含 serviceTask 的 `FlowSchema` 调 `FlowSchemaValidator.Validate`）：

```csharp
    [Fact]
    public void WebApi_PathWithArraySubscript_E_WF_016()
    {
        var schema = new FlowSchema {
            Nodes = {
                new FlowNode { Id = "s", Type = "start" },
                new FlowNode { Id = "svc", Type = "serviceTask", ServiceKind = ServiceKind.WebApi,
                    ServiceConnectorName = "erpEcho", ServicePath = "/o/{lines[0]}" },   // 下标非法
                new FlowNode { Id = "e", Type = "end" },
            },
            Edges = { new FlowEdge { From = "s", To = "svc" }, new FlowEdge { From = "svc", To = "e" } },
        };
        Assert.Contains("E-WF-016", FlowSchemaValidator.Validate(schema));
    }
```

  > 若 `ServiceTaskValidatorTests.cs` 里已有构造 `FlowSchema` 的 helper（如 `Node(...)`/`Edge(...)`），复用之，别自造重复脚手架——先读该文件顶部。

- [ ] **Step 2: 跑验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "ServiceVarsHelperTests|ServiceTaskValidatorTests"`。

- [ ] **Step 3: 实现**

  a. `ServiceVarsHelper.cs` 类级 XML 文档（`:29-31` 的 `<summary>`）追加限制说明，并新增探测方法（放在 `MergeOutputVars` 之后、`ResolveDotPath` 之前）：

```csharp
    /// <summary>
    /// 探测模板 token 是否含**不支持**的数组下标语法（`[...]`）。点路径求值（<see cref="ResolveValue"/>）
    /// 仅支持嵌套对象的逐段导航（`$.a.b`）——**不支持**数组下标（`$.items[0]`），也**无法**表达含点的键名
    /// （`{"a.b":1}` 与嵌套 `a.b` 二义，按嵌套解析）。这两类由设计期校验拦截（<c>FlowSchemaValidator</c>），
    /// 运行期遇到则静默求值为空串。本方法只对 `$.`/`{...}` 模板 token 内的 `[`/`]` 报真，避开字面 JSON 数组值。
    /// </summary>
    public static bool ContainsUnsupportedSubscript(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        // $.path[...]  ——  $. 后跟标识符/点，直到出现下标括号
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\$\.[A-Za-z0-9_.]*[\[\]]")) return true;
        // {placeholder[...]}  ——  花括号占位内出现下标括号
        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\{[A-Za-z0-9_.]*[\[\]][^}]*\}")) return true;
        return false;
    }
```

  b. `FlowSchemaValidator.cs` serviceTask 分支（`:85-95`）在 `bad` 判定里追加下标检查。把 `:88-93` 的 `bool bad = ...` 表达式尾部加一项：

```csharp
            var kind = (n.ServiceKind ?? string.Empty).Trim();
            bool bad =
                !KnownServiceKinds.Contains(kind)
                || (kind == ServiceKind.DataWriteback && string.IsNullOrWhiteSpace(n.ServiceActionName))
                || (kind == ServiceKind.WebApi && (string.IsNullOrWhiteSpace(n.ServiceConnectorName) || string.IsNullOrWhiteSpace(n.ServicePath)))
                || (kind == ServiceKind.Timer && (string.IsNullOrWhiteSpace(n.ServiceDelayMode) || string.IsNullOrWhiteSpace(n.ServiceDelayValue)))
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServicePath)         // 票4：路径模板不得含数组下标
                || ServiceVarsHelper.ContainsUnsupportedSubscript(n.ServiceParamsJson)   // 票4：参数模板不得含数组下标
                || !schema.Edges.Any(e => e.From == n.Id && e.IsError != true);   // P2-3：无非错误出边
            if (bad) { errs.Add("E-WF-016"); break; }
```

- [ ] **Step 4: 跑验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "ServiceVarsHelperTests|ServiceTaskValidatorTests"`。

- [ ] **Step 5: Wf 闸 + commit**
```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter Wf
git add -A && git commit -m "fix(wfs-service-task): T4 ServiceVarsHelper 点路径限制文档化 + 设计期拦数组下标模板（E-WF-016）"
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

