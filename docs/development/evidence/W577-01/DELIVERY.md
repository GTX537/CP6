# W577-01 · 旧 OA 撤回与父流程恢复

2026-11-14。Main 基线 `503b93252a503c88967056afab6eeeb245d17645`，受测修复 `761371cd056acc3ce7dd73475d61c83400693257`。使用隔离 PostgreSQL 18.6、实际 Main Kestrel 与真实登录。父子流程、待办、服务作业及账号为模拟数据。 [缺陷 #1073](https://github.com/GTX537/CP6/issues/1073)

旧 `POST /api/wf/flow/{id}/withdraw` 在业务事务前读取权限。基线撤权先提交时旧请求仍返回 200，业务先提交的对照通过。修复在 Main 的 IAM PostgreSQL 事务根内核对当前 `oa-inbox:withdraw` 和发起人，原子保存撤回、待办与令牌清理、传签履历、服务作业取消、历史和恢复凭据。HTTP 继续在撤回提交后执行既有父流程恢复，用已持久化凭据承接恢复。WMS 功能变更取消已在自身 IAM 中调用原服务，本轮沿用其调用方式。

| 实际顺序 | 结果 |
| --- | --- |
| 撤权先提交 | 撤回等待 `cp6_authentication_v1.acquire()`，随后返回 403。子流程、待办、令牌、传签、作业和父流程保持原样，未产生恢复凭据。 |
| 撤回先提交 | 撤权在同一事务根等待。单独锁住父流程时，独立连接已经读到撤回状态、清理、历史和待处理恢复凭据。解除父流程行锁后，父流程沿错误边进入 `err`，恢复凭据成功一次。旧会话重复撤回返回 403。 |

实际读回角色修订、权限代次、身份快照、Outbox、IAM 审计和回执。两个 PostgreSQL HTTP 回归及 17 个撤回、作业清理、子流程两阶段和级联组件用例通过，零失败、零跳过。父流程的模拟场景走错误边进入人工节点。 [回归入口](../../../../CP6.Tests/Erp/Pgt017LegacyOaWithdrawPostgreSqlTests.cs)

本地 Debug PostgreSQL 发布使用同一受测程序集，实际 `/health/live` 200，匿名撤回 401。WebApi SHA-256 `63d1cdc81f9abff9bcc570d0742334f72d2174fd3dc9cd383f8e8df674a8da45`。310 文件清单 SHA-256 `618bbfe28ff183c070ecbd0fbfeb7ac97a43ba91be0b7c2b3f985f18b7d884b5`。基线、通过 TRX、发布哈希、启动和停止证据位于 `D:/CP6-test-artifacts/W577-01`。已有依赖及编译警告仍见构建记录。Actions 启动、重跑和取消为零。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。旧 OA 退回、批量办理及触发器，财务、独立 Space Design、其余普通写者以及缺失写集对账、安全重开和切换编排仍待核销。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)
