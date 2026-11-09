# W427-01 · PGT-008 普通 DELETE 与密钥列权限

DEV-2 于 2026-11-09 20:27:29 UTC 从 Main #860 `5d36859c4fb653f87fcb503afacd758465ccffd4` 开工。定向验收使用一个已停机的 W368 私有副本及 PostgreSQL 18.6 原生客户端，合成 A/B 身份沿用原角色 OID 和可信登录目录。新增入口仅供本地验证。[原 PGT-008](../../acceptance/postgresql-tenancy.json#L251) · [验证入口](../../../../eng/wp02-02/shared-rls/verify_delete.py)

## 新执行范围

隔离测试表沿用 ERP 的四条专属策略及原 `session_user` 映射 helper。`SELECT` 和 `DELETE` 各有旧行 `USING`，`INSERT` 有新行 `WITH CHECK`，`UPDATE` 同时有两者。启用并强制 RLS，核对实际策略组合、角色属性和有效授权。A/B 只得到测试表 `Id` 列的 `SELECT`，A 另有该表的 `DELETE`。普通 A 定向删除实际 B 行，随后执行无条件且无 `RETURNING` 的删除。独立只读会话核对 B 的完整行、`xmin` 和 `ctid`，以及原 Stock 三条记录。[ERP 原策略](../../../../CP6.Core/Persistence/Tenancy/ErpTenantRowSecurityV1.cs#L130)

密钥反例使用真实 `public."DataProtectionKeys"` 的 `SELECT "Xml" ... LIMIT 0`。控制会话只查询表、列、Owner 与 ACL 的目录元数据。PostgreSQL 原生拒绝及空输出是验证条件。[原 Core 表定义](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261002161715_PostgreSqlCoreBaselineV1.cs#L259)

## 已有实证与原件

W368 原 PostgreSQL 18.6 实测 `T_WmsSequence` 普通常量 `UPDATE 1`、针对 B 的 `UPDATE 0`、非法新行和改属拒绝、目录与 `TRUNCATE`/产品 `DELETE` 的 `42501`。ERP 八表与 Identity Priority 四表沿用原生通过记录中的旧行保护、新行拒绝和原业务表 `DELETE` ACL 拒绝。以上原测试按各自原范围采用。[W368 实际交付](/D:/CP6-local-test/CP6-72H-20261109/W368-VERIFY/DELIVERY.md) · [ERP 10/10](../WP02-02/erp-tenant-isolation/DELIVERY.md) · [Identity Priority 5/5](../WP02-02/identity-priority-tenant-isolation/DELIVERY.md)

实际停机复制了 159,243,274 字节。复制前后的五项控制摘要一致。系统标识 `7706107046221120904`，库 `w368_stock_rls`，OID `16386`，私有端口 `55568`。复制回执 SHA-256 `c0e8d484d5f55b19335a239723073694aafc25c29a74375bffbacdc14d31eaef`，原 Stock 见证 SHA-256 `4aea46e2429b3266fe94aa246ef762cd959518f69d68007ad73e177f27caf6ee`。任务副本标记为可清理，原件和运行证据保留。[私有复制回执](/D:/CP6-local-test/CP6-72H-20261109/W427-01/stock-clone/copy.json) · [原 Stock 见证](/D:/CP6-local-test/CP6-72H-20261109/W368-VERIFY/run-20261109T095102Z/stock-rls/witness-after-denials.stdout)

## 实际门限和续跑

`native-01` 于 20:41:20.290–20:43:20.889 UTC 采样 81 次。峰值 881 MiB，低于本轮获准的 950 MiB 启动门限，120.600 秒后退出 75。55568 无监听和本窗 postmaster PID，原件与副本的控制和 HBA 摘要一致。PostgreSQL 未启动，普通 SCRAM、两项删除及真实密钥列投影均待实测。采样 SHA-256 `eebdd1784d382745d6fc4e9ca5caf5a2c3e25ade07abdd3acf43d6d3e38f6362`，结束记录 SHA-256 `e8ee4b97b10630f20f40a5c022525252a9872e7bd8ef14fd6da3ad1f56dbb71e`。[实际采样](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-01/before-pg.json) · [结束记录](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-01/terminal.json)

协调方重新分配资源后，在同一停机副本上开始新的启动尝试。启动要求两次达 950 MiB 并即时复核，启动后各阶段为 840 MiB，均要求 19 GiB 可用提交空间。一个 PostgreSQL 进程使用 32 MiB 共享缓冲区和 16 MiB 维护内存，客户端串行执行。

```powershell
$w = 'D:/CP6-local-test/CP6-72H-20261109/W427-01'
python -B "$w/run-w427.py" native --attempt native-02 --pg-minimum 950 --seconds 120 2>&1 |
  Tee-Object -FilePath "$w/test-native-02.log"
$w427Exit = $LASTEXITCODE
Set-Content -LiteralPath "$w/test-native-02.exit" -Value $w427Exit
exit $w427Exit
```

任务分支为本地暂停检查点，DEV-2 仍登记 W427-01。正式矩阵待原生结果和协调审查。
