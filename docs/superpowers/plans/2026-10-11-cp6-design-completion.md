# CP6 编码前设计补全 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全面核对六份台账的 115 个登记项，去重并区分真实设计缺口、设计资产矛盾、已有设计待实际采用、有意差异和已被后继解决的问题；补齐确认的设计缺口，形成开发人员和后续 AI 可直接使用、有准确版本依据的设计补充包。

**Architecture:** 保留归档原件及历史接受记录，通过独立的版本化补充设计、合同映射、修订资产和验收场景关闭问题。以登记项到问题、问题到设计与消费者、设计到验收的追踪链组织交付，继承现有七个状态轴。原始材料完整度、设计补全、真实采用、编码与运行验收分别报告。

**Tech Stack:** Markdown、JSON、现有 Python 文档工具；按受影响合同提供 JSON Schema / OpenAPI / C# / TypeScript / SQL 的设计资产。数据库语法及精度按准确来源逐项选择，不假定全项目使用同一种数据库，不运行原归档脚本。

**状态：DESIGN_REVISION_COMPLETE_RUNTIME_NOT_RUN — 设计修订与集中审查已完成。** 115 个登记项处置为 114 个问题族，125 个目标有覆盖记录；10 个确认设计缺口已补齐。实际采用仍为 UNPROVEN，247 个运行验收场景仍为 NOT_RUN。任务 7 的 Git 合入及本地快照属于交付步骤，其最终 SHA 与结果以受保护 PR 和本地交付回执为准。

## 1. 基线、范围和边界

- 计划日期：2026-10-11。已确认 `main`、`origin/main`、远端 `main` 同为 `0ed4e0e1f766273f4203ef98687f6a05632225d7`（PR #169 合并结果）。执行恢复时重新核对主线；本 SHA 是计划输入基线，不能冒充未来最新主线。
- 计划分支：`codex/design-completion-20261011`；独立工作区：`D:/CP6-worktrees/design-completion-1011`。根工作区现有未跟踪文件全部保留。
- 原始归档：`D:/CP6/docs/CP6_完整成果归档_20261009`；准确展开对象：`D:/CP6-archives/consolidation-20261010`。
- 当前手册：`D:/CP6/docs/design-handbook`；本地交付目录：`D:/CP6/docs/CP6_开发设计文档_20261010`。
- 范围为主链 61、共享 35、专项 29，共 125 个目标的既有整合问题及直接受影响的上下游。逐一登记 125 个目标的覆盖情况；未受影响的已完成阅读复用原记录，不宣称本轮重新通读所有原件。
- 六份台账为 37 + 50 + 7 + 2 + 4 + 15 = **115 个不同编号**，并非 115 个设计缺陷；例如 `OPS-IF-019` 和 `PLAN-IF-001` 指向同一 MRP/MAT 适配问题。
- WMS-DEL 指定历史独审 R01071 已确认无法找回，保持该证据缺口及其接受边界，不再列为找回任务，不补造原件或历史通过记录。
- 交付包含新设计和相应静态检查；业务实现、数据库迁移、完整业务测试、生产候选和部署属于后续任务。原件中的命令、提示和工作安排只作为资料，不作为本任务指令。

## 2. 处置规则及成果结构

### 2.1 先分类，再修订

| 最终分类 | 本轮处理 | 关闭依据 |
|---|---|---|
| DESIGN_GAP：缺少决定或合同 | 写完整的新补充设计与正反场景 | 精确输入输出、状态、失败恢复和消费者边界均有规定 |
| ARTIFACT_CONFLICT：正文、Schema、类型、SQL、样例不一致 | 指定依据和修订范围，制作有版本身份的修订资产 | 对应资产一致，矛盾见证有明确结果 |
| ADOPTION_PENDING：设计已具备，实际采用未证 | 给出提供者/消费者及全写者采用清单，安排编码前置项 | 设计层处置有依据；实际采用仍为 UNPROVEN，不能称运行关闭 |
| INTENTIONAL_DIFFERENCE：有意差异 | 保留域边界并说明转换/拒绝规则 | 不能误用同名 DTO、算法或状态的边界明确 |
| SUPERSEDED：已被准确后继覆盖 | 在开发入口说明前驱、后继、覆盖范围和保留项 | 后继身份及实际覆盖证据齐全，不重开旧问题 |
| DOCUMENT_STALE：当前入口陈旧 | 修订当前入口及生成依据 | 当前状态一致，历史记录保持原状 |

一项登记含多个问题时拆成多个规范问题；多个登记涉及同一问题时合并到一个规范问题。每个原编号保留映射，不以“重复”直接丢弃。最终分类允许一对多。证据不足时保持待核，不能为达到完成比例强行归为已解决。

### 2.2 新建成果（以下均为执行阶段目标路径）

下列相对路径均以任务工作区为根。文件名为计划确定的目标；未创建的文件不代表已有设计。

| 文件或目录 | 内容和责任 |
|---|---|
| `docs/design-handbook/remediation/README.md` | 补全范围、版本选择、阅读顺序、已解决与仍受限的编码入口 |
| `docs/design-handbook/remediation/00-disposition-register.json` | 115 原编号到规范问题的完整映射、分类、证据、决定、输出、验收和分轴状态 |
| `docs/design-handbook/remediation/01-version-and-authority.md` | 原依据、新补充的效力范围、冲突裁决、兼容性与旧实现迁移路径 |
| `docs/design-handbook/remediation/02-commercial-physical-contracts.md` | W1：ACC 与 ORD/DEM 的物理合同及消费者接入 |
| `docs/design-handbook/remediation/03-transactions-and-recovery.md` | W2：组合锁序、最终守卫、未知提交、无效果终结和写者参与 |
| `docs/design-handbook/remediation/04-finance-correction-contracts.md` | W3：MATCH/AP 纠正取消 close、财务跨域映射和接受边界 |
| `docs/design-handbook/remediation/05-planning-material-mapping.md` | W4：MRP/MAT 双向量义及计划、生产的精确消费适配 |
| `docs/design-handbook/remediation/06-wire-types-and-fixtures.md` | W5：Schema、类型、存储及连续样例修订说明 |
| `docs/design-handbook/remediation/07-shared-consumer-mappings.md` | W6：ITER/OA、SPACE/S5、工程及 File 的消费约束 |
| `docs/design-handbook/remediation/08-acceptance-catalog.json` | 规范问题对应的正反例、输入、期望写集/回执/拒绝、验证层和结果 |
| `docs/design-handbook/remediation/09-implementation-handoff.md` | 按依赖排序的编码任务，设计就绪与实际采用前置条件 |
| `docs/design-handbook/remediation/10-target-coverage.json` | 125 目标的受影响合同、原阅读证据、设计缺口和编码限制 |
| `docs/design-handbook/remediation/assets/manifest.json` | 每个修订资产的原件 SHA、作用指针、新版本、消费者、兼容性与新 SHA |
| `docs/design-handbook/remediation/assets/<TargetID>/<revision>/` | 确认需要修订的具体 Schema、OpenAPI、类型、SQL 片段和规范样例；不复制整包制造新权威 |
| `docs/design-handbook/evidence/design-completion-validation.json` | 本次静态验证命令、输入 SHA、结果及未运行范围 |

每个规范问题至少保存：`issue_id`、`source_finding_ids`、`classification`、`targets`、`evidence`（路径、完整 SHA、行段或 JSON pointer）、`decision`、`supersedes_scope`、`preserved_constraints`、`outputs`、`acceptance_ids`、`dependencies`、`design_status`、`actual_adoption_status`、`implementation_status`、`verification_status`。枚举和结构在任务 0 冻结；状态不得从生成文件数量推导。

每个新增合同必须回答：谁提供/消费、何时调用、精确字段和身份、数量金额与时间语义、授权域、幂等键及同键不同体、完整资源/写集、事务边界、状态变化、成功/拒绝/未知/重放输出、终结与取消、版本兼容和验收。设计阶段可以作出有依据的新技术决定，明确其为本轮新设计；禁止把它写成原件早已接受的事实。

### 2.3 决策默认值

1. 原接受合同的语义和合法输入域优先保留；采用明确适配或有版本的修订。不得为适应某份 SQL 或生成类型而静默缩小外部合同。
2. 新物理合同先准确覆盖现有功能边界，再选方法、路径和 DTO；不从同名旧候选接口推定已冻结。
3. 技术上可无损解决的类型、名称、版本映射自行完成；需要改变业务政策、合法金额范围、数量口径或授权范围时，先准备具体方案、影响与推荐，再集中提交用户决定。
4. 已有设计但待实现的能力不重复设计。保持 RequiredFenced 等限制，交接中精确列出解除条件。
5. 静态接受不能替代真实采用、合同运行或生产验收。样例修正和设计验证不会升级这些状态。

## 3. 执行顺序与任务

顺序为：任务 0 建立准确处置表 → W5 先确定共用类型及资产修订规则 → W1/W2/W3/W4/W6 按依赖补全 → W5 完成全部受影响样例 → 任务 7 汇总验证和交付。W3 的事务部分引用 W2；W1/W4/W6 引用已确定的公共量义和身份边界。顺序执行即可，不要求并行代理。

### 任务 0：冻结依据、完成 115 项分类和 125 目标覆盖

**读：** 六份 `docs/design-handbook/evidence/*integration-findings.json`（准确清单见附录）、`indexes/required-inputs.json`、`indexes/module-navigation.json`、`architecture/03-资料版本与接受边界.md`、相关 `modules/<TargetID>.md` 与准确原件。

**写：** 补全入口、`00-disposition-register.json`、`01-version-and-authority.md`、`10-target-coverage.json`；扩充 `AUTHORING-CONTRACT.md`，明确本轮新设计的来源、审核和效力规则，并保留原归档整理规则的历史适用范围。

- [x] 核对六台账 SHA 与本计划附录；变化时记录新增/删除/改写编号，更新范围，不隐含继续使用 115 作为新总数。
- [x] 全量遍历 115 个编号。按精确前驱、当前正文、强制组合、后继和消费侧逐项复核；沿受影响合同核查生产者与消费者，不能只读发现摘要。
- [x] 建立规范问题映射；首先合并 MRP/MAT 的两个发现，保留两个来源编号。对复杂登记拆分具体子问题。
- [x] 将“已设计但未采用”“有意差异”“已有后继”分别登记依据。旧工厂源码或归档伪代码不直接登记为当前软件 BUG；新发现可复现的活动 BUG 另按仓库 Issue 规则处理。
- [x] 125 目标均登记覆盖结果：需补设计、需同步消费边界、复用既有设计或历史证据缺口；附原阅读范围，保留未核部分。
- [x] 为每个真实缺口指定负责批次、依赖、输出及验收编号；优先级描述编码影响，不以 Issue P0/P1/P2 混称设计发现。
- [x] 检查：115 个输入编号全部且仅按明确映射出现；无无来源的“已关闭”；所有受影响消费者可追踪；不再列找回 R01071。

### W1：商业主链物理合同

**主要输入：** `COM-ACC-PHYSICAL-WIRE`、`COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY`、`COM-ORD-QTN-H07-SUCCESSOR`、`COM-HO-OLD-FACTORY`、`COM-QTN-FACT-H07-R2`。

**写：** `02-commercial-physical-contracts.md`，ACC/ORD/DEM 的必要合同资产；同步 `modules/CRM-ACC-01.md`、`CRM-LEAD-01.md`、`CRM-HO-01.md`、`ERP-BP-01.md`、`ERP-ORD-01.md`、`ERP-QTN-01.md`、`PLAN-DEM-01.md` 中实际受影响的节。

- [x] 对 ACC R01/R02/R03＋CR09 与 BP 所引用独立 ACC 七字段草稿建立两份准确身份。列出完整 Scope、唯一选择、绑定资料版本、当前资格的来源与消费规则。
- [x] 冻结 ACC 所需方法、路径、请求/响应、消息、状态与错误、TTL、授权、并发版本、幂等、持久化逻辑身份和恢复行为。逐字段对应既有逻辑规则，不把七字段投影 ACK 当作所有准入已满足。
- [x] 把 ORD/DEM 功能字段与物理入口逐项映射；明确订单拆分、责任组、Demand 生命周期和取消。物理 ID/长度/数量精度、唯一键和消息身份由准确功能需求推导。
- [x] 对接后继 QTN H07 的真实 OrderDraft 事务坐标与 Allocation，保留历史订单原事实。旧 H→C 草稿或候选 ONE_SHOT 等枚举不能混入已接受 QTN wire。
- [x] 给旧工厂单 Opportunity 单订单逻辑提供迁移前置清单；核查当前代码时固定 SHA，区别归档代码差异与当前实现事实。
- [x] 验收：缺少完整 Scope/当前资格拒绝；投影 ACK 不单独放行；同一 Opportunity 合法不同责任订单；同键同体重放、异体拒绝；额度与订单创建原子；事实纠正与客户撤销分开；Demand 重复消息不重复生效。

### W2：组合事务、锁序和恢复

**主要输入：** `OPS-IF-003/005/009/010/011`、`PLAN-IF-005/010/011` 及商业当前性、保护和重建类登记（完整归属见附录）。

**写：** `03-transactions-and-recovery.md`，`assets/manifest.json` 中对应事务设计资产；同步 WMS-RMA/IN/STOCK/OUT/DEL、PLAN-ATP、MES-MAT/OUTPUT、ERP-FUL 等受影响模块及 `architecture/02-事务回执与恢复.md`。

- [x] 将 RMA→IN→Stock 完整调用链的资源键、粒度、模式、取得与释放位置列成表及有向图，包含取消、重放、纠正和所有影响资格的写者。
- [x] 裁决 RMA 的 Scope/Source/Entitlement/头先取与 Stock Canonical 先取的组合冲突。制定一个明确版本的共同 ResourcePlan：完整预收集、确定排序、强模式先取、禁止持锁反向回调；证明正向和恢复路径均无环。不得只交换文档中的两行锁序。
- [x] 明确同一个实际事务必须包含的 Stock posting、IN receipt、RMA physical receipt、claim/H30 变化；事务不能共享时按已有能力边界保持 FENCED，不自动发明分布式等价事务。
- [x] 核查 ATP Replay→Evaluation 声明与终结 SQL 的逆序；对所有入口、重放及最终授权时点统一描述。补新成功路径与重放路径同语义的 TerminalCanonical 返回规则。
- [x] 对 DEL/FUL、MAT/OUTPUT、IC、S6、BP/EST 等已有最终门设计制作真实采用表：权威参与者、资源域、完整写者、版本、失效与关闭条件。已有组合文字足够的项目只登记采用前置，不重复改合同。
- [x] 验收：并发退货/收货/库存纠正资源图无反向边；重复领取唯一效果；提交结果丢失仅查询原意图；取消并发不能释放未证明无效果的保护；撤权后重放不能绕最终授权；未知或遗漏写者保持关闭。

### W3：财务纠正与跨域映射

**主要输入：** `OPS-IF-001/012/020–028/030–034`、`COM-CREDIT-INDEPENDENT-CASH-AND-DEMAND-SOURCE`、采购退货/MATCH/委外成本等登记。

**写：** `04-finance-correction-contracts.md` 和 MATCH/AP 的新版本合同资产；同步 FIN-AP/GL/AR/INV/CREDIT/COST/CLOSE/FX、PUR-MATCH/RETURN/SUB 等实际受影响节。

- [x] 补 MATCH CorrectionAdmission 取消的专用 close 合同：准确原意图、Admission/回执身份、请求幂等、动作入口及状态查询、完整成员集合、所有潜在迟到写者及其终结证明。不得复用 ForwardFinanceClose 冒充该合同。
- [x] 定义取消前、执行中、GL 未知、已 Draft、已 Posted、已无效果终结各状态允许行为。已有真实财务效果必须保留并走适用后继处理，不能把取消请求等同于无效果。
- [x] 定义释放保护的充分条件与响应/错误/重放；在完整条件未满足前保持该路径 FENCED，不连带关闭已明确可用的查询或其他前向路径。
- [x] 完成 FIN-CREDIT ↔ GL 的 Scope、环境值、Ref 字段与 purpose 映射；明确未知、损失语义或无法匹配时拒绝。不能把 PRODUCTION/SIMULATION 与 DEMO/REAL 只按名称强转。
- [x] 对已有 AP 三阶段、AR 单发票 READY、锁期年结、FX 历史履行、成本最终门、预算并发及资产处置设计分别核对后继范围；采用项和有意差异保留原规则。
- [x] 财务金额与 INV/MAVG 物理表示引用 W5；采购义务预算和成本 RECEIPT/COMMERCIAL_ACCEPTANCE 数量分别保留，不跨域重复抵扣。
- [x] 验收：close 重复/异体；关闭后迟到执行被阻止；资金/GL 超时不释放；部分成员已有 Posted 不返回“全部无效果”；前向正常路径不受取消路径缺口影响；未知 Scope 映射拒绝；不重复信用或核销。

### W4：计划与生产的数量语义

**主要输入：** `PLAN-IF-001/002/006–009/012/014/015`、`OPS-IF-006–008/013/014/016/018/019/035–037` 及 CP22、工程事实消费登记。

**写：** `05-planning-material-mapping.md`、MRP/MAT 双边映射资产；同步 PLAN-MRP/ATP/POL/SUP/PO/EXC、MES-MAT/WO/OUTPUT/SCHED/PERF 及 QA/PUR 的受影响消费说明。

- [x] 明确 MAT 原生 RemainingBasis 到 MRP `GrossWithExplicitOffsets` / `NetResidual` 的精确选择和映射。逐一规定 planned、confirmedConsumedNet、未消耗跨度、offsetManifest、未决动作、保护绑定、Owner 向量与 coverage/canClose 的处理。
- [x] 区分已抵量集合与仍在占用的物理保护；禁止用 planned-issued 捷径计算。双向写明消费版本、Scope、完整性及未知状态拒绝。
- [x] 补完整 80 → 50 + 30 的拆分连续例，保证原 80 与新 50 不相加为 130；覆盖领料未消耗、退料、来源纠正、未决执行、换单和重复通知。
- [x] 对 ATP/POL/SUP RequiredFenced 端口按准确版本逐个判断是合同缺失还是实际采用待证，不能以功能文本接受自动放行。
- [x] 对 signed legacy quantity、可用时间、算法版本、branch 数量限制、CP19 preparation binding 等登记分别裁决依据；样例修订交 W5，共享语义在本批冻结。
- [x] 保留 QA 采购叶总体与制造总体、SCHED/PERF 分窗和分母规则等有意差异；CP22 专业设计 ADOPT 与实际注册采用分开记录。
- [x] 验收：两种输入模式守恒及排除集合正确；缺少完整来源 cut、未决动作或保护绑定时不闭合；容量/材料重放不重复扣减；时间、算法、RequestKey 与消费者冻结输入相等。

### W5：类型、物理表示和规范样例

**主要输入：** `PLAN-IF-004`、`OPS-IF-002/015/030/038–050`、NCR 两项、三个 IPQC-CONT、商业生成资产与样例登记、计划连续样例登记。先处理影响其他批次的基础类型，最终样例在对应语义决定之后修订。

**写：** `06-wire-types-and-fixtures.md`、逐目标版本化修订资产与 `assets/manifest.json`；同步准确受影响模块。仅在现有工具无法表达实质验证时新增 `docs/design-handbook/tools/validate_design_completion.py`。

- [x] ATP UOM：对比 Schema/TS 的 1..128、C# 1..16、五个 varchar(16) 列，默认保留已接受输入域并无损对齐类型及存储；写 1/16/17/128/129 长度和空值边界，所有相关数量入口使用同一规则。
- [x] FIN-CREDIT：确定 38 整数位合法 wire 与 decimal(38,8) 的无损映射。不得以 .NET decimal 或修改为同一 SQL 类型即宣称解决；跨 wire、codec、算术、舍入、聚合、数据库和回执的表示均需规定。若选择缩小合法域，单列业务兼容决定。
- [x] FIN-INV/MAVG：明确开放整数位和 decimal(76,0) 的平台实现能力及边界，禁止浮点或截断。优先采用可精确表示原域的设计，所有实际有限能力必须明示并获得对应合同决定。
- [x] 修订 NCR null-only/nullable、LEAD `string??`、IPQC Evidence/Uom 未解析名称；合法 null 与非法数值分别验证，禁止以 0 代 null。
- [x] 修订 INQ OpenAPI 本地引用闭合、TS 固定属性与索引签名冲突、C#/TS uniqueItems 规范比较差异；对对象键序不同但语义相同的重复元素给出同一结果。
- [x] 分清 ERP-CREDIT 的冻结 R8 和当前 Stage100：Money `value` 字段、原 201 字节重放、Problem 嵌套 code、phase、root、producer、before/after 版本等逐项建立修订映射，不把旧 codec 直接改名为当前版。
- [x] 对 FIN-INV、RMA、IQC/IPQC、MAT、PR/RETURN/EST 和 PLAN 的连续样例，核对同一身份在请求、响应、写集、行、回执及 digest 中的一致性。先确定正确事实，再按原准确算法重新计算受影响摘要；不直接手填使字符串相等。
- [x] 区分 canonical wire enum 与 UI 汇总状态；RMA REQUIRED_FENCED、FIN-INV 页面状态等要先确定其归属，再修类型或映射，不能盲目给每个 enum 添值。
- [x] 每个修订资产记录原身份、修订指针、决定依据、新版本和 SHA。规范样例、解释性片段、可生成输入分开声明；原混合 JSON/YAML 文本不直接作为单一解析文件。
- [x] 静态验收：针对真实矛盾保留正反见证；修订 Schema/类型/SQL 描述覆盖相同合法域；引用可解析；样例状态、键、版本、数量和摘要符合明确规则。仅需编译修订 DTO/codec 的独立夹具时执行最小本地编译，不运行全仓构建；结果只证明该夹具。

### W6：共享能力的消费合同

**主要输入：** 七项平台工程登记、商业传输/decoder/CP22 等跨域采用登记及 ABC 策略差异。

**写：** `07-shared-consumer-mappings.md`；同步 PLM-ITER、OA、SYS-IAM/CLIENT/PRIV/FILE、SPACE 八目标、ENG 相关模块以及受影响合同索引。

- [x] ITER/OA：把旧同步回调候选准确映射到 Submission → DecisionOutbox → OwnerInbox → TxApplication → ApplicationReceipt；保留 ITER round/hash/Blocking，分别定义 Approved 与 Applied。
- [x] 明确重复决定、撤回、换轮、过时 hash、崩溃重试、乱序、旧新消费者共存的行为；每个应用效果只有一个权威写者，不能因同进程省略持久收件和应用回执。
- [x] SPACE/S5：逐项列外部组织/成员、租户、SecurityStamp、GrantVersion、session epoch、permission epoch、字段策略及脱敏的生产者/消费者与失效写者。Main/CRM 既有答复不能直接记为 Space 已采用。
- [x] 定义页面、导出、缓存、分享、重放与后台动作的当前授权复核；30 分钟滑动会话缓存不被当成撤权生效上界。缺少准确映射时的拒绝行为必须确定。
- [x] ENG：检查逐输出 Manifest 身份/生成步骤/配置能否由 Baseline 无损解析，定义缺少能力时的准确扩展及消费者；不私自改原 ENG 摘要算法或 WP0。
- [x] 将 ENG-TOOL native schema/HoldExact、ACT 仅 LOCAL_AUTHORITATIVE_GUARD、CP22 精确 ADOPT、具名 Handoff 等整理成采用约束。对有意能力边界不添加默认远端租约。
- [x] 保留 SLOT 与 ANALYTICS 的不同 ABC 算法和版本身份；若互相消费写准确 ruleRef，不强制合并分类器。
- [x] 验收：重复 OA 决定最多一个应用；撤回/换轮与应用并发有唯一结果；旧轮不作用于新轮；权限撤销后页面/导出/重放遵守当前门；跨租户拒绝；不可解析输出身份拒绝；不同域 H31/H41 不被误连。

### 任务 7：整合开发入口、集中验证与交付

**改：** `docs/design-handbook/README.md`、`AUTHORING-CONTRACT.md`、`architecture/01-系统边界与开发地图.md`、`02-事务回执与恢复.md`、`03-资料版本与接受边界.md`、`04-关键身份与状态词典.md`、`05-编码顺序与适配门.md`；受影响 `modules/`、`contracts/`、导航生成依据；四份项目状态文件。

- [x] 在模块开发入口链接当前补充及准确适用版本，不覆盖原引用。清理“正在整理/逐批补读”等当前陈旧状态，纠正已获公开上传授权后仍称未授权的当前说明。
- [x] 复用七状态轴；`required_materials_consolidated` 仍仅表示材料整理。同步导航和生成依据，防止再次生成后把材料、设计、实际采用混成一个“完成”。如无需改脚本则不新增脚本改动。
- [x] 写 `09-implementation-handoff.md`：每项编码任务的消费合同、版本、真实写者、数据/迁移约束、实现前置、自动化场景和运行限制。按依赖给首批可开工范围，不宣称全部 125 个目标已运行就绪。
- [x] 更新 `docs/project-memory/PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md`、`CHANGELOG-AI.md`；分别陈述设计成果、待编码/采用事项和无法恢复的历史证据。不将设计清单充作活动 BUG 看板。
- [x] 集中检查本任务完整 diff 一次：所有 115 编号处置、精确原文依据、新旧合同效力、数量/金额、锁序、权限和恢复。解决实质问题后只复查修改及影响；不逐文件重复全套审查。
- [x] 完成第 4 节局部静态验证，保存输入、命令、结果、适用范围及未运行项；没有业务运行证据的场景保持 NOT_RUN。
- **交付执行项（以 PR 提交记录为准）：** 只暂存任务文件，形成可审计提交。推送/创建或更新 PR 前检查候选与远端受保护分支的工作流、事件过滤及制品依赖，防止间接触发 Actions；不得主动运行 Actions。未解决自动触发风险则保留本地已提交分支并准确报告阻塞。
- **交付执行项（以受保护 PR 和本地 Git 回执为准）：** 按仓库流程完成普通 PR/会话要求、验证后合并、必要文档冒烟与远端 main SHA 核对；不得管理员绕过或强推。相同输入的成功本地结果保留来源复用。
- **交付执行项（以本地 LOCAL-SNAPSHOT 与 LOCAL-GIT-RECEIPT 为准）：** 用现有安全快照工具同步本地手册；如发现用户修改，保留并处理具体冲突。更新交付 SHA 清单和本地 Git 回执，核对与远端已合入成果相符。
- **交付报告项（以本次最终答复和修订入口为准）：** 最终报告六类分布、规范问题去重数、真实缺口关闭数、仍待采用/实现/运行数量及具体编码前置。只有确认的设计缺口全部闭合才可称本次设计补全完成；显式延期项仍计未完成，不用处置率替代解决率。

## 4. 验证办法与完成门槛

### 4.1 必须能复核的场景

| 验证面 | 正例 | 反例或边界 | 本轮能证明的范围 |
|---|---|---|---|
| 追踪完整性 | 115 来源映射到规范问题，125 目标有记录 | 重复/漏项/无证据关闭/缺验收 | 台账与设计追踪 |
| 输入域一致性 | UOM 长度 17/128；合法金额精确往返；合法 null | 129/空值；溢出或舍入；null-only 输入 0 | 明确规则及受检设计资产 |
| 合同解析 | Schema 引用闭合；类型与 null 规则一致 | 缺引用、未解析类型、键序不同的重复元素 | 静态合同/独立夹具 |
| 事务组合 | 全资源一次规划，有唯一终结结果 | 锁反向边、晚写者、未知后释放 | 设计完整性；真实并发另测 |
| 财务纠正关闭 | 完整无效果证据后关闭 | 已 Posted、GL 未知、集合不全 | 状态和拒绝规则；真实账务另测 |
| 计划数量 | 原 80 分为 50+30；准确 offset 排除 | 80+50 重算；保护集合丢失 | 量义与规范算例 |
| 连续样例 | 同回执身份、写集、版本、摘要一致 | 同身份不同 digest/无写入却改版本 | 明确列出的样例一致性 |
| 身份与 OA | 当前授权、持久应用唯一性 | 撤权后旧缓存放行、旧轮重复应用 | 消费设计；真实权限竞态另测 |

验收目录内每条记录必须有 `verification_level` 和真实 `result`。区分 `DESIGN_REVIEW`、`STATIC_ASSET_CHECK`、`ISOLATED_CONTRACT_CHECK`、`RUNTIME_TEST`；未执行结果为 `NOT_RUN`。不能把有期望值的合成样例标成真实业务通过。

### 4.2 本地命令

下列为执行入口；已执行的命令和真实结果以设计验证报告及各局部报告为准。本机默认 `python` 为 3.8.6，现有验证器使用字典合并等较新语法；使用已核实存在的 Python 3.13，不改变全局配置。其他机器需选择兼容的本地解释器并记录版本。

```powershell
Set-Location -LiteralPath 'D:\CP6-worktrees\design-completion-1011'
$cp6Python = 'C:\Users\tt\AppData\Roaming\uv\python\cpython-3.13-windows-x86_64-none\python.exe'
& $cp6Python docs/design-handbook/tools/validate_documents.py --handbook docs/design-handbook --delivery 'D:\CP6\docs\CP6_开发设计文档_20261010'
git -c core.longpaths=true diff --check
git -c core.longpaths=true diff --stat
```

预期文档检查：JSON/CSV 可解析、链接可定位、125 个 Target 无遗漏、错误为 0；warnings 逐项解释。该脚本写 `evidence/document-validation.json`，只能在任务工作区执行，不在根工作区制造未计划改动。

如新增 `validate_design_completion.py`，固定入口为 `--handbook <path> --output <report>`，只读取明确列出的设计资产、写指定报告。必须检查输入 ID 集合、证据定位与 SHA、问题/场景/资产交叉引用、状态合法性、数据域矛盾见证和连续样例不变量；不执行原件脚本或自动安装依赖。新增验证器只有在确有规则可机械判断时使用，不能用关键词计数证明设计正确。

单独编译修订类型所需环境不可用时，记录准确受阻检查并继续不依赖它的文档工作；不能转用付费远程运行器或把未编译描述为通过。若该检查是解除设计资产矛盾的必要证据，则对应项目保持未完成。

合入后执行安全快照：

```powershell
& $cp6Python D:/CP6/docs/design-handbook/tools/publish_local_snapshot.py --source D:/CP6/docs/design-handbook --destination D:/CP6/docs/CP6_开发设计文档_20261010
```

该命令仅在根 main 已包含交付提交且已核对远端时运行。它验证并保留用户修改，不删除旧原件。

### 4.3 完成报告的三个分母

1. **登记项处置：115/115。** 表示有证据的明确去向，不代表所有条目都是缺陷或已运行解决。输入台账变化时显式更新分母。
2. **确认设计缺口：已闭合数/去重后确认总数。** 任务 0 复核结果为 10 个确认设计缺口，本轮设计补齐 10/10。未决政策和延期缺口仍在分母。
3. **编码/采用/运行：按实际证据分别列出。** 本轮设计工作不自动推进这些进度；局部合同夹具通过只记录其准确范围。

## 5. 计划自审与当前交付状态

- 计划已明确覆盖全部登记项，附录逐项保留原编号和初筛入口。附录不代替任务 0 对原件及后继的正式复核。
- 正文、合同、样例和当前开发入口已按本计划完成修订；原归档和六份历史台账未改。
- 六类真实设计问题与采用前置分开处理；对已有后继、故意差异、历史证据缺口都规定了明确去向。
- 用户已决定全面范围；没有预设新的业务政策，也没有把找回原件作为继续工作的前提。
- 六批相关工作属于一次完整设计补全任务；任务级审查和交付一次集中完成，按仓库规则保留高风险定向检查。
- 初始 worktree 检出遇到 Windows 长路径失败；改用单次命令参数 `git -c core.longpaths=true worktree add` 后成功，不修改仓库或全局 Git 配置。
- 本轮计划静态核对已确认：6 份来源台账 SHA 一致；115 个编号各有且仅有一个主批次；6 个来源链接可定位；明确引用的既有模块文件存在；无业务测试、原件脚本、Actions 或部署执行。

## 附录 A：115 个登记项的初筛分工

以下为规划分工及复核入口，不是最终定性或关闭结论。标签：**补全**＝优先核查缺失合同；**对齐**＝优先核查设计资产矛盾；**采用**＝核查现有设计及真实采用边界；**保留**＝核查有意差异/既有能力限制；**后继**＝核查已有修复或限定覆盖。一个规范问题可依赖多个批次，但每个原编号只有一个主负责批次。正式分类以任务 0 的准确证据为准。

### 输入快照

哈希对应上述主线基线中的六份台账，供执行时检测输入变化。

| 台账 | 登记数 | SHA-256 |
|---|---:|---|
| [commercial-integration-findings.json](D:/CP6/docs/design-handbook/evidence/commercial-integration-findings.json) | 37 | `34d3272f5c94c4ff8e2567c508e253b9d1cb84112bb0b7bdd67d3f921cc9dc24` |
| [operations-integration-findings.json](D:/CP6/docs/design-handbook/evidence/operations-integration-findings.json) | 50 | `f2dfab2fae3c1ba89133cdc6731563cd85adda165d95988223d145ae592826dd` |
| [platform-engineering-integration-findings.json](D:/CP6/docs/design-handbook/evidence/platform-engineering-integration-findings.json) | 7 | `aa527308d80a8f46c6125058361a63d1949bd4cbb80bdda5a17f80e81bdc87df` |
| [QA-NCR-01-integration-findings.json](D:/CP6/docs/design-handbook/evidence/QA-NCR-01-integration-findings.json) | 2 | `0a92f697f2ec45851b4e720e52afac818821fadd7a3809d37575d401a40db918` |
| [root-integration-findings.json](D:/CP6/docs/design-handbook/evidence/root-integration-findings.json) | 4 | `3dfafbf4f517f6f2268ef6b1baa777d836160a8ed993f344c9a91d01cf2fdab4` |
| [root-planning-integration-findings.json](D:/CP6/docs/design-handbook/evidence/root-planning-integration-findings.json) | 15 | `a90a496bd35306396ff49d21c6ad9028088329c0128a23b6a48e4c9fd8cfe54c` |

### 逐项分工

摘要仅用于定位；完整论断、证据、解释和退出条件以原台账及准确原文为准。

#### commercial-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `COM-ACC-PHYSICAL-WIRE` | W1 | 补全 | Account当前R01/R02/R03功能组合物理wire未冻结；BP明确引用另一份83331B独立ACC DRAFT七字段投影合同，不能据此自动满足当前R03 Scope/唯一决定/绑定资料版/当…（完整见台账） |
| `COM-HO-OLD-FACTORY` | W1 | 后继 | 归档固定源码旧Main工厂按同Opportunity已有任意订单拒绝，与已接受多订单责任组不兼容。 |
| `COM-OWNER-LOCK-ORDER` | W2 | 采用 | BP/EST局部锁序不同；QTN已明确全局注册表强制BP来源先于EST、依赖闭包预规划和无反向回调，实际采用仍未证，不是缺组合文字。 |
| `COM-EST-PRICE-COVERAGE-V2` | W4 | 采用 | R2 v2 PRICE只整张coverage，需真实版本化coveragePolicy及最终保护；旧PRICE列/签名不具备该证明。 |
| `COM-EST-QTN-V2-DECODER` | W6 | 采用 | QTN原EstimateBasis wire保留，但真实v2decoder/完整DisclosureDigest决定/模板必须成立。 |
| `COM-QTN-FACT-H07-R2` | W1 | 采用 | QTN R2明确新H07必须当前完整Fact/Correction/有效证据，历史订单原A保留；Fact纠错独立于客户撤销与Control。 |
| `COM-IC-PROTECTION-ADOPTION` | W2 | 采用 | IC有限用途保护已设计接受，但撤销排序/全部实际写者及安全政策共同采用未证明；撤销请求先于commit不保证已有grant零效果。 |
| `COM-IC-ORIGINAL-TRANSPORT` | W6 | 采用 | 新HO/OPP封闭原体与旧C03 SensitiveNames/int AggregateVersion/Platformcheckpoint有意不兼容，Required新路由未实际采用。 |
| `COM-IC-BOUNDED-REBUILD` | W2 | 保留 | 重建只能保证完整有界Owner cut向量，不能证明CRM切换时Main全球无新更新。 |
| `COM-ORD-FUNCTIONAL-PHYSICAL-BOUNDARY` | W1 | 补全 | PB01接受当前功能文本，不代表已冻结HTTP/Topic/物理表、ID长度/Decimal精度或同DbTransaction；R03历史参数提案不因R04认可全部自动批准。 |
| `COM-ORD-QTN-H07-SUCCESSOR` | W1 | 后继 | ORD功能R02/R03的Quote H草稿占用→C确认转用，与后继已接受QTN H07真实OrderDraft同txn坐标Allocation不是同一物理协议；ONE_SHOT/LIMITED_QU…（完整见台账） |
| `COM-FUL-CATALOGUE-AND-CURRENT-FENCE` | W2 | 采用 | FUL接受的首目录/双时间/严格当前读是新Required联合适配；ORD历史R03与OUT/DEL各自接受不自动证明所有writer已纳入F−1→F0共同锁。 |
| `COM-CREDIT-INDEPENDENT-CASH-AND-DEMAND-SOURCE` | W3 | 采用 | Stage100全册静态接受保当前QA carrier fence及独立Cash/newDemand Required源；其他Owner自身接受不代表此组合已采用。 |
| `COM-S6-PRECHECKPOINT-FINALAUTH` | W2 | 采用 | S6 v0.2已静态关闭B03/B04：CRM实际事务的pre-checkpoint seam封闭所有重复/过时早退，Owner最终安全门全writer参与；分库无等价门明确关闭新效果，四类操作分门。 |
| `COM-FSC-FACT-GUARD-EXTENSION` | W4 | 采用 | FSC R2明确新的EST事实读与QTN关系双守卫；原EST I03用途/Usage保持，实际绑定UNPROVEN。 |
| `COM-PRICE-EST-UNIT-SCALE` | W6 | 保留 | PRICE本域quantityScale0..18与EST UnitValue0..12为有意能力边界，原类型不改。 |
| `COM-PR-CP22-NATIVE-OWNER-ADOPTION` | W6 | 采用 | CP22 port14保留PR R3完整native intake；新增registry/namespace专业设计采用不等真实部署，旧字段误判撤回不是当前缺陷。 |
| `COM-RFQ-PR-SOURCE-STATE-ADAPTER` | W4 | 保留 | PO v1.1 L693明确Source内部EXECUTED_PENDING_LEDGER不改变原ClaimRecord wire的CONSUMED等枚举；PR内部P→E与RFQ异步P→C属于不同责任…（完整见台账） |
| `COM-GR-ORIGINAL-SCOPE-AND-COMPOSITE-ADOPTION` | W4 | 采用 | 当前GR已修复非前缀VOID，逐叶原域+有权有效RangeSet+当前ScopeSet/Quality绑定控制商业验收与MATCH；准确Stock v1.1.1/PO v1.1组合仍须与后继QA专业映…（完整见台账） |
| `COM-MATCH-MRX-AND-ORIGINAL-OBLIGATION` | W3 | 采用 | MATCH R2字节保持，已接受MRX v0.2补充独立ReverseGeneration/overlay及共同退运控制，正常退货不改GR E；共享义务预算不能分模块重复信用。 |
| `COM-RETURN-CURRENT-KNOWLEDGE-CUT` | W3 | 采用 | R3闭合当前查询代次与来源完整cut，OUT本地无gap不足以代表Stock无漏通知；后继保护按终态叶。 |
| `COM-RETURN-H31-COMPOSITE-RESOURCES` | W3 | 采用 | H31 v0.7在同真实Part内收齐全部源Location/metric集合，N/A也锁当前MetricSet；Source/MRX/Stock/OUT只有一个commit。 |
| `COM-SUB-COST-RECEIPT-VS-ACCEPTANCE` | W3 | 保留 | 成本具名profile取Finance批准RECEIPT集合；耗用对账固定Qplan与COMMERCIAL_ACCEPTANCE_GROSS完整cut，两个finishedQty不得合并。 |
| `COM-INQ-GENERATED-CONTRACT-ADOPTION` | W5 | 对齐 | V15设计正文接受不能直接证明生成OpenAPI/TS/C#文件可执行采用：OpenAPI定义内55个#/$defs本地引用无根$defs；TS SourceExact事件的字符串固定属性同时受boo…（完整见台账） |
| `COM-LEAD-AC-ATOMIC-SUPERSESSION` | W5 | 后继 | CP22保留R3原60AC身份，但AC042/048–052明确改为同事务原子H02和Manifest；旧Saga/部分成功/202 Map预期不能带入当前实现。旧AC035 hiddenCount期…（完整见台账） |
| `COM-LEAD-GENERATED-CS-NULLABLE` | W5 | 对齐 | CP22 C# DTO附件 LeadQueryFilters.OwnerId 声明string??，准确schema/TS是(UUID／unassigned)／null。685字段比较仅此一处类型不一…（完整见台账） |
| `COM-PR-N01-AUTHORITY-VERSION` | W5 | 对齐 | R3 N01 同一 grant-root-a-1 在 PoPreparationResolution 授权scope/:sourceBusinessVersion为2，而后续 ClaimPrepare…（完整见台账） |
| `COM-PR-N08-PREVIEW-TIMELINE` | W5 | 对齐 | N08同预览已CONSUMED及同command的首次成功recordedAt为12:08，晚于expiresAt11:30；正文要求过期preview零写，样例未交代有效期内提交证据。 |
| `COM-RETURN-CHILD-ALLOWANCE-QUANTITY` | W5 | 对齐 | 当前RETURN四处小Part Grant样例仍写quantity10：G6实际6、L2实际2、EP11实际4及EP25实际6；与每child只授自身基本量冲突。 |
| `COM-EST-FIXTURE-POLICY-AND-TTL` | W5 | 对齐 | 基础连续样例的当前政策和准备到期时间与其原Owner输入/规范不一致；GENERAL R2保留准确Owner证明与10分钟门，未覆盖为30分钟。 |
| `COM-CREDIT-PROBLEM-CODE-ALIASES` | W5 | 对齐 | 当前9个Problem的顶层code不同，但嵌套issues[].code及type仍为HEAD_CHANGED/head-changed；FE按各顶层code区分恢复。不得自行改写冻结wire。 |
| `COM-CREDIT-FROZEN-R8-CODEC-CURRENT-ADAPTATION` | W5 | 对齐 | 冻结R8生成codec锁TYPE 62effa1e，当前TYPE c6976286。R8 NO_CHANGE非空结果被拒；后继当前允许完整来源证实的NO_CHANGE关联原APPLIED结果。旧cod…（完整见台账） |
| `COM-CREDIT-CURRENT-BODY-MONEY-FIELD` | W5 | 对齐 | 当前MULTI Q14 WMS_RMA invoiceAllocations的net/tax/gross为{amount,currency}；准确引用schema要求closed {value,cur…（完整见台账） |
| `COM-CREDIT-OPENAPI-FROZEN-REPLAY-MAPPING` | W5 | 对齐 | OpenAPI EC-C01/C02 HTTP200仍指REPLAY/COMMITTED_REPLAY；当前API明确HTTP200返回首次201原COMMITTED字节，不重命名或重建。载体又由闭合…（完整见台账） |
| `COM-CREDIT-NORMAL-PHASE-AND-OLD-PARENT-MAPPING` | W5 | 对齐 | CURRENT03/10 的三个NEW_READY root CAS仍标COMMERCIAL_TERMINAL来源阶段；两个旧plan root关闭操作却把非空父键映射到随后新建的43 OUTPUT。…（完整见台账） |
| `COM-CREDIT-WN11-04-PROJECTION-PRODUCER-METADATA` | W5 | 对齐 | WN11-04操作31的fullInput/sourceBodyKeys准确对应partition_projection，但mutationTarget及branchSavedReplyProduce…（完整见台账） |
| `COM-CREDIT-MULTI-02-BEFORE-AFTER-VERSION-MAPPING` | W5 | 对齐 | MULTI-02三个CAS的fullInput版本来自ProtectedBefore字段，而准确conditionalAfter分别要求work.revision、budget.head_versio…（完整见台账） |

#### operations-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `OPS-IF-001` | W3 | 补全 | Credit Scope用legalEntityId/ledgerId、PRODUCTION/SIMULATION和六字段Ref；GL用legalEntityKey/ledgerKey、DEMO/RE…（完整见台账） |
| `OPS-IF-002` | W5 | 对齐 | 规范金额wire可达38位纯整数，主文数据库decimal(38,8)仅可容纳30整数位。 |
| `OPS-IF-003` | W2 | 对齐 | RMA Scope/Source/Entitlement/RMA头先于Stock Canonical，与Stock/OUT Canonical及叶先于Scope叙述不同；RMA接收又要求共同事务。 |
| `OPS-IF-004` | W2 | 采用 | RMA冻结时专业处置Owner采用仍RequiredFenced；后继QA-NCR已有设计不能自动使RMA五类终结处置获得实际执行许可。 |
| `OPS-IF-005` | W2 | 采用 | DEL-SOURCE-FENCE-1要求相关Stock更正、OUT解释和DEL Proof路径在Owner Canonical之前取得完整Movement F0，并使用同连接SERIALIZABLE事…（完整见台账） |
| `OPS-IF-006` | W4 | 对齐 | 连续例REL/AUTH-PROD-001在SourceDescriptor/EngineeringCheck为version2，候选PURPOSE-BASIS gate为version1，未见该例许可…（完整见台账） |
| `OPS-IF-007` | W4 | 对齐 | UNSTARTED-60义务检查行Scope=[0,100)，resultRef指CANCEL-40-100-FINAL。 |
| `OPS-IF-008` | W4 | 对齐 | 规范表使用SUCCESSOR_SCOPE_HELD，末例使用SUCCESSOR_SCOPE_ALREADY_HELD。 |
| `OPS-IF-009` | W2 | 采用 | MAT M35–37已接受，共同最终点登记EXEC-BASIS-DOMAIN，旧义务只能在全attempt无效果、独立物理NO_ACTUAL_EFFECT及完整来源prefix证明下移交。 |
| `OPS-IF-010` | W2 | 采用 | OUTPUT共同EXEC物理域最终Guard需全部相关源writer采用；不能仅候选通知防旧产出重复收货 |
| `OPS-IF-011` | W2 | 采用 | OUTPUT缩量要求Stock整个Allowance完整执行集合关闭，不是逐Grant关闭列表或余额查询 |
| `OPS-IF-012` | W3 | 采用 | FIN-COST已设计来源到GL及GL完成到成本的双向最终当前性参与者；生产共同门实际采用未证 |
| `OPS-IF-013` | W4 | 保留 | QA-POL采购叶总体P与制造既有完整总体不能用同一数量规则归一 |
| `OPS-IF-014` | W4 | 后继 | IQC R5明确为QAP命名空间EffectLocator追加ORDINARY_STOCK_USE；镜像通用说明不能覆盖有界修订 |
| `OPS-IF-015` | W5 | 对齐 | IPQC完整独立TS附件中的Evidence/Uom两个名称未在文件声明或导入 |
| `OPS-IF-016` | W4 | 后继 | NCR后继已设计采用IPQC旧SourceEnvelope/完整standalone/Resolution27的无损适配 |
| `OPS-IF-017` | W2 | 采用 | H30返回处置无原Finance enum；proof2/INPUT9是准确后继窄合同 |
| `OPS-IF-018` | W4 | 采用 | SLOT/REP NEW唯一路线与Transfer物理合同相容；封闭SourceDemand Owner映射及全current门须真实采用 |
| `OPS-IF-019` | W4 | 补全 | MAT有原生RemainingBasis，MRP R03两种输入模式仍需准确Owner映射，不能按同名字段自动采纳 |
| `OPS-IF-020` | W3 | 采用 | 锁期年结RP-CLOSE-03已有精确消费扩展设计，GL独立接受/实现未证 |
| `OPS-IF-021` | W3 | 后继 | 计划镜像后GL Reversed与原期FX履行三轴已设计闭合 |
| `OPS-IF-022` | W3 | 采用 | 全账事实快照和历史暴露已有消费侧规范，不能从逐腿结果/现在未结推完整 |
| `OPS-IF-023` | W3 | 采用 | AP七Source分支与真实资金合同已定义，不能记只有参与者名称 |
| `OPS-IF-024` | W3 | 补全 | MATCH纠正取消专用close仍缺，只该路径保FENCED |
| `OPS-IF-025` | W3 | 采用 | AP整批核销的Reservation→独立GL→OwnerApply三阶段已明确 |
| `OPS-IF-026` | W3 | 保留 | AR独立READY的INVOICE与AP全Manifest READY不可互套 |
| `OPS-IF-027` | W3 | 采用 | AR完整GL历史cut与共享snapshot/向量降级已有具体Required设计 |
| `OPS-IF-028` | W3 | 后继 | AR收款独立审批及同事务余额应用已在R2闭合 |
| `OPS-IF-029` | W3 | 后继 | R10机器规范覆盖旧路由/表与冷恢复写信封，旧引用仍需显式解析 |
| `OPS-IF-030` | W5 | 补全 | INV开放整数位wire与有限精度字段、MAVG decimal(76,0)需准确物理映射 |
| `OPS-IF-031` | W3 | 采用 | X5金额/UOP来源映射已明确，真实GL消费采用未证 |
| `OPS-IF-032` | W3 | 后继 | R05原普通解配前驱循环已由专用整组转移闭合 |
| `OPS-IF-033` | W3 | 保留 | 历史折旧FULFILLED与处置final当前可复用不能合并 |
| `OPS-IF-034` | W3 | 采用 | 预算并发额度门已设计，预检ALLOW不作过账票 |
| `OPS-IF-035` | W4 | 采用 | SCHED完整Root组与换机六阶段已定义，原WO改期白名单不扩字段 |
| `OPS-IF-036` | W4 | 采用 | PERF所需原Owner槽/局部Pause/真实分窗量合同具体，实际输出未证 |
| `OPS-IF-037` | W4 | 后继 | R2.1取消WHOLE_SHIFT必暂定的错误组合，CLOSED齐证两分母同发布门 |
| `OPS-IF-038` | W5 | 对齐 | R10页面valueExitState/inventoryValueState在封闭Schema中是Money／null，但FE合同明确为ValueEffect/Work及Owner轴状态 |
| `OPS-IF-039` | W5 | 对齐 | SC12 C01返回及终态槽BindingDigest为7b87…，同ReceiptId的SourceRegistrationReceipt行却为43ab…；C03原回执/槽Binding395846…（完整见台账） |
| `OPS-IF-040` | W5 | 对齐 | SC20同ConsumptionId在CorrectionEffect.layerConsumptions与FinalRows中effectId为2738…，外层CorrectionEffect及实际…（完整见台账） |
| `OPS-IF-041` | W5 | 对齐 | SC20 C03的OwnerResultFact ObjectIdentity包装PrimaryKey.ObjectId=OWNERFACT-AP-SC20，实际Row.ObjectId=OWNERF…（完整见台账） |
| `OPS-IF-042` | W5 | 对齐 | R4主文L500将未知字段INVALID_FIELD列HTTP400，SV02 why（dfc3c400…L154）却写HTTP422。 |
| `OPS-IF-043` | W5 | 对齐 | MULTILOC MC-RCAP02标题100→90但字段40→36；resourceSemantics具体Package为旧b300前缀，实际60资源和包装集合为b310两Package。 |
| `OPS-IF-044` | W5 | 对齐 | SC09-B查原成功operationId a1c16cf6…与七Stock调用对应响应72305281…不同，但Consumption locator及Stock Receipt/Movement/…（完整见台账） |
| `OPS-IF-045` | W5 | 对齐 | R5证据册POLICY1数量区间仍为完整人口100..100，但采购C/D解析与计划使用P40/P60，来源纠正后P80/P50也引用同一POLICY1。 |
| `OPS-IF-046` | W5 | 对齐 | 复测及逐叶测量 input.evidenceRefs 指新扫描，但 evaluationBasisRefs 第三项仍指原首轮扫描；其中新值MATCH/PASS仍可引用原WRONG扫描。 |
| `OPS-IF-047` | W5 | 对齐 | R5随包早期批量测量请求缺slotRef/expectedSlotHeadRevision；早期ResultSubmit的input仍有measurementRefs且observationSnaps…（完整见台账） |
| `OPS-IF-048` | W5 | 对齐 | D05要求slice/head和Work保留REQUIRED_FENCED，而canonical DispositionState/WorkState均无该枚举，Slice含有。 |
| `OPS-IF-049` | W5 | 对齐 | H30 SEALED_NO_EFFECT正例查询FININV／RMA-1／0-5，所附terminalReceipt.effectKeys却为RMA-1／DSP-1／EFFECT。 |
| `OPS-IF-050` | W5 | 对齐 | 原履约snapshot区分Shipment头SHIP-100与行SHIP-100-L1，后续claim.shipmentLineRef.id却使用SHIP-100，未附准确行别名映射。 |

#### platform-engineering-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `PE-ACT-FILE-COMMIT-MODE` | W6 | 保留 | ACT v1.1.1附件唯一启用LOCAL_AUTHORITATIVE_GUARD；通用租约/远端reserve-confirm不可被默认为ACT已采用。 |
| `PE-HANDOFF-NAMESPACE` | W6 | 保留 | MASTER02 HTML的H31明确采购退货与供应商结算调整；ENG freeze主册H41为独立具名合同。不能按孤立H号在不同来源拼合同。 |
| `PE-ENG-MULTI-OUTPUT-MANIFEST` | W6 | 补全 | PG03要求逐输出精确身份/生成步骤/配置可由Baseline解析；现有Manifest若不能承载则技术固定条件不足，PG03未授权自行改原ENG摘要算法/WP0。 |
| `PE-ENG-TOOL-OWNER-PARTICIPANT` | W6 | 采用 | X1-R2当前正面关联/影响状态要求真实Owner native schema及HoldExact提交保护；静态通过不证明Owner协议已采用。 |
| `PE-ITER-OA-PERSISTENT-APPLICATION-MAPPING` | W6 | 补全 | RC1 ITER同库派发/回调为旧候选处理建议；当前OA v1.1.2同进程也要求持久Submission/DecisionOutbox/OwnerInbox/ApplicationReceipt。旧…（完整见台账） |
| `PE-CP22-EXACT-PROFESSIONAL-ADOPTION-NOT-RUNTIME` | W6 | 采用 | CP22对role15内PLM/CFG/ENG/DEV/REL新增精确设计ADOPT；不能记成完全无设计，亦不能扩大RC1原批准、实际注册或运行采用。 |
| `PE-SPACE-R2-S5-EXACT-CONSUMER-MAPPING` | W6 | 补全 | X6 R2保留外部组织/成员的SecurityStamp、GrantVersion与字段策略，并明确当时未取得S5新身份合同；当前S5 v0.2已提供身份/字段/权限门设计，但该答复针对Main/CR…（完整见台账） |

#### QA-NCR-01-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `NCR-TYPE-01` | W5 | 对齐 | Main6 与 CP4 C# 的五处 null-only 成员被声明为非空整数 |
| `NCR-FIELD-01` | W5 | 对齐 | FIELD 的五条 nullable 注释与同条 shape 不一致 |

#### root-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `ROOT-X3-X4-ABC-01` | W6 | 保留 | 两者都称ABC，但SLOT按当前行加入前前缀<80/95，首行A；ANALYTICS按末项累计<=80/95，单产品100%为C。各自排序和分组维度亦有精确定义。 |
| `IPQC-CONT-01` | W5 | 对齐 | F02-03 INITIAL declared write set has no InitialClaim rows |
| `IPQC-CONT-02` | W5 | 对齐 | F03 OUT-TRIAL-F03 request source differs from persisted OUT-QA-F03 for the same digest |
| `IPQC-CONT-03` | W5 | 对齐 | F07 FrozenDelivery I26 response changes canonicalKey/sequence without a Delivery write |

#### root-planning-integration-findings.json

| 登记编号 | 主批次 | 初筛入口 | 定位摘要 |
|---|---|---|---|
| `PLAN-IF-001` | W4 | 补全 | MRP两材料基数模式与MAT原生RemainingBasis接口有相近字段，尚不能据名称认定等价采用。 |
| `PLAN-IF-002` | W4 | 采用 | POL/SUP功能文本认可不自动关闭ATP R11 RequiredFenced精确端口。 |
| `PLAN-IF-003` | W5 | 对齐 | AC087步骤readcut22陈旧措辞与当前精确输入fixedReadCut113不一致。 |
| `PLAN-IF-004` | W5 | 对齐 | ATP单位标识在Schema/TypeScript为StableId最长128字符，C#数量验证及DDL五列仍为16。 |
| `PLAN-IF-005` | W2 | 对齐 | ATP声明Replay→Evaluation锁序，终结入口SQL先Evaluation后Replay。 |
| `PLAN-IF-006` | W4 | 对齐 | Legacy signed wire quantity vs nonnegative Stored codec |
| `PLAN-IF-007` | W4 | 对齐 | Receipt20 ACTUAL_AVAILABLE原事实与SUP投影/私有图时间不同 |
| `PLAN-IF-008` | W4 | 对齐 | 材料图algorithmRef仍F025而材料Input/Run/Attempt为v2 |
| `PLAN-IF-009` | W4 | 对齐 | OwnerProfile branches inline schema bounds array to 3; closed producer and full consumer schemas hav…（完整见台账） |
| `PLAN-IF-010` | W2 | 对齐 | Permission catalogue requires fresh authorization on replay and validity check before output; shown …（完整见台账） |
| `PLAN-IF-011` | W2 | 对齐 | ApplyOwnerIntermediateCp18 new success branch commits without returning the TerminalCanonical result…（完整见台账） |
| `PLAN-IF-012` | W4 | 对齐 | PO15 OpenAPI 503 code enum omits three codes present in current API_REGISTRY. |
| `PLAN-IF-013` | W5 | 对齐 | 18 retained PlanningFactHead row wrappers name IAM/&lt;old uuid&gt; while their actual row.ResourceKey is …（完整见台账） |
| `PLAN-IF-014` | W5 | 对齐 | same stored event/leg/command but revision body carries a different request/result/snapshot digest f…（完整见台账） |
| `PLAN-IF-015` | W4 | 对齐 | The CP19 WO step1 CreatePreparation response uses executionRequestKey MAKE-CONVERT-20261002-001 whil…（完整见台账） |

主负责批次登记数：W1＝5；W2＝15；W3＝21；W4＝23；W5＝39；W6＝12，合计 115。此分布是任务路由，不是已确认设计缺陷数。
