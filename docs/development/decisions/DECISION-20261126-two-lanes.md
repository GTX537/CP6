# 2026-11-26 总控决定摘录

## 用户原话

> 2026-11-26 用户在总控会话回复：「接受建议，1.批准 2.b 裁定这四项事实本来就该由别的卡负责产出」。此前用户已选定执行渠道 A（只用总控 CLI 派发的 Codex 会话）。

## 决定 1：Main／CRM 双线并行（覆盖 AGENTS.md「单对话、单卡串行」的执行方式）

> - **Main 线**：DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018`，一次一卡。顺序 WP04-33（resume）→ WP10-47 → WP11-03 / WP11-09 / WP11-08 → WP07-01 → WP11-05 / WP11-64 → WP10-09。
> - **CRM 线**：DEV-2 `D:/CP6-worktrees/dev-2`（CP6.CRM），一次一卡。顺序 WP03-22 → WP02-29（见决定 3）→ WP03-18 → WP03-19。
> - 每线仍是一卡一分支一 PR 一报告；两线卡的共享互斥资源不得重叠；四仓 2+1 席位上限不变；总控逐卡独立复核；push／PR／合并／发包／部署／迁移仍须用户逐次批准。
> - 本决定须在后续治理 PR 中同步到 `AGENTS.md` 与 `roadmap.json` 的 `dispatch_policy`；同步前以本文件为授权依据。

## 决定 2：WP04-33 四项缺口归属（方案 b）

台账位置为 `implementation_tasks` 中 `WP05-02`、`WP05-32`、`WP05-42` 和 `WP04-04` 的 `remaining_scope`。[状态台账](../roadmap.json)

## 决定 3：WP02-29 四平台目标无归属缺口（回复 QUESTION `5fbe1723`）

台账位置为 `targets` 中 `PLT-HTTP-01`、`PLT-MSG-01`、`PLT-OBS-01` 和 `PLT-RELEASE-01` 的 `completion_audit.gaps`，对应归属为 `WP14-08`、`WP14-09`、`WP14-10`。[状态台账](../roadmap.json)
