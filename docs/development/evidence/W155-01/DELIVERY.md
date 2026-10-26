# W155-01 原单位时间定义与实际资源工时读取

本聊天COORD单人，Main基线9f40bb8d9d003e0fa50c073a0dc495c1d397e288，固定DEV-1/DEV-2。[计划与范围](../../dispatch/20261026-wave155/README.md)。预计45–70分钟，Main START至本登记23.08分钟；最终finish/proof计时，不叠加Handoff。原七天目标ACTIVE。

## 实际交付

Main.Erp.UomTimeBasis.v1沿用实际UomProfile原UUID、Tenant和原Owner query/edit及当前对象范围。明确登记一个BASE单位代表SECOND/MINUTE/HOUR、Enabled和依据；精确BaseRef含Owner/UUID/version/digest，不凭SEC/HOUR编码或量纲字符串赋予物理含义。原单位主记录、比例和精度保持不变，不造第二套UOM主账。原单位版本改变或停用、时间定义撤回会使当前采用失效。

PostgreSQL前向表保存不可变原件JSON/SHA256、单调head、原BASE版本外键和头/原件延迟约束；禁止历史更新/删除/截断。无猜测种子、业务数据改写或角色自动授权。实际事务锁覆盖UOM原源和时间定义；写入前/提交前检查原对象范围，当前source查询authority也受最终门约束。准确CAS、无变化不升版、原键同体重放/异体冲突、审计与原结果共事务；UNKNOWN查询/关闭且关闭键禁止晚写，历史和原结果持续要求当前读取权限。

ResourceTimeOwnerV1在真实Machine/WorkCenter查询事务读取准确ResourceSourceRef、实际原字段、目标单位及其当前BASE/时间定义。仅standardCycleSeconds、plannedRunMinutesPerDay、dailyCapacityHours三个原字段；量值不由客户端提供。BigInteger有理数运算服从实际目标比例、小数位和包装倍数，不能精确表示则拒绝，保留大数/微小数精度；不舍入或默认系数。null/无定义/失效来源显式UNKNOWN，过期精确Ref拒绝。返回原值、准确来源/目标/定义Ref，CURRENT_TRANSACTION_ONLY及NOT_ATTESTED；可选ExpectedBasis用于冻结采用校验。原480只是原字段值，不代表批准产能。后续正式消费仍需冻结Ref和自身事务，不能把本次查询当作持续有效许可。

/erp/time-unit-bases从单位配置进入，使用原query门；支持明确选择时间含义、依据/启停、版本CAS、历史和最小原键恢复。设备/工作中心来源页面提供实际工时换算，只读原字段；结果显示精确值、三个版本与未知原因，禁止在查询中切换选择造成结果混淆。私有内容在身份变化/当前401/403时清理；恢复只保留命名空间下operationId/kind/objectId，Web Locks协调，UNKNOWN不会重发。

## 验证与集中审查

本地40个不同PG18/实际HTTP、43个UI/IAM通过，无跳过；vue-tsc/Vite通过。证据D:/CP6-local-test/wave155/foundation/final-validation.json绑定实际最终源码/测试blob。包含原件/CAS/noop/历史/撤回、两种资源及原480/大数/微小数/null/非精确拒绝、原配置或资源变化、并发只写一后继、不可变历史、跨租户/当前部门和撤权、错误上下文/无锁拒绝、提交中断原件/head/原操作回滚、真实HTTP no-store和必填启用标志。

最终审查补充显式edit对象范围及提交前检查，11项时间定义测试重新通过；29项未变UomProfile/ResourceSource结果复用final-owner真实时间。16项改动页面检查重新通过；27项未变单位/IAM/router复用ui-first，不重复计数。前端覆盖明确选择/准确Ref、最小恢复/UNKNOWN不重发、错误回执/无WebLocks拒绝、历史不替换草稿、身份切换迟到响应丢弃、403清理、实际资源换算/未知原因。

原失败保留：未实现入口404；测试辅助函数Times遮蔽共享partial中的Moq.Times导致编译失败，改名TimeBases后通过。一次作者SPEC/QUALITY集中审查全diff及上述定向修正，无未解决实质阻断。无Actions、Docker、部署或既有环境迁移；未执行浏览器E2E，不把隔离本地夹具称为真实企业验收。

## 剩余范围与收尾

只交付实际时间单位含义和资源原工时读取，完整WP08-16/WP10-47不完成；正式资源能力批准、Site/Assignment时间计划、完整RES-WRITE-1、资格/Claim/Start及ROU/BOM/WO/SCHED仍需继续。Continue original resource/capability/assignment and material/tool prerequisites for routing/BOM and WO/SCHED; preserve every original D2/D5/D6/D7 exit and real external prerequisites.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/131，source 66fce7fc0690c749d3c04d1c2417c4b9aa899c3d，目标 51f663b568fe13d4f9e83fa7a1e73579e6c5d02d；准确分支删除、DEV-2官方finish释放。Main保留真实收尾前快照，正常PR/精确远端采用/准确分支删除/finish由最终proof确认。固定目录与缓存保留。

W154仅归档一次并关闭WP08-16.wave154_resource_sources；本轮只新增WP08-16/WP10-47.wave155_time_basis限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED，125完整目标0、247运行NOT_RUN、41历史静态不变。仅D1/D4整阶段完成，2/7=28.6%为阶段比例，不能作为工作量完成百分比。
