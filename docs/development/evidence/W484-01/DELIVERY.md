# W484-01 · 首条询盘保留原图与原验收采用

状态：本任务已正常远端采用并完成生命周期。继续用户已接受的 [12h 执行](../../planning/20261111-codex-12h-business-chain.md)，原生上一读数 18,218 秒（5h03m38s），8h 检查点尚未到。COORD 单对话串行，无子代理。官方 status/start 已完成，复用 Main DEV-1 / CRM DEV-2；分支均 `codex/w484-01`，基线 Main `0a30d55436a5169174da95e905a8163abaf51b86`、CRM `8edb41fee2a300a5177eb142ced397253aeeeb07`，集成席位空闲。

## 前项及准入

[W483](../W483-01/DELIVERY.md) 已正常采用 Main #951 / CRM #170，Issue #169 在远端核对后关闭；两仓准确任务分支清理、工作区干净、官方 finish 和席位释放均已核销，主目录安全快进。Main 主目录原 14,932 个未跟踪资料路径保留，CRM 主目录干净。没有 Actions 或部署；此前生成缓存完整可逆保留于私有目录，无删除。

本窗口关联原 WP03-04 / CRM-INQ-01，沿已交付当前人工/独立服务权限、原生 PostgreSQL 共同事务、原回执、真实导出和保留入口。Main 仅协调/原验收台账及必要文档；CRM 独占当前原生 privacy/retention Owner、相关持久图验证与必要修正。不会将 Main 协调采用当 CRM 产品采用，也不重做未变的配置/审核及 W482/W483 有效证据。

## 真实缺口与执行顺序

1. 核原 AC-INQ-041/058 的四 disposition、行截止、Lead 日历月及全部 PII store。源码已确认 EF 对 Lead 的 Activities/Collaborators/History 及 correction 子图 AutoInclude；因此“只 Include History 必然漏子图”的初步疑问不成立，不据猜测修生产代码。
2. 区分已有实际原 60 分钟保留/hold/故障回滚/联合清除，现有仅内存的 Lead 子图未来投影，以及尚缺的持久完整原图/正式原义务证据。先用明确模拟的合法业务原图验证实际 PostgreSQL 行为，再对真实失败作最小修复。模拟历史输入只能在构造本身明确其范围；不回写已执行原件的更早截止、不改数据库时钟或缩短政策，不把组件投影冒充完整原生运行。
3. 按原60项 AC 与两项 R1 的全部规则、原 fixture 及当前源码逐项判断可复用证据。满足整条义务才采用 PASS，缺口保留明确 NOT_RUN/失败；设计源文件的历史 NOT_RUN 与实际运行记录分别保留。共同 NUM/FILE/PRIV 按冻结入口的真实调用判断，不添加不存在的公开字段，也不取消整卡尚未满足的正式前置。
4. 相关行为测试、一次任务级集中 SPEC/QUALITY 审查；仅实际修正部分追加回归。不因文档、SHA 或重复输入重跑全仓，不生成第二套逐文件回执/资格包装。
5. 两仓分别正常 PR/远端核对、停止自有进程、准确分支处置和官方 finish。原375、PG16/11和完整目标均按实际完整范围核销。

## 证据及退出条件

本任务唯一交付报告为本文件；原始命令、失败、结果及实际模拟范围保存在 `D:/CP6-local-test/CP6-12H-20261111/W484-01`。当前已实际完成下述存储/R1检查；集中审查、准确采用和收尾继续。没有本窗口既有环境迁移、发包、Actions 或部署权限变化。

- [x] 合法原图/真实调用与原 AC/R1 逐项对应，明确已有证据和实际未覆盖部分。
- [x] 与当前选定缺口相称的持久 PostgreSQL 行为验证；真实失败修复后复测，原失败保留。
- [x] 两条完整R1原义务准确记入原台账，未满足项不冒通过；相关文档/生成一致性。
- [x] 一次集中审查及必需验证通过；正常分仓采用、进程和分支/工作区/席位收尾。

## 实际发现与修复

CRM [Issue #171](https://github.com/GTX537/CP6.CRM/issues/171)：合法完整 Lead 子图擦除保存后，PostgreSQL 已生成新的 Activities/Collaborators `Version`，而原 `PrepareLeadErasure` 在保存前封存的完整图仍包含旧子行版本，重新读取后 `RequireLead` 报 `CRM_INQUIRY_GRAPH_INTEGRITY_INVALID`。`retention-persistence-diagnostic-06/results.trx` 的未改生产代码 RED 已确认：原图认证及实际当前 `NotDue` 均成功，擦除/保存后失配，诊断还确认已存凭据仅匹配先前子行版本。诊断只作用于测试内存，产品不尝试旧版本或替代哈希域。

修复仅抽取原保存阶段：同一既有 Owner 事务内先保存擦除，取得 PostgreSQL 返回的真实子行版本，再封存完整 Lead 并保存最终凭据；之后才沿原流程追加效果并提交。当前授权、行锁、hold、期限、原结果及最终提交边界仍由既有 Caller 负责；没有新资格层、DDL、权限或业务合同。封存最后一步失败时，前面已执行的 PII/子图/版本/历史变更随整个 PostgreSQL 事务回滚。

模拟范围必须区分：通过实际 H01/HTTP 创建根及原合法配置两日历月截止，合规模拟子图真实入库；当前真实数据库时钟下先证实 native retention 返回 `NotDue`。随后的擦除存储组件以同一个未修改截止作为明确未来投影参数，实际 PostgreSQL 保存、生成版本、回滚和重读认证；没有改数据库时钟、回写更早期限或实际等待两个月。它补齐持久存储风险证明，不能单独核销 AC-INQ-041/058 的四 disposition/完整月度原生验收。

| 本任务私有根证据 | 实际结果与适用范围 |
|---|---|
| `retention-persistence-red-01` | R1合法key长度/Unicode/CAS场景1 PASS；组件最初夹具缺Activity来源字段，23514，非产品RED。 |
| `retention-persistence-red-02`、`diagnostic-04/05` | 原Type/Outcome组合、xUnit断言写法编译和未按Owner历史序列读取的夹具/诊断失败均保留，不计业务通过。 |
| `retention-persistence-red-03`、`retention-persistence-diagnostic-06` | 后保存认证失败；最终06的合法、稳定原图诊断确认存凭据仅匹配旧子行版本，为产品RED依据。 |
| `retention-persistence-green-02` 有效子集 | 正常完整子图擦除、真实生成版本、重读认证1 PASS。另1项DDL故障注入被现有schema完整性检查提前拒绝，保留失败；没有关闭该防线。此前green-01固定夹具ID碰到真实全局PK，失败保留，改为全局唯一且有序的模拟ID。 |
| `retention-persistence-fault-03/results.trx` | 1 PASS：在最终保存边界明确注入I/O故障，真实PG第一次保存已发生后整事务回滚；PII、原截止、原版本/完整图与历史保持；解除故障后同操作成功，匿名化历史仅1条。无DDL变更。 |
| `r1-adoption-01` 有效子集 | 实际原生产LeadCreated事件经当前解码器：boolean扩展可用，number/string/array/null扩展拒绝，重复JSON键拒绝；键序不同语义相等且uniqueItems判为重复；原业务计数不变，1 PASS。另1项将历史55修订引用误当总数，观测实际257个引用均可解析，但数量断言失败，保留原失败。 |
| `r1-reference-02/results.trx` | 1 PASS：从未改原OpenAPI中逐一定位55个原错误指针，核当前生产有效Schema同位置均采用正确components指针，同时解析当前全部257个实际引用。原件作为测试资源，不改生产Schema/附件。 |
| `retention-regressions-01` 有效子集 | 14 PASS / 1 FAIL / 0 skipped，当前权限、原截止、原件/密钥、lease/hold竞争及持久失败/原图保护；唯一失败为旧独立worker夹具未映射必需Health V1路由，启动前拒绝。按正式宿主补Map，单独复测见后续结果。 |
| `retention-host-02/results.trx` | 1 PASS / 0 FAIL / 0 skipped：旧worker夹具按正式宿主Map映射，真实启动/篡改原图拒绝/持久失败/原PII保护/正常停止通过。其余14项输入未变化，直接复用。 |

## 两项 R1 完整运行义务采用

原定义仍引用 `08-acceptance-catalog.json` 中POS/NEG原文及原映射，不改原设计的历史NOT_RUN。以下全部原运行义务已逐项核对，且CRM源已正常进入其远端main，roadmap对应两条登记PASS；其余245条保持NOT_RUN。原60AC及原375项不据此批量提升。

| 原义务 | 实际采用与有效证据 |
|---|---|
| POS：55引用可解析、不同对象键序同值 | 当前实际生产有效Schema/原OpenAPI逐指针对照 `r1-reference-02`；实际生产事件及生产比较器/uniqueItems在 `r1-adoption-01` 有效成功。 |
| POS：终态公开字段null | S01-08原90/99解码/页面类型有效证据复用；[W482](../W482-01/DELIVERY.md)实际HTTP五分钟99与[W483](../W483-01/DELIVERY.md)实际HTTPS刷新99，公开expiry/artifact/proof均null，内部原截止保留。 |
| POS：合法key下201/4001字符422 | `Native_R1_legal_key_length_rejections_preserve_attempt_and_accept_exact_unicode_boundaries` 真实HTTP/PG：两项422、业务/配额0写、服务调用0；同原Attempt/key的合法200/4000 Unicode随后201且原文无损。 |
| NEG：扩展值仍由decoder检查、重复JSON键拒绝 | `r1-adoption-01` 从实际已持久化原事件取原文，使用当前生产decoder/严格JSON执行，拒绝后无业务计数变化；没有只用TS unknown类型作资格。 |
| NEG：过期proof/错误ETag不下载或写入 | W482真实五分钟旧proof410、一次性消费/错绑及当前权限；W483当前HTTPS终态；本轮真实privacy request错误ETag412，Submission/原版本/Job不变。GET下载仍按原session/独立proof合同，不新增If-Match要求。 |
| 原定写集/当前资格 | W482实际Main TLS v2、当前人工/独立服务、原202/原结果/共同PG事务与末段回滚、真实五route消费者，W483原Job恢复/身份边界，及本轮实际422/201/412零副作用控制组合复用；不冒称NUM/FILE未来消费、全卡或生产部署通过。 |

实际 .NET入口为 `run-tests.ps1` 中同一原PG测试项目、显式自有settings及各条过滤；资源、源码与命令完整保留。未变C#/TS合同库、原60分钟实际NeedsReview保留/hold/删除故障联合回滚按W482有效范围直接复用，不因为新增提交或本组件未来投影重复一小时实测，也不将复用改称新执行。

## 集中审查与剩余边界

一次任务级SPEC/QUALITY集中审查覆盖两仓相对已确认main的完整修改：同一事务的两阶段保存/实际生成版本、持锁和最终当前授权归属、末段失败整体回滚、原完整图/截止/字段事实/历史与原结果、数据不复活、R1实际事件及HTTP、测试资源与正式宿主映射、准确原义务/复用/失败和台账。测试项目仅增加未改原OpenAPI资源，没有改依赖或生产配置；相关项目实际编译/行为检查通过，无未解决必需门禁失败。普通文档只检查diff/引用/台账生成，不运行全仓产品构建；未启动额外审查代理。

原AC-INQ-041/058及完整WP03-04、原60AC整体、正式PG16/11仍保留其完整余项；本窗口采用的是两条明确R1运行义务。无生产部署、既有数据库安装/迁移、Actions、保护变更或历史数据重写。

## 正常采用与生命周期

CRM [PR #172](https://github.com/GTX537/CP6.CRM/pull/172) 已将源 `94b21d24225fc713766910c84cf09099a247f9cb` 正常合入远端 main `a992004e984252d94049e9bf17b458c2427118a6`。已fetch核对祖先关系及完整源码树均为 `476a0e1bf4c1e4c6eb26180808553e751bfd2735`；按未变输入复用成功，不因合并SHA重跑。[Issue #171](https://github.com/GTX537/CP6.CRM/issues/171) 在远端核对后补记准确诊断、模拟范围、验证和来源并关闭。官方finish成功，准确本地/远端任务分支删除；DEV-2干净、detached供复用，席位已释放，证明为本任务私有 `crm-finish.json`。

自有PG经系统身份、目录、marker和无其他客户端核验后smart停止，数据与失败日志保留；本轮.NET/HTTP/worker进程已退出。CRM本任务Actions列表为空。两仓workflow/依赖内容未变化，普通入口仅手动，未建版本tag；没有Actions取消、触发/保护变更或部署。源修复不含生产DDL；测试PG的临时DDL故障注入已按准确自有引用移除，最终回滚验证使用保存边界I/O注入。

Main [PR #952](https://github.com/GTX537/CP6/pull/952) 已将协调源 `f485b1e185eaa6f0289a4f0da256e4fb027b6404` 正常合入远端main `4fa6d2713ddbe0bf2783dca589b425c6be336ccb`，已fetch核对祖先及完整源码树 `179ba0c5a11581e70a9dfae687be0f2ada629101` 一致。官方finish成功，准确本地/远端任务分支删除，DEV-1干净detached供复用，席位已释放；两仓最终证明为本任务私有 `main-finish.json` / `crm-finish.json`。主目录安全快进，Main原14,932个未跟踪资料路径完整保留。12h目标继续ACTIVE。
