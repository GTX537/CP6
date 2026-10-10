# WMS-INK-01 油墨批次与混合开发设计

状态：核心语义已整理，必要附件仍在补读；设计被 X4 接受不表示功能已实现。本文不授权运行旧稿中的指令。导航：[总目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 目的、操作者和权威边界

仓库人员登记油墨资料、记录首开封、准备和提交两个父批的混合、记录颜色匹配并查询谱系。WMS Ink 管理这些意图和不可变事实；Stock 管理实物数量、原份额、位置、转换和数量回执；Quality 管理期限规则、配方适用、检验义务与质量结果。颜色匹配成功不能当质量放行，InkLot.Quantity 不能成为第二套库存账。

正式混合仅支持两个不同父批，同租户、现场、货主、基本单位且属于获准兼容组。两父必须先完成有据首开封。输入输出以八位精确十进制守恒；密度换算、挥发损失和货主转让不在正常混合范围。真实异常保存观察后交 Stock/Quality 处置。[范围与政策](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:16)

## 2. 当前组合与接受边界

当前正文为 R2.1，SHA 前缀 `5fbc434a55d2a031`。完整 SHA 和必需附件通过 [规范输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json) 按 TargetID 检索。R2.1 只补齐原 R03 的 `sourceSelectionRef` 请求、绑定和传播；保留 R2 的其他七项修订、原 SPEC/AC/任务编号。

正文首页 `NOT_ACCEPTED` 是停止修改时的静态状态；随后 X4 接受记录接受 INK R2.1 与另外五册 R2。共 27 SPEC、95 AC、46 任务仍全部 NOT_RUN；INK 为 14 AC、8 任务。接受由获用户授权的代理记录，不等同用户逐件签字。实际 Owner 采用、同事务接线和业务验收仍未证明。[正文状态](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:3)、[接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/59/59dff54660e0a2fe__X4-SIX-TARGET-ACCEPTANCE.md:1)

## 3. 数据、身份和数值

| 对象 | 开发时必须保留的含义 |
|---|---|
| InkLotRead | profileRevision 与来源知识版本分开；Stock 绑定量与 legacyDeclaredQuantity 分开；开封事实、正式应用、效期知识、质量观察和当前资格分别展示 |
| InkOpenEvent | 每批唯一首开封，保存原发生时刻、记录时刻、原效期、政策、计算结果和稳定发生键；纠正另作后继 |
| MixDraft | 两父准确原 Root/半开区间、量/UOM、开封事件和控制头；输出 OUT1、产品、位置、期限规则；冻结 manifestDigest |
| MixReceipt | 原 mix/operation、Stock 操作与原回执、父消费份额、子 Root/lot、守恒结果、质量应用和审计引用 |
| Match | 配方版本、两种消耗模式、报告量、真实消耗回执、观察结果和质量结果各自保存 |

稳定 K 为可信 `(environmentId,tenantId,siteId,mode,producerOwner,sourceKind,sourceDocumentId,sourceLineId,occurrenceLeafId)`，sourceKind 为 `INK_OPEN_ACTUAL` 或 `INK_MIX_ACTUAL`。recordVersion、HTTP 键、草稿 revision 不产生新发生。相同 K 同实际内容返回原效果；同 K 不同父、量、时间或输出为 `OCCURRENCE_CONTENT_CONFLICT`。证据注册必须把稳定 evidence/document/line/eventOrdinal/kind 唯一映射至同一实际叶，不能每次点击新建身份。[对象定义](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:42)、[发生键](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:86)

数量为 decimal(21,8) 字符串，汇总 decimal(38,8)；拒绝科学计数、负零、超八位或无法精确表达。viscosityCp 为可空非负 decimal(10,2)，solidContent 为可空 decimal(5,2)、0..100。配方最多 100 个唯一色号，比例在 [0,1] 且精确合计 1，规范 JSON 不超过 2000 bytes。[页面字段](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:28)

## 4. 流程、状态与业务效果

1. 登记只绑定既有 Stock Root/Slice；历史观察模式不产生入库量。
2. 首开封计算 `min(原效期,获准开封后期限,更早手工截止)`，不能人工延长。开封事件/profile 后继/Audit/Receipt 原子保存，Quality/Stock 后续应用状态独立。旧“30 天”示例不是专业政策。
3. 混合 `DRAFT → PREPARED → SUBMITTED → POSTED`，或 `REJECTED_FINAL`。prepare 冻结完整来源和政策，不扣量、不发执行许可。submit 最终同事务复验后，一次消费两父精确范围、建立子 Root、子 Ink 元数据、MixReceipt、子开封、质量总体映射和 Outbox。缺最终适配停在待采用状态。
4. 子批从混合形成时即 OPENED。derivedBaseExpiry 取两父 effectiveExpiry 与有据 outputBaseBound 的最小值，再用 afterMixOpenBound 求 effectiveExpiry；未知期限不能按无限期处理。唯一 `MixDerivedOpenEvent` 绑定 K/OUT1，不能再调用普通 open。
5. 匹配 OBSERVATION_ONLY 仅增记录；WITH_CONSUMPTION 必须核原实际消耗回执并只引用，不再次扣量。MATCHED 与 Quality PASSED 分轴。

`UNKNOWN` 是提交观察，不覆写原持久终态。父部分耗用后仍保 OPENED 和原缩期；不能恢复未开封寿命。[处理过程](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:58)、[子批期限](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:144)

实际已开封或已混合但政策/证据缺失时，显式 observation 保存原 K 和真实 body，返回 RECORDED_OBSERVATION；不扣父量、建子库存或给质量许可。开封观察使实际知识 REPORTED_OPEN、正式应用 PENDING、效期 UNKNOWN，新正常使用受限。有权 recognition decision 认定事实后按原 K apply；原发生未授权仍保 UNPROVEN/VIOLATION，不能倒填批准。异常混合 apply 只关联 Stock/Quality 的原处置，不重跑正常 Mix。[观察与认定](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:94)

## 5. API、Quality 与 Stock 合同

候选前缀 `/api/wms/specials/v1/ink` 提供 lots/lineage/history、stock-candidates、policies、matches、operations，及 register/open、mixes 的 create/update/prepare/submit、matches。发生 options/register、observations、recognition-decisions、apply 有独立公开入口。除纯 create 外原稿要求 If-Match，缺失 428。这些路径尚非已实现路由。[API 原文](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:54)

拟议 InkStockAdapter 为 `Prepare → ExecuteInUow → QueryOriginal`；完整输入保两父原区间/UOM/货主/技术用途、容量和稳定发生，完整输出保原数量回执、消费范围、输出 Root/数量。Stock Owner 尚须签认 native 映射，不能称其已有原生 API。[Stock 适配](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:62)

Quality 候选 `INKQ-1` 是独立协议，不能把其 sourceKind/stage 塞进 QA-POL 旧枚举。流程如下：

1. Ink producer 生成不可变 QualitySourceSelection，完整给 OPEN 或 MIX 联合、实际总体 domain、member manifest、原 Root 映射、来源头和 coverage。新子 Root 未建立前，OUT1 的 `[0,q)` 是实际输出总体坐标，不能伪造 Stock RootId。
2. `GET /api/quality/ink-policy/v1/options` 读 selector；随后 POST resolutions 必须在 body **再次显式传 sourceSelectionRef**，不能依赖 options 会话。
3. Quality 回读准确 version/digest 原件，逐字段比对完整 source 回显、主体摘要、父范围、量、UOM、技术/用途、时间、开封/control 和 sourceVector；把 selector 与 sourceSubjectDigest 纳入本域输入摘要和不可变 Resolution。
4. 同 requestKey 同完整绑定回放原结果，今天 producer 不可达也不重算；换 selector 为 409，缺 selector 400、回显不符 422、原件不可证 503。current 与最终 Guard 必须传同一 selector/inputDigest/sourceSubjectDigest。

Resolution 区分 APPLICABLE、NOT_APPLICABLE_WITH_BASIS、REJECTED、UNPROVEN，**不是质量 PASS**。每个检验要求均须明确 REQUIRED/EXEMPT/NOT_APPLICABLE 及依据；缺项为 PARTIAL/UNPROVEN。完整 wire 见 [INKQ 定义及 R2.1 补正](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:106)。

## 6. 事务、并发、幂等与未知恢复

原 operation 查回先于新执行；`ink.v1` canonical 输入保 decimal/UTC/稳定源序，hash 只校验身份。最终 `InkQualityParticipant.HoldExactInUow` 必须加入真实 QuantityUnitOfWork，沿共同锁序保护当前政策/目录、父开封和来源、技术、完整 Family 头直至最外层提交。HTTP current、TTL 或 prepare 结果不能替代此门。[最终保护](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:134)

同次 Stock 转换必须建立完整 PopulationToRootMap，将 OUT1 的 domain 区间一一绑定实际 Root 原区间；Quality RecordUseInUow 记录 Guard 使用、Family 义务及数量回执，Stock 子 Root 保 PENDING/RESTRICTED 直至实际检验或合法豁免应用。任一 map/义务缺失使整个转换零提交。撤回先获门则混合零效果；混合先提交则历史保留，撤回改变当前限制。

两 mix 抢同父范围由 Stock 排他，只允许一方成功。提交断线保原 operationId/frozenManifest；查两域原 journal，不因 UNKNOWN/NOT_FOUND_OBSERVED 换键再混。Stock 已提交而本域副投影缺失只能补原结果投影，并作为同事务架构异常调查。错误族 422 表达业务条件、409 版本/manifest/重复发生、503 Owner/结果未知、403 权限。[恢复及错误](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:68)

## 7. 权限、租户和敏感信息

Scope 来自服务身份，不能信请求 body。原 wms-ink:add/open/mix 与拟议 read/history/recipe-read/consumption-link、actual-record/recognize/apply 要由 IAM 明确映射。认定与应用保职责分离；不信浏览器 approved 或 operatorCd。客户配方/成本的字段掩码必须覆盖排序、搜索、谱系和导出。审计保实际操作者、前后版本、父范围、政策、发生/记录双时间和原回执。历史读权缩减仅减少返回，不改原摘要。[权限与采用门](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:76)

## 8. 页面和服务端校验

P01 批次列表；P02 资料/开封/谱系/质量/数量回执/历史；P03 首开封和显式实际观察；P04 两父混合工作台；P05 客户色匹配；P06 一层展开的谱系。默认 50、上限 100；无量来源为 UNKNOWN，不能显示 0。色号统一最多 30 字符，金额/数量不转 JS number。

混合界面先展示两父开封、范围、质量用途、期限和目标容量，冻结 manifest 后保原 mix/operation 恢复。UNKNOWN 只查原结果；409 保草稿；迟返不覆盖新筛选；权限变化清敏感缓存。图上无父读权显示 REDACTED_LINK，历史环标 LEGACY_GRAPH_CONFLICT，不无限递归。正常动作失败不能静默转为“观察成功”。[页面细则](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:26)

## 9. 实施顺序和旧代码差异

建议按原 I01–I08 依赖：身份/不可变事件/唯一约束与旧数据保留 → 开封政策和观察 → Stock 同事务转换 → INKQ producer/resolve/query/Guard/Family → 匹配和谱系 → 六个页面 → IAM/旧 writer 互斥 → 全部契约与并发验收。原任务定位详见 [开发任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:156)。

原稿对 CP6 `90c871fe…` 固定代码的映射指出：CreateLot 直接写 Quantity，Open 可覆盖 expiry，Mix 直接扣两条 InkLot 并建子量，未显式核共同 UOM/货主/技术/质量；RecordMatch 只写历史。此为原稿的固定源码证据，本轮没有再次审计业务代码。迁移须逐批准确绑定 Stock，旧 Quantity 标 LEGACY_UNRECONCILED、旧配方/expiry/父号保留，旧谱系缺消费范围不反推均分。采用 writer epoch 后必须挡住旧 mix 直改余额，不能双写。[源码映射](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:10)、[迁移](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:74)

## 10. 验收设计与执行状态

14 项 AC 全部 NOT_RUN；本次仅整理，未执行业务测试。必须覆盖：登记不重复入库；首开封取最短期限及拒延长；重放不二开；父 5kg 取 3、8kg 取 4，剩 2/4、子 7 且全事务；单位/货主/期限错误零效果；竞争仅一次；MATCHED 不越权放行；观察不扣量；真实消耗引用不重扣；配方比例/重复/字节边界；历史未知不补造；断线原键恢复；UI 双击与迟返；配方访问控制；时区、八位边界和 overflow。

R2 扩充还须验实际观察/认定、稳定 K 换 transport/version 不重复、父首开封前置、子独立期限、撤回竞争、输出总体/Root/Family 完整映射。R2.1 在原 AC07 补 selector 成功、缺失、回显冲突、同键换 selector 四类，并验证读过 options 后 POST 仍回读绑定。[全部 AC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:154)

## 11. 编码前待核

- G-INK1：Stock 真实两父转换、共同 UoW/锁序和原键查询采用证据；缺失时正式混合 RequiredFenced。
- G-INK2：Quality 批准的期限、配方、子总体/检验义务与 INKQ 新 adapter；不能默认 30 天或复用旧 QAP 枚举。
- G-INK3–5：MASTER 位置/容量、IAM/旧新 writer 隔离、客户/产品准确身份读。
- 本文所述完整候选 wire 解决“需要什么”；native 映射、真实 writer 覆盖与验收仍需证明。共享评审、目标 UA/CURRENT、适用性和原任务/验收册已补核；跨Owner双方合同与现实采用证据仍待集中核对。

[采用条件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/5f/5fbc434a55d2a031__WMS-INK-01_油墨批次管理_完整详设_R2.1_CANDIDATE.md:80)

## 12. 来源和实际阅读覆盖

本轮完整阅读 R2.1 正文 1–173 行，含全部 wire、补正、页面、任务和 14 AC；同时已完整阅读 X4 接受、R2.1 最终独审、根决定和来源/复用限定。该覆盖不代表所有必需附件已整理。准确逐件 SHA、行段和未读范围见 [root 阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)。没有把旧稿中的测试通过、源码读取或停止指令算成本轮执行。

补读完成记录及准确同文复用方法见[X4六专项阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X4-六专项阅读与采用边界.md)。这不替代外域依赖的双方集中核对或实际业务验收。

## 必要材料整理结论

当前17项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
