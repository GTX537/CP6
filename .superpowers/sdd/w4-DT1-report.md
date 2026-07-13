# Task D-T1 报告：后端查询层 rowMode 分组 + 分页正确性

**STATUS: DONE**
**Commit:** `13570a1`（已 push 至 `feat/wfs-inbox-ux`）
**测试:** 全量 `dotnet test CP6.slnx` = 2028 passed / 5 skipped（基线 2016 + 12 新增）；migration 无 pending model changes。

## 交付内容

严格 TDD（RED→GREEN）落地：

1. **`IInboxService.PendingAsync` 签名扩展**
   `Task<IReadOnlyList<InboxPendingItem>> PendingAsync(Guid userId, string rowMode = "merged", int? page = null, int? pageSize = null)`
   — 默认 merged，page/pageSize 缺省=全量（现状零变化）。

2. **`InboxService.PendingAsync` 实现**
   在 `.ToListAsync()` 与「Batch-load frozen stage plans」段之间插入分组+分页：
   - merged（默认）：`GroupBy(InstanceId) → OrderByDescending(CreateDate).First() → OrderByDescending(CreateDate)`（逐字照 `DoneAsync:143-144` 既有合并口径）。
   - **不变量**：分组先于 Skip/Take（同实例任务永不跨页）；`page/pageSize` 双非空且 ≥1 才分页。

3. **`InboxController.Pending` 端点**
   - 注入 `IPrefService _pref`（ctor 追加参数）。
   - `[FromQuery] rowMode/page/pageSize`；rowMode 显式合法值直用，否则回落 `_pref.GetRowModeAsync(me)`。
   - **me（查看者本人）取偏好，eff（act-as 被代理人）用于查询** —— 显示偏好属查看者，与被代理人无关。

4. **`PrefMergeTests` 追加 `GetRowMode_ParsesTopLevelKey_DefaultMerged`**（6 InlineData：无行/无键/expanded/merged/非法值/畸形 JSON → 全部按 A-T2 既有 `GetRowModeAsync` 语义验证）。`GetRowModeAsync` 由 A-T2 已实现，本任务仅补消费方 + 测试。

## B-T1 接缝验证（C5）

- B-T1 的 `BatchTransferPreviewAsync` 原以单参 `PendingAsync(fromUserId)` 复用列表读模型。新签名默认 merged 后，该调用语义会从「逐任务行」变为「合并行」，将改变多任务同实例场景下 Sample 的抽样口径。
- **按 C5 处置**：将该内部调用显式改为 `PendingAsync(fromUserId, rowMode: "expanded")`，保持 preview Sample 的逐任务行口径不变。
- **B-T1 测试全绿**：`BatchTransferTests` 8 个用例（含 `Preview_ReturnsTotalAndSample_WithoutTransferring`）随 Oa|Wf 过滤（430 绿）与全量（2028 绿）通过。其 seed 每实例仅 1 任务（`SingleAsync(t => t.InstanceId==instId)`），merged 与 expanded 观察等价，但显式 expanded 保证未来多任务同实例场景不回归。

## 一处必要偏离（已记录）

**`PendingRowModeTests.StaircaseAsync` 排序键：`OrderBy(t.Id)` → `OrderBy(t.CreateDate).ThenBy(t.Id)`**

- 简报逐字给出的 `StaircaseAsync` 按 `t.Id`（`Guid.NewGuid()` 随机值）排序铺阶梯。`Merged_Paging` 用例注释假设「A(0-2分) < B(3-5分)」，即先提交实例 instA 的 3 任务全部排在后提交 instB 之前——但该假设仅在 Guid 恰好按插入序排列时成立（作者误以为 Guid 随插入递增，实为随机）。
- 首轮 Oa|Wf 全过滤跑复现失败（instA 偶然抽到最大 Guid → 占据最新 minute → 错误地进 page1），确认为**简报测试的潜在 flaky 缺陷**（隔离跑约 50% 概率过）。
- 根因：`BaseEntity.CreateDate = DateTime.Now` 在对象构造时赋值，.NET8/Windows 高精度时钟使每个任务 CreateDate 严格递增；instB 的任务在第二次 `SubmitAsync`（跨一次 awaited SaveChangesAsync）后构造，严格晚于 instA。原始 CreateDate 天然正确排序，`OrderBy(t.Id)` 反而打乱它。
- 修复：阶梯按创建时序（CreateDate，Id 兜底）铺设，还原简报文档化的「先提交 < 后提交」确定性意图。**连跑 5 次 PendingRowModeTests 全绿**，flaky 消除。仅改测试 helper 一行，断言零改。

## 门禁核对

1. ✅ 新测试通过；全量 `dotnet test CP6.slnx` = 2028 + 5 skipped（含全部 B-T1 用例）。
2. ✅ `dotnet ef migrations has-pending-model-changes` = No changes（zero migration，engine zero-diff）。
3. ✅ `git show --stat HEAD` 仅简报 5 文件（IInboxService / InboxService / InboxController / PendingRowModeTests / PrefMergeTests），未触碰工作区既存的 .superpowers/sdd/*.md。

## 关注点 / Concerns

- **C5 默认行为变更已生效**：merged 现为 PendingAsync 默认。既有单任务实例测试（InboxServiceTests / SerialInboxDtoTests 等）对分组无观察差异，全绿；行为差异仅限「同实例多待办同人」（并行分支/会签同人）场景，属 spec D5 明文要求，建议 D-T2 前端 QA 走查确认。
- **前端消费**：端点已支持 rowMode/page/pageSize，但前端列表当前仍全量拉取（R5 现状）；参数供 D-T2 及后续消费。
- **StaircaseAsync 偏离**：如后续 executor 复核在意「测试逐字」，此一行改动为消除 flaky 的必要工程决策，已如上完整记录。
