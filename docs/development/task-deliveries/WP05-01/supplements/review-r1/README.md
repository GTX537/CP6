# WP05-01 已知义务覆盖补充

版本：`CP6-WP05-01-ACCEPTANCE-BASIS/1.1.0-proposed`。本版由原 [1.0.0 六份规范资产及清单](../../basis-manifest.json) 加本目录制品共同组成，准确字节见 [manifest.json](manifest.json)。旧版六文件和清单逐字节保留。本版仍为 **PROPOSED**，正式采纳记录为空；合并只交付设计，不改变 WP01-01 完成门。

[独立审查](../../evidence/coordination/review.md)发现原 221 条可恢复场景和 295 项正文定位没有完整展开所有已知义务。补充矩阵承接 18 项工作簿观察、15 项 CP22 消费条件及 10 项 R1 细则，共 43 项审查映射，准确字段、源 hash、定位和旧依据 ID 见 [补充 JSON](wp05-supplement-proposed.json)。每项必须同时读取 scenario、required_observation、owner_scope 和 source_refs；这些是可执行观察设计，尚无业务结果。

43 是补充映射数，包含复用与冲突处理，不是 43 条恢复的旧 AC，也不能与 221、295、8 条 R1、10 条细化向量相加宣传覆盖率。旧 ITER 30 / REL 48 仍为 NOT_INVENTORIED、UNRECOVERABLE、UNPROVABLE；所有业务运行均为 NOT_RUN。已知源条文冲突按原正文及具名后继解决，不扩大权限、不引入默认能力。R1-09 明确拒绝重复输入引用，不能静默去重改变非法输入的结果。

本版正式生效仍遵循原 [adoption.json](../../adoption.json) 及 README 第6节的追加式采纳要求，决定必须绑定本版 manifest hash、范围与历史不可比较限制；旧版批准不能自动覆盖本版。原稿内关于 S02-00 等待状态属于原准备时点：当前已合入的 PostgreSQL 设计是后续实施输入，不能冒充本版本采纳、建库或运行采用。目标完成门工具仍未改变。

## 本地复核

从仓库根目录执行（ARCHIVE_ROOT 指向实际只读归档根，OUTPUT 指向另一个证据文件）：

```text
python docs/development/task-deliveries/WP05-01/supplements/review-r1/verify-wp05-supplement.py --root . --archive-root ARCHIVE_ROOT --supplement docs/development/task-deliveries/WP05-01/supplements/review-r1/wp05-supplement-proposed.json --out OUTPUT
```

校验器使用原 sources.json 固定的 Git blob 或原归档字节，检查清单、49 个准确来源定位、旧 ID、43 行和未采纳/NOT_RUN 边界。它不执行原程序或 Office 公式，只写显式指定的输出。静态通过不能证明语义完整、真实 Owner 实现或运行验收；语义复核另见定向审查证据。

24 项正反检查脚本与已运行摘要一并保存，检查器代码未变，复用来源见 [协调记录](../../evidence/coordination/README.md)。本次观察措辞修改后重新运行资料校验；没有把此前检查器测试改称新执行。

## 逐项导航

| 补充 ID | 目标 | 观察义务 |
|---|---|---|
| WP05-01-REVIEW/WB-01 | PLM-ITER-01 | 仅稳定ID定位；当前逐对象授权；统一掩码不泄漏对象存在。 |
| WP05-01-REVIEW/WB-02 | PLM-ITER-01 | 只改变展示；服务端仍按当前阶段和权限拒绝且无新业务写入。 |
| WP05-01-REVIEW/WB-03 | PLM-ITER-01 | 按适用命令返回缺失428/不匹配412；原业务状态/回执写集不产生未经授权变化；合法当前ETag才继续。 |
| WP05-01-REVIEW/WB-04 | PLM-ITER-01 | 重试保原键，同体回原结果、异体409；不把认证秘密作为业务键或持久回执索引。 |
| WP05-01-REVIEW/WB-05 | PLM-ITER-01 | 拒绝latest；不可得显示Unknown并阻所依赖动作，不默认为无漂移。 |
| WP05-01-REVIEW/WB-06 | PLM-ITER-01 | 仅受控内部路由枚举；保原筛选游标；深链按当前授权解析，返回重查权限和真实状态；拒绝任意外部跳转。 |
| WP05-01-REVIEW/WB-07 | PLM-ITER-01 | 计划日期不带时区；事实UTC并本地时区展示；歧义拒绝；客户端不能覆盖服务器审计时间。 |
| WP05-01-REVIEW/WB-08 | PLM-ITER-01 | 先权限过滤再按updatedAt DESC,id DESC稳定排序；未知非0；50/200等须DP后继决定，不自动当已批SLO。 |
| WP05-01-REVIEW/WB-09 | PLM-ITER-01 | 按原工作簿已明确页面—ACT—PROC关系核对，拒绝只靠字段归属反推；这是已存工作簿定位，绝非旧30AC恢复。 |
| WP05-01-REVIEW/WB-10 | PLM-ITER-01 | 确认显示摘要/版本/原因输入；重复按钮禁用不代服务端幂等；失败保留输入和真实原回执链接，不假报外部完成。 |
| WP05-01-REVIEW/WB-11 | PLM-ITER-01 | 冻结内容不可PATCH；终态不可覆盖/删除；HELD禁止新增提交/评价/冻结；所有拒绝由后端执行，隐藏存在性。 |
| WP05-01-REVIEW/WB-12 | PLM-ITER-01 | 旧轮只读且不列无权成员；源Unknown/asOf保留，不泄漏裸下载地址；历史UTC+ID稳定顺序。 |
| WP05-01-REVIEW/WB-13 | PLM-ITER-01 | 文本不执行公式；下载重验权；EvidenceUnavailable显式；PLM-DOC文件版本不替换，ITER不直接上传成PASS。 |
| WP05-01-REVIEW/WB-14 | PLM-ITER-01 | 本册不批准批量导入；先来源/状态映射和缺证列表；不得伪造迭代或通过。 |
| WP05-01-REVIEW/WB-15 | PLM-ITER-01 | 角色职责分离；读取不附送编辑/恢复/原文件权限；服务身份核issuer/tenant/action且不能伪造用户决定；审计及失败日志不漏敏感对象。 |
| WP05-01-REVIEW/WB-16 | PLM-ITER-01 | 按原403/404掩码、409/412/422分类；无新增业务写入；已独立发生外部事实保留，查原请求；不换键逃避。 |
| WP05-01-REVIEW/WB-17 | PLM-ITER-01 | 按专用动作和原完整枚举；至多一当前选定；转移需受理责任期限；技术尝试独立；未知/建议参数仍留边界。 |
| WP05-01-REVIEW/WB-18 | PLM-ITER-01 | 按正文REQ023和处理细则核ECR/ECO；保留原AL149冲突，不能凭N/A删核验或批准导入。 |
| WP05-01-REVIEW/CP22-01 | PLM-REL-01 | 固定完整原Owner SourceDescriptor/digest/current body与namespace；仅Source search→真实Preparation→EngineeringCheck→Snapshot→CREATE原五步；不得编造前驱结果。 |
| WP05-01-REVIEW/CP22-02 | PLM-REL-01 | PREPARE恰四且preparationRef=null；CHECK/SNAPSHOT/CREATE各恰十一，同真实preparationId/revision；缺项多项或跨cut不得后继派发。 |
| WP05-01-REVIEW/CP22-03 | PLM-REL-01 | MES仅运输；issuer/documentRef.owner为原Owner；完整tuple及真实providerContractId/version/digest、AdoptedByOwner/有效期/未撤销验证，canonical全文digest独立核；不造未接受HTTP入口。 |
| WP05-01-REVIEW/CP22-04 | PLM-REL-01 | current head准确绑定documentKind/executionKey/preparationRef/sourceDescriptorDigest；保不可变旧capture但不可回退；WO_SUPPORTING_OWNER_REQUIRED_FENCED，零后继native派发；PLAN-PO不得签上游truth。 |
| WP05-01-REVIEW/CP22-05 | PLM-REL-01 | 只接受原PLM全body、PRODUCTION/NEW_MANUFACTURE、当前APPLICABLE、准确Site/窗口和technical basis；与REL同cut；不可用MES布尔绕过。 |
| WP05-01-REVIEW/CP22-06 | PLM-REL-01 | request RESOLVED、resolved UNIQUE且count1；完整variables/operations/materials/outputs与ENG canonical相等；其他状态fence。 |
| WP05-01-REVIEW/CP22-07 | PLM-REL-01 | 固定原RELEASED baseline/item/site/purpose/窗口和全technicalContent；操作身份、依赖、consumptionPoint和材料关系无损；完整三数组相等。 |
| WP05-01-REVIEW/CP22-08 | PLM-REL-01 | 保完整target/material/output usages和versioned units、正rational、EXACT整数比例及loss逐项守恒；数值相同不代量义证明。 |
| WP05-01-REVIEW/CP22-09 | PLM-REL-01 | missingInstructions/missingQualityRequirements为空仍须独立可信body；完整五member逐项对独立capture，coverage COMPLETE/missingRefs空；技术hash不代正文。 |
| WP05-01-REVIEW/CP22-10 | PLM-REL-01 | 全部准确匹配原capture和真实前驱；保accepted native wire，错配不后继派发；未证provider仍fence。 |
| WP05-01-REVIEW/CP22-11 | PLM-REL-01 | 本CP22 candidate只采用独立DEV NOT_APPLICABLE_PROVEN且空refs，与REL scope/execution和ENG digest相同；其他情况RequiredFenced；不批准替代Claim/余额流程。 |
| WP05-01-REVIEW/CP22-12 | PLM-REL-01 | 原REL全body无损；仅PRODUCTION GRANTED/current有效窗，candidate executionScope与准确授权scope/version/ranges/quantity相等；任何错配拒绝本CP22路径。 |
| WP05-01-REVIEW/CP22-13 | PLM-REL-01 | 只接受真root后原Owner post-root全schema；authorization/execution/scope/ranges/quantity/state/WO/reservationVersion齐全；保真root.relReservationRefs，零自产预约/释放。 |
| WP05-01-REVIEW/CP22-14 | PLM-REL-01 | nested仍role15；完整原native body/ordinal/status及所有键/digest保真；canIngest与canReadOwnEvents分离；gap持久WAITING_PREDECESSOR；重放原bytes，异文冲突；APPLIED仅观测，Now与原observedAt分离；noncommanded已注册read/eval才null command key；历史链不要求重造今日支持源。 |
| WP05-01-REVIEW/CP22-15 | PLM-REL-01 | 保准确accepted pins/历史批准范围；actual provider未证、17 actualAdopted=0、providerGrant=0、37AC NOT_RUN；完整后继独审/根接受不可由本意见替代。 |
| WP05-01-REVIEW/R1-01 | PLM-ITER-01 | 注册受众来自真实可信注册；完整成员保原ID/version/ref/digest及排序；不截断；超原路由能力阻提交，分包另有协议。 |
| WP05-01-REVIEW/R1-02 | PLM-ITER-01 | 严格domain+canonical完整字节摘要；Scope/Round/aggregate/Package双向相等；原业务摘要不覆写。 |
| WP05-01-REVIEW/R1-03 | PLM-ITER-01 | 合法原键保原值，否则真实持久不可变别名+完整原字节/schema双向解析；拒错误算法；Snapshot独立摘要；大小限制无截断。 |
| WP05-01-REVIEW/R1-04 | PLM-ITER-01 | 字段/大小写/null逐字遵OA R2；rejectDisposition仅本地冻结；绑定精确匹配，拒非法wire。 |
| WP05-01-REVIEW/R1-05 | PLM-ITER-01 | 按原Tx及独立唯一性持久Inbox/原包；GAP不跳过；当前绑定/SOD/全部Blocking核验，应用与Receipt/Outbox/Audit同commit；OA投影不再次驱动Owner。 |
| WP05-01-REVIEW/R1-06 | PLM-ITER-01 | 一个固定应用单元；相同不可变回执重投；仅许可BLOCKED追加revision/previousReceiptId，终态不覆写；锁外投递；不新增旁路HTTP，未注册/不完整/不无损阻新提交。 |
| WP05-01-REVIEW/R1-07 | PLM-REL-01 | 保独立输出occurrence及完整注册input/operation集合和配置；合法无输入有明确原工程依据；原single-PRODUCT wire不修改。 |
| WP05-01-REVIEW/R1-08 | PLM-REL-01 | 同时验证原Manifest和新RelationSet/Binding，准确跨引用/配置/主PRODUCT至少匹配一输出；缺任一不给完整输出。 |
| WP05-01-REVIEW/R1-09 | PLM-REL-01 | 新schema域分离canonical；数组按occurrence排序并拒绝重复输入引用；保原字节/长度，Scope+完整Ref/version精确比较。 |
| WP05-01-REVIEW/R1-10 | PLM-REL-01 | ENG本地完整同事务后继冻结；原key查回；缺映射422 REQUIRED_OUTPUT_MAPPING、源配置漂移412、异体409、Owner503且无新Baseline；旧封闭wire只经版本化错误适配；REL实际读用证据另列。 |
