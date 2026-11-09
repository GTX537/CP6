# W427-01 · PGT-008 普通 DELETE 与密钥列权限

DEV-2 于 2026-11-09 20:27:29 UTC 从 Main #860 `5d36859c4fb653f87fcb503afacd758465ccffd4` 开工。定向验收使用一个已停机的 W368 私有副本及 PostgreSQL 18.6 原生客户端，合成 A/B 身份沿用原角色 OID 和可信登录目录。新增入口仅供本地验证。[原 PGT-008](../../acceptance/postgresql-tenancy.json#L251) · [验证入口](../../../../eng/wp02-02/shared-rls/verify_delete.py)

## 原生结果

本次 `native-02` 在代码提交 `3d43fc41211bb80fa9134d009a65e1b4fd1bb8c7` 上实际退出 0。验证器 SHA-256 `8c1e093f76b2f547548bc09ca7c2b6ec4593b9f71e79a5f55040aa8626c6d4ca`，结果 SHA-256 `c7b8b2431e460818c1d02be0ec803bee2933b8f3739232ac98299c15d7ee53aa`。[实际结果](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/actual.json)

- 副本短暂开放 `postgres` 库的控制 Owner 环回入口，重发三个合成 SCRAM 密码后恢复原 HBA 字节。Owner 与 A/B 的无密码连接均退出 2，空 stdout，原生报 `no password supplied`。之后原 A/B 普通 LOGIN 成功，各只见自己的合成行，`session_user`、`current_user` 及可信 helper 均与租户相符。[A 原观测](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/visibility-a.stdout) · [B 原观测](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/visibility-b.stdout) · [密码缺失原例](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/no-password-a.stderr)
- 实际隔离表 OID `43496`，ENABLE/FORCE 均为 true。目录恰有四条针对角色 OID `16384`、`16385` 的 permissive 策略。`SELECT` 和 `DELETE` 各以 `"TenantId" = cp6_tenancy_v1.current_tenant_id()` 作 `USING`，`INSERT` 用同一 `WITH CHECK`，`UPDATE` 同时使用两者。A/B 无角色成员关系和管理、复制或 BYPASSRLS 属性，均只有测试表 `Id` 列的 `SELECT`，仅 A 有该表的 `DELETE`。[ERP 原策略](../../../../CP6.Core/Persistence/Tenancy/ErpTenantRowSecurityV1.cs#L130) · [实际目录与全部角色属性](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/native-catalog.stdout)
- 普通 A 定向删除已存在的 B 行得到 `DELETE 0`，两行均保持原样。无条件且无 `RETURNING` 的第二条语句得到 `DELETE 1`，只删除 A。三个独立只读观察会话 PID `27064`、`4600`、`50164` 核得 B 完整行逐字段一致，`xmin=1267`、`ctid=(0,2)`。原 Stock 三条记录的完整行、`xmin`、`ctid` 与 W368 原见证逐项相等。[B DELETE 0](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/targeted-b-delete.stdout) · [A DELETE 1](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/unfiltered-a-delete.stdout) · [独立终态](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/fixture-after-unfiltered.stdout) · [原 Stock 对照](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/stock-after.stdout)
- A 在真实 `public."DataProtectionKeys"` OID `16523` 上执行 `SELECT "Xml" ... LIMIT 0`，psql 退出 3，原生关系 ACL 返回 `42501`，stdout 为零字节。控制端只读目录元数据，确认 A/B 可使用 `public` schema，但均无该表或 `Xml` 列的 SELECT 权限。[原 Core 表定义](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261002161715_PostgreSqlCoreBaselineV1.cs#L259) · [密钥投影原始错误](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/checks/data-protection-xml.stderr)

## 已有实证与原件

W368 原 PostgreSQL 18.6 实测 `T_WmsSequence` 普通常量 `UPDATE 1`、针对 B 的 `UPDATE 0`、非法新行和改属拒绝、目录与 `TRUNCATE`/产品 `DELETE` 的 `42501`。ERP 八表与 Identity Priority 四表沿用原生通过记录中的旧行保护、新行拒绝和原业务表 `DELETE` ACL 拒绝。以上原测试按各自原范围采用。[W368 实际交付](/D:/CP6-local-test/CP6-72H-20261109/W368-VERIFY/DELIVERY.md) · [ERP 10/10](../WP02-02/erp-tenant-isolation/DELIVERY.md) · [Identity Priority 5/5](../WP02-02/identity-priority-tenant-isolation/DELIVERY.md)

实际停机复制了 159,243,274 字节。复制前后的五项控制摘要一致。系统标识 `7706107046221120904`，库 `w368_stock_rls`，OID `16386`，私有端口 `55568`。复制回执 SHA-256 `c0e8d484d5f55b19335a239723073694aafc25c29a74375bffbacdc14d31eaef`，原 Stock 见证 SHA-256 `4aea46e2429b3266fe94aa246ef762cd959518f69d68007ad73e177f27caf6ee`。任务副本标记为可清理，原件和运行证据保留。[私有复制回执](/D:/CP6-local-test/CP6-72H-20261109/W427-01/stock-clone/copy.json) · [原 Stock 见证](/D:/CP6-local-test/CP6-72H-20261109/W368-VERIFY/run-20261109T095102Z/stock-rls/witness-after-denials.stdout)

## 实际资源与停机

`native-01` 于 20:41:20.290–20:43:20.889 UTC 采样 81 次。峰值 881 MiB，低于获准的 950 MiB 启动门限，120.600 秒后在 PostgreSQL 启动前退出 75。端口与两处控制摘要均已复核。采样 SHA-256 `eebdd1784d382745d6fc4e9ca5caf5a2c3e25ade07abdd3acf43d6d3e38f6362`，结束记录 SHA-256 `e8ee4b97b10630f20f40a5c022525252a9872e7bd8ef14fd6da3ad1f56dbb71e`。[首次采样](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-01/before-pg.json) · [首次结束记录](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-01/terminal.json)

`native-02` 于 20:51:07.573–20:51:15.098 UTC 在同一副本执行，实际 7.528 秒。启动前两次合格样本为 1,073、1,071 MiB，绑定复核后即时采样 1,071 MiB。启动后为 1,031 MiB，后续实测最低 1,014 MiB，提交空间最低 20,140 MiB。仅一个私有 PostgreSQL 实例，使用 32 MiB 共享缓冲区和 16 MiB 维护内存，客户端串行执行，零 CLR。[实际启动采样](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/before-pg.json) · [实际结束记录](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/terminal.json)

对任务 postmaster PID `47204` 执行 `pg_ctl stop` 退出 0，随后 `pg_ctl status` 退出 3，PID 文件消失，55568 无监听。独立复核原 W368 五项控制摘要，原 `pg_control` 为 `3cacbc962f0b4d975f7d845c5dee5923665d6646b7e5110596be79b122e73bae`。原件与副本的 HBA 均为 `0c8dc6e6e57399790417a6e13b3a8e1b5e27aa19708a2122148fbfe3bdcecd42`。停机记录 SHA-256 `5dad19d9a8cc55032df77abeb9de8795fbeda3eaf8dfa2425255b439ffa0061f`。[停机原件](/D:/CP6-local-test/CP6-72H-20261109/W427-01/native-02/stopped.json)

本次私有启动命令如下，启动门限为 950 MiB，启动后各阶段为 840 MiB，均要求 19 GiB 可用提交空间。

```powershell
$w = 'D:/CP6-local-test/CP6-72H-20261109/W427-01'
python -B "$w/run-w427.py" native --attempt native-02 --pg-minimum 950 --seconds 120 2>&1 |
  Tee-Object -FilePath "$w/test-native-02.log"
$w427Exit = $LASTEXITCODE
Set-Content -LiteralPath "$w/test-native-02.exit" -Value $w427Exit
exit $w427Exit
```

本窗的新写入限于副本合成登录和隔离测试表。既有 Stock、ERP、Identity 业务表权限实证按原范围复用，正式 PGT-008 由协调方合并核对。任务候选在本地 `codex/w427-01`，DEV-2 待协调收尾。
