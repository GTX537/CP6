# W506-01 询盘 PostgreSQL 租户外键与事务行

实际 PostgreSQL 18.6 安装目录核对 122 条物理外键。逐项核对有序源列和目标列、验证和强制状态、类型 OID、typmod、collation、实际可用的唯一支持索引。首次按清单字面核对 124 条，发现两条较短 Activity 关系由同一租户和 Activity 前缀、额外非空 `CorrectionStorage` 的实际三列外键覆盖，更新和删除均为 NO ACTION。定向重验显式确认两项覆盖关系及全部物理外键。另核对 manifest 的 341 个必填列与 13 个原生关键必填列。

两个真实应用租户分别创建模拟询盘。普通运行角色的跨租户 ReceiptProof 和同租户错误回执标识均命中同一三列外键 `23503`。新签发行的 `IdentityJson=NULL` 命中 `23502`。回滚后，两租户的 Submission、PublicAttempt、Consent、ReceiptProof 和 IssuedAttempt 行与原记录一致。

FX26-S22 用当前 PostgreSQL 时钟和持续启用的约束建立合成历史预览图。持久 TTL=0 的源字节 SHA、规范内容 SHA 和预览关联已回读。配置 Owner 和准备绑定均拒绝该历史政策。签名激活实际为 `503 crm.configuration_unavailable`，保存原结果、草稿、绑定、配置头、预览、激活、审计和 Outbox 的十组行零变化。相同主体仍能激活另一有效预览。此处核销 R-057 的业务拒绝与零写入，原 422 响应留给 AC-INQ-034。 [CRM #196](https://github.com/GTX537/CP6.CRM/issues/196)

FX25-S06–08 实际下载只推进目标制品的 `ConsumedTicks` 并增加同主体、Job、版本和时点的一条审计。错配 Job、错证明和重复请求分别实测 404、403、410，并核对制品、Job、提交、历史、审计、效果及 Outbox 零增量。旧样例的 404 统一状态与现行下载协议有区别，此项采用现行持久写入义务。

原 R-057 其余步骤按实际来源复用。FX03 采用完整提交、原件重放与晚段回滚。FX16/17 采用审核胜者、H01、审计、原件和原生 Outbox 同事务。FX25 采用四处置实际回执、撤回、导出、保留、hold 与恢复。共享限流桶按已采用的独立期限维护事务执行，旧样例所列同一业务擦除事务中的 RateLimitBucket 删除未重跑。FX26 采用真实保存、预览、激活、旧 Attempt 和证明/CAS 零写边界。 [提交图](../../dispatch/20261024-wave112/README.md) · [完整提交](../W117-01/DELIVERY.md) · [审核](../W121-01/DELIVERY.md) · [审核赢家](../W502-01/DELIVERY.md) · [隐私](../W482-01/DELIVERY.md) · [保留](../W491-01/DELIVERY.md) · [限流](../W495-01/DELIVERY.md) · [活体处置](../W496-01/DELIVERY.md) · [配置](../W485-01/DELIVERY.md) · [换版](../W247-01/DELIVERY.md)

实际本地 .NET 8、Kestrel、隔离 PostgreSQL 18.6 和普通运行角色，模拟业务及 IAM。`inquiry-storage-01.trx` 保留 2 通过、1 目录失败和 0 跳过，`inquiry-storage-02.trx` 定向目录及负例 1 通过、0 跳过。共 3 个不同用例有成功结果。执行命令、TRX 和服务器日志位于 `D:/CP6-test-artifacts/W506-01`，自有 PostgreSQL 已停止。

CRM #197 采用源 `77491be21c24ac6da6bb524f9239e37870053923`，远端 `35c8860e5c47446bd79f5014afe4a28a78760374` 与验证源码树一致。本轮 CRM 提交为测试。集中审查覆盖约束语义、普通角色、历史装载和业务写入。准确任务分支已在本地及远端清理，W506 的 DEV-2 已释放。采纳 AC-INQ-047/057，原询盘 59 PASS / 1 NOT_RUN。Actions 运行与取消均为 0，部署 0。 [CRM #197](https://github.com/GTX537/CP6.CRM/pull/197)
