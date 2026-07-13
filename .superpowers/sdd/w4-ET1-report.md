# Task E-T1 报告 — I18nOaInboxUxScreenSeed 五语 seed + Program.cs concat

## 状态
DONE。五语 i18n seed 文件已建、Program.cs concat 链已接线、全量测试绿、迁移洁净。

## 交付文件（仅本任务两文件 + 本报告）
- 新建 `CP6.WebApi/Seed/I18nOaInboxUxScreenSeed.cs`（39 键，五列 ZhCN/ZhTW/En/Ja/Ko，ja/ko 真译）
- 修改 `CP6.WebApi/Program.cs`（concat 链尾部追加一行，在 `I18nSpaceScreenSeed` 之后；尾部既有 `.Where(!existingKeys)` + `GroupBy(LangKey)` 双层去重兜底）

## 真实键数 = 39（计划估计 39，双向对账吻合）
主控交接注记提示 branchPruned 是计划后长出的第 5 行「键数可能 >39」，但计划附带的 seed 清单已含 branchPruned，故最终确为 39，无偏差。

## 键面权威 = 四波实际 t()/错误码引用（双向 grep 对账，零缺零孤儿）

| 前缀 | 键数 | 消费来源（实际引用） | seed 行 |
|---|---|---|---|
| oa.notify.matrix.* | 7 | InboxSettings.vue（colType/colInApp/colEmail/unsupported/reset/resetOk/saveOk） | ✅ 7/7 |
| oa.notify.type.* | 5 | InboxSettings.vue:51 `t('oa.notify.type.'+row.typeKey)`；typeKey 源自后端 `NotifyMatrix.Rows()`：todoCreated/flowApproved/flowRejected/timeout/branchPruned | ✅ 5/5 |
| oa.bt.* | 23 | BatchTransferDialog.vue（20）+ FlowAdmin.vue:12 entry + InboxService.cs 三错误码 errSameUser/errTargetInvalid/errTooMany | ✅ 23/23 |
| oa.inbox.rowMode.* | 2 | InboxPending.vue:10-11（merged/expanded） | ✅ 2/2 |
| oa.inbox.mobileFilter | 1 | InboxDone.vue:26,31 | ✅ 1/1 |
| oa.pref.errBadJson | 1 | PrefService.cs:57,61（抛出） | ✅ 1/1 |
| **合计** | **39** | | **✅ 39/39** |

### 逐键对账（零孤儿：每 seed 行都有消费者）
- **通知矩阵 7 键**：colType, colInApp, colEmail, unsupported, reset, resetOk, saveOk — 全部 InboxSettings.vue 实引。
- **通知类型 5 键**：todoCreated, flowApproved, flowRejected, timeout, branchPruned — 与后端 `NotifyMatrix.Rows()` 反射生成的 5 个 TypeKey 逐位对齐（`NotifyMatrix.cs:40-44` 支持映射表五行）。矩阵行由后端 API 数据驱动，故键源为后端权威。
- **oa.bt 23 键**：entry, title, fromUser, toUser, comment, commentHint, filterFlowKey, filterBefore, preview, previewTotal, previewEmpty, confirm, resultSummary, allOk, colTask, colFlow, colError, retry, retryOk, retryGone（前端 20）+ errSameUser, errTargetInvalid, errTooMany（后端 InboxService.cs:351/354/358 抛出）。
  - 注：errSameUser/errTargetInvalid 前端无 t() 引用（经拦截器 toast 呈现后端错误码）；errTooMany 前端仅 BatchTransferDialog.vue:177 注释提及，实际由拦截器 toast。三者均须播种。
- **rowMode 2 + mobileFilter 1 + errBadJson 1** — 全部实引/实抛。

### 去重（零冲突）
- 全部为新前缀（`oa.notify.matrix.*` / `oa.notify.type.*` / `oa.bt.*` / `oa.inbox.rowMode.*` / `oa.inbox.mobileFilter` / `oa.pref.errBadJson`）。
- `CP6.WebApi/**/*.cs` 全库 grep 这些前缀 → 除本 seed 外零命中，既有 seed（oa.notify.settings.*/oa.transfer.*/oa.flowadmin.*/common.* 等）无一撞键。
- SeedLangs insert-only：全新键在已部署库启动时洁净套用，无需 SQL 补丁。

## 门禁验证
1. `dotnet build CP6.WebApi` → **Build succeeded, 0 Error**（1 既有 warning，非本任务）。
2. `dotnet ef migrations has-pending-model-changes` → **No changes**（零迁移）。
3. `dotnet test CP6.slnx` → **Passed! Failed: 0, Passed: 2028, Skipped: 5, Total: 2033**（精确匹配基线 2028/5）。
4. 双向对账表见上（零缺零孤儿，真实键数 39）。

## 关注点
- 无。seed 纯新键 insert-only，部署库启动即生效，无回填风险。
- branchPruned 类型的 IsEnabled 后端已默认双通道 true（NotifyMatrix.cs:44），前端 t 标签本 seed 已备；生产者 `IWfNotifier.BranchPrunedAsync` 由 hardening 波接入后即端到端可用，标签无需再改。
