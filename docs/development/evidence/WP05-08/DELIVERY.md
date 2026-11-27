# WP05-08 版本化工艺操作与工具规格引用交付

## 1. 开工核对与验收范围

派工为 Main 线 CODE／M，目标 `ENG-ROU-01`。DEV-1 于 2026-11-27 05:12:13 UTC 以 `CP6:WP05-08`、Owner `CP6-EXEC` 登记，任务分支为 `codex/wp05-08`。GitHub `refs/heads/main` 核实为 `76805d0b7daf9673b7ceb67339e6757ab7a33976`。同卡远端分支与同名 PR 查询为空。[原派工](<D:/CP6-test-artifacts/WP05-08/WP05-08-CARD-20261127.md>) · [原卡](../../task-plans/WP05.md#wp05-08)

7 项直接前置为 S01-02、S02-00、WP02-10、WP02-13、WP05-03、WP05-04、WP08-16。加入 WP02-13b 后直接层为 8 项。递归去重为 17 项，含 S01-01、S01-02、S01-07、S01-09、S02-00、WP02-01、WP02-02、WP02-03、WP02-07、WP02-10、WP02-13、WP02-13b、WP04-33、WP05-02、WP05-03、WP05-04、WP08-16，固定台账均为 `DONE`。Main 已核对实际合并采用，Platform 的三项依赖在已核实远端 `a07edc24160d10cb1e3936cc6f495ad4195dd8f0` 祖先中。产品身份、工程修订、资源提供者和需求回归分别固定 #1338、#1340、#1330 和 #1341。WP05-04 的版模来源与技术草稿合同随 #1321 已采用。[开工原件](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/preflight.json>)

互斥资源为 `Main:engineering-routing`、`Main:erp-product`、`ProductProcess.cs` 和 `ProductCoProduct.cs` 的 Main 文件锁。WP03-19 分支 `f28408c065719f82b27a4bea73c53c3c2e9475fe` 的 50 个远端实际变更路径与本卡声明路径无交集。交付时的资源与路径核对另有读回原件。[双线决定](../../decisions/DECISION-20261126-two-lanes.md) · [交付读回](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/handoff.json>)

原 scope 为适配 `ProductProcess` 路线版本、稳定工序发生项、顺序及依赖、资源参数与替代工艺，保留工时、损耗、单位来源和准确工具规格。以原 Owner 提供维护、差异、使用引用、校验输入和历史读回。正向验收为同码两次独立排序、新旧路线与旧执行快照各自可读、准确工具规格可追。反向验收为循环、未知量纲和未证参数不能校验通过，`PlateNo` 或同号实例不产生 `HoldExact`。

W186、W203 已交付工序发生项、旧原件、资源能力版本和工艺授权。本次核销 W310-01 所列余项中的准确资源目录与时基、工具规格引用、显式替代条件、过程参数证据与工序输入校验。整包工程冻结、资源 Claim／Start 与工具实例资格继续由原 Owner 验收。原 AC 的完整场景出口见下表。[ENG-ROU-01](../../../design-handbook/modules/ENG-ROU-01.md) · [W310-01](../W310-01/DELIVERY.md#wp05-08) · [原 AC 目录](../../assessments/20261011/catalogs/engineering-oa.json) · [共享消费者修订](../../../design-handbook/remediation/07-shared-consumer-mappings.md)

## 2. 实际改动

- 沿用 `Main.Plm.EngineeringRouting.v1` 的路线原件、工序发生项、命令、差异与原键恢复。对准确的 WP08-16 资源／能力目录保留工时 UOM、时间基准和批准操作键。旧 `ProductProcess` 工时、损耗、数值原文和历史版本继续可读。
- 工序可固定同产品同场所 `IPQC` 已批准质量规格的准确条款。参数范围、目标值、方法、量纲、单位和精度在真实 Main Owner 事务核对，旧记录沿原权限和摘要读取。草稿可保存缺项，证据撤回后当前校验标为不可用。
- 替代工序固定同组主工序发生项、批准配置规则与明确选项。重复条件、错误关系以及依赖环进入冲突投影。页面提供条款和规则选择、已保存路线使用引用、工序输入状态及来源读回。
- 工具规格准确原件与实际实例分开。路线保留 `DRAFT_ONLY`。BOM、制造视图、工程检查和工程修订透传同一质量 Owner，旧工程写入、重基和冻结合同沿用。新增字段写入既有版本原件，没有新增迁移。[产品契约](../../engineering-routing-v1.md)

## 3. 卡级验证和证据

合成输入标为 `SIMULATED`，实际运行 Main PostgreSQL、Owner 和适用 HTTP。质量与作业资料使用本机受控合成夹具，质量环境为 `DEMO`。最终报告以本文件所在准确提交／树的一次运行结果为准。报告索引包含逐名 TRX、SHA-256、Release 命令、环境变量脱敏记录、前端工具版本及停机读回。[最终证据](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/validation-final/final-verification.json>) · [执行脚本](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/run-final.ps1>)

| 用户验收 | 用例 |
| --- | --- |
| 同码两次各保独立发生项及有序依赖 | `EngineeringRouting_original_history_same_process_occurrences_and_version_CAS` 与 版本化工艺页面同码用例 |
| 路线后继、新旧准确原件及旧工单明细各自回读 | `Wp0508_lost_commit_recovers_original_key_and_route_revision_preserves_work_order_snapshot`、`EngineeringRouting_composition_uses_exact_route_without_parent_recursion_and_preserves_old_binding` |
| WP08-16 批准目录、时间定义及 IPQC 同场所的精确参数，错误量纲和值阻断 | `Wp0508_http_pins_resource_approval_work_time_and_exact_process_parameter` 与 `ResourceCatalogue_HTTP_approved_capability_pins_sources_and_keeps_unknown` |
| 循环、未知量纲及无证参数不通过 | `EngineeringRouting_dependency_cycles_exact_numbers_and_unknown_units_remain_unqualified`、`Wp0508_routing_requires_exact_catalogue_dimension_and_parameter_evidence` |
| 明确替代方向和规则，重叠、依赖冲突及撤回处理 | `Wp0508_alternative_is_directional_and_bound_to_an_approved_configuration_option` 与页面替代规则用例 |
| 025 的批准策略保留工艺不适用，026 的必需工艺缺失及虚报不适用被拒绝 | `Wp0508_approved_purchased_exception_and_required_internal_route_are_distinct` |
| 027 的准确技术规格和同号物理实例边界 | `Wp0508_exact_tool_specification_does_not_confer_same_number_instance_qualification`、`EngineeringRouting_exact_tool_specification_not_plate_alias_and_stale_unit_prevents_new_binding` |
| 跨租户、当前权限撤销、两连接竞争及未知提交原键恢复 | `EngineeringRouting_two_connections_choose_one_version_and_other_tenant_cannot_read`、`EngineeringRouting_interrupted_commit_close_and_current_read_only_recovery`、`Wp0508_http_pins_resource_approval_work_time_and_exact_process_parameter`、`Wp0508_lost_commit_recovers_original_key_and_route_revision_preserves_work_order_snapshot` |

同树回归按四组存档。`wp0502` 重新执行并逐名核对原 95 项，`engineering` 重新执行并逐名核对 WP05-03 工程 44 项。`requirements` 沿 WP05-13 的 REQ、销售来源及 DOC／QUALITY 过滤。`routing` 含新卡用例、WP08-16 能力、归属和资源来源，以及工具规格回归。分组各自新建仅回环的隔离 PostgreSQL 18 夹具，避免共享夹具菜单分配相互影响；`restore` 和 `test` 使用同一最终构建目录。前端覆盖路线、工程、BOM、REQ、DOC、QUALITY、工具规格、资源和工时，执行 Vitest、`vue-tsc --build --force` 与 Vite 构建。[测试与停机记录](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/validation-final/final-verification.json>)

开发原件保留旧实现的缺证红例。首轮组合测试出现夹具菜单主键冲突、新测试重复插入授权范围和尝试为同产品新建第二条配置规则。夹具改为沿既有授权与规则后继，并在同号工具用例中采用两边都能持久化的模拟编号。扩展资源回归还发现两处历史夹具与当前 Main 的迁移清单不兼容。资源源测试改用迁移前的原列写合成行，W203 测试固定目标迁移且校验后续迁移仍待应用。两组保留原件、回执和授权断言。最终证据使用本报告所在提交完整重跑。[开发记录](<D:/CP6-test-artifacts/WP05-08/exec-20261127T051503Z>)

## 4. 原 AC 逐项出口

`ENG-ROU-01` 原清单完整，共 102 条。以下状态表示整个原业务场景。表中列为局部已验的断言有上表对应本地证据，整包、执行或真实采用分别仍有接续门。每条保留原目录编号。

| 原 AC | 完整场景 | 接续卡及依据 |
| --- | --- | --- |
| `AC-CFG-001` | `NOT_RUN` | WP05-39（承接 WP05-02）。配置上下文仍需同时固定工程和经营身份。 |
| `AC-CFG-002` | `NOT_RUN` | WP05-39（承接 WP05-02）。配置自动合并入口仍需用真实 Owner 证据拒绝同名对象。 |
| `AC-CFG-003` | `NOT_RUN` | WP05-09。同一材料在不同装配位置的展开和用量核对归 BOM。 |
| `AC-CFG-004` | `NOT_RUN` | WP05-09／11。原例检查 BOM 层级环与冻结门，本卡的工序环用例覆盖工艺图。 |
| `AC-CFG-005` | `NOT_RUN` | WP05-10。两工厂 MBOM 和路线需分别关联准确 EBOM 及制造依据。 |
| `AC-CFG-006` | `NOT_RUN` | WP05-10／11。缺失必需部件的映射须保留缺口及技术包检查结果。 |
| `AC-CFG-007` | `NOT_RUN` | WP08-07。需核已批准技术工艺与未开工的 MES 实际状态并列。 |
| `AC-CFG-008` | `NOT_RUN` | WP08-03／04。本卡验证旧路线和存量工单明细，正式 WO 固定路线与后续切换另验。 |
| `AC-CFG-009` | `NOT_RUN` | WP05-39。待运行完整合法选项的解析原件及成员集合。 |
| `AC-CFG-010` | `NOT_RUN` | WP05-39。待验证必选输入缺失在正式解析端保持待补。 |
| `AC-CFG-011` | `NOT_RUN` | WP05-39。本卡验证工序替代方向，原例的材料或配置逆向资格由 CFG 裁决。 |
| `AC-CFG-012` | `NOT_RUN` | WP05-29／39。临时差异的额度和永久 BOM 不改需联合证明。 |
| `AC-CFG-013` | `NOT_RUN` | WP05-39。正式解析需以两 Site、各自时点和基线复核共存。 |
| `AC-CFG-014` | `NOT_RUN` | WP05-39。序列范围须由原 Owner 提供可比较顺序。 |
| `AC-CFG-015` | `NOT_RUN` | WP05-32／WP08-04。保留技术匹配，正式制造使用检查 REL 用途。 |
| `AC-CFG-016` | `NOT_RUN` | WP05-39。正式解析须在两个不相容基线匹配时报告冲突。 |
| `AC-CFG-017` | `NOT_RUN` | WP08-08／WP10-17。不同技术基线的实际批份额须随库存仍可区分。 |
| `AC-CFG-018` | `NOT_RUN` | WP08-03／07。必须核原 MES 实际来源及计划与实际的独立投影。 |
| `AC-CFG-019` | `NOT_RUN` | WP05-36。分 Site 做实际采用、在制处置和范围关闭回读。 |
| `AC-CFG-020` | `NOT_RUN` | WP05-36／42。受控回退仍须重新核实当前用途许可。 |
| `AC-PLM-PG03-M013` | `NOT_RUN` | WP05-10／33／WP08-03。共同过程多产出绑定一个根意图、原 WO 与一份输入预约。 |
| `AC-PLM-PG03-M014` | `NOT_RUN` | WP05-33／WP08-04。不可分共同过程需同时证明两产出的强制制造前置。 |
| `AC-PLM-PG03-M015` | `NOT_RUN` | WP05-32／42。本次限定试制与未来销售的用途条件分别裁决。 |
| `AC-PLM-PG03-M016` | `NOT_RUN` | WP05-33／WP08-07。必需操作次数未知须阻止建单并保留输入预约及原键。 |
| `AC-PLM-PG03-M025` | `NOT_RUN` | WP08-15。停止需求后由 MES 证明已经发生的共同投入与未知 B 产出。 |
| `AC-PLM-PG03-M026` | `NOT_RUN` | WP08-15／WP05-28。外协后果缺失时原执行及变更责任仍需明确保留。 |
| `AC-PLM-PG03-M027` | `NOT_RUN` | WP05-35／28。按原用量终结证明核 REL 数量余量和 ECM 库存义务。 |
| `AC-PLM-PG03-M028` | `NOT_RUN` | WP08-09／WP05-28。迟到真实更正须保留旧关闭和后继差额处置。 |
| `AC-PLM-PG03-M029` | `NOT_RUN` | WP05-25／26。B 工序变化需触发对共同过程和 A 既有报告的影响分析。 |
| `AC-PLM-PG03-M030` | `NOT_RUN` | WP05-24／26。工程和 VER 分别证明未受影响的可复用要求范围。 |
| `AC-PLM-PG03-M031` | `NOT_RUN` | WP05-24。技术验证须逐产出展示 A 已满足和 B 缺件，组关闭另判。 |
| `AC-PLM-PG03-M032` | `NOT_RUN` | WP05-22／WP08-08。后继验证计划及旧产出批的身份各自沿原基线。 |
| `AC-PLM-PG03-A017` | `NOT_RUN` | WP05-10／11。本卡供应准确路线及单位引用，工厂 MBOM 映射及有证转换的批准另验。 |
| `AC-PLM-PG03-A018` | `NOT_RUN` | WP05-10／11。本卡拒绝工序量纲错配，材料和几何之间的有证换算及原系数留存另验。 |
| `AC-PLM-PG03-A019` | `NOT_RUN` | WP05-11／12。工序输入和工具规格可固定独立原件，作业文档及完整配置包进入受控版本另验。 |
| `AC-PLM-PG03-A020` | `NOT_RUN` | WP05-11／WP08-01。本卡保留缺参数证据，机器在线与正式技术包缺规范的联合负例另验。 |
| `AC-PLM-FB01-013` | `NOT_RUN` | WP05-12／39。从准确旧规则读回冻结基线，再按后继规则和用途解析新活动。 |
| `AC-PLM-FB01-014` | `NOT_RUN` | WP05-32／42。当前 CFG 匹配与缺 REL 用途许可须在本次执行入口共同核对。 |
| `AC-PLM-FB01-015` | `NOT_RUN` | WP05-36／42。历史真实采用和新活动许可撤销需在同一业务视图复核。 |
| `AC-PLM-FB01-025` | `NOT_RUN` | WP05-11／12／32／42（含 WP05-09 的 BOM 输入）。本机已保留批准策略的工艺不适用证据，完整技术包和后继用途由原 Owner 履行。 |
| `AC-PLM-FB01-026` | `NOT_RUN` | WP05-11／12。本机检查已阻断自制必需工艺缺失和伪造不适用，实际冻结事务与缺项责任须独立复测。 |
| `AC-PLM-FB01-027` | `NOT_RUN` | WP05-06／11／12／32／42。本机已固定准确规格并拒绝同号实例，技术冻结和场内实际 HoldExact 按后续用途验收。 |
| `AC-PLM-PG04-A067` | `NOT_RUN` | WP05-39（承接 WP05-02）。真实 E@2 与经营物料 M 的配置引用及适用需从 CFG 入口运行。 |
| `AC-PLM-PG04-A068` | `NOT_RUN` | WP05-39（承接 WP05-02）。需验证未解析的工程身份阻止配置固定。 |
| `AC-PLM-PG04-A069` | `NOT_RUN` | WP05-39（承接 WP05-02）。双 Site 的真实映射应在完整配置中按范围准确采用。 |
| `AC-PLM-PG04-A070` | `NOT_RUN` | WP05-39（承接 WP05-02）。同码异规格不能经配置映射合并。 |
| `AC-PLM-PG04-A071` | `NOT_RUN` | WP05-09。原场景是完整 EBOM 层级及同零件两位置的保存。 |
| `AC-PLM-PG04-A072` | `NOT_RUN` | WP05-09／11。原场景是 BOM 层级环或未知子版阻止整包通过。 |
| `AC-PLM-PG04-A073` | `NOT_RUN` | WP05-10。需要逐位置核 Site 制造投入产出及额外包装来源。 |
| `AC-PLM-PG04-A074` | `NOT_RUN` | WP05-10／11。需在真实制造映射门验证删必需行仍报缺口。 |
| `AC-PLM-PG04-A075` | `NOT_RUN` | WP05-26／WP08-03。本卡已验证路线后继和旧执行明细，真实 ECO 与 WO 快照采用另验。 |
| `AC-PLM-PG04-A076` | `NOT_RUN` | WP08-03／04。本卡验证路线修订不会改旧工单，从原工单写入口防止伪造工序版本仍待验。 |
| `AC-PLM-PG04-A077` | `NOT_RUN` | WP05-39。工程编制版本化选项和客户商业选择的分权需要贯通。 |
| `AC-PLM-PG04-A078` | `NOT_RUN` | WP05-39。真正必选项缺失时首项不能成为商业默认。 |
| `AC-PLM-PG04-A079` | `NOT_RUN` | WP05-39。完整选项集及其批准规则版须形成正式解析依据。 |
| `AC-PLM-PG04-A080` | `NOT_RUN` | WP05-39。缺值与并存互斥冲突在正式解析端须分别显示。 |
| `AC-PLM-PG04-A081` | `NOT_RUN` | WP05-39。本卡验证向主工序引用，真实对象 B 替 A 的专业候选仍由 CFG 提出。 |
| `AC-PLM-PG04-A082` | `NOT_RUN` | WP05-39。本卡禁止替代链引用，型号相似的对象兼容还需专业证据。 |
| `AC-PLM-PG04-A083` | `NOT_RUN` | WP05-39／32。客户甲受限技术规则与后续用途批准须独立核验。 |
| `AC-PLM-PG04-A084` | `NOT_RUN` | WP05-29／39。临时差异仅覆盖原批次，永久 BOM 修改走原变更门。 |
| `AC-PLM-PG04-A085` | `NOT_RUN` | WP05-39。需要准确 A、B 对象和专业结论的评价受理。 |
| `AC-PLM-PG04-A086` | `NOT_RUN` | WP05-39。两段有限关系不能推出第三段对象兼容。 |
| `AC-PLM-PG04-A087` | `NOT_RUN` | WP05-39／09。B+C 套件规则按数量和安装关系的完整批准另验。 |
| `AC-PLM-PG04-A088` | `NOT_RUN` | WP05-39／09。缺 C 时正式套件检查须拒绝部分兼容。 |
| `AC-PLM-PG04-A089` | `NOT_RUN` | WP05-39。按业务时区、订单集合、Site 和活动时点保存正式适用草案。 |
| `AC-PLM-PG04-A090` | `NOT_RUN` | WP05-39。范围空白及无序列 Owner 顺序必须阻断解析。 |
| `AC-PLM-PG04-A091` | `NOT_RUN` | WP05-39。完整规则覆盖与唯一匹配的解释沿正式配置请求验证。 |
| `AC-PLM-PG04-A092` | `NOT_RUN` | WP05-39。权限不足导致规则覆盖不全时不能声明全局 MATCH。 |
| `AC-PLM-PG04-A093` | `NOT_RUN` | WP05-39。补必选原输入要形成新的正式解析记录。 |
| `AC-PLM-PG04-A094` | `NOT_RUN` | WP05-39。重叠互斥的裁决前不得让列表顺序决定。 |
| `AC-PLM-PG04-A095` | `NOT_RUN` | WP08-08。两个实物批须经原 WO 原根和实际份额证明不同技术来源。 |
| `AC-PLM-PG04-A096` | `NOT_RUN` | WP08-03／08。历史实际来源缺失仍 UNKNOWN，不能用计划基线替代。 |
| `AC-PLM-PG04-A097` | `NOT_RUN` | WP05-25／WP08-08。原 MES 差异回执与工程和质量处置需贯通。 |
| `AC-PLM-PG04-A098` | `NOT_RUN` | WP08-08／WP10-17。库存只有料号时缺可信实际技术来源。 |
| `AC-PLM-PG04-A099` | `NOT_RUN` | WP05-39／32。活动 E1 显式选择和原用途资格需共同保留。 |
| `AC-PLM-PG04-A100` | `NOT_RUN` | WP05-39。两个合法备选等业务选择，不能按最新自动采用。 |
| `AC-PLM-PG04-A101` | `NOT_RUN` | WP05-39。待核完整可见规则和活动输入后返回适用理由。 |
| `AC-PLM-PG04-A102` | `NOT_RUN` | WP05-39。权限或来源缺口不能转成无候选并宣称唯一。 |
| `AC-PLM-PG04-A103` | `NOT_RUN` | WP05-36。两个 Site 的 WIP、活动锚点与责任需要实际切换计划。 |
| `AC-PLM-PG04-A104` | `NOT_RUN` | WP05-36。保留未知 SiteB 在制，不能用筛选后的范围关账。 |
| `AC-PLM-PG04-A105` | `NOT_RUN` | WP05-36。需逐 Site 真实采用及各自准确时点的回执。 |
| `AC-PLM-PG04-A106` | `NOT_RUN` | WP05-36／42。协调回退后仍需原 WO 不变和重新许可。 |
| `AC-PLM-PG04-A107` | `NOT_RUN` | WP05-39／40。受影响范围的规则变化须交 LCM 差分重评。 |
| `AC-PLM-PG04-A108` | `NOT_RUN` | WP05-39／40。已采用的旧规则引用不得按名称被改写。 |
| `AC-PLM-PG04-A109` | `NOT_RUN` | WP05-36／40。裁决后继采用须看当前许可和旧义务后果。 |
| `AC-PLM-PG04-A110` | `NOT_RUN` | WP05-40／42。旧库存来源未知与现有许可不足仍需先解决。 |
| `AC-PLM-LR02-033` | `NOT_RUN` | WP05-39／40。明确 A@1/B@2 的有方向、限客户规则和 LCM 引用。 |
| `AC-PLM-LR02-034` | `NOT_RUN` | WP05-39。正式解析应拒绝没有反向专业证据的逆向对象替换。 |
| `AC-PLM-LR02-035` | `NOT_RUN` | WP05-39。正式对象规则不得按两段关系推导未经评价的传递兼容。 |
| `AC-PLM-LR02-036` | `NOT_RUN` | WP05-39／09。B+C 完整套件缺 C 时须保持本次活动受阻。 |
| `AC-PLM-LR02-037` | `NOT_RUN` | WP05-39／40。客户乙的 NO_MATCH 和 LCM 额度外采用拒绝另验。 |
| `AC-PLM-LR02-038` | `NOT_RUN` | WP05-39。活动级 CHOICE_REQUIRED 和强制冲突各自保留。 |
| `AC-PLM-LR02-039` | `NOT_RUN` | WP05-39。规则区间和时区按业务时点精确核对相邻端点。 |
| `AC-PLM-LR02-040` | `NOT_RUN` | WP05-39。所需规则或配置覆盖不完整时正式解析输出 UNKNOWN。 |
| `AC-PLM-LR02-041` | `NOT_RUN` | WP05-40。经确认的采用子集保留准确原 CFG 规则引用。 |
| `AC-PLM-LR02-042` | `NOT_RUN` | WP05-40。LCM 不能扩展批准的客户范围，超域回专业评审。 |
| `AC-PLM-LR02-043` | `NOT_RUN` | WP05-39／32。允许选项按现有受控范围和许可使用，不派生无依据基线。 |
| `AC-PLM-LR02-044` | `NOT_RUN` | WP05-36／WP08-03。本卡已保留旧工序明细，LCM/MES 拒绝及处置回执另验。 |
| `AC-PLM-LR02-045` | `NOT_RUN` | WP05-39／40。正式旧活动保留 v1，新限制只约束剩余活动的实际裁决。 |
| `AC-PLM-LR02-046` | `NOT_RUN` | WP05-25／40／42。撤回兼容依据需阻断新用并把已用范围交有权调查。 |
| `AC-PLM-LR02-047` | `NOT_RUN` | WP05-29／32／34。DEV 额度、REL 试制和发布引用共同验证，避免重复占用。 |
| `AC-PLM-LR02-048` | `NOT_RUN` | WP05-40／41。缺合格替代时还需分别核对停用、旧版服务和未结承诺。 |


### 必需共享消费者修订

| 修订场景 | 完整场景 | 接续卡及依据 |
| --- | --- | --- |
| `PE-ENG-MULTI-OUTPUT-MANIFEST-POS` | `NOT_RUN` | WP05-10 核对逐输出生成工序与完整输入关系，WP05-12 核对准确技术绑定与冻结事务。 |
| `PE-ENG-MULTI-OUTPUT-MANIFEST-NEG` | `NOT_RUN` | WP05-10 核对错配和陈旧预览，WP05-12 核对冻结拒绝和历史基线完整性。 |

## 5. 交接状态

交接保存在已登记的本地 `codex/wp05-08`，DEV-1 由 `CP6-EXEC` 持有。候选 SHA、树、完整 diff 核对、静态工作流报告、有效时间与 `status --porcelain` 的实际读回见证据。真实投产、现网数据与全场景原 AC 仍待对应 Owner。分支、复核与席位下一步由总控处理。[交付读回](<D:/CP6-test-artifacts/WP05-08/exec-20261127T052752Z/handoff.json>)
