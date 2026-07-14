# WFS 信箱体验增强 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **每个 Task 执行前必读对应 spec 章节**(`docs/superpowers/specs/2026-07-05-wfs-inbox-ux-design.md`)。本计划所有产品代码 / 测试代码在任务内逐条给全，不引用「见 spec」以外的外部内容。

**Goal:** 给 WFS 电子表单信箱加四项体验增强——①通知设定（用户级 类型×通道 开关矩阵，PrefsJson 零迁移）②在途批量转单（管理员批量改派，逐条独立事务+汇总报告）③移动端响应式（信箱列表/详情/操作栏，<768px）④同单多状态 rowMode merged|expanded 显示偏好（后端查询层分组）。

**Architecture:** 通知矩阵 = 纯函数 `NotifyMatrix.IsEnabled(prefsJson, type, channel)` 三态坍缩（缺行/缺键/缺通道键→true）+ 遗留扁平键兼容，`PersistentWfNotifier` 每方法逐收件人×逐通道查偏好（`IPrefService.IsEnabledAsync`，per-request 内存缓存）；类型轴反射 `WfNotificationType` 常量数据驱动（hardening 的 `BranchPruned` 合入即自动长出矩阵行）。批量转单 = `InboxService.BatchTransferAsync` 逐条调引擎 `TransferAsync`（引擎内部单次 SaveChanges = 单条独立事务），仿既有 `ActBatchAsAsync` 循环口径；权限走 `[RequirePermission("oa-inbox","batch-transfer")]` + Program.cs seed。移动端 = 既有 `useBreakpoint().isMobile` 模板切换（表格→卡片，仿 `StockDwellView.vue`）+ 各页 `<style>` 尾部 `@media (max-width: 767px)` 块（对齐 UI 翻新做法）。rowMode = `PendingAsync` 加 `rowMode/page/pageSize` 参数，merged 按 InstanceId 分组取最新（仿 `DoneAsync` 既有合并口径），**分组先于分页**。

**Tech Stack:** .NET 8 / EF Core（InMemory 测试，仿 `PrefServiceTests`/`InboxServiceTests`）/ xUnit（`CP6.Tests/Oa`）/ Vue3 + Element Plus + Design System v1.0 tokens（`cp6.web/src/views/oa`）/ vitest。

---

## Global Constraints（每个 Task 隐含遵守）

- **测试基线**：后端 `dotnet test CP6.Tests/CP6.Tests.csproj` 1509 通过（5 skip）→ +N 全绿，既有测试零回归；前端 `npm run test` 320 通过 → +N 全绿；`npm run type-check` 大堆参数照常通过；`npm run build` 通过。
- **零 EF 迁移**：本计划**零实体/DbSet/索引改动**（`Wf_InboxPref.PrefsJson` 自由结构承载全部新偏好键）。DoD 跑 `dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` 必须 clean。
- **零跨模块污染**：只碰 `CP6.Core/Services/Oa`、`CP6.Core/Services/Wf`（只读引用，**不改 `TransferAsync` 等引擎动作**）、`CP6.WebApi`（Controllers/Oa、Services、Seed、Program.cs 定点块）、`cp6.web/src/views/oa/**`、`cp6.web/src/api/oa/**`、`cp6.web/src/types/oa/**`。不碰 Space/WMS/ERP/FIN 任何文件。每 Task 完成 `git show --stat` 复核。
- **五语 i18n**：全部新 UI 文案走 `t('...')` 运行时键；键在 E-T1 一次性 seed（ZhCN/ZhTW/En/Ja/Ko 五列，`Sys_Lang`）。后端业务错误抛 i18n 键字符串（前端 http 拦截器 `t(raw)` 自动本地化，`http.ts:92-95` 既有口径）。
- **零硬编码色**：新增 CSS 一律 Design System v1.0 token（`--cp-*`，`cp6.web/src/styles/tokens.css`）。
- **桌面端像素零回归**：全部移动端适配走 `isMobile` 模板分支或 `@media (max-width: 767px)` 尾部块，≥768px 渲染路径与现状字节等价（QA 双端走查）。
- **审批人策略勿碰**：`ApproverResolver.cs` / `NodePropertyPanel.vue` 审批人段已完成，本计划零接触。
- **提交纪律**：TDD（先失败测试→最小实现→绿→commit）；提交信息 `feat(wfs-inbox): <任务号> <中文描述>`；**只本地 commit 不 push**。

---

## 侦察结论（2026-07-05 实读，各任务代码以此为准）

### R1 通知栈现状

- `WfNotificationType`（`CP6.Entity/DomainModels/Wf/WfNotificationType.cs`）**实际值域 4 个 const int**：`TodoCreated=1, FlowApproved=2, FlowRejected=3, Timeout=4`。`BranchPruned` **尚未合入**（hardening spec §4.2 同期新增）。
- `IWfNotifier`（`CP6.Core/Services/Wf/IWfNotifier.cs`）只有 3 个方法：`TodoCreatedAsync / FlowApprovedAsync / FlowRejectedAsync`。**没有 TimeoutAsync**。
- **邮件动作清单**（矩阵格子禁用依据）：`PersistentWfNotifier`（`CP6.WebApi/Services/PersistentWfNotifier.cs`）3 个方法都有 `TrySendEmailAsync` 邮件动作 → **todoCreated / flowApproved / flowRejected 双通道有效**；`Timeout(4)` **全库无生产者**（`WfTimeoutService.ScanOnceAsync` 的 remind/escalate 均调 `TodoCreatedAsync`，以 Type=1 发出）→ **timeout 行 inApp+email 双格子禁用**（带提示，数据驱动，将来接独立发送路径自动点亮）。
- **既有偏好机制**（关键）：`IPrefService.GetNotifyPrefsAsync` → `PrefService.ParseNotifyPrefs`（`CP6.Core/Services/Oa/PrefService.cs:38-62`）已解析 `PrefsJson.notify` 键，但是**扁平形态** `{"notify":{"todo":bool,"approved":bool,"rejected":bool,"timeout":bool,"email":bool}}`——事件开关 + 单一全局 email 开关，非矩阵。`notify` 键已被占用。

### R2 InboxPref 并发口径（spec §6 核实项，结论）

- `Wf_InboxPref : BaseTenantEntity`（`CP6.Entity/DomainModels/Wf/Wf_InboxPref.cs`）——继承链 `BaseTenantEntity : BaseEntity`，**无 RowVersion**（RowVersion 只在 `BaseBizEntity` 上，本表不继承）。
- `PrefService.SaveAsync` 是**整串覆盖**（无合并）；合并目前全在前端 `InboxSettings.vue` 的 `storedRaw` spread。无并发控制，last-write-wins。
- **本计划口径**：零迁移约束下**不加 RowVersion**；新增**服务端顶层键合并写** `SaveMergeAsync`（读-改-写在单请求单 SaveChanges 内完成），把跨会话键覆盖窗口收敛到毫秒级；单用户自改冲突概率可忽略（spec §6 原话），文档化 last-write-wins per top-level key。

### R3 转交引擎与批量口径

- `IFlowEngine.TransferAsync`（`CP6.Core/Services/Wf/IFlowEngine.cs:54`，实现 `AdvancedFlow.cs:78-98`）：`Task TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)`。校验 task Pending / 实例 Running / to 存在（**租户全局过滤器保证同租户：跨租户查不到 = E-WF-002**）且 ≠ 当前 assignee；改 `task.AssigneeId`、FormTo 双行（原行→Transferred + 新 Pending 行，from/to 审计在此）、`AddHistory(instId, nodeId, actorId, "transfer", comment)`、`TodoCreatedAsync` 通知新受让人、**末尾单次 `SaveChangesAsync` = 事务边界**。失败一律抛 `InvalidOperationException("E-WF-002")`。→ 批量逐条调它 = 逐条独立事务，天然满足 D3。
- 批量循环精确先例：`InboxService.ActBatchAsAsync`（`InboxService.cs:199-217`）——`foreach taskIds.Distinct()` + 前置校验 + try/catch `InvalidOperationException` 收集明细。
- 审计免费：`OperLogFilter`（全局，POST 自动记操作者/请求体/租户）+ `Wf_FlowHistory`（ActorId+action=transfer）+ `Wf_FlowFormTo`（from/to 对）。**无需新审计代码**。
- `TransferAsync` **不校验 to.Enable**（只查存在）→ spec「to 停用 → 400」由批量服务层前置校验补。

### R4 权限机制（spec 名映射）

- 全库**无** `OA.Inbox.BatchTransfer` 式点分常量；实际机制 = `[RequirePermission(menu, action)]`（`CP6.Core/Auth/RequirePermissionAttribute.cs`，menu=`Sys_Menu.MenuKey`，`/oa/inbox`→`oa-inbox`）+ `Sys_MenuAction`/`Sys_RoleAction` seed。**映射：spec `OA.Inbox.BatchTransfer` → `[RequirePermission("oa-inbox", "batch-transfer")]`**。
- `PermissionService.HasActionAsync` **无 admin 旁路**（Program.cs:1121 注释）→ 必须 seed 动作点 + 授 `RoleId=1`，否则 admin 也 403。OA 菜单 733（`/oa/inbox`）已 seed，但 OA 目前零动作点（本计划为首个）。

### R5 rowMode / 列表查询现状

- `InboxService.PendingAsync`（`InboxService.cs:19-65`）**现状 = 每任务一行（即 expanded 形态）**，无分组无分页；`DoneAsync`（:142-147, :169-171）已有「`GroupBy(InstanceId).OrderByDescending.First()` 取最新」合并口径——merged 模式照抄此口径。
- 信箱列表端点均无分页参数；设置页 `pageSize` 偏好存在但列表未消费。rowMode 分页正确性通过服务层可选 `page/pageSize` 参数落地（分组先于 Skip/Take），前端列表暂维持全量拉取（现状），参数供测试与后续消费。

### R6 前端现状

- 三页：`InboxView.vue`（壳：header + `el-aside 200px` 菜单 + `el-drawer size=60%` 详情）/ `InboxPending.vue`（el-table + batch-bar）/ `FormDetail.vue`（`el-col :span=14/10` 左表单右时间线 + `.action-bar` 底部按钮排）。**无独立「Sign Records 弹窗」**——签核记录 = 右栏 `FlowTimeline` 内联（移动端处理为纵向堆叠 + Transfer/SendBack 对话框全屏化）。
- 移动端先例：`useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`MOBILE_MAX=767`）+ `v-if="!isMobile"` 表格 / `v-else .mobile-list` 卡片（`StockDwellView.vue:116-170` + `:402-458` CSS）+ 尾部 `@media (max-width: 767px)`。断点 `<768px` = `max-width: 767px`，与既有约定一致。
- 设置页 `InboxSettings.vue` 已有 notify tab（扁平开关堆，:46-73）→ 替换为矩阵卡片。
- i18n seed：`CP6.WebApi/Seed/I18nOa*ScreenSeed.cs`（`Sys_Lang[] Items`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；Program.cs concat 链 :1813-1819，尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重；新 seed 插 :1819 之后。

### Spec 与现状冲突登记（**不改 spec**，实现取向如下）

| # | 冲突 | 实现取向 |
|---|------|---------|
| C1 | spec §2.1 示例键 `taskArrived` vs 实际枚举 `TodoCreated` | spec 自注「示意，按实际枚举对齐」→ 类型键 = camelCase 枚举名：`todoCreated/flowApproved/flowRejected/timeout`（+`branchPruned` 若合入） |
| C2 | spec D2「notify 新键零迁移」 vs `notify` 键已被**扁平形态**占用（含用户已存的 `todo:false` 等） | `IsEnabled` 在类型键非对象时回落解析遗留扁平键（事件关→双通道关；`email:false`→仅邮件关），**语义与现状逐位等价** = D2「向后兼容零数据迁移」的落实；矩阵 UI 保存后写新嵌套形态整体替换 `notify` 键 |
| C3 | spec §2.1 示例含 timeout 行 email 开关 vs Timeout **无任何发送路径**（含邮件） | timeout 行保留（类型轴=枚举值域）但 inApp+email 双格禁用 + 提示（spec §2.3 授权 plan 核定格子有效性） |
| C4 | spec §3.1 `权限点 OA.Inbox.BatchTransfer` vs 实际机制 (menuKey, action) 二元组 | 映射为 `("oa-inbox", "batch-transfer")`，见 R4 |
| C5 | spec §5「merged=默认=现状」 vs `PendingAsync` 现状实为逐任务行 | 按 spec 文本执行：merged 为默认。行为差异仅限「同实例多待办同人」场景（并行分支/会签同人），QA 走查确认 |
| C6 | spec §4「Sign Records 弹窗全屏化」 vs 无独立签核弹窗 | 对应现状落点 = FlowTimeline 堆叠 + TransferDialog/SendBackDialog 移动端全屏（`width 100vw`） |
| C7 | spec §3.1 `filter.beforeUtc` vs 库内 `CreateDate` 为服务器本地时（`DateTime.Now` 全库惯例） | 参数名照 spec，直接与 `CreateDate` 比较；QA README 注明传服务器本地时刻 |
| C8 | spec「批量转单 UI 预览待转清单」但 §3.1 只定义了一个端点 | 补 `POST /api/oa/inbox/batch-transfer/preview`（同请求体、同权限点、只读），spec 未禁止、UI 必需 |

---

## File Structure（创建/修改清单）

**后端 `CP6.Core/Services/Oa`**
- Create `NotifyMatrix.cs` — 纯函数：`IsEnabled` 三态坍缩 + 遗留兼容 + 反射类型轴 `Rows()`。
- Modify `IPrefService.cs` / `PrefService.cs` — `IsEnabledAsync`（per-request 缓存）、`SaveMergeAsync`（顶层键合并写）、`GetRowModeAsync`。
- Modify `IInboxService.cs` / `InboxService.cs` — `PendingAsync` 加 rowMode/page/pageSize；新增 `BatchTransferAsync` / `BatchTransferPreviewAsync`。
- Modify `InboxModels.cs` — 批量转单 record 族。

**后端 `CP6.WebApi`**
- Modify `Services/PersistentWfNotifier.cs` — 三方法接矩阵偏好（逐收件人×逐通道）。
- Modify `Controllers/Oa/PrefController.cs` — `SavePrefReq` 加 `Merge`；`GET notify-matrix`。
- Modify `Controllers/Oa/InboxController.cs` — `pending` 加查询参数；`POST batch-transfer` + `POST batch-transfer/preview`（`[RequirePermission]`）。
- Modify `Program.cs` — 权限点 seed（733/batch-transfer + RoleId=1）；i18n seed concat。
- Create `Seed/I18nOaInboxUxScreenSeed.cs` — 五语 ~37 键。

**前端 `cp6.web/src`**
- Modify `api/oa/pref.ts` — `saveMerge` / `notifyMatrix`；Modify `api/oa/inbox.ts` — `pending` 参数化、`batchTransfer`、`batchTransferPreview`。
- Modify `types/oa/inbox.ts` — 批量转单类型。
- Create `views/oa/settings/notifyMatrixModel.ts`（+ `notifyMatrixModel.test.ts`）— 矩阵状态纯函数（vitest 可测核心）。
- Modify `views/oa/settings/InboxSettings.vue` — notify tab 换矩阵卡片；显示偏好保存走合并写。
- Create `views/oa/admin/BatchTransferDialog.vue`；Modify `views/oa/admin/FlowAdmin.vue` — 批量改派入口。
- Modify `views/oa/inbox/InboxView.vue` / `InboxPending.vue` / `InboxRunning.vue` / `InboxDone.vue` / `FormDetail.vue` / `TransferDialog.vue` / `SendBackDialog.vue` — 移动端适配 + rowMode 开关。
- Modify `views/oa/inbox/inboxModel.ts`（+ 既有 `inboxModel.test.ts` 加用例）— `parseRowMode`。

**测试 / QA**
- Create `CP6.Tests/Oa/NotifyMatrixTests.cs`、`PrefMergeTests.cs`、`PersistentWfNotifierTests.cs`、`BatchTransferTests.cs`、`PendingRowModeTests.cs`。
- Create `docs/superpowers/qa/wfs-inbox-ux/{README.md, seed.sql, qa_inbox_ux.ps1}`。

---

## 共享契约（所有 Task 用这些**精确**名字）

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";
    public static bool IsEnabled(string prefsJson, string type, string channel);
    public static IReadOnlyList<NotifyMatrixRow> Rows();
}

// IPrefService 新增
Task<bool> IsEnabledAsync(Guid userId, string type, string channel);  // per-request 缓存（Scoped 实例内字典）
Task SaveMergeAsync(Guid userId, string partialJson);                 // 顶层键合并；patch 值为 null → 删除该键
Task<string> GetRowModeAsync(Guid userId);                            // "merged" | "expanded"，缺省 merged

// IInboxService 变更/新增
Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);

// InboxModels.cs 新增（批量上限常量在 InboxService：private const int MaxBatchTransfer = 500;）
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);   // Sample = 前 10 条
```

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
export interface NotifyMatrixRow { typeKey: string; typeValue: number; inAppSupported: boolean; emailSupported: boolean }
export type MatrixState = Record<string, { inApp: boolean; email: boolean }>
export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState
export function toNotifyPatch(state: MatrixState): string        // → '{"notify":{...}}'

// cp6.web/src/views/oa/inbox/inboxModel.ts 新增
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded'
```

- 端点：`POST /api/oa/pref/save`（`SavePrefReq(string PrefsJson, bool Merge = false)`）、`GET /api/oa/pref/notify-matrix`、`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`、`POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`。
- 业务错误 i18n 键（不占 E-WF 码，走既有「message=键、前端 t(raw)」口径）：`oa.bt.errSameUser` / `oa.bt.errTargetInvalid` / `oa.bt.errTooMany` / `oa.pref.errBadJson`。
- 通知类型键（camelCase 枚举名）：`todoCreated` / `flowApproved` / `flowRejected` / `timeout` / （`branchPruned` 若枚举已合入）。

### 任务波次（spec §9）：**{X-A ‖ X-B ‖ X-C ‖ X-D} → X-E**

四波相互独立可并行（X-D 对 `PrefService.cs` 的追加与 X-A 不同方法，顺序执行零冲突）；X-E（i18n+QA+DoD）依赖前四波全部完成。

---

## Wave X-A — 通知设定

### Task A-T1: NotifyMatrix 纯函数（IsEnabled 三态坍缩 + 遗留兼容 + 反射类型轴）

**Files:**
- Create: `CP6.Core/Services/Oa/NotifyMatrix.cs`
- Test: `CP6.Tests/Oa/NotifyMatrixTests.cs`

**Interfaces:**
- Consumes: `WfNotificationType`（`CP6.Entity.DomainModels.Wf`，const int 反射）。
- Produces: `NotifyMatrix.IsEnabled(string prefsJson, string type, string channel)`、`NotifyMatrix.Rows()`、常量 `ChannelInApp="inApp"` / `ChannelEmail="email"`、`record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported)` —— A-T2/A-T3/A-T4 全依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/NotifyMatrixTests.cs
using CP6.Core.Services.Oa;
using Xunit;

namespace CP6.Tests.Oa;

public class NotifyMatrixTests
{
    // ── 三态坍缩：缺行/缺键/缺通道键 → true（spec §2.1）──
    [Theory]
    [InlineData("")]                                            // 空串（等价无行）
    [InlineData("{}")]                                          // 无 notify 键
    [InlineData("""{"notify":{}}""")]                           // notify 空对象（无类型键）
    [InlineData("""{"notify":{"todoCreated":{}}}""")]           // 类型对象存在但无通道键
    [InlineData("NOT_VALID_JSON{{{")]                           // 畸形 JSON 回落 true 不抛
    public void IsEnabled_ThreeStateCollapse_DefaultsTrue(string prefsJson)
    {
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelInApp));
        Assert.True(NotifyMatrix.IsEnabled(prefsJson, "todoCreated", NotifyMatrix.ChannelEmail));
    }

    [Fact]
    public void IsEnabled_NewMatrixShape_PerTypePerChannel()
    {
        const string json = """{"notify":{"flowRejected":{"inApp":true,"email":false},"todoCreated":{"inApp":false}}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "flowRejected", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));   // 缺通道键 → true
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp"));   // 缺类型键 → true
    }

    // ── 遗留扁平形态兼容（C2：既有 notify.{todo,...,email} 语义逐位等价）──
    [Fact]
    public void IsEnabled_LegacyFlat_EventOff_KillsBothChannels()
    {
        const string json = """{"notify":{"todo":false,"email":true}}""";
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));   // 现状：事件关 → 整跳（含邮件）
        Assert.True (NotifyMatrix.IsEnabled(json, "flowApproved", "inApp")); // 其他事件不受影响
    }

    [Fact]
    public void IsEnabled_LegacyFlat_GlobalEmailOff_KillsOnlyEmail()
    {
        const string json = """{"notify":{"todo":true,"approved":true,"email":false}}""";
        Assert.True (NotifyMatrix.IsEnabled(json, "todoCreated",  "inApp"));
        Assert.False(NotifyMatrix.IsEnabled(json, "todoCreated",  "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowApproved", "email"));
        Assert.False(NotifyMatrix.IsEnabled(json, "flowRejected", "email"));  // 缺 rejected 键也吃全局 email
    }

    [Fact]
    public void IsEnabled_NewShapeWinsOverLegacy_WhenTypeObjectPresent()
    {
        // 同一 notify 里新旧混存：类型键为对象 → 走新形态，无视遗留 email 全局开关
        const string json = """{"notify":{"email":false,"todoCreated":{"inApp":true,"email":true}}}""";
        Assert.True(NotifyMatrix.IsEnabled(json, "todoCreated", "email"));
    }

    // ── 类型轴（反射枚举，数据驱动）+ 邮件动作核定（R1）──
    [Fact]
    public void Rows_ReflectsEnum_WithSupportFlags()
    {
        var rows = NotifyMatrix.Rows();
        Assert.Contains(rows, r => r is { TypeKey: "todoCreated",  TypeValue: 1, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowApproved", TypeValue: 2, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "flowRejected", TypeValue: 3, InAppSupported: true,  EmailSupported: true });
        Assert.Contains(rows, r => r is { TypeKey: "timeout",      TypeValue: 4, InAppSupported: false, EmailSupported: false }); // 无发送路径（R1）
        // BranchPruned 未合入时不出现；合入后（hardening spec §4.2）自动长出且双通道 true——不对存在性做负断言，保证两 spec 任意合并顺序都绿
        foreach (var r in rows.Where(r => r.TypeKey == "branchPruned"))
        {
            Assert.True(r.InAppSupported);
            Assert.True(r.EmailSupported);
        }
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter NotifyMatrixTests`。预期：编译失败（`NotifyMatrix` 不存在）。

- [ ] **Step 3: 最小实现**

```csharp
// CP6.Core/Services/Oa/NotifyMatrix.cs
using System.Reflection;
using System.Text.Json;
using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Oa;

/// <summary>通知矩阵一行（类型轴 = WfNotificationType 反射；Supported 标志驱动 UI 格子禁用）。</summary>
public record NotifyMatrixRow(string TypeKey, int TypeValue, bool InAppSupported, bool EmailSupported);

/// <summary>
/// 通知偏好矩阵纯函数（wfs-inbox-ux §2）。
/// PrefsJson.notify 新形态：{"notify":{"todoCreated":{"inApp":bool,"email":bool},...}}。
/// 三态坍缩：无行/无 notify 键/无类型键/无通道键/解析失败 → true（默认全开，D2 零迁移）。
/// 遗留扁平形态（{"notify":{"todo":bool,...,"email":bool}}）兼容：类型键非对象时回落——
/// 事件键=false → 双通道关；全局 email=false → 仅邮件关（与既有 ParseNotifyPrefs 语义逐位等价）。
/// </summary>
public static class NotifyMatrix
{
    public const string ChannelInApp = "inApp";
    public const string ChannelEmail = "email";

    /// <summary>新类型键 → 遗留扁平键 映射（仅既有四类型有遗留形态）。</summary>
    private static readonly Dictionary<string, string> LegacyKeyMap = new()
    {
        ["todoCreated"] = "todo",
        ["flowApproved"] = "approved",
        ["flowRejected"] = "rejected",
        ["timeout"] = "timeout",
    };

    /// <summary>
    /// 通道支持清单（2026-07-05 实读核定，R1）：
    /// todoCreated/flowApproved/flowRejected = PersistentWfNotifier 三方法，站内+邮件双动作；
    /// timeout = 全库无生产者（超时提醒以 TodoCreated 发出）→ 双禁用；
    /// branchPruned = hardening spec §4.2 预留（合入 IWfNotifier.BranchPrunedAsync 即双通道生效）。
    /// 未登记的新类型默认 (inApp:true, email:false)——站内可开关、邮件保守禁用。
    /// </summary>
    private static readonly Dictionary<string, (bool InApp, bool Email)> Support = new()
    {
        ["todoCreated"]  = (true, true),
        ["flowApproved"] = (true, true),
        ["flowRejected"] = (true, true),
        ["timeout"]      = (false, false),
        ["branchPruned"] = (true, true),
    };

    public static bool IsEnabled(string prefsJson, string type, string channel)
    {
        if (string.IsNullOrWhiteSpace(prefsJson)) return true;
        try
        {
            using var doc = JsonDocument.Parse(prefsJson);
            if (!doc.RootElement.TryGetProperty("notify", out var notify) || notify.ValueKind != JsonValueKind.Object)
                return true;                                                      // 无 notify 键 → 默认开

            if (notify.TryGetProperty(type, out var typeEl) && typeEl.ValueKind == JsonValueKind.Object)
            {
                // 新矩阵形态：仅字面 false 为关；缺通道键/true/非布尔 → 开
                return !(typeEl.TryGetProperty(channel, out var ch) && ch.ValueKind == JsonValueKind.False);
            }

            // 遗留扁平形态回落（C2）
            if (!LegacyKeyMap.TryGetValue(type, out var legacyKey)) return true;  // 新类型无遗留形态 → 开
            var eventOn = !(notify.TryGetProperty(legacyKey, out var ev) && ev.ValueKind == JsonValueKind.False);
            if (channel == ChannelInApp) return eventOn;
            var emailOn = !(notify.TryGetProperty("email", out var em) && em.ValueKind == JsonValueKind.False);
            return eventOn && emailOn;                                            // 既有语义：事件关→整跳；email 关→仅邮件跳
        }
        catch (JsonException)
        {
            return true;                                                          // 畸形 JSON → 默认开（与 ParseNotifyPrefs 一致）
        }
    }

    /// <summary>类型轴 = 反射 WfNotificationType 全部 public const int（BranchPruned 合入即自动长出）。</summary>
    public static IReadOnlyList<NotifyMatrixRow> Rows() =>
        typeof(WfNotificationType)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f =>
            {
                var key = char.ToLowerInvariant(f.Name[0]) + f.Name[1..];         // TodoCreated → todoCreated
                var (inApp, email) = Support.TryGetValue(key, out var s) ? s : (true, false);
                return new NotifyMatrixRow(key, (int)f.GetRawConstantValue()!, inApp, email);
            })
            .OrderBy(r => r.TypeValue)
            .ToList();
}
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter NotifyMatrixTests`，预期全绿。

- [ ] **Step 5: 全量回归闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"    # 既有 Oa/Wf 照绿
git add -A && git commit -m "feat(wfs-inbox): A-T1 NotifyMatrix 三态坍缩+遗留扁平兼容+反射类型轴"
```

---

### Task A-T2: PrefService 矩阵读取（per-request 缓存）+ 服务端合并写 + PrefController 端点

**Files:**
- Modify: `CP6.Core/Services/Oa/IPrefService.cs`
- Modify: `CP6.Core/Services/Oa/PrefService.cs`
- Modify: `CP6.WebApi/Controllers/Oa/PrefController.cs`
- Test: `CP6.Tests/Oa/PrefMergeTests.cs`

**Interfaces:**
- Consumes: `NotifyMatrix.IsEnabled` / `NotifyMatrix.Rows()`（A-T1）。
- Produces: `Task<bool> IsEnabledAsync(Guid userId, string type, string channel)`、`Task SaveMergeAsync(Guid userId, string partialJson)`（patch 顶层键为 `null` → 删除该键）、`POST /api/oa/pref/save` 的 `SavePrefReq(string PrefsJson, bool Merge = false)`、`GET /api/oa/pref/notify-matrix` → `Ok2(NotifyMatrix.Rows())`。A-T3（notifier）与 A-T4/D-T2（前端）依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PrefMergeTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class PrefMergeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static PrefService Svc(CP6Context db) => new(db);

    // ── 合并写不覆盖他键（spec §7）──
    [Fact]
    public async Task SaveMerge_PatchesTopLevelKey_PreservesOthers()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");

        using var doc = JsonDocument.Parse(await Svc(db).GetAsync(me));
        Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());              // 他键保留
        Assert.False(doc.RootElement.GetProperty("notify").GetProperty("todo").GetBoolean());
        Assert.Equal("expanded", doc.RootElement.GetProperty("rowMode").GetString());       // 新键并入
    }

    [Fact]
    public async Task SaveMerge_ReplacesKeyWholesale_And_NullDeletesKey()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"pageSize":50,"notify":{"todo":false,"email":false}}""");

        await Svc(db).SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");  // 顶层键整体替换
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            var notify = doc.RootElement.GetProperty("notify");
            Assert.False(notify.TryGetProperty("todo", out _));                              // 旧扁平键被替换掉
            Assert.False(notify.GetProperty("todoCreated").GetProperty("inApp").GetBoolean());
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }

        await Svc(db).SaveMergeAsync(me, """{"notify":null}""");                             // 恢复默认 = 删键
        using (var doc = JsonDocument.Parse(await Svc(db).GetAsync(me)))
        {
            Assert.False(doc.RootElement.TryGetProperty("notify", out _));
            Assert.Equal(50, doc.RootElement.GetProperty("pageSize").GetInt32());
        }
    }

    [Fact]
    public async Task SaveMerge_NoRow_CreatesRow()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveMergeAsync(me, """{"rowMode":"expanded"}""");
        Assert.Equal(1, await db.Wf_InboxPrefs.CountAsync(p => p.UserId == me));
    }

    [Fact]
    public async Task SaveMerge_BadPatchJson_Throws_i18nKey()
    {
        using var db = NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Svc(db).SaveMergeAsync(Guid.NewGuid(), "NOT_JSON{{{"));
        Assert.Equal("oa.pref.errBadJson", ex.Message);
    }

    // ── IsEnabledAsync：查库 + per-request 缓存（缓存不跨请求，spec §7）──
    [Fact]
    public async Task IsEnabledAsync_ReadsMatrix_FromDb()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        await Svc(db).SaveAsync(me, """{"notify":{"flowRejected":{"email":false}}}""");
        var svc = Svc(db);
        Assert.True (await svc.IsEnabledAsync(me, "flowRejected", "inApp"));
        Assert.False(await svc.IsEnabledAsync(me, "flowRejected", "email"));
        Assert.True (await svc.IsEnabledAsync(Guid.NewGuid(), "flowRejected", "email"));   // 无行 → true
    }

    [Fact]
    public async Task IsEnabledAsync_CachesWithinInstance_NotAcrossInstances()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = "{}" });
        await db.SaveChangesAsync();

        var svc1 = Svc(db);                                             // 模拟请求 1（Scoped 实例）
        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 首查 → 缓存 "{}"

        var row = await db.Wf_InboxPrefs.SingleAsync(p => p.UserId == me);
        row.PrefsJson = """{"notify":{"todoCreated":{"inApp":false}}}""";
        await db.SaveChangesAsync();

        Assert.True(await svc1.IsEnabledAsync(me, "todoCreated", "inApp"));   // 同实例（同请求）：命中缓存，仍 true
        Assert.False(await Svc(db).IsEnabledAsync(me, "todoCreated", "inApp")); // 新实例（新请求）：读到新值
    }

    [Fact]
    public async Task SaveMerge_InvalidatesOwnCache()
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        var svc = Svc(db);
        Assert.True(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                 // 缓存默认 "{}"
        await svc.SaveMergeAsync(me, """{"notify":{"todoCreated":{"inApp":false}}}""");
        Assert.False(await svc.IsEnabledAsync(me, "todoCreated", "inApp"));                // 同实例保存后读到新值
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PrefMergeTests`。预期：编译失败（新方法不存在）。

- [ ] **Step 3: 实现服务层**

`IPrefService.cs` 追加（保留既有三方法与注释不动）：

```csharp
    /// <summary>矩阵偏好查询（wfs-inbox-ux §2.2）。逐收件人×逐通道；Scoped 实例内字典缓存（= per-request）。</summary>
    Task<bool> IsEnabledAsync(Guid userId, string type, string channel);

    /// <summary>顶层键合并写（wfs-inbox-ux §6）：读-改-写单次 SaveChanges；patch 键值为 null → 删除该键。
    /// patch 非法 JSON → InvalidOperationException("oa.pref.errBadJson")。</summary>
    Task SaveMergeAsync(Guid userId, string partialJson);

    /// <summary>rowMode 显示偏好（wfs-inbox-ux §5）："merged"（默认）| "expanded"。</summary>
    Task<string> GetRowModeAsync(Guid userId);
```

`PrefService.cs` 追加（`GetRowModeAsync` 在 D-T1 实现，此处先加接口成员会导致编译失败——**本任务一并给最小实现**，D-T1 只加测试与消费方）：

```csharp
    // ── wfs-inbox-ux：矩阵偏好 + 合并写 ────────────────────────────────────

    /// <summary>per-request 缓存：本服务 Scoped 注册（Program.cs:151），实例生命周期=单请求。</summary>
    private readonly Dictionary<Guid, string> _prefsCache = new();

    private async Task<string> GetCachedAsync(Guid userId)
    {
        if (_prefsCache.TryGetValue(userId, out var cached)) return cached;
        var json = await GetAsync(userId);
        _prefsCache[userId] = json;
        return json;
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(Guid userId, string type, string channel) =>
        NotifyMatrix.IsEnabled(await GetCachedAsync(userId), type, channel);

    /// <inheritdoc/>
    public async Task SaveMergeAsync(Guid userId, string partialJson)
    {
        System.Text.Json.Nodes.JsonObject patch;
        try
        {
            patch = System.Text.Json.Nodes.JsonNode.Parse(
                string.IsNullOrWhiteSpace(partialJson) ? "{}" : partialJson) as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidOperationException("oa.pref.errBadJson");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("oa.pref.errBadJson");
        }

        System.Text.Json.Nodes.JsonObject baseObj;
        try
        {
            baseObj = System.Text.Json.Nodes.JsonNode.Parse(await GetAsync(userId)) as System.Text.Json.Nodes.JsonObject
                      ?? new System.Text.Json.Nodes.JsonObject();
        }
        catch (JsonException)
        {
            baseObj = new System.Text.Json.Nodes.JsonObject();   // 库内畸形 → 以 patch 重建（与解析回落口径一致）
        }

        foreach (var kv in patch.ToList())
        {
            if (kv.Value is null) baseObj.Remove(kv.Key);                       // null → 删键（恢复默认）
            else baseObj[kv.Key] = kv.Value.DeepClone();                        // 顶层键整体替换
        }

        await SaveAsync(userId, baseObj.ToJsonString());
        _prefsCache.Remove(userId);                                             // 同请求内后续读取到新值
    }

    /// <inheritdoc/>
    public async Task<string> GetRowModeAsync(Guid userId)
    {
        try
        {
            using var doc = JsonDocument.Parse(await GetCachedAsync(userId));
            if (doc.RootElement.TryGetProperty("rowMode", out var el)
                && el.ValueKind == JsonValueKind.String && el.GetString() == "expanded")
                return "expanded";
        }
        catch (JsonException) { }
        return "merged";
    }
```

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PrefMergeTests`。

- [ ] **Step 5: 控制器端点**

`PrefController.cs`：`SavePrefReq` 换签名 + `Save` 分流 + 新 `NotifyMatrixRows` action（`Get`/`Ok2`/`Err` 既有帮手不动）：

```csharp
    // ── 保存偏好 ──

    /// <summary>Merge=false：整串覆盖（既有行为不变）；Merge=true：服务端顶层键合并写（wfs-inbox-ux §6）。</summary>
    public record SavePrefReq(string PrefsJson, bool Merge = false);

    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] SavePrefReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            if (r.Merge) await _pref.SaveMergeAsync(me, r.PrefsJson);
            else await _pref.SaveAsync(me, r.PrefsJson);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 通知矩阵元数据（类型轴 + 通道支持标志，驱动设置 UI 格子禁用）──

    [HttpGet("notify-matrix")]
    public IActionResult NotifyMatrixRows() => Ok2(NotifyMatrix.Rows());
```

（`SavePrefReq` 加默认参 `Merge = false`：既有前端只传 `{ prefsJson }`，JSON 绑定缺字段取默认 → **既有调用方零变化**。）

- [ ] **Step 6: 编译 + 回归闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter "PrefServiceTests|PrefMergeTests"   # 既有 PrefServiceTests 照绿（GetNotifyPrefsAsync 未动）
git add -A && git commit -m "feat(wfs-inbox): A-T2 PrefService 矩阵读取/合并写/rowMode + save Merge 分流 + notify-matrix 端点"
```

---

### Task A-T3: PersistentWfNotifier 接矩阵偏好（逐收件人 × 逐通道）

**Files:**
- Modify: `CP6.WebApi/Services/PersistentWfNotifier.cs`
- Test: `CP6.Tests/Oa/PersistentWfNotifierTests.cs`

**Interfaces:**
- Consumes: `IPrefService.IsEnabledAsync`（A-T2）、`NotifyMatrix.ChannelInApp/ChannelEmail`（A-T1）。
- Produces: 行为变更——`inApp=false` → 跳过该收件人的持久化+SignalR；`email=false` → 跳过该收件人的邮件。方法签名零变化（`IWfNotifier` 不动）。**不回溯**：既有 `Wf_Notification` 行不动（本来就只影响新发送）。

> 铁律沿袭（文件头注释①②③保留并更新③措辞）：持久化仅 Add 不 SaveChanges；SignalR/邮件 best-effort 各自吞异常；**新③：偏好按 收件人×类型×通道 独立生效（矩阵）**。
> `TodoCreatedAsync` 每次调用只有一个收件人，但引擎对多审批人会逐人调用——偏好天然逐收件人生效（spec §2.2 口径）。
> 若 hardening 的 `BranchPrunedAsync` 已合入本文件：同口径改造（type key `"branchPruned"`）；未合入：本任务不创建该方法（矩阵行由 A-T1 反射自动出现与否）。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PersistentWfNotifierTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Hubs;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CP6.Tests.Oa;

public class PersistentWfNotifierTests
{
    // ── 手写 fakes ──────────────────────────────────────────────────────────
    private sealed class RecordingNotif : INotificationService
    {
        public readonly List<(Guid UserId, int Type)> Created = new();
        public Task CreateAsync(Guid userId, int type, string title, string body, Guid? instanceId, Guid? taskId, string? flowKey)
        { Created.Add((userId, type)); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationItem>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize)
            => Task.FromResult<IReadOnlyList<NotificationItem>>(Array.Empty<NotificationItem>());
        public Task<int> UnreadCountAsync(Guid userId) => Task.FromResult(0);
        public Task MarkReadAsync(Guid userId, Guid id) => Task.CompletedTask;
        public Task MarkAllReadAsync(Guid userId) => Task.CompletedTask;
    }

    private sealed class RecordingEmail : IEmailSender
    {
        public readonly List<(string To, string Subject)> Sent = new();
        public Task SendAsync(string to, string subject, string body)
        { Sent.Add((to, subject)); return Task.CompletedTask; }
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public int SendCount;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default)
        { SendCount++; return Task.CompletedTask; }
    }

    private sealed class FakeHubClients : IHubClients
    {
        public readonly FakeClientProxy Proxy = new();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> x) => Proxy;
        public IClientProxy Client(string x) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> x) => Proxy;
        public IClientProxy Group(string x) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> x) => Proxy;
        public IClientProxy GroupExcept(string x, IReadOnlyList<string> y) => Proxy;
        public IClientProxy User(string x) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> x) => Proxy;
    }

    private sealed class FakeHub : IHubContext<NotifyHub>
    {
        public readonly FakeHubClients FakeClients = new();
        public IHubClients Clients => FakeClients;
        public IGroupManager Groups => null!;   // 通知器不触达 Groups
    }

    // ── 脚手架 ──────────────────────────────────────────────────────────────
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private sealed record Rig(CP6Context Db, PersistentWfNotifier Notifier,
        RecordingNotif Notif, RecordingEmail Email, FakeHub Hub);

    private static async Task<Rig> BuildAsync(Guid user, string? prefsJson)
    {
        var db = NewDb();
        db.Sys_Users.Add(new Sys_User { Id = user, UserName = "u1", NickName = "用户一", Password = "x", Email = "u1@cp6.local" });
        if (prefsJson is not null)
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = user, PrefsJson = prefsJson });
        await db.SaveChangesAsync();
        var notif = new RecordingNotif();
        var email = new RecordingEmail();
        var hub = new FakeHub();
        var notifier = new PersistentWfNotifier(db, notif, new PrefService(db), email, hub,
            NullLogger<PersistentWfNotifier>.Instance);
        return new Rig(db, notifier, notif, email, hub);
    }

    // ── 跳过矩阵（spec §7）──────────────────────────────────────────────────
    [Fact]
    public async Task Default_NoPrefRow_PersistsPushesAndEmails()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, prefsJson: null);
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.TodoCreated, r.Notif.Created[0].Type);
        Assert.Equal(1, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Single(r.Email.Sent);
    }

    [Fact]
    public async Task InAppOff_SkipsPersistAndPush_EmailStillSent()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todoCreated":{"inApp":false,"email":true}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Empty(r.Notif.Created);
        Assert.Equal(0, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Single(r.Email.Sent);                       // 通道独立：邮件照发
    }

    [Fact]
    public async Task EmailOff_PersistsAndPushes_NoEmail()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowApproved":{"email":false}}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Equal(WfNotificationType.FlowApproved, r.Notif.Created[0].Type);
        Assert.Equal(1, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task BothOff_SkipsEverything()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.FlowRejectedAsync(user, Guid.NewGuid(), "leave", "缺附件");
        Assert.Empty(r.Notif.Created);
        Assert.Equal(0, r.Hub.FakeClients.Proxy.SendCount);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task TypesIndependent_RejectedOff_TodoStillFull()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"flowRejected":{"inApp":false,"email":false}}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Single(r.Email.Sent);
    }

    // ── 遗留扁平数据回归（C2：旧用户已存开关不失效）──
    [Fact]
    public async Task LegacyFlat_TodoOff_SkipsAllChannels()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"todo":false,"email":true}}""");
        await r.Notifier.TodoCreatedAsync(user, Guid.NewGuid(), Guid.NewGuid(), "leave");
        Assert.Empty(r.Notif.Created);
        Assert.Empty(r.Email.Sent);
    }

    [Fact]
    public async Task LegacyFlat_GlobalEmailOff_SkipsOnlyEmail()
    {
        var user = Guid.NewGuid();
        var r = await BuildAsync(user, """{"notify":{"email":false}}""");
        await r.Notifier.FlowApprovedAsync(user, Guid.NewGuid(), "leave");
        Assert.Single(r.Notif.Created);
        Assert.Empty(r.Email.Sent);
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PersistentWfNotifierTests`。预期：`InAppOff_SkipsPersistAndPush_EmailStillSent` / `EmailOff_*` 等 FAIL（现状事件关=整跳、email 全局）。（Default/BothOff/Legacy 用例在现状下即绿——它们是回归保护。）

- [ ] **Step 3: 实现** — 三个方法统一改为矩阵口径（题头注释③同步更新）。以 `TodoCreatedAsync` 为例，**逐字**：

```csharp
    /// <inheritdoc />
    public async Task TodoCreatedAsync(Guid assigneeId, Guid instanceId, Guid taskId, string flowKey)
    {
        // 1. 逐收件人 × 逐通道查矩阵偏好（per-request 缓存在 IPrefService 内）
        var inApp = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelInApp);
        var email = await _pref.IsEnabledAsync(assigneeId, "todoCreated", NotifyMatrix.ChannelEmail);
        if (!inApp && !email) return;

        const string title = "您有新的待办";
        var body = $"您有新的待办：{flowKey}";

        if (inApp)
        {
            // 2. 持久化（仅 Add，不 SaveChanges）
            await _notif.CreateAsync(
                assigneeId, WfNotificationType.TodoCreated,
                title, body, instanceId, taskId, flowKey);

            // 3. SignalR（best-effort）
            try
            {
                await _hub.Clients.All.SendAsync("WfNotification", new
                {
                    type       = WfNotificationType.TodoCreated,
                    userId     = assigneeId,
                    instanceId,
                    taskId,
                    flowKey
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SignalR WfNotification(TodoCreated) 失败，忽略（用户 {UserId}）", assigneeId);
            }
        }

        // 4. 邮件（best-effort，独立通道）
        if (email)
            await TrySendEmailAsync(assigneeId, title, body);
    }
```

`FlowApprovedAsync` / `FlowRejectedAsync` 同构改造：把 `var prefs = await _pref.GetNotifyPrefsAsync(...); if (!prefs.Approved) return;` 换成上面的双通道查询（type key 分别为 `"flowApproved"` / `"flowRejected"`），持久化+SignalR 包进 `if (inApp)`，`if (prefs.Email)` 换 `if (email)`。title/body/payload 逐字保留现状。文件头加 `using CP6.Core.Services.Oa;`（NotifyMatrix 命名空间）。

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter PersistentWfNotifierTests`。

- [ ] **Step 5: 回归闸 + commit** — 既有 `NotificationEngineHookTests`（引擎钩子）与 `TimeoutScanTests` 必须照绿（`GetNotifyPrefsAsync`/`ParseNotifyPrefs` 保留未删，仅 notifier 停用）：

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): A-T3 PersistentWfNotifier 接矩阵偏好 逐收件人×逐通道独立跳过"
```

---

### Task A-T4: 设置页「通知设定」矩阵卡片（前端）

**Files:**
- Create: `cp6.web/src/views/oa/settings/notifyMatrixModel.ts`
- Test: `cp6.web/src/views/oa/settings/notifyMatrixModel.test.ts`
- Modify: `cp6.web/src/api/oa/pref.ts`
- Modify: `cp6.web/src/views/oa/settings/InboxSettings.vue`

**Interfaces:**
- Consumes: `GET /api/oa/pref/notify-matrix`（A-T2）、`POST /api/oa/pref/save`（Merge=true）。
- Produces: `buildMatrixState(prefsJson, rows)` / `toNotifyPatch(state)`（共享契约签名）、`prefApi.saveMerge(partialJson)` / `prefApi.notifyMatrix()`——D-T2 复用 `saveMerge`。

- [ ] **Step 1: 写失败 vitest**

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.test.ts
import { describe, it, expect } from 'vitest'
import { buildMatrixState, toNotifyPatch, type NotifyMatrixRow } from './notifyMatrixModel'

const rows: NotifyMatrixRow[] = [
  { typeKey: 'todoCreated',  typeValue: 1, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowApproved', typeValue: 2, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowRejected', typeValue: 3, inAppSupported: true,  emailSupported: true },
  { typeKey: 'timeout',      typeValue: 4, inAppSupported: false, emailSupported: false },
]

describe('notifyMatrixModel', () => {
  it('三态坍缩：空/缺键/畸形 → 全 true', () => {
    for (const json of ['', '{}', '{"notify":{}}', 'NOT_JSON{{{']) {
      const s = buildMatrixState(json, rows)
      expect(s.todoCreated).toEqual({ inApp: true, email: true })
      expect(s.timeout).toEqual({ inApp: true, email: true })
    }
  })

  it('新矩阵形态逐格解析（仅字面 false 为关）', () => {
    const s = buildMatrixState('{"notify":{"flowRejected":{"inApp":true,"email":false}}}', rows)
    expect(s.flowRejected).toEqual({ inApp: true, email: false })
    expect(s.flowApproved).toEqual({ inApp: true, email: true })
  })

  it('遗留扁平形态回落（镜像后端 NotifyMatrix.IsEnabled）', () => {
    const s = buildMatrixState('{"notify":{"todo":false,"email":false,"approved":true}}', rows)
    expect(s.todoCreated).toEqual({ inApp: false, email: false })   // 事件关 → 双关
    expect(s.flowApproved).toEqual({ inApp: true, email: false })   // 全局 email 关 → 仅邮件关
  })

  it('toNotifyPatch 产出可回读的 notify patch', () => {
    const s = buildMatrixState('{}', rows)
    s.flowRejected.email = false
    const patch = JSON.parse(toNotifyPatch(s))
    expect(patch.notify.flowRejected).toEqual({ inApp: true, email: false })
    expect(patch.notify.todoCreated).toEqual({ inApp: true, email: true })
    expect(Object.keys(patch)).toEqual(['notify'])                  // 只 patch notify 顶层键
  })
})
```

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- notifyMatrixModel`。预期：模块不存在。

- [ ] **Step 3: 实现纯模型**

```ts
// cp6.web/src/views/oa/settings/notifyMatrixModel.ts
/** 通知矩阵纯函数（wfs-inbox-ux §2.3）。解析语义逐位镜像后端 NotifyMatrix.IsEnabled。 */
export interface NotifyMatrixRow {
  typeKey: string
  typeValue: number
  inAppSupported: boolean
  emailSupported: boolean
}

export type MatrixState = Record<string, { inApp: boolean; email: boolean }>

const LEGACY_KEY: Record<string, string> = {
  todoCreated: 'todo',
  flowApproved: 'approved',
  flowRejected: 'rejected',
  timeout: 'timeout',
}

export function buildMatrixState(prefsJson: string, rows: NotifyMatrixRow[]): MatrixState {
  let notify: Record<string, unknown> = {}
  try {
    const parsed = JSON.parse(prefsJson || '{}')
    if (parsed && typeof parsed.notify === 'object' && parsed.notify !== null) notify = parsed.notify
  } catch {
    notify = {} // 畸形 → 全默认 true
  }
  const state: MatrixState = {}
  for (const r of rows) {
    const cell = notify[r.typeKey]
    if (cell && typeof cell === 'object') {
      const c = cell as Record<string, unknown>
      state[r.typeKey] = { inApp: c.inApp !== false, email: c.email !== false }
    } else {
      const legacyKey = LEGACY_KEY[r.typeKey]
      const eventOn = legacyKey ? notify[legacyKey] !== false : true
      const emailOn = notify['email'] !== false
      state[r.typeKey] = { inApp: eventOn, email: eventOn && emailOn }
    }
  }
  return state
}

/** 序列化为顶层 notify patch（配 prefApi.saveMerge，服务端合并保他键）。 */
export function toNotifyPatch(state: MatrixState): string {
  const notify: Record<string, { inApp: boolean; email: boolean }> = {}
  for (const [k, v] of Object.entries(state)) notify[k] = { inApp: v.inApp, email: v.email }
  return JSON.stringify({ notify })
}
```

- [ ] **Step 4: 跑验证 PASS** — `npm run test -- notifyMatrixModel`。

- [ ] **Step 5: API + 设置页接线**

`cp6.web/src/api/oa/pref.ts` 全文替换为：

```ts
import http from '../http'

export const prefApi = {
  get:  ()                     => http.get('/oa/pref/get'),
  save: (prefsJson: string)    => http.post('/oa/pref/save', { prefsJson }),
  /** 服务端顶层键合并写（保他键；值 null=删键恢复默认） */
  saveMerge: (partialJson: string) => http.post('/oa/pref/save', { prefsJson: partialJson, merge: true }),
  /** 通知矩阵元数据（类型轴 + 通道支持标志） */
  notifyMatrix: () => http.get('/oa/pref/notify-matrix'),
}
```

`InboxSettings.vue` 改造（三处）：

**(a) 模板**：notify tab（:46-73 的 `el-card` 内容）整体替换为矩阵表格：

```html
      <!-- Tab 3: 通知设定（类型×通道矩阵，wfs-inbox-ux §2.3） -->
      <el-tab-pane :label="t('oa.notify.settings.tab')" name="notify">
        <el-card shadow="never" style="max-width: 640px; margin-top: 16px">
          <el-table :data="matrixRows" size="small" border>
            <el-table-column :label="t('oa.notify.matrix.colType')" min-width="180">
              <template #default="{ row }">{{ t('oa.notify.type.' + row.typeKey) }}</template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colInApp')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.inAppSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey].inApp"
                    :disabled="!row.inAppSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column :label="t('oa.notify.matrix.colEmail')" width="110" align="center">
              <template #default="{ row }">
                <el-tooltip :disabled="row.emailSupported" :content="t('oa.notify.matrix.unsupported')">
                  <el-switch
                    v-model="matrixState[row.typeKey].email"
                    :disabled="!row.emailSupported"
                  />
                </el-tooltip>
              </template>
            </el-table-column>
          </el-table>
          <div class="matrix-actions">
            <el-button type="primary" :loading="notifySaving" @click="saveNotifyMatrix">
              {{ t('common.save') }}
            </el-button>
            <el-button @click="resetNotifyMatrix">{{ t('oa.notify.matrix.reset') }}</el-button>
          </div>
        </el-card>
      </el-tab-pane>
```

**(b) 脚本**：删 `NotifyPrefs` 接口、`notifyPrefs` ref、`saveNotifyPref`；加：

```ts
import { buildMatrixState, toNotifyPatch, type MatrixState, type NotifyMatrixRow } from './notifyMatrixModel'

// ─── Notify matrix tab ───────────────────────────────────────────────────────
const matrixRows = ref<NotifyMatrixRow[]>([])
const matrixState = ref<MatrixState>({})
const notifySaving = ref(false)

async function loadNotifyMatrix(prefsJson: string) {
  try {
    const res = await prefApi.notifyMatrix()
    matrixRows.value = (((res as any).data as NotifyMatrixRow[]) || [])
    matrixState.value = buildMatrixState(prefsJson, matrixRows.value)
  } catch {
    // HTTP interceptor auto-toasts
  }
}

async function saveNotifyMatrix() {
  notifySaving.value = true
  try {
    await prefApi.saveMerge(toNotifyPatch(matrixState.value))   // 服务端合并：保 pageSize/rowMode 等他键
    ElMessage.success(t('oa.notify.matrix.saveOk'))
    await loadPref()
  } finally {
    notifySaving.value = false
  }
}

async function resetNotifyMatrix() {
  try {
    await prefApi.saveMerge('{"notify":null}')                  // 删键 = 恢复默认全开（三态坍缩）
    ElMessage.success(t('oa.notify.matrix.resetOk'))
    await loadPref()
  } catch {
    // HTTP interceptor auto-toasts
  }
}
```

`loadPref()` 内：删除 `notifyPrefs.value = {...}` 赋值段（:299-306），在解析出 `prefsJson` 后追加 `await loadNotifyMatrix(prefsJson ?? '{}')`（无 prefsJson 时传 `'{}'` 也要调，保证矩阵行渲染）。`savePref()` 的 `prefApi.save(JSON.stringify(merged))` 改为 `prefApi.saveMerge(JSON.stringify(prefs.value))`（显示偏好三键顶层合并，`storedRaw` spread 保留兜底不删）。

**(c) 样式**（`<style scoped>` 追加）：

```css
.matrix-actions {
  display: flex;
  gap: 10px;
  margin-top: 14px;
}
```

- [ ] **Step 6: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): A-T4 设置页通知矩阵卡片(格子禁用+恢复默认+服务端合并写)"
```

---

## Wave X-B — 在途批量转单

### Task B-T1: InboxService.BatchTransferAsync + Preview（逐条独立事务 + 汇总报告）

**Files:**
- Modify: `CP6.Core/Services/Oa/InboxModels.cs`（record 族）
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`
- Modify: `CP6.Core/Services/Oa/InboxService.cs`
- Test: `CP6.Tests/Oa/BatchTransferTests.cs`

**Interfaces:**
- Consumes: `IFlowEngine.TransferAsync(Guid taskId, Guid actorId, Guid toUserId, string? comment = null)`（**只调用不改动**，引擎内部单次 SaveChanges = 单条独立事务，R3）。
- Produces（共享契约原文）：`BatchTransferFilter / BatchTransferItemResult / BatchTransferReport / BatchTransferPreview` record 族 + `BatchTransferAsync` / `BatchTransferPreviewAsync`——B-T2 端点与 B-T3 UI 依赖。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/BatchTransferTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class BatchTransferTests
{
    // 脚手架照 InboxServiceTests：InMemory + 真引擎 + 真 ForecastService
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>种 n 个实例（同一 flowKey）全部待办压给 from。返回 taskId 列表（按提交序）。</summary>
    private static async Task<List<Guid>> SeedPendingAsync(CP6Context db, Guid starter, Guid from, int n, string flowKey = "leave")
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "starter", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == from))
            db.Sys_Users.Add(new Sys_User { Id = from, UserName = "from", NickName = "转出人", Password = "x" });
        if (!await db.Wf_FlowDefs.AnyAsync(d => d.FlowKey == flowKey))
            db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
                SchemaJson = JsonSerializer.Serialize(new FlowSchema {
                    Nodes = { new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = from },
                              new FlowNode { Id = "end", Type = "end" } },
                    Edges = { new FlowEdge { From = "n1", To = "end" } } }),
                Version = 1, Enable = true });
        await db.SaveChangesAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < n; i++)
        {
            var instId = await Engine(db).SubmitAsync(flowKey, starter, "{}");
            ids.Add((await db.Wf_FlowTasks.SingleAsync(t => t.InstanceId == instId)).Id);
        }
        return ids;
    }

    private static Sys_User NewEnabledUser(Guid id, string name) =>
        new() { Id = id, UserName = name, NickName = name, Password = "x", Enable = true };

    // ── 部分成功 + 汇总（spec §7：逐条事务部分成功、失败明细）──
    [Fact]
    public async Task Batch_PartialSuccess_DirtyRowDoesNotBlockOthers()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 弄脏中间一条：先办结（Status != Pending → TransferAsync 抛 E-WF-002）
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);

        var report = await Inbox(db).BatchTransferAsync(admin, from, to, "离职移交");

        Assert.Equal(2, report.Total);          // 已办结那条不再是 Pending → 不入候选
        Assert.Equal(2, report.Succeeded);
        Assert.Empty(report.Failed);
        Assert.Equal(2, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == to && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Batch_ExplicitTaskIds_DirtyRow_FailsWithDetail_ContinuesRest()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        var taskIds = await SeedPendingAsync(db, starter, from, 3);

        // 显式点名 3 条（重试口径：TaskIds 命中时不预筛状态，让引擎裁决）；第 2 条已办结
        await Engine(db).ActAsync(taskIds[1], from, approve: true, comment: null);
        var report = await Inbox(db).BatchTransferAsync(admin, from, to, null,
            new BatchTransferFilter(TaskIds: taskIds));

        Assert.Equal(3, report.Total);                                   // 点名 3 条全入候选
        Assert.Equal(2, report.Succeeded);                               // 循环中段失败不中断后续（D3）
        var f = Assert.Single(report.Failed);                            // 失败以明细行呈现（spec §3.2 重试同口径）
        Assert.Equal(taskIds[1], f.TaskId);
        Assert.Equal("E-WF-002", f.Error);                               // 引擎语义原样透出
        Assert.Equal("leave", f.FlowKey);
    }

    // ── 上限 500（spec §3.1 防御）──
    [Fact]
    public async Task Batch_Over500_Rejected_WithHintKey()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 501);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null));
        Assert.Equal("oa.bt.errTooMany", ex.Message);
        Assert.Equal(501, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));   // 一条都没转（前置校验）
    }

    // ── from==to / to 停用 / to 不存在（跨租户同路径）──
    [Fact]
    public async Task Batch_FromEqualsTo_Rejected()
    {
        using var db = NewDb();
        var from = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, from, null));
        Assert.Equal("oa.bt.errSameUser", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetDisabled_Rejected()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "to", NickName = "停用者", Password = "x", Enable = false });
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    [Fact]
    public async Task Batch_TargetCrossTenant_Rejected_SamePathAsMissing()
    {
        using var db = NewDb();
        var to = Guid.NewGuid();
        // 显式设他租户（StampTenant 只盖 TenantId==Guid.Empty 的新增行，CP6Context.cs:2211-2213）
        db.Sys_Users.Add(new Sys_User { Id = to, UserName = "alien", NickName = "他租户", Password = "x",
            Enable = true, TenantId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        // 全局查询过滤器（TenantId==CurrentTenantId）查不到 → 与不存在同路径拒绝
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Inbox(db).BatchTransferAsync(Guid.NewGuid(), Guid.NewGuid(), to, null));
        Assert.Equal("oa.bt.errTargetInvalid", ex.Message);
    }

    // ── 审计与引擎语义（spec §7：审计行齐全、TransferAsync 语义不变回归）──
    [Fact]
    public async Task Batch_WritesEngineAudit_HistoryAndFormToPair_PerTask()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid(); var admin = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2);

        await Inbox(db).BatchTransferAsync(admin, from, to, "移交");

        // Wf_FlowHistory：每条一行 action=transfer，ActorId=操作者（admin）
        Assert.Equal(2, await db.Wf_FlowHistories.CountAsync(h => h.Action == "transfer" && h.ActorId == admin));
        // Wf_FlowFormTo 双行：原行 Transferred(ActualHandlerId=from) + 新 Pending 行(ExpectedHandlerId=to)
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Transferred && f.ActualHandlerId == from));
        Assert.Equal(2, await db.Wf_FlowFormTos.CountAsync(f => f.Status == FlowFormToStatus.Pending && f.ExpectedHandlerId == to));
    }

    // ── filter 收窄 + preview ──
    [Fact]
    public async Task Batch_FilterByFlowKey_NarrowsCandidates()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        db.Sys_Users.Add(NewEnabledUser(to, "to"));
        await db.SaveChangesAsync();
        await SeedPendingAsync(db, starter, from, 2, flowKey: "leave");
        await SeedPendingAsync(db, starter, from, 1, flowKey: "expense");

        var report = await Inbox(db).BatchTransferAsync(Guid.NewGuid(), from, to, null,
            new BatchTransferFilter(FlowKey: "leave"));

        Assert.Equal(2, report.Total);
        Assert.Equal(1, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from && t.Status == FlowTaskStatus.Pending)); // expense 留下
    }

    [Fact]
    public async Task Preview_ReturnsTotalAndSample_WithoutTransferring()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var from = Guid.NewGuid();
        await SeedPendingAsync(db, starter, from, 12);

        var preview = await Inbox(db).BatchTransferPreviewAsync(from);

        Assert.Equal(12, preview.Total);
        Assert.Equal(10, preview.Sample.Count);                                            // 抽样前 10
        Assert.Equal(12, await db.Wf_FlowTasks.CountAsync(t => t.AssigneeId == from));     // 只读
    }
}
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter BatchTransferTests`。预期：编译失败（record/方法不存在）。

- [ ] **Step 3: 实现**

`InboxModels.cs` 末尾追加：

```csharp
// ── 在途批量转单（wfs-inbox-ux §3）──
public record BatchTransferFilter(string? FlowKey = null, DateTime? BeforeUtc = null, IReadOnlyList<Guid>? TaskIds = null);
public record BatchTransferItemResult(Guid TaskId, string FlowKey, bool Ok, string? Error);
public record BatchTransferReport(int Total, int Succeeded, IReadOnlyList<BatchTransferItemResult> Failed);
public record BatchTransferPreview(int Total, IReadOnlyList<InboxPendingItem> Sample);
```

`IInboxService.cs` 追加：

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3）── actorId=操作者（管理员本人）；逐条独立事务（引擎 TransferAsync 内部 SaveChanges）
    Task<BatchTransferReport> BatchTransferAsync(Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null);
    Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null);
```

`InboxService.cs` 追加（仿 `ActBatchAsAsync` 循环口径 :199-217）：

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3，D3：逐条独立事务 + 汇总报告）──────────

    private const int MaxBatchTransfer = 500;

    /// <summary>
    /// 候选查询。常规路径：from 的全部 Pending 待办（Running 实例）按 filter 收窄；
    /// BeforeUtc 直接比对 CreateDate（库内为服务器本地时，C7）。
    /// <b>TaskIds 显式点名（=单条重试口径，spec §3.2）</b>：不预筛任务/实例状态，让引擎
    /// TransferAsync 裁决——已办结等脏数据以失败明细行（E-WF-002）呈现，不特殊处理；
    /// 仍保留 AssigneeId==from 归属过滤（已被转走的任务不再属 from，绝不能改派他人任务）。
    /// </summary>
    private async Task<List<(Guid TaskId, string FlowKey)>> QueryTransferCandidatesAsync(Guid fromUserId, BatchTransferFilter? f)
    {
        if (f?.TaskIds is { Count: > 0 } ids)
        {
            var named = await (from t in _db.Wf_FlowTasks
                               where t.AssigneeId == fromUserId && ids.Contains(t.Id)
                               join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                               orderby t.CreateDate
                               select new { t.Id, i.FlowKey }).ToListAsync();
            return named.Select(x => (x.Id, x.FlowKey)).ToList();
        }

        var q = from t in _db.Wf_FlowTasks
                where t.AssigneeId == fromUserId && t.Status == FlowTaskStatus.Pending
                join i in _db.Wf_FlowInstances on t.InstanceId equals i.Id
                where i.Status == FlowInstanceStatus.Running
                select new { t.Id, i.FlowKey, t.CreateDate };
        if (!string.IsNullOrWhiteSpace(f?.FlowKey)) q = q.Where(x => x.FlowKey == f.FlowKey);
        if (f?.BeforeUtc is { } before) q = q.Where(x => x.CreateDate < before);
        var rows = await q.OrderBy(x => x.CreateDate).ToListAsync();
        return rows.Select(x => (x.Id, x.FlowKey)).ToList();
    }

    /// <inheritdoc/>
    public async Task<BatchTransferReport> BatchTransferAsync(
        Guid actorId, Guid fromUserId, Guid toUserId, string? comment, BatchTransferFilter? filter = null)
    {
        // 前置校验（入参级，400 口径，不占 E-WF 码）
        if (fromUserId == toUserId)
            throw new InvalidOperationException("oa.bt.errSameUser");
        var to = await _db.Sys_Users.FirstOrDefaultAsync(u => u.Id == toUserId);   // 全局租户过滤器：跨租户查不到（R3）
        if (to is null || !to.Enable)
            throw new InvalidOperationException("oa.bt.errTargetInvalid");

        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        if (candidates.Count > MaxBatchTransfer)
            throw new InvalidOperationException("oa.bt.errTooMany");               // 超上限 → 提示分批（防长事务假象与超时）

        var failed = new List<BatchTransferItemResult>();
        var succeeded = 0;
        foreach (var (taskId, flowKey) in candidates)
        {
            try
            {
                // 引擎动作只调用不改动：内部校验 + FormTo 双行 + history + 通知 + 单次 SaveChanges（=单条独立事务）
                await _engine.TransferAsync(taskId, actorId, toUserId, comment);
                succeeded++;
            }
            catch (InvalidOperationException e)                                    // 单条失败不中断后续（D3）
            {
                failed.Add(new BatchTransferItemResult(taskId, flowKey, false, e.Message));
            }
        }
        return new BatchTransferReport(candidates.Count, succeeded, failed);
    }

    /// <inheritdoc/>
    public async Task<BatchTransferPreview> BatchTransferPreviewAsync(Guid fromUserId, BatchTransferFilter? filter = null)
    {
        var candidates = await QueryTransferCandidatesAsync(fromUserId, filter);
        var candidateIds = candidates.Select(c => c.TaskId).Take(10).ToHashSet();
        var all = await PendingAsync(fromUserId, rowMode: "expanded");             // 复用列表读模型拿展示字段
        var sample = all.Where(p => candidateIds.Contains(p.TaskId)).ToList();
        return new BatchTransferPreview(candidates.Count, sample);
    }
```

> 注：`PendingAsync(userId, rowMode: "expanded")` 依赖 D-T1 的签名扩展。**若 X-B 先于 X-D 执行**：本任务先以 `PendingAsync(fromUserId)` 现签名调用（现状即逐任务行 = expanded 语义，R5），D-T1 改签名时默认参不破本调用——两种顺序都编译。执行者按当时签名取其一，语义相同。

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter BatchTransferTests`。

- [ ] **Step 5: 回归闸 + commit**

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"    # ActBatch/Transfer 既有语义零回归
git add -A && git commit -m "feat(wfs-inbox): B-T1 BatchTransferAsync 逐条独立事务+汇总+上限500+前置校验+preview"
```

---

### Task B-T2: 批量转单端点 + 权限点 seed

**Files:**
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`
- Modify: `CP6.WebApi/Program.cs`（权限点 seed，插在 OA 菜单 seed 块之后，:1354-1358 附近）
- Test:（控制器薄壳走 build + B-T1 服务测试承载 + E-T2 QA harness e2e）

**Interfaces:**
- Consumes: `IInboxService.BatchTransferAsync/BatchTransferPreviewAsync`（B-T1）、`RequirePermissionAttribute(menu, action)`（`CP6.Core/Auth`）。
- Produces: `POST /api/oa/inbox/batch-transfer`、`POST /api/oa/inbox/batch-transfer/preview`（同请求体 `BatchTransferReq`）；权限点 `("oa-inbox","batch-transfer")` = spec `OA.Inbox.BatchTransfer` 的落地映射（C4）。B-T3 前端依赖。

- [ ] **Step 1: 控制器 action**（`InboxController.cs` 追加，DTO 记在文件底部既有 record 区；文件头加 `using CP6.Core.Auth;`）

```csharp
    // ── 在途批量转单（wfs-inbox-ux §3；权限点 = spec OA.Inbox.BatchTransfer → (oa-inbox, batch-transfer)）──
    // 审计：OperLogFilter 全局记 POST 请求体（操作者/from/to）+ 引擎 Wf_FlowHistory/Wf_FlowFormTo 逐条记录（R3）。

    public record BatchTransferFilterReq(string? FlowKey, DateTime? BeforeUtc, List<Guid>? TaskIds);
    public record BatchTransferReq(Guid FromUserId, Guid ToUserId, string? Comment, BatchTransferFilterReq? Filter);

    private static BatchTransferFilter? ToFilter(BatchTransferFilterReq? f) =>
        f is null ? null : new BatchTransferFilter(f.FlowKey, f.BeforeUtc, f.TaskIds);

    [HttpPost("batch-transfer")]
    [RequirePermission("oa-inbox", "batch-transfer")]
    public async Task<IActionResult> BatchTransfer([FromBody] BatchTransferReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();   // 操作者=登录管理员本人（管理动作不走 act-as）
            return Ok2(await _inbox.BatchTransferAsync(me, r.FromUserId, r.ToUserId, r.Comment, ToFilter(r.Filter)));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("batch-transfer/preview")]
    [RequirePermission("oa-inbox", "batch-transfer")]
    public async Task<IActionResult> BatchTransferPreview([FromBody] BatchTransferReq r)
    {
        try
        {
            return Ok2(await _inbox.BatchTransferPreviewAsync(r.FromUserId, ToFilter(r.Filter)));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
```

- [ ] **Step 2: Program.cs 权限点 seed** — 在 OA 菜单 seed 块（`MenuId == 733`，Program.cs:1354-1358）之后追加（照 Fin 块 :1128-1158 的既有 idiom；**HasActionAsync 无 admin 旁路，必须授 RoleId=1**）：

```csharp
        // ── OA 信箱批量改派权限点（wfs-inbox-ux §3.1；spec OA.Inbox.BatchTransfer → (oa-inbox, batch-transfer)）──
        {
            var inboxMenu = db.Sys_Menus.FirstOrDefault(m => m.MenuId == 733);
            if (inboxMenu is not null && string.IsNullOrEmpty(inboxMenu.MenuKey))
                inboxMenu.MenuKey = inboxMenu.RoutePath!.Trim('/').Replace('/', '-');   // "/oa/inbox" → "oa-inbox"
            if (!db.Sys_MenuActions.Any(x => x.MenuId == 733 && x.ActionCode == "batch-transfer"))
                db.Sys_MenuActions.Add(new Sys_MenuAction { MenuId = 733, ActionCode = "batch-transfer", ActionName = "批量改派", Sort = 0 });
            if (!db.Sys_RoleActions.Any(x => x.RoleId == 1 && x.MenuId == 733 && x.ActionCode == "batch-transfer"))
                db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 1, MenuId = 733, ActionCode = "batch-transfer" });
            db.SaveChanges();
        }
```

- [ ] **Step 3: 编译 + 回归闸 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): B-T2 batch-transfer/preview 端点+RequirePermission(oa-inbox,batch-transfer)+权限点seed"
```

---

### Task B-T3: 批量改派对话框 UI（流程管理入口 + 预览 + 结果报告 + 单条重试）

**Files:**
- Create: `cp6.web/src/views/oa/admin/BatchTransferDialog.vue`
- Modify: `cp6.web/src/views/oa/admin/FlowAdmin.vue`（#actions 加入口按钮）
- Modify: `cp6.web/src/api/oa/inbox.ts`
- Modify: `cp6.web/src/types/oa/inbox.ts`

**Interfaces:**
- Consumes: `POST /oa/inbox/batch-transfer` / `.../preview`（B-T2）；用户远程搜索 `userApi.getList`（照 `TransferDialog.vue:74-92` 逐字模式）。
- Produces: `inboxApi.batchTransfer(p)` / `inboxApi.batchTransferPreview(p)`；单条重试 = **同端点 + `filter.taskIds:[id]` + 同失败明细口径**（spec §3.2：任务被他人办结/转走等结果同样以明细行呈现，不特殊处理）。

- [ ] **Step 1: API + 类型**

`cp6.web/src/api/oa/inbox.ts` 追加两行（对象内）：

```ts
  batchTransfer: (p: BatchTransferReq) => http.post('/oa/inbox/batch-transfer', p),
  batchTransferPreview: (p: BatchTransferReq) => http.post('/oa/inbox/batch-transfer/preview', p),
```

文件头加 `import type { BatchTransferReq } from '@/types/oa/inbox'`。

`cp6.web/src/types/oa/inbox.ts` 末尾追加：

```ts
// ── 在途批量转单（wfs-inbox-ux §3）──
export interface BatchTransferReq {
  fromUserId: string
  toUserId: string
  comment?: string
  filter?: { flowKey?: string; beforeUtc?: string; taskIds?: string[] }
}

export interface BatchTransferItemResult {
  taskId: string
  flowKey: string
  ok: boolean
  error?: string
}

export interface BatchTransferReport {
  total: number
  succeeded: number
  failed: BatchTransferItemResult[]
}

export interface BatchTransferPreview {
  total: number
  sample: PendingItem[]
}
```

- [ ] **Step 2: 对话框组件**（完整代码；用户搜索段与 `TransferDialog.vue` 同模式）

```html
<!-- cp6.web/src/views/oa/admin/BatchTransferDialog.vue
     在途批量改派（wfs-inbox-ux §3.2）：选 from/to → 预览待转清单 → 确认 → 结果报告（失败行单条重试）。
     重试走同一 batch-transfer 端点 + filter.taskIds（同 TransferAsync、同失败明细口径）。 -->
<template>
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.bt.title')"
    :width="isMobile ? '100vw' : '640px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
    <!-- Step 1: 表单 + 预览 -->
    <template v-if="!report">
      <el-form label-width="100px">
        <el-form-item :label="t('oa.bt.fromUser')">
          <el-select v-model="fromUserId" filterable remote :remote-method="searchFrom"
            :loading="fromLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable
            @change="preview = null">
            <el-option v-for="u in fromOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.toUser')">
          <el-select v-model="toUserId" filterable remote :remote-method="searchTo"
            :loading="toLoading" :placeholder="t('oa.transfer.userHint')" style="width: 100%" clearable>
            <el-option v-for="u in toOptions" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterFlowKey')">
          <el-input v-model="filterFlowKey" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.filterBefore')">
          <el-date-picker v-model="filterBefore" type="datetime" value-format="YYYY-MM-DDTHH:mm:ss"
            style="width: 100%" clearable @change="preview = null" />
        </el-form-item>
        <el-form-item :label="t('oa.bt.comment')">
          <el-input v-model="comment" type="textarea" :rows="2" :placeholder="t('oa.bt.commentHint')" />
        </el-form-item>
      </el-form>

      <div v-if="preview" class="bt-preview">
        <CpTag tone="info">{{ t('oa.bt.previewTotal', { n: preview.total }) }}</CpTag>
        <el-table v-if="preview.sample.length" :data="preview.sample" size="small" border max-height="220">
          <el-table-column prop="flowName" :label="t('oa.col.flowName')" min-width="140" />
          <el-table-column prop="starterName" :label="t('oa.col.starter')" width="110" />
          <el-table-column :label="t('oa.col.sentAt')" width="160">
            <template #default="{ row }">{{ formatTime(row.sentAt) }}</template>
          </el-table-column>
        </el-table>
        <CpEmpty v-else :text="t('oa.bt.previewEmpty')" />
      </div>
    </template>

    <!-- Step 2: 结果报告 -->
    <template v-else>
      <div class="bt-result">
        <CpTag :tone="report.failed.length ? 'warn' : 'ok'">
          {{ t('oa.bt.resultSummary', { total: report.total, ok: report.succeeded, fail: report.failed.length }) }}
        </CpTag>
        <el-table v-if="report.failed.length" :data="report.failed" size="small" border max-height="260">
          <el-table-column :label="t('oa.bt.colTask')" width="120">
            <template #default="{ row }">{{ row.taskId.slice(0, 8) }}</template>
          </el-table-column>
          <el-table-column prop="flowKey" :label="t('oa.bt.colFlow')" min-width="110" />
          <el-table-column :label="t('oa.bt.colError')" min-width="140">
            <template #default="{ row }">{{ t(row.error ?? '') }}</template>
          </el-table-column>
          <el-table-column width="90" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link type="primary" :loading="retrying.has(row.taskId)"
                @click="retryOne(row)">
                {{ t('oa.bt.retry') }}
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </template>

    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <template v-if="!report">
        <el-button :disabled="!fromUserId" :loading="previewing" @click="doPreview">
          {{ t('oa.bt.preview') }}
        </el-button>
        <el-button type="warning" :loading="submitting"
          :disabled="!fromUserId || !toUserId || !preview || preview.total === 0"
          @click="doTransfer">
          {{ t('oa.bt.confirm') }}
        </el-button>
      </template>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { inboxApi } from '@/api/oa/inbox'
import { userApi } from '@/api/sys/user'
import { useBreakpoint } from '@/composables/useBreakpoint'
import CpTag from '@/components/base/CpTag.vue'
import CpEmpty from '@/components/base/CpEmpty.vue'
import type { BatchTransferItemResult, BatchTransferPreview, BatchTransferReport, BatchTransferReq } from '@/types/oa/inbox'

defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [val: boolean] }>()

const { t } = useI18n()
const { isMobile } = useBreakpoint()

const fromUserId = ref('')
const toUserId = ref('')
const comment = ref('')
const filterFlowKey = ref('')
const filterBefore = ref('')
const preview = ref<BatchTransferPreview | null>(null)
const report = ref<BatchTransferReport | null>(null)
const previewing = ref(false)
const submitting = ref(false)
const retrying = reactive(new Set<string>())

// ── 用户远程搜索（同 TransferDialog.vue 模式）──
interface UserOption { label: string; value: string }
const fromOptions = ref<UserOption[]>([])
const toOptions = ref<UserOption[]>([])
const fromLoading = ref(false)
const toLoading = ref(false)

async function searchUsers(keyword: string, into: typeof fromOptions, loading: typeof fromLoading) {
  if (!keyword) { into.value = []; return }
  loading.value = true
  try {
    const res: any = await userApi.getList({ page: 1, pageSize: 20, keyword })
    into.value = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: u.id }))
  } catch {
    // HTTP interceptor already toasts the error
  } finally {
    loading.value = false
  }
}
const searchFrom = (kw: string) => searchUsers(kw, fromOptions, fromLoading)
const searchTo = (kw: string) => searchUsers(kw, toOptions, toLoading)

function buildReq(taskIds?: string[]): BatchTransferReq {
  return {
    fromUserId: fromUserId.value,
    toUserId: toUserId.value,
    comment: comment.value || undefined,
    filter: {
      flowKey: filterFlowKey.value || undefined,
      beforeUtc: filterBefore.value || undefined,
      taskIds,
    },
  }
}

async function doPreview() {
  previewing.value = true
  try {
    const res: any = await inboxApi.batchTransferPreview(buildReq())
    preview.value = res.data as BatchTransferPreview
  } catch {
    // 400（errSameUser/errTargetInvalid 等）由拦截器 t(raw) 自动 toast
  } finally {
    previewing.value = false
  }
}

async function doTransfer() {
  submitting.value = true
  try {
    const res: any = await inboxApi.batchTransfer(buildReq())
    report.value = res.data as BatchTransferReport
    if (!report.value.failed.length) ElMessage.success(t('oa.bt.allOk'))
  } catch {
    // 拦截器 toast（含 oa.bt.errTooMany 分批提示）
  } finally {
    submitting.value = false
  }
}

/** 单条重试：同端点 + filter.taskIds=[id]（同 TransferAsync、同失败明细口径，spec §3.2） */
async function retryOne(row: BatchTransferItemResult) {
  if (retrying.has(row.taskId) || !report.value) return
  retrying.add(row.taskId)
  try {
    const res: any = await inboxApi.batchTransfer(buildReq([row.taskId]))
    const r = res.data as BatchTransferReport
    if (r.succeeded === 1) {
      report.value = {
        ...report.value,
        succeeded: report.value.succeeded + 1,
        failed: report.value.failed.filter((f) => f.taskId !== row.taskId),
      }
      ElMessage.success(t('oa.bt.retryOk'))
    } else {
      // 重试仍失败（可能已被他人办结/转走）→ 用最新明细行替换（同口径呈现）
      const latest = r.failed.find((f) => f.taskId === row.taskId)
      report.value = {
        ...report.value,
        failed: report.value.failed.map((f) =>
          f.taskId === row.taskId && latest ? latest : f),
      }
      if (r.total === 0) {
        // 已不在 from 名下（他人办结/已转走）→ 从失败清单移除并提示
        report.value = { ...report.value, failed: report.value.failed.filter((f) => f.taskId !== row.taskId) }
        ElMessage.info(t('oa.bt.retryGone'))
      }
    }
  } finally {
    retrying.delete(row.taskId)
  }
}

function formatTime(s: string): string {
  return s ? s.replace('T', ' ').slice(0, 19) : ''
}

function onClose() {
  emit('update:modelValue', false)
  fromUserId.value = ''
  toUserId.value = ''
  comment.value = ''
  filterFlowKey.value = ''
  filterBefore.value = ''
  preview.value = null
  report.value = null
  fromOptions.value = []
  toOptions.value = []
}
</script>

<style scoped>
.bt-preview,
.bt-result {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 4px;
}
</style>
```

- [ ] **Step 3: FlowAdmin 入口**（`FlowAdmin.vue`）：

模板 `#actions` 内、刷新按钮之前加：

```html
      <el-button type="warning" plain @click="batchTransferVisible = true">
        {{ t('oa.bt.entry') }}
      </el-button>
```

`</CpListPage>` 与 `</CpPageShell>` 之间加：

```html
    <BatchTransferDialog v-model="batchTransferVisible" />
```

脚本加：

```ts
import BatchTransferDialog from './BatchTransferDialog.vue'

const batchTransferVisible = ref(false)
```

（权限由后端 403 强制：未授权用户点击确认时拦截器 toast「无权限：oa-inbox:batch-transfer」；按钮不做前端隐藏——OA 前端当前无权限位可查，R4。）

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): B-T3 批量改派对话框(预览+结果报告+单条重试)+流程管理入口"
```

---

## Wave X-C — 移动端响应式（<768px = `max-width: 767px` 既有约定，R6）

> 纯前端，与 X-A/X-B/X-D 并行。全部用 `useBreakpoint().isMobile`（模板结构切换）+ 各页 `<style>` 尾部 `@media (max-width: 767px)` 块（样式微调），照 `StockDwellView.vue` / `LayoutView.vue` 既有做法。零新依赖（D4）。**≥768px 渲染路径逐字节不变**（桌面像素零回归）。
> 范围界定：spec §4 三项 = 列表（InboxView 壳 + Pending/Running/Done 卡片化 + 筛选收抽屉）/ 表单详情（FormDetail 堆叠）/ 审批操作（钉底栏 + 对话框全屏化）。Dashboard/Draft 仅随壳受益（容器 padding），不卡片化——spec 未列，保持现状。

### Task C-T1: 信箱壳 + 三个列表页卡片化 + 筛选抽屉

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/InboxView.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxPending.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxRunning.vue`
- Modify: `cp6.web/src/views/oa/inbox/InboxDone.vue`

**Interfaces:**
- Consumes: `useBreakpoint()`（`cp6.web/src/composables/useBreakpoint.ts`，`isMobile = width<=767`）。
- Produces: 无对外契约（纯视图）；InboxPending 的 `selected` 数组语义不变（D-T2/批量条继续复用）。

- [ ] **Step 1: InboxView.vue（壳）**

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()

/** 移动端文件夹横滑条（含流程管理路由项） */
const folderList = computed(() => [
  { key: 'dashboard',  label: t('oa.inbox.dashboard') },
  { key: 'pending',    label: t('oa.inbox.pending') },
  { key: 'running',    label: t('oa.inbox.running') },
  { key: 'done',       label: t('oa.inbox.done') },
  { key: 'draft',      label: t('oa.inbox.draft') },
  { key: 'flow-admin', label: t('oa.inbox.flowAdmin') },
])
```

模板三处：

(a) `el-aside` 桌面独占：

```html
      <el-aside v-if="!isMobile" width="200px" class="inbox-aside">
```

(b) `el-aside` 结束标签后、`el-main` 前无需插入——横滑条放 `inbox-body` 之上（`el-header` 结束标签之后）：

```html
    <!-- 移动端文件夹横滑条（替代左侧菜单） -->
    <div v-if="isMobile" class="mobile-folder-bar">
      <el-button
        v-for="f in folderList"
        :key="f.key"
        size="small"
        round
        :type="folder === f.key ? 'primary' : 'default'"
        @click="onSelect(f.key)"
      >
        {{ f.label }}<template v-if="f.key === 'pending' && stats?.pendingCount"> ({{ stats.pendingCount }})</template>
      </el-button>
    </div>
```

(c) 详情抽屉移动端全屏：

```html
    <el-drawer
      v-model="drawerVisible"
      :size="isMobile ? '100%' : '60%'"
      :title="t('oa.inbox.detailTitle')"
      destroy-on-close
    >
```

`<style scoped>` 尾部追加：

```css
.mobile-folder-bar {
  display: flex;
  gap: 6px;
  padding: 8px 12px;
  overflow-x: auto;
  background: var(--cp-card);
  border-bottom: 1px solid var(--cp-line-soft);
  flex-shrink: 0;
  -webkit-overflow-scrolling: touch;
}

.mobile-folder-bar .el-button {
  flex-shrink: 0;
  margin-left: 0;
}

@media (max-width: 767px) {
  .inbox-header {
    padding: 0 12px;
  }

  .inbox-title {
    font-size: 14px;
  }

  .inbox-main {
    padding: 10px;
  }
}
```

- [ ] **Step 2: InboxPending.vue（卡片化 + 移动端多选）**

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()

/** 移动端卡片多选：直接维护同一 selected 数组（批量条 doBatch 复用零改动） */
function isSelected(row: PendingItem): boolean {
  return selected.value.some((r) => r.taskId === row.taskId)
}

function toggleMobileSelect(row: PendingItem) {
  selected.value = isSelected(row)
    ? selected.value.filter((r) => r.taskId !== row.taskId)
    : [...selected.value, row]
}
```

review 面板 `<el-table ...>`（:28-47）加 `v-if="!isMobile"`，其后（`CpEmpty` 之前）插卡片流（spec §4 字段：单号/流程名/当前关卡/时间戳/状态 CpTag）：

```html
        <div v-if="isMobile" class="mobile-list" v-loading="reviewLoading">
          <div
            v-for="row in reviewRows"
            :key="row.taskId"
            class="mobile-row"
            :class="{ 'row-unread': !row.isRead }"
            @click="onReviewRowClick(row)"
          >
            <div class="mobile-main">
              <el-checkbox
                :model-value="isSelected(row)"
                @click.stop
                @change="toggleMobileSelect(row)"
              />
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.stageName || row.nodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span class="mobile-key">{{ row.flowKey }}</span>
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.sentAt) }}</span>
            </div>
          </div>
        </div>
```

cc 面板同法：`<el-table>`（:57-73）加 `v-if="!isMobile"`，其后插：

```html
        <div v-if="isMobile" class="mobile-list" v-loading="ccLoading">
          <div v-for="row in ccRows" :key="row.ccId" class="mobile-row" @click="onCcRowClick(row)">
            <div class="mobile-main">
              <span class="mobile-flow">{{ row.flowName }}</span>
              <CpTag tone="info">{{ row.atNodeId }}</CpTag>
            </div>
            <div class="mobile-meta">
              <span>{{ row.starterName }}</span>
              <span>{{ formatTime(row.createDate) }}</span>
            </div>
          </div>
        </div>
```

`<style scoped>` 尾部追加（卡片样式照 `StockDwellView.vue:402-443` 词汇）：

```css
.mobile-list {
  display: flex;
  flex-direction: column;
}

.mobile-row {
  padding: 12px 2px;
  border-bottom: 1px solid var(--cp-line);
  cursor: pointer;
}

.mobile-row:last-child {
  border-bottom: none;
}

.mobile-main {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--cp-ink);
  font-size: 14px;
  margin-bottom: 6px;
}

.mobile-flow {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mobile-meta {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  color: var(--cp-muted);
  font-size: 12px;
}

.mobile-key {
  font-family: monospace;
}

.mobile-row.row-unread .mobile-flow {
  font-weight: 650;
}

@media (max-width: 767px) {
  .batch-bar {
    flex-wrap: wrap;
  }

  .batch-bar .el-input {
    width: 100% !important;
    order: 3;
  }
}
```

- [ ] **Step 3: InboxRunning.vue** — 同法：脚本加 `useBreakpoint`；`<el-table>` 加 `v-if="!isMobile"`；其后插：

```html
    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="instanceStatusTone(row.status)">{{ t(instanceStatusText(row.status)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.currentNode }} · {{ row.currentHandlers.join('、') }}</span>
          <span>{{ formatTime(row.createDate) }}</span>
        </div>
      </div>
    </div>
```

`<style scoped>` 尾部追加与 InboxPending 相同的 `.mobile-list/.mobile-row/.mobile-main/.mobile-flow/.mobile-meta` 五条规则（scoped 样式不跨组件，需各页自带；逐字同上，无 `.mobile-key`/`.row-unread` 两条）。

- [ ] **Step 4: InboxDone.vue（卡片化 + 筛选收抽屉）**

脚本加：

```ts
import { Filter, Refresh } from '@element-plus/icons-vue'   // 原行只有 Refresh，替换
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()
const filterDrawer = ref(false)
```

模板：`.done-controls`（:4-20）加 `v-if="!isMobile"`；`table-toolbar`（:22-25）内刷新按钮后加移动端筛选入口；`.done-controls` 原块整体复制进底部抽屉（月份选择 + tab 换 `el-radio-group`）：

```html
    <!-- 移动端：筛选入口 + 底部抽屉 -->
    <div class="table-toolbar">
      <CpTag>{{ t('共 {n} 条', { n: rows.length }) }}</CpTag>
      <el-button :icon="Refresh" circle size="small" :loading="loading" @click="load" />
      <el-button v-if="isMobile" :icon="Filter" size="small" round @click="filterDrawer = true">
        {{ t('oa.inbox.mobileFilter') }}
      </el-button>
    </div>

    <el-drawer v-model="filterDrawer" direction="btt" size="40%" :title="t('oa.inbox.mobileFilter')">
      <el-form label-width="90px">
        <el-form-item :label="t('oa.done.allMonths')">
          <el-date-picker v-model="selectedMonth" type="month" value-format="YYYY-MM"
            :placeholder="t('oa.done.allMonths')" clearable style="width: 100%" @change="load" />
        </el-form-item>
        <el-form-item>
          <el-radio-group v-model="activeTab" @change="load">
            <el-radio-button label="mine">{{ t('oa.done.mine') }}</el-radio-button>
            <el-radio-button label="all">{{ t('oa.done.all') }}</el-radio-button>
            <el-radio-button label="cc">{{ t('oa.done.cc') }}</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>
    </el-drawer>
```

`<el-table>` 加 `v-if="!isMobile"`，其后插：

```html
    <div v-if="isMobile" class="mobile-list" v-loading="loading">
      <div v-for="row in rows" :key="row.instanceId" class="mobile-row" @click="onRowClick(row)">
        <div class="mobile-main">
          <span class="mobile-flow">{{ row.flowName }}</span>
          <CpTag :tone="formToStatusTone(row.formToStatus)">{{ t(formToStatusText(row.formToStatus)) }}</CpTag>
        </div>
        <div class="mobile-meta">
          <span>{{ row.starterName }}</span>
          <span>{{ formatTime(row.doneAt) }}</span>
        </div>
      </div>
    </div>
```

`<style scoped>` 尾部追加同一组 `.mobile-*` 五条规则（同 Step 3）。

- [ ] **Step 5: 验证 + commit** — 桌面回归：既有 vitest 全绿（列表逻辑零改，纯模板分支）；375px 走查留 E-T2 harness。

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): C-T1 信箱壳+待办/在途/已办列表移动端卡片化+筛选抽屉(767px断点)"
```

---

### Task C-T2: FormDetail 堆叠 + 审批操作钉底栏 + 对话框全屏化

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`
- Modify: `cp6.web/src/views/oa/inbox/TransferDialog.vue`
- Modify: `cp6.web/src/views/oa/inbox/SendBackDialog.vue`

**Interfaces:**
- Consumes: `useBreakpoint()`。
- Produces: 无对外契约（纯视图）。签核记录=右栏 FlowTimeline 内联（C6）→ 堆叠即「全屏化」落点；Transfer/SendBack 对话框移动端 fullscreen。

- [ ] **Step 1: FormDetail.vue 左右列堆叠** — `el-col` 换响应式栅格（el-col 原生 xs/sm 属性，≥768px 走 sm 值与现状 span 等价）：

```html
        <el-col :xs="24" :sm="14" class="detail-left">
```

```html
        <el-col :xs="24" :sm="10" class="detail-right">
```

（原 `:span="14"` / `:span="10"` 删除。）

- [ ] **Step 2: 操作钉底栏 + 样式** — `.action-bar` 模板不动（`v-if="myTaskId"` 保留）；`<style scoped>` 尾部追加：

```css
@media (max-width: 767px) {
  .detail-left {
    border-right: none;
    padding-right: 0;
  }

  .detail-right {
    max-height: none;
    padding-left: 0;
    margin-top: 16px;
    overflow-y: visible;
  }

  /* 审批操作钉底栏（安全区适配，spec §4） */
  .action-bar {
    position: sticky;
    bottom: 0;
    z-index: 5;
    flex-wrap: wrap;
    background: var(--cp-card);
    box-shadow: var(--cp-shadow-up);
    margin: 16px -16px 0;
    padding: 10px 12px calc(10px + env(safe-area-inset-bottom));
  }

  .action-bar .el-input {
    width: 100% !important;   /* 覆盖行内 280px（同 OtdReportView 既有 !important 口径） */
  }
}
```

（`margin: 0 -16px` 抵消 `el-drawer` body 内边距使钉底栏贴满；FormDetail 挂在 InboxView 抽屉内，抽屉移动端已 100% 全屏——C-T1。）

- [ ] **Step 3: TransferDialog.vue / SendBackDialog.vue 全屏化** — 两文件同改：

脚本加：

```ts
import { useBreakpoint } from '@/composables/useBreakpoint'

const { isMobile } = useBreakpoint()
```

`TransferDialog.vue` 的 `<el-dialog ... width="440px">` 改：

```html
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.transfer.title')"
    :width="isMobile ? '100vw' : '440px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
```

`SendBackDialog.vue` 的 `<el-dialog ... width="440px">`（:5，实读现值 440px）同法改：

```html
  <el-dialog
    :model-value="modelValue"
    :title="t('oa.detail.sendback')"
    :width="isMobile ? '100vw' : '440px'"
    :fullscreen="isMobile"
    @close="onClose"
  >
```

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): C-T2 详情页移动端堆叠+审批操作钉底栏(安全区)+转交/退回对话框全屏化"
```

---

## Wave X-D — 同单多状态多行显示（rowMode merged|expanded）

### Task D-T1: 后端查询层 rowMode 分组 + 分页正确性

**Files:**
- Modify: `CP6.Core/Services/Oa/IInboxService.cs`（PendingAsync 签名）
- Modify: `CP6.Core/Services/Oa/InboxService.cs`（PendingAsync 实现）
- Modify: `CP6.WebApi/Controllers/Oa/InboxController.cs`（pending 端点参数 + 注入 IPrefService）
- Test: `CP6.Tests/Oa/PendingRowModeTests.cs`、`CP6.Tests/Oa/PrefMergeTests.cs`（GetRowModeAsync 用例追加）

**Interfaces:**
- Consumes: `IPrefService.GetRowModeAsync`（A-T2 已实现，本任务补测试与消费方）；`DoneAsync` 既有合并口径（`GroupBy(InstanceId)→OrderByDescending→First`，R5）。
- Produces: `Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null)`；`GET /api/oa/inbox/pending?rowMode=&page=&pageSize=`（rowMode 缺省 → 读**查看者本人**（me，非 act-as 被代理人）的偏好）。D-T2 前端与 B-T1 preview 依赖。
- **不变量**：merged 分组**先于** Skip/Take（同实例任务永不跨页出现两行）；expanded = 现状逐任务行；page/pageSize 缺省 = 全量（现状零变化）。

- [ ] **Step 1: 写失败测试**

```csharp
// CP6.Tests/Oa/PendingRowModeTests.cs
using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Xunit;

namespace CP6.Tests.Oa;

public class PendingRowModeTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));
    private static IInboxService Inbox(CP6Context db) => new InboxService(db, Engine(db),
        new ForecastService(db, new ApproverResolver(db), new ApprovalStagePlanner(new ApproverResolver(db))));

    /// <summary>并行三分支同审批人 → 同实例 3 个 Pending 任务（多状态多行素材）。返回 instanceId。</summary>
    private static async Task<Guid> SeedParallelSameApproverAsync(CP6Context db, Guid starter, Guid approver, string flowKey)
    {
        if (!await db.Sys_Users.AnyAsync(u => u.Id == starter))
            db.Sys_Users.Add(new Sys_User { Id = starter, UserName = "s", NickName = "发起人", Password = "x" });
        if (!await db.Sys_Users.AnyAsync(u => u.Id == approver))
            db.Sys_Users.Add(new Sys_User { Id = approver, UserName = "a", NickName = "审批人", Password = "x" });
        var schema = new FlowSchema
        {
            Nodes =
            {
                new FlowNode { Id = "split", Type = "parallelSplit" },
                new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n2", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "n3", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
                new FlowNode { Id = "join", Type = "parallelJoin" },
                new FlowNode { Id = "end", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "split", To = "n1" },
                new FlowEdge { From = "split", To = "n2" },
                new FlowEdge { From = "split", To = "n3" },
                new FlowEdge { From = "n1", To = "join" },
                new FlowEdge { From = "n2", To = "join" },
                new FlowEdge { From = "n3", To = "join" },
                new FlowEdge { From = "join", To = "end" },
            },
        };
        db.Wf_FlowDefs.Add(new Wf_FlowDef { Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = flowKey,
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = true });
        await db.SaveChangesAsync();
        return await Engine(db).SubmitAsync(flowKey, starter, "{}");
    }

    /// <summary>把用户全部 Pending 任务的 CreateDate 摆成确定性阶梯（排序/分页稳定）。</summary>
    private static async Task StaircaseAsync(CP6Context db, Guid approver)
    {
        var tasks = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderBy(t => t.Id).ToListAsync();
        var baseline = new DateTime(2026, 7, 1, 8, 0, 0);
        for (var i = 0; i < tasks.Count; i++) tasks[i].CreateDate = baseline.AddMinutes(i);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Merged_SameInstanceThreeTasks_CollapsesToOneRow_LatestState()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instId = await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "merged");

        var row = Assert.Single(rows);
        Assert.Equal(instId, row.InstanceId);
        // 显最新态：合并行 = CreateDate 最大的那个任务
        var latest = await db.Wf_FlowTasks.Where(t => t.AssigneeId == approver)
            .OrderByDescending(t => t.CreateDate).FirstAsync();
        Assert.Equal(latest.Id, row.TaskId);
    }

    [Fact]
    public async Task Expanded_SameInstanceThreeTasks_ThreeRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        var rows = await Inbox(db).PendingAsync(approver, rowMode: "expanded");
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(r => r.TaskId).Distinct().Count());
    }

    [Fact]
    public async Task Default_IsMerged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");

        Assert.Single(await Inbox(db).PendingAsync(approver));   // 缺省参数 = merged（spec D5）
    }

    // ── 分页正确性：同实例 3 任务跨页界（spec §7）──
    [Fact]
    public async Task Merged_Paging_GroupsBeforeSkipTake_NoInstanceStraddlesPages()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        var instA = await SeedParallelSameApproverAsync(db, starter, approver, "parA");   // 3 任务
        var instB = await SeedParallelSameApproverAsync(db, starter, approver, "parB");   // 3 任务
        await StaircaseAsync(db, approver);                                                // A(0-2分) < B(3-5分)

        var page1 = await Inbox(db).PendingAsync(approver, "merged", page: 1, pageSize: 1);
        var page2 = await Inbox(db).PendingAsync(approver, "merged", page: 2, pageSize: 1);
        var page3 = await Inbox(db).PendingAsync(approver, "merged", page: 3, pageSize: 1);

        Assert.Equal(instB, Assert.Single(page1).InstanceId);   // 分组后按最新 CreateDate 倒序
        Assert.Equal(instA, Assert.Single(page2).InstanceId);
        Assert.Empty(page3);                                     // 若分组晚于分页会错误地出现第 3 页
    }

    [Fact]
    public async Task Expanded_Paging_SkipTakeOverTaskRows()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        await StaircaseAsync(db, approver);

        var page1 = await Inbox(db).PendingAsync(approver, "expanded", page: 1, pageSize: 2);
        var page2 = await Inbox(db).PendingAsync(approver, "expanded", page: 2, pageSize: 2);

        Assert.Equal(2, page1.Count);
        Assert.Single(page2);
        Assert.Equal(3, page1.Concat(page2).Select(r => r.TaskId).Distinct().Count());   // 无重复无遗漏
    }

    [Fact]
    public async Task NoPaging_ReturnsAll_BehaviourUnchanged()
    {
        using var db = NewDb();
        var starter = Guid.NewGuid(); var approver = Guid.NewGuid();
        await SeedParallelSameApproverAsync(db, starter, approver, "par");
        Assert.Equal(3, (await Inbox(db).PendingAsync(approver, "expanded")).Count);
    }
}
```

`PrefMergeTests.cs` 追加 GetRowModeAsync 用例：

```csharp
    // ── GetRowModeAsync（D-T1 消费）──
    [Theory]
    [InlineData(null, "merged")]                                  // 无行 → 默认
    [InlineData("{}", "merged")]                                  // 无键 → 默认
    [InlineData("""{"rowMode":"expanded"}""", "expanded")]
    [InlineData("""{"rowMode":"merged"}""", "merged")]
    [InlineData("""{"rowMode":"garbage"}""", "merged")]           // 非法值 → 默认
    [InlineData("NOT_JSON{{{", "merged")]                         // 畸形 → 默认
    public async Task GetRowMode_ParsesTopLevelKey_DefaultMerged(string? prefsJson, string expected)
    {
        using var db = NewDb();
        var me = Guid.NewGuid();
        if (prefsJson is not null)
        {
            db.Wf_InboxPrefs.Add(new Wf_InboxPref { Id = Guid.NewGuid(), UserId = me, PrefsJson = prefsJson });
            await db.SaveChangesAsync();
        }
        Assert.Equal(expected, await Svc(db).GetRowModeAsync(me));
    }
```

- [ ] **Step 2: 跑测试验证 FAIL** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "PendingRowModeTests"`。预期：编译失败（PendingAsync 无该签名）。

- [ ] **Step 3: 实现**

`IInboxService.cs` 的 PendingAsync 行替换为：

```csharp
    // ── 未處理（T5 + wfs-inbox-ux §5 rowMode）──
    // rowMode: "merged"(默认，同实例多任务合并一行显最新态) | "expanded"(逐任务平铺)。
    // page/pageSize 可选（null=全量，现状不变）；merged 下分组先于分页（跨页正确性）。
    Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null);
```

`InboxService.cs` 的 `PendingAsync` 方法签名改为上式，方法体在 `.ToListAsync()`（:30）与「Batch-load frozen stage plans」段（:32）之间插入分组+分页（分组口径逐字照 `DoneAsync:143-144`）：

```csharp
        // ── rowMode（wfs-inbox-ux §5）：merged=同实例合并取最新（照 DoneAsync 既有口径）；分组先于分页 ──
        if (rowMode != "expanded")
            rows = rows.GroupBy(x => x.i.Id)
                       .Select(g => g.OrderByDescending(x => x.t.CreateDate).First())
                       .OrderByDescending(x => x.t.CreateDate)
                       .ToList();
        if (page is { } p && pageSize is { } ps && p >= 1 && ps >= 1)
            rows = rows.Skip((p - 1) * ps).Take(ps).ToList();
```

（`rows` 为匿名类型列表，`var` 推断不变；后续 stage plan 批量加载与投影零改。）

`InboxController.cs`：ctor 注入 `IPrefService`（字段 `_pref`，构造参数照既有风格追加）；`Pending` action 替换为：

```csharp
    [HttpGet("pending")]
    public async Task<IActionResult> Pending([FromQuery] string? rowMode = null,
        [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        try
        {
            var (eff, _) = await EffectiveAsync();
            // 显示偏好属查看者本人（me），与 act-as 被代理人（eff）无关
            var me = await CurrentUserIdAsync();
            var mode = rowMode is "merged" or "expanded" ? rowMode : await _pref.GetRowModeAsync(me);
            return Ok2(await _inbox.PendingAsync(eff, mode, page, pageSize));
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
```

（B-T1 的 `BatchTransferPreviewAsync` 若此前以旧签名调用 `PendingAsync(fromUserId)`，本步改为 `PendingAsync(fromUserId, rowMode: "expanded")`——见 B-T1 注。）

- [ ] **Step 4: 跑测试验证 PASS** — `dotnet test CP6.Tests/CP6.Tests.csproj --filter "PendingRowModeTests|PrefMergeTests"`。

- [ ] **Step 5: 回归闸 + commit** — **关键回归**：既有 `InboxServiceTests.Pending_*` / `SerialInboxDtoTests` 等均为单任务实例 → merged 分组对其无观察差异，必须照绿；C5 冲突已登记（多任务同实例场景默认行为变化 = spec D5 明文要求）。

```bash
dotnet test CP6.Tests/CP6.Tests.csproj --filter "Oa|Wf"
git add -A && git commit -m "feat(wfs-inbox): D-T1 PendingAsync rowMode合并/平铺+分组先于分页+端点偏好回落"
```

---

### Task D-T2: 列表工具栏 rowMode 切换开关（写回偏好）

**Files:**
- Modify: `cp6.web/src/views/oa/inbox/inboxModel.ts`（+ 既有 `inboxModel.test.ts` 追加用例）
- Modify: `cp6.web/src/api/oa/inbox.ts`
- Modify: `cp6.web/src/views/oa/inbox/InboxPending.vue`
- Modify: `cp6.web/src/views/oa/inbox/FormDetail.vue`（pending 调用固定 expanded，行为保真）

**Interfaces:**
- Consumes: `GET /oa/inbox/pending?rowMode=`（D-T1）、`prefApi.saveMerge`（A-T4）、`prefApi.get`。
- Produces: `parseRowMode(prefsJson): 'merged'|'expanded'`（共享契约）；`inboxApi.pending(rowMode?)`。

- [ ] **Step 1: 写失败 vitest** — `cp6.web/src/views/oa/inbox/inboxModel.test.ts` 追加：

```ts
import { parseRowMode } from './inboxModel'

describe('parseRowMode', () => {
  it('缺省/缺键/非法/畸形 → merged', () => {
    expect(parseRowMode(undefined)).toBe('merged')
    expect(parseRowMode('')).toBe('merged')
    expect(parseRowMode('{}')).toBe('merged')
    expect(parseRowMode('{"rowMode":"weird"}')).toBe('merged')
    expect(parseRowMode('NOT_JSON{{{')).toBe('merged')
  })
  it('expanded 显式识别', () => {
    expect(parseRowMode('{"rowMode":"expanded"}')).toBe('expanded')
    expect(parseRowMode('{"rowMode":"merged"}')).toBe('merged')
  })
})
```

（`describe/it/expect` 该文件既有 import 复用。）

- [ ] **Step 2: 跑验证 FAIL** — `cd cp6.web && npm run test -- inboxModel`。

- [ ] **Step 3: 实现**

`inboxModel.ts` 末尾追加：

```ts
/** rowMode 显示偏好解析（wfs-inbox-ux §5）：PrefsJson 顶层 rowMode 键；缺省/非法/畸形 → merged。 */
export function parseRowMode(prefsJson: string | undefined): 'merged' | 'expanded' {
  if (!prefsJson) return 'merged'
  try {
    const parsed = JSON.parse(prefsJson)
    return parsed?.rowMode === 'expanded' ? 'expanded' : 'merged'
  } catch {
    return 'merged'
  }
}
```

`api/oa/inbox.ts` 的 `pending` 行替换：

```ts
  pending:   (rowMode?: 'merged' | 'expanded') => http.get('/oa/inbox/pending', { params: { rowMode } }),
```

（axios 自动省略 undefined 参数 → 既有无参调用点走后端偏好回落，零变化。）

`InboxPending.vue`：

(a) review 面板 `.table-toolbar`（:6-9）追加开关（刷新按钮之后）：

```html
          <el-radio-group v-model="rowMode" size="small" class="rowmode-toggle" @change="onRowModeChange">
            <el-radio-button label="merged">{{ t('oa.inbox.rowMode.merged') }}</el-radio-button>
            <el-radio-button label="expanded">{{ t('oa.inbox.rowMode.expanded') }}</el-radio-button>
          </el-radio-group>
```

(b) 脚本：

```ts
import { prefApi } from '@/api/oa/pref'
import { parseRowMode } from '@/views/oa/inbox/inboxModel'

// ── rowMode（wfs-inbox-ux §5：切换即写回偏好 + 重载列表）──
const rowMode = ref<'merged' | 'expanded'>('merged')

async function initRowMode() {
  try {
    const res: any = await prefApi.get()
    rowMode.value = parseRowMode(res.data?.prefsJson)
  } catch {
    // 默认 merged
  }
}

async function onRowModeChange() {
  try {
    await prefApi.saveMerge(JSON.stringify({ rowMode: rowMode.value }))   // 顶层键合并：不碰 notify/pageSize 等
  } catch {
    // HTTP interceptor auto-toasts；写回失败不阻塞本次切换显示
  }
  await loadReview()
}
```

(c) `loadReview` 的取数行改为 `const res = await inboxApi.pending(rowMode.value)`；`onMounted(loadReview)` 改为：

```ts
onMounted(async () => {
  await initRowMode()
  await loadReview()
})
```

(d) `<style scoped>` 追加：

```css
.rowmode-toggle {
  margin-left: auto;
}
```

`FormDetail.vue`：`loadDetail` 内 `inboxApi.pending()`（:172）改为 `inboxApi.pending('expanded')`——详情页找「我的可办任务」需逐任务粒度，不随显示偏好合并（行为保真）。

- [ ] **Step 4: 验证 + commit**

```bash
cd cp6.web && npm run test && npm run type-check && npm run build
git add -A && git commit -m "feat(wfs-inbox): D-T2 待办工具栏rowMode切换开关+偏好写回+详情页expanded保真"
```

---

## Wave X-E — i18n + QA + DoD（依赖 X-A/X-B/X-C/X-D 全部完成）

### Task E-T1: i18n 五语 seed（39 键）

**Files:**
- Create: `CP6.WebApi/Seed/I18nOaInboxUxScreenSeed.cs`
- Modify: `CP6.WebApi/Program.cs`（concat 链 :1819 之后加一行）

**Interfaces:**
- Consumes: `Sys_Lang`（`CP6.Entity.DomainModels.Sys`，五列 `ZhCN/ZhTW/En/Ja/Ko`）；concat 链既有双层去重（`.Where(!existingKeys)` + `GroupBy(LangKey)`，:1820-1821）。
- Produces: 前四波全部 `t('...')` 键。键名已与既有 seed 全量比对不冲突（`oa.notify.settings.*`/`oa.notify.title` 等在 `I18nOaNotifyScreenSeed`；`oa.transfer.*` 在既有 seed——本 seed 全部为新前缀 `oa.notify.matrix.*` / `oa.notify.type.*` / `oa.bt.*` / `oa.inbox.rowMode.*` / `oa.inbox.mobileFilter` / `oa.pref.errBadJson`）。

- [ ] **Step 1: 写 seed 文件**（照 `I18nOaInboxScreenSeed.cs` 逐字结构）

```csharp
// CP6.WebApi/Seed/I18nOaInboxUxScreenSeed.cs
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>WFS 信箱体验增强（wfs-inbox-ux）五语键：通知矩阵 / 批量改派 / rowMode / 移动端。</summary>
public static class I18nOaInboxUxScreenSeed
{
    public static readonly Sys_Lang[] Items = new[]
    {
        // ── 通知矩阵（设置页 notify tab，A-T4）──
        new Sys_Lang { LangKey = "oa.notify.matrix.colType", ZhCN = "通知类型", ZhTW = "通知類型", En = "Type", Ja = "通知タイプ", Ko = "알림 유형" },
        new Sys_Lang { LangKey = "oa.notify.matrix.colInApp", ZhCN = "站内", ZhTW = "站內", En = "In-app", Ja = "アプリ内", Ko = "앱 내" },
        new Sys_Lang { LangKey = "oa.notify.matrix.colEmail", ZhCN = "邮件", ZhTW = "郵件", En = "Email", Ja = "メール", Ko = "이메일" },
        new Sys_Lang { LangKey = "oa.notify.matrix.unsupported", ZhCN = "该类型暂无此通道动作", ZhTW = "該類型暫無此通道動作", En = "No action wired for this channel", Ja = "このチャネルの動作は未接続です", Ko = "이 채널에 연결된 동작이 없습니다" },
        new Sys_Lang { LangKey = "oa.notify.matrix.reset", ZhCN = "恢复默认", ZhTW = "恢復預設", En = "Reset to default", Ja = "デフォルトに戻す", Ko = "기본값 복원" },
        new Sys_Lang { LangKey = "oa.notify.matrix.resetOk", ZhCN = "已恢复默认（全部开启）", ZhTW = "已恢復預設（全部開啟）", En = "Reset to default (all on)", Ja = "デフォルト（すべてオン）に戻しました", Ko = "기본값(모두 켜짐)으로 복원했습니다" },
        new Sys_Lang { LangKey = "oa.notify.matrix.saveOk", ZhCN = "通知设定已保存", ZhTW = "通知設定已儲存", En = "Notification settings saved", Ja = "通知設定を保存しました", Ko = "알림 설정을 저장했습니다" },

        // ── 通知类型行标签（数据驱动 t('oa.notify.type.'+typeKey)；branchPruned 预留：hardening 合入即用）──
        new Sys_Lang { LangKey = "oa.notify.type.todoCreated", ZhCN = "新待办", ZhTW = "新待辦", En = "New to-do", Ja = "新規TODO", Ko = "새 할 일" },
        new Sys_Lang { LangKey = "oa.notify.type.flowApproved", ZhCN = "签核通过", ZhTW = "簽核通過", En = "Flow approved", Ja = "承認完了", Ko = "승인 완료" },
        new Sys_Lang { LangKey = "oa.notify.type.flowRejected", ZhCN = "流程驳回", ZhTW = "流程駁回", En = "Flow rejected", Ja = "却下", Ko = "반려" },
        new Sys_Lang { LangKey = "oa.notify.type.timeout", ZhCN = "超时提醒", ZhTW = "超時提醒", En = "Timeout reminder", Ja = "タイムアウト通知", Ko = "시간 초과 알림" },
        new Sys_Lang { LangKey = "oa.notify.type.branchPruned", ZhCN = "分支剪枝", ZhTW = "分支剪枝", En = "Branch pruned", Ja = "ブランチ剪定", Ko = "분기 정리" },

        // ── 偏好保存 ──
        new Sys_Lang { LangKey = "oa.pref.errBadJson", ZhCN = "偏好格式错误", ZhTW = "偏好格式錯誤", En = "Invalid preferences payload", Ja = "設定の形式が不正です", Ko = "설정 형식이 잘못되었습니다" },

        // ── 批量改派（B-T3）──
        new Sys_Lang { LangKey = "oa.bt.entry", ZhCN = "批量改派", ZhTW = "批次改派", En = "Batch transfer", Ja = "一括再割当", Ko = "일괄 재배정" },
        new Sys_Lang { LangKey = "oa.bt.title", ZhCN = "在途批量改派", ZhTW = "在途批次改派", En = "Batch transfer pending tasks", Ja = "進行中タスクの一括再割当", Ko = "진행 중 작업 일괄 재배정" },
        new Sys_Lang { LangKey = "oa.bt.fromUser", ZhCN = "转出人", ZhTW = "轉出人", En = "From user", Ja = "移譲元", Ko = "이관자" },
        new Sys_Lang { LangKey = "oa.bt.toUser", ZhCN = "接收人", ZhTW = "接收人", En = "To user", Ja = "移譲先", Ko = "수신자" },
        new Sys_Lang { LangKey = "oa.bt.comment", ZhCN = "备注", ZhTW = "備註", En = "Comment", Ja = "コメント", Ko = "비고" },
        new Sys_Lang { LangKey = "oa.bt.commentHint", ZhCN = "例如：离职移交", ZhTW = "例如：離職移交", En = "e.g. offboarding handover", Ja = "例：退職引継ぎ", Ko = "예: 퇴사 인수인계" },
        new Sys_Lang { LangKey = "oa.bt.filterFlowKey", ZhCN = "限定流程", ZhTW = "限定流程", En = "Flow filter", Ja = "対象フロー", Ko = "대상 플로우" },
        new Sys_Lang { LangKey = "oa.bt.filterBefore", ZhCN = "此时间前发出", ZhTW = "此時間前發出", En = "Sent before", Ja = "この時刻以前に送付", Ko = "이 시각 이전 발송" },
        new Sys_Lang { LangKey = "oa.bt.preview", ZhCN = "预览待转清单", ZhTW = "預覽待轉清單", En = "Preview", Ja = "対象をプレビュー", Ko = "대상 미리보기" },
        new Sys_Lang { LangKey = "oa.bt.previewTotal", ZhCN = "待转 {n} 条", ZhTW = "待轉 {n} 條", En = "{n} task(s) to transfer", Ja = "移譲対象 {n} 件", Ko = "이관 대상 {n}건" },
        new Sys_Lang { LangKey = "oa.bt.previewEmpty", ZhCN = "无符合条件的待办", ZhTW = "無符合條件的待辦", En = "No matching pending tasks", Ja = "条件に合うタスクがありません", Ko = "조건에 맞는 작업이 없습니다" },
        new Sys_Lang { LangKey = "oa.bt.confirm", ZhCN = "确认改派", ZhTW = "確認改派", En = "Transfer", Ja = "再割当を実行", Ko = "재배정 실행" },
        new Sys_Lang { LangKey = "oa.bt.resultSummary", ZhCN = "共 {total} 条，成功 {ok} 条，失败 {fail} 条", ZhTW = "共 {total} 條，成功 {ok} 條，失敗 {fail} 條", En = "{total} total, {ok} succeeded, {fail} failed", Ja = "全 {total} 件、成功 {ok} 件、失敗 {fail} 件", Ko = "총 {total}건, 성공 {ok}건, 실패 {fail}건" },
        new Sys_Lang { LangKey = "oa.bt.allOk", ZhCN = "全部改派成功", ZhTW = "全部改派成功", En = "All transferred", Ja = "すべて再割当しました", Ko = "모두 재배정했습니다" },
        new Sys_Lang { LangKey = "oa.bt.colTask", ZhCN = "任务", ZhTW = "任務", En = "Task", Ja = "タスク", Ko = "작업" },
        new Sys_Lang { LangKey = "oa.bt.colFlow", ZhCN = "流程", ZhTW = "流程", En = "Flow", Ja = "フロー", Ko = "플로우" },
        new Sys_Lang { LangKey = "oa.bt.colError", ZhCN = "失败原因", ZhTW = "失敗原因", En = "Error", Ja = "失敗理由", Ko = "실패 사유" },
        new Sys_Lang { LangKey = "oa.bt.retry", ZhCN = "重试", ZhTW = "重試", En = "Retry", Ja = "再試行", Ko = "재시도" },
        new Sys_Lang { LangKey = "oa.bt.retryOk", ZhCN = "重试成功", ZhTW = "重試成功", En = "Retry succeeded", Ja = "再試行に成功しました", Ko = "재시도 성공" },
        new Sys_Lang { LangKey = "oa.bt.retryGone", ZhCN = "该任务已被办结或转走", ZhTW = "該任務已被辦結或轉走", En = "Task already handled or transferred", Ja = "タスクは既に処理済みまたは移譲済みです", Ko = "이미 처리되었거나 이관된 작업입니다" },
        new Sys_Lang { LangKey = "oa.bt.errSameUser", ZhCN = "转出人与接收人不能相同", ZhTW = "轉出人與接收人不能相同", En = "From and to user must differ", Ja = "移譲元と移譲先は同一にできません", Ko = "이관자와 수신자는 같을 수 없습니다" },
        new Sys_Lang { LangKey = "oa.bt.errTargetInvalid", ZhCN = "接收人不存在或已停用", ZhTW = "接收人不存在或已停用", En = "Target user missing or disabled", Ja = "移譲先が存在しないか無効です", Ko = "수신자가 없거나 비활성화되었습니다" },
        new Sys_Lang { LangKey = "oa.bt.errTooMany", ZhCN = "待转数量超过单批上限 500，请用过滤条件分批", ZhTW = "待轉數量超過單批上限 500，請用過濾條件分批", En = "Over the 500-per-batch cap, narrow with filters", Ja = "1回の上限500件を超えています。条件で分割してください", Ko = "1회 상한 500건을 초과했습니다. 필터로 나눠 주세요" },

        // ── rowMode + 移动端（C-T1 / D-T2）──
        new Sys_Lang { LangKey = "oa.inbox.rowMode.merged", ZhCN = "合并显示", ZhTW = "合併顯示", En = "Merged", Ja = "集約表示", Ko = "병합 표시" },
        new Sys_Lang { LangKey = "oa.inbox.rowMode.expanded", ZhCN = "逐任务显示", ZhTW = "逐任務顯示", En = "Per task", Ja = "タスク別表示", Ko = "작업별 표시" },
        new Sys_Lang { LangKey = "oa.inbox.mobileFilter", ZhCN = "筛选", ZhTW = "篩選", En = "Filters", Ja = "絞り込み", Ko = "필터" },
    };
}
```

- [ ] **Step 2: Program.cs concat** — :1819（`I18nOaServiceTaskScreenSeed` 行）之后插一行：

```csharp
            .Concat(CP6.WebApi.Seed.I18nOaInboxUxScreenSeed.Items)  // WFS 信箱体验增强：通知矩阵/批量改派/rowMode/移动端
```

（尾部 `.Where(!existingKeys)` + `GroupBy(LangKey)` 既有去重兜底；`SeedLangs` 幂等。）

- [ ] **Step 3: build 验证 + commit**

```bash
dotnet build CP6.WebApi/CP6.WebApi.csproj
git add -A && git commit -m "feat(wfs-inbox): E-T1 I18nOaInboxUxScreenSeed 五语39键+concat"
```

---

### Task E-T2: gstack QA harness（只写不跑；live QA 用户在场时执行）

**Files:**
- Create: `docs/superpowers/qa/wfs-inbox-ux/README.md`
- Create: `docs/superpowers/qa/wfs-inbox-ux/seed.sql`
- Create: `docs/superpowers/qa/wfs-inbox-ux/qa_inbox_ux.ps1`

照 `docs/superpowers/qa/wfs-service-task/`（README+seed+ps1）与 `wfs-serial-signing` 既有模式：seed.sql 用单数表名（`Wf_FlowDef`/`Wf_FlowTask`…）+ `SET QUOTED_IDENTIFIER ON`；ps1 走 HTTP e2e（登录取 Cookie + CSRF 双提交头）；隔离库 `CP6DB_OA`（真 SQL Server），**harness 只写不跑服务器**。

- [ ] **Step 1: 写 README 剧本**（六幕，覆盖 spec §7 QA 行）：
  1. **通知矩阵→跳过验证**：设置页关 `flowRejected`×`email` → 发起并驳回一单 → `Wf_Notification` 有 Type=3 行、**无邮件**（Dev 环境 `LogEmailSender` 日志无 send 记录）；再关 `flowRejected`×`inApp` → 再驳回 → 无新 Type=3 行。timeout 行双格灰置禁用可见（tooltip 文案）。恢复默认后全通道恢复。
  2. **遗留数据兼容**：SQL 直写旧扁平 `{"notify":{"todo":false}}` → 触发新待办 → 无 Type=1 行且无邮件（C2 兼容回归）；打开设置页看矩阵 todoCreated 行显示双关（回落解析）。
  3. **批量改派全流程含失败重试**：seed 30 条 Pending 压给 from（其中 1 条办结制造脏数据）→ FlowAdmin「批量改派」→ 预览（29 条 + 抽样 10）→ 确认 → 报告 29 成功 0 失败；再点名已办结 task（SQL 复原一条为 Pending 后由第二会话抢先办结）演示失败明细 + 单条重试同口径。校验 `Wf_FlowHistory(action=transfer, ActorId=admin)` 与 FormTo 双行、`Sys_OperLog` 有 POST 行。无权限用户（RoleId≠1 测试角色）调端点 → 403 `无权限：oa-inbox:batch-transfer`。
  4. **rowMode**：seed 并行三分支同审批人实例 → 列表 merged=1 行、切 expanded=3 行 → 刷新页面偏好持久（PrefsJson.rowMode）→ 详情页操作不受显示偏好影响。
  5. **移动端 375px 三页走查**（gstack browse 真浏览器，viewport 375×812）：列表卡片流+文件夹横滑条+筛选抽屉、详情堆叠+钉底操作栏（同意/驳回/转交/退回可点）、转交对话框全屏；截图存本目录。
  6. **桌面 1280px 像素走查**：同三页 + 设置页，对照改造前（零回归；重点：表格列宽、action-bar 非 sticky、抽屉 60%）。
- [ ] **Step 2: 写 seed.sql** — 幂等（`IF NOT EXISTS`）：QA 租户下 from/to/admin 三用户、`leave` 线性流程与 `par3` 并行三分支流程 FlowDef、30 单 Pending 数据（存储过程式 WHILE 循环 INSERT `Wf_FlowInstance`/`Wf_FlowToken`/`Wf_FlowTask`/`Wf_FlowFormTo`，字段口径照 `wfs-serial-signing/seed.sql` 既有列清单）。
- [ ] **Step 3: 写 qa_inbox_ux.ps1** — 幕 1~4 的 HTTP e2e：登录 → `POST /api/oa/pref/save`（merge 矩阵）→ 发起/驳回（`/api/oa/inbox/batch` reject）→ 查 `/api/oa/notify/list` 断言；`batch-transfer/preview` + `batch-transfer` + 断言报告数字；`pending?rowMode=` 两态行数断言。ASCII 数据、`-SkipCertificateCheck`、失败 `exit 1`。
- [ ] **Step 4: commit** — `git add -A && git commit -m "test(wfs-inbox): E-T2 gstack QA harness(6幕剧本+seed+e2e脚本,只写不跑)"`

---

### Task E-T3: DoD 验收（全量闸）

- [ ] 后端全量：`dotnet test CP6.Tests/CP6.Tests.csproj` → **1509+N 通过（5 skip）**，零失败零新 skip。
- [ ] 前端全量：`cd cp6.web && npm run test`（**320+N 全绿**）+ `npm run type-check` + `npm run build`。
- [ ] **零 EF 迁移**：`dotnet ef migrations has-pending-model-changes --project CP6.Core/CP6.Core.csproj --startup-project CP6.WebApi/CP6.WebApi.csproj --context CP6Context` → clean；`git diff --stat main..HEAD` 无 `CP6.Core/Migrations`、无 `CP6.Entity` 改动。
- [ ] **零跨模块污染**：`git diff --stat main..HEAD` 无 `views/space`/`*Space*`/WMS/ERP/FIN/MES 文件；`composables/useBreakpoint.ts` 零改动（只消费）。
- [ ] 引擎零改动：`git diff main..HEAD -- CP6.Core/Services/Wf/` 为空（`TransferAsync` 只调用）。
- [ ] spec §7 测试矩阵逐条对号（见下「覆盖核对」）。
- [ ] QA harness 三件套齐；live QA（用户在场，隔离库 CP6DB_OA + 前端 dev server）另行排期——移动端 375px 走查与桌面像素走查在 live QA 完成。
- [ ] `git log --oneline main..HEAD` 提交信息全部 `feat(wfs-inbox)|test(wfs-inbox)` 前缀。

### 覆盖核对（spec §7 → 任务/测试）

| spec §7 条目 | 任务 | 测试 |
|---|---|---|
| 三态坍缩默认真 | A-T1 | `NotifyMatrixTests.IsEnabled_ThreeStateCollapse_DefaultsTrue` |
| 各类型×通道跳过矩阵 | A-T3 | `PersistentWfNotifierTests.{InAppOff_*, EmailOff_*, BothOff_*, TypesIndependent_*}` |
| 合并写不覆盖他键 | A-T2 | `PrefMergeTests.SaveMerge_PatchesTopLevelKey_PreservesOthers` 等 4 例 |
| 缓存不跨请求 | A-T2 | `PrefMergeTests.IsEnabledAsync_CachesWithinInstance_NotAcrossInstances` |
| 遗留数据兼容（D2 向后兼容） | A-T1/A-T3 | `IsEnabled_LegacyFlat_*` / `LegacyFlat_*` + QA 幕 2 |
| 逐条事务部分成功 + 失败明细 | B-T1 | `BatchTransferTests.Batch_PartialSuccess_*` / `Batch_MidLoopFailure_*` |
| 上限 500 | B-T1 | `Batch_Over500_Rejected_WithHintKey` |
| from==to 拒 | B-T1 | `Batch_FromEqualsTo_Rejected` |
| 跨租户拒 | B-T1 | `Batch_TargetCrossTenant_Rejected_SamePathAsMissing`（+停用/不存在两例） |
| 审计行齐全 | B-T1/B-T2 | `Batch_WritesEngineAudit_*` + OperLogFilter（全局既有）+ QA 幕 3 |
| TransferAsync 语义不变回归 | B-T1 | `--filter "Oa|Wf"` 全量闸（引擎零 diff） |
| rowMode 跨页分页正确性（同实例 3 任务跨页界） | D-T1 | `PendingRowModeTests.Merged_Paging_GroupsBeforeSkipTake_*` / `Expanded_Paging_*` |
| rowMode 偏好写回 | D-T2 | `parseRowMode` vitest + QA 幕 4 |
| 移动端 375px 三页走查 | E-T2 | QA 幕 5（真浏览器） |
| 桌面像素零回归 | E-T2 | QA 幕 6 + 每任务 build/test 闸 |

### 执行顺序与依赖

A-T1 → A-T2 → A-T3 → A-T4（波内顺序）；B-T1 → B-T2 → B-T3；C-T1 → C-T2；D-T1 → D-T2。四波之间无契约依赖可并行（同分支顺序执行推荐 A→B→D→C：B-T1 preview 消费 D-T1 签名的默认参兼容已在 B-T1 注明，两序皆编译）；E-T1 → E-T2 → E-T3 收尾，依赖前四波全部合入。

---

*生成于 2026-07-05。铁律：引擎动作（TransferAsync）只调用不改动；spec 冲突登记 C1~C8 不改 spec 只按登记口径实现；每 Task `git show --stat` 复核零 Space/跨模块污染。*






---

## 波④完成记录（2026-07-13，fable 终审 Ready 零必修，终审报告 .superpowers/sdd/w4-final-review-report.md）

14 任务全完成；后端 2028 绿/5 skip、前端 434 绿/type-check 0/build 过；六项硬不变量（引擎零改动/零迁移零 Entity/零跨模块/桌面像素零回归/C1~C8 登记口径/全量闸）终审亲证；接缝 a~h 全过。

### 跟踪票（6 项）

1. **B-T1 preview UX**：TaskIds 模式脏项入 Total 不入 Sample + Sample 排序继承 Pending 降序非「前10」直觉序（UX 呈现层，B-T3 retryGone 已部分缓解）。
2. **真相源计数漂移**：`docs/seeds/oawf-permission-keys.md` 计数 37/39→39/41 待补（B-T2 重基线后未随波更新，既有惯例缺口）。
3. **移动端 4px 缝隙**：C-T2 钉底栏 margin -16px vs EP drawer padding 20px——并入 live QA 真机 375px 走查票。
4. **前端整洁 sweep（可并单）**：A-T4 `InboxSettings.savePref` merged/storedRaw 死变量 + `BatchTransferDialog.retryOne` 无 catch（unhandled rejection 控制台噪音）+ InboxPending 跨断点回填全 stale 极窄边界（watch 内先按 ids 过滤 reviewRows 再回填可闭）。
5. **StatsAsync pendingCount 语义**：随 C5 默认 merged 从「任务数」变「实例数」（同实例多待办同人场景可见）——并入 live QA 走查确认产品预期。
6. **live QA 待用户在场**：harness 三件套（docs/superpowers/qa/wfs-inbox-ux/）6 剧本 + 移动端 375px 三页走查 + 桌面像素走查 + 上述 #3/#5 确认项，隔离库 CP6DB_OA。

放行项（不记票）与逐条依据见终审报告 Minor Triage 小节。
