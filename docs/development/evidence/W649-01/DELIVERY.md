# W649-01 · Main 持久化 CRM 租户暂停

Main 本地提交 `9d70a9e6e69e9aa9703e4b94be8de6531219fdfa`，父提交 `1e2b294eb2f4f5a72cf903a16bac724edc74694d`。CRM 前置已采用 `5f09533b9168ee614a6df1f75629b39635585f86`。本轮联验用该采用所含 `8f5064db86bee2f0660fcb163a94d3496d7a39c2` 的 CRM 产品源码及实际原生宿主，CRM 只读源投影的补充夹具只在本机任务制品目录。 [#1012](https://github.com/GTX537/CP6/issues/1012) · [CRM #238](https://github.com/GTX537/CP6.CRM/pull/238) · [CRM 前置实证](https://github.com/GTX537/CP6.CRM/blob/5f09533b9168ee614a6df1f75629b39635585f86/docs/development/evidence/W649-01/DELIVERY.md)

## 提交与恢复

- 版本化和旧暂停入口先在现有 Main 认证根扣住当前平台 actor，保存一行租户待确认意图。持久行固定原键与输入、actor 及 actor 租户、目标管理修订、配置 StoreId 和 C02 租户版本。Main 两阶段对本地事务启用 `synchronous_commit` 的耐久确认。首次连接失败时租户仍是原生效状态。按原键读取返回 `503 TENANT_OUTCOME_UNKNOWN`，已提交的操作继续返回原回执。
- 待确认期间，人类配置及带 nonce 的已采用 context 保留当前授权签名，只停止发当前写 fence。仍准许原只读用途。无 fence 也能独立消费的旧人类及服务直签返回 503。判断来自 Main 已提交的意图及原认证根。其他请求的重启用、配置变更及原生租户状态 DML 无法越过该意图。
- 原 actor 再次请求时，Main 在第二个共用根事务内复验原意图、目标 CAS、StoreId 和 C02 版本，并复用原 CRM `close_v1` 的精确提交。只有确认 CRM 已提交后，才一次提交租户状态、身份快照、Outbox、审计、原回执及清除意图。CRM 结果未知继续原操作。Main 最终结果未知按原回执核对。已先取得 CRM 栅栏的合法业务可先完成一次。
- 旧 HTTP 保留输入及 `code=0` 形状。无客户端键时，原服务端定位由 StoreId 和该 C02 前驱构成，哈希域与公开命令键隔离。待确认只接受原 actor。已确认停止时，同 actor 恢复本代原件，另一当前有权平台管理员可获原无操作成功。换至新 C02 代次后按现有请求合同执行。

前向迁移 `20261115190000_CrmTenantPendingSuspensionV1` 保留既有 `commands` 历史。新表及保护函数收回 PUBLIC 权限。若部署分离迁移和 Main 应用登录，沿现有租户管理/签发身份采用新表所需的 `SELECT`、`INSERT`、`DELETE`。现有 Main 与 CRM 同代已采用协议提供保护配置回放和公共 GET 门。

## 隔离实测

两套真实 PostgreSQL 18.6 与 Main/CRM Kestrel，使用模拟租户、平台 actor、服务及已提交 CRM 配置。W540 配置桥使用真实 Main OIDC 和签名，经本机可信 TLS 终结转发。W542 服务路径继续使用原 TLS 夹具。两个冷窗口的 Main 人类与旧服务入口走实际 HTTP，带 nonce 的服务证明由实际登记 registry、同一 Main PG 及产品签发者直接核对。

| 场景 | 实测 |
| --- | --- |
| 两入口原源 RED | 只禁止专用 CRM 封存登录建立第一次真实连接。两条旧暂停都返回 503，Main 和 CRM 生效写集未变，但 Main 仍签出了当前 fence。 |
| 冷连接及重启 | 版本化和旧入口各走实际首次拒接。CRM 业务连接仍可用。Main 实际停止并以新 PID 在同一端口恢复，原意图的 actor、修订、StoreId、C02 均保持一致。CRM 合法原件回原 201、JSON、ETag，新写 503，配置只读 200。 |
| 并发意图与原权限 | 待确认换操作、更新、重启用及本库原生更新失败。换合法 actor 接手待确认返回 403。旧直签人类、服务 503。人类及服务 context 原签名无写 fence，只读用途正常。旧入口已确认后另一合法管理员仍 200 且写集一致。 |
| 版本化两个提交回包丢失 | CRM COMMIT 实际成功后丢回包，独立读回 CRM 已封存而 Main 生效写集未动。重试同键后用实际 PostgreSQL blocker 确认 Main 正等待第二段原生写，随后丢失其真实 COMMIT 回包。Main 返回 `TENANT_OUTCOME_UNKNOWN`，实际查询返回唯一原回执，重复 POST 给相同原件，已清意图。 |
| 两种提交顺序 | W540 实际 CRM Owner 先持锁提交原业务再暂停。W542 实际 Main 先封存时拦住持旧证的迟到公开 GET 及提交，原合法效果保留。 |
| 未绑定及既有入口 | 24 项原租户管理审计、当前 actor、旧服务授权和 PG 回执/并发定向回归通过。 |

最终 32 项通过，CRM 4、Main 28，零失败、零跳过。增强夹具首轮的服务 registry 未置发布，真实原因与失败保留；最终测试使用与实际服务合同相同的已发布来源及签发者。 [版本化 RED](D:/CP6-test-artifacts/W649-01/main-red-versioned/main.trx) · [旧入口 RED](D:/CP6-test-artifacts/W649-01/main-red-legacy/main.trx) · [重启与回包](D:/CP6-test-artifacts/W649-01/main-final-versioned-ack) · [旧入口](D:/CP6-test-artifacts/W649-01/main-final-legacy) · [业务先](D:/CP6-test-artifacts/W649-01/main-final-owner-first) · [封存先](D:/CP6-test-artifacts/W649-01/main-final-seal-first) · [未绑定回归](D:/CP6-test-artifacts/W649-01/main-final-unbound/main.trx) · [复现入口](D:/CP6-test-artifacts/W649-01/run-main.ps1)

## 本地发布与处置

从干净最终提交发布，WebApi、Core、PostgreSql 迁移程序集的嵌入版本均自报完整提交 `9d70a9e6e69e9aa9703e4b94be8de6531219fdfa`。310 个发布文件清单 SHA-256 为 `DD89B99A7E8065FD3ED5D0E8F5DF160779B18706F9E068C738A8AE4A124D0D33`。独立新库实际 `DatabaseInit` 退出 0，待确认表及原生保护触发器存在。发布程序 `/health/live` 返回 200，匿名租户管理返回 401。定向构建零错误，既有分析器告警见构建日志。 [发布启动及程序集摘要](D:/CP6-test-artifacts/W649-01/main-publish-smoke/result.json) · [发布清单](D:/CP6-test-artifacts/W649-01/main-publish-smoke/publish-sha256.json) · [候选构建](D:/CP6-test-artifacts/W649-01/main-build-current.log)

完整任务差异与 `git diff --check` 已核对。Main 工作流树与已核对的 origin/main 相同，为 `3355235636fd45116f5445fca32fc65e49f13003`。该任务进程及 57240–57259 监听均为零。`codex/w649-01` 保留本地最终提交，DEV-1 干净，远端采用与释放由协调方接续。Actions 与部署均为零。这次证据涵盖首次专用封存连接拒绝和暂停恢复，角色、用户、期限与部门各沿其已登记的现行协议及独立证据。

协调组合采用已确认 Main `1aa2654fcb3f14f58c608aa416c07fa7474c8824`。前序 W648、W650 的 Space 来源与规划改动未触及本任务的 11 个产品、迁移和测试输入。核对可分离源文件与受测提交逐字节一致，继续引用上文已经实际完成的 Main/CRM 联验和本地发布。
