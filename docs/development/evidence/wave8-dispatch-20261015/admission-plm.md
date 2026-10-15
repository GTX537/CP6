# 第八轮 WP01-01 准入分析（只读）

日期：2026-10-15。核对本地 main 与 origin/main 均为 `b0a4f5601b008d0003598065bbf3241124442890`；本轮未访问网络。Platform `66480fe24d51c706708be971e7e9d75a684a3bc4` 来自协调已确认输入，本分析未重新验证。没有修改仓库、台账、数据库或 Actions，没有重新执行旧验证。

**结论：放行 WP01-01 的“正式台账完成门代码接线＋完整append-only采纳记录候选”有界实施窗口。不要再派一轮无接线的准备包。** 用户已经授权设计修订和继续开发，协调可以作不改变合法输入、身份、权限与事务原子性的技术取舍并安排实现；旧材料的 `UNSIGNED/NOT_ADOPTED` 描述此前事实，不构成要求用户重复授权开发的理由。技术记录须如实说明由协调在既有授权范围内作出何种决定，引用可核对的原授权来源；不能替用户补姓名、签名或过去的签署时间。技术取舍记录与业务basis正式采纳分开。

**本轮台账边界：WP01-01整体仍为 PLANNED / NOT_CHECKED；局部 bound implementation slice 单独 CONFIRMED，不提升整卡 READY。** 协调需在本任务assignment/窗口附属记录明确允许adapter、manager及定向测试实际代码实施，注明整体entry条件尚未全部满足；若manager当前只允许整卡CONFIRMED启动，应以专门的有界窗口表达/校验补充，不能靠伪造整卡READY穿过门。派工和完成代码接线都不写 `actual_adoption=true`。最终准确manifest、579义务、正式门行为具备可审查结果后，再处理业务basis正式采纳。现有本分析上下文只有协调报告的广义开发授权，未含用户对最终依据的准确采纳措辞，不据此宣称已正式采纳。此次安排不强行补足四个并行任务。

## 已核定的实施切口

- `docs/development/tools/manage_roadmap.py:94` 的 WP01 汇总目前只接受全部125份原清单 COMPLETE；`:149` 的目标 DONE 目前只接受原用例全 PASS。必须同时实现两个层级：123个目标保持原门；ITER/REL 可走明确采用的新依据路径，旧清单仍为 NOT_INVENTORIED。输出应显示“123份原清单＋2份新依据”，不能显示“125份原件已找回”。WP01 汇总仍保留全部原有非清单条件。
- 当前 `eng/wp01-01/decision-closure/gate_candidate.py:177` 对 actual 无条件加入 `AUTHORITY_AUTHENTICITY_UNVERIFIED`、`REMOTE_ANCESTRY_UNVERIFIED`，并要求候选 `production_enabled=false`。它只能做私有设计验证，不能翻布尔开关成为正式门。保留原候选字节，新增正式 adapter，显式核对采纳权限来源、准确制品、执行证据来源与各仓远端包含性；不能只信 JSON 中 `signed/PASS/synthetic=false`。
- 1.1.0-proposed 原矩阵是577个唯一义务，ITER400、REL177；1.2.0-proposed 的 DC-01/DC-02 各新增1项，合计579（401/178）。18个结构化 source_assertion 全字段、数组顺序及逐义务准确来源须保留。它们是义务数，不是找回的原30/48测试数，也不是业务已通过数。
- 应明确分别返回：`basis_adoption_valid`（新依据生效）、`inventory_coverage_valid`（可知义务覆盖）、`target_runtime_valid`（真实目标完成）。WP01-01 任务交付不要求先完成所有 PLM 服务或全部业务用例；ITER/REL DONE 仍要求完整义务、R1、实现、消费者和真实运行证据。不得把业务未完成变成新依据不能接线的循环前置。
- 完整候选 manifest 当前 RAW_FILE_BYTES SHA-256：`7eaa62f8e2904978f7440de5a15c8e0d9bc8328ffe12199dfee99ebe098ab839`。candidate gate ref：`7fd2401374e1e77900de73879937f720fcca5267dfdf7a2a99edeb2b62c9fee6`，94101字节。正式记录应绑定准确 Git blob/原字节表示，新增正式版本 manifest，不覆盖旧版或旧未签模板。

## DP01–08 的具体决定与边界

| 项 | 可由协调在本轮决定/推荐 | 仍缺的具体值或实证 | 只阻断什么 |
|---|---|---|---|
| DP01 摘要 | 采用四种摘要分离：原 ENG technicalManifestDigest 保原 schema/algorithm/value/raw bytes、ITER opaque；ReviewPackageDigest 保冻结原包；aggregate 用已接受 ITER-CANDIDATE-AGGREGATE/1；snapshot 只对准确快照字节 SHA-256。`CD-ENG-01/1.0.0-proposed` 可作为具名技术后继开展兼容验证，推荐暂不替换既有 ENG 摘要。 | ENG Owner 采用的准确 schema/version、规范化/域前缀/字段集、原合法域兼容向量、旧→新无损映射与版本路由。现有新 envelope 只是候选；不能凭名称写入旧 technicalManifestDigest。属于合同工程取舍和兼容验证，不默认是用户业务签字事项。 | 未确定算法的新 ENG 序列化路由；不阻止保留原摘要的门实现。后继 WP05-03/12/19。 |
| DP02 审批 | 采用可信 Owner/BizType/SubjectKind 注册、冻结 round/policy binding、当前权限及 SOD 复核；技术实现同时支持 EXACT_PINNED 与一次 RESOLVE_PUBLISHED_ON_SUBMIT，测试默认选前者便于重现，真实配置不能擅自默认。缺策略拒绝、无合格角色按 OA Suspended。 | 实际 tenant/project；成员目录版本和成员ID；角色分配版本；代理 grant/ref/有效期；SOD规则及单独授权例外；已发布 policy ID/version/digest；selectionMode；rejectDisposition=RETURN 或 REJECT。真实业务 Owner/用户须选择或提供这些配置，不能造人员或自批例外。 | 对应项目真实审批发起/应用；不阻止通用 binding、校验器和完成门。后继 WP12-06/07/19、WP05-19/20。 |
| DP03 PostgreSQL | 采用 cp6-pg-tenancy-v1 有界布局及修正后 ITER-REL-PG-PHYSICAL 候选作为后继实施基准。两原Id全局（Project/Iteration），四原Id按源作用域（IterationInput/CandidateObjectRef/ReviewRound/ExternalRequestReceipt）；TenantId与同租户父FK保留；全局键不能因schema拆分。UUID CAS 与业务版本区分，原数字/时间/身份字节权威、numeric 仅无损投影。 | 实际 schema/login/role/RLS/可信映射、所有写者锁序、driver精确数值 roundtrip、迁移谱系、CAS竞争及恢复。若实现共享身份注册适配，需具名技术版本和无损证明，不自动全库一租户一schema。 | 真实DB写入/隔离和生产迁移；不阻止接受物理设计与离线门。后继 WP02-02/03/10/13、WP05-18/32。 |
| DP04 保留 | 采用 append-only 更正、关闭/否决不删证据、当前 hold 与 Owner fence 校验。缺有效政策时清理拒绝，允许原已授权 append/read；CRM的24个月规则不外推。 | 工程各分类/司法辖区/用途的保留年限及起算点；脱敏界线；批准销毁条件、有效policyRef/version与scopeDigest；实际 legalHold 判断和对象版本。这些是业务/合规事实，须真实负责人提供；协调不能臆定30天/24个月。 | 销毁/匿名化清理，不阻断正常保留和可追溯读写。后继 WP02-13、WP05-21/36。 |
| DP05 容量 | 推荐采纳 UI默认50、API分页最大200、stream chunk500、展示引用/交付物chunk50作为分页预算，绝不是合法集合上限；固定快照cursor与字节序；OA原256KiB快照/1MiB正文/1000 IDs边界不变。 | 真实项目工作负载分布、最大候选/引用/评审集、并发量；应由产品/业务接受的延迟/吞吐/内存SLO数值；测量环境与容量报告。协调可选实验参数，不能声称生产SLO已接受。 | 容量验收与超既有OA wire域路由；不能因此删合法草稿或截断集合。后继 WP05-18/19。 |
| DP06 安全结束 | 原Plan/version/intent/key/content、根＋完整子集、准确量纲/范围/profile、水位及无迟到执行 fence 均已定。UNKNOWN/超时/NOT_FOUND/仅根Cancelled不得释放；原键恢复。 | VER/MES真实安全终态生产者、完整根子事实和无迟到效果证明，联合故障/竞争/恢复测试。**这是实现/实证缺口，不是等待用户再批准语义。** | 依赖它的取消、关闭、释放/结清。后继 WP05-21/24、WP08-15/18；相关结果发布另按WP05-61实际卡片。 |
| DP07 多语言 | 中文为现有审阅基准；稳定FieldId/Value不随Label变；版本化翻译草稿→合格审阅→发布bundle→CAS manifest及回滚流程可直接实施。 | 逐key的日/韩最终译文、审阅集/entry版本、真实合格审阅者/授权、发布版本和客户端精确bundle采纳。可生成草稿，但不能机器翻译自签发布。 | 日韩内容正式发布及客户端语言验收，不阻止中文实现/通用流程。后继 WP02-17、WP05-18/32。 |
| DP08 当前源码 | 采用有界 REUSE/CHANGE/NEW 审计；b7ec269仅历史参考；旧同步 FirstOrDefault callback 不能当持久OA协议。本地比对99fc0ce..main在 CP6.Core/Entity/WebApi、cp6.web及manager无相关源码变更，原有界审计可复用到当前Main，不重做产品全审。 | 实际服务/model/API/host/worker/UI/DB、准确Platform包与消费路由、跨仓remote采用证据。Platform源基线已前进，后继仅核对所选profile/包的相关差异。 | 对应真实业务目标完成，不阻断WP01技术采纳与门实现。 |

## 采纳记录如何做到具体而不伪造批准

准备拟生效的 `CP6-WP05-01-ACCEPTANCE-BASIS/1.2.0` 或等价明确版本包，状态为ADOPTION_PENDING，追加引用旧1.0、1.1、1.2-proposed及Issue211修正的准确manifest；不原地把候选版本字符串改为正式，也不因版本号去掉proposed便宣称生效。完整采纳候选文本明确“以当前可知义务建立新标准，无法证明旧30/48逐项等价”，确认579条与未解决业务配置继续存在。

技术选择记录可填写实际作决定的COORD代理身份、记录产生时间、用户已给开发授权的可核对会话/决定来源及其范围；如无原授权准确时间则保留未知，不能拿当前时间替代。将“用户授权继续修订开发”与“协调选定上述技术方案”分栏，不伪称用户已逐项签了新hash。业务basis采纳候选的批准者/批准时间/effective_at仍保持真实未知，不写用户名字。本轮无需为技术选型重复问用户；adapter和完整可审查记录先做完。只有最终候选已经具体可审且实际授权来源/准确范围可核验时，才处理正式生效。不能以Git提交者、Handoff ACK或固定commit本身替代业务批准。

正式门需本地可信输入边界：读取版本化采纳记录，验证授权来源与委托范围；校验真实执行报告与其原始日志/来源，验证各适用仓库准确commit的远端main包含性与包hash。缺任一信任输入返回明确 UNVERIFIED，不能改成可信布尔字段。测试可注入明确隔离的假验证器，测试fixture永远不作为真实采纳或运行证据。这里的“正式门”是本地台账工具，不是部署、CI运行或生产服务启用授权。

## 真实消费者缺口与下一卡前置

当前 provider-consumer-closure.json 是1256条传递规划边，1256条的 contract.profile、provider_implementation、consumer_application 都为空；它不是1256个已运行消费路径。正式adapter应保留声明依赖，允许按可核授权的具体场景登记实际适用边及其原因，不凭空把TASK_DELIVERY删除为可选，也不要求先执行全部传递图才交付WP01门。

缺少的具体消费链：

- ITER：WP05-18项目/输入/任务确认 → WP05-19全集评审包与OA持久提交 → WP05-20决定应用/问题关闭/撤回竞争 → WP05-21结果关闭/原操作恢复。对应拟新增 EngineeringIterationService、IterationReviewSubmissionService、IterationDecisionApplicationService、IterationClosureService；同时需要真实controller/model/host/worker/事务/UI。
- OA：WP12-06提交政策及注册、WP12-07持久决定和回执、WP12-19可信代理/SOD；当前FormSubmissionService、ApprovalDispatcher、Wf_ApprovalBinding须改造后才能成为这些消费者。
- REL：WP05-32用途授权、33预约/intent准入、34目标可靠交付、35真实消费/结清、36逐Target采用与后果关闭、42专业条件采用；对应ReleaseUseAuthorization/Reservation/TechnicalReleaseDelivery/ConsumptionSettlement/TargetAdoption/ProfessionalConditionAdoption服务。
- VER/MES：WP05-24、WP08-15/18提供真实根子安全终态与防迟到；后继服务消费这些事实后才可释放。WP05-61实际标题是“工程Intake Progress规范流与精确纠错提供者”，不能笼统写成完整联合E2E已覆盖。
- Platform→Main：CallerTransactionInboxV1/OutboxV2 与 Owner 锁序组合仍要选准确交付profile、package版本/hash并落实同连接同事务。Main声明 `[0.10.2]` 不等于恢复包/发布包/真实运行采用；候选中的Platform build默认 `0.8.0-alpha.2` 也不能自动替代。另有WP02-13审计、WP02-17语言客户端采用。

当前台账中 WP05-01=DONE/CONFIRMED；WP01-01=PLANNED/NOT_CHECKED。WP02-10/13/16、WP05-03/14、WP12-06/07/19均PLANNED/NOT_CHECKED，所以此刻不能直接把WP05-18/19/32作为已准入的整任务派工；WP01-01门实现本身的直接前置WP05-01已经具备。

## 建议下发任务卡

**任务ID仍为WP01-01；有界窗口名：正式台账门接线与采纳记录候选。** 本地最新main建独立分支/worktree，协调登记 `coordination:roadmap` 串行窗口。此窗口将准备成果落实到可运行的正式完成门，并提交可审查的具体技术选择及完整业务采纳候选。整体PLANNED/NOT_CHECKED，窗口CONFIRMED；不通过改整卡READY声称正式basis已经采纳。

允许A实际写入：`docs/development/evidence/WP01-01/adoption-v1.2/`（新manifest、采纳来源、逐DP决定/剩余项、579映射、真实门判定报告、交付）；`docs/development/tools/acceptance_basis.py`（建议新adapter）；`docs/development/tools/test_acceptance_basis.py`（建议新定向测试）。COORD本轮把 `docs/development/tools/manage_roadmap.py`、`docs/development/tools/test_manage_roadmap.py` 独占编辑窗口明确授权给A，A可以完成实际接线，不必再交回协调代写。`task-definitions.json` 若需细化本卡仍仅由协调修改；roadmap、生成任务/进度页及四份项目状态仍仅由协调统一同步。

禁写：旧WP05交付/采纳模板、preparation/decision-closure已固定证据和其候选测试结果；业务Core/Entity/WebApi/UI、EF/DDL/迁移、Platform/CRM、现有环境、`.github/workflows`；不得补假批准、改旧30/48状态或标目标DONE。

前置：最新main核对；WP05-01准确交付和Issue211修正纳入；原授权来源可引用；共享工具/台账写窗口；全部候选准确字节可读。业务配置缺失不构成这张实现卡的启动前置。

验收：

1. 完整拟生效版本和采纳候选可审；COORD技术选择有真实开发授权范围依据，业务采纳仍ADOPTION_PENDING/actual_adoption=false；原候选不可变；两全局四scoped原Id、精确值与范围不变。
2. 门区分任务交付、清单闭合、目标DONE；现有真实台账维持零新增业务PASS/DONE，旧30/48缺件不变。具名采纳不能令空业务证据通过。
3. 401/178精确义务与18结构化断言、逐义务来源齐全；缺失/多余/重复义务、少字段、数组重排、错hash/字节表示、JSON字符串伪装结构、其他123目标旁路均拒绝。
4. 无权采纳、错版本/目标、未经证实的authority/remote、静态或synthetic冒充真实、仅包引用或ACK、缺Owner/consumer/运行证据均拒绝。1256条规划边不能自称已BOUND；细分场景不能删除声明前置。
5. 本地运行新增adapter定向测试、现有manager相关回归、manager只读校验与diff检查；不全仓编译、不重跑既有无变化的业务验证、不运行Actions。单次任务级审查；合并后核对远端main包含性，记录复用范围。

剩余未验证：本报告没有确认业务basis最终采纳、真人签署/业务配置、生产SLO、日韩审阅、DB/RLS/事务、真实PLM/OA/VER/MES消费、包恢复/发布及各仓remote。全部继续准确保持ADOPTION_PENDING/NOT_RUN/UNPROVEN。局部窗口代码交付不关闭WP01-01；只在正式依据、门实现、文档状态、必要本地回归与remote包含性全部满足后关闭整任务，不能连带关闭WP01整包或ITER/REL业务目标。
