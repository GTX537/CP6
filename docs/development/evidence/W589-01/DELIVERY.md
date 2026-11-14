# W589-01 银行单边凭证逐笔权限与凭证来源

[缺陷 #1093](https://github.com/GTX537/CP6/issues/1093) · [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)。Main 基线 `f1362739b793410b38dd390bcd49ea3730abe379`，实际受测 `cebf05b4a0ad0a57b1abc20a013054c172f93bd3`。隔离 PostgreSQL 18.6、真实 Main Kestrel 和模拟租户、银行与账务数据。

- 每笔复用 Main IAM 共同根，重查当前会话、`generate-voucher`、对账单、流水及对方科目。采番、凭证与借贷分录、银行流水、匹配组、分录链接和逐笔业务审计同一事务。金额 `46.81` 及原借贷方向一致，保留原凭证反冲合同与历史记录。数据库提交不确定走原 IAM 错误和业务回读。
- 双方提交顺序通过真实等待链验证。暂停先提交返回 401，动作撤权先提交返回 403，无新写入。先合法过账的原凭证、流水、采番、匹配和审计保留。实际一批两笔时，第一笔提交后 Main 暂停，第二笔返回 `REAUTH_REQUIRED` 且无新增，整批响应仍保留第一笔成功。同步核对暂停或撤权的代次、身份快照、Outbox、审计及回执。
- 原凭证使用对账单号作来源，第二条流水触及 `UX_Fin_JournalEntry_AutoVoucherSource`。PostgreSQL 新凭证按稳定流水 ID 形成来源键，原对账单号仍记于摘要和原匹配关系，既有凭证继续可读。正常批中两条同单流水分别过账，中间无效项继续逐笔失败。对方科目拒绝后不消耗编号。重试原流水返回 `E-A4-BANKONLY-DUP`，原写集相同。
- 基线四种实际竞争均复现无共同栅栏。第五项基线另有 HTTP 测试读取响应结构的问题。中间 `final/results.trx` 39 通过、1 失败，发现原凭证来源键冲突。最终 `verified/results.trx` **40/40** 通过，11 项实际 PostgreSQL、29 项相关组件，零跳过。局部发布 310 个 SHA-256，主体与受测程序集相同，实际宿主存活 200，匿名凭证入口 401。原始测试、发布摘要与停机记录在 `D:/CP6-test-artifacts/W589-01`。Actions 无启动、重跑或取消，未部署。

正式 PostgreSQL **26 PASS / 1 NOT_RUN**，原询盘 **60/60**。银行自动撮合及其他财务写者、独立 Space Design、全写者登记仍要核对，CRM 正式恢复与切换编排待验。 [正式矩阵](../../acceptance/postgresql-tenancy.json#L646)
