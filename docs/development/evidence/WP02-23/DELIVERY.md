# WP02-23 INT-OPS Main 原结果目录与受控重放

WP02-23 办理 DONE。INT-OPS-01 原 AC **14/35**、必需运行修订 **0/2**，目标开放，CRM 消费端实际采用和其余出口按 WP02-24 等原职责推进。[Main PR](https://github.com/GTX537/CP6/pull/1290) · [原卡](../../task-plans/WP02.md#wp02-23)

派工原提交 1786134eaf44cfb3e4889ab359ad1d7bdae075f2，完整路径 handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T140456Z-exec-wp02-14b-wp02-23/MESSAGE.md。执行人 EXEC-WP02-14B 从实际 Main 9614cb89eaa65bf8ced38052beaf58274d196131 领取 DEV-1 及 codex/wp02-23。WP02-10、WP02-22 完整 DONE，采用提交 ab3c7da128d1f33fabb34b92f5c1ea9ffd532116、a015ed8d267ecbcdb44547272a54710a999f0d9b 均已核实为祖先。受验产品 388f3af15c841b59ebc6132f8a8858f2801b1ae9，树 74fe766c2220556fac61434f7d9c515db7ceba0c。合并后实际 Main 和席位回收以原链 DELIVERY 回执为准。[派工原件](https://github.com/GTX537/CP6.AI-Handoff/blob/1786134eaf44cfb3e4889ab359ad1d7bdae075f2/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261120T140456Z-exec-wp02-14b-wp02-23/MESSAGE.md) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/8f22a4cbbebd9d03b1e203d2d53c11e0b7a6bfb4/handoffs/CP6-DEV-20261012/tasks/WP02-23/messages/20261120T141050Z-exec-wp02-14b-start/MESSAGE.md)

## 行为及实际验证

| 验证面 | 实际运行 | 模拟边界与结果 |
| --- | --- | --- |
| 三原生动作 | Main Kestrel 租户和当前 IAM 读取，C03 Inbox、Platform Outbox、原订单 bridge；真实 PostgreSQL 队列和原日志 | 模拟业务原件、人员和目标 Owner。请求、结果使用原消息和 UTF-8 摘要，bridge 用原 orderKey 身份摘要且载体为空。错域、codec、原值、版本、活 lease 和路由变化均拒绝。 |
| 原键与撤权 | 真实 Main POST、GET、当前权限表、固定原回执及后续 HTTP 宿主 | 模拟提交后的回包丢失、用户、资格时钟和 JWT。202 与读回 200 使用同键；资格过期三分钟、队列升版、撤新写及重建宿主后取回同一回执。异义拒绝，撤读拒绝，禁止借资格票据换 ID。重试的新增调度、调度审计、业务效果均为 0。 |
| 事务、未知和 worker | 隔离 PostgreSQL 的同连接队列、原审计和原操作日志，真实 IAM 根竞争与实体锁，实际命令处理器和桥 worker | 模拟审计触发器错误、Owner 资格及其事务锁、桥下游原步骤。锁超时为带同 ID 的 UNKNOWN。审计失败回滚 queue、死信和所有 audit。两个原键请求只受理一次；两个桥 worker 只提交一份受控夹具步骤。撤权后另一个桥和请求零新业务效果。 |
| 已提交结果重投 | 实际旧 C03 DTO、PostgreSQL Outbox 及 Platform claim/publish，已提交 BP 原事件由真实 C03 生成 | 模拟外部发布者、运输故障、订单和金额输入。结果原 EventId、精确原字节、金额 9007199254740.12345678 和版本 7 保持，原订单、请求和回执不重建。旧结果路由同键仍返回原 202 结构。 |
| 多来源目录与隐私 | Main 列表、详情、签名游标、真实 Main 源查询、实际字段遮罩和宿主日志 | 两个外部来源使用模拟适配。相同 UTC 时刻和 nativeId 能分开排序，固定 sourceSet 可重复翻页。原缺源恢复仅加入新查询，所选源丢失报 SOURCE_SET_UNAVAILABLE，唯一来源不可用为 503。撤 Owner 读取和字段授权后不泄露原身份、摘要、异常或 PII。 |
| Web 和前向配置 | 生产 Vue、真实 Chromium、Main HTTP 和隔离 PostgreSQL。沿用权限管理中的新查询、证据、审计、纯投递及重放操作 | 浏览器身份、导航和非本卡接口模拟。详情焦点与安全摘要、关闭详情、刷新按原 ID 回查、身份切换清理均实际执行。权限迁移登记动作，租户 IAM 自行授予。 |

标准输出和行数见本卡工件 proof.json。隔离验证入口为 本卡 test.ps1 与 test-c03-adjacent.ps1 使用 dotnet test 定向运行，生产 Vue 用 npm run type-check 及 build-only。实际 TRX 与 Chromium JSON 已存入 D:/CP6-test-artifacts/WP02-23/current-20261120。主验收 1/1（含真实 Chromium），相邻 ERP HTTP 18/18，WP04-03 相邻原 C03/I02 1/1，相邻 Vue 25/25。Vue 类型检查和生产构建保存同目录，采用后同树冒烟在正式回收前执行。

旧原生 ERP 关系套件的 49 项均在旧伙伴夹具进场时失败，开工树同 49 项、同错误复现（AUTHORITY_FENCE_UNAVAILABLE）。两次都在调用业务伙伴旧建档构造器时止于现行授权根，实际错误关联和原测试名一致。该组保留失败记录。受影响的 Main 入口、当前授权、队列和真实 C03/I02 由本轮新版夹具实际覆盖。

已实际验证的真实 Main 适配范围是 C03 三种队列。外部 Owner 资格只在隔离运行中由明确的持门夹具提供。当前真实路由缺少可参与同一事务的安全门时，原请求和桥新效果保留 BLOCKED_WAITING_AUTHORITY，可按原键读取结果，实际业务 Owner 适配由各自职责卡注册。两个 S6 原 checkpoint 修订继续待实际外部事务闭环证据。CRM 界面与消费者接入参见 WP02-24。

## 台账与裁决

按 #362 查重后把 WP04-03 的 **372.252 秒**单次结转，4043.652 秒至 **4415.904 秒**，本次计划累计起点 **112004.883 秒**。WP02-23 截至核销快照 2026-11-20T15:06:02.514840+00:00 为 3312.209 秒，目标核对另列 0.177 秒，累计 115317.269 秒。按 #363 给 ERP-BP-01 的 WP14-08 增列 AC063，保留两条历史 QUESTION 和裁决引用，当前 24 项待裁决。ERP-BP-01 仍为原 79/124、必需 3/10。[#362](https://github.com/GTX537/CP6.AI-Handoff/blob/df21a2711dd937b48827d93e1cc6521f7bd5cd6c/handoffs/CP6-DEV-20261012/tasks/WP04-03/replies/20261120T135813Z-coord-acceptance-ack/MESSAGE.md) · [#363](https://github.com/GTX537/CP6.AI-Handoff/blob/45f973226c7ca21b622504ff41e13009f454628c/handoffs/CP6-DEV-20261012/tasks/WP04-03/replies/20261120T140229Z-coord-gap-decision/MESSAGE.md) · [唯一台账](../../roadmap.json)

PLT-DEL-01 已登记由本卡承接 DEL05-A2，原断言要求三动作、nativeType、摘要域精确以及 bridge 保留空原载体。主专项逐项执行后核销该项，原缺口与既有审查历史保留，目标原 AC 为 1/60，继续开放。另三条原缺口按既有职责保留。

## 原 AC 逐项核对

以原目录全文断言核账，局部证据对应的剩余条件见各行。[原目录](../../assessments/20261011/catalogs/foundation.json)

| 原项 | 原断言 | 结果 | 真实执行或余出口 |
| --- | --- | --- | --- |
| AC-OP01-01 | 三allowedAction/nativeType/digest(domain/codec/value)准确：request/result为真实raw envelope，bridge仅原身份摘要且message/envelope=null；不伪造业务input摘要 | PASS | 三个原生动作逐个核对原标识和 typed digest。请求及结果逐字节复算 raw SHA-256，桥仅身份摘要且 message/envelope 均为空。 |
| AC-OP01-02 | 查询按当前tenant及Owner scope，未授权对象不泄露存在性或payload | PASS | 当前租户、受信 JWT 用户、独立 query 授权和源行范围实际验证。异租户及无读权拒绝，外部模拟 Owner 撤可见后无记录返回。 |
| AC-OP01-03 | 跨Owner分页使用含owner/nativeType/nativeId/failureRef的唯一全序，cursor固定sourceSet；缺源恢复新查询，中途丢源明确未就绪，不拼漏项旧页 | PASS | 同一 UTC 时刻和同一 nativeId 跨三来源，用固定 sourceSet 及签名 cursor 重复翻页。恢复源仅新查询加入，已选源丢失要求重新查。 |
| AC-OP01-04 | 部分Owner不可用明确partial；全部不可用503；不以空列表冒成功 | PASS | 一源离线为 partial，保留缺源名。筛到唯一离线源返回 503，无正常空页。 |
| AC-OP01-05 | 原始异常、PII和secret不进入列表、复制诊断、通用日志或指标标签 | PASS | Inbox 与模拟源注入敏感异常。详情、列表、安全码、审计和实际宿主日志均未返回该值。诊断仅给受控摘要和允许的安全码。 |
| AC-OP01-06 | COMPENSATED历史标记不计业务已恢复；旧时间未明不伪造UTC | NOT_RUN | 桥原失败时间缺份时保留历史时间未知。旧 WMS COMPENSATED 原记录及状态展示尚待独立端到端回核。 |
| AC-OP01-07 | 页面Back/刷新/切tenant/迟到响应不保留旧组织数据，不因查看自动重排 | NOT_RUN | Main 生产 Vue 实测打开、关闭、刷新、回查及身份切换清理。各消费者的 Back、迟到响应和组织切换全集尚未逐路核销。 |
| AC-OP02-01 | 同一可预期业务拒绝持久为terminal，不被worker技术retry重新执行业务 | NOT_RUN | 当前授权层保持业务终态和技术重试独立。逐 Owner 终态拒绝及其 worker 不重执仍需实际链验证。 |
| AC-OP02-02 | 技术回滚后用新事务记录retry；失败上下文不二次提交旧跟踪实体 | NOT_RUN | 本卡新调度的实际审计故障已全组回滚。原业务上下文损坏后的逐 Owner 技术重试另按原证据入口核对。 |
| AC-OP02-03 | 提交超时仅UNKNOWN，先查原Owner回执；HTTP503不自动证明未执行 | NOT_RUN | 本卡运维调度提交不确定按原键。业务 Owner 的真实提交歧义及外端时序须逐条核对。 |
| AC-OP02-04 | 重复业务键同输入复用原结果，异输入冲突；messageId变化不创建第二业务效果 | NOT_RUN | 本卡同原运维键/异义已实跑。独立业务键、多个运输消息 ID 及各消费端闭环仍待核对。 |
| AC-OP02-05 | SKIPPED/bool=false逐route经Owner映射；未映射保持未分类且重放关闭 | NOT_RUN | 缺可证安全门的源保持阻塞。各下游 legacy SKIPPED/bool=false 的真实映射与结果由相应 Owner 核对。 |
| AC-OP02-06 | 已被新版取代旧请求不可恢复旧业务版本，UI同时展示原版本与现版本 | NOT_RUN | Main DTO 保留原版本、有 superseded 分支。消费端与实际目标新旧版本同屏证明未齐全。 |
| AC-OP02-07 | transport success/Inbox processed与Owner applied分列，任一技术状态不冒充业务成功 | NOT_RUN | 目录分投递状态、Owner 结果、证据状态。尚缺全链真实 Inbox/Dapr/消费者结果分离证据。 |
| AC-OP03-01 | 同operation先查原tuple再分支；首次响应丢、queue已改版、eligibility过期或已无新写权但有读权，仍只读回原Receipt，零新调度；异义冲突 | PASS | 响应实际提交后丢失、原 nativeVersion 改变、eligibility 过期三分钟及撤投递／重放权后，同键同输入恢复原回执，无调度、调度审计及业务新增。同键异义 409。 |
| AC-OP03-02 | 按三动作分支核原nativeVersion和typed摘要；bridge不要求不存在的payload/schema；域/codec/action错配稳定拒绝且无队列/业务变更 | PASS | 三动作各取本域版本和摘要。错域、错 codec、错摘要、错原生版本及跨 Owner 伪造拒绝，无队列或业务新增。 |
| AC-OP03-03 | 新调度/新效果只经§3.6已采用共同门；失权DENIED、分库缺门UNAVAILABLE，历史原结果读/可信事实接收/纯技术重投按各自门，不借原grant续权 | NOT_RUN | Main 实际 IAM 事务根、当前动作与读权独立。新效果的外部 Owner gate 使用锁定夹具，未登记真实跨库新效果保持 BLOCKED，原修订的事实接收与全链采用另验。 |
| AC-OP03-04 | 原已APPLIED业务不重做；原结果delivery或原bridge尚未完成步骤按各自原效果范围/门处理，不能把订单APPLIED当下游步骤成功；对应终态拒绝/取代不改旧结果 | NOT_RUN | 实测已提交原结果只投递，桥从原订单绑定进入各自门并保持原事实。原终态拒绝、已取代以及实际下游桥完整合集尚未核销。 |
| AC-OP03-05 | lease/route epoch变化、并发worker或新状态导致资格重判，零双消费者双写 | PASS | 活 lease、路由 epoch 和当前租户授权复核。两调度者竞争同一键只受理一次，两桥 worker 在 PG 下只提交一份模拟步骤。 |
| AC-OP03-06 | 新调度audit+queue同事务；响应丢/提交进行中/锁超时返回同operation的Processing/Unknown/RetryAfter，先回查，不以查无/旧版本失配生成新ID | PASS | 真实 IAM 根锁超时返回 503 UNKNOWN、同 ID、Retry-After。审计后段错误整笔回滚，同 ID 调度后回包丢失，读取原 ID 恢复，换 ID 重用票据拒绝。 |
| AC-OP03-07 | 202只表示已安排；关闭dialog不取消已接纳任务；同ID可重开查询，过期eligibility或原新写权变化不被误作已有操作消失 | PASS | 实际 Main 202 与同站 Location 只承诺安排。生产 Vue 详情关闭及刷新后，按同 ID 回查原回执。撤写和原资格过期时原操作仍可查询。 |
| AC-OP04-01 | 原operation同义回查零新调度/调度audit；新接纳强制audit与调度同commit，异义不重写原audit | PASS | 原生队列转换、原审计与原操作日志同事务。注入失败后队列、死信、原回执均保原状，重复和异义不增删或改写原审计。 |
| AC-OP04-02 | 原actor/operator/worker分列，新增效果引用真实Owner最终门fence/epoch/事务证据，缺能力不能补造ALLOWED_HELD | NOT_RUN | 真实操作者、权限版本、数据库根与模拟 Owner 持门记录可查。逐个原业务作者、各真实 worker 决定和各 Owner 安全门由实际参加者承担。 |
| AC-OP04-03 | allowedAction/nativeType及typed digest原样入audit；raw/data/canonical/bridge identity四域不互换，不存在载体保持null | PASS | 真实审计逐一核对三类 action、nativeType、typed digest 和原版本。raw 与桥身份域独立，桥载体为空。 |
| AC-OP04-04 | 错误/日志/导出不带secret和未授权PII；敏感证据访问受单独scope | NOT_RUN | 安全码、当前字段遮罩和实际日志本地实跑。独立敏感证据访问及导出的全链覆盖尚待核查。 |
| AC-OP04-05 | 历史缺失数据保持LEGACY_UNKNOWN，不伪造auth版本、真实撤权时间或Owner提交时间 | NOT_RUN | 旧 replay 审计可显式展示 LEGACY_UNKNOWN。所有历史记录的撤权与原提交时间没有完整前向样本。 |
| AC-OP04-06 | UNKNOWN/争议/引用证据不被通用技术retention自动清理；Owner政策未证则不启用清理 | NOT_RUN | 新的原操作记录使用现有永久日志。真实跨源保留、到期争议与 legal hold 的运行证据需按原规则联验。 |
| AC-OP04-07 | 审计只能追加更正，原history保持可追溯；平台trace不冒充Owner业务回执 | NOT_RUN | 新日志继承数据库只追加规则，现有 audit 保留。跨 Owner 更正、完整历史及 trace 与业务证据合并未全量实测。 |
| AC-OP05-01 | 提交UNKNOWN只查原Owner回执；查无/已过期/副本延迟均不等于NOT_APPLIED_PROVEN | NOT_RUN | Main 查无返回 NOT_OBSERVED，缺实际 Owner 证明保持阻塞。原 Owner commit unknown、证据到期与副本延迟的组合场景未齐。 |
| AC-OP05-02 | Owner已APPLIED且delivery失败仅重发原结果，业务对象数/金额/版本不重复变化 | PASS | Main 原调度、PG 平台投递和模拟发布者实际运行，EventId、字节、金额 9007199254740.12345678 及原版本 7 保持，订单及原请求计数不变。 |
| AC-OP05-03 | 业务终态拒绝保持原结果；Inbox processed/Dapr success不显示业务成功 | NOT_RUN | Main 区分业务终态与投递能力。消费端拒绝原回执与 Dapr/Inbox 实际闭环仍待核对。 |
| AC-OP05-04 | 消费者同版异内容隔离、低版不回退、新版取代返回明确未采纳原因 | NOT_RUN | CRM 消费者同版冲突、低版、新版及应用终态须由真实消费入口完成。 |
| AC-OP05-05 | 原请求/结果/当前对象三套版本与raw/canonical域完整保留，对账不重算原输入身份 | NOT_RUN | 原生 receipt、原 request 和结果版保持分离，保留原 raw 摘要。当前目标版本及消费者的全部对账轴未齐。 |
| AC-OP05-06 | 历史结果返回按当前字段/PII权限投影；原不可变结果摘要和nativeRef不改 | PASS | 真实字段权设置 NativeEvidence 隐藏，原回执和 Owner 原结果当前投影均裁剪。受控读取时准确原摘要、nativeRef 与原始载荷一致，业务事实不改。 |
| AC-OP05-07 | 固定consumerSet/registryVersion及每项原完成谓词；证据齐全与业务义务完成分层，Received/Processed/Unknown不当Applied，原WMS/INT-COM独立义务不被Case Resolved覆盖 | NOT_RUN | 固定消费者集合、逐消费者原谓词以及独立义务全链仍待真实消费方接入。 |

## 必需运行修订逐项核对

[运行修订](../../../design-handbook/remediation/08-acceptance-catalog.json)

| 修订 | 输入 | 原预期 | 结果 | 实际范围和余出口 |
| --- | --- | --- | --- | --- |
| COM-S6-PRECHECKPOINT-FINALAUTH-POS | 重复外部事务且原checkpoint完备 | 当前读权限下返回原receipt | NOT_RUN | 同键 Main 运维回执已用当期读权恢复。该原修订要求实际外部事务及原 checkpoint 完备，仍需 WP02-24 的相应消费者及 WP14-02 原闭环。 |
| COM-S6-PRECHECKPOINT-FINALAUTH-NEG | 同txId不同摘要或最终授权无真实gate | DRIFT quarantine撤销allow；无gate BLOCKED | NOT_RUN | 无实际 gate 的原请求和桥有可核验的阻塞路径。同 txId 不同摘要后的真实 DRIFT quarantine 与撤销 allow 仍待同一原修订全链验证。 |
