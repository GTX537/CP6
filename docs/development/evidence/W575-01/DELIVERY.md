# W575-01 · 旧 OA 加签与 Main IAM 撤权

2026-11-14。Main 基线 `533bd74b52ab6b218fe0b164a22cc7f4875f7f87`，受测修复 `3b777feb7e472020d40b74c9466639e6ca12769c`。模拟 OA 审批通过实际 Main Kestrel、真实登录和隔离 PostgreSQL 18.6 验证。 [缺陷 #1069](https://github.com/GTX537/CP6/issues/1069)

旧 `POST /api/wf/advanced/addsign` 在取得业务事务前作出动作权限判断。基线撤权先提交时，旧加签仍返回 200 且创建任务。原业务先提交的对照通过。将入口接入现有 `CurrentWriteAuthority`，在 Main 共同事务根内重验 `oa-inbox:addsign`。同一个 Core 事务提交原任务状态、添加签核任务、历史和通知意图。

| 实际提交顺序 | 结果 |
| --- | --- |
| 撤权先提交 | 加签在 `cp6_authentication_v1.acquire()` 等待。随后返回 403，原任务待办状态保留，新增任务、履历、待办通知均为零。 |
| 加签先提交 | 撤权在同一根上等待。合法前加签返回 200，原任务挂起，受让人待办、历史和通知各一条。此后使用旧会话重复加签返回 403，原写集保留。 |

使用独立 PostgreSQL 连接及 `pg_blocking_pids` 确认锁序，同时读回角色修订、身份快照和代次、Outbox、审计与 IAM 回执。最终 2 项 PostgreSQL 并发 HTTP 回归及 28 项已有加签、归属和工作流组件测试通过，零失败、零跳过。

本地 Debug PostgreSQL 发布 310 个文件。WebApi SHA-256 `2aa7719567191cce5cea7ccbdc1f780968b2995c51c662346c64d330b23d6c30` 与受测程序集相同，文件清单 SHA-256 `c0bf78abb57fc1c96aedb2e4e151d138f74738c668ee6573282414f432771256`。发布实际启动，存活 200，匿名加签 401。基线失败、最终 TRX、发布及停止原件位于 `D:/CP6-test-artifacts/W575-01`。 [回归入口](../../../../CP6.Tests/Erp/Pgt017LegacyOaAddSignPostgreSqlTests.cs)

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。旧 OA 退回、撤回、批量转交和办理及触发器继续沿原事务验证，另有财务、独立 Space Design 和余下普通写者。缺少原结果时的差量对账、安全重开及切换编排继续验收。Actions 启动、重跑和取消为零，本地发布未推广环境。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
