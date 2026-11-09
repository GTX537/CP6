# W364-01 · Core RMA 托管草稿

2026-11-09 09:39:46 UTC 在 DEV-2 开工，分支 `codex/w364-01`，登记基线 `9fe80b515fee21a84150f6224e5ec3cfb627b7de`。归档兼容范围在 W366 经 #819 采用后从 `8c4796e89b3a0e3b28381cbb30d0f080962aafe4` 继续。两基线之间的源码变更仅涉及归档 Python 与文档。 [W361 原 journal](../W361-01/DELIVERY.md) · [W366 归档](../W366-01/DELIVERY.md)

## 交付

`install-rma-core-draft-writer-v1 --stdin` 复用精确物理目标、W353 Ready/history、完整普通登录目录和 W361 受保护原件。同一事务安装 writer 和保存原回执。专用 `RmaReadTenancy:DraftEnabled` 开启实际 `POST /api/wms/rma/native-applications-v1` 的 `wms-rma:draft-write` 上下文。未启用的读 profile 保持现有入口门禁。 [CLI](../../../../tools/CP6.TenantStorage/README.md#hosted-core-rma-draft-creation) · [Owner](../../../../CP6.Core/Services/Wms/RmaApplicationOwnerV1.cs)

实际普通事务先拿原认证 root。另一连接持原共享出库和权限读栅栏，核当前 bearer、权限、字段、来源和最终授权，直到普通事务结束。新建选原 `add`，原件重放选原 `query`，沿用原 actor、输入冲突、出库可见性、精度和 ETag。写提交或回滚确认不明时沿用 `OPERATIONS_OUTCOME_UNKNOWN` 与同键恢复。 [权限入口](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.RmaDraft.cs) · [事务](../../../../CP6.Core/Persistence/Tenancy/TenantRmaReadStore.Draft.cs)

同一 NOLOGIN 执行角色按原登录目录限定租户，只有 tenant schema USAGE 和固定 heads/versions 列 INSERT。单一受控函数保存 v1 head、version 和原 journal；定义时解析的 SQL `RETURN` 函数绑定真实 W361 `store`，原 journal schema ACL 与目录哈希保持。归档 v1/v2 仅为确切物理安装、受保护 marker、同一 NOLOGIN 及 owner 授予的 11 个不可转授权限接受这组 ACL。 [原生 writer](../../../../CP6.Core/Persistence/Tenancy/RmaTenantStorageV1.DraftWriter.cs) · [归档](../../../../eng/wp02-02/rma-tenant-archive/archive.py) · [PostgreSQL SQL 函数体](https://www.postgresql.org/docs/18/sql-createfunction.html)

## 实际验证

Windows PostgreSQL 18.6，专属环回 55567，库 OID 16386，system identifier 7706109155464991768。发布 W351 DatabaseInit `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40` 实际 exit 0。保存的 W353、W361 CLI 在同一新库中真实采用。登录、出库、客户、草稿和故障注入均为隔离模拟。 [复现入口](../../../../eng/wp02-02/rma-tenant-migration/verify_draft.py)

| 场景 | 实测结果 |
| --- | --- |
| 安装和原件 | 缺原 journal、错物理 OID、异键拒绝。同键回原安装字节。实际普通登录直插 head、跨租写、直调桥接和切换执行角色均拒绝 |
| 正常托管 POST | A/B bearer 并发，同 application 和 operation UUID 各得 201。两份实际 head、version、原键及独立输入/结果字节精确。GET 保留 8 位数量 `1.12345678`、原 ETag 和 `NOT_GRANTED` |
| 故障和撤权 | stale source 返回 412 且零写入。journal 私有测试触发器实见本事务中两张聚合行后报错，三处均回滚。普通 PID 44140 实际等待原认证锁持有者 19664。持有者提交撤销 add 后待处理 POST 403 且三处零写入 |
| 当前授权重放 | 丢弃首个成功响应正文，撤 add 后持 query 同键恢复原结果和 ETag，变更输入 409。A/B 的原件与两个 Ready/history 保持。旧 W361 CLI 实测重新返回原 journal 安装及原 W353 commissioning 字节。submit 和 close 继续走既有不可用门禁 |
| 同库归档 | 未改的 W366 在同一实际源库以 `rma-schema-acl-rejected` 拒绝 writer ACL。窄化兼容后，v1/v2 实际 pg_dump、COPY 与包校验通过。两版各有 A 的一份 head/version/history 和出库引用，v2 另有一份 exact A COMMITTED 原件。额外 executor UPDATE 或错安装 marker 都拒绝且没有 manifest |

完整原件为 `D:/CP6-local-test/CP6-72H-20261109/W364-01/rma-draft-20261109T095913-c36831eb/draft`。`rma-draft.trx` 记录一条定向真实测试通过、零跳过，`native-draft-statements.log` 记录普通 SQL 和两行已写后触发的故障，`archive-after-draft.json` 于 10:02:09 UTC 记录同库归档。`source.json` 保存实际宿主、测试、CLI、CLI Core、宿主 Core 与复用二进制的 SHA-256。

锁定还原复用现有包。单节点 .NET 8.0.424 Release 宿主与测试构建 0 错误，有既有告警和一条 xUnit 断言写法提示。旧 W361 CLI 对新入口的 RED 是 exit 2。首次候选 PostgreSQL 日志记录 Npgsql 对 `BEGIN ATOMIC` 内部分号分句后返回 42601。原生小夹具确认 `RETURN` 同样绑定原函数 OID，且 journal 失败时聚合回滚。仅重编 CLI 及依赖，复用原宿主和测试构建。失败和两次小夹具原件均保留。

正式宿主 PID 45588 与 postmaster 48568 已停止。所有四个专用 PostgreSQL 实例均有停止原件，55567 和测试 HTTP 57821 均无监听。提交确认丢失时沿用既有协议，实测采用首响应正文丢弃与确定回滚。当前范围限草稿 POST 和归档导出，submit、close、正式恢复与生产启用各有对应验收。Actions 和部署均为零。候选分支及干净 DEV-2 交 COORD 处理集中审查、远端采用和席位释放。
