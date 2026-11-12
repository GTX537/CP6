# W509-01 正式 PGT-014 CRM 身份接收

真实 Kestrel CRM 身份接收经受控传输登记调用当前原生 Owner，使用 PostgreSQL 18.6 普通登录和正式 Platform EntityFramework 0.10.3。两类竞争均观测胜方事务持有七级原锁，确认业务行和回执在胜方提交前不可见。 [正式验收](../../acceptance/postgresql-tenancy.json) · [共同事务边界](../../../design-handbook/architecture/02-postgresql-tenancy.md)

- 同租竞争在实际 Registration advisory 锁等待。胜方提交后，竞争者在原身份 fence 上收到真实 `40001`，签名 HTTP 为 `503 RETRY`。同一原件重新投递取得新 Serializable xid 和快照，判为 TransportDuplicate，恢复胜方的原结果。
- 跨租竞争在实际全局 `InboxV1Transport` 唯一键 INSERT 等待胜方事务。PostgreSQL SSI 返回真实 `40001`，该次 HTTP 为 `503 RETRY`。原报文重投取得新事务和快照，判为 TransportIdentityConflict，技术收据提交后返回 DROP。胜方原结果保留，另一租户的来源、当前投影、业务 operation、观察、冲突及 Inbox 写集均为零。
- 现行回归实际覆盖早退前内容/历史、重复/旧版、同版不同内容、当前登记重验、Published/DRIFT 同事务撤回与回滚、最终 Inbox 写入失败的保存点，以及提交结果未确认时扣留 SUCCESS。Main ERP 使用独立 command journal。 [原队列边界](../W431-01/DELIVERY.md)

`formal-01.trx` 实际 11 通过、0 失败、0 跳过。两个原生 `40001` 的原始服务器日志与事务、快照和回执记录保存在 `D:/CP6-test-artifacts/W509-01`。命令入口为 `run-native-ingress.ps1`。两次 owned PG 核验停止，55585 释放。模拟业务身份和传输登记经实际运行入口处理。

CRM #199 采用源 `019263b6d0e0effb4b7734c5a545b7143813b97f`，已核对远端 `8347e875d0cec71cd8dd119f0e323cd4706a849f` 与测试树。提交仅增加当前运行入口的测试。集中审查覆盖实际竞争、各结果归属、同事务、登记和 ACK。CRM 准确任务分支已清理，W509 DEV-2 已释放。采用 PGT-014，正式 PostgreSQL 17 PASS / 10 NOT_RUN。Actions 和部署均为 0。 [CRM #199](https://github.com/GTX537/CP6.CRM/pull/199)
