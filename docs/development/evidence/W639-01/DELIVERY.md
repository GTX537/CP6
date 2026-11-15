# W639-01 外注成品成本的共同事务栅栏

Main 开发基线 `659268457855db0f8eb547acefddd45d10296ab7`，受测代码 `4b633310fc5e2dcf24fbddde8c4cd77740feb3b1`。本批从 [W629](D:/CP6-test-artifacts/W629-01/DELIVERY.md) 的真实正常入账和原凭证回读继续。 [PGT-017 #1012](https://github.com/GTX537/CP6/issues/1012)

现有 `POST /api/pur/subcontract/{poNo}/{lineNo}/finished-cost` 使用原 `pur-subcontract:cost` 和 `CurrentWriteAuthority`。成本计算、凭证查询或新凭证写入、总账采番和最终签名会话检查共用 Main 原生事务。期内业务错误沿用原 HTTP 合同。W629 的原始小数计算、来源键 `SC-{PO}-1` 与同键响应继续沿用。 [HTTP 入口](../../../../CP6.WebApi/Controllers/Pur/SubcontractController.cs#L52) · [原出账适配器](../../../../CP6.Core/Services/Pur/Contracts/FinCostServiceAdapter.cs#L28)

## PostgreSQL 实测

使用 PostgreSQL 18.6、签名 Main Kestrel、独立 PostgreSQL 观察连接及模拟外注 PO、支给材、会计期和总账模板。两项真实并发和 25 项既有外注组件通过，失败和跳过均为 0。 [原 TRX](D:/CP6-test-artifacts/W639-01/candidate/results.trx) · [命令与受测内容对象](D:/CP6-test-artifacts/W639-01/candidate/invocation.json) · [并发夹具](../../../../CP6.Tests/Erp/Pgt017SubcontractCostPostgreSqlTests.cs)

- 业务先取得 Main 根，真实凭证 INSERT 在独立事务的表锁等待。签名暂停在其持有的 `cp6_authentication_v1.acquire()` 上等待。业务返回 200，原记账及单次采番已经提交。用原暂停前 token 再提交返回 401，写集不变。
- 签名暂停先在真实租户 UPDATE 等待，业务在同一原生栅栏等待。暂停先提交时业务返回 401，凭证、采番、PO、库存支给材记录维持原快照。重新启用后使用正式新登录凭据，原业务保存一张凭证。
- 两种顺序分别回读 Main 暂停回执、权限代次、快照、审计与 Outbox。加工费 30.85、支材 5.6875、成品成本原值 36.5375，原凭证借成品、贷库存各 36.54。重新授权后的相同请求返回原凭证和原响应，独立读回逐行一致。

## 本地发布及交接

本地发布清单覆盖 310 个文件，SHA-256 为 `33752cc5bacf9e41746975fc143d862f9ec285f745d9e2c12c9b91229709cbc5`。Main、Core 和 PostgreSQL 迁移 DLL 与实际测试逐项一致。独立 PostgreSQL 57131 和 Kestrel 57138 实际启动，存活 200，匿名成品成本提交 401，任务进程已停止。仅本地验收。 [发布冒烟](D:/CP6-test-artifacts/W639-01/publish-smoke.json) · [文件哈希](D:/CP6-test-artifacts/W639-01/publish-sha256.json)

本批验收外注成品成本入账，支给材登记、分批发料和其他背景写者有各自的 Owner 与写集。本轮正式 PostgreSQL 状态仍为 26 PASS / 1 NOT_RUN。
