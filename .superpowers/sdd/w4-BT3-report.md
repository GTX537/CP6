# Task B-T3 报告：批量改派对话框 UI（流程管理入口 + 预览 + 结果报告 + 单条重试）

**STATUS: DONE**
**Commit:** `bbfe445`（已 push 至 `feat/wfs-inbox-ux`）
**分支:** feat/wfs-inbox-ux

## 交付内容

按 brief 逐字实现，4 个前端文件（后端未触碰）：

| 文件 | 变更 |
|---|---|
| `cp6.web/src/types/oa/inbox.ts` | 追加 `BatchTransferReq` / `BatchTransferItemResult` / `BatchTransferReport` / `BatchTransferPreview`（+26 行）；`BatchTransferPreview.sample` 复用既有 `PendingItem[]` |
| `cp6.web/src/api/oa/inbox.ts` | 文件头 `import type { BatchTransferReq }`；对象内追加 `batchTransfer` / `batchTransferPreview` 两端点（+3 行） |
| `cp6.web/src/views/oa/admin/BatchTransferDialog.vue` | 新建（242 行）—— from/to 用户远程搜索（同 TransferDialog 模式）+ flowKey/beforeUtc 过滤 + 预览步骤（CpTag 总数 + 前 10 条 el-table / CpEmpty）+ 结果报告（成功/失败摘要 + 失败明细表 + 单条重试）。重试走同一 batch-transfer 端点 + `filter.taskIds=[id]`，含「重试成功→移除失败行」「仍失败→最新明细替换」「total===0（已被他人办结/转走）→移除并 info 提示」三分支 |
| `cp6.web/src/views/oa/admin/FlowAdmin.vue` | 入口按钮 + 对话框接线（+7 行） |

## 入口放置决策（context 要求记录）

FlowAdmin.vue 现为 el-tabs 布局（波③ E-T2 加了触发器 tab），brief 的 `#actions` 锚点早于 tab 化。据 brief 意图（流程 tab 的 toolbar），将 `oa.bt.entry` 按钮置于 `#actions` 内、刷新按钮之前，并沿用同 tab 现有约定加 `v-if="activeTab === 'flows'"`（触发器 tab 下不显示批量改派入口）。对话框 `<BatchTransferDialog v-model>` 置于 `</el-tabs>` 之后、`</CpPageShell>` 之前。

## 权限（R4）

入口按钮不做前端隐藏——OA 前端当前无权限位可查。权限由后端 `[RequirePermission("oa-inbox","batch-transfer")]` 强制：未授权用户点确认时由 HTTP 拦截器 toast 后端返回的错误键。符合 house convention 与 brief 明示。

## 门禁验证（全绿）

- `bun run type-check`：0 错误（vue-tsc --build 通过，noUncheckedIndexedAccess 严格模式下无问题）
- `bun run test -- --run`：**432 passed (65 files)**，与基线 432 持平（本任务无新增测试规格——brief Step 4 仅要求 verify + commit）
- `bun run build`：成功（7.44s，仅既有 chunk 体积告警，与本任务无关）
- `git show --stat HEAD`：仅上述 4 个前端文件，无越界

## Concerns

1. **i18n 键依赖 E-T1**：本组件用到 `oa.bt.*`（title/fromUser/toUser/filterFlowKey/filterBefore/comment/commentHint/preview/previewTotal/previewEmpty/confirm/entry/resultSummary/colTask/colFlow/colError/retry/retryOk/retryGone/allOk）及 `oa.col.flowName`/`oa.col.starter`/`oa.col.sentAt`。这些键在 E-T1 seed 落地。E-T1 未合入前，界面会显示原始键名而非译文——属波内既定依赖，非缺陷。
2. **响应体形状差异**：用户搜索走 `res.rows`（/user 分页壳），batch-transfer/preview 走 `res.data`（inbox 直返），与 brief 逐字一致，与既有 inboxApi 消费口径吻合。
3. 后端 2016/5skip 未触碰。
