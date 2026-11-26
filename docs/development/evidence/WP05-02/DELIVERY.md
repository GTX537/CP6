# WP05-02 工程产品身份与经营料号双 Owner 映射

## 基线、候选与准入

| 对象 | 提交 SHA | Git 树 |
| --- | --- | --- |
| 派发及核对的 Main | `bbaf7aaa39fbb6e079525a816170c57221f3d0b9` | `1139d352ea13882d4c14e386c6fb5852f892ea5a` |
| 交付前最新 Main | `80113f4f751617d73d1b1e989838a2f6ee0b27da` | `f64614f369db41db7a69d4aa6de7c4fa38364fd8` |
| 受验源码候选 | `3c6ead47d72a0afdd4faa7ed071e9b63296f51e8` | `72393a047e792f0f7acb505492948a1eacff9113` |
| 全组 .NET 与前端实测输入 | 暂存树 | `0091eae0e32cbda31854073e8c60ccf6b24fa4bc` |

Main 线在 DEV-1 领取 `CP6:WP05-02`，执行者 `CP6-EXEC`，分支 `codex/wp05-02`。六项直接前置 S01-02、S01-07、S02-00、WP02-10、WP02-13、WP04-33 递归展开为 13 项，含 WP02-13b。13 项均为 DONE，交付提交分别属于固定 Main 和 Platform `a07edc24160d10cb1e3936cc6f495ad4195dd8f0` 的祖先。Main 锁定还原的 `CP6.Platform.AspNetCore` 为 0.11.0。领取时远端同名分支和同卡 PR 查询均为空。[派工卡](<D:/CP6-test-artifacts/WP05-02/WP05-02-CARD-20261126.md>) · [严格前置原件](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/prerequisites.json>) · [总控决定](<D:/CP6-test-artifacts/COORD/DECISION-20261126-two-lanes-and-wp04-33.md>)

主运行后只追加 PG04-A176 独立用例。最终源码树与主运行树逐文件比较，产品和前端输入相同。该新增用例在最终树复跑。交付前的 Main #1337 实施双线治理，增量涉及治理文档与台账工具。本卡文件、运行验证输入及前置记录与该增量无交集，候选仍以派发 SHA 为父提交。本报告单独提交，最终 HEAD、Git 树、差异统计和席位读回见验证原件。[远端增量核对](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/upstream-review.json>)[最终读回](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation.json>)

## 原范围与交付

- 沿现有工程身份保存类别、识别来源以及 Site、法人、技术修订和配置的适用域。法人采用 WP04-01 已登记的当前法人原键和版本，经营产品归属由 ERP 产品 Owner 在确认事务内核对。技术适用域使用工程修订原件中准确配置及 `APPLIES`。来源缺证、越权、过期或有冲突时关闭相关采用。登记新版需要再次核对。
- 沿现有产品和映射入口增加版本化歧义核对。扩展范围的映射先保存草稿，再保存 `REVIEW`，双方 Owner `CONFIRM` 时复核当前权限、源版本和对象范围。同一实际操作者持两项授权时保留两项独立 Owner 事实。映射版本、原操作和审计同事务保存。补证须固定同一映射链中较早的工程、经营和 Site 原引用及准确旧版摘要。
- ERP 产品列表按原 UUID 打开授权可见的当前映射及旧映射补证。WP04-33 的身份及组合查询投影当前法人范围和可读旧映射证明。BOM 和工艺路线的新采用核对准确 Site、技术修订和当前来源。停用、换版和撤权分别影响采用与披露，既存记录仍以原键和摘要识别。
- PostgreSQL 前向迁移 `20261126120000_EngineeringMappingBusinessReferenceV1` 从不可变原件建立经营原引用索引，保留原数据字节、角色授权与原结果。旧工程修订迁移用例按原切点字段准备夹具，保留全部原断言。

[原卡](../../task-plans/WP05.md#wp05-02) · [ENG-PROD-01](../../../design-handbook/modules/ENG-PROD-01.md) · [WP04-01 法人登记](../WP04-01/DELIVERY.md) · [W150-01 原事务和 Site 映射](../W150-01/DELIVERY.md) · [W310-01 余项](../W310-01/DELIVERY.md#wp05-02) · [WP04-33 读取合同](../WP04-33/DELIVERY.md) · [WP10-47 目录及交接](../WP10-47/DELIVERY.md)

## 卡验收与剩余出口

| 验收／出口 | PostgreSQL 用例与结论 |
| --- | --- |
| 同一工程身份跨两个工厂，保留各 ERP 料号、Site、法人及双方 Owner | `Wp0502_two_sites_keep_their_own_legal_scope_and_conflicting_owner_facts_are_rejected` 与 `EngineeringMapping_two_sites_keep_original_ERP_keys_and_require_both_Owner_actions` PASS。 |
| 同原键返回同身份及映射版本，竞争与未知提交按原键恢复 | `EngineeringProduct_actual_HTTP_creation_replay_and_current_revocation`、`Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` 与 `EngineeringMapping_commit_failure_rolls_back_confirmation_and_original_then_exact_CAS_writers_arbitrate` PASS。 |
| 同名异规格保持分立，未核对或未确认映射不产生可采用事实 | `EngineeringProduct_duplicate_codes_do_not_merge_identity_and_history_CAS_remains_immutable`、`Wp0502_same_item_code_at_another_site_does_not_supply_technical_equivalence` 与 `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft` PASS。 |
| 工程角色更新 ERP 价格、库存被拒，同码跨租户不可恢复 | `Wp0502_erp_entry_resolves_native_product_uuid_and_engineering_authority_cannot_edit_price_or_stock` 与 `Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` PASS。 |
| 出口 1 法人归属和适用域 | `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft` 与 `Wp0502_two_sites_keep_their_own_legal_scope_and_conflicting_owner_facts_are_rejected` PASS。WP04-33 `legalEntityApplicability` 从 `REQUIRED_FENCED` 转为 `CONFIRMED` 并返回准确范围。缺证、停用或越权时关闭当前范围。 |
| 出口 2 双 Owner 旧映射确认与历史 | `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft`、`Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` 与 `Wp0502_mapping_forward_index_preserves_old_original_receipt_and_current_permissions` PASS。准确旧映射补证可读时从 `UNKNOWN` 转为 `KNOWN`，原版字节及原回执可回读，未补证保持 `UNKNOWN`。停用保留经当前授权可见的历史。 |
| 出口 3 WP10-47 原工程序列 | `Wp0502_wp1047_engineering_sequence_pins_original_mapping_and_current_revision_and_authority` 与 `Operational_catalogue_approved_native_ratio_can_be_pinned_by_draft_bom_before_stock_posting` PASS。BOM、工艺路线持有真实原映射引用，错 Site 或修订拒绝新采用，换版保留历史，撤权拒绝读取。 |
| 前滚迁移及原经营入口 | `Wp0502_mapping_forward_index_preserves_old_original_receipt_and_current_permissions`、`EngineeringRevision_forward_migration_preserves_original_product_and_number_rule_without_grants`、`Wp0433_legacy_product_http_keeps_order_identity_and_rolls_back_stale_delete_with_its_children` PASS。原件、原结果和授权一致，ERP 产品及 ProductCoProduct 走原经营入口。 |

### 原 AC 与修订验收

| 原 AC | 本卡证据及后续出口 |
| --- | --- |
| AC-SUP-001、AC-PLM-PG04-A175 | 本机合成 PASS。跨两个实际夹具 Site/料号，独立双 Owner 证据和准确适用域。 |
| AC-SUP-002、AC-PLM-PG04-A174、AC-PLM-PG04-A176 | 本机合成 PASS。同名异规格保持分立，另一 Site 的同物料码、不同枝号和规格仍待核，已确认的原关系可读。 |
| AC-PLM-PG04-A173 | 本机合成 PASS。候选保存准确双方原件及待核状态，经营价、库存写权限独立。 |
| AC-PLM-FB01-025 | 完整场景 NOT_RUN。本卡提供产品类别、来源与适用域。采购成品的 BOM／工艺不适用证据与技术包检查沿 WP05-08/09/11/12，后继用途按 WP05-32/42。 |
| AC-PLM-FB01-026 | 完整场景 NOT_RUN。自制工艺 Required 检查和冻结拒绝沿 WP05-08/11/12。 |
| AC-PLM-FB01-027 | 完整场景 NOT_RUN。工具规格／实例与冻结、现场使用按 WP05-06/08/11/12/32/42 原责任。 |
| DR-PE-ENG-MULTI-OUTPUT-MANIFEST POS／NEG | NOT_RUN。逐输出关系和基线绑定归 WP05-10 与 WP05-12。 |

[原九项清单](../../assessments/20261011/catalogs/engineering-oa.json) · [R1 设计](../../../design-handbook/remediation/07-shared-consumer-mappings.md) · [R1 运行目录](../../../design-handbook/remediation/assets/shared-consumers/acceptance.json)

## 本机验证及复用

- Windows，.NET SDK 10.0.404 Release，锁定还原和测试使用同一 `--artifacts-path`。PostgreSQL 18.6 在回环 `127.0.0.1:55458` 与 `127.0.0.1:55459` 使用分别新建的 `permission-consumption-wp05-02-candidate` 和 `permission-consumption-wp05-02-samecode` 启动。测试使用合成的法人、Site、产品、角色、价格、库存与工程原件，真实本机 Main Kestrel 路径见 TRX。测试脚本记录所有所需环境变量和子进程合成配置。
- 最终 .NET 去重 **90 通过、0 失败、0 跳过**。7 项为本卡新增，其余覆盖 ERP 产品、ProductCoProduct、WP04-33、WP10-47、工程身份、修订和 BOM／工艺路线。主运行 89 项，TRX SHA-256 `2b6027d0faf787675f52b78403893dc9f93f31b5814c9d9ecdef5037dab3d933`。最终树新增 PG04-A176 定向 1 项，TRX SHA-256 `219012989d45f456b587015125afc977ddc3ee6533b16d80adad8c5b799bee8f`。[主运行](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation-candidate.json>) · [主运行 TRX](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/results-candidate/wp05-02-candidate.trx>) · [补充定向](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation-samecode.json>) · [补充 TRX](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/results-samecode/wp05-02-samecode.trx>) · [复现脚本](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/run-validation.ps1>)
- Node v20.19.0，Vitest **53 通过、0 失败、0 跳过**，六个定向套件。实际 `vue-tsc --build --force` 和 Vite 构建退出 0。Vitest JSON SHA-256 `e6c1ed784c444e6f1abbde2c79c27b8c9d63d913bccec7a864f0da104a364776`。[前端验证证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/frontend-candidate/validation.json>) · [前端复现脚本](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/run-frontend.ps1>)
- 基线红测定位到现有映射命令未接受法人证据，以及前端提前展示确认动作。扩展回归的旧工程修订夹具因现行 EF 审计列晚于迁移切点而失败，修正后在最终源码复跑。W150-01 的已采用稳定身份、事务和 Site 事实及 W310-01 的余项核销作为基础。WP04-33 和 WP10-47 的受影响用例在最终树上重新运行。[基线 HTTP 红测](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/results-red-http/wp05-02-red-http.trx>) · [扩展回归原件](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/results-regression/wp05-02-regression.trx>)
- 集中审查核对完整差异的规格、权限、租户、并发、原件保留和敏感信息。两侧工作流事件、过滤和依赖以实际 Git 对象及变更路径做静态核对。所有本卡 PostgreSQL 实例已停机，PID 文件、端口监听和本卡测试进程读回为空。[最终工作流核对](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/workflow-final.json>) · [测试停机汇总](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/pg-stop-readback.json>)

## 未证、有效工时与席位

- **UNPROVEN** 真实企业法人归属、原始业务单据和实际远端采用。测试夹具的证据模式为合成。
- **NOT_RUN** 制造资格 WP05-32/42、产品币种绑定 WP04-04、整目标的跨 Owner 场景及多输出修订验收、SQL Server、生产环境迁移或部署、Actions、远端合并后冒烟及浏览器端到端。工程技术适用域在本地工作修订上受验。
- 首次核对 `2026-11-26T11:36:41Z`。领取 `2026-11-26T11:39:21.040776Z`。本报告快照 `2026-11-26T12:21:52.6250657+00:00`，有效时间 **2711.625 秒**，含前置核对、实现、本机测试及审查。M 卡额度 14400 秒，150% 阈值 21600 秒。
- 候选保存在本地 `codex/wp05-02`，DEV-1 登记为 `active`，等待总控独立复核。远端分支和 PR 查询为空，台账登记沿总控 PR 流程。最终工作树状态与精确差异见读回。[分支与席位](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation.json>) · [差异统计](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/diff-stat.txt>)
