# W399-01 · ERP v8 原键只读对账

DEV-2 于 2026-11-09 12:58:04 UTC 从 #837 `4c1fceb3e47c013c422ac05a0228e83f827247b5` 开工。新增入口接收独立 SHA-256 固定的 v8 私有归档和原资产根，复用全包校验以及当前物理目标、原 installation、租户 Ready、原迁移回执和 ERP 原目录检查。[对账入口](../../../../eng/wp02-02/rma-tenant-archive/reconcile_erp_deliveries.py) · [W395 v8 原包](../W395-01/DELIVERY.md)

工具在同一 PostgreSQL `REPEATABLE READ READ ONLY` 快照中查询三张原表。CommandInbox 按全局 C `bpchar`、Outbox 按全局 ICU `bpchar`、Bridge 按租户和 C `bpchar` 匹配，三者沿用原生尾空格语义。命中后才判断租户及原始键字节、原事件或信封绑定和实际 payload SHA-256。报告记录原/current RowVersion、状态、尝试次数及相关时钟。Outbox 仅记录租约字段的存在、到期和 token 是否变化。Bridge 以原键和完成时间记录进度，钩子效果留在业务验证范围内。

    python -B eng/wp02-02/rma-tenant-archive/reconcile_erp_deliveries.py `
      --pg-bin $PgBin --target-file $ExactSourceTarget --username $AuthorizedReadRole `
      --tenant $TenantId --bundle $V8Bundle --manifest-sha256 $ManifestPin `
      --file-content-root $OriginalAssetRoot --output $NewPrivateOutput

`0` 发布 `OBSERVED`，`2` 发布包含 `UNKNOWN` 或 `BLOCKED` 的报告，`1` 拒绝并清理未完成报告。全局他租键、同租原绑定不符均不输出当前行事实。报告为 `recoveryReady=false`，范围限于当前原键观察。[实库夹具](../../../../eng/wp02-02/rma-tenant-archive/verify_erp_delivery_reconcile.py)

## 实库结果

复用 W395 已有可丢弃合成副本、PostgreSQL 18.6、环回 55567。实际核对 `SHOW data_directory` 对应值、原数据库 OID 16386、系统标识 `7706139311168619192`、角色及默认只读，再运行入口。临时 HBA 只放行指定库和两个合成角色。每段 fixture DDL/DML 在同一写事务内重新验证精确副本和物理目标。当前任务没有新建集群或重编产品。

- A 使用 `v8-cut` pin `ef9876aa2ec112952c9793eb77bda0670b3b1200c85784061867ba36ba424bda`。B 使用 `v8-b` pin `984bab4a2f745eb9d0636df1cb17bf5611a74755cd12507d355f77a3b9e13c57`。两者各有三个 `PRESERVE_CURRENT`。A 的 CommandInbox 保持 `Processed`，尝试 `4 → 5`。Outbox 保持 `Dispatching`，观察到 token 已换、记录租约在快照时尚未过期、PublishedAt 为 NULL。Bridge `Pending → Completed`，完成时间 `2026-11-10 12:00:00.765432+00`。A 原 RowVersion 分别为 `02`、`03`、`04`，当前末字节分别为 `0f`、`11`、`12`。B 大小写不同的 CommandInbox 原键独立、尝试为 6，Outbox 原 token 保持，Bridge 同一原键仍是 `Pending`。
- 同名错误 ICU level3 的原 Outbox 列和唯一索引在 OID 43586 时，入口实际以 `erp-integration-key-collation-rejected` 拒绝。夹具在副本恢复原 level2 OID 16406，重新绑定列和唯一索引。拒绝前后及恢复后的 A/B 三表原生 COPY 均一致。低权限 `w395_backup` 实际在 FORCE RLS `CommandInbox` 得到 42501，未发布报告。错误 pin 和缺失资产根均在建立数据库会话之前拒绝。
- 合成原键移动后，A CommandInbox 对 B 小写键为 `UNKNOWN`，A Outbox 对 B 大写加尾空格别名为 `GLOBAL_KEY_CONFLICT`，Bridge 对 A 大写变体及 B 同键为 `UNKNOWN`。第二场景中，B 同大小写加尾空格 CommandInbox 为 `GLOBAL_KEY_CONFLICT`，Outbox 原 payload 实际字节改变及 Bridge 同租尾空格字节漂移为 `BINDING_CONFLICT`。两次报告退出 2，所有冲突及缺失条目均无当前行事实。
- 五段读取各有 A/B 原生 COPY 前后原件，逐段一致。夹具刻意改变两段负例之间的合成状态并推进原生 RowVersion。副本保留最后一段故障状态。原 W387 数据目录始终停止，其 `pg_control` 与 HBA 摘要前后一致。

原始文件在 `D:/CP6-local-test/CP6-72H-20261109/W399-01`。`test-red.log` 保留实现前缺入口。`test-native-01.log` 保留私有运行器比较摘要对象与 SHA 文本的首次失败，发生在启动数据库前。提取 SHA 字段后，`test-native-02.exit` 为 0。`native/checks` 保留原始报告、native COPY、目录与错误，`native/stopped.json` 记录 postmaster 49536 已停止、55567 无监听、临时 HBA 已移除。`run_owned_clone.py` 保留实际运行命令。当前 v8 两个固定包的资产根为空，带实际外部资产字节的原验证见 W389。[W389 私有资产验证](../W389-01/DELIVERY.md)

导入、重放、发送、外部业务结果、切换与正式 PGT-021/022 验收另行执行。分支 `codex/w399-01` 本地保存，DEV-2 交付时工作树干净，远端采用与席位释放交 COORD。[正式验收卡](../../acceptance/postgresql-tenancy.json)
