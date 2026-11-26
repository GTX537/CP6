# WP05-02 工程产品身份与经营料号双 Owner 映射

## 基线、候选与准入

| 对象 | 提交 SHA | Git 树 |
| --- | --- | --- |
| 派发及核对的 Main | `bbaf7aaa39fbb6e079525a816170c57221f3d0b9` | `1139d352ea13882d4c14e386c6fb5852f892ea5a` |
| 交付前最新 Main | `80113f4f751617d73d1b1e989838a2f6ee0b27da` | `f64614f369db41db7a69d4aa6de7c4fa38364fd8` |
| R0 交付与独立复核对象 | `b16d57cd2a4cba6e6179c6ca625580515c7763d5` | `f5563490efcf71f095fef83298728a988de564e2` |
| R1 源码候选 | `b1c4c4f84d3c168cf6df79e1b22f00c38db1e6e3` | `ec5ab7023b9ad7fe2b10722221c96bc88711ec7c` |
| R1 交付与总控二轮复核对象 | `af52e9f69414957c738950abfe9b811a100b9fef` | `8bf5f5ea681a3873e0a7eb591832125bbc783564` |
| R2 源码候选 | `aaa92e1e280772d5776902d1fd00181523d7075d` | `00329709d4c7312c2e4db65b109f2ca73532d839` |
| R2 最终交付候选 | 最终验证原件的 `Head` | 同一原件的 `HeadTree` 和 `InputTree` |

Main 线在 DEV-1 领取 `CP6:WP05-02`，执行者 `CP6-EXEC`，分支 `codex/wp05-02`。六项直接前置 S01-02、S01-07、S02-00、WP02-10、WP02-13、WP04-33 递归展开为 13 项，含 WP02-13b。13 项均为 DONE，交付提交分别属于固定 Main 和 Platform `a07edc24160d10cb1e3936cc6f495ad4195dd8f0` 的祖先。Main 锁定还原的 `CP6.Platform.AspNetCore` 为 0.11.0。领取时远端同名分支和同卡 PR 查询均为空。[派工卡](<D:/CP6-test-artifacts/WP05-02/WP05-02-CARD-20261126.md>) · [严格前置原件](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/prerequisites.json>) · [总控决定](<D:/CP6-test-artifacts/COORD/DECISION-20261126-two-lanes-and-wp04-33.md>)

Main #1337 的增量为治理文档与台账工具，本卡沿派发 SHA 和原任务分支接续。R2 在 DEV-1 原登记接续。Main 和 Platform 的已核实远端主线保持相同提交，13 项前置与采用提交沿用同一基线的复核。最终候选含本报告，最终 SHA、树、TRX SHA-256 和工作区状态由同树复跑及读回原件记录。[R2 打回单](<D:/CP6-test-artifacts/WP05-02/WP05-02-REWORK-R2-20261126.md>) · [总控对 R1 的复核](<D:/CP6-test-artifacts/WP05-02/coord-verify-20261126T131549Z/coord-review.md>) · [R2 前置与接续](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/preflight-r2.json>) · [最终同树证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-final.json>) · [最终读回](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation.json>)

## 原范围与交付

- 沿现有工程身份保存类别、识别来源以及 Site、法人、技术修订和配置的适用域。法人采用 WP04-01 已登记的当前法人原键和版本，经营产品归属由 ERP 产品 Owner 在确认事务内核对。技术适用域使用工程修订原件中准确配置及 `APPLIES`。来源缺证、越权、过期或有冲突时关闭相关采用。登记新版需要再次核对。
- 沿现有产品和映射入口增加版本化歧义核对。扩展范围的映射先保存草稿，再保存 `REVIEW`，双方 Owner `CONFIRM` 时复核当前权限、源版本和对象范围。同一实际操作者持两项授权时保留两项独立 Owner 事实。映射版本、原操作和审计同事务保存。补证须固定同一映射链中较早的工程、经营和 Site 原引用及准确旧版摘要。
- ERP 产品列表按原 UUID 打开授权可见的当前映射及旧映射补证。WP04-33 的身份及组合查询投影当前法人范围和可读旧映射证明。BOM 和工艺路线的新采用核对准确 Site、技术修订和当前来源。停用、换版和撤权分别影响采用与披露，既存记录仍以原键和摘要识别。
- PostgreSQL 前向迁移 `20261126120000_EngineeringMappingBusinessReferenceV1` 从不可变原件建立经营原引用索引，保留原数据字节、角色授权与原结果。旧工程修订迁移用例按原切点字段准备夹具，保留全部原断言。

[原卡](../../task-plans/WP05.md#wp05-02) · [ENG-PROD-01](../../../design-handbook/modules/ENG-PROD-01.md) · [WP04-01 法人登记](../WP04-01/DELIVERY.md) · [W150-01 原事务和 Site 映射](../W150-01/DELIVERY.md) · [W310-01 余项](../W310-01/DELIVERY.md#wp05-02) · [WP04-33 读取合同](../WP04-33/DELIVERY.md) · [WP10-47 目录及交接](../WP10-47/DELIVERY.md)

### R1 修复

- WP04-33 组合查询按选定映射的当前 Site、法人来源和准确版本给出 `legalEntityApplicability`。选中缺证映射或未选映射时，结果为 `REQUIRED_FENCED`，且 `missing` 保留 `LEGAL_ENTITY_APPLICABILITY`。独立产品读取仍披露经当前授权可读的各 Site 法人范围。
- 对已选 Site 的 BOM 和工艺路线，修订沿用快照时，在原事务内锁定并核对准确映射、当前授权、目标 Site、已确认的技术修订及配置。Site 或技术版本不匹配时拒绝修订，原键查询为 `UNKNOWN`。匹配时直接保留原快照字节与摘要，前序版本和已提交回执按原键回读。

### R2 修复

- 两个修订入口把原快照所属上一版的 Site 与准确技术修订传入共用校验。仅沿用身份关系的旧草稿可保持 Site 待定，原技术修订相同的名称等编辑继续保存为 `DRAFT_ONLY`，保留 `SITE_REQUIRED`、快照及历史回执。
- 省略映射和显式重选同一版本均使用上一版范围。待定 Site 被设置或技术修订改变时返回 422 `ENGINEERING_MAPPING_USAGE_SCOPE_MISMATCH`。显式新映射沿原导入校验。已选 Site 的修订继续核对准确 Site、已确认的技术版本及当前授权。
- WP04-33 组合查询选择待定 Site 的映射时，保留 `DUAL_OWNER_ENGINEERING_MAPPING` 和 `LEGAL_ENTITY_APPLICABILITY`，`canAdoptIdentitySite` 为 `false`。现有 HTTP 用例已补上该断言。

## 卡验收与剩余出口

| 验收／出口 | PostgreSQL 用例与结论 |
| --- | --- |
| 同一工程身份跨两个工厂，保留各 ERP 料号、Site、法人及双方 Owner | `Wp0502_two_sites_keep_their_own_legal_scope_and_conflicting_owner_facts_are_rejected` 与 `EngineeringMapping_two_sites_keep_original_ERP_keys_and_require_both_Owner_actions` PASS。 |
| 同原键返回同身份及映射版本，竞争与未知提交按原键恢复 | `EngineeringProduct_actual_HTTP_creation_replay_and_current_revocation`、`Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` 与 `EngineeringMapping_commit_failure_rolls_back_confirmation_and_original_then_exact_CAS_writers_arbitrate` PASS。 |
| 同名异规格保持分立，未核对或未确认映射不产生可采用事实 | `EngineeringProduct_duplicate_codes_do_not_merge_identity_and_history_CAS_remains_immutable`、`Wp0502_same_item_code_at_another_site_does_not_supply_technical_equivalence` 与 `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft` PASS。 |
| 工程角色更新 ERP 价格、库存被拒，同码跨租户不可恢复 | `Wp0502_erp_entry_resolves_native_product_uuid_and_engineering_authority_cannot_edit_price_or_stock` 与 `Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` PASS。 |
| 出口 1 法人归属和适用域 | `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft`、`Wp0502_two_sites_keep_their_own_legal_scope_and_conflicting_owner_facts_are_rejected` 与 `Wp0502_r1_qualification_uses_only_the_selected_site_legal_scope` PASS。同一产品选 Site A 的已确认映射时返回准确法人范围。选 Site B 的缺证映射时法人项仍为 `REQUIRED_FENCED`。 |
| 出口 2 双 Owner 旧映射确认与历史 | `Wp0502_http_distinguishes_scoped_dual_owner_confirmation_from_legacy_draft`、`Wp0502_scoped_review_confirmation_and_recovery_keep_authority_and_tenant_boundaries` 与 `Wp0502_mapping_forward_index_preserves_old_original_receipt_and_current_permissions` PASS。准确旧映射补证可读时从 `UNKNOWN` 转为 `KNOWN`，原版字节及原回执可回读，未补证保持 `UNKNOWN`。停用保留经当前授权可见的历史。 |
| 出口 3 WP10-47 原工程序列 | `Wp0502_wp1047_engineering_sequence_pins_original_mapping_and_current_revision_and_authority`、`Operational_catalogue_approved_native_ratio_can_be_pinned_by_draft_bom_before_stock_posting`、`Wp0502_r1_bom_revise_rechecks_inherited_mapping_scope_and_keeps_original_snapshot` 与 `Wp0502_r1_routing_revise_rechecks_inherited_mapping_scope_and_keeps_original_snapshot` PASS。初始采用及修订均核对 Site 和技术修订，匹配修订与历史回读保留原快照，撤权拒绝读取。 |
| 出口 3 R2 待定 Site 修订 | `Wp0502_r2_bom_revise_keeps_pending_site_and_rejects_scope_changes`、`Wp0502_r2_routing_revise_keeps_pending_site_and_rejects_scope_changes` PASS。只改名称可保存待定草稿及原快照，设置 Site 或换技术修订被拒，原版本和原回执可回读。 |
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

- Windows，.NET SDK 10.0.404 Release。锁定还原和测试使用同一 `--artifacts-path`。PostgreSQL 18.6 使用 `127.0.0.1:55458` 和各轮新建的 `permission-consumption-*` 目录。法人、Site、产品、角色、价格、库存与工程原件均为合成。HTTP 用例访问本机 Main Kestrel，环境变量及子进程配置由脚本记录。[.NET 复现脚本](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/run-validation.ps1>)
- R2 源码候选一次运行 **95 通过、0 失败、0 跳过**，逐名包含 R1 全量 93 项及 R2 新增两项。源码树 `00329709d4c7312c2e4db65b109f2ca73532d839`，TRX SHA-256 `dc667ee627dc5b7b58f1f35017f286b0fdb422a679573843e2247d1d0f26df0d`。[R2 源码候选证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-source-r2.json>) · [R2 源码候选 TRX](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/results-source-r2/wp05-02-source-r2.trx>)
- R2 最终候选使用独立构建目录和 PostgreSQL 夹具，在一条 `dotnet test` 命令中重跑全部 95 项。最终 TRX 的 SHA-256、候选提交和树记录在同一份验证原件，`HeadTree` 与 `InputTree` 对应含本报告的最终候选。[最终 .NET 证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-final.json>) · [最终全量 TRX](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/results-final/wp05-02-final.trx>) · [95 项清单](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/expected-dotnet-tests.txt>)
- 前端复跑六个 Vitest 套件共 53 项、`vue-tsc --build --force` 与 Vite 构建。复现脚本将结构化计数投影到控制台，最终读回核对控制台与验证文件的一致性。[最终前端证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/frontend-final/validation.json>) · [控制台摘要](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/frontend-final-console.json>) · [前端复现脚本](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/run-frontend.ps1>)
- R0 的 WP04-33 与 WP10-47 受影响用例包含在实测树 `0091eae0e32cbda31854073e8c60ccf6b24fa4bc` 的 89 项主回归中。PG04-A176 单项证据对应 `72393a047e792f0f7acb505492948a1eacff9113`。R1 的 93 项全量对应树 `8bf5f5ea681a3873e0a7eb591832125bbc783564`，执行侧 TRX SHA-256 为 `5714f053ada107769286faa436617d4dd2134542299f146b3393136927d50ab5`。[R0 主回归](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation-candidate.json>) · [R0 PG04-A176](<D:/CP6-test-artifacts/WP05-02/exec-20261126T114139Z/validation-samecode.json>) · [R1 同树证明](<D:/CP6-test-artifacts/WP05-02/exec-20261126T123943Z/validation-final.json>)
- R2 两条新用例在 R1 上复现只改名称被拒。定向复查补验显式重选同一映射的范围变更，最终两条 R2 用例与三条 R1 用例均通过。[R2 初次复现](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-red-r2.json>) · [重选旧映射复现](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-red-r2-same-ref.json>) · [最终定向验证](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation-green-r2-scope.json>)
- 两侧工作流事件、过滤和依赖按实际 Git 对象及变更路径核对。W150-01 的稳定身份、事务和 Site 事实与 W310-01 的余项核销继续作为本卡基础。最终读回核对各轮 PostgreSQL 实例退出、PID 文件、监听和进程。[最终工作流核对](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/workflow-final.json>) · [停机与进程读回](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation.json>)

## 未证、有效工时与席位

- **UNPROVEN** 真实企业法人归属、原始业务单据和实际远端采用。测试夹具的证据模式为合成。
- **NOT_RUN** 制造资格 WP05-32/42、产品币种绑定 WP04-04、整目标的跨 Owner 场景及多输出修订验收、SQL Server、生产环境迁移或部署、Actions、远端合并后冒烟及浏览器端到端。工程技术适用域在本地工作修订上受验。
- 首轮有效时间 **2821.17 秒**，R1 **2210.13 秒**。R2 计时从 `2026-11-26T13:30:40.9975009Z` 起。R2 单独工时及合计见最终读回。M 卡额度 14400 秒，150% 阈值 21600 秒。[有效时间](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation.json>)
- R2 候选在已远端保存的 R1 提交后追加，保存在本地 `codex/wp05-02`。DEV-1 的 `CP6:WP05-02` 登记为 `active`，远端保存、独立复核、PR 及后续台账交由总控。[分支与席位](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/validation.json>) · [远端读回](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/remote-readback.json>) · [全差异](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/diff-stat.txt>) · [R2 差异](<D:/CP6-test-artifacts/WP05-02/exec-20261126T133201Z/rework-diff-stat.txt>)
