# WMS-KIT-01 仓储套件作业开发设计

状态：当前正文和本目标接受原件已全文读，核心语义已整理；X3共用评审/来源附件与双方合同仍在核对。[模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)。

## 1. 业务目的和Owner边界

仓管人员定义套件版本、建立组套或拆套订单、选择各组件准确批位/Root份额，在真实现场作业后一次提交完整组成转换，并沿原组成追溯。五个SPEC为定义、订单、执行、撤销、追溯。仓储组套不自动成为MES工单、制造完工、QC通过或Finance过账。

Main WMS拥有Composition意图、稳定物理指令和组成关系；Stock独占数量/Root/Claim/Movement/物理容量账，MASTER管正式位置，RSV管Claim使用保护，技术和质量仍由原Owner给资格。通用流程不强制纸品字段；行业/物料profile必须明示适用能力，未支持几何不能用OTHER绕检。[范围和闭环](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:5)

## 2. 当前版本及接受层

本轮补核结论：2026-10-08T11:56:55Z代理接受准确六册R2及具名profile；原九根累计静态闭合，全部业务AC仍NOT_RUN、Owner运行采用UNPROVEN。[X3共同阅读与采用边界](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/X3-六专项阅读与采用边界.md)保存准确组合、112AC/31任务逐条等值核对和历史R1原ZIP缺口。下文提及原候选标签均属保留历史。

当前 v1.1 R2 RECOVERY，42871字节，SHA `7a882fade5d085d5d26e61c074304c7e3befb16e58d00768e978960437c367ed`。这是新编写的有界后继，不是缺失旧原件的字节恢复。2026-10-08T11:56:55Z 的 `UA-20261008-X3-WMS-KIT-01-R2-STATIC-MD02` 接受原完整Target和有界通用制造profile，保五SPEC、15AC、五任务。

接受是USER_AUTHORIZED_DELEGATE记录，非用户逐件个人签字；正文STOPPED保历史，所有业务测试NOT_RUN，实际Source/Owner/profile映射/共同事务/物理效果仍UNPROVEN。不能将静态Stage100当运行完成率。[准确接受原件](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3c/3c01439ddc8f03d5__UA-20261008-X3-WMS-KIT-01-R2-STATIC-MD02.json:184)、[必需输入索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)

## 3. 核心模型、配方和数量身份

| 对象 | 必须冻结的内容 |
|---|---|
| DefinitionVersion | kitProductRef/kitTechnicalRef/kitBaseUomRef、recipeBasisQuantity、稳定组件lineKey/原产品技术/requiredQuantity/转换，版本不可变 |
| Order | 原定义快照、ASSEMBLE或DISASSEMBLE、kitQuantity及准确换算、拆套前驱Composition、正式位置/原套件Root、用途 |
| KitSelection | 各组件可多Root批位的精确不重叠spans、原Claim、来源位置、输出计划及真实physicalInstruction |
| CompositionIntent | 原订单/定义、kitBaseQuantity、稳定physicalOccurrence、完整输入输出manifest、SourceBudget、writerEpoch |
| CompositionResult/Lineage | 原operation/intent、所有输入输出StockReceipt、input spans→output Root和规则、提交时间；缺任一回执不能SUCCEEDED |

业务唯一 `(orderId,physicalOccurrenceKey,direction)`，并以 `(physicalInstructionOwner,occurrenceKey)` 防复制订单重复实作。kitSku是显示/查询码，不代ProductRef；已引用不改名。InputCoverage在共同Root锁下排他，SourceBudget换版本不重置已耗。[DTO](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:47)、[持久约束](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:101)

成套输入先精确转换为kitBaseUom，再算无量纲 `recipeMultiplier=kitBaseQuantity/recipeBasisQuantity`，组件需要量=倍数×每基准量，再按该组件自身单位转换。例：1BOX=10EA，基准1EA需A2EA，组1BOX需A20EA，可选12+8两批；不能把数值1BOX当1EA。输出产品/技术/UOM须逐字等于定义，基本量合计=kitBaseQuantity。实际损耗/替代料不在正常完整组拆范围，异常事实另处置。[准确量纲](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:45)、[订单计算](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:82)

X3写入Quantity为正十进制定点字符串，最多13位整数/8位小数；只读TotalQuantity最多30位整数/8位小数，可为0。二者JSON形状相同、schema域不同：两个合法13位量合计14位可读，不能成为一行命令量。Ref保owner/id/version/digestDomain/codecVersion/digest；version opaque。Scope DTO为siteRef/warehouseRef，Tenant/Environment来自可信绑定。汇总还须同产品/技术/货主/UOM和不重叠范围，跨维度为NOT_AGGREGATABLE，UNKNOWN为null，完整空集合才0。[共用精确类型](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:146)

## 4. 正常流程、拆套和取消

定义DRAFT发布ACTIVE前核非空1–200组件、正量、产品/UOM/用途、重复行和全路径循环。首版仅原子产品组件，嵌套可执行Kit报NESTED_KIT_UNSUPPORTED。编辑建立新版本，旧版SUPERSEDED/RETIRED可读；只有从未引用DRAFT可软删留审计。

订单DRAFT → PREPARED → EXECUTING → COMPLETED；prepare冻结原定义、全部输入/输出、转换及物理指令，不等已执行。ASSEMBLE只选ACTIVE、predecessorCompositionRef=null；DISASSEMBLE必须从原Composition及仍在库的套件Root反查当时定义，允许已SUPERSEDED/RETIRED，但核当前用途/质量。不许以最新配方拆旧套件；历史组成不可证只读或RECONCILIATION。[定义和选择](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:79)

执行由Composition Owner签真实来源额度与稳定发生，输入用Stock CONSUME、输出用有据RECEIPT；不能用SHIP，也不能擅增Stock TRANSFORM枚举。最终全组提交原结果后通知只重发消息，不再组套。lineage精确到原区间和输出比例：A[0,20)、B[0,10)组成K[0,10)，每K对应2A/1B；LotNo或数组索引不能代映射。[执行及谱系](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:87)

DRAFT取消须完整证明未准备/未执行；PREPARED取消必须永久封全部原Owner请求、未回步骤/worker并证现场未发生。任一腿APPLIED但全组缺关联→RECONCILIATION，UNKNOWN保保护。全组SEALED_NO_EFFECT或有权NEVER_SENT且现场无效果才CANCELLED，释放沿RSV NoFurtherUse。已完成反悔是新物理拆/组订单；记账错误另由Stock Correction处理，不抹原lineage。[取消](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:94)

## 5. API与跨Owner合同

候选 `/api/wms/kit/v1`：context和受权definition/product/location/stock/composition/kit-uom-conversion selectors；definitions create/update/activate/retire；orders create/update/read/preview/prepare/cancel/trace；operations execute、by-key/read、resume/seal。所有真实Ref由Owner selector给，不能手填URL/外域ID。CommandMeta含commandKey、expectedRevision（create=null，已有对象必非null）、必要reason；bodyHash由服务按封闭schema计算。[接口表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:57)

`ComposeInSameUow(CompositionIntent)` 为拟议版本化联合适配：先核全部Stock原计划/manifest，收集所有execution/leaf/Scope/Root/Claim/容量域和本Composition canonical，整体校验输入输出写集；Stock消费/产出Root及Receipt、组成谱系、订单终态、SourceBudget、结果/Audit/Outbox同Main QuantityUoW。跨产品/UOM只核各腿自身量义与配方关系，不做裸数量加总守恒。现Stock实际支持能力仍待Owner采用。[联合适配](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:90)

## 6. 同事务、幂等和恢复

必须同DbConnection/DbTransaction/QuantityUnitOfWork，底层不能提前commit，持锁中不做远端HTTP。先外读原件和收集完整资源，再按共同ordinal取canonical、Scope/Use/Writer gates、Root/Claim/SourceAllowance/容量和heads，重读当前权限/来源/期限后原子提交。锁后发现新资源必须整笔回滚重准备，不动态补锁。仅UNIQUE(from,to)不足以保护区间重叠。[共用事务约束](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:162)

HTTP commandKey与业务实际键分别防重；换HTTP键不能再消费同实物。外域调用先持久原请求体/摘要域、originalBusinessKey、locator、step/attempt/epoch；无Owner operationId时按原请求键查，不伪造ID。Operation state与knowledge正交，timeout不改REJECTED。恢复只开放有权QUERY_ORIGINAL、RESUME_ORIGINAL_STEP、LINK_CONFIRMED_RESULT、REDRIVE_ORIGINAL_EVENT、REQUEST_SEAL；APPLIED只补关联，NOT_FOUND不证无效果，NOT_APPLIED_PROVEN必须有完整边界与永久fence。未决墓碑不可随缓存清理。[恢复](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:172)

## 7. 权限和审计

对象权为租户∩Site∩源仓∩目标仓/区∩货主∩字段，空授权拒绝；不能只核目标。read/draft/prepare/execute/approve/cancel/recover/seal/history/export分开；批准不含执行，运维重投不能改输入。历史原成功按当前读权回放，不要求仍有新写权。REAL拒DEMO源/回执，页面不能升级环境。

审计保真实actor/service、原inputDigest及域、旧新revision、业务键、请求/决定/提交时间、理由、Scope/Policy/WriterEpoch及Owner回执。导出生成和下载均重新核权，受限字段不返回、未知字段带reason，两者不同。[权限边界](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:189)

## 8. 页面与错误处理

K01定义列表，K02定义详情/版本，K03订单列表，K04方向/历史组成/准确换算订单，K05各腿范围/Claim/容量预览与冻结执行，K06原图与当前解释追溯，K07取消/原键恢复。准备后字段只读；换物料/技术/仓/货主/UOM清候选和preview。拆套新输出批号在prepare稳定分配，重试不重选号。

分页默认50、最大200，cursor绑定主体/授权/筛选/固定cut；完整空候选不等Owner不可用。400 schema/精度、403动作/404不可见、409键/范围冲突、412 revision stale、422来源/质量/位置/单位、423旧writer fenced；202为持久等待/UNKNOWN，503仅在可能已提交时带原键与未知，不造虚假ref。关闭/Back只停轮询，双击复用原key，迟返按queryGeneration丢弃。[页面](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:33)、[错误表](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:174)

## 9. 开发任务和存量切换

KIT-D01定义版本/完整成套量纲/主数据；D02准确单位、多批、历史拆套与冻结意图；D03 Stock联合多计划UoW与组成Source；D04取消/UNKNOWN/封闭/真实反向与历史半成功处置；D05谱系、权限、兼容/导出。共同采用门未满足时可做草稿/查询，不能退回旧逐腿ApplyAsync执行。[任务](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:105)

原稿固定CP6 `90c871fe…` 的KittingService映射：执行时读当前配方、逐组件单一足量Stock、逐次OUT后IN，底层无ambient时各自commit，最后才写Executed；接口注释不证全组原子。本轮未新审代码。旧Draft已有RelatedNo流水却无完整TxnNos必须RECONCILIATION，不能重Execute补齐。[旧事实](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:25)

DP11/12/16按Tenant+Site+模块+对象/范围设置OLD→HOLD→NEW及单调epoch，列所有旧路由、job、导入、客户端。保原单号/状态/JSON/TxnNos/摘要和已知时区。先HOLD阻新写、排尽/封闭未决、核LegacyMap和Owner采用，再NEW。新效果已提交只能前滚修复；回OLD须证NEW无任何效果/未知已发、永久封新入口并批准，不以隐藏按钮代最终门。[迁移](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:197)

## 10. 验收设计及真实状态

15 AC均NOT_RUN：非法/循环定义零Stock；v2发布后依原v1组成拆套；1BOX→10EA且A12+8多批；缺历史组成拒准备；输出SKU/技术/UOM不符零效果；最后输出容量失败全组回滚；同实际发生换HTTP键只一次；两个订单争Root最多一组；UNKNOWN取消保保护；已完成反向是新订单；精确区间追溯与14位只读汇总；更正未知保原图；源仓撤权阻执行；NEW下旧Execute423；旧Draft半成功进入核对。

正文附录的完整DefinitionInput、OrderInput及Quantity/TotalQuantity反例已逐字段读；64个零digest明确是隔离形状占位，不作签名/真实回执证据。[全部AC](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:113)、[精确JSON与断言](D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/7a/7a882fade5d085d5__WMS-KIT-01_仓储套件作业_完整设计_RECOVERY_v1.1_R2.md:217)

## 11. 尚需确认

K-G01 Stock联合多计划UoW；K-G02 WMS真实Compose Source/容量映射；K-G03产品/技术/UOM/质量；K-G04全部旧Execute/导入/job单writer；K-G05历史准确组成；K-G06权限/留存。均需真实Owner采用与适用范围证据，缺哪项只阻对应新效果。X3共同评审/CURRENT/来源/AC任务登记仍在补核，尚不声明完整必要材料终结。

## 12. 来源和实读范围

本轮完整实读当前正文1–415行（含共同A1–A7和全部JSON），KIT UA1–305行；其余X3材料按[root阅读账](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/root-specialty-reading.json)明确待核。依赖旧稿的下载403/源码读取/测试叙述沿原时间与窗口，不冒成本轮重新执行。全量哈希和规范入口见[必需输入](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json)。

## 必要材料整理结论

当前15项必需引用均已完整阅读或使用有证据的精确复用：主文、当前接受/审查、原范围、AC与任务文字以及来源资格已纳入设计。历史成员目录只证明来源身份，不等于旧代码全文审计；外域实际采用、Provider和业务运行继续按本册门禁确认。见[逐目标必需引用核对](../evidence/root-specialty-required-closure.json)。后续整任务进行一次集中跨模块复核，不将静态设计整理等同实际验收。
