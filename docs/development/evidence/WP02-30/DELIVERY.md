# WP02-30 独立入站与手工 Lead 的维护授权合同

执行人 `EXEC-WP02-14B`。Main DEV-1 `D:/CP6-worktrees/wave17-dispatch-20261018`，分支 `codex/wp02-30`。本卡交付 Main 基础合同，CRM 业务消费归 WP03-16b。Main 开工基线 `482a70a55ff4dacc4e44c6b8767f2e48bbc37f41`，CRM 联验基线 `9c33181d621bfed58ac93cba818e8a689ec9af8b`，受验 Main 产品 `4c14e1dad3fab70f7c9c21173b7b7faedc0fcb45`，树 `97aa023ad9e029e2ed81fd2abdd704197beae743`。WP02-05（含 WP02-05b）、WP02-10、WP02-15 均已完整 DONE 并核对实际采用。[Main PR](https://github.com/GTX537/CP6/pull/1301) · [派工 #382 原件](https://github.com/GTX537/CP6.AI-Handoff/blob/feff8cf3265fe73e11ef098bed3a6a1f34052cfb/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261121T153358Z-exec-wp02-14b-wp02-30/MESSAGE.md) · [授权 #381](https://github.com/GTX537/CP6.AI-Handoff/pull/381) · [已回读 START](https://github.com/GTX537/CP6.AI-Handoff/blob/99194300aee0dc508b494d6b64f1192567c52c93/handoffs/CP6-DEV-20261012/tasks/WP02-30/messages/20261121T153904Z-exec-wp02-14b-start/MESSAGE.md)

## 合同与可复验原件

询盘保留 v1 和导出 v2 的请求与签字节均绑定真实 SiteId 和 FormId。独立入站及手工 Lead 用 `POST /connect/lead-maintenance-context-v1`，复用 Basic 客户端校验、实际服务身份发布、IAM `crm-privacy:retention-run`、身份完成锁、Ed25519 签发材料及 TLS 判定。独立对象用途额外核对持久授予。Main 在自己的 PostgreSQL 对象锁内取得已签的 SOURCE、POLICY 和 HOLD 原件，依据当前角色、授予及数据库 UTC 时刻签发短期凭据。前向迁移 `20261121154000_LeadMaintenanceAuthorityV1` 建立范围和证据表，Main 签发为只读。同租户的原入站消息有唯一绑定。[接入及验签说明](../../../../contracts/lead-maintenance/v1/CONTRACT.md) · [现行询盘 v1](../../../../contracts/inquiry/service-permission-context/v1/schema.json)

| 交回 CRM 的原件 | 精确 Git 字节的 SHA-256 |
| --- | --- |
| [schema.json](../../../../contracts/lead-maintenance/v1/schema.json) | `c91db138f90a08ac647d17d4087bc5bbe7ba707b9f6918005e4d6769b8b40e4a` |
| [CONTRACT.md](../../../../contracts/lead-maintenance/v1/CONTRACT.md) | `dd7e1cf9f97f78839564cd2712c4174b83773513b69da26369f8baa7fcb31b0b` |
| [examples.json](../../../../contracts/lead-maintenance/v1/examples.json) | `51f3c0fc59236b7c40aaf67c1af5d82e72d796d0bf6964e2cd8540529fc14389` |

合同版本为 `cp6-lead-maintenance-context-v1`。两种对象各有 policy、hold、maintain 的实际签名回包及错权威、错用途、活动 hold 的拒绝回包。独立 Draft 2020-12 校验通过 12 个 HTTP 样例，Node Ed25519 回验 6 份 Main 签名和 14 份 Owner 签名。重编码或追加字节均使签名失效。样例仅带公开验证材料，按原签发时刻检查其时间语义。线上调用按新 nonce 和当前资格再取。

## 逐项运行结果

| 本卡断言 | 实际结果 | 夹具与模拟范围 |
| --- | --- | --- |
| 独立入站租户、权威、InboundId、原来源消息 | 实际 Main TLS 回包签进同一范围，PG 唯一索引拒绝把原消息绑给另一个 InboundId。冻结政策的适用期间包含数据库接收时刻，按 3600 UTC 秒得到期限，保留 opaque 政策和 keyPolicy 版本。后发改写冻结政策被 PG 拒绝。 | 隔离 PostgreSQL 18.6，模拟来源权威、公钥、接收数据及 3600 秒政策。 |
| 手工 Lead 租户、权威、LeadId | 使用原 `2024-02-29T10:30:00Z` 加 24 个 UTC 日历月，得到 `2026-02-28T10:30:00Z`。过期当前政策为 503。换有效新政策时，原 HOLD 摘要不符则 503，新绑定到位才恢复。 | 模拟手工 Lead、时间输入、政策和 hold，真实 Main 判断及 PG 读写锁。 |
| 用途与精确范围 | 三种用途分别需要对象授予。错租户、权威、对象或入站原消息返回 403，普通读、下载、导出、法律用途返回 400。测试额外 SiteId、重复字段及无效 nonce 均 400。 | 两类隔离合成对象，TLS 客户端及设备用测试身份。 |
| 当前撤权与过期 | 过期或关闭对象维护授予返回 403，仍有授权的取政策可成功。实撤 IAM action 后返回 403。真正持久身份停用后，旧宿主返回 503，采用新禁用登记的宿主拒绝客户端并返回 401。 | 服务身份、主机登记及撤权为隔离配置，使用真实 IAM 表和发布代码。 |
| 来源或 hold 不可证 | 无 SOURCE、无 POLICY、无 HOLD、错签名、已过期 HOLD、HOLD 指向错误的政策摘要分别失败关闭。返回 503，原错误不附对象详情。 | 策略/hold 发布者与私钥为模拟，实际 PG 原件与 Main 双层验签。 |
| 活动 hold 和确定边界 | 观察到 Main 实际等待同对象 advisory lock，hold 发布事务提交后，原维护请求返回 409 `active_hold`。授权 hold 查询签回 `holdActive=true`。未来 hold 起点缩短当前凭据有效期，旧发布序号被真实数据库拒绝。 | 同一隔离数据库的独立连接。hold 数据、未来时间和法律来源为模拟。 |
| 丢回包与原范围恢复 | TLS 故障钩子丢弃 Main 已生成的 200 回包。关闭宿主后由新 Kestrel 宿主和独立 DI/注册实例使用原对象、原消息、原用途及新 nonce 取回相同 scopeDigest 和相同 Owner 原件。撤权后沿相同请求拒绝。签发后的范围授予和证据行数各为原 3 行。 | 故障与客户端模拟，实际 TLS、签发、数据库及重复读取。新宿主在同一测试进程内重建。 |
| 既有询盘保留 v1、导出 v2 | 实际 Main TLS 到独立 CRM 消费者分别通过 1/1。覆盖当前维护资格，以及导出独立隐私动作、Site/Form 和旧签名用途。 | 两套隔离 PostgreSQL，固定 CRM 已采用源码，询盘、租户和身份均为合成。 |

定向 Main 与相邻回归 11/11，旧合同跨仓 TLS 2/2，均零跳过。台账工具 123/123。证据位于 `D:/CP6-test-artifacts/WP02-30/current-20261121`，采用 `verified.trx`、两份 `main-priv-*-verified/main.trx`、`wire-schema.json`、`wire-signatures.json` 和 `proof.json`。基线入口的 404 保留于 `red.trx`。测试登记更新时首次使用旧 epoch 被现行身份发布拒绝，按真实的版本和 epoch 轮换再验。另一次运行在 PG 监听端口分配失败，改用已验证空闲端口后的 `verified.trx` 为计数依据。

复验可用产品工作区运行下列命令。任务证据目录、TLS 及 PostgreSQL 均由测试夹具管理。

```powershell
dotnet build CP6.Tests/CP6.Tests.csproj -p:Cp6PostgreSqlDevelopment=true --artifacts-path <独立制品目录>
$env:CP6_PERMISSION_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_REQUIRED = '1'
$env:CP6_PERMISSION_PG_ROOT = '<未存在的绝对路径>/permission-consumption-wp0230'
$env:CP6_PERMISSION_PG_PORT = '<隔离空闲端口>'
$env:CP6_LEAD_MAINTENANCE_PROOF_DIR = '<本轮样例输出目录>'
dotnet vstest <独立制品目录>/bin/CP6.Tests/debug/CP6.Tests.dll '/TestCaseFilter:FullyQualifiedName~Lead_maintenance_actual_TLS'
```

旧合同的跨仓调用复用实际签发及消费夹具，命令、各自连接边界和回读结果见证据目录的 `test-main-priv.ps1` 与 `result.json`。通过范围为本地受影响 Main/CRM 入口和隔离 PostgreSQL。线上政策、真实法律 hold、KMS、生产配置和发布归各自批准流程，当前样例的时长不能用作实际法律政策。

## CRM 消费与台账

CRM-LEAD-01 保持开放，原 AC 45/60，必需运行修订 6/12。本卡核销的是 Main 提供者。WP03-16b 在 CRM 最终事务中复核对象、期限、政策、hold、完整保护图与原操作键，负责独立入站缺证时的 `NEVER_RETAINED` 及零 raw/key、手工 Lead 擦除、完整 CP22 v4、通知重驱、当前导出和先应用擦除水位的恢复。政策/hold 等证明按版本、范围摘要及上述签字节接入。AC-LEAD-057 至 059 保留在该卡的职责范围。[WP03-16b 任务卡](../../task-plans/WP03.md#wp03-16b)

#381 登记 WP02-30、WP03-28、WP03-29。后两卡为 PLANNED。WP03-28 承接 AC-LEAD-001、002、004–006、010–013、055、056。WP03-29 承接共享 W1-ACC-01，Lead、Account、ERP-BP 沿同一原 ID。WP03-07 承接 W1-ACC-03 至 06，WP14-02 继续承接 Lead 集成的 W1-ACC-07 与 AC-LEAD-060。两条原链 QUESTION 以原 ID 和授权逐项保留。[唯一台账](../../roadmap.json) · [#381](https://github.com/GTX537/CP6.AI-Handoff/pull/381)

三张 M 卡共 43200 秒，由 WP03-02 给 WP02-30 拨 14400 秒，WP03-15 给 WP03-28 拨 14400 秒，WP03-05 与 WP03-02 给 WP03-29 分别拨 12545.630 和 1854.370 秒。原卡池 2052 小时、计划总上限 2503.55 小时守恒。按 #380 查重后给 WP03-16 单次补记 250.367 秒，父卡累计 3775.198 秒，WP03-16b 额度 10624.802 秒，强制拆卡点 15937.203 秒。核销快照 `2026-11-21T16:05:51.802084+00:00`，WP02-30 本卡入账 1607.666 秒，归属核对另列 0.001 秒，计划累计 139721.405 秒。[#380 签收](https://github.com/GTX537/CP6.AI-Handoff/pull/380) · [单卡计划](../../planning/20261117-card-execution.md)

采用核对、同树隔离冒烟、精确分支处置和 DEV-1 释放以同一 PR、原链 DELIVERY 及本地 `adoption.json`、`adopted-smoke.trx` 和 `finish.json` 为凭据。唯一台账和四份项目状态页在同一 PR 同步。[Main PR](https://github.com/GTX537/CP6/pull/1301) · [原通信分支](https://github.com/GTX537/CP6.AI-Handoff/tree/codex/dev-handoff-20261117-exec-wp02-14b/handoffs/CP6-DEV-20261012/tasks/WP02-30/messages)
