# W547-01 · Main 服务登记撤销与 CRM 导出

2026-11-13 UTC。Main 基线 `ad1df9b3954af7d21b33ca48288e4a50932af5f2`，实测夹具 `73fd3934821f87fc4db07ebcef7ee123acd0c640`。CRM 采用 `a13588e8120f3af40cbc8023ad7924953e96b2e4`，使用 W546 的实测制品。 [W546](../W546-01/DELIVERY.md) · [正式 PGT-017](../../acceptance/postgresql-tenancy.json#L646)

两套隔离 PostgreSQL 18.6。使用模拟租户、服务登记和询盘。Main 的实际 TLS 服务上下文和原 `InquiryServiceIdentityPublisherV1` 处理一次服务登记移除，CRM 使用实际导出 Owner。登记变更通过新的不可变配置实例执行，模拟原受控重启后的发布阶段。

- `verified-service-pause-first`。Main 服务登记移除先提交。CRM 收到撤销前已签发的 V2 服务上下文后拒绝新效果，导出任务、制品、审计及 Outbox 均未变。CRM 测试期间尚未接收新的 C02 事实。
- `verified-service-owner-first`。通过 `pg_blocking_pids` 观察 Main 发布器等待 CRM 原 C02 锁。CRM 原导出、制品及审计先提交。随后 Main 提交服务主体墓碑，已生成制品保留。
- 两种顺序均核对租户仍启用、角色导出权限仍在、服务快照版本及代次各推进一次、同版本优先 Outbox 和幂等重复发布。旧 Main 签发实例读取已撤销的源后返回 503。两库用例共 4 项通过，Main 定向 12 项通过。保留夹具误读公共菜单列和旧测试程序集的失败记录。

本批只改 Main 测试与交付记录。复用 W546 产品发布哈希和实际 HTTP 冒烟。原始 TRX 及进程停止结果在 `D:/CP6-test-artifacts/W547-01`。Actions 启动、重跑和取消为零。CRM 复用上轮已合入主线和已释放的席位。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。该服务登记移除已通过两库验证。部门与其他来源权限变更、租户自然到期与截止变更、直接 SQL、Main 其余独立写者、跨库确认丢失和恢复待验收。 [跨库余项 #1012](https://github.com/GTX537/CP6/issues/1012)

Main 分支和席位处置见远端 PR 和本地 `main-finish.json`。
