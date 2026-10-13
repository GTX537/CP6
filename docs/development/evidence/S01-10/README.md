# S01-10 PostgreSQL 精确存储候选交付

**当前结论：实现与本地真实 PG fixture 已验证；20 项无损尺度补充仍待 COORD 窗口确认，尚未采用到正式 `postgresql-mappings.json`，尚未合并 main / DONE。** 本报告不替代唯一台账。初始/最新已核主线 `e20740e37e20bc0210e0b32c5670c2bce962002a`；创建/输入基线 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`。代码及证据在分支 `codex/s01-10-postgresql-exact-20261012`。

## 实际改变

- 在现有 [Persistence 程序集](../../../../CP6.Persistence.PostgreSql/Contracts/ExactValueStorage.cs) 加入小型适配器；复用既有 Npgsql 8.0.8、S01-03/04/06 精确类型，没有改公共项目/锁、EF 模型、迁移或旧写入入口。
- CREDIT 先核最终原字段 profile，再以 `BigInteger` 绑定 `numeric(38,0)` coefficient 与 `smallint` scale，通过 `GetFieldValue<BigInteger>()` 读回。恢复时拒绝不规范 pair，nullable 只允许两列同时 null；42 个原字段绑定逐项测试。
- INV 保留规范整数 text、数值 scale、独立 originalWire 三列，校验原串与规范值相符；任意整数位不经 decimal、浮点或有限 numeric。MAVG 原 `00`/`-0` 分子及未约分分母独立保存；ACTIVE/EMPTY 的五个可空字段、数量/金额与正分母关系经过真实约束验证。该适配器不是完整 Rational/MAVG 对象 codec 或成本算法。
- ATP 五个 UOM 列采用完整 composed-14 中的确切定义，同时保留所选完整表的状态、引用、表示链和扣减关系 CHECK。只执行[审阅过的定义子集](../../../../eng/contracts/postgresql-tests/atp-reviewed-subset.sql)及[恢复锁/绑定关键段](../../../../eng/contracts/postgresql-tests/atp-recovery-subset.sql)，不执行整份历史 DDL、真实恢复/publish/授权过程。
- 适配器不设置截断参数 Size、Precision 或 Scale。数据库原始输入拒绝测试采用收窄前的 text 守卫；单独记录 `numeric(38,0)` 可先把1.5舍入、varchar可先裁去超长尾空格的实际反例，未把列 CHECK 误称为原始入参验证。[PostgreSQL 数值类型](https://www.postgresql.org/docs/18/datatype-numeric.html)、[字符类型](https://www.postgresql.org/docs/18/datatype-character.html)、[Npgsql 类型映射](https://www.npgsql.org/doc/types/basic.html)

## 尺度补充与协调请求

原 `CP6-EXACT-NUMERIC-STORAGE/1` 的 66 项覆盖13表，51非空/15可空；原 scale0/8/12/18 的数量分别为6/20/27/13。20个旧scale8字段的 native Money.amount / RoundingTrace 金额仍允许 Decimal18，业务 policy scale0..8不能作为 wire 小数位限制。

[完整66项提案](postgresql-mappings.proposed.json)版本 `CP6-POSTGRESQL-EXACT-MAPPINGS/1.0.0`：明确源字典pointer、native pointer链、派生绑定与原存储范围；仅20项PG数值scale从0..8扩到0..18，其余46项不变。原源文件与固定预期未修改；每项保留原串，可无损区分负零/尾零。20项均已在明确标为 **PROPOSED_PENDING_COORD** 的临时 fixture 验证18位非零小数；这不是正式合同采用。

已通过 Handoff 发布并逐文件读回远端固定字节：

- START `d2232a5224223ffad6b006a78cbcc1bc6b99f256`。
- 无损补充 QUESTION `cc6dde813dfa841f9302c9cc5b9b9a25ded25f68`。
- 完整提案附件 `0ef3cec4b113032f10b96ca45046d020d41745ce`，路径 `handoffs/CP6-DEV-20261012/tasks/S01-10/messages/20261013T054851Z-mapping-review/`。

待 COORD 确认具体公共合同窗口后，才将相同字段映射采纳到已分配的 `eng/contracts/s01/postgresql-mappings.json` 并记录版本/决定。不得以消息上传、已通过实验或等待时间代替确认。

## 验证证据

测试项目引用**实际 Persistence 项目/程序集**，没有 link 一份测试专用生产实现。PostgreSQL服务器 **18.6**，Npgsql **8.0.8.0**；.NET SDK10.0.302 / net8.0。包恢复沿用锁定依赖；生成的唯一新依赖锁在本任务测试目录。

| 执行 | 结果 | 准确范围 |
|---|---:|---|
| [完整真实PG运行](runs/full-storage/run.json) | **210/210 PASS；0失败/0跳过** | 固定56向量（CREDIT25/INV9/ATP22）、42原金额字段、66映射极值/null、非法pair/token、131073整数位INV、MAVG状态/原串、ATP五列与关系/锁序、回滚/重连/重复持久化、owned资源边界 |
| [收尾定向回归](runs/pin-reconnect-regression/run.json) | **57/57 PASS；0失败/0跳过** | 增加测试运行时固定包原始字节hash/profile绑定校验，重跑全部56向量；给重连场景补齐ATP五列128字符恢复 |
| [启动/清理定向回归](runs/lifecycle-regression/run.json) | **1/1 PASS；0失败/0跳过** | 同名库拒绝、OID/marker/角色漂移拒绝、活动会话拒绝、writer禁止DDL/SET ROLE、正常清理 |
| [源码/原设计核对](source-verification.json) | **11/11来源hash；66/66字典；5/5恢复修订** | 固定包Git字节hash、三个native原件及ATP四组原/组合输入；9个明确选定SQL定义 |

210项与57项包含重复覆盖，**不相加为267个独立用例**。完整运行后只改变测试输入保护与ATP重连断言，生产适配器和SQL未变；未受影响结果复用完整运行，57项为实际新增执行。后续文档/审查记录不触发全仓编译。每个运行目录保存实际命令、exit code、TRX逐测试结果、工作文件与程序集SHA256、数据源身份、未变输入证明及所用proposal hash；未将未提交源码冒称某个历史SHA已包含。

首次完整编译/两次启动器失败及最初 RED 均在 [FAILURES.md](FAILURES.md) 逐项说明，日志/TRX原样保留。失败没有改名PASS或跳过。早期Windows ACL和输出管道问题已按根因修复。

复现命令（只在本任务已分配的Windows本地资源窗口运行）：

```powershell
dotnet restore eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj --locked-mode
dotnet test eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj --no-restore --filter FullyQualifiedName~ExactValueStorageTests
```

需要完整身份/产物证据时使用 `eng/contracts/postgresql-tests/run_local.py <新的run-id>`；它执行同一测试命令并拒绝复用证据目录。该runner自动创建并清理自己占有的集群；资源不存在/冲突/归属失败会使测试失败并记录BLOCKED，不会skip。原设计复核脚本 `prepare_sources.py` 只读取固定归档并提取指定定义，不连接数据库。

## 资源、权限与未验收范围

仅使用 `127.0.0.1:55410` 和 `D:/CP6-local-test/S01-10/<owner32>/`。每轮新initdb/UTF8/SCRAM/loopback，私有密码文件ACL仅当前用户；bootstrap、NOLOGIN owner、受限LOGIN writer分离。DDL/测试/清理前核对 marker、完整data_directory、system identifier、数据库OID/Owner和session_user；普通存取以writer执行，Pooling=false。清理先核所有其他client session为零，正常DROP与pg_ctl smart stop，无强制终止。

成功运行的[lifecycle目录](lifecycle/)显示数据库已删除、进程已停止、凭据文件与lease已移除；仅保留停止的本任务空集群/本地日志。两个失败运行另有明确身份/收尾回执。不访问或停止5432，不读取业务Secret，不上传密码/私有配置/数据库卷。

[逐Owner采用清单](OWNER-ADOPTION.md)覆盖13个INV表、42个CREDIT字段、ATP五列及恢复入口；真实表采用、旧writer全部切换、迁移/回退兼容、同连接事务、完整对象合同和tenant schema/连接池/权限隔离仍待各自任务。S02-00设计不因此成为运行PASS；247完整运行场景、41历史REFERENCE_ONLY、ITER/REL缺原件及WP05 PROPOSED范围均保持原状态。

本任务无Actions取消项、触发变更或远程执行，没有发布包或部署。推送前还须核候选与受保护main的工作流事件；共享台账/生成页/四状态页由COORD在正式交付窗口串行同步，远端main包含性核对后才可DONE。
