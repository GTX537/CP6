# `main` 同步候选收敛报告（2026-08-08）

## 当前结论

`codex/main-sync-20260808` 已在 `origin/main@adbe7bcd` 上整体合入 `origin/integration/space-v1-20260730@f8c3bae8`。原始 5 个冲突、客户端心跳时序测试和候选数据库合并包均已在本地收敛；完整代码门禁通过。

候选 merge commit 为 `79fa0301`；本地 Docker 修复 `9ffbf8f4` 已单独摘取为 `0fc6f529`。当前仍未推送候选或修改远端 `main`；下一步由远端备份、受保护 PR 和生产备份恢复副本演练决定是否进入 `main`。

## 分支事实

- 远端 `main`：`adbe7bcdb6c4bd103ba4273e6ca5453f28a11827`，最后提交时间 2026-07-19。
- 集成 tip：`f8c3bae8c7308cea9cfb4940ff6df7527ff638c4`。
- 共同基线：`71073056fbb3688c4558e5a7a62d79b6ce3033e3`。
- 分叉：main-only 3 / integration-only 390；集成侧相对共同基线涉及 1,676 个文件。
- P2.5 Analytics `9b48ffbb` / `dd6637ea` 明确排除；它们没有进入同步候选。

## 5 个冲突的最终规则

1. `FormInitiate.submit.spec.ts`：采用集成侧测试，覆盖 `formApi.submit`、禁止隐式 draft save 和独立 SFS 草稿。
2. `FormInitiate.vue`：采用 `formApi.submit(formKey, model, idempotencyKey)`，删除旧 `flowApi` 导入与提交流程。
3. `06-Todo.md`：保留 GR-VP 已完成事实、Space 待办和 `main` 同步门禁。
4. `CHANGELOG-AI.md`：保留双方历史并新增冲突收敛证据。
5. `PROJECT_STATE.md`：合并 Git/下一动作事实，不整文件覆盖。

OA 两个最终文件与集成分支版本一致；五个文件均无冲突标记，Git unmerged 集合为空。

## 合并门禁修正

- `ClientDeviceHeartbeatLoopTests.SessionChangeWakesLoopAndSendsImmediately` 改用 1 分钟测试周期，并在首次事件唤醒发送后停止循环；只消除测试自身与 25ms 周期发送的竞争，不改变生产心跳实现。
- 定向时序测试连续 50/50，通过后完整客户端 71/71。
- 清理 10 条可确定修正的空值/xUnit warning：入库空批号规范化、OA FormKey 空值投影、迁移数组断言、Cookie header 空项、集合与 Single 断言。

## 数据库候选包

目录：`deploy/production/sql/main-sync-20260808/`

- `00-preflight.sql`：验证 main 基线、Space schema/history、遗留 `ModelAssetId` 和活跃 Publish slot。
- `01-cp6-context.sql`：从 `20260714075419_WfsSubFlow` 到候选 tip，共 14 个幂等迁移。
- `02-space-context.sql`：从空 Space history 到候选 tip，共 36 个幂等迁移。
- `03-postflight.sql`：逐项核对 14 + 36 个 migration history。
- 两个迁移脚本显式固定 SQL Server 过滤索引需要的 SET 选项。

真库演练在随机命名的 LocalDB 临时库完成：先用 EF 逐迁移推进到远端 `main` 基线，再连续执行整包两轮，`ROUND_1=PASS`、`ROUND_2=PASS`，最终 `CORE_CANDIDATE=14`、`SPACE_TOTAL=36`。schema/history 漂移、遗留资产和活跃发布分别以 51083、51000、51020 失败关闭。所有临时数据库均已删除。

该证据不替代生产备份恢复副本演练；正式执行仍需备份、发布冻结、WMS 对账和受保护环境审批。

## 最终本地验证

| 门禁 | 结果 |
| --- | --- |
| OA 冲突聚焦 | 2/2 |
| 前端全量 | 133 files / 711 tests |
| 前端 type-check / production build | 通过；仅既有大 chunk 提示 |
| 完整 solution Release / Android AOT | 0 warning / 0 error |
| CP6.Tests | 2816 passed / 17 environment-gated skipped |
| Space Unit | 487/487 |
| Space Integration 默认门禁 | 288 passed / 95 SQL-gated skipped |
| CP6 Client | 71/71；心跳定向重复 50/50 |
| EF model drift | 无待迁移模型变更 |
| 合并数据库包 | main 基线双执行 2/2；14 + 36 history 精确匹配 |

默认测试中跳过的专项 SQL 集仍需在其正式环境门禁中运行；本轮新增的完整迁移包已经在真实 LocalDB 上完成 main 基线双执行。

## 剩余动作

1. 推送候选分支作为远端备份，并运行 GitHub PR 门禁、代码审查和迁移人工复核。
2. 在生产备份恢复副本重跑数据库包两轮并留存证据。
3. PR 与部署审批通过后才合并 `main`；标签、R2 candidate 和生产部署均为独立后续动作。
