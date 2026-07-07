# OA 审批解耦执行计划（WFS 审批接入套件 + 全站唯一审批面）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让代码写的客制化页面"实现一个 Callback + 放一个 ApprovalPanel + 配一条绑定"即接入 WFS 审批；审批 UI 全站收敛为一份（SFS FormDetail 同步换装）；顺带修复旧单渲染错位与 FormDetail 丢 rules 两个正确性风险。

**Architecture:** 后端在既有 `ApprovalService`/`Wf_ApprovalBinding`/`IApprovalCallback` 骨架上补条件选流程（fail-closed）、聚合查询端点（可见性判定）、绑定管理与生命周期守卫；前端新建 `useApproval` composable + `<ApprovalPanel>` 组件（引擎动词 vs 业务动词的描述符驱动动作模型），收件箱按 `detailRoute` 深链跳业务页。**不设通用 submit 端点**——提交永远走业务端点（防 snapshot 信任漏洞）。

**Tech Stack:** .NET 8 + EF Core（迁移/xUnit InMemory 测试）/ Vue3 + TS + vitest / Sys_Langs 五语种子。

**Spec:** `docs/superpowers/specs/2026-07-07-oa-approval-decoupling-design.md`（含 2026-07-07 用户评审 8 项修订 + plan 阶段两处回写）。**每任务实现前先读 spec 对应节。**

## Global Constraints

- 基线不许跌：后端 `dotnet test` 1565 绿；前端 `cd cp6.web && bun run test` 369 绿；`bun run type-check` 0 错误（需 `NODE_OPTIONS=--max-old-space-size=8192`）。
- **每个 commit 完成后立即 `git push`**（用户硬性纪律）。
- 错误码本包锁定 **E-WF-031~035**（spec §7 表），沿用 Wf 惯例 `throw new InvalidOperationException("E-WF-0xx")`，前端经 i18n 词条翻译。
- fail-closed：条件选流程任何解析/求值/FlowKey 无效错误一律拒绝提交，绝不静默回落。主 FlowKey 恒检（有意从严，spec §3.1，不得放松）。
- FlowKey 可发起性判定**单点收敛**到 `FlowStartability.IsStartableAsync`（四期 V-A 前向条款：将来只改这一处）。
- 前端组件用 Cp* 设计系统 token；新词条五语（zh-CN/zh-TW/en/ja/ko）随种子入库，不硬编码。
- 后端实体继承链、多租户零代码（BaseTenantEntity 自动过滤/盖章）不动。

---

## 文件地图

| 文件 | 动作 | 职责 |
|---|---|---|
| `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs` | 改 | +DetailRoute |
| `CP6.Entity/DomainModels/Wf/Wf_FormData.cs` | 改 | +SchemaSnapshotJson |
| `CP6.Core/Services/Wf/ExpressionEvaluator.cs` | 改 | +TryEvaluateStrict/+ValidateSyntax（严格语义补充，现有安全失败 API 不动） |
| `CP6.Core/Services/Wf/FlowStartability.cs` | 建 | FlowKey 可发起性单点判定 |
| `CP6.Core/Services/Wf/ApprovalService.cs` | 改 | 条件选流程 + fail-closed |
| `CP6.Core/Services/Oa/ApprovalPanelService.cs` | 建 | 聚合查询 + 可见性判定 |
| `CP6.Core/Services/Oa/ApprovalBindingAdminService.cs` | 建 | 绑定 CRUD/校验/删除守卫/模拟求值 |
| `CP6.WebApi/Controllers/Oa/ApprovalController.cs` | 建 | GET /api/oa/approval/detail |
| `CP6.WebApi/Controllers/Oa/FlowAdminController.cs` | 改 | +绑定管理端点 |
| `CP6.WebApi/Seed/I18nOaApprovalSeed.cs` | 建 | E-WF-031~035 + 面板/管理 UI 词条五语 |
| `CP6.Core/Services/Wf/FormService.cs` | 改 | 提交落 schema 快照 |
| `CP6.Core/Services/Oa/InboxService.cs` | 改 | 详情优先快照 + 行下发 detailRoute |
| `cp6.web/src/api/oa/approval.ts` | 建 | 聚合端点 API |
| `cp6.web/src/types/oa/approval.ts` | 建 | ApprovalAction/PanelData 类型 |
| `cp6.web/src/composables/useApproval.ts` | 建 | 审批逻辑层 |
| `cp6.web/src/components/approval/ApprovalPanel.vue` | 建 | 全站唯一审批面 |
| `cp6.web/src/components/approval/{TransferDialog,SendBackDialog}.vue` | 迁 | 从 views/oa/inbox 收编 |
| `cp6.web/src/views/oa/inbox/FormDetail.vue` | 改 | 换装 Panel + rules 显隐修复 |
| `cp6.web/src/views/oa/inbox/{InboxView,InboxPending,InboxRunning,InboxDone}.vue` | 改 | 深链 |
| `cp6.web/src/views/oa/admin/ApprovalBindingAdmin.vue` | 建 | 绑定管理（挂 FlowAdmin 抽屉，复用其权限） |
| `docs/oa/11-approval-integration.md` | 建 | 接入黄金模板 |

任务依赖：A1→A2→A3→A4→A5 串行（A6/A7 可与 A4/A5 并行）；B1 依赖 A4；B2 依赖 B1；B3/B4 依赖 B2 与 A6/A7；B5 依赖 A5；B6 依赖 B2；C1/C2 收尾。

---

### Task A1: 实体扩列 + EF 迁移

**Files:**
- Modify: `CP6.Entity/DomainModels/Wf/Wf_ApprovalBinding.cs`
- Modify: `CP6.Entity/DomainModels/Wf/Wf_FormData.cs`

**Interfaces:**
- Produces: `Wf_ApprovalBinding.DetailRoute: string?`（MaxLength 200）、`Wf_FormData.SchemaSnapshotJson: string?`（nvarchar(max)）——A3~A7 全部依赖这两列。

- [ ] **Step 1: Wf_ApprovalBinding 加 DetailRoute**（`Remark` 属性之前插入）

```csharp
    /// <summary>前端深链路由模板（spec §3.1），如 "/pur/orders/{bizId}"；仅支持 {bizId} 占位符。
    /// 为空 = 该绑定走收件箱 FormDetail（SFS 表单绑定留空）。停用绑定不影响本列下发（Enable 只封发起）。</summary>
    [MaxLength(200)]
    public string? DetailRoute { get; set; }
```

- [ ] **Step 2: Wf_FormData 加 SchemaSnapshotJson**（`DataJson` 属性之后）

```csharp
    /// <summary>提交时的表单 schema 快照（spec §6.1）：SaveDefAsync 原地覆盖 SchemaJson 致历史版不可回查，
    /// 故提交时定格。渲染侧优先用本列；null（存量老单）回落当前 FormDef.SchemaJson。</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? SchemaSnapshotJson { get; set; }
```

- [ ] **Step 3: 生成迁移并核对**

Run: `dotnet ef migrations add OaApprovalDecoupling --project CP6.Core --startup-project CP6.WebApi`
Expected: 迁移文件只含两个 AddColumn（Wf_ApprovalBinding.DetailRoute nvarchar(200) null / Wf_FormData.SchemaSnapshotJson nvarchar(max) null）。多出任何其他变更 = 模型漂移，停下排查。

- [ ] **Step 4: 全量编译+测试**

Run: `dotnet build && dotnet test`
Expected: 编译 0 错误，1565 测试绿（纯加列不破坏既有行为）。

- [ ] **Step 5: Commit + push**

```bash
git add CP6.Entity CP6.Core/Migrations
git commit -m "feat(oa-approval): Wf_ApprovalBinding.DetailRoute + Wf_FormData.SchemaSnapshotJson 迁移"
git push
```

---

### Task A2: ExpressionEvaluator 严格求值 API

**背景（实现者必读）**：现有 `Evaluate` 是**安全失败**语义（任何错误静默返 false，绝不抛）。条件选流程若直接用它，"表达式坏了"会退化成"规则不命中"静默回落主 FlowKey——违反 spec §3.1 fail-closed。故补两个 API，现有 API 语义**一字不动**（表单/条件边继续安全失败）。

**Files:**
- Modify: `CP6.Core/Services/Wf/ExpressionEvaluator.cs`
- Test: `CP6.Tests/Wf/ExpressionEvaluatorStrictTests.cs`（新建）

**Interfaces:**
- Produces: `public static (bool ok, bool value) TryEvaluateStrict(string? expression, IReadOnlyDictionary<string, object?> vars)`；`public static bool ValidateSyntax(string? expression)`——A3 提交求值、A5 保存预检依赖。

- [ ] **Step 1: 写失败测试**

```csharp
using CP6.Core.Services.Wf;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>严格求值 API（审批绑定条件选流程用，spec §3.1 fail-closed）。</summary>
public class ExpressionEvaluatorStrictTests
{
    private static IReadOnlyDictionary<string, object?> Vars(string json) => ExpressionEvaluator.ParseVars(json);

    [Fact] public void Strict_True()
        => Assert.Equal((true, true), ExpressionEvaluator.TryEvaluateStrict("amount > 100", Vars("""{"amount":200}""")));

    [Fact] public void Strict_False()
        => Assert.Equal((true, false), ExpressionEvaluator.TryEvaluateStrict("amount > 100", Vars("""{"amount":50}""")));

    [Fact] public void Strict_UnknownField_IsError()   // 与 Evaluate 的静默 false 相区分——这是本 API 存在的理由
        => Assert.False(ExpressionEvaluator.TryEvaluateStrict("nosuch > 1", Vars("""{"amount":1}""")).ok);

    [Fact] public void Strict_SyntaxError_IsError()
        => Assert.False(ExpressionEvaluator.TryEvaluateStrict("amount >", Vars("""{"amount":1}""")).ok);

    [Fact] public void Strict_Empty_IsError()          // 绑定规则 when 不允许为空
        => Assert.False(ExpressionEvaluator.TryEvaluateStrict("  ", Vars("{}")).ok);

    [Fact] public void ValidateSyntax_Ok_EvenWithUnknownFields()   // 保存预检不知道运行时字段，任意字段按 0 兜底
        => Assert.True(ExpressionEvaluator.ValidateSyntax("amount > 100 && dept == \"IT\""));

    [Fact] public void ValidateSyntax_BadSyntax_False()
        => Assert.False(ExpressionEvaluator.ValidateSyntax("amount > "));

    [Fact] public void ValidateSyntax_Empty_False()
        => Assert.False(ExpressionEvaluator.ValidateSyntax(""));
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test --filter ExpressionEvaluatorStrictTests`
Expected: 编译错误（TryEvaluateStrict 不存在）。

- [ ] **Step 3: 实现**（加在 `Compute` 重载之后；`TryEval`/`ToBool` 是类内已有私有成员，直接用）

```csharp
    /// <summary>严格求值（审批绑定条件选流程，spec §3.1 fail-closed）：区分「求值为 false」与「表达式错误」。
    /// 空表达式=错误（绑定规则 when 必填）。ok=false 时调用方必须拒绝，不得回落。</summary>
    public static (bool ok, bool value) TryEvaluateStrict(string? expression, IReadOnlyDictionary<string, object?> vars)
    {
        if (string.IsNullOrWhiteSpace(expression)) return (false, false);
        var (ok, val) = TryEval(expression, vars);
        return (ok, ok && ToBool(val));
    }

    /// <summary>语法预检（绑定保存用，spec §3.2）：任意字段按 0 兜底取值，只暴露词法/语法错误，
    /// 不误伤运行时才存在的字段。</summary>
    public static bool ValidateSyntax(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var (ok, _) = TryEval(expression, AnyFieldVars.Instance);
        return ok;
    }

    /// <summary>ValidateSyntax 专用：任何键都命中、值恒 0d 的字典。</summary>
    private sealed class AnyFieldVars : IReadOnlyDictionary<string, object?>
    {
        public static readonly AnyFieldVars Instance = new();
        public object? this[string key] => 0d;
        public IEnumerable<string> Keys => Array.Empty<string>();
        public IEnumerable<object?> Values => Array.Empty<object?>();
        public int Count => 0;
        public bool ContainsKey(string key) => true;
        public bool TryGetValue(string key, out object? value) { value = 0d; return true; }
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
            => Enumerable.Empty<KeyValuePair<string, object?>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
```

注意：若 `ToBool` 对 0d 之外的场景有特殊处理导致 `dateDiff` 等函数在 AnyFieldVars 下抛非语法错误，属可接受边界（该表达式会在模拟求值时用真样本验证）——但 8 个测试必须全绿。

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test --filter ExpressionEvaluatorStrictTests`
Expected: 8 PASS。再跑 `dotnet test --filter ExpressionEvaluatorTests` 确认既有 14 用例不回归。

- [ ] **Step 5: Commit + push**

```bash
git add CP6.Core/Services/Wf/ExpressionEvaluator.cs CP6.Tests/Wf/ExpressionEvaluatorStrictTests.cs
git commit -m "feat(oa-approval): ExpressionEvaluator 严格求值 TryEvaluateStrict + 语法预检 ValidateSyntax"
git push
```

---

### Task A3: 条件选流程 + FlowStartability 单点 + 错误码种子

**Files:**
- Create: `CP6.Core/Services/Wf/FlowStartability.cs`
- Modify: `CP6.Core/Services/Wf/ApprovalService.cs`
- Create: `CP6.WebApi/Seed/I18nOaApprovalSeed.cs`（照 `CP6.WebApi/Seed/I18nBackendMsgSeed.cs` 的注册与幂等模式接线到启动种子链——先读该文件抄结构）
- Test: `CP6.Tests/Wf/ApprovalBindingResolveTests.cs`（新建）

**Interfaces:**
- Consumes: A2 的 `TryEvaluateStrict`。
- Produces: `FlowStartability.IsStartableAsync(CP6Context db, string flowKey): Task<bool>`（A5 复用）；`ApprovalService.ResolveFlowKey(Wf_ApprovalBinding, string varsJson): string`（internal static，供直测与 A5 模拟求值复用）；`ApprovalService.BindingRule(string? When, string? FlowKey)` record。

- [ ] **Step 1: 写失败测试**（InMemory CP6Context 构造照 `CP6.Tests/Wf/` 既有测试类的 fixture 写法——先读 `CP6.Tests/Pur/PurApprovalIntegrationTests.cs` 抄 DbContext 搭建）

```csharp
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>条件选流程（spec §3.1）：首中即选 / 回落 / fail-closed 三族 + 主 key 有意从严。</summary>
public class ApprovalBindingResolveTests
{
    private static Wf_ApprovalBinding B(string? cond) => new()
        { BizType = "PO", FlowKey = "po-default", ConditionJson = cond, Enable = true };

    [Fact] public void NoCondition_ReturnsMainKey()
        => Assert.Equal("po-default", ApprovalService.ResolveFlowKey(B(null), """{"amount":1}"""));

    [Fact] public void FirstMatch_Wins()
        => Assert.Equal("po-high", ApprovalService.ResolveFlowKey(
            B("""[{"when":"amount > 100000","flowKey":"po-high"},{"when":"amount > 10000","flowKey":"po-mid"}]"""),
            """{"amount":200000}"""));

    [Fact] public void SecondMatch_WhenFirstMisses()
        => Assert.Equal("po-mid", ApprovalService.ResolveFlowKey(
            B("""[{"when":"amount > 100000","flowKey":"po-high"},{"when":"amount > 10000","flowKey":"po-mid"}]"""),
            """{"amount":50000}"""));

    [Fact] public void NoMatch_FallsBackToMainKey()
        => Assert.Equal("po-default", ApprovalService.ResolveFlowKey(
            B("""[{"when":"amount > 100000","flowKey":"po-high"}]"""), """{"amount":1}"""));

    [Fact] public void BadJson_Throws032()
        => Assert.Equal("E-WF-032", Assert.Throws<InvalidOperationException>(() =>
            ApprovalService.ResolveFlowKey(B("not-json"), "{}")).Message);

    [Fact] public void EmptyWhen_Throws032()
        => Assert.Equal("E-WF-032", Assert.Throws<InvalidOperationException>(() =>
            ApprovalService.ResolveFlowKey(B("""[{"when":"","flowKey":"x"}]"""), "{}")).Message);

    [Fact] public void EvalError_Throws033_NotSilentFallback()   // fail-closed 的灵魂用例
        => Assert.Equal("E-WF-033", Assert.Throws<InvalidOperationException>(() =>
            ApprovalService.ResolveFlowKey(
                B("""[{"when":"nosuch > 1","flowKey":"po-high"}]"""), """{"amount":1}""")).Message);
}
```

- [ ] **Step 2: 跑测试确认编译失败** — `dotnet test --filter ApprovalBindingResolveTests`

- [ ] **Step 3: 建 FlowStartability**

```csharp
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>FlowKey 可发起性单点判定（spec §3.1 前向条款）。
/// ⚠ 四期版本治理 V-A pin 落地时，口径从「存在且 Enable」切「最新 Published 且 Enable」（E-WF-029 语境）
/// <b>只改本方法</b>——ApprovalService 提交闸、BindingAdmin 保存校验自动继承，不得在调用点各写判定。</summary>
public static class FlowStartability
{
    public static Task<bool> IsStartableAsync(CP6Context db, string flowKey)
        => db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == flowKey && d.Enable);
}
```

（若编译报 `Wf_FlowDef` 无 `Enable` 属性：打开 `CP6.Entity/DomainModels/Wf/Wf_FlowDef.cs` 找启用位的真实名（可能是 `Status`/`Enabled`），以实名替换并同步改 A5 校验与本任务提交闸——**语义不变：该 FlowKey 当前可发起**。）

- [ ] **Step 4: 改 ApprovalService.SubmitAsync + 加 ResolveFlowKey**

`SubmitAsync` 中原来两行（取 binding、直接 `_flow.SubmitAsync(binding.FlowKey, ...)`）改为：

```csharp
        var binding = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(b => b.BizType == bizType && b.Enable)
                      ?? throw new InvalidOperationException("E-WF-031");   // 缺绑定/停用（Enable 只封发起，spec §3.1）

        var vars = formSnapshot is null ? "{}" : JsonSerializer.Serialize(formSnapshot);
        var flowKey = ResolveFlowKey(binding, vars);

        // 主 key 恒检（有意从严，spec §3.1：兜底契约不许烂着）+ 命中 key 检——都收敛到 FlowStartability 单点
        if (!await FlowStartability.IsStartableAsync(_db, binding.FlowKey))
            throw new InvalidOperationException("E-WF-034");
        if (flowKey != binding.FlowKey && !await FlowStartability.IsStartableAsync(_db, flowKey))
            throw new InvalidOperationException("E-WF-034");

        return await _flow.SubmitAsync(flowKey, starterId, vars, bizType, bizId);
```

类内新增：

```csharp
    /// <summary>条件规则行（spec §3.1）：[{"when":"amount > 100000","flowKey":"po-high"}, ...]。</summary>
    public sealed record BindingRule(string? When, string? FlowKey);

    private static readonly JsonSerializerOptions RuleJsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>条件选流程：顺序求值首中即选，全不中回落主 FlowKey。fail-closed：
    /// JSON 坏/行缺 when 或 flowKey → E-WF-032；表达式求值错误 → E-WF-033（绝不静默回落）。</summary>
    internal static string ResolveFlowKey(Wf_ApprovalBinding binding, string varsJson)
    {
        if (string.IsNullOrWhiteSpace(binding.ConditionJson)) return binding.FlowKey;
        List<BindingRule>? rules;
        try { rules = JsonSerializer.Deserialize<List<BindingRule>>(binding.ConditionJson, RuleJsonOpts); }
        catch { throw new InvalidOperationException("E-WF-032"); }
        if (rules is null || rules.Count == 0) throw new InvalidOperationException("E-WF-032");

        var vars = ExpressionEvaluator.ParseVars(varsJson);
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.When) || string.IsNullOrWhiteSpace(r.FlowKey))
                throw new InvalidOperationException("E-WF-032");
            var (ok, hit) = ExpressionEvaluator.TryEvaluateStrict(r.When, vars);
            if (!ok) throw new InvalidOperationException("E-WF-033");
            if (hit) return r.FlowKey!;
        }
        return binding.FlowKey;
    }
```

同文件顶部补 `using CP6.Entity.DomainModels.Wf;`（若缺）。

- [ ] **Step 5: 错误码种子** — 建 `CP6.WebApi/Seed/I18nOaApprovalSeed.cs`。**先读 `CP6.WebApi/Seed/I18nBackendMsgSeed.cs` 全文**，抄它的类结构、幂等守卫（按键 Any 判断）与注册点（Program.cs 或种子聚合类里与它相邻注册）。词条内容（键=码，五语）：

| 键 | zh-CN | ja | en |
|---|---|---|---|
| E-WF-031 | 未配置该业务类型的审批绑定或已停用 | 承認バインディングが未設定または無効です | Approval binding missing or disabled |
| E-WF-032 | 审批绑定条件规则配置错误 | 承認条件ルールの設定が不正です | Invalid approval condition rules |
| E-WF-033 | 审批条件表达式求值失败 | 承認条件式の評価に失敗しました | Condition expression evaluation failed |
| E-WF-034 | 审批流程不存在或已停用 | 承認フローが存在しないか無効です | Target flow missing or disabled |
| E-WF-035 | 绑定已被审批实例引用，仅可停用不可删除 | 実績のあるバインディングは削除不可（無効化のみ） | Binding referenced by instances; disable instead of delete |

zh-TW/ko 按上表语义补齐（zh-TW 用繁体直译，ko 用敬体）。UI 词条留待 B2/B5 追加进同一类。

- [ ] **Step 6: 跑测试** — `dotnet test --filter ApprovalBindingResolveTests` 7 PASS；`dotnet test` 全量绿（既有 ApprovalService 相关测试若断言了旧文案"未配置 …"需同步改为断言 E-WF-031——先 `grep -rn "未配置" CP6.Tests/` 排查）。

- [ ] **Step 7: Commit + push**

```bash
git add CP6.Core CP6.Tests CP6.WebApi/Seed
git commit -m "feat(oa-approval): 条件选流程 fail-closed(E-WF-031~034) + FlowStartability 单点 + 五语种子"
git push
```

---

### Task A4: 聚合端点（面板状态查询 + 可见性判定）

**Files:**
- Create: `CP6.Core/Services/Oa/ApprovalPanelService.cs`（含 `IApprovalPanelService` 与 DTO，单文件）
- Create: `CP6.WebApi/Controllers/Oa/ApprovalController.cs`
- Modify: `CP6.WebApi/Program.cs`（DI：`AddScoped<IApprovalPanelService, ApprovalPanelService>()`，加在 Oa 服务注册段相邻处）
- Test: `CP6.Tests/Oa/ApprovalPanelServiceTests.cs`（新建）

**Interfaces:**
- Produces（B1 前端契约，字段名 camelCase 序列化后一致）:

```csharp
public record ApprovalPanelMyTask(Guid TaskId, string NodeId, string? NodeName);
/// Status: -1=None(无实例) 0=Running 1=Approved 2=Rejected 3=Withdrawn 4=Suspended 5=Draft
public record ApprovalPanelDto(Guid? InstanceId, int Status, string? CurrentNodeName, Guid? StarterId,
    string? StarterName, ApprovalPanelMyTask? MyTask, IReadOnlyList<TimelineRow> Timeline,
    IReadOnlyList<ForecastStep> Forecast, bool CanSubmit);

public interface IApprovalPanelService
{
    /// <summary>双键二选一（都传/都不传 → InvalidOperationException("E-WF-002")，Controller 转 400）。
    /// 可见性未命中 → UnauthorizedAccessException（Controller 转 403 零信息）。</summary>
    Task<ApprovalPanelDto> GetAsync(Guid userId, string? bizType, string? bizId, Guid? instanceId);
}
```

- [ ] **Step 1: 写失败测试**（InMemory fixture 同 A3；种子一个实例 + FormTo + Cc 各角色）

```csharp
// CP6.Tests/Oa/ApprovalPanelServiceTests.cs —— 用例清单（spec §9），每条一个 [Fact]：
// 1 Starter_CanSee                    发起人查 → 返回 DTO，Status=Running
// 2 HistoricalHandler_CanSee          Wf_FlowFormTo.ActualHandlerId 命中 → 可见
// 3 CcRecipient_CanSee                Wf_FlowCc.RecipientId 命中 → 可见
// 4 Unrelated_Throws403               无关用户 → UnauthorizedAccessException
// 5 PendingHandler_GetsMyTask         当前办理人 → MyTask.TaskId = 其 Pending 任务
// 6 Observer_MyTaskNull               可见但无待办 → MyTask=null（旁观者语义）
// 7 NoInstance_SkeletonCanSubmit      biz 键无实例+绑定启用 → Status=-1, CanSubmit=true, Timeline 空
// 8 NoInstance_BindingDisabled        绑定 Enable=false → CanSubmit=false
// 9 Rejected_CanSubmitTrue / 10 Withdrawn_CanSubmitTrue / 11 Running_CanSubmitFalse / 12 Suspended_CanSubmitFalse
// 13 BothKeys_ThrowsE002 / 14 NeitherKey_ThrowsE002
// 15 BizMode_PicksLatestInstance      同 bizType+bizId 两实例 → 取 CreateDate 最新
```

（写全 15 个 Fact 的 Arrange/Act/Assert；Arrange 直接向 InMemory `CP6Context` 塞 `Wf_FlowInstance`/`Wf_FlowTask`/`Wf_FlowFormTo`/`Wf_FlowCc`/`Wf_ApprovalBinding` 行。）

- [ ] **Step 2: 确认编译失败** — `dotnet test --filter ApprovalPanelServiceTests`

- [ ] **Step 3: 实现 ApprovalPanelService**

```csharp
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>ApprovalPanel 聚合查询（spec §3.3）。可见性 = 发起人 ∪ 曾任/现任办理人(FormTo 三列) ∪ 被抄送人；
/// 管理员不入集合（走 FlowAdmin 自有入口，无权限旁路）。无实例场景只回骨架（信息量近零）。</summary>
public class ApprovalPanelService : IApprovalPanelService
{
    private readonly CP6Context _db;
    private readonly IInboxService _inbox;
    public ApprovalPanelService(CP6Context db, IInboxService inbox) { _db = db; _inbox = inbox; }

    public async Task<ApprovalPanelDto> GetAsync(Guid userId, string? bizType, string? bizId, Guid? instanceId)
    {
        var bizMode = !string.IsNullOrWhiteSpace(bizType) && !string.IsNullOrWhiteSpace(bizId);
        if (bizMode == instanceId.HasValue) throw new InvalidOperationException("E-WF-002"); // 双键二选一

        var inst = instanceId.HasValue
            ? await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId.Value)
            : await _db.Wf_FlowInstances.Where(i => i.BizType == bizType && i.BizId == bizId)
                .OrderByDescending(i => i.CreateDate).FirstOrDefaultAsync();

        if (inst is null)
        {
            if (!bizMode) throw new InvalidOperationException("E-WF-007");   // instanceId 模式查无 → 404 语义
            var canSubmitNew = await _db.Wf_ApprovalBindings.AnyAsync(b => b.BizType == bizType && b.Enable);
            return new ApprovalPanelDto(null, -1, null, null, null, null,
                Array.Empty<TimelineRow>(), Array.Empty<ForecastStep>(), canSubmitNew);
        }

        // ── 可见性（spec §3.3）──
        var visible = inst.StarterId == userId
            || await _db.Wf_FlowFormTos.AnyAsync(f => f.InstanceId == inst.Id &&
                   (f.ExpectedHandlerId == userId || f.ActualHandlerId == userId || f.OnBehalfOfId == userId))
            || await _db.Wf_FlowCcs.AnyAsync(c => c.InstanceId == inst.Id && c.RecipientId == userId);
        if (!visible) throw new UnauthorizedAccessException();

        var detail = await _inbox.DetailAsync(inst.Id)
            ?? throw new InvalidOperationException("E-WF-007");

        var myTaskRow = await _db.Wf_FlowTasks.FirstOrDefaultAsync(t =>
            t.InstanceId == inst.Id && t.AssigneeId == userId && t.Status == FlowTaskStatus.Pending);
        var myTask = myTaskRow is null ? null
            : new ApprovalPanelMyTask(myTaskRow.Id, myTaskRow.NodeId,
                detail.Timeline.LastOrDefault(r => r.NodeId == myTaskRow.NodeId)?.NodeName);

        var binding = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(b => b.BizType == inst.BizType && b.Enable);
        var canSubmit = binding is not null && inst.Status is FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn;
        // ↑ canSubmit 口径（spec §4.3）：None|Rejected|Withdrawn 且绑定启用；None 已在无实例分支返回。
        //   Suspended/Running/Approved/Draft 一律 false —— 不得掉 default。

        var currentNodeName = detail.Timeline.LastOrDefault(r => r.NodeId == inst.CurrentNode)?.NodeName;
        var starterName = detail.Timeline.Count > 0 ? null : null; // 见下一行注释
        // StarterName 用 OaUserNames 解析（照 InboxService.DetailAsync 用法）：
        var names = await OaUserNames.ResolveAsync(_db, new[] { inst.StarterId });

        return new ApprovalPanelDto(inst.Id, inst.Status, currentNodeName, inst.StarterId,
            names.GetValueOrDefault(inst.StarterId, inst.StarterId.ToString()),
            myTask, detail.Timeline, detail.Forecast, canSubmit);
    }
}
```

（`Wf_FlowTask` 的 `InstanceId`/`AssigneeId`/`NodeId` 属性名以实体文件为准，编译期即验证；`starterName` 中间行删除，直接用 names 解析结果。）

- [ ] **Step 4: 实现 ApprovalController**（错误映射照 InboxController 的 `Ok2/Err` 惯例）

```csharp
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>审批面板聚合端点（spec §3.3）。写动作不在此——同意/驳回/退回/转办走 /api/oa/inbox 既有端点。</summary>
[ApiController]
[Route("api/oa/approval")]
[Authorize]
public class ApprovalController : LocalizedControllerBase
{
    private readonly IApprovalPanelService _panel;
    private readonly ICurrentPermissionContext _ctx;
    public ApprovalController(IApprovalPanelService panel, ICurrentPermissionContext ctx)
    { _panel = panel; _ctx = ctx; }

    [HttpGet("detail")]
    public async Task<IActionResult> Detail([FromQuery] string? bizType, [FromQuery] string? bizId, [FromQuery] Guid? instanceId)
    {
        var me = (await _ctx.GetAsync()).UserId;
        try
        {
            var dto = await _panel.GetAsync(me, bizType, bizId, instanceId);
            return Ok(new { code = 0, message = "OK", data = dto });
        }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { code = 403, message = "forbidden" }); } // 零信息
        catch (InvalidOperationException e) when (e.Message == "E-WF-007")
        { return NotFound(new { code = 404, message = "E-WF-007" }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }
}
```

- [ ] **Step 5: DI 注册 + 跑测试** — Program.cs Oa 段加注册；`dotnet test --filter ApprovalPanelServiceTests` 15 PASS；全量绿。

- [ ] **Step 6: Commit + push**

```bash
git add CP6.Core CP6.WebApi CP6.Tests
git commit -m "feat(oa-approval): 聚合端点 GET /api/oa/approval/detail——双键+可见性判定+canSubmit 口径"
git push
```

---

### Task A5: 绑定管理服务与端点（CRUD/校验/删除守卫/模拟求值）

**Files:**
- Create: `CP6.Core/Services/Oa/ApprovalBindingAdminService.cs`（含接口与 DTO）
- Modify: `CP6.WebApi/Controllers/Oa/FlowAdminController.cs`（追加端点，**沿用该控制器现有权限守卫**——先读它现有 attribute 与路由前缀照抄）
- Modify: `CP6.WebApi/Program.cs`（DI 注册）
- Test: `CP6.Tests/Oa/ApprovalBindingAdminTests.cs`

**Interfaces:**
- Consumes: A2 `ValidateSyntax`、A3 `FlowStartability.IsStartableAsync` + `ApprovalService.ResolveFlowKey`。
- Produces（B5 前端契约）:

```csharp
public record BindingDto(Guid? Id, string BizType, string FlowKey, string? DetailRoute,
    bool Enable, string? ConditionJson, string? Remark);
public record SimulateResult(bool Ok, string? FlowKey, int MatchedIndex, string? Error); // MatchedIndex=-1 表示回落主 key

public interface IApprovalBindingAdminService
{
    Task<IReadOnlyList<BindingDto>> ListAsync();
    Task<Guid> SaveAsync(BindingDto dto);       // 校验失败抛 E-WF-032/034/BizType 重复(InvalidOperationException 原文案)
    Task DeleteAsync(Guid id);                  // 被实例引用 → E-WF-035
    Task<SimulateResult> SimulateAsync(BindingDto dto, string sampleJson);  // 不落库，纯求值
}
```

- [ ] **Step 1: 写失败测试**（用例即 spec §9 绑定生命周期节）

```csharp
// CP6.Tests/Oa/ApprovalBindingAdminTests.cs —— [Fact] 清单：
// 1 Save_New_Ok                       合法 dto（flowKey 已种入 Wf_FlowDefs 且 Enable）→ 落库
// 2 Save_BadExpression_Throws032      ConditionJson 含 "amount >" → E-WF-032（ValidateSyntax 拦）
// 3 Save_UnknownFlowKey_Throws034     规则 flowKey 未种 → E-WF-034（FlowStartability 拦，主 key 同检）
// 4 Save_DuplicateBizType_Throws      同 BizType 二条启用 → 抛（文案含 BizType）
// 5 Delete_Referenced_Throws035       种一条 Wf_FlowInstance.BizType 匹配（任意状态）→ E-WF-035
// 6 Delete_Unreferenced_Ok            无实例引用 → 物理删除成功
// 7 Simulate_Hit                      sample {"amount":200000} → FlowKey=po-high, MatchedIndex=0
// 8 Simulate_Fallback                 不中 → 主 key, MatchedIndex=-1
// 9 Simulate_EvalError_ReturnsError   规则字段 sample 里没有 → Ok=false, Error="E-WF-033"（模拟不抛，回结果）
```

- [ ] **Step 2: 确认失败** — `dotnet test --filter ApprovalBindingAdminTests`

- [ ] **Step 3: 实现服务**。要点（完整写出，此处列关键实现约束）：

```csharp
    public async Task<Guid> SaveAsync(BindingDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.BizType) || string.IsNullOrWhiteSpace(dto.FlowKey))
            throw new InvalidOperationException("BizType/FlowKey 必填");
        // BizType 唯一（启用维度）——排除自身
        if (await _db.Wf_ApprovalBindings.AnyAsync(b => b.BizType == dto.BizType && b.Id != (dto.Id ?? Guid.Empty)))
            throw new InvalidOperationException($"业务类型 {dto.BizType} 已存在绑定");
        // 主 key + 全部规则 flowKey 可发起性（FlowStartability 单点，勿另写查询）
        if (!await FlowStartability.IsStartableAsync(_db, dto.FlowKey))
            throw new InvalidOperationException("E-WF-034");
        if (!string.IsNullOrWhiteSpace(dto.ConditionJson))
        {
            List<ApprovalService.BindingRule>? rules;
            try { rules = System.Text.Json.JsonSerializer.Deserialize<List<ApprovalService.BindingRule>>(
                      dto.ConditionJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch { throw new InvalidOperationException("E-WF-032"); }
            if (rules is null || rules.Count == 0) throw new InvalidOperationException("E-WF-032");
            foreach (var r in rules)
            {
                if (!ExpressionEvaluator.ValidateSyntax(r.When)) throw new InvalidOperationException("E-WF-032");
                if (string.IsNullOrWhiteSpace(r.FlowKey) || !await FlowStartability.IsStartableAsync(_db, r.FlowKey!))
                    throw new InvalidOperationException("E-WF-034");
            }
        }
        // upsert：dto.Id null=新建，否则更新五列（BizType/FlowKey/DetailRoute/Enable/ConditionJson/Remark）
        ...
    }

    public async Task DeleteAsync(Guid id)
    {
        var b = await _db.Wf_ApprovalBindings.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("绑定不存在");
        // 守卫（spec §3.1）：曾被任何实例引用（不限在途）即禁删——保历史深链
        if (await _db.Wf_FlowInstances.AnyAsync(i => i.BizType == b.BizType))
            throw new InvalidOperationException("E-WF-035");
        _db.Wf_ApprovalBindings.Remove(b);
        await _db.SaveChangesAsync();
    }

    public Task<SimulateResult> SimulateAsync(BindingDto dto, string sampleJson)
    {
        var tmp = new Wf_ApprovalBinding { BizType = dto.BizType, FlowKey = dto.FlowKey, ConditionJson = dto.ConditionJson };
        try
        {
            var key = ApprovalService.ResolveFlowKey(tmp, sampleJson);   // 与运行时同一条代码路径——模拟即真
            var idx = key == dto.FlowKey ? -1 : IndexOfRule(dto.ConditionJson!, key);
            return Task.FromResult(new SimulateResult(true, key, idx, null));
        }
        catch (InvalidOperationException e)
        { return Task.FromResult(new SimulateResult(false, null, -1, e.Message)); }
    }
```

（`IndexOfRule` = 反序列化后 FindIndex(r => r.FlowKey == key)。首中即选语义下若多规则同 flowKey 返回首个，可接受。）

- [ ] **Step 4: FlowAdminController 追加端点**（路由 `bindings`；guard 照该控制器既有写法）

```csharp
    [HttpGet("bindings")]           public async Task<IActionResult> Bindings() => ...列表...
    [HttpPost("bindings")]          public async Task<IActionResult> SaveBinding([FromBody] BindingDto dto) => ...
    [HttpDelete("bindings/{id:guid}")] public async Task<IActionResult> DeleteBinding(Guid id) => ...
    [HttpPost("bindings/simulate")] public async Task<IActionResult> Simulate([FromBody] SimulateReq r) => ...
    public record SimulateReq(BindingDto Dto, string SampleJson);
```

错误映射沿用该控制器现有 catch 惯例（InvalidOperationException → 400 + message）。

- [ ] **Step 5: 跑测试** — 9 PASS + 全量绿。
- [ ] **Step 6: Commit + push**

```bash
git add CP6.Core CP6.WebApi CP6.Tests
git commit -m "feat(oa-approval): 绑定管理——保存校验/删除守卫 E-WF-035/模拟求值(与运行时同路径)"
git push
```

---

### Task A6: Schema 快照落库与详情回显（旧单错位修复）

**Files:**
- Modify: `CP6.Core/Services/Wf/FormService.cs:57-78`（SubmitDataAsync）
- Modify: `CP6.Core/Services/Oa/InboxService.cs:219-253`（DetailAsync）
- Test: `CP6.Tests/Wf/FormSchemaSnapshotTests.cs`

- [ ] **Step 1: 核实 FormData↔实例 关联键**

Run: `grep -rn "SubmitDataAsync" CP6.Core CP6.WebApi --include=*.cs`
Expected: 找到调用点（FormController/DraftService 一带），确认传入的 `bizId` 实参是什么（预期=流程实例 Id 字符串）。**把找到的事实写进本任务 commit message**。若关联键不是实例 Id，DetailAsync 的快照查询按真实关联改写（快照仍在 Wf_FormData 上，只换查询键）。

- [ ] **Step 2: 写失败测试**

```csharp
// CP6.Tests/Wf/FormSchemaSnapshotTests.cs —— [Fact] 清单：
// 1 Submit_StampsSchemaSnapshot     SubmitDataAsync 后该行 SchemaSnapshotJson == 提交时 def.SchemaJson
// 2 Detail_PrefersSnapshot          改版 FormDef.SchemaJson 后 DetailAsync 的 FormSchemaJson 仍 == 旧快照
// 3 Detail_LegacyNullFallsBack      快照置 null（存量老单）→ FormSchemaJson == 当前 FormDef.SchemaJson
```

- [ ] **Step 3: 实现**。FormService.SubmitDataAsync 的 `new Wf_FormData{...}` 初始化器加一行：

```csharp
            SchemaSnapshotJson = def.SchemaJson,   // spec §6.1：提交时定格 schema，防改版后旧单错位
```

InboxService.DetailAsync 的 formSchema 取值（原 224-225 行）改为：

```csharp
        var snapshotSchema = await _db.Wf_FormDatas
            .Where(f => f.BizId == instanceId.ToString())
            .OrderByDescending(f => f.CreateDate)
            .Select(f => f.SchemaSnapshotJson)
            .FirstOrDefaultAsync();
        var formSchema = snapshotSchema ?? (def == null ? null
            : (await _db.Wf_FormDefs.FirstOrDefaultAsync(fd => fd.FormKey == def.FormKey))?.SchemaJson);
```

（查询键按 Step 1 核实结果为准。）

- [ ] **Step 4: 跑测试** — 3 PASS + 全量绿（既有 `FormServiceTests` 5 用例、`FormRuleRecomputeTests` 6 用例必须不回归）。
- [ ] **Step 5: Commit + push**

```bash
git add CP6.Core CP6.Tests
git commit -m "fix(oa-form): 提交定格 SchemaSnapshotJson,详情优先快照——旧单渲染错位修复(spec §6.1)"
git push
```

---

### Task A7: 收件箱行下发 detailRoute

**Files:**
- Modify: `CP6.Core/Services/Oa/InboxModels.cs`（三个列表 record 加可选尾参）
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（Pending/Running/Done 投影）
- Test: `CP6.Tests/Oa/InboxDetailRouteTests.cs`

**Interfaces:**
- Produces: `InboxPendingItem.DetailRoute: string?`、`InboxRunningItem.DetailRoute: string?`、`InboxDoneItem.DetailRoute: string?`（record 位置参数**尾部追加、带默认值 null**——不破坏既有构造调用）。B4 依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/InboxDetailRouteTests.cs：
// 1 Pending_BizRow_GetsRenderedRoute   绑定 DetailRoute="/pur/orders/{bizId}"、实例 BizId="PO-1"
//                                       → item.DetailRoute == "/pur/orders/PO-1"
// 2 Pending_SfsRow_NullRoute           实例 BizType=null → DetailRoute=null
// 3 DisabledBinding_StillRoutes        绑定 Enable=false → DetailRoute 照常下发（Enable 只封发起,spec §3.1）
// 4 Running_And_Done_AlsoRouted        另两列表同断言
```

- [ ] **Step 2: 确认失败 → Step 3: 实现**。三个 record 尾部各加 `string? DetailRoute = null`。InboxService 三个查询方法各自在投影前加载路由字典并渲染（**注意查全部绑定不过滤 Enable**）：

```csharp
        var routeMap = await _db.Wf_ApprovalBindings
            .Where(b => b.DetailRoute != null)
            .ToDictionaryAsync(b => b.BizType, b => b.DetailRoute!);
        string? Route(string? bizType, string? bizId) =>
            bizType != null && bizId != null && routeMap.TryGetValue(bizType, out var tpl)
                ? tpl.Replace("{bizId}", Uri.EscapeDataString(bizId)) : null;
```

投影处传 `DetailRoute: Route(x.i.BizType, x.i.BizId)`（Running/Done 查询若当前投影未取 BizType/BizId，在其 Select 中补取——实例表上都有）。

- [ ] **Step 4: 跑测试** — 4 PASS + 全量绿。
- [ ] **Step 5: Commit + push**

```bash
git add CP6.Core CP6.Tests
git commit -m "feat(oa-approval): 收件箱三列表下发 detailRoute(停用绑定照下发,防在途变砖)"
git push
```

---

### Task B1: 前端 API 层 + useApproval composable

**Files:**
- Create: `cp6.web/src/types/oa/approval.ts`
- Create: `cp6.web/src/api/oa/approval.ts`
- Create: `cp6.web/src/composables/useApproval.ts`
- Test: `cp6.web/src/composables/useApproval.spec.ts`

**Interfaces:**
- Consumes: A4 端点（camelCase JSON）；写操作复用 `inboxApi.batch/sendBack` 与 `transfer` 既有 API。
- Produces（B2/B3/B6 依赖）: 见下方类型与 `useApproval` 返回形状。

- [ ] **Step 1: 类型**（`types/oa/approval.ts`，与 A4 DTO 逐字段对齐）

```ts
import type { TimelineRow, ForecastStep } from '@/types/oa/inbox'

/** 实例状态：-1=None 0=Running 1=Approved 2=Rejected 3=Withdrawn 4=Suspended 5=Draft */
export type ApprovalStatus = -1 | 0 | 1 | 2 | 3 | 4 | 5

export interface ApprovalPanelMyTask { taskId: string; nodeId: string; nodeName: string | null }

export interface ApprovalPanelData {
  instanceId: string | null
  status: ApprovalStatus
  currentNodeName: string | null
  starterId: string | null
  starterName: string | null
  myTask: ApprovalPanelMyTask | null
  timeline: TimelineRow[]
  forecast: ForecastStep[]
  canSubmit: boolean
}

export interface ApprovalCtx {
  status: ApprovalStatus
  myTask: ApprovalPanelMyTask | null
  isStarter: boolean
}

/** 动作描述符（spec §4.2）：引擎动词语义固定五个；业务动词面板只渲染+调 handler。 */
export interface ApprovalAction {
  key: string
  labelKey: string
  kind: 'engine' | 'business'
  engineVerb?: 'approve' | 'reject' | 'sendBack' | 'transfer' | 'revoke'
  appearance?: 'primary' | 'danger' | 'default'
  confirmText?: string
  commentRequired?: boolean
  when?: (ctx: ApprovalCtx) => boolean
  handler?: (ctx: ApprovalCtx) => Promise<void>
}
```

（若 `types/oa/inbox.ts` 未导出 `TimelineRow/ForecastStep`，在其中补导出——先打开确认。）

- [ ] **Step 2: API**（`api/oa/approval.ts`，风格照 `api/oa/inbox.ts`）

```ts
import http from '../http'

export const approvalApi = {
  detailByBiz: (bizType: string, bizId: string) =>
    http.get('/oa/approval/detail', { params: { bizType, bizId } }),
  detailByInstance: (instanceId: string) =>
    http.get('/oa/approval/detail', { params: { instanceId } }),
  bindings: () => http.get('/oa/flow-admin/bindings'),
  saveBinding: (dto: unknown) => http.post('/oa/flow-admin/bindings', dto),
  deleteBinding: (id: string) => http.delete(`/oa/flow-admin/bindings/${id}`),
  simulate: (dto: unknown, sampleJson: string) =>
    http.post('/oa/flow-admin/bindings/simulate', { dto, sampleJson }),
}
```

（`flow-admin` 路由前缀以 A5 实际路由为准——打开 FlowAdminController 核对 `[Route]`。撤回端点：`grep -rn "withdraw" cp6.web/src/api` 找现有 API；若在 taskCenter/query 族则 import 复用，勿新写。）

- [ ] **Step 3: 写 composable 失败测试**（vitest + vi.mock 两个 api 模块）

```ts
// useApproval.spec.ts —— it() 清单：
// 1 biz 模式加载：mock detailByBiz → state 填充（status/myTask/canSubmit）
// 2 instance 模式加载：mock detailByInstance 被调、detailByBiz 不被调
// 3 approve()：调 inboxApi.batch([taskId], true, comment) 后自动 refresh（detail 被再次拉取）
// 4 reject() 同理 approve=false
// 5 无 myTask 时 approve() 直接 no-op 不发请求
// 6 onDecided：refresh 后 status 变为终态(1/2/3) 时回调触发一次
```

- [ ] **Step 4: 实现 useApproval**

```ts
import { computed, ref } from 'vue'
import { approvalApi } from '@/api/oa/approval'
import { inboxApi } from '@/api/oa/inbox'
import type { ApprovalCtx, ApprovalPanelData } from '@/types/oa/approval'

export interface UseApprovalKey { bizType?: string; bizId?: string; instanceId?: string }

export function useApproval(key: UseApprovalKey, currentUserId?: string) {
  const data = ref<ApprovalPanelData | null>(null)
  const loading = ref(false)
  const decidedCbs: Array<(d: ApprovalPanelData) => void> = []

  async function refresh() {
    loading.value = true
    try {
      const res = key.instanceId
        ? await approvalApi.detailByInstance(key.instanceId)
        : await approvalApi.detailByBiz(key.bizType!, key.bizId!)
      const prev = data.value?.status
      data.value = (res as any).data as ApprovalPanelData
      const s = data.value.status
      if (prev === 0 && (s === 1 || s === 2 || s === 3)) decidedCbs.forEach((cb) => cb(data.value!))
    } finally { loading.value = false }
  }

  const ctx = computed<ApprovalCtx>(() => ({
    status: data.value?.status ?? -1,
    myTask: data.value?.myTask ?? null,
    isStarter: !!currentUserId && data.value?.starterId === currentUserId,
  }))

  async function act(approve: boolean, comment?: string) {
    const taskId = data.value?.myTask?.taskId
    if (!taskId) return
    await inboxApi.batch([taskId], approve, comment)
    await refresh()
  }

  return {
    data, loading, ctx, refresh,
    approve: (c?: string) => act(true, c),
    reject: (c?: string) => act(false, c),
    sendBack: async (kind: 'prevStage' | 'starter' | 'node', nodeId?: string, comment?: string) => {
      const taskId = data.value?.myTask?.taskId
      if (!taskId) return
      await inboxApi.sendBack(taskId, kind, nodeId, comment)
      await refresh()
    },
    onDecided: (cb: (d: ApprovalPanelData) => void) => decidedCbs.push(cb),
  }
}
```

（transfer/revoke 经对话框/独立 API 触发后调 `refresh()`，由 B2 面板层接线。）

- [ ] **Step 5: 跑测试** — `cd cp6.web && bun run test -- useApproval` 6 PASS；`bun run type-check` 0。
- [ ] **Step 6: Commit + push**

```bash
git add cp6.web/src/types/oa/approval.ts cp6.web/src/api/oa/approval.ts cp6.web/src/composables
git commit -m "feat(oa-approval): approval api + useApproval composable(双键/动作分发/onDecided)"
git push
```

---

### Task B2: ApprovalPanel 组件 + 对话框收编 + UI 词条

**Files:**
- Create: `cp6.web/src/components/approval/ApprovalPanel.vue`
- Move: `cp6.web/src/views/oa/inbox/TransferDialog.vue` → `cp6.web/src/components/approval/TransferDialog.vue`（git mv；SendBackDialog 同）
- Modify: `CP6.WebApi/Seed/I18nOaApprovalSeed.cs`（追加 UI 词条）
- Test: `cp6.web/src/components/approval/ApprovalPanel.spec.ts`

**Interfaces:**
- Consumes: B1 全部。
- Produces（B3/B6 用法契约）:

```
<ApprovalPanel biz-type="PO" :biz-id="id" :submit-handler="fn" :actions="a?" :current-user-id="uid" @decided="reload" />
<ApprovalPanel :instance-id="iid" :current-user-id="uid" @decided="..." />
```

- [ ] **Step 1: 写组件失败测试**（vitest + @vue/test-utils，mock useApproval 模块）

```ts
// ApprovalPanel.spec.ts —— it() 清单（四态矩阵 + 动作模型，spec §4.2/§4.3）：
// 1 无实例+canSubmit+有 submitHandler → 渲染提交按钮；点击调 handler 后 refresh
// 2 无实例+canSubmit+无 submitHandler → 不渲染提交按钮
// 3 Running+myTask → 渲染意见框+默认四动作（同意/驳回/退回/转办）
// 4 Running+无 myTask（旁观者）→ 只读状态条，无动作区
// 5 Suspended → 挂起提示行，无动作区（不掉 default）
// 6 Withdrawn+canSubmit → 提交按钮重新出现（卡死回归）
// 7 自定义 actions 完全接管：传 [{key:'cancel',kind:'engine',engineVerb:'revoke',...}] → 默认四件套不渲染
// 8 business 动作：handler 被调后 refresh 被调
// 9 commentRequired 动作在意见为空时禁用
// 10 timeline 有实例即渲染（FlowTimeline stub 存在性断言）
```

- [ ] **Step 2: 实现组件**。结构（完整实现按此骨架展开，样式用 Cp token）：

```vue
<template>
  <div class="approval-panel">
    <!-- 状态条：CpTag tone 按 status 映射（-1 无/0 info/1 success/2 danger/3 warning/4 warning/5 info） -->
    <!-- Suspended 提示行：t('oa.approval.suspendedHint') -->
    <!-- 提交区：canSubmit && submitHandler → 提交按钮（loading 态），成功后 refresh() -->
    <!-- 办理区：myTask 非空 → 意见框 + 动作按钮 v-for="a in effectiveActions"
         engine: approve/reject 直调 composable；sendBack/transfer 打开收编的对话框；revoke 走确认框+撤回 API
         business: a.handler(ctx) 后 refresh
         confirmText 存在 → ElMessageBox.confirm 先行；commentRequired && !comment → disabled -->
    <!-- 时间线：data && data.instanceId → <FlowTimeline :timeline :forecast /> -->
    <slot name="actions" :ctx="ctx" />   <!-- 极端定制出口 -->
  </div>
</template>

<script setup lang="ts">
// props: bizType?, bizId?, instanceId?, submitHandler?: () => Promise<void>,
//        actions?: ApprovalAction[], currentUserId?: string
// emit: (e: 'decided', d: ApprovalPanelData)
// 默认动作集（不传 actions 时）:
const DEFAULT_ACTIONS: ApprovalAction[] = [
  { key: 'approve',  labelKey: 'oa.detail.approve',  kind: 'engine', engineVerb: 'approve',  appearance: 'primary' },
  { key: 'reject',   labelKey: 'oa.detail.reject',   kind: 'engine', engineVerb: 'reject',   appearance: 'danger', commentRequired: false },
  { key: 'sendback', labelKey: 'oa.detail.sendback', kind: 'engine', engineVerb: 'sendBack', appearance: 'danger' },
  { key: 'transfer', labelKey: 'oa.detail.transfer', kind: 'engine', engineVerb: 'transfer', appearance: 'default' },
]
// effectiveActions = (props.actions ?? DEFAULT_ACTIONS).filter(a => !a.when || a.when(ctx.value))
// onDecided → emit('decided', d)
</script>
```

FlowTimeline import 自 `@/views/oa/inbox/FlowTimeline.vue`（不迁移它——它同时被别处使用，YAGNI）。对话框 git mv 后更新自身无需改（自包含），FormDetail 的旧 import 在 B3 一并处理。

- [ ] **Step 3: UI 词条追加进 I18nOaApprovalSeed**：`oa.approval.submit`（提交审批）/ `oa.approval.suspendedHint`（流程挂起待指派）/ `oa.approval.statusNone/Running/Approved/Rejected/Withdrawn/Suspended` / `oa.approval.revoke`（撤回）/ `oa.approval.revokeConfirm` —— 五语。

- [ ] **Step 4: 跑测试** — 10 PASS；type-check 0；`dotnet build`（种子改动）过。
- [ ] **Step 5: Commit + push**

```bash
git add cp6.web CP6.WebApi/Seed
git commit -m "feat(oa-approval): ApprovalPanel 全站唯一审批面(四态矩阵+动作描述符)+对话框收编+五语词条"
git push
```

---

### Task B3: FormDetail 换装 + rules 显隐修复

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`（大改）
- Test: `cp6.web/src/views/oa/inbox/FormDetail.spec.ts`（新建）

**Interfaces:**
- Consumes: B2 `<ApprovalPanel :instance-id>`；`applyRules` from `@/views/wf/ruleEngine`。

- [ ] **Step 1: 写失败测试**

```ts
// FormDetail.spec.ts：
// 1 rules 显隐：schema 含 rule（when:"kind == \"B\"" → hide fieldX），data kind="A" → fieldX 渲染；kind="B" → 不渲染
// 2 换装断言：不再存在 .action-bar 内的写死按钮（同意/驳回按钮选择器归 ApprovalPanel stub）
// 3 ApprovalPanel 收到 instance-id prop == props.instanceId
```

- [ ] **Step 2: 改造**。变更清单：
  1. 模板右列 `FlowTimeline + 手写 action-bar + 两对话框` 全部删除，替换为 `<ApprovalPanel :instance-id="instanceId" :current-user-id="meId" @decided="emit('done')" />`；CC 标签块保留在右列 Panel 之下。
  2. 脚本删 `myTaskId/myPendingItem/comment/acting/transferVisible/sendbackVisible/doAction/onTransferDone/onSendBackDone` 与 `inboxApi.pending()` 并联拉取（面板自己判 myTask）；删 TransferDialog/SendBackDialog import。
  3. rules 修复（spec §6.2）：

```ts
import { applyRules } from '@/views/wf/ruleEngine'

const parsedSchema = computed((): FormSchema => {
  const s = safeParseObject(detail.value?.formSchemaJson)
  return {
    fields: (Array.isArray(s.fields) ? s.fields : []) as FormFieldDef[],
    rules: Array.isArray(s.rules) ? s.rules : [],          // 不再丢弃（spec §6.2）
  }
})

/** 只读视图应用 visible 效果：条件隐藏字段不渲染；required/disabled 只读态无意义；compute 不重算 */
const visibleSchema = computed((): FormSchema => {
  const effects = applyRules(parsedSchema.value as any, { ...formData.value })   // 拷贝防 compute 写回污染展示数据
  return {
    fields: parsedSchema.value.fields.filter((f) => effects[f.name]?.visible !== false),
  }
})
```

模板 DynamicForm 改喂 `visibleSchema`，mask 同步按 `visibleSchema.fields` 构建。（`FormSchema` 类型若无 `rules` 字段，在 `types/wf/wf.ts` 补可选 `rules?: unknown[]`。`applyRules` 第二参会被 compute 写回——传拷贝。）
  4. `meId` 取当前用户 id：照项目里现有取法（`grep -rn "userStore\|currentUser" cp6.web/src/views/oa/inbox/InboxView.vue` 找同模块惯例照抄）。

- [ ] **Step 3: 跑测试** — 新 3 PASS + 前端全量 369+ 绿 + type-check 0。
- [ ] **Step 4: Commit + push**

```bash
git add cp6.web
git commit -m "refactor(oa-inbox): FormDetail 换装 ApprovalPanel(审批面归 WFS)+rules 显隐修复(spec §6.2)"
git push
```

---

### Task B4: 收件箱深链

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/InboxView.vue`（openDetail 分流）
- Modify: `cp6.web/src/views/oa/inbox/{InboxPending,InboxRunning,InboxDone}.vue`（事件带 detailRoute）
- Test: `cp6.web/src/views/oa/inbox/inboxDeepLink.spec.ts`

- [ ] **Step 1: 写失败测试**（挂 stub router）

```ts
// 1 行带 detailRoute → openDetail 触发 router.push('/pur/orders/PO-1')，抽屉不开
// 2 行无 detailRoute → 抽屉开（现状路径回归）
```

- [ ] **Step 2: 实现**。三个列表组件把 `emit('open-detail', row.instanceId)` 改为 `emit('open-detail', row.instanceId, row.detailRoute ?? null)`（各文件精确找到点击处改签名）。InboxView.vue：

```ts
import { useRouter } from 'vue-router'
const router = useRouter()
function openDetail(id: string, detailRoute?: string | null) {
  if (detailRoute) { router.push(detailRoute); return }   // 业务单据 → 深链业务页（spec §5）
  detail.id = id                                           // SFS → 现状抽屉
}
```

- [ ] **Step 3: 跑测试** — 2 PASS + 全量绿 + type-check 0。
- [ ] **Step 4: Commit + push**

```bash
git add cp6.web
git commit -m "feat(oa-inbox): 待办深链——detailRoute 跳业务页,SFS 走现状抽屉"
git push
```

---

### Task B5: FlowAdmin 绑定管理界面

**Files:**
- Create: `cp6.web/src/views/oa/admin/ApprovalBindingAdmin.vue`
- Modify: `cp6.web/src/views/oa/admin/FlowAdmin.vue`（#actions 加「审批绑定」按钮开抽屉挂载新组件——复用 FlowAdmin 页面权限，不加新菜单）
- Modify: `CP6.WebApi/Seed/I18nOaApprovalSeed.cs`（追加管理界面词条）

- [ ] **Step 1: 实现 ApprovalBindingAdmin.vue**。功能块（用 Cp 组件族 + el-table，照 FlowAdmin.vue 现有表格写法）：
  - 列表（approvalApi.bindings）：BizType / FlowKey / DetailRoute / Enable 开关 / 规则数 / 操作（编辑/删除——删除失败 E-WF-035 时 ElMessage 提示"改为停用"）。
  - 编辑对话框：BizType、主 FlowKey（下拉，选项来自 flowAdminApi.list() 过滤启用）、DetailRoute、Enable、条件规则行编辑（when 输入 + flowKey 下拉 + 上移/下移/删行）、Remark。
  - 模拟求值折叠区：sample JSON textarea + 「模拟」按钮 → 显示 `命中规则 #idx → flowKey` 或错误码。
- [ ] **Step 2: FlowAdmin.vue #actions 加按钮 + el-drawer 挂载**。
- [ ] **Step 3: 词条**：`oa.approval.binding.*`（title/bizType/mainFlow/detailRoute/rules/simulate/sampleJson/deleteBlocked 等）五语入种子。
- [ ] **Step 4: 手动冒烟**（dev server）：建绑定→坏表达式被拒→模拟求值命中→删除被 E-WF-035 拦。type-check 0 + 全量测试绿。
- [ ] **Step 5: Commit + push**

```bash
git add cp6.web CP6.WebApi/Seed
git commit -m "feat(oa-approval): FlowAdmin 审批绑定管理(条件规则编辑+模拟求值+删除守卫提示)"
git push
```

---

### Task B6: 三个已接模块前端换装（首批用户兼验收）

**Files:**（先发现后改，每模块一小步）
- Modify: 采购/财务凭证/预算的单据详情页（发现步确定精确路径）

- [ ] **Step 1: 发现**

Run: `grep -rn "BizType" CP6.Core/Services/Pur/PurApprovalCallback.cs CP6.Core/Services/Fin/JournalApprovalCallback.cs CP6.Core/Services/Fin/BudgetApprovalCallback.cs`
Expected: 三个 bizType 常量字符串。
Run: `grep -rln "<三个bizType各自的提交API关键词>" cp6.web/src/views/pur cp6.web/src/views/fin`
Expected: 三个详情/列表页，各自现有"提交审批/审批状态"散装 UI。

- [ ] **Step 2: 每模块换装**（同一模式，逐模块独立 commit）：详情页删散装审批状态/按钮，放

```vue
<ApprovalPanel :biz-type="BIZ_TYPE" :biz-id="String(row.id)" :submit-handler="submitForApproval"
               :current-user-id="meId" @decided="reload" />
```

`submitForApproval` = 该模块**现有**提交端点封装（不新建端点；快照由后端从已落库单据构建——黄金模板第 2/3 条）。若某模块页面现状只有列表无详情页，把 Panel 放进其现有审批入口弹窗，保持最小改动。

- [ ] **Step 3: 每模块**：type-check + 全量前端测试绿 → commit + push（`feat(pur|fin): XX 单据换装 ApprovalPanel`）。同时为 FlowAdmin 里这三个 bizType 的绑定补 `DetailRoute`（SQL 或管理界面），使收件箱深链生效。

---

### Task C1: 接入黄金模板文档 + 跨 spec 前置注记

**Files:**
- Create: `docs/oa/11-approval-integration.md`（体例照 docs/oa 丛书；内容 = spec §8 checklist 展开成手册：Callback 铁律全文、SubmitAsync 契约、snapshot 字段即契约警告、submit 前持久化、绑定配置步骤、ApprovalPanel 双模式与动作描述符示例、B6 三模块作为参考实现链接）
- Modify: `docs/superpowers/plans/2026-07-05-wfs-ringi-print.md`（头部加一行前置注记：字段表格渲染须复用 FormDetail 的"快照+applyRules"只读投影，见本包 spec §6.2）
- Modify: `docs/superpowers/plans/2026-07-05-wfs-inbox-ux.md`（头部加一行：X 波以换装 ApprovalPanel 后的 FormDetail 为基线，本包已先行）

- [ ] Step 1 写文档 → Step 2 两个 plan 头部注记 → Step 3 commit + push

```bash
git add docs
git commit -m "docs(oa): 审批接入黄金模板(11-approval-integration)+打印/信箱两 plan 前置注记"
git push
```

---

### Task C2: DoD 全量闸 + 真库 QA

- [ ] **Step 1: 全量回归**

Run: `dotnet test` → 期望 ≥1565+本包新增全绿；`cd cp6.web && bun run test` → ≥369+新增全绿；`bun run type-check`（NODE_OPTIONS=--max-old-space-size=8192）→ 0；`bun run build` 过。

- [ ] **Step 2: 迁移应用真库**：对 dev 库跑 `dotnet ef database update --project CP6.Core --startup-project CP6.WebApi`，确认两列落库。
- [ ] **Step 3: 真库 QA 剧本**（HTTP e2e，照 WFS ServiceTask E-T3 harness 模式；**live 浏览器走查需用户在场，单独约**）：
  1. FlowAdmin 建 PO 绑定（条件规则 amount>100000→高链）+ DetailRoute。
  2. 业务端点提交 amount=200000 → 实例 FlowKey=高链（条件命中）。
  3. 无关用户 GET /api/oa/approval/detail → 403；办理人 → myTask 非空。
  4. 收件箱 pending 行含 detailRoute；办理→回调落库。
  5. SFS：改版表单后旧单详情 schema == 快照（字段不错位）；含 hide 规则的表单，审批人视图不见隐藏字段。
  6. 停用绑定 → 提交 400 E-WF-031，但在途待办仍可办、深链仍在。
- [ ] **Step 4: 台账**：`.superpowers/sdd/progress.md` 记录各任务行；memory 更新。最终 commit + push。

---

## Self-Review 记录（plan 完成时自查）

- spec 覆盖：§3.1 条件选流程+生命周期(A3/A5/A7)、§3.2 管理 UI(A5/B5)、§3.3 聚合+授权(A4)、§4.1-4.3 套件(B1/B2)、§4.4 FormDetail(B3)、§5 深链(A7/B4)、§6.1/6.2 修复(A6/B3)、§7 错误码(A3/A5 种子)、§8 黄金模板(C1)、§9 测试(各任务内嵌+C2)、§11 串行注记(C1)。无缺口。
- 类型一致：ApprovalPanelDto/ApprovalPanelData 字段逐一对齐；BindingRule 在 A3 定义、A5 复用；FlowStartability 在 A3 定义、A3/A5 两处消费。
- 已知实现期核实点（各任务内已写死指令）：Wf_FlowDef.Enable 实名（A3）、FormData 关联键（A6 Step 1）、flow-admin 路由前缀（B1）、撤回 API 位置（B1）、当前用户 id 取法（B3）。
