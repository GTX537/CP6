# AI 可读变更日志

> 依据 Git log 汇总，不替代完整 Git 历史。重点记录影响接手判断的里程碑。

## 2026-08-14：Space Studio WP6 发布恢复可观测性基础

- 新增跨租户、无业务标识标签的 Publish Recovery 聚合器与 Prometheus Gauge，按固定三种恢复状态输出活动数量、最老等待时长、SLO 超时数量和 15 分钟/4 小时目标。
- 新增自动恢复超时、人工恢复/对账超时和指标缺失告警规则，并补齐正式 Retry/Reconcile、旧 Published 连续服务、幂等与证据要求的运行手册；权威 Spec 同步冻结低基数和生产等价演练要求。
- 聚焦合同测试与 WebApi 构建通过。真实 SQL/WMS 用例在本机仍因环境门禁 skipped，规则尚未部署到真实通知链；本卡只交付仓库可观测性基础，不等于 WP6 或核心 GA 完成。

## 2026-08-14：Space Studio WP6 发布 Warning 明确认领

- Publish Preview 新增与 ValidationRun 和 Warning Issue 集绑定的确认哈希；发布请求在存在 Warning 时必须携带该哈希，缺失返回稳定 422，集合变化返回 409 并要求刷新。
- 发布控制面新增独立 Warning 复核勾选，不再用通用风险确认代替；历史重发遇到新 Warning 会停在生成的 Ready 版本等待人工确认，旧 Published 不受影响。
- OpenAPI、C#/TypeScript SDK、Spec、错误码和自动化同步更新。仓库门禁通过，真实 SQL/CP6 WMS 恢复演练、监控告警、双仓 Pilot 与五方签字仍未完成，本卡不等于 WP6/核心 GA。

## 2026-08-14：Space Studio WP4 底图与 Excel–CAD 工作台路径闭环

- 底图上传并挂接后，工作台上下文面板现在显示“标定底图”，已标定来源可重新标定；只读状态禁用写入口。三点标定继续写既有 Design V1 calibration 合同。
- `matchJobId` 深链会自动打开问题域与 Excel–CAD 权威匹配面板；匹配行可定位 Draft Rack，显式确认仍绑定 Artifact Hash 和 Expected Content Revision 后 Apply。
- Playwright 新增图片底图、Excel–CAD、DWG、DXF 四条路径证据；Web 762、Space Studio Playwright 13、Vue type-check、production build 与 diff whitespace 通过。测试使用受控 API fixture，真实 Provider/文件/WMS/Pilot 门禁未完成，本切片不等于 WP4 或核心 GA。

## 2026-08-14：Space Studio WP5 2D/3D 同源选择与逐楼层视角恢复

- 草稿 3D 增加参数化场景 raycast 拾取，Element/Zone/Aisle/Rack 使用同一 Design LogicalId，RackLevel 回到父 Rack；点选进入既有工作台选择状态，Ctrl/Command 切换选择，Orbit 拖动不误触。
- 2D pan/zoom、投影模式和 3D camera/target 以 Version+Floor 为 scope 保存在当前浏览器标签页；相同楼层刷新恢复，楼层切换隔离，损坏/越界/旧 schema 状态拒绝，新楼层重新 framing。
- Web 761、Space Studio Playwright 10、Vue type-check、production build 与 diff whitespace 通过。Iris Xe 500/10,000 性能、Published Viewer 真机、独立 UX/辅助技术验收和现场签字未完成，本切片不等于 WP5/核心 GA。

## 2026-08-14：Space Studio WP5 工作台键盘与可达性闭环

- 检查器改为标准 tab 键盘模型，补齐工具选中语义、快捷键元数据、状态播报、画布焦点和统一焦点环；`G` 按严重度循环定位 Open 问题，窄屏只读模式保持 3D 并同步选择。
- CAD/Excel/属性/WMS/3D 核心面板统一 Space Studio token、正文/元数据字号与 44px 主要热区，修复固定宽度面板在 324px 检查器中的溢出及暗色工作台内的颜色继承不一致。
- Web 754、Space Studio Playwright 9、Vue type-check、production build 与 diff whitespace 通过。Iris Xe 500/10,000 性能、独立对比度/辅助技术验收和现场签字未完成，本切片不等于 WP5/核心 GA。

## 2026-08-14：Space Studio WP3 Site CAD Provider 认证与路由基础

- 新增 Tenant/Site 级版本化 CAD Provider 配置、Primary/Backup 认证记录、专用管理权限及只读能力接口；部署模式、数据边界、有效期、格式、审批和 Secret 引用均由服务端校验，Secret 内容不回传，认证历史不可修改。
- Preparation/Parse 统一经合规路由，只使用当前 Site 已认证且运行可用的 Provider；Primary 可在可重试资源故障时切至同配置 Backup，未认证链、未批准云边界和 Backup 反向切换均失败关闭。Parse payload v3 绑定 Preparation Provider 与 Semantic Preview Hash。
- Space Studio 起始向导显示 Site 主备状态与阻断码，无有效链时禁止扫描轮询和 Preview；两条链都有效、运行可用且覆盖 DWG/DXF 才报告 `CadGaReady`。
- 验证通过：Release solution 0 warning / 0 error；.NET 3,753 passed、123 environment-gated skipped，Web 754、Playwright 8，Vue type-check、production build、OpenAPI/双 SDK、EF 与 diff drift 均绿色。真实 SQL 和真实 ODA/APS Worker/审批证据尚未完成，本切片不等于 CAD GA。

## 2026-08-14：Space Studio WP2 CAD 起始向导

- 新增扫描状态、Mapping Profile 查询与 CAD preparation preview；服务端通过受控 Provider 边界生成坐标、Inventory、Mapping 和 Semantic Preview，并保存绑定来源、楼层、Draft 基线与全部 Hash 的两小时 sealed Preparation。
- 原有 `StartSpaceCadParseRequest` 新增必填 `preparationId`；解析启动会拒绝伪造、过期或 stale Preparation，不产生 Job 或 Draft 写入。OpenAPI、C#/TypeScript SDK、稳定错误码、权限和可回滚迁移同步更新。
- Space Studio 上传后进入四步向导，单位与 Profile 无静默默认值，语义对象、置信度与阻断摘要可见；转换和映射必须分别勾选确认后才能启动原有解析 Job。默认 Provider 仍失败关闭，真实 Site 主备 Provider、黄金 CAD、Pilot 与签字仍是后续硬门槛。
- 验证通过：完整 Release solution 0 warning / 0 error；.NET 3,744 passed / 122 个既有环境用例 skipped，Space Unit 501、CAD 聚焦 12、OpenAPI/权限/Controller 81、Web 752、Playwright 8；Vue type-check、生产构建、SDK/EF/diff 漂移门禁通过。真实 SQL 未配置，未把 skipped 场景算作完成证据。

## 2026-08-13：Space Studio WP1 设计态库位批量编码

- 在独立 `codex/space-layout-bulk-coding` 中交付 Design V1 `location-codes:preview` → `location-codes:apply`，复用既有编码规则语义但只写 `SpaceLocationRevision`，不调用旧运行态编码写服务，也不触碰 Published/WMS。
- 服务端按 Zone/Floor/Tenant 默认优先级选规则；Preview 绑定双 Revision、完整差异与 Proposal Hash 且零写入，Apply 在 Floor applock/Serializable 事务内复算，并用租约、双 Revision、Proposal Hash 和幂等命令包关闭 stale/重复写入。WMS Bound、Adopted、Imported、Manual 编码不可被覆盖，重建审计保留真实 before/after。
- Space Studio 批量域提供填空/重建、整层/单库区、逐项差异、保护原因与显式确认；普通失败保留原 commandBatchId 重试，Revision/规则变更要求重新 Preview。OpenAPI、C#/TypeScript SDK、稳定错误、权限和自动化同步更新。
- 当前已通过 Space Unit 501、Space Web/API 聚焦 501、Web 749、真实 SQL 1、OpenAPI/权限 73、Space Studio Playwright 7、完整 Release solution 0 warning / 0 error、Vue type-check、生产构建与 SDK drift；Provider、黄金 CAD、Viewer 真机、WMS 恢复、双仓 Pilot 和五方签字仍未完成，未将本卡描述为 GA。

## 2026-08-13：Space Studio WP1 布局修改与级联删除

- 在独立 `codex/space-layout-update-delete` 中扩展 Design V1 Layout Command，交付 Zone/Aisle/Rack 修改与删除；所有写入继续使用租约、Floor/Content Revision、幂等和原子事务，不触碰 Published/WMS。
- Rack 规格修改保留仍存在的层/库位身份、编码和绑定，新库位保持未编码；删除默认保护含子对象的布局，只有显式 `cascade=true` 才把设计态子树标记为 `RemoveRequested`。命令审计记录真实 before/after，冲突返回稳定 Problem Details 与恢复动作。
- Space Studio 画布与右侧属性域支持三类布局对象的选择和修改，鼠标及键盘删除都要求级联确认；OpenAPI、C#/TypeScript SDK、SQL/契约/组件/E2E 自动化同步更新。
- 验证通过：真实 SQL 聚焦回归 1/1、OpenAPI 38/38、Web 744/744、Space Studio Playwright 6/6、Vue type-check、生产构建、SDK drift、完整 solution 0 warning / 0 error 和 diff whitespace。本卡不代表 WP1/GA 完成，批量编码 Preview → Apply 仍待独立交付。

## 2026-08-13：Space Studio WP1 工作台创建接入

- 在独立 `codex/space-layout-workbench-create` 中把 Design V1 Layout Command 接入 Space Studio“构件”上下文，提供 Zone/Aisle/Rack 表单、画布坐标、逐层货架规格、库位数预览和可选编码前缀；保存、租约丢失、Revision 冲突、导出与重放继续使用工作台统一状态。
- 扩展共享参数化 Design 渲染计划，使 Zone/Aisle 权威多边形与 Rack/Element 一起进入 2D/3D；布局上下文不可选择，避免误走当前只支持通用 Element/Rack 的编辑命令。新增机器清单一致性回归。
- Web 全量 740/740、Space Studio Playwright 6/6、Vue type-check、生产构建和 diff whitespace 通过。修改/删除、级联语义和批量编码仍按独立任务交付，未将该卡记为 WP1 或 GA 完成。

## 2026-08-13：Space Studio WP1 Layout Command 创建链

- 在 `main@9c320a74` 上建立独立 `codex/space-layout-command-v1`，新增 Design V1 `/layout-commands` 原子写入口和 C#/TypeScript SDK；Zone、Aisle、Rack 继续写设计态 Revision，RackLevel/Location 由逐层规格与确定性身份算法生成，不伪装成 `Space_Element`。
- 写入统一受编辑租约、Floor Revision、Content Revision、幂等回放和命令审计保护；任一父级/代码/身份冲突整批零写入。库位编码前缀为显式输入，未提供时保持未编码，为后续 Preview → Apply 批量编码保留权威边界。
- 分支验证为 Release solution 0 warning / 0 error、Space Unit 497、真实 SQL Space Integration 397（0 skipped）、CP6.Tests 2868、Web 729、Vue type-check/生产构建与 SDK/EF drift clean。
- 任务提交 `77256dd9` 已通过 `289b51d0` 合入并推送远端 `main`；后续仍需工作台 UI、修改/删除、批量编码及完整门禁，未将该切片记为 WP1 或 GA 完成。

## 2026-08-13：Space Studio v1.3 核心实现与预发布收口

- 以 v1.2 的完整详细正文为底稿增量修订低成本 3D 建模 Spec 到 v1.3；RFC-003 补齐影响、兼容、测试、回滚和五方批准表，并明确“产品冻结”不等于跨职能 RFC 生效或 GA。
- 将 `DesignUnderlayView` 收敛为独立 Space Studio 子主题四栏工作台，增加状态栏、任务清单、检查器域、窄屏只读、保存状态与恢复草稿。
- 新增带数据库唯一槽、数据库 UTC、会话 fence 和 rowversion 的 Floor 编辑租约；保存与租约写入共用 Floor applock，接管要求 edit+takeover 双权限并记录 display name、correlation、request source 的不可变审计。
- CAD Parse Review Workspace 绑定解析启动时 BaseContentRevision/Hash、Source/Job/Floor 和三个 SHA；输出 typed 新增/修改/删除/冲突/低置信度/未识别变更集，确认 Apply 通过租约、Revision、ContentRevision、变更集哈希与幂等键原子写入，成功、重放和 stale 零写入均有自动化证据。
- 空白画布/底图已支持墙、柱、门、月台和静态设备直接创建及本地 2D/3D 同源；“校验并发布”深链会选择指定 Site/Version 并自动启动正式 Validation，但不会绕过 Preview、审批确认或发布权限。库区/巷道/首个货架直建及 CAD Mapping Profile 启动 UI 仍保留为 P0。
- 门禁通过：Space Unit 497、真实 SQL Space Integration 396（0 skipped）、CP6.Tests 2867、Web 727、Playwright 5；SDK/EF drift clean，完整 Release solution 0 warning / 0 error。
- 记录真实 Provider、黄金 CAD、Viewer 基准、两仓 Pilot 与五角色签字仍是未完成门禁，未将本次代码交付表述为 GA。
## 2026-08-13：CRM V1 T1 对抗审阅收口

- 把公开提交从浏览器直达 CRM 收紧为同源 Next.js BFF，并固定服务 JWT、Dapr mTLS AppId、workload/network identity 交集；补齐 attempt 绑定、幂等回执、tombstone 与 3800-byte Cookie 预算。
- 将 Quarantined 建成 PublicSubmission 独立处置闭环，复用既有 22 权限；释放时复制原 ReceivedAt/SLA 锚点，避免隔离漂白首次响应指标。
- 固定 Azure SQL GP zone-redundant/GZRS/PITR 与 Emergency Intake，区分 AZ RPO/RTO 和逻辑损坏季度恢复门禁；System Manifest 增加 previous digest 与机器兼容范围。
- 明确 CRM V1 不承担软件商城/订阅/客户产品中心，CRM 仓只在 CRM01-S01 前置关闭后创建；本变更仍无业务代码、仓库、云资源、迁移或部署。
- 验证：本地工程/设计与合入前 fallback 复核修正 Dapr 调用图、IntakeDeptId/PII 权限、实际 migration ID 和首次切换回退边界后无剩余 Critical/High；正式交互技能审阅未在缺少 AskUserQuestion 的宿主中冒充完成；CRM Foundation 16/16、Markdown 相对链接和 `git diff --check` 通过。

## 2026-08-13：修复 OpenAPI 原生客户端漂移门禁

- GitHub `client-contract` 在 `main` 与 CRM PR #5 上均因相同 OpenAPI 指纹漂移失败，证明问题属于既有主线门禁而非 CRM 文档变更。
- 改用 Node.js 稳定排序/哈希，并将 schema 集合收敛为所选原生客户端路径的递归引用闭包，消除 PowerShell 版本差异及无关模块 schema 的假阳性。
- 新增 Node 20/22 合同单测，更新受审指纹；真实 Swagger check、CP6.Tests、Client、Web 与 R2 source gate 全部通过。未改变 API、客户端运行行为、数据库或发布权威。

## 2026-08-12：CRM V1 规范批准与采用门禁

- 从 `main/origin/main@c68d9b53` 的独立任务分支修订 CRM 产品框架、可执行 Spec 和入口，将工程、QA、采用/设计审阅结论固化为 Approved implementation-planning baseline；未修改旧根工作区或业务代码。
- 新增可执行的 Lead 创建/Assignment/Activity/Merge 幂等和并发合同、428/409/412/422 与零部分写入、412 保文/差异/显式重试、回执加密有界 HttpOnly Cookie、受控 CMS、高保真设计前置与 WCAG 2.2 AA。
- 固化真实 C03 handler + 隔离 ERP SQL UAT、Pilot 与 CRM12 两层性能配置、Observation/Pilot/Lead/Full Journey 不可豁免采用 manifest，以及 GHCR/GitHub R2 唯一候选权威；Azure 仅作非权威验证或消费相同 digest。
- 该变更只关闭规范 T1。M0/R00 ADR、named Owner/cohort/Observation、三仓实施、迁移、候选和生产发布仍为待办。
- 验证：三份外部审阅工件 hash 与记录一致；CRM Foundation 聚焦测试 16/16 通过；Markdown 相对链接和差异格式门禁通过。

## 2026-08-11：新增 Azure DEV 自动部署学习链

- 新增 `azure-pipelines-dev.yml` 与静态合同：只响应 `GTX537.CP6/main` 成功 Run，绑定专用部署 Agent，按完整 Git SHA 构建一次本机镜像，并通过 `cp6-dev` deployment job 部署、验证和发布证据。
- `Invoke-Cp6LabEnvironment.ps1` 支持从 Azure task 进程环境安全接收四个 Secret，参数化 ReleaseVersion/GitSha，保留原 DPAPI 路径，并隔离 Azure RabbitMQ volume。
- 新增 `DEV-AUTOMATIC-DEPLOYMENT.md`，记录外部 Pipeline 创建、最小资源授权、首次运行与验收步骤。Variable Group 已由截图确认，首次实际 Azure deployment 尚未完成，且该学习链不提升为 UAT/生产发布权威。

## 2026-08-11：CRM 产品框架与三仓可执行 Spec

- 从 `main == origin/main == f149c75e` 的干净独立 worktree 复核 CRM Foundation 和 DevOps/R2 约束；聚焦 CRM 测试 16/16 通过。确认 Foundation 只有 20 表、状态机、EF 迁移和菜单权限种子，没有 CRM API、应用服务或前端。
- 新增 CRM 产品框架，确定包装/制造行业的售前获客到 ERP 订单定位、用户角色、官网/人工渠道、全旅程、V1/VNext、管理端与公开站点 IA/UX、指标和产品验收。
- 新增三仓可执行 Spec，冻结 CP6/Platform/CRM 边界，以及 Dapr/Kafka/YARP、RS256/OIDC/JWKS、Next.js、独立数据库、CloudEvents/JSON Schema、Outbox/Inbox、ERP 数据主权和 20 表迁移策略。
- 规格包含状态机、数据/API/事件、权限/PII/租户隔离、迁移/切换、SLO/威胁模型、测试矩阵、发布门禁、Platform P01–P10、CP6 C01–C04、CRM01–CRM12 依赖与 DoD。本变更只交付规划材料和项目记忆，不创建新仓、改业务代码、部署或迁移。

## 2026-08-11：Azure 专用部署 Agent Readiness

- 记录 `CP6-Deploy` Pool、Agent `LAPTOP-3QQ44FJS` 和非管理员服务身份 `cp6_deploy_agent` 已由本机状态与 Azure Online/Idle 截图验证。
- 新增手工、无 Secret、无 Checkout 的部署 Agent readiness Pipeline，验证实际 Windows 身份、非管理员边界、Git、Docker Desktop Linux engine、Docker Compose 和 `KOUSQLSERVER` TCP。
- 新增静态合同测试与 Azure 创建/授权/外部 Run 验收清单；Azure Build ID `10` / Run `20260811.1` 已成功，截图和本机 Worker 日志确认验证 Step 与 Job 均为 `Succeeded`。

## 2026-08-11：本机 DEV/UAT/PROD-LAB Docker 发布环境

- 新增参数化 Lab Compose、PowerShell 管理工具与合同测试；DEV/UAT/PROD-LAB 使用独立 project、端口、network、volumes、消息资源和 SQL 数据库，但消费同一组 API/Web 镜像。
- migration/runtime/infrastructure Secret 完全拆分。SQL 凭据读取已有 DPAPI note；新生成 RabbitMQ/JWT 密钥写入 ACL 受限的 DPAPI vault，Compose 只消费任务期间的临时 env 文件。
- 修复 API Docker restore 缺少 Space 项目文件和 Web Docker context 缺少仓库级 TypeScript SDK 的可复现构建故障；同步根 Compose、R2 candidate workflow 与文档。
- 实际验证三套数据库迁移成功、15 个 Lab 容器全部健康、三套 live/ready Healthy、API/Web 版本与 Git SHA 一致。
- 2026-08-11 用户提供的 Azure DevOps 列表截图确认 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 已创建且均为 `Never deployed`；只记录截图能证明的名称与状态，Resource、权限、审批和实际 deployment job 仍待验收。

## 2026-08-11：Azure DevOps CI/CD 项目记忆与路线图

- 新增 `docs/devops/` 五份交叉链接文档，覆盖当前 Azure CI、目标架构、分阶段 Azure Pipelines 计划、发布操作和 DEV/UAT/PROD 环境策略；`AGENTS.md` 与 README 已增加接手入口。
- 准确记录 `azure-pipelines.yml` 当前为 `main` + `pr: none` + `Default` self-hosted CI，仅执行 .NET/客户端/Web 验证，尚未生成 Azure 候选制品或部署环境。
- 明确现有 GitHub R2 继续作为迁移期生产权威；ACR 是待决策候选，必须先解决 GHCR/ACR、候选清单和影子期唯一性，再实现 Docker Release。
- 固化 Build once、digest 推广、CI/部署身份分离、资源侧审批、一次性 db-init、前向迁移和健康/发布身份核对。本变更只生成文档与项目状态记录，未修改 Pipeline 或执行部署。

## 2026-08-10：CRM V1 Foundation

- 形成 `docs/crm/CRM-V1-SPEC.md`，冻结从官网/人工获客到线索、企业/联系人、商机、报价接受、ERP 订单和 Won 的 V1 闭环，以及多租户营销官网、归因、反垃圾和 PII 保留边界。
- 新增 20 张 CRM/CMS 实体表、固定状态机、租户过滤/唯一约束、聚合 Restrict 外键、公开路由注册表和 `CrmFoundation` EF 迁移；标准化联系方式、来源 URL、IP 哈希和 User-Agent 均纳入 PII 擦除。
- 新增 6 个菜单节点、22 个动作及租户管理员幂等授权。页面未落地前菜单保持禁用；Foundation 不冒充 CRM 端到端完成，Intake/API、Vue、CMS 与运营任务继续列为 P0。

## 2026-08-10：Space 单格货位码 Zone 级 rackSeq

- `CodeEngineService.GenSingleAsync` 不再把货架序号固定为 `1`，而是读取目标 Zone 全部货架，与批量生成共享 `(X, Y, Id)` 确定性排序。
- 相同坐标以货架 `Id` 稳定排序；回归覆盖非首架单格生成，证明结果与批量重建一致且不与首架重复。
- 无规则模型、API、数据库、迁移或前端变化。验证为 CodeEngine/LocationPublish 聚焦 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，任务 diff 与新增行格式检查通过。

## 2026-08-10：FIN BudgetLine 版本级并发控制

- 以 `BudgetVersion.RowVersion` 作为预算行聚合令牌；行新增/编辑/删除与 Excel 确认导入都要求客户端版本令牌，任一预算桶写入都使旧快照失效，冲突统一为 `E-A5-CONCURRENCY-001`。
- 单行行头与 12 期明细使用单一事务；Excel 确认导入使用整批事务并检查内部 upsert 结果。API 缺失/非法令牌失败关闭，前端成功或冲突后同时刷新版本和行令牌。
- 真 SQL Server 用例使用独立写者验证不同行也会发生版本冲突、刷新可重试、陈旧删除整体回滚。验证为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped、`KOUSQLSERVER` 1/1、前端 3/3、Vue type-check、WebApi Release 0 warning / 0 error。

## 2026-08-10：PLAN/PUB Attachment 宿主业务权限补强

- 保留 Attachment 横切组件/无独立菜单的既定边界；`Attachment:EnforceBizPermission` 缺省改为 true，list/upload/download/preview/delete/rebind 全部以请求或持久化 `BizType` 回查宿主菜单。
- 下载/预览授权后才打开物理流，删除授权后才进入引用计数服务；rebind 要求当前用户拥有 draft token 下全部附件且具备全部宿主菜单。显式 false 只作受控兼容。
- `PubUpload` 新增宿主 `writePermission`，只隐藏上传/删除 UX，下载/预览保持可用；安全边界仍在后端。验证为后端聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 skipped / 0 failed、前端 3/3 与全量 716/716、Vue type-check/production build、WebApi Release 0 warning / 0 error。

## 2026-08-09：WF 通知定向推送与遗留广播清理

- 生产通知路径明确为事务内 outbox + 提交后派送：`PersistentWfNotifier` 只入队，`WfNotificationDispatchWorker` 使用 `Clients.User(row.UserId.ToString())` 定向触达接收人，`NotifyHub` 继续要求认证。
- 删除未注册的 `SignalRWfNotifier` 与 `PersistentWfNotifier` 中四段 outbox 后不可达的 `Clients.All`/邮件直发回退；通知器构造依赖收敛为通知存储与偏好服务，避免旧广播实现被意外恢复。
- 同步关闭 API TODO、跨波跟踪票和 KnownIssue。验证为通知聚焦 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。

## 2026-08-09：分支优先规范与本地配置优先级修复

- `04eaf42d` / `e4e33364`：新增并合入 `AGENTS.md` 与开发指南中的分支优先规则；`main` 只作集成，任务必须在独立分支验证，脏工作区使用 worktree 隔离，合并后再推送远端。
- `e3bf2420`：把 `appsettings.Local.json` 排序逻辑提取为可测试组件，准确定位 `Prefix: null` 的无前缀环境变量源，避免误插到 `DOTNET_`/`ASPNETCORE_` 主机源之前。
- 环境变量和后置命令行源继续覆盖 Local JSON；无无前缀环境源时安全追加。`.claude/settings.local.json` 被忽略，个人 `KOUSQLSERVER` 启动设置未进入任务。
- 验证为配置 4/4、OpenAPI 30/30、CP6.Tests 2832 passed / 18 skipped / 0 failed、WebApi Release 0 warning / 0 error，whitespace 与 diff 检查通过。

## 2026-08-09：Space E13 无锁 Zone 父关系确定性推导

- `d19a5300`：新增 `warehouse-rule-only-v2`。无人工 `relations.zoneSourceKey` 锁的 Aisle/Rack，只有在一个确定性 Zone Polygon 完整包含子几何时才写入父关系；字段来源与证据固定为 `DeterministicRule` / `RULE:ZONE_GEOMETRY_CONTAINMENT_V1`。
- 零候选和多候选分别以 `no-containing-zone` / `ambiguous-containing-zones` 的 Blocking `SPACE_RULE_ONLY_PARENT_REQUIRED` 失败关闭；凹多边形按完整线段验证。人工锁优先，冲突 AI Relation 被拒绝并产生融合问题，已解析关系参与环检测。
- BuildScene 复用融合问题，避免重复落库。v1 冻结 Run 与恢复链保持旧行为；不同 SourceHash 的几何匹配、建议继承和人工确认未提前实现。
- 验证为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 SQL 环境门禁 skipped、完整 Release/AOT 0 warning / 0 error。无 Migration、HTTP/OpenAPI/SDK、前端、Provider、Usage、High Accept 或 Draft 自动写入。证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## 2026-08-09：`main` 受保护同步与 P2.5 受控整合完成

- PR #2 以 `8045d872` 把原 Space 集成 tip `f8c3bae8` 受保护合入 `main`；OA 2 个和项目记忆 3 个冲突均按权威边界人工解决，Docker 本地 HTTP Cookie 修复以 `0fc6f529` 等价纳入。
- OA 保留 `formApi.submit(formKey, model, idempotencyKey)`；客户端心跳测试稳定重复 50/50。Core 14 + Space 36 迁移包从 main 基线在 LocalDB 双执行通过，51083/51000/51020 失败关闭通过。
- `030a97b9` 没有整段合并 P2.5 历史分支，而是在现行 E10 Runtime/Viewer 真相源上选择性整合独立控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器。
- 历史迁移 `20260720035903` 未进入主线；替代迁移 `20260809092206_SpaceAnalyticsControlTowerCurrent` 只新增两张分析表和三个索引。`b2a91680` 随后对齐 Space 权限、菜单种子与配置文档。
- 主线完整门禁为 Release/AOT 0 warning / 0 error、前端 711、CP6.Tests 2816、Space Unit 487、默认 Integration 288、Client 71 和 EF drift clean；P2.5 独立门禁也全绿。完整合并前评估见 `docs/space/reports/2026-08-08-main-merge-readiness.md`。
- `e4e33364` 已成为远端 `main`，并纳入 `04eaf42d` 的分支优先开发规则；R2 标签、生产数据库建立和生产部署仍需独立审批。

## 2026-08-08：Space E13 RackGenerationProfile 权威版本链

- `19d32650`：新增独立 RackGenerationProfile 头/不可变版本、System/Tenant 可见性、Tenant-only 幂等创建、列表/精确读取、规范化 SHA-256 与 Migration `20260808164544`；真实 SQL 部署脚本双执行通过。
- Generation Run 首建验证并冻结 Active/Ready 精确版本，RuleOnly BuildScene 以 `ExplicitSelected` 消费并生成 RackLevel/Location；Web 显式选择且不推断默认，空选择仍失败关闭为 Blocking。
- 三条 Design V1 API、读写审计、OpenAPI 118 operations 与双 SDK 已同步。验证为真实 SQL 1/1、前端 711/711、Space Unit 487/487、Integration 288 passed / 95 skipped、CP6.Tests 2816 passed / 17 skipped、完整 Release/AOT 0 warning / 0 error。
- 无 Provider、网络、Secret、Usage、High Accept 或 Draft 自动写入；追加 v2、System 配置、完整管理 UI、无锁父关系和不同 SourceHash 确认仍属后续边界。
- `19d32650` / `6f12a19e` / `70dd670d` 已完成功能、报告与 no-ff 远端集成；合并态前端 9/9、OpenAPI/权限 63/63、SDK drift 通过。远端祖先链确认后删除本地/远端临时分支，清理 38 个可再生成目标、29,418 个文件并回收 1,985,000,330 bytes（约 1.85 GiB）；`main` 未修改。

## 2026-08-08：Space E13 Generation Run 建模 Web 入口

- `52bb3a29` / `282d4e54` / `2871df1b`：实现、记录并 no-ff 集成建模 Web 的统一 `CreateGenerationRun`；从已确认 DWG/DXF Preview 启动 RuleOnly Run，排队/处理中轮询 Run，达到 AwaitingReview 后再读取提案，Failed/Stale 恢复使用同一 BasedOn、`If-Match` 与幂等合同。
- Run 详情补齐 Source/Mapping/Rack Profile 冻结标识，OpenAPI 与 C#/TypeScript SDK 同步；409/422 后重读当前 Draft 与来源，浏览器筛选不替代服务端权威校验。
- 验证为前端聚焦 11/11、全量 710/710、type-check/build、OpenAPI/审计 31/31、Space Unit 484/484、Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK strict/drift 与完整 Release/AOT 0 warning / 0 error。
- 无 Migration、Provider、Secret、网络、Usage 或 Draft 自动写入；RackGenerationProfile 权威存储、无锁父关系、异 SourceHash 确认、外部 Provider 与正式 CAD/黄金集仍是后续边界。
- 合并态前端 11/11、OpenAPI 29/29 与 SDK drift 复验通过；清理 38 个可重建目标和 29,416 个文件，回收 1,982,552,577 bytes（约 1.85 GiB）。远端祖先链确认后已删除本地/远端临时分支，`main` 未修改。

## 2026-08-08：Space E13 首次 Generation Run 创建入口

- `770bdc96` / `bbcaf6fe` / `9d0971f4`：实现、记录并 no-ff 集成版本级统一 `CreateGenerationRun`；新增首次 RuleOnly 创建，保留 BasedOn replacement Run，并冻结 `If-Match`、ContentRevision、权限、审计与幂等合同。
- 首建重新验证 Draft/CAD Source/Clean file/SourceHash/坐标/Floor/Mapping/Preview；BusinessKey 和 Job 固定 Preview Artifact ID/SHA，Worker 与恢复 Run 不再漂移到另一个最新 Preview。
- 同一公开 create 幂等域覆盖首次与恢复；同键不同请求冲突、不同键相同业务输入复用。OpenAPI/C#/TypeScript SDK 已同步，旧公开 `RecoverGenerationRun` operation 替换为 `CreateGenerationRun`。
- 验证为聚焦 9/9、合同 31/31、Space Unit 484/484、Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK strict/drift 和完整 Release/AOT 通过；最终构建 0 error / 7 条未改动测试文件既有 warning，C# SDK 0 warning / 0 error。
- 仅 RuleOnly 首建可用；AiAssisted、未经验证 RackProfile、外部 Provider、Web UI 与正式 CAD/黄金集仍失败关闭或待办。无 Migration、外部网络、Usage 或 Draft 自动写入，`main` 未修改。
- 合并后聚焦 9/9、OpenAPI/审计 31/31 复验通过；清理 36 个可重建目录、8,622 个文件，回收 1,666,117,627 bytes（约 1.55 GiB）。

## 2026-08-08：Space E13 纯规则 BuildScene 生产执行链

- `36cc0241` / `89c6fb2a` / `9e7f7e0a`：实现、记录并 no-ff 集成生产默认 `SpaceBuildSceneJobStepExecutor`；RuleOnly recovery 从权威 CAD PreviewSet 走完 12 步 BuildScene 并生成可审阅 Proposal/Issue。
- 新增 local-only 稳定特征快照和同 SourceHash confirmed locked facts 重映射；Proposal/Issue 以 Serializable 事务和逐字段校验实现幂等重放，缺少尺寸、父关系或 RackProfile 时失败关闭为 Blocking。
- 执行链不调用外部 Provider、不记录 AI Usage、不写 Draft；Provider-backed 模式稳定返回 `SPACE_AI_PROVIDER_UNAVAILABLE`。验证为规则/融合 21/21、BuildScene 2/2、Space Unit 484/484、默认 Integration 277 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 Release 0 warning / 0 error。
- 合并后重点复验 24/24；清理 36 个可重建目录、6,108 个文件，回收 1,209,344,722 bytes（约 1.13 GiB）。首次 Generation Run 创建入口与外部证据仍待后续，`main` 未修改。

## 2026-08-08：Space V1 E13-S14 离线评估工程能力

- `e69b3bca` / `9261d59a` / `292a26ed`：实现、记录并 no-ff 集成最终融合提案离线评估、Calibration-only 阈值选择、Validation+ReleaseHoldout 样本外门禁、95% Wilson 下界、人工操作下降率和防篡改报告。
- 按 SampleId+稳定 SourceKey 一对一匹配；重复预测计 False Positive，类型、关键属性和精确关系必须正确，关系不能跨样本、自引用或指向不存在对象。
- 正式门禁要求 20 资产、L1～L5、10/5/5、唯一 CAD hash、授权/脱敏、冻结版本、独立标注仲裁、验收日期、不可变和 hash-sealed 完整性审计；DevelopmentSeed 永远不能通过发布门禁。
- 新增 `evaluate-ai-offline` 命令和现有 Draft Proposal 适配。验证为核心 11/11、命令 1/1、CAD 工具 26/26、Space Unit 482/482、默认 Integration 275 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 Release 0 warning / 0 error。
- 本提交只完成 E13-S14 工程切片；正式黄金数据、真实 Provider/人工操作实测与审批仍待外部输入，S15/S18/S19 不提前签收，`main` 未修改。
- 远端集成祖先链核验后删除本地/远端临时功能分支；清理 41 个可重建目录、6,982 个文件并回收 1,380,171,591 bytes（约 1.29 GiB）。

## 2026-08-06：Space Version Clone 必填字段前向修复

- `0564afad` / `01eba1b7`：补齐 Zone/Aisle/Rack `Name` 和 Rack `RackType` 的 Published → Draft 快照 SQL 映射；无 Schema/Migration 变化。
- 根因已在 `ac9c977c` 独立基线复现：非空 `Name` 遗漏导致父记录插入失败并连锁触发 RackLevel/Location 外键错误，`RackType` 遗漏则会静默丢失。
- 回归使用不同于编码的业务名称和非空 RackType，验证 RowId 重映射、LogicalId/层级关系和字段保真；修复前 1/1 失败、修复后 1/1 通过。
- 验证为 Version Clone 7/7、Space Unit 430/430、Space Integration + KOUSQLSERVER 336/336 且 0 skipped，Unit/Integration Release build 均 0 warning / 0 error；`main` 未修改。
- 远端集成推进到 `08e3fe40` 后验证功能与 no-ff 祖先链，删除本地/远端临时功能分支；清理 16 个可重建目录并回收 513,840,161 bytes。

## 2026-08-06：Space V1 E13-S17 保留清理与前向修复

- `12db5531` / `e7720df4`：实现并 no-ff 集成 Tenant 级 AI retention Job、90 天生成载荷净化、365 天 Usage 逻辑归档、Run 保留锁和同租户 `sp_getapplock`。
- Published/Superseded/Publishing/Reconciliation、Decision、Locked Fact、CommandBatch、预算账本与审计不删除；Staging 清空临时 JSON 后软删除，批次和重复执行均安全。
- Migration `20260806160931` 只增加 5 列与 4 索引，幂等脚本在 KOUSQLSERVER 连续执行两次通过；`Down` 以 `THROW 51017` 禁止破坏性回滚，故障按更高版本 forward-fix 处理。
- 验证为本卡 unit 6/6、内存/迁移 4/4、真实 SQL 3/3、Space Unit 430/430、默认 Integration 255 passed / 81 SQL-gated skipped、Release 0 error、EF/SQL/diff 门禁通过。两个既有 Version Clone 真实 SQL 失败已在 `ac9c977c` 基线复现并登记。
- 远端集成推进到 `1659b333` 后验证功能祖先链，删除本地/远端临时功能分支；清理 16 个可重建目录并回收 523,868,809 bytes，`main` 未修改。

## 2026-08-06：Space V1 E13-S13 外部 AI 安全门禁

- `37bf5c37` / `e1682efc`：实现并 no-ff 集成外部主体 Gateway 拒绝、External Provider 冻结字段与最小化 Token 出站门禁、AI 读取审计补齐和跨角色操作矩阵。
- 4 个 AI 控制器共 16 个端点均有唯一审计动作，7 个 GET 均启用读审计；Customer/Supplier/3PL × 16 操作稳定返回 `SPACE_EXTERNAL_SUBJECT_DENIED/403` 且不进入数据访问或产生写入。非最小化 Payload 在配额和 Provider 前返回 `SPACE_AI_OUTBOUND_PAYLOAD_DENIED/403`。
- 验证为 Space Unit 424/424、Provider/最小化 34/34、外部/管理 8/8、审计/OpenAPI/权限 87/87、非 SQL 10/10、KOUSQLSERVER 21/21，以及 Application Debug/Release 和 WebApi Release build 通过。
- CSO 范围审计为 0 个确认的当前漏洞；两个候选因生产无 Gateway 调用方、External Provider 空注册和配额失败关闭被独立复核排除。本卡是未来外接前安全封口，不代替真实网络、供应商合规、正式数据和独立渗透测试。清理 28 个可重建目录并回收 1,062,306,204 bytes；`main` 未修改，下一张建议卡为 E13-S17。

## 2026-08-06：Space V1 E13-S11 Generation Run 恢复产品化

- `dcbbfca8` / `c695850f` / `d3c2da75`：实现、记录并 no-ff 集成取消、同输入安全重试、废弃、Apply CommandBatch 对账、Failed/Stale replacement Run、RuleOnly 降级、OpenAPI/SDK 与前端恢复操作面。
- 取消由 Worker 安全点确认且不拆分 S10 原子事务；重试沿用同一 Job/Run/冻结输入/检查点/ApplyPlan，仅接受 Transient/Resource/Bug。对账只信任匹配当前 Draft revision、RunId 和计划哈希的已提交 CommandBatch。
- replacement 保留 basedOnRunId、源/映射/货架/规则/策略快照与 Decision 审计，旧 Run 不原地 rebase。Failed current 源在同一事务中先退役，同键并发正确 replay；RuleOnly 只对 BuildScene Provider 故障建议。
- 验证为状态机/分类 42/42、OpenAPI/权限 52/52、真实 SQL 14/14、前端 129 files/695 tests、聚焦 6/6、type-check、production build、SDK 生成和 WebApi build 通过。
- 清理 34 个可重建目录并回收约 0.939 GiB；`main` 未修改。生产 BuildScene executor、外部 Provider、真实 CAD/黄金集与发布证据继续失败关闭/待办。

## 2026-08-06：Space V1 E13-S10 原子 AI Apply

- `43dc5534` / `fbc59fb3` / `5be724cf` / `0c587d4c`：实现、纠偏、记录并 no-ff 集成 Staging + ApplyPlan、ApplyGeneration Worker、原子 Draft 写入、Run/Apply Design V1 API、OpenAPI/SDK 和前端轮询刷新。
- Apply 支持新增和更新既有审核基线；Zone/Aisle/Rack/Element 原位更新，RackLevel/Location 确定性协调。跨类型/跨楼层/资产 Element 冲突、WMS 绑定库位移除、陈旧 revision/review、引用/边界/碰撞失败均保持零部分 Draft。
- Queue 使用 Serializable + tenant/run `sp_getapplock`；最终事务固定锁序并只推进一个 Floor Revision/ContentRevision，记录唯一 CommandBatch 与 before/after。Published、WMS 和设备控制数据保持隔离。
- 验证为真实 SQL 7/7、Space Unit 413/413、默认 Space Integration 248 passed / 71 SQL-gated skipped、CP6.Client 71/71、CP6.Tests 2783 passed / 17 environment-gated skipped、前端 129 files / 694 tests；完整 solution、EF/SQL/SDK drift、type-check、production build 与 diff check 通过。
- 功能分支已推送远端备份，`main` 未修改；下一张独立卡为 E13-S11 取消、重试、降级和 Stale 恢复产品化。

## 2026-08-05：Space V1 E13 S04–S09 受控开发链

- E13-S04～S08 已依次交付 CAD IR 最小化与脱敏、本地/Mock Provider 与失败降级、不可信输出校验、规则/AI 确定性融合及只读提案审核工作台；各切片均以独立功能/证据提交 no-ff 进入唯一 Space 集成分支。
- `c87289f2` / `382d5722` / `396ee38b`：实现、记录并 no-ff 集成 E13-S09 追加式 Accept/Reject/Modify 决策、单条/批量 API、rowversion/ReviewEtag 并发、幂等账本、问题解决血缘、同源人工锁定继承、Migration、OpenAPI/C#/TypeScript SDK 和实时前端决策面板。
- S09 继续关闭批量 High 自动 Accept，不写 Draft、Published、WMS 或设备状态；异源建议继承、真实 Worker 接线、外部 Provider、授权真实 CAD 与正式黄金集未被提前宣称完成。下一张独立卡为 E13-S10 Staging + 原子 Apply。
- S09 验证：Space Unit 413/413、Space Integration + KOUSQLSERVER 312/312、CP6.Tests 2779 passed / 17 environment-gated skipped、CAD 25/25、前端 128 files / 692 tests，完整 solution Release 0 error / 10 条既有 warning；EF/迁移/幂等 SQL、SDK drift、type-check 和 production build 通过。
- 功能 tip 已进入远端集成祖先链，本地/远端临时功能分支已删除；清理 38 个可重建目录并回收约 1.42 GiB。`main` 未修改。

## 2026-07-31：Space V1 E04 S04 受控集成

- `9a87dc30` / `f9c7fd21`：实现并 no-ff 集成货架/通用元素统一多选、套索、对齐、等距、旋转、批量删除、货架阵列与保存后补偿式撤销/重做。
- schema v1 命令扩展 `MoveObject`、`RotateObject`、`RestoreLogicalObject` 和 `GenerateRackArray`；继续使用 S03 的 Serializable 批次、Floor/Version revision、请求哈希幂等与 append-only before/after 审计。
- 阵列把模板计入总数，复制 Active RackLevel；生成库位使用新 LogicalId、空编码、Generated/Unbound，不复制 WMS 绑定语义。编码冲突、缺失目标和坐标/数量越界均在整批写入前失败关闭。
- 合并态验证：完整 solution 0 error / 10 个既有 warning，Space Unit 213 passed，默认 Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed，API/OpenAPI/权限 25/25；前端 96 files / 575 tests、type-check 和 production build 通过；SDK/EF drift 通过。
- E07 S05 的 E04 S04 前置依赖已解除；下一张建议卡为存量 WMS 采纳与绑定。E04 S05 仍等待 E02 S07，E04 S06 应独立排卡。

## 2026-07-30：Space V1 E00 / E01 S01–S06 / E02 S01 / E04 S01–S03 / E05 S01–S05 / E07 S01–S04 / E13 S01–S03、S12 受控集成

- `0d25da4d`：把 542 个文件的 Space 后续候选固化到 `checkpoint/space-candidate-20260730`；安全审计未发现真实凭据、私钥或异常构建产物。该提交仅作可回退候选，不是正式实现基线。
- `539d56de`：从 `dcc1ac9a` 建立 `integration/space-v1-20260730`，no-ff 合入 E00 S01–S04 与 E01 S01–S03。
- `bac76444`：从候选重建 E01 S04 最小切片，实现 Published→Draft Clone、八类版本快照、LogicalId 保留与 RowId 重映射、幂等预留、租约围栏和失败清理；未夹带后续 E05/E06/E12 能力。
- `85792161`：以 no-ff 方式把 S04 合入唯一 Space 集成分支。
- `3258d47f`：从候选依赖边界重建 E01 S05 最小切片，实现独立 Design API v1 的 6 条路径/8 个操作、Problem Details、权限/外部主体/cutover 闸、cursor 分页、持久化幂等，以及 OpenAPI/C#/TypeScript SDK 生成闭环；未夹带 Scene、Asset、Planning、Publish 或 S06 文件安全能力。
- `36f534d9`：以 no-ff 方式把 S05 合入唯一 Space 集成分支。
- `6daf1aeb`：从候选依赖边界重建 E01 S06 最小切片，实现失败关闭的文件安全扫描、XLSX/ZIP 中央目录安全检查、扫描 Job 原子终态、保留期限、引用感知墓碑与对象删除补偿；未新增上传 HTTP、CAD/Excel 解析命令或后续 Scene/Asset/Planning/Publish/WMS 能力。
- `2ccdff7a`：以 no-ff 方式把 S06 合入唯一 Space 集成分支。
- `fe959066`：重建 E02 S01 非生产实验门禁，加入黄金集/版本矩阵审计、确定性 50MiB 与 100 万实体压力生成、子进程证据、ODA/APS fail-closed preflight、隔离 Aspose 26.6.0 复现及 DWG/DXF 字节稳定属性；未实现生产 CAD 适配器或 E02 S02。
- `3742fbff`：以 no-ff 方式把 E02 S01 可交付实验切片合入唯一 Space 集成分支；最终技术选型继续由正式黄金集、授权、供应商包/凭据和冻结 Worker 阻塞。
- `d06a8bd1`：按 E07 S01–S03 边界重建版本化 WMS 能力合同、CP6 真实适配器、持久化幂等操作账本和标准模拟器；加入跨站点操作键隔离、脏 EF 上下文失败关闭、真实/模拟来源标记及五类故障注入，未夹带 S04/S05、E08、E13、Workload 或发布 Saga。
- `6e67a9d1`：以 no-ff 方式把 E07 S01–S03 合入唯一 Space 集成分支。
- `74577015`：按 E07 S04 冻结边界重建确定性标准仓数据集、加载器、生成器和验收包；同一模型生成 500 货架、10,000 库位、WMS seed、DXF/底图/期望答案和 6 个故障样本，并以逐字节 Git 属性保护 Manifest 哈希。
- `6d751e0c`：以 no-ff 方式把 E07 S04 合入唯一 Space 集成分支。
- `8f7fc25e`：按 E13 S01 冻结边界实现 Provider/确定性端口、Schema v1 强类型契约、租户/Site/别名/数据策略/外部开关门禁、原子配额租约端口，以及默认 Disabled/无 Provider/配额失败关闭；未实现外部适配器、运行数据模型、输出校验或调用外部网络。
- `ea161975`：以 no-ff 方式把 E13 S01 合入唯一 Space 集成分支。
- `cff25a25` / `94822669`：实现并受控集成 E13 S02 的 Run/Proposal/Decision/Usage 可审计数据模型。
- `cebd401a` / `dca6e19c`：实现并受控集成 E13 S03 的 Import/BuildScene 可恢复 Worker 控制面。
- `54456946` / `b33929fb`：实现并受控集成 E13 S12 的数据库三并发槽、预算预留和 Provider 请求幂等对账。
- `5bb0cdfb` / `49dbabe3`：实现并受控集成 E05 S01 通用元素与类型化属性。
- `2fc03681` / `3d554852`：实现并受控集成 E05 S02 非均匀逐层货架规格。
- `00021f0a` / `a1edecef`：实现并受控集成 E05 S03 Design Revision 权威统一场景 DTO。
- `85b57960` / `888de795`：实现并受控集成 E05 S04 System/Tenant 版本化资产库。
- `856f138c` / `a3864d9c`：实现并受控集成 E05 S05 `space-parametric-v1` 确定性前端渲染器。
- `335659b2`：记录 E05 S05 完成报告并把唯一集成基线推进到 S05。
- `1d57a3b5` / `e8e84853`：实现并受控集成 E04 S01 PDF/PNG/JPG 底图上传、安全扫描、Ready/Clean 楼层挂接、受权 Blob 内容读取和 PDF.js/Konva 渲染。
- `b721468c`：记录 E04 S01 完成报告并把唯一集成基线推进到 S01。
- `20ee0af0` / `c1043d15`：实现并受控集成 E04 S02 两点等比标定、第三控制点动态误差验证、坐标确认、append-only 审计、revision、来源复合外键与 Clone 保真。
- `96113ea3`：记录 E04 S02 完成报告并把唯一集成基线推进到 S02。
- `b322e84a` / `39146c38`：实现并受控集成 E04 S03 通用元素单选、属性面板、RemoveRequested 删除、schema v1 原子命令批次、Floor/Version revision、协议幂等与逐命令 before/after 审计。
- `407dcbea`：记录 E04 S03 完成报告并把唯一集成基线推进到 S03。
- 冲突解决保留 WMS 序列追踪不可降级、Definition 不可变、Space 审计追加写三套保存护栏，并在 `CP6.slnx` 同时保留 Mobile 与六个 Space 项目。
- S06 功能态验证：CP6 主测试 2674 passed / 17 environment-gated skipped；SDK drift、C# build、TypeScript strict compile、触及文件格式和范围污染审计通过。合并态全解构建 0 error / 10 existing warnings，Space Unit 52 passed、Space Integration 17 passed / 29 SQL-gated skipped，EF 模型无待迁移变更。前端产品代码未受影响，沿用此前 type-check、86 files / 539 tests 和 production build 通过基线。
- E02 中立工具 10/10 测试通过，Aspose 实验适配器构建 0 warning / 0 error；严格 readiness 与 ODA/APS preflight 分别按预期失败关闭为退出码 `3` / `4`。Aspose 25 次复验中 L5 5/5 崩溃，成功样本 20/20 图层退化为 `0`，因此保持淘汰。
- E07 验证：Release 全解构建 0 error，Space Unit 73 passed，Space Integration 35 passed / 30 SQL-gated skipped，CP6.Tests 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型无待迁移变更，新增文件精确格式门禁通过。
- E07 S04 验证：两次独立生成 17 个文件、0 differences，干净检出 Manifest 哈希错误为 0；合并态全解构建 0 error / 10 existing warnings，Space Unit 79 passed，Space Integration 40 passed / 30 SQL-gated skipped，CP6.Tests 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01 验证：合并态 Release 全解构建 0 error / 10 existing warnings，Space Unit 97 passed，Space Integration 41 passed / 30 SQL-gated skipped，CP6.Tests 2680 passed / 17 environment-gated skipped，Client 71 passed；Provider 契约 18 passed、权限聚焦 17 passed，新增/修改 C# 精确格式门禁通过。
- E05 S05 合并态验证：聚焦渲染 2 files / 7 tests、完整前端 88 files / 546 tests、type-check 和 production build 全部通过；仅保留既有大 chunk 提示。
- E04 S01 验证：聚焦 API/OpenAPI/权限/存储 22/22，Space Unit 205/205，默认 Integration 48 passed / 42 SQL-gated skipped，真实 SQL 6/6，CP6.Tests 2685 passed / 17 environment-gated skipped；前端聚焦 2 files / 11 tests、全量 90 files / 557 tests、type-check 与 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S02 验证：Space Unit 210/210，默认 Integration 48 passed / 43 SQL-gated skipped，真实 SQL 9/9，CP6.Tests 2687 passed / 17 environment-gated skipped，API/权限 20/20；前端聚焦 3 files / 15 tests、全量 91 files / 561 tests、type-check 与 production build 通过；SDK/EF drift 通过，合并态完整 solution 0 warning / 0 error。
- E04 S03 验证：Space Unit 213/213，默认 Integration 48 passed / 44 SQL-gated skipped，命令闭环真实 SQL 1/1，API/OpenAPI/权限 21/21；前端聚焦 4 files / 8 tests、全量 95 files / 569 tests、type-check 与 production build 通过；SDK/EF drift 通过。合并态完整 solution 0 error / 10 个既有 warning；CP6.Tests 的 6 个 RFQ 固定日期失败已在 `f8dff096` 基线复现。
- 下一张可独立推进的 3D Space 卡为 E04 S04 多选、对齐、分布与阵列命令；E13 S04/S05、E07 S05 和 E02 S01 继续遵守各自依赖与外部门禁。未独立提取的剩余候选禁止整包合入。

## 2026-07-19：GR-VP T7 部署与真实权限冒烟

- `d79a39c`：T6 以 no-ff 合入并推送 `main`。
- `ffca422`：修复 OA 发起页提交链误先调用 `draft/save(add)`；改为直接调用既有 `wf/flow/submit(submit)`，草稿保存权限保持不变，并新增组件回归测试。
- 干净 `main` 验证：73 files / 488 tests、type-check 0、Vite 2649 modules production build。
- API/Web 双镜像运行指纹分别为 `2ee04fc0…` / `0271d4af…`；HTTP 与最近 API 错误日志检查通过。
- 当前租户注册表仅 `DEFAULT/A1`：一般用户 4 菜单/8 动作，admin 148 菜单/323 动作；`qa_general` 本人审批 200、他人待办 400、无权端点 403。
- 两条测试流程实例及临时自审批定义清理归零，保留 `qa_general`。四租户原计划因 B1/C1/D1 不存在改按全部现存租户验收并记档。

## 2026-07-19：GR-VP PUR / PLAN / PUB T6

- `4bb7512`：PUR/PLAN/PUB 扫描 12 个目标视图，37 个页面级权限声明覆盖 33 个唯一后端写权限键；2 个只读 POST 明确豁免。
- `VolTable` 通过可选 add/edit/delete permission props 接入 Seq CRUD，覆盖桌面与移动入口；受限移动端不再显示空菜单，也不会由卡片点击越权打开编辑框。
- `cf20d42`：修复 `app.mount()` 后异步加载权限导致首屏指令不重判的问题；移动下拉项改用响应式条件渲染并增加事件二次守权。
- T6 验证：后端 oracle 11/11、type-check 0、72 files / 487 tests、聚焦权限测试 6/6、生产 build、真实 Chrome 权限矩阵与正式复审全部通过。
- 下一任务为 T7：合入 main、双镜像部署、四租户种子 SQL 验证与 `qa_general` 端到端冒烟。

## 2026-07-18：GR-VP FIN T5

- `5732057`：FIN 16 个视图完成 66 条 `v-permission` 铺设，覆盖 51 个真实权限键；所有键逐字命中 FIN Controller 贴点。
- 预算 M1–M12、控制模式和控制口径实现“有 edit 权可写、无 edit 权只读”，修复独立复审发现的 view-only 空白风险；最终复审 0 blockers。
- T5 前端验证：type-check 0、71 files / 481 tests、生产 build、系统 Chrome 权限矩阵与预算 view-only/edit-only 复测全部通过，console error 0。
- 下一任务改为 T6 PUR/PLAN/PUB；T7 全量验收、部署与一般用户冒烟随后执行。

## 2026-07-18：GR-VP 合入 main 与 MES T4

- `8e696d2`：`feat/general-role-vperm` 以 no-ff merge 合入并推送 `main`；合并后后端 build 通过，2220 passed / 5 skipped / 0 failed。
- `6e4ade1`：MES 31 条 `v-permission` 覆盖 12 个视图、24 个真实写权限键；设备、工单、质检的新增/编辑模式精确分流。
- T4 前端验证：type-check 0、71 files / 481 tests、生产 build、Chrome denied/edit-only/add-only 三角色场景全部通过；独立复审 0 findings。
- 下一任务改为 T5 FIN；T6 PUR/PLAN/PUB 与 T7 部署冒烟随后执行。

## 2026-07-18：开发环境迁移与知识固化

- 三个 SQL Server 数据库生成压缩 checksum 备份并通过 VERIFYONLY。
- `.bak` 改由 Git LFS 管理并推送私有 GitHub。
- 保存 GR-VP SDD 工作区，建立迁移标签和恢复说明。
- 新增 `docs/project-memory`，固化架构、状态、风险和 AI 接手步骤。

## 2026-07-17：普通角色与全模块权限 UX

- 建立 GR-VP 七任务计划。
- 新增每租户标准一般用户角色 RoleId=10：4 菜单、8 OA 动作，insert-only。
- OA/WF 40 个按钮、17 个视图完成 `v-permission`。
- ERP 39 个按钮、16 个视图完成 `v-permission`。

## 2026-07-17：权限与工作流安全收口

- WF 引擎四类审批动作加入归属复验 E-WF-029，admin 不豁免。
- 跨模块 HttpPatch 反射扫描补齐。
- 建立六模块“权限贴点必须存在于种子”互锁测试。
- PLAN/PUB、MES、ERP、OA/WF、PUR、WMS 多轮授权横切完成并部署验证。

## 2026-07 上旬：WFS、Space 与平台波

- WFS 完成内核硬化、信箱 UX、事件触发、基础设施、服务任务和子流程等系列波。
- Space 完成发布闭环、前端交互、生命周期、库存覆盖、路径/多楼层与收口波。
- 完成平台安全、租户配置、财务集成和 UI 设计系统/迁移等工作。

## 更早阶段

- 建成 ERP/MES/WMS 核心业务与闭环集成。
- 扩展采购、财务、OA/WF、权限平台、计划中台与 Space 3D。
- 建立多语言、操作日志、实时通信、容器部署、测试体系和完整领域文档。

## 维护规则

每完成一个可交付任务，追加日期、提交、业务影响、验证结果和遗留票。不要复制整个 commit 列表；细节用 `git log --all --decorate --oneline` 查询。
