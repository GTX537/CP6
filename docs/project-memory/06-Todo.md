# 当前待办与优先级

## P0：Space Studio v1.3 GA 外部与扩展门禁

- WP3 的 Site 级认证数据模型、管理/查询接口、Primary/Backup 选择、合规故障切换和向导能力展示已完成仓库基础。下一张 Provider 任务卡必须在现有 `ICadConverter`/Provider 注册边界实现真实 ODA、APS 或评分后替代者的适配器与隔离 Worker 注册；不得把供应商类型写入领域层，也不得允许客户端提交任意 Provider Key。
- 为每个启用 CAD GA 的 Site 接入并实测一个主 DWG/DXF Provider 和一个同合同、同黄金集、同 Site 审批的备用 Provider；补齐法务、安全、数据区域、删除保留、Secret 管理和审批证据。当前默认运行注册为空，能力接口会失败关闭，不能作为真实 CAD 验收。
- 在已配置 `CP6_TEST_SQLSERVER` 的真实 SQL Server 执行 `SpaceCadProviderSqlServerTests`，保存并发替换、唯一 Current Revision、历史追加、认证不可变和幂等迁移证据；本机 skip 不算关闭此门禁。
- CAD 起始向导、sealed Preparation、parse start fence 和 Site 能力检查已完成仓库内闭环；画布拖放精调保持后续 UX 卡，旧 `FloorEditor` 不继续发展为第二套权威。
- WP4 的图片底图标定入口、Excel–CAD 深链审核/确认及 DWG/DXF 分格式浏览器合同已完成仓库内自动化；仍须用授权真实 DWG、DXF、Excel 和 PDF/图片在两条已认证 Provider 链及 CP6 WMS 环境完成端到端证据。Mock/fixture 结果不得计入黄金 CAD、性能、恢复或 Pilot 完成度。
- 用 20 份授权真实黄金 CAD 执行 10/5/5 Calibration/Validation/Holdout，产出覆盖率、准确率、高置信度精确率和 Blocking 遗漏证据。
- 工作台 GA 快捷键、问题定位、标准 tab 焦点、窄屏 3D 保持、字号/主要热区、2D/3D 同源选择和逐 Version+Floor 视角恢复已有仓库内自动化；仍须在独立验收环境完成 4.5:1 对比度、真实键盘/辅助技术与 1440×900/1280×720 人工 UX 签字。在 Iris Xe/WebGL2/500 货架/10,000 库位跑正式 Viewer 门槛，核验 Published-only 生产 Viewer 并形成性能报告。
- WP6 已完成 Warning 集合绑定的 Preview → 显式确认 → Publish fence；仍须在真实 SQL Server 与 CP6 WMS 环境运行发布成功、超时自动恢复、部分写入对账、同 PublishPlan 重试无重复、历史重发及旧 Published 持续服务证据，并完成监控、告警、备份恢复、运行手册和安全矩阵演练。当前 104 个 SQL/environment-gated Space Integration 用例的 skip 不得计为通过。
- 执行一个绿地仓和一个存量仓各 14 天 Pilot、WMS 故障恢复与对账演练，并取得产品、QA、WMS、架构、安全签字后才可声明核心 GA。
## 已完成：OpenAPI 原生客户端漂移门禁

- PowerShell 版本差异与全局无关 schema 导致的假阳性已消除；门禁现在只哈希原生客户端路径及递归可达 schema，并使用 Node.js 稳定规范化。
- Node 20/22 单测、真实 Swagger check、.NET/Client/Web 和 R2 source gate 均已通过。该项不再作为 CRM PR #5 的归因问题；修复合入 `main` 后应更新 PR #5 基线并重跑 GitHub Actions。

## P0：Azure DevOps Release/CD 演进

- 外部 Readiness Pipeline 已命名为 `CP6 Deploy Agent`；可再补全为 `CP6 Deploy Agent Readiness`，并保持 `CP6-Deploy` Pool 未对所有 Pipelines 开放。
- Readiness Build ID `10` 已通过；`cp6-dev-secrets` Variable Group 和四个锁定 Secret 已由截图确认创建。下一步从 `/azure-pipelines-dev.yml` 创建 `CP6 DEV CD`，只对它授权 `CP6-Deploy`、`cp6-dev-secrets` 与 `cp6-dev`，再用最新成功的 `GTX537.CP6/main` Run 完成首次部署验收。
- 首次 DEV Run 必须保存 Build/Run ID、Environment deployment history 和 `cp6-dev-evidence`；在这些外部证据齐全前，只能称为“仓库配置已交付”，不能称为“DEV 自动部署已成功”。
- 本机 DEV/UAT/PROD-LAB Docker 运行边界已建立并实际验证；Azure DevOps 的 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 也已由 2026-08-11 外部截图确认创建。下一步在详情页核对三者 Resource 为空，并确认没有录入 Secret。
- DEV 学习 Pipeline 已有独立 deployment job；UAT/PROD-LAB 不得复制本机重新 Build 方案。完成 Registry/发布权威决策后，再创建不可变候选推广 Pipeline，并为 UAT/PROD-LAB 配置审批与 exclusive lock。单人学习期 PROD-LAB 可自批，真实生产必须换独立批准人。
- 当前 Azure `azure-pipelines.yml` 已完成基础 CI，使用 `Default` self-hosted pool、`main` trigger 和 `pr: none`；先补运行证据、Agent 运维边界和 PR 门禁归属，不把 CI 绿灯描述为上线。
- 下一张 CRM 相关任务卡为 M0/R00：把 CRM V1 已锁定的 GHCR/R2 唯一权威、候选清单、Azure 非权威影子边界、等价矩阵和回退写入 ADR；不得重新选择 Registry。ACR 迁移与其他产品的长期 Azure Registry 决策独立立项。
- 决策通过后按独立任务推进：Docker Release（版本/SHA、provenance、SBOM、扫描、digest）→ DEV Environment/健康与身份核对 → UAT → PROD 资源侧审批 → 回滚/前滚演练 → AKS 多仓。
- 全阶段遵守 Build once：DEV/UAT/PROD 只推广同一 digest，不按环境重新 Build；Azure 与 GitHub 不得对同一版本生成两套权威候选。
- CRM V1 全周期固定使用 GitHub R2/GHCR 作为候选权威；Azure 即使达到等价也只能在本 Epic 内消费相同 digest 或做非权威验证。其他产品的未来显式切换继续按 `docs/devops/AZURE-PIPELINES-PLAN.md` 独立决策。

## P0：CRM V1 端到端交付

- 产品框架和三仓可执行 Spec 已批准为 implementation-planning baseline，入口为 `docs/crm/README.md`；Foundation 的 20 张表、固定状态机、迁移、6 个禁用菜单节点和 22 个动作只作为迁移源与兼容语义，不是目标服务实现。
- 先完成 M0/R00 ADR，冻结 GHCR/R2 权威、Azure SQL/Emergency Intake、System Manifest 整体回退，并取得 Sponsor、Product、Sales Operations、Architecture、Security、Data、ERP、SRE、QA、Release 的 named Owner、Pilot cohort 与 Observation Gate 证据；缺失即 No-Go。
- 不要现在建立 `GTX537/CP6.CRM` 空仓。只有 T1 已在最新 main、M0 输入关闭且 P01 runner/合同可消费后，才由 CRM01-S01 创建私有仓库；V1 不实现软件产品目录、商城、订阅或客户产品中心。
- 第一阶段可并行推进 Platform P01–P07、CP6 C01–C03、CRM01–CRM03；随后只实现 CRM04 的 Lead Pilot 子集、C 分栏工作台、真实 Dapr/Kafka Intake、两租户负向与 Pilot 性能 Smoke。Pilot UAT 通过后才解锁 CRM04 余项、CRM05–CRM10、完整 ERP/CMS 旅程，再进入 P08–P10、C04A、CRM11/CRM12。
- Intake 必须覆盖人工录入、同源 BFF 官网提交、稳定 attempt、Needs Review release/reject/expiry、原 ReceivedAt 首次响应 SLA、Emergency Intake、线索池、分配/移交、协作人、活动时间线、重复候选与受控合并；数据范围按负责人、协作人、部门和管理员显式校验。
- 后续能力为企业/联系人/商机转化、报价接受和 ERP 订单桥接、独立 CRM Next.js 工作台、营销官网 CMS/多语言 SSR/ISR、PII 24 个月匿名化、SLA 通知及漏斗/来源报表。
- CRM09 开始前必须批准首页、能力/行业和联系/回执的桌面/平板/移动高保真稿与受控 CMS Schema；完整 UAT 必须使用真实 C03 handler 和隔离 ERP SQL，Mock 只允许单元测试。
- 单次生产切换后依次执行 ≥10 工作日/≥200 Eligible Lead 的 Lead Adoption 和最多 30 日 Full Journey Gate；技术绿灯、部署成功或菜单可见都不能关闭 Epic。两项通过并完成只读观察后才执行 C04B，旧表物理删除另立任务。

## 已完成：Space `CodeEngineService` Zone 级 rackSeq

- 批量 `GenerateAsync` 与单格 `GenSingleAsync` 已复用同一套 Zone 级 `(X, Y, Id)` 确定性货架排序；单格生成不再把非首架简化为序号 `1`。
- CodeEngine/LocationPublish 聚焦回归 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，该项不再是跨波待办。

## 已完成：FIN BudgetLine 版本级并发控制

- `BudgetVersion.RowVersion` 已成为预算行聚合并发边界；新增、编辑、删除和 Excel 确认导入必须携带客户端最后读到的版本令牌，任一预算行写入都会推进共享版本令牌。
- 不同预算桶的两个写者也会在同一版本令牌上串行化；陈旧令牌统一返回 `E-A5-CONCURRENCY-001`，前端冲突或成功后同时刷新版本和行令牌。Excel 确认导入参与单一事务，不再忽略内部 upsert 失败或留下部分提交。
- 门禁为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped；本机 `KOUSQLSERVER` 原生 `rowversion` 用例 1/1、前端令牌合同 3/3、Vue type-check、WebApi Release 0 warning / 0 error。该项不再是跨波待办。

## 已完成：PLAN/PUB Attachment 宿主业务权限

- Attachment 继续作为无独立页面的横切组件，不新增暗菜单；六个读写入口已按 `BizType` 回查宿主菜单，缺省失败关闭，rebind 另校验草稿上传人。
- `PubUpload` 已要求宿主 `writePermission` action key，无写权限时隐藏上传/删除，后端权限不依赖前端隐藏。
- 后端聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 environment-gated skipped / 0 failed、前端聚焦 3/3、全量 716/716、Vue type-check 与 production build 通过、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：分支优先规范与本地配置优先级

- `main@e4e33364` 已包含仓库级分支优先规则；后续任务除有记录的技术例外外，均需独立分支、验证后合并并推送。
- `e3bf2420` 已修复 `appsettings.Local.json` 的配置源插入边界，只在无前缀环境变量源前插入，环境变量和命令行继续保持最高优先级。
- 配置聚焦 4/4、OpenAPI 30/30、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：WF 通知定向用户推送

- 当前运行链路由 `PersistentWfNotifier` 在事务内写 outbox，提交后由 `WfNotificationDispatchWorker` 以 `Clients.User(userId)` 定向派送；`NotifyHub` 要求认证。
- 未注册的 `SignalRWfNotifier` 及不可达的 `Clients.All` 回退已经删除，项目中不再存在工作流通知广播后由客户端过滤的路径。
- 门禁为通知聚焦 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。该项不再是待办。

## 已完成：`main` 同步与 P2.5 受控整合

- 远端 `main` 的文档同步基线 `e4e33364` 已包含 PR #2 的 `8045d872`、P2.5 受控整合 `030a97b9`、Space 权限/文档对齐 `b2a91680` 和分支优先流程；原始 5 个冲突已按权威边界解决，Docker Cookie 修复已由 `0fc6f529` 等价纳入。
- Analytics Control Tower 原始提交 `9b48ffbb` / `dd6637ea` 已从当前 `main` 选择性整合：保留 E10 Runtime/Viewer 真相源，加入独立控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器。
- 原 `20260720035903` 迁移未进入主线；已基于当前 ModelSnapshot 生成 `20260809092206_SpaceAnalyticsControlTowerCurrent`，只创建两张分析表和三个索引。
- 客户端心跳时序断言已稳定：测试周期与“事件立即唤醒”解耦，定向 50/50、客户端全量 71/71；生产心跳实现未修改。
- `deploy/production/sql/main-sync-20260808` 已包含 14 个 Core + 36 个 Space 幂等迁移、preflight/postflight；LocalDB 从远端 main 基线双执行 2/2，history 14/36，51083/51000/51020 失败关闭通过。仍需在生产备份恢复副本重复同一演练。
- P2.5 门禁已通过 WebApi build、前端 type-check/生产 build、全量 Vitest、Space UnitTests 和 CP6.Tests 并进入远端 `main`；标签、R2 候选和生产部署仍需另行审批。
- 完整依据：`docs/space/reports/2026-08-08-main-merge-readiness.md`。

## 已完成：E13 无锁 Zone 父关系确定性推导

- `d19a5300` 已实现 `warehouse-rule-only-v2`：无人工父关系锁的 Aisle/Rack 仅在一个确定性 Zone Polygon 完整包含子几何时推导 `relations.zoneSourceKey`；零候选和多候选继续 Blocking，不猜测。
- 人工锁、确定性规则、AI 的优先级保持不变；AI 冲突和父关系环失败关闭，BuildScene 不重复落同一问题。`warehouse-rule-only-v1` 冻结 Run 与恢复链保持旧行为。
- 门禁为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 skipped、完整 Release/AOT 0 warning / 0 error。证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## P0：Space V1 下一批受控实现

- 当前功能检查点为 RackGenerationProfile 权威链功能提交 `19d32650`；Generation Run Web 入口 `52bb3a29` / `282d4e54` / `2871df1b`、首次创建 `770bdc96` / `bbcaf6fe` / `9d0971f4` 与纯规则 BuildScene `36cc0241` / `89c6fb2a` / `9e7f7e0a` 均已进入 `main`。E07 S01–S05、E13 S01–S13/S16/S17、E13-S14 工程切片、E03 S01–S05 及其报告不要重复实现。
- E13-S11 已完成用户可见取消安全点、同输入重试分类、权威 CommandBatch 对账、Failed/Stale replacement Run、RuleOnly 降级和真库运维演练；生产默认 BuildScene executor 现可让 RuleOnly recovery 从权威 PreviewSet 到 AwaitingReview，且零 Provider、零 Usage、零 Draft 写入。Provider-backed 模式仍失败关闭，不能描述成真实外部 Provider 端到端完成。
- E13-S13 已完成外部主体在 16 个 AI 操作及 Gateway 的稳定 403 拒绝、External Provider 字段/Token 外发白名单、7 个 GET 读审计和 Customer/Supplier/3PL 矩阵；生产没有真实外部 Provider，不能把门禁实现描述成网络端到端签收。
- E13-S17 已完成加法 Migration、幂等 SQL、Tenant 清理 Job、90/365 天保留、保留锁、同租户并发租约和 forward-fix 操作说明；生产定时器仍需受控 Worker 配置专用 service principal。
- E13-S14 的离线评估器、Calibration-only 阈值校准、样本外 Wilson 门禁、规范报告哈希和命令入口已完成；正式 S14 仍需获授权 20 份黄金 CAD、10/5/5、L1～L5、两人标注与 QA 仲裁、真实版本/Provider 输出、人工操作实测、完整性审计和签字。开发合成数据不得计入发布。S15/S19 继续等待供应商合规、影子运行、试点和独立审批证据；S18 依赖 S15，不能提前签收。
- E13-S10 已消费 E13-S09 Decision 并原子写入 Draft；真实 Worker 的同 SourceHash、已确认 `LoadLockedFacts` 已自动接入 RuleOnly 融合并重映射名称、allowlisted 属性和父关系。不同 SourceHash 的确定性几何建议继承与人工确认仍未完成，不能用猜测匹配绕过失败关闭。
- RackGenerationProfile 权威头/不可变版本、Tenant-only 创建、System/Tenant 读取、Run 冻结、Worker 消费和 Web 显式选择已由 `19d32650` 完成；不要再以 Asset 或任意 GUID 替代。无人工锁时的确定性 Zone 父关系推导已由 `d19a5300` 完成。下一张独立产品卡可处理不同 SourceHash 的几何匹配、建议展示与人工确认；在确认闭环完成前不得自动继承或 Apply。现有方案追加 v2、System 配置和完整管理 UI 也保持独立，仍不能发明默认尺寸或关系。
- 继续保持批量 High Accept 默认关闭、原始 CAD 不外发、外部 Provider 默认关闭、配额失败关闭、规则路径不依赖 Provider，以及 Draft/Published/WMS/设备边界隔离。
- E02 S01 中立实验工具已集成，但最终签收仍需数据/QA 提供正式 20 文件黄金集（Calibration 10 / Validation 5 / Holdout 5、L1–L5 各至少 4）及 DWG/DXF 版本/实体矩阵。
- 法务/采购需确认 ODA 正式 Web/SaaS 授权；工程需获得校验过的 ODA Windows/Linux SDK 包。APS 备试需批准区域、DPA、删除/保留证据和非生产凭据。平台/安全需提供 8 vCPU / 32GiB 的冻结隔离 Worker。
- 外部输入齐全后，在同一冻结环境对 ODA 与 APS 各黄金样本 5 次、50MiB/100 万实体/200MiB 上限、超时/取消/并发进行评分；低于 ADR-0001 的 80 分硬门槛不得主选，若都失败则继续阻断 DWG Beta。
- 本机 `KOUSQLSERVER` 已用于 E13-S17 的迁移、重复清理、并发租约和幂等 SQL 双执行，结果 3/3、0 skipped；随后 Version Clone 缺失的 Zone/Aisle/Rack `Name` 与 Rack `RackType` 映射已修复，完整 Space Integration 现为 336/336 passed、0 skipped。该项不再是待办。
- `0d25da4d` 中 E05–E12 是候选证据，不得整包 merge/cherry-pick；必须重新核对依赖、迁移链和产品冻结范围。
- P2.5 已在当前主线按现行 E10 数据边界完成受控整合；历史 P2.5 分支仅保留追溯用途，不再整段 merge/cherry-pick。

## 已完成：GR-VP 波

权威计划：`docs/superpowers/plans/2026-07-17-general-role-vperm.md`。

1. T6 已通过 `d79a39c` 合入并推送 `main`。
2. API/Web 双镜像已重建并运行，Web 使用干净 `main` 产物。
3. 当前注册租户仅 `DEFAULT/A1`，已验证 RoleId=10 的 4 菜单/8 动作、admin 零扰动，以及 `qa_general` 的本人审批、归属闸和无权端点 403。

T1–T7 已完成，不要重复铺设。T7 细节见 `.superpowers/sdd/gr-vp-t7-report.md`。

## P1：GR-VP 收口票

- PMS/Sys 平台管理页仍未统一铺 `v-permission`。
- 决定标准角色是否使用日文显示名。
- 为 B1/C1 等租户建立/挂接一般用户。
- 评估 insert-only 标准角色种子是否应在管理员删除基线键后自动补回。
- 若后续恢复的数据库重新出现 B1/C1/D1，补跑 T7 四租户 SQL 矩阵；当前库只有 A1，不要为凑验收数虚构租户。

## 已知跨波跟踪票

- WFS/Space 各 plan 文末保留若干 live QA、移动端视觉和清理票；动手前读对应最新计划的“完成后跟踪票”。

## 文档维护任务

- 每完成一项任务，更新 `PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md` 和 `CHANGELOG-AI.md`。
- README/CODEMAP 的规模数字已经过时，未来可单独刷新，但不要在权限任务中夹带修改。
