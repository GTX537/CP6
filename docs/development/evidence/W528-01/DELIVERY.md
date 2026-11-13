# W528-01 · PGT-024 混合版本与应用回退

正式 PostgreSQL **24 PASS / 3 NOT_RUN**，PGT-024 为 PASS。原询盘 **60/60**。基线 `0d54024d146b1dbc445f9af2eaea2ddf6d7de5d6`，源码提交 `01c7640e540cef40a7661e1c3ddb74f5af9fd6a0`，复用固定 DEV-1 和 `codex/w528-01`。原任务 WP02-02 继续开放。

## 采用范围

共享 Core EF 迁移 `20261113090000_RmaTenantModelV2Shared` 先扩展。在已佣用且启用原 journal、draft 和 submit 的同一数据库，由 Owner 单独把 A 迁移到 `rma-tenant-storage-0002`。视图、写入限制、租户本地历史及共享原回执在同一事务提交。B 保持 `rma-tenant-storage-0001`。同键同输入读回原字节，改变操作键拒绝。

新宿主按可信配置为 A 建立实际映射到两张 PostgreSQL 安全调用者视图的 EF 模型。B 和兼容旧宿主使用原租户表。视图复用相同业务原件，保留 Owner、TenantId、业务版本、原回执和精度。旧版对 A 的 draft、submit、close 均由 PostgreSQL 拒绝。v2 写者沿用原业务与 journal 函数，在同一事务记录和移除仅内部角色可写的登记，普通登录无法写入该登记。 [操作说明](../../../../tools/CP6.TenantStorage/README.md) · [兼容矩阵](../../../../CP6.WebApi/rma-tenant-model-compatibility.json) · [PostgreSQL 视图权限](https://www.postgresql.org/docs/18/sql-createview.html)

## 实际验收

环境为本机隔离 PostgreSQL 18.6、.NET SDK 8.0.424、两份实际 Kestrel 宿主和普通 A/B 登录。使用 W526 已停机源副本、模拟业务数据和正式 Platform 0.10.4。旧宿主为 W526 的固定制品，新宿主和迁移 CLI 由本任务编译。原源和关闭候选保持原状态。

| 验收行为 | 实际结果 |
| --- | --- |
| 共享最低版本、租户版本及未知模型 | 缺共享 EF 迁移时 Owner 拒绝，A HTTP 503 且零普通连接查询，B 原业务 200。共享就绪但 A 尚未迁移、A 配置未知模型时仍拒绝。关闭候选拒绝迁移 |
| 前向扩展和原回执 | A 的新 DDL 和历史实际提交，原件完整。相同输入回执逐字节相等，改变键拒绝。A 迁移历史 1→2，B 保持 1。Core 224→226 包含上一轮 Stock 迁移和本轮共享扩展，ERP/Identity/Space 历史各自保持 |
| 同库两个实际模型 | 并发执行新版 A 草稿 201 和旧版 B 读取 200。原生 SQL 记录 A v2 两视图和新 writer、B v1 两表。A 同键读回一致，随后提交及关闭原操作成功。新版 B 和旧版 B 的 v1 写入分别成功 |
| 旧写者与跨租限制 | 旧版对 A 的 draft、submit、close 均 HTTP 503、数据库 42501，前后业务及 journal 一致。普通 A 向内部表写入、普通 B 查询 A 的视图均 42501。内部写入登记最终为零 |
| 应用回退 | 停新版后，旧版 A 读到已提交业务的相同内容和 ETag，B 保留旧读写。无数据库 Down，完整历史前后相同。A 写入需恢复 v2 宿主 |
| 归档与制品 | v1 归档与同库恢复入口明确拒绝 v2 的 A，B 的 v1 原生导出并校验通过。WebApi 构建及本地 publish 的兼容矩阵摘要均为 `7500eee1f907bb92256991d59db5908754337274a5584569e7821fb8c42bea31` |
| 定向自动化 | 21 通过、0 失败、0 跳过。包含实际 Core EF 共享迁移、两套 EF 映射和租户存储契约回归 |

原始证据位于 `D:/CP6-test-artifacts/W528-01/`。最终双宿主和回退证据为 `native-04/actual.json`、四个原生 SQL 窗口、各进程身份及 `terminal.json`。A 迁移操作 `a778cff4-767b-4f71-bcbd-0b4090bd4c82`，脚本摘要 `af81b4f90ad9a3d921e0c72c2d79b52b632228812cc137ce6ce48fe1ca66b86a`。构建和 21 项回归见 `webapi-04.log`、`cli-04.log`、`results/rma-model-final.trx`。本地 publish 原件摘要见 `local-publish.json`。旧制品采用范围参考 [W526](../W526-01/DELIVERY.md)，同模型双宿主前序证据见 [W518](../W518-01/DELIVERY.md)。

## 审查与交付

集中审查核对共享版本门、A/B 物理映射、原写入事务、旧写者和归档边界。早期实测发现对既有 journal/submit schema 的额外授权改变了原目录摘要，改为定义时绑定已有函数，保留相同权限和原回执复验。初次资源检查的候选进程归属误判与 EF 映射红色测试均保留。最终通过的隔离数据库和宿主进程已经停止。

v2 归档和恢复采用需另有兼容路径。生产已有数据切换、正式制品发布和生产部署仍为独立范围。本轮 GitHub Actions 触发和取消均为零。剩余正式项 PGT-011、PGT-017、PGT-025。分支、工作区和席位处置见本任务 PR 及 `D:/CP6-test-artifacts/W528-01/main-finish.json`。 [正式矩阵](../../acceptance/postgresql-tenancy.json)
