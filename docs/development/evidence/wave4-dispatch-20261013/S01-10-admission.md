# S01-10 PostgreSQL 精确存储准入核对

结论：**可由 COORD 开放 B 的实现窗口；未发现缺源、程序或硬件资源阻塞。** 必须先登记独占范围；真正 PG 验证须在专属 cluster 完成配置、归属核对后执行。当前 **未 provision、未连接数据库，PG/业务均 NOT_RUN**。本报告只读调查，不更新台账，不授予 Owner/租户布局采用完成状态。

基线：COORD 已核 Main `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`。本子任务没有执行 Git/远端查询；本地台账仍为 S01-10 `PLANNED / NOT_CHECKED`。未来工作树 `D:/CP6-worktrees/s01-10-20261012`。

## 依赖与准确输入

S01-03/04/06 与 S02-00 均在本地台账 DONE；已交付的合同单测分别为 205、132、60 C#＋81 TS/Schema，S02-00 为 `cp6-pg-tenancy-v1` 设计、运行采用 UNPROVEN。只引用原执行来源，不把它们作为 PG 结果。

| 依赖 | 实现源 SHA | 主线交付 |
|---|---|---|
| S01-03 | `b24f76445ffdc9b4df0de77eb394a6aa47726a0c` | PR188，远端回执 `08bf7c923fa0b21af39d5e1f1fd550ed635246ed` |
| S01-04 | `bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8` | PR190，`688383439311a3babf00bae98ee426122ce60cd1` |
| S01-06 | `f406e4e515a7ce82d77db5307e467e7bcdbc25e4` | PR191，`bd25558ea4c0981b0cff1e4fa77ab8f61277211c` |
| S02-00 | `9e2f1d4f1ed7048c125d350e31a4067745ae3233` | PR178，`b584563e5ebf323c840866e9c242a710f8c8d6ec` |

当前文件重新计算 SHA-256：

- 包 `CP6-S01-CONTRACT-INPUTS/1.0.0`：profile-inputs `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`；vectors `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。
- `CP6-EXACT-NUMERIC-STORAGE/1`：`b67a8fea437a1db1990c3513a09cd9bc791704aac3b3a8f3f9b80642b5d4a607`。
- ATP DDL 原件 `cf6943f7f350ad0b6fa532aca93a2c5beef00a32ec975965b9f5b665d029cd4f`，组合 `29cd39f1448bf998a377497413baab6ff884d1a3939771479d0625fce31df1e0`，9 个操作同时包括 PLAN-IF-004/005。
- CREDIT/INV 三原件与 ATP 四组原件/组合，共 **11/11 hash 相符**；66 个字典 source_pointer 的表/列/旧类型/nullability **66/66 相符**；ATP 五行定义 **5/5 相符**。完整路径、pin、现有代码哈希在同名 JSON。

## 实现必须保留的边界

**CREDIT：** 42 个准确原字段绑定（signed 4、nonnegative 33、positive 5）。同一行/事务存 `numeric(38,0)` coefficient＋`smallint` scale 0..8；BigInteger 绑定和读取，零规范 `(0,0)`，scale>0 禁系数尾零。先按最终字段 profile 校验，禁止 decimal/浮点中转、舍入和截断。Npgsql 支持 `BigInteger → Numeric` 与 `GetFieldValue<BigInteger>()`；默认 numeric 读取为 decimal，不能误用。[官方驱动映射](https://www.npgsql.org/doc/types/basic.html)

**INV：** 66 项覆盖 13 表，51 非空/15 可空；原映射 scale 上限分布 0×6、8×20、12×27、18×13。各项采用 ASCII 规范有符号整数 `text`＋scale，null 成对耦合，原 wire 独立保存；禁止把任意整数位 CAST 到 numeric/SUM。JSON 已逐项列全映射。

逐字段无损证明是必需交付：20 项旧存储 scale=8，原 Money.amount/RoundingTrace 金额仍引用 Decimal18；Money.scale/舍入政策 scale 不能当成通用 wire 小数上限。实施者须说明每个原字段、数值与政策的关系。若确需扩宽存储范围，记录有版本的 PG 映射与无损依据并取得 COORD 公共合同窗口，不能静默限8或改写固定源。

MAVG 的分子原词法允许 `00`/`-0`，分母必须正；ACTIVE 的五个平均/余数字段均非 null 且 afterQty>0；EMPTY 均 null 且数量/金额为零。`InventoryRatio` 只提供 gcd 约分的内部运算，必须另保原分子/分母与 wire；余数属于约分后分母，不能替换原值或 receipt 字节。

**ATP：** 采用 `ATP-STABLEID-LEXICAL/20261011-R1`，完整组合必须保留 PLAN-IF-005 的五处恢复锁序/绑定修订。只执行经审查的 fixture 子集，禁止整份历史业务 DDL 直接落库。

| 准确组合列 | 原行 | nullable |
|---|---:|---|
| `plan_atp.evaluation.result_uom` | 132 | 是 |
| `plan_atp.segment.uom` | 291 | 否 |
| `plan_atp.basis.native_uom` | 325 | 否 |
| `plan_atp.basis.result_uom` | 327 | 否 |
| `plan_atp.reservation_ledger.uom` | 382 | 否 |

五列均为 varchar(128)，完整 CHECK 为 `col IS NULL OR col !~ '[^A-Za-z0-9._:/-]' AND col ~ '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$'`，四列另有 NOT NULL。精确原定义在 JSON。

PG 的 `numeric(38,0)` 会对非整数输入先舍入；varchar 的显式长度 CAST 和超长尾空格可能截断。适配器必须在参数绑定前校验原输入，ATP 不设截断 Size/CAST；若声称数据库拒绝原始非法文本，应在 text 入参、发生类型收窄前守卫，不能只靠最终列 CHECK。[PG numeric](https://www.postgresql.org/docs/18/datatype-numeric.html)、[PG character](https://www.postgresql.org/docs/18/datatype-character.html)

## 独占资源

- 已发现 `C:/Program Files/PostgreSQL/18/bin` 下 psql/postgres/pg_ctl/initdb，程序版本 **18.6**；.NET SDK **10.0.302**，Npgsql 8.0.8 缓存可用。实际服务器/驱动运行版本待实测。
- 现有 `postgresql-x64-18` 服务运行于 **5432**，明确禁止连接或操作。Docker 已安装，current context=desktop-linux；Docker Desktop Service 停止，未探测 daemon，本任务无需依赖它。
- COORD 预约 **127.0.0.1:55410** 与 **D:/CP6-local-test/S01-10/**。55410 检查时无监听；资源尚未创建，实施时重新检查，冲突则返回协调，不能占用/终止其他进程。
- 新 run 目录 `<owner32>/{data,private,logs,receipts}`；数据库 `cp6_s0110_<UTCyyyyMMdd>_<owner8>_exact`，完整 marker `CP6-S01-10:<owner32>`。新 initdb、UTF8、仅 loopback、SCRAM；private 凭据限当前用户，不读业务配置/Secret。
- setup bootstrap 与 NOLOGIN owner、受限 LOGIN writer 分离；writer 无 SUPERUSER/CREATEDB/CREATEROLE/BYPASSRLS/对象创建权。DDL/setup 和普通存储验证分开记录。
- DDL、测试、清理前核路径归属、端口、数据库名/OID、完整 marker、session_user/owner、cluster 身份及数据目录。拒绝复用同名未知库。清理须无其他活动会话，不 force kill；仅凭实际归属回执停止本任务 pg_ctl；递归删除先核绝对路径仍在预约目录内，保留证据。

现有 `OwnedTestDatabase.cs`/lifecycle 写死 WP6/task/role，并固定 SearchPath=public；不能给 S01-10 冒用 WP6 marker。建议在本任务测试目录内实现 PG-only runner，复用既有“拒绝碰撞、完整 marker、OID/owner/角色、无会话清理、诊断脱敏”机制，保持公共助手只读。此 fixture 不证明 S02-00 租户角色/RLS/池/迁移方案已采用。

## 写入窗口与验证

B 仅写：

- `CP6.Persistence.PostgreSql/Contracts/ExactValueStorage.cs`
- `eng/contracts/s01/postgresql-mappings.json`
- `eng/contracts/postgresql-tests/**`（新 csproj、自己的 lock、fixture、runner、测试）
- `docs/development/evidence/S01-10/**`

固定输入、S01-03/04/06 源码、叶子公共测试项目、OwnedTestDatabase/lifecycle、现有项目/依赖、EF context/迁移、业务 Owner、前端均只读。总台账、生成页和四状态页由 COORD 交付窗口同步；发现实际需要的公共改动先协调串行窗口。测试优先引用实际 Persistence 程序集，不复制第二份权威实现。

待实施后实际运行：

```powershell
dotnet restore eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj
dotnet test eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj --no-restore --filter FullyQualifiedName~ExactValueStorageTests --logger "trx;LogFileName=s01-10-postgresql.trx" --results-directory docs/development/evidence/S01-10/postgresql
```

前置 runner 只配置上述独占资源；真实执行结果须包含 SQL/驱动版本、源码/程序集 hash、脱敏归属回执和 TRX。若测试仅 link 生产文件，还需一次实际 Persistence 项目 build 证明程序集集成。已有不变合同测试可引用原结果；不因 SHA 改变重跑整仓。

必测：固定 **56 向量（CREDIT25/INV9/ATP22）**；66 映射/null 耦合；CREDIT 38位/30+8/符号/非法值；INV 90位与18/12小数、原负零/尾零；MAVG ACTIVE/EMPTY/正分母/余数；ATP 五列16/17/128接受与129/空串/非ASCII/前导符号/LF/CRLF/尾空格拒绝；失败事务全量回滚，断开后用新物理连接读回原值/原wire。**配置缺失或归属失败记录 BLOCKED；不得 skip 后计 PASS。**

未来真实 Owner 采用清单另列每表所有写者/raw SQL/bulk/导入、原wire/receipt、旧表示可恢复性、同连接事务、租户/唯一约束、迁移与回退兼容及全写者切换。现有 CreditControlService(decimal)、StockFinBridge(round2)、ISupplyService(decimal) 均不因 fixture 成功成为新 Owner。

本轮只新增这两份 scratch 报告；没有仓库/Git修改、Actions、DB连接/迁移、服务操作、部署或网络发布。