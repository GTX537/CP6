# W419-01 · ERP C03 Owner 原件同源切面

DEV-1 于 2026-11-09 18:21:42 UTC 开工，基线 #854 `03f713c0fb94b1f607420d8e213937ac0bcbe595`。新增定向验收工具。源业务输入沿用真实 PostgreSQL 缺合作伙伴合同，W412 的原生占位行仍保留其暂存验证范围。[原缺合作伙伴用例](../../../../eng/crm/erp-integration-tests/ErpHandlerRelationalTests.cs#L19) · [验收驱动](../../../../eng/wp02-02/rma-tenant-archive/WriteErpOwnerOriginals.cs) · [验证入口](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_owner_cut.py)

## 封存输入

本窗使用 W405 的停机源 `127.0.0.1:55567/w374_live`、OID `16386`、system identifier `7706139311168619192`、原安装 `7679733c-6e8e-4896-b8ae-b7f27a400c65`。A/B 是原候选中的两个独立合成租户。一个副本 `D:/CP6-local-test/CP6-72H-20261109/W419-01/source-clone/data` 共 160,737,692 字节。复制前后原件和副本为 `pg_ctl status=3`，五个 PG 文件摘要相等。复制回执 `source-clone/copy.json` SHA-256 `4fafb7e5c52156c36a4022b8ef4f2976be6e33f066fdc7f71fce0cd54b2583c6`。原 W405 v10 pin `7ac25b3c3eae626277c03802e29203e7cf1f7912664e05f92aeaba817453b6e9` 及所有成员复核，沿用其 W402 空私有资产根。[原 W405 切面](../W405-01/DELIVERY.md)

沿用 #811 实库测试保存的 Core/Platform 封存文件。`CP6.Core.dll` SHA-256 `4e65b1352175ddda351bf5ed00a115a3e94a04911c1478cbc94abf8cd30ce092`。#811 `310b99df8c41ca33e846a706c1025bab63f85404` 与 #854 的 C03 ERP 服务、ERP PostgreSQL 迁移、上下文连接/事务绑定和 ERP 契约目录无差异。只编译私有驱动。CLR 在连接前核验实际 Core、Platform EF/Messaging、EF、Npgsql 和 PostgreSQL EF provider 的位置与摘要。验收脚本通过封存契约构建原报文，调用实际 `ErpRequestHandler.ConsumeAsync`。[#811 原制品来源](../BUG-808-STOCK-RESTORE-OUTBOX/DELIVERY.md)

## 操作范围

A、B 共享原 RequestId 和 AccountId。每个租户各有真实全局消息 ID，RequestVersion `3` 和 CRM AggregateVersion `7` 独立。只调用实际 Owner 与原校验器。处理器找不到该合作伙伴时产生 C03 的终态失败和原 Pending Outbox。同一原报文重复调用后比较八张 ERP 表及共享行版本序列。独立 PostgreSQL COPY 用于核对原 ERP 行与正常 `archive.py export`/`verify` 的 v10 成员。

私有 `run-w419.py` 保留准确编译、验证输入、实际资源及停止证据。原候选数据库只读默认，本窗唯一受控写连接使用已核对的任务副本。没有启动 Web 宿主、Dapr 或派送器。主线的归档及产品接口沿用原实现。

旧入口缺失的实际退出 2 保存在 `test-red.stderr`。原 W405 v10 用当前正常 `archive.py verify` 实际退出 0。首轮私有 csc `build-01` 实际退出 1。对 Npgsql EF provider 类型的直接编译引用出现 `CS1702`，其 EF Relational 引用为 8.0.11，封存运行时依赖实际为 8.0.30。后续改用原运行时依赖绑定并保留连接前的实际文件和摘要检查，编译仍按警告即错误执行。首轮 PID 58584 已退出，原生数据库未启动。

## 实际结果

`build-02` 编译退出 0。`native-01` 在首个 A 原报文调用时实际返回 `RetryScheduled`，运行退出 1。原生日志显示该后端仅执行了精确物理、租户与合作伙伴缺失检查，没有执行 ERP SQL。对照 `DatabaseConnectionFactory.ValidateConnection` 定位为夹具直接打开的连接缺 `Search Path=public`。最终驱动改由原 `DatabaseConnectionFactory` 建立连接，先固定路径，再打开和核物理目标。`build-03` 实际 csc 退出 0，PID 56928 已停止，驱动 SHA-256 `55f86cc5a2c96a172ceb3b3406c6c4872235b3233a8aa4535766b7b021d34eef`。[原连接合同](../../../../CP6.Core/Persistence/DatabaseConnectionFactory.cs)

- `native-02` 复用同一已停副本。CLR 前，独立 COPY 核 A/B 各八个 ERP 成员仍只有原表头，与前轮逐字节一致。合作伙伴、原共享流水、RMA 和 Stock 两租户控制快照也一致。未重建或清空数据库。
- A/B 使用同一原请求 `ef806d20-098e-4802-a4e0-f1789c52e1f5` 和账号 `8e887014-1c78-4010-be46-2516560f78d5`。真实 C03 均返回 `Applied`，原报文重复均为 `Duplicate`。各自 `Aggregate` 水位为 `3/7/1`，Inbox 为 `Processed`、尝试 1 次，`Request` 为 `Terminal=true`、`Succeeded=false`。原结果类型 `com.gtx537.erp.business-partner-failed.v1`，错误码 `C03_BUSINESS_PARTNER_NOT_FOUND`、`retryable=false`，实际结果通过封存 validator。A/B 请求和结果的全局消息键各异，原 Outbox 为 `Pending`、尝试 0、无租约或已发送时间。原命令、原结果字节摘要及原 RowVersion 留在 `checks/actual.json`。两次重复前后的原生八表及序列相同，摘要 `9021d3d22ceb5f11555f9115f72b9cc0207e16a3652c84ac683e05d2bff0733a`。
- 正常 `archive.py export` 与 `verify` 均退出 0。原生日志记录 36 次导入同一快照 `00000005-00000006-1`。A 的八张 ERP 表分别比对独立原生 COPY，全部字节一致。`Aggregate`、`CommandInbox`、`Cp6_OutboxMessage` 和 `Request` 各一行，其余四表保留空成员。B 同键保留在副本，切面后再次回读八表字节一致。合作伙伴、RMA、Stock 和原共享 Owner 快照前后相同，4,503 字节 SHA-256 `277a57178b17a892a7507c4150b0d887a38c7ae1ab13b81315c36b5343263b4d`。
- 新制品为 `D:/CP6-local-test/CP6-72H-20261109/W419-01/native-02/checks/v10-cut/bundle`。manifest 36,610 字节，SHA-256 `5a2711b967d03cd9258509ea04d24f0a5a004ff9243f154df23d2e0a7359a446`。原 `erp-request.csv` SHA-256 `0888543bd76762925abe73afec3b698f33f44951b65473dcd6bdd802dcb09997`，`erp-aggregate.csv` SHA-256 `148b849a30d7c77c900391154643d3825a1586182b72011d2b469b28037498be`。同级 `actual.json` SHA-256 `317ef2ab40bc9738fe8f8b6d8f03664b6716494d0e6b8bf97bec0b8ca879fd7a`。沿用已固定的 W402 空资产根，制品保持 `recoveryReady=false`。

## 停止与交接

最后窗口 PostgreSQL 前物理样本 1,468/1,490 MiB，CLR 前 1,422/1,411 MiB，提交可用均高于 19 GiB。实际 Windows PostgreSQL 18.6 使用 32 MiB shared buffers。唯一 Owner CLR PID 57356 退出 0，工作集峰值 176,680,960 字节，CLR 采样物理最低 1,200 MiB。最后原生窗口 23.148 秒，18:47:10.476 UTC 退出 0，距 START 1,527.543 秒。postmaster 27100 已停止，源与副本 `pg_ctl status` 均为 3，55567 无监听且无 pidfile，临时 HBA 已移除。原 W405 五个 PG 文件和原 v10 全成员保持，专属副本停机保留供后续对账。

本窗覆盖真实 Owner 的合成终态原件和同源 v10。目标原位导入、行版本序列推进、派送、完整恢复及 PGT-021/022 另验。候选分支 `codex/w419-01` 本地保存，正式合并和 DEV-1 释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
