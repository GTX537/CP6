# W363-01 · RMA 同快照归档与隔离 staging

DEV-1 于 2026-11-09 09:11:24 UTC 开工，基线 93fde21bff2981f7b4b34116802995e6eba38a21。实测使用 PostgreSQL 18.6、Python 3.8.6 与专属 loopback 库，RMA、出库和账户为合成数据。阶段范围为 RMA 源归档与隔离恢复验证。[S02 §9](../../../design-handbook/architecture/02-postgresql-tenancy.md#9-单租户导出恢复和全局恢复)

## 归档入口

给定显式物理目标、安装 GUID 和租户 GUID，工具在一个 PostgreSQL 只读可重复读事务中核 TCP 主节点、库名/OID、集群标识、受保护安装、Ready 和本地迁移回执。所选 namespace、表、函数和权威控制表归属实际库 owner，租户对象 ACL 限定在 owner 和原普通租户角色。受控备份登录可与 owner 分离。[归档工具](../../../../eng/wp02-02/rma-tenant-archive/archive.py)

持有 pg_export_snapshot 事务，原生 pg_dump 只归档精确 t_<GUID N> schema。另一个只读事务导入同一 snapshot，只导出当前租户 RMA heads 所引用的共享出库 ID、单号、仓库、状态和软删除标记。共享 CSV 恢复到 staging 的独立表。原生归档携带三张表、延迟外键、不可变版本函数/触发器和原 ACL。缺 Ready、历史不一致、错 owner、越界 ACL 或缺引用即拒绝。完成后发布 manifest，记录范围、水位、计数、确切文件字节和排除依赖。失败保留 native 诊断，完成 manifest 不会出现。[PostgreSQL 快照同步](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION) · [pg_dump](https://www.postgresql.org/docs/18/app-pgdump.html)

SHA-256 校验包内文件的内容完整性。操作方仍须核对可信来源。原字节证明覆盖归档、CSV 和本地 text 回执的 UTF-8 字节，RMA jsonb 校验 PostgreSQL 原存值与数字精度。隔离 staging 只执行本窗自有的已验证包，原 ACL 所引用的全局角色由私有夹具提供。

## 实库结果

最终证据位于 D:/CP6-local-test/CP6-72H-20261109/W363-01/rma-archive-20261109T092353-eb98873a。test-native-03.log 对应 exit 0。复用 W351 c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40 发布 DatabaseInit，PID 48056 exit 0，四套历史实际为 224/1/1/1。W353 CLI 的 prepare/commission PID 16696/21940 均 exit 0。源 DLL SHA-256、请求和原生进程结果见 inputs.json 及各 process.json。

- 真实只读 snapshot holder 37464 在两个模拟写者之前固定快照。A/B 写者 54908/27444 同一秒实际提交新 RMA head/version 和共享出库状态。之后 pg_dump 38164 与 COPY 61260 分别导入 `00000006-00000005-1`。归档只含 A 的 1 head、2 versions、1 history、1 条被引用的旧出库，排除同键 B、未引用 A 出库与新提交。原生日志和 live-before/after.json 记录两租户合法差量。
- rma.pgcustom 原件 10,045 bytes，SHA-256 53d39d6adcd64b7c0190f41f859618a0a35701c75c8baba68bf13a98cfd563a8。outbounds.csv 原件 157 bytes，SHA-256 ab371fb94739ebfe81250d64645d6ffb11929a7c5fa6503e404bd0609644386e。原生 TOC、manifest 和两文件在 export-a 下。
- pg_restore 单事务与原始 CSV COPY 恢复到私有 w363_staging。逐值核 A 原 TenantId、两版 payload、数量 1234567890.12345678901234、出库引用和原迁移回执。实测外键 23503、固定 TenantId 和不可变版本 23514。原普通 A 登录可读自己的业务表，B 跨租、A 写及 A 读共享 staging 表均 42501。源 A/B 在 staging 前后指纹一致。
- 用独立只读 w363_backup 登录执行真实 archive.py export 与 verify，两者 exit 0，新快照为 2/4/1/2。实际错目标 OID、缺 Ready、为 B 授权、把原同名函数 owner 改为 B 均拒绝出包且没有完成 manifest，随后恢复夹具原权限。缺共享文件、篡改共享文件、错误目标租户均拒绝校验。相关 stderr 和拒绝回执保留。
- test-red.log 记录实现前工具模块不存在。首次实库和后续 ACL 专项实库记录在 rma-archive-20261109T091823-fe322f90 与 rma-archive-20261109T092151-ae108c62。三个专属 PostgreSQL postmaster 11648、50192、39492 均有停止记录，已复核 55566 无监听。

## 复现与边界

在任务工作树运行下列命令，使用尚未占用的本机端口和专属验证目录。[实际夹具](../../../../eng/wp02-02/rma-tenant-archive/verify.py)

    & 'C:/Program Files/python/python.exe' -B 'eng/wp02-02/rma-tenant-archive/verify.py' --root 'D:/CP6-local-test/CP6-72H-20261109/W363-01' --pg-bin 'C:/Program Files/PostgreSQL/18/bin' --port 55566 --dotnet 'C:/Users/tt/.dotnet/dotnet.exe' --cli 'D:/CP6-local-test/CP6-72H-20261109/W353-01/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll' --webapi 'D:/CP6-local-test/CP6-72H-20261109/W351-01/6a5c78c9642844379b98beea99dbbb71/current/CP6.WebApi.dll' --webapi-source 'c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40'

人工导出使用 archive.py export 的 --pg-bin、--target-file、--installation、--tenant、--username、--output。target-file 只含 Server、Port、Database、DatabaseOid、SystemIdentifier。备份凭据经操作人私有的 PGPASSFILE 或 PGPASSWORD 提供。archive.py verify 使用 --bundle 和 --tenant。输出目录须尚未存在，bundle 与 native 诊断分置。

完整 IAM/撤权、业务原 operation/journal、Inbox/Outbox、路由水位、审计、外部资产与生产切换仍属后续。PGT-021/022 保持 NOT_RUN，manifest 的 recoveryReady 为 false。分支 codex/w363-01，专属进程已停止，COORD 负责正常合并和 DEV-1 官方 finish。[正式验收卡](../../acceptance/postgresql-tenancy.json)
