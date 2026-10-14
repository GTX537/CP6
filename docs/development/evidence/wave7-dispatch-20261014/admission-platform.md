# Wave7 Platform 准入建议（只读）

建议 C=WP02-12、D=WP02-26 在 COORD 登记本文的窄范围和独占本地资源后置 READY，进入限定库实现。两项直接前置已交付，存在不依赖 Actions、生产环境或付费外网的真实本地验收路径。当前台账仍是 PLANNED / NOT_CHECKED / 未领取；本报告不代替登记，不代表本轮测试已通过，也不扩大原业务验收。

本报告未启动、停止或连接数据库，未运行测试、创建容器或进行远端写入。只写这份报告及 JSON。资源实测、精确路径和全部限制以同目录 admission-platform.json 为机器可读版本。

## 固定基线和前置

- Main：`1129953156b7775152a74ad2f51cb95ef430ee56`，D:/CP6。保留原有未跟踪资料；没有修改。
- Platform：`b28db763f70ab6f164cb7c87d6ce49a6d97267f1`，D:/CP6.Platform-worktrees/wave6-integration-20261014，干净。
- 本次本地 ancestry 检查以下全部为祖先，退出码 0。远端包含性复用 COORD wave6 固定回执；本次不虚构重新查询远端。

| 前置 | 实际交付源码 | 可消费范围 |
|---|---|---|
| S01-09 | 753696c8333325d9faa5d80ea429ab8b9480e71c | 精确 native 身份、原字节；不是所有业务事件注册表 |
| WP02-01 | d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6 | 可信上下文原语；实际 verifier 仍由 Host 提供 |
| WP02-08 | 2e659e382e95988ee06799cd2feb0d580dcd8323 | 已修 #66/#67 的 OutboxV2；不是真实 broker/Owner 采用 |
| S01-03 | b24f76445ffdc9b4df0de77eb394a6aa47726a0c | CreditAmount 精确 BigInteger coefficient/scale |
| S01-04 | bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8 | Inventory 原词法、精确值和分数，各自分离 |
| WP02-25 | 80f43346e4e94e6d7b402d7b557c685b475ba743 | 已修 #68/#69 的本地观测 V1；Completeness 仍 Incomplete |

WP02-08 的 Blocked 原 mutation 回放与发送预算修复、WP02-25 的单调新鲜度与 no-store 修复已读集中复查。新任务不再从旧候选重建能力，也不修改这些任务历史失败或报告。

## 来源与当前源码结论

已完整读取 WP02-12/26 台账行、卡片和任务定义；读取基础验收目录全部 MSG40、OBS32、TEST24 条断言。原设计接受仍是静态身份；各本地子断言与原真实 Owner AC 必须分开记录。

精确原件（本次核 SHA-256）：

- MSG v0.2：`docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7b/7b59ff237b27e8ef__PLT-MSG-01_完整静态设计_v0.2.md`，`7b59ff237b27e8efc452ae1ecf776ac77002382466a9e7d9744a3bf924e505e2`；全文实读。
- OBS v0.1：`.../20/20c9a128b3f2bab0__PLT-OBS-01_完整静态设计_v0.1.md`，`20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99`；读取原 SLO 章节及归并手册相关完整语义。
- TEST v0.1：`.../d8/d8b4ef19a1b3a0a5__PLT-TEST-01_完整静态设计_v0.1.md`，`d8b4ef19a1b3a0a528ddd7831f5159295d75b7eecadfa787e36ad028d362c5aa`；读取原 case/fault/capture 章节及归并手册全文。
- 共同入口：`docs/development/task-plans/WP02.md`、`task-definitions.json`、`roadmap.json`、`assessments/20261011/catalogs/foundation.json`、`docs/design-handbook/modules/PLT-{MSG,OBS,TEST}-01.md`。

当前 Cp6CloudEventValidator 已采用 S01 严格 JSON、拥有输入副本和递归重复业务字段拒绝，保留根 traceparent/tracestate 重复时丢弃 trace 的既有例外；不能当“完全未实现”重写。仍缺统一字节上限与有限、安全 location 收集。Cp6ContractBundle 是单实例单精确版本 loader；原设计明确选独立实例静态选择，不能改成新动态 registry。

Cp6DaprEventPublisher 原样传递 structured bytes；Cp6DaprDeliveryValidator 接受调用者传入的 topic/key 字符串，自己不认证来源。MessageContextExecutorV1 的注释也要求 Host verifier 核实际 transport/producer。旧 p05 fixture 直接取 __topic/__key，错误就 DROP、成功只存内存，且 component authType:none；不能复制为“可信生产者、持久隔离、业务提交”的成功证据。

Cp6SloEvidenceEvaluator 只核声明和阈值，不核原 source/query bytes，不推导 ratio/percentile，不证明 production flag。Cp6TelemetryRecorder 是全局 listener、无界队列、numeric 统一 double、direct-parent 链；这些是 D 要补的具体差距。WP02-25 不能替 D 提供真实总体或生产 SLO 资格。

## C / WP02-12 可执行派工

范围为显式 opt-in 传输 V1，限定可信路由与生产者、精确合同选择、原体传输/隔离/恢复和本地真实 broker 证明。保持 Generic P05 与 C02 native profile 分开，不开真实业务路由，不另造 Inbox/业务幂等账。

允许写入：

- `src/CP6.Platform.Messaging/TransportV1/`
- `src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs`
- `src/CP6.Platform.Messaging/Cp6ContractBundle.cs`
- `src/CP6.Platform.EntityFramework/TransportV1/`（仅消费既有 OutboxV2 的窄 publisher bridge；无需改现有 EF 模型/原语）
- `tests/CP6.Platform.WP0212.Tests/`
- `tests/CP6.Platform.WP0212.Fixture/`
- `tests/wp02-12-runtime/`
- `docs/WP02-12-MESSAGE-TRANSPORT-V1.md`
- `docs/development/evidence/WP02-12/`

只准在任务 tests 子目录增加项目和子级 Directory.Packages.props/NuGet.config，引用现有版本；不得改 shared sln/root props、生产 csproj、公共 Contracts、Testing、现有 OutboxV2/InboxV1 或 Main/CRM。现有 Messaging→EF 反向依赖不允许；IOutboxPublisherV2 的 bridge 放 EF/TransportV1，当前依赖方向已支持。

实施应交付：

1. 固定 (profile,route,type,schemaId,schemaVersion)→bundle/schema hash 和独立 validator/publisher；重复键即使同 hash 也拒绝。可信入口先选 route/profile，body 或 header 不得自选。无 latest/fallback/远程 schema。
2. 真实 verifier 的 producer/tenant/region 资格与 envelope、外层 id/aggregate/version 精确一致。任意 bool/DTO 自报认证不能产出积极资格。
3. 有界原字节、副本、media/size/depth/重复字段与安全诊断。raw envelope hash、业务输入 digest、schema hash、S6 typed digest 分域；旧消息按原 tuple/原 bytes 选择，不重新序列化。
4. Confirmed 只表示运输接受；取消、timeout、模糊 ACK 保 Unknown 与原 message/mutation。DLQ 或 quarantine 的原体保留需要实际持久策略，恢复不换键、不编辑原 payload。DROP 只能在所需持久隔离完成后。
5. 保持 OutboxV2 的 committed send-intent、无网络事务锁、剩余预算和 unknown readback 语义。可在独立 PG fixture 中实现测试 Owner，但明确不是 Main/CRM Owner。

实际验收必须有：

- 单元/合同正反例：错误 producer、profile/type/version/hash、重复选择、错 tenant/region、伪 header、未知 schema、限长、原体不变。
- **真实** Kafka4.3.1＋Dapr1.18.2 roundtrip：独立读取 Kafka 实际 record 的 topic/key/body hash，与真实 receiver 收到的数据核对；不能用 payload 重新推导 key 再冒 broker 证据。
- 认证 Host/sidecar 的真实入口，及能证明 producer 对此 route/topic 有权的边界。推荐任务私有 SASL/ACL 或同样明确、独立验证的专用路由授权；仅 authType:none、孤立网络、sidecar token 或 scope 标签不能各自自动证明全链 producer 身份。未证明则相应能力 Unproven、新 route 关闭，不能以缺项宣称本卡正向保证完成。
- 真实错 key/topic/producer、伪造直连 header、缺 metadata、broker/sidecar unavailable；原 message 重试和 ACK unknown。注入丢 ACK 必须标 injected，与实际 broker 行为分列。
- 若声明 DLQ/Outbox 持久恢复或 commit/ack 边界，需 C 自己 PG55430 的真实事务与原字节 readback；仅委托返回 success 或内存 fixture 不足。
- 保留40项原 MSG AC 全映射，能在库/本地 fixture 层证明的子断言单列，业务 Owner 部分继续 NOT_RUN，不能全表 N/A。

条件消费者：WP02-29 仍需其其余直接前置与真实 CRM Host/Owner。C02 新业务采用仍需 WP02-05/06 真实 producer/projection，五 type 各1.0.0、原 index hash `24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63`、原 topic `cp6.platform.events.v1`、冒号 key、rawPayload=true、专属 publisher/validator不变。WP05-34/61、WP10-43、WP12-20 仍需 Main WP02-10/13 和各自真实输入/Owner；不因本卡 DONE 自动 READY。WP14-08/09 另有全链门。

## D / WP02-26 可执行派工

范围为版本化 SLO 资格化与 TEST_ONLY 辅助，不改变原 SLO1.0.0 的字段/历史 evaluator 语义，不声称测试产生生产 SLO。

允许写入：

- `src/CP6.Platform.Contracts/SloQualificationV1/`
- `src/CP6.Platform.Testing/EvidenceV1/`
- `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs`
- `src/CP6.Platform.Testing/Cp6RecordedMetric.cs`
- `src/CP6.Platform.Testing/Cp6HttpFaultScript.cs`
- `src/CP6.Platform.Testing/Cp6HttpFaultHandler.cs`
- `src/CP6.Platform.Testing/Cp6FaultInjectionServiceCollectionExtensions.cs`
- `tests/CP6.Platform.WP0226.Tests/`
- `tests/CP6.Platform.WP0226.PostgreSqlTests/`
- `docs/WP02-26-SLO-TEST-EVIDENCE-V1.md`
- `docs/development/evidence/WP02-26/`

任务 tests 内可加 csproj/子级 package 配置；不改 shared sln/root props/生产 csproj。Contracts 保持无项目/包依赖，Testing 继续 IsPackable=false，生产项目不得引用它。现有 Cp6SloEvidenceDocument/Evaluator、observability/slo-evidence/v1 JSON、WP02-25 ObservabilityV1、Messaging、EF、Main native 源码只读。台账原 evaluator 互斥锁保留，避免别人并改；如果实现必须改旧 wire 或公共包装，由 COORD 单独扩窗口，不能默改兼容定义。

实施应交付：

1. 在原 Evaluate 前加 SloQualificationV1，校验实际不可变 query/source/definition bytes 和 hash、真实窗口与独立总体证明；仅 hash 字符串合格式不得合格。来源只准受控本地输入，不任意 URL 回源。
2. ratio 按精确 K/N 与单位重算；percentile 固定 method/weights/units，不能平均 p95；gauge 明确采样/时点/缺口。缺总体、零样本、溢出/丢弃、未绑定混版、未核排除→Indeterminate。不替业务 Owner 发明阈值。
3. 窗口封存、修订新 evidenceId/version/supersedes，保旧失败/旧内容。测试和注入永远 productionSloClaimed=false；WP02-25 Incomplete 不能被本 API 靠自报覆盖升级 Complete。
4. case 独立 clock/random、handler 内外位置、有限脚本和 inner 次数、内存终端、防网络 fallback；取消/耗尽/cleanup 失败不能算 Pass。
5. case 过滤和有界 capture count/time/bytes，保 links、扇出/retry/receive/apply 分轴 DAG；禁止跨 case join、删中间 span 伪链、把本地 sequence 当 DB commit 序。
6. 精确类型捕获/断言用 BigInteger coefficient/scale/ratio，区分原词法与数值；不得通过 double、System.Decimal 或 epsilon 验大金额/数量。消费 S01-03/04 的固定来源/向量，不在 Platform 另造财务合同权威。若测试链接 Main 原源码须 pin/hash 核对且仅 tests；可用带来源向量和泛型精确断言，不能把向量成功写成 Main Owner 采用。

实际验收必须有：

- 相同源 bytes、definition、window 重算一致；query/hash/单位/排除/分母漂移、迟到原地改写、缺总体、零样本等反例。
- >2^53、超 System.Decimal、FIN-CREDIT38/8、FIN-INV18/12、signed zero/trailing zero 和非有限小数分数的精确案例；不缩小原合法范围。
- 扇出、多次 retry、Dapr 中间 span、收件/应用分轴及错边/丢边/跨 case 反例；overflow、Dispose 与清理失败影响完整性。
- 明确脚本调用与 inner 调用数，Success/Delay 真达指定内存 terminal；脚本穷尽不得 fallback。
- 新 PG55429 的实际并发事务、原 operation/message key 与精确存储值，以及仅自己 cluster 的受控 crash/restart。真实数据库观测与应用 ACK suppression 分开；注入故障不得冒原生 SQLSTATE/自然生产故障。不得只用 fake DbContext 填 PG 正向验收。
- 原32 OBS+24 TEST AC 映射保留；库、HostIntegration、RuntimeQualification、生产采用分层。生产 SLO 的独立来源/批准/保留/真实总体不在本轮。

## 只读资源核对与执行许可边界

探测时间：2026-10-14T05:14:56.8647258-04:00。端口19092、13500、15001、18012、18013、55429、55430均无 listener；这是当时观察，worker 开始前重查，不能当持久锁。

Docker daemon 可读，以下镜像已缓存 linux/amd64：

| 镜像 | 实际本地内容 digest |
|---|---|
| apache/kafka:4.3.1 | sha256:77e3df9054047a88b520d0cc46e16696d3b22022e1d580aeccd2632df6532837 |
| daprio/daprd:1.18.2 | sha256:8e94ba37d6bc95875e88545ce7d8ff781354f29080db02706d57446285fcc4a5 |
| mcr.microsoft.com/dotnet/sdk:8.0 | sha256:bb32ba3ba3ea36e38572d9d8db76fa15f7cbf722f3f886e06bca6d528bd4fba8 |

`D:/CP6-local-test` ACL owner 为 `LAPTOP-3QQ44FJS\\tt`，WP02-12/26 子根均不存在，没有创建 owner、密码或数据库。PG18.6 已安装。显式 `C:/Users/tt/.dotnet/dotnet.exe --version` 为8.0.424；PATH dotnet 只列 SDK10，不改仓库 global.json迁就它。Dapr.Client1.18.5、Npgsql8.0.8/provider8.0.11 本地缓存存在，完整新项目离线 restore 尚未执行。

COORD 已接受以下资源提案；worker 在正式领取后可自行创建、运行任务专属资源，不需重复询问用户：

- C：`D:/CP6-local-test/WP02-12/<owner32>/`，marker `CP6-WP02-12:<owner32>`；Docker project `cp6-wp0212-<owner12>`，每容器/network/volume 标记完整 owner32＋task ID。只映射127.0.0.1:19092/13500/15001/18012/18013，优先内部网络。需要持久化断言时新 PG127.0.0.1:55430，数据库 `cp6_wp0212_<UTCyyyyMMdd>_<owner8>`，私有 settings env `CP6_WP0212_SETTINGS`。
- D：`D:/CP6-local-test/WP02-26/<owner32>/`，marker `CP6-WP02-26:<owner32>`；新 PG127.0.0.1:55429，数据库 `cp6_wp0226_<UTCyyyyMMdd>_<owner8>`，私有 settings env `CP6_WP0226_SETTINGS`。
- 固定缓存 image digest、pull_policy never，不 pull、不外部付费网络。旧 p05 Dockerfile 会请求未确认缓存的 aspnet image 和常规网络 restore，不直接照跑；任务 fixture 可用缓存 SDK 镜像承载离线构建产物。缺依赖即资源门不满足，先报告，不转远程 Actions。
- PG 每次 setup/test/crash/restart/stop 前核完整 owner token、canonical contained path、endpoint、cluster system identifier、database OID/name/owner、session_user及无意外 session。setup/admin 与最小 DML runtime 身份分开；runtime 为 NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOINHERIT/NOBYPASSRLS，凭据私存、不打印、不入 Git。
- crash 只针对已确认自己 cluster，保受控故障标签与日志；正常结束 smart-stop。数据、失败、日志保留，不删除来修绿，不 kill 未知 session。
- 当前已有 cp6-demo-kafka、cp6-kafka、cp6-db 等活跃容器，完全不触碰。也不重启/清理旧 WP02-07/08 集群，不碰5432。停止仅匹配自己完整 owner/ID 的容器/集群；禁止全局 prune、批量 kill、删除既存容器或既存数据。

若真实 metadata、认证边界、离线依赖或 PG 所有权任一尚不满足，相关执行用例保持 NOT_RUN，允许继续无依赖库代码/私有用例准备，但不能宣称真实 broker/PG 正向验收通过。若最终确实无法实现本地认证或运行能力，应交具体缺口而非总括“fixture 已够”。

## 并行与收尾

C/D 无源码或任务 tests/docs 重叠。C 仅 Messaging 与独立 EF/TransportV1；D 仅 Contracts/SloQualificationV1 与 Testing。共享依赖、项目、schema/packaging、Main台账/生成页/四份状态统一由 COORD 管理，不允许为并行复制权威定义。

各任务从执行时最新已确认 Platform main 建独立分支/worktree；适用验证集中一次，失败后只定向复查，保真实失败和输入/产物身份。C 不自动使用尚未交付的 D helper。合并仍按任务串行，push/PR 前核候选和 protected main workflow，禁止 Actions 自动/手动启动。仅在各任务本地门、审查和远端 main 包含性闭环后登记 DONE；普通库 DONE 不升级原业务 AC。

无新增用户决策要求。准备、资源创建和本地验收已有清晰可执行范围；生产部署、真实业务迁移、原合同改变、付费外网或生产 SLO 另属独立任务，本轮不隐含授权。

