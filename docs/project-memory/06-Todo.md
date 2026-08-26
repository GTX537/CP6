# 当前待办与优先级

## P0：CRM V1 公开同步与 M0

- `CP6-SAAS-V1-PUBLIC-CONTRACT` 已由 ProgramOwner 对精确摘要批准并同步为 Complete；下一步把公开 merge commit、摘要和审批证据回写私有聚合记录。公开同步完成本身不构成 M0 Go。
- M0 唯一人类批准角色是 `ProgramOwner`。依次关闭 `DEC-001`、`DEC-003` 至 `DEC-009` 的合同和专业证据，复核 `DEC-000`/`DEC-002`，并强制 Critical/High 清零、分支保护及必需检查；SQL 容量模型和真实 Pilot cohort 未冻结时必须保持 Pending。
- 私有 `CP6.CRM` 仍因 GitHub 账户方案限制无法启用 required checks；在 GitHub Pro 生效并回读保护规则前，M0 必须保持 No-Go。只有 M0 Go 后才能解锁 CRM01。

## P0：白天临时家庭测试环境的外部边界

- 本机完整编译内存风险已通过 GitHub hosted build + Azure 轻量 Artifact 桥关闭；main #118、自动关闭门 #119、Manual #120/#121 均已成功，三次手动验收达到 3/3。继续保持本机 CI 不编译，除非另有等价隔离与容量证明。
- 本机必须保持开机、未睡眠，且 Docker Desktop 与网络正常；这是当前白天临时测试方案的可用性边界。若需要夜间、无人值守或稳定 SLA，仍须另选真实云主机/托管容器平台并完成生产部署设计，不能把本机 Tunnel 描述为高可用云部署。
- #95/#120/#121 手动 3/3、#129 低内存失败关闭、#131 Stage retry 与 #132→#133 最终自动发布均已验收；600 秒 readiness、备份/VERIFYONLY、Deploy、健康/身份、attempt-aware 证据 Artifact 和根环境零漂移已由真实 Run 证明。`CP6_DEV_AUTO_DEPLOY_ENABLED=true` 继续生效；任何旧版本手动回退前必须先关闭自动。
- 首次切换 `cp6.uk` 前，运行 `Invoke-Cp6PublicTunnel.ps1 -Action Validate`、显式停止旧 `cp6-cloudflared`、启动 `cp6-public-tunnel` 并核对完整 SHA；确认后才设置 `CP6_DEV_PUBLIC_VERIFICATION_ENABLED=true`。旧/新 connector 禁止同时运行。
- 给同事开放测试前，确认 `cp6-dev` 的 `19991`/`18080` 与公网 release identity 一致；同事只使用 `https://cp6.uk` 的应用账号，不共享 `.env`、Tunnel JSON、数据库/RabbitMQ/Kafka 管理端口或基础设施凭证。根 `cp6` 继续作为私人开发环境。
- Cloudflare Workers 的 `estimate` Git 集成仍需在 Cloudflare 控制台单独断开或改正 Build 配置。它与 `cp6-cloudflared` Tunnel 不在同一部署链；当前家庭测试服务器不依赖 `estimate`，也没有修复其外部构建失败。
- `MSSQLLaunchpad$KOUSQLSERVER`、`SQLPBENGINE$KOUSQLSERVER`、`SQLPBDMS$KOUSQLSERVER` 在故障恢复后保持 Stopped，但 StartMode 仍为 Automatic。确认 CP6 与其他本机工作负载均不使用这些功能后，另立管理员任务决定是否禁用；Pipeline 不得自行修改 Windows 服务启动类型。

## P0：整顿后的仓库治理与续开发边界

- 登录体验恢复已关闭：大幅模板重排已完成组件、全量 Web、类型、生产构建和桌面/移动浏览器验收；折叠 Tenant 焦点、虚假健康状态、语言语义与并发认证问题均有回归覆盖。后续新增实时服务状态时必须接入真实健康检查与失败/未知状态，不能恢复静态“正常”宣称。
- Kafka Dispose 恢复已关闭：刷新异常仍释放 producer、关闭异常只告警不阻断 Host、剩余队列可观测且重复调用幂等；4 个聚焦行为测试和 `CP6.Tests` 全量回归通过。
- 日期时间恢复的 P4/P5 已关闭：不恢复多余且弱类型的 Vue shim；普通业务日期时间固定到分钟精度并完成五语言回归。若后续审计日志明确要求秒/毫秒，必须新建立独立精确格式任务，不得修改全局 `long` 合同。
- CRM Draft PR #7 和 #8 已基于当前 `main`，继续等待各自产品/治理确认与 CI。PR #7 的 Cloudflare Workers 外部构建失败需单独归因；PR #8 公共契约校验已通过。两者保持 Draft，不纳入本次干净 `main`。
- 完成整顿后把本机归档复制到第二介质，再考虑清理 `D:\CP6-archives\2026-08-24-branch-consolidation`；在此之前禁止删除 bundle、patch、原始未跟踪文件或 SHA-256 清单。

## P0：Space Studio v1.3 GA 外部与扩展门禁

- LM-FR-001 的 `Blank` 初始化、Design Floor shell、Site 入口、楼层选择、不可变 System/Tenant 整仓模板目录/零写入预览、Tenant 模板持久化/跨租户隔离，以及两类 Template → 既有 Draft Floor 的分楼层原子 Apply 已有自动化纵切；现有 Blank/PublishedVersion Draft 的 LM-FR-002 来源、稳定创建者 ID、创建/更新时间和 Open Blocking 摘要也已交付。剩余 P0 是面向仓库人员的受控模板制作表单、Blank/Published/System/Tenant 四模式统一创建向导、对应模板创建来源的持久化，以及如需姓名时受控的历史身份显示合同。Template Apply 目前要求先显式创建 Draft/Floor，多楼层需分别确认；不得用单构件 Asset、预览结果、旧运行态模板或默认楼层冒充已创建仓库。LM-FR-001/WP1 保持 Partial/Pending。
- WP4 详细 Spec LM-FR-017～029 的编辑器系列已有仓库实现；两点实距标定、构件库、统一历史、2D/3D 同源、选择/视角/未保存状态、四步清单、问题筛选定位和窄屏只读均有自动化。LM-FR-004 的后端 SHA-256 复用合同和当前 CAD/底图直接上传提示、LM-FR-005 的来源引用预检/双 Revision Fence/幂等软删除/文件与审计保留、LM-FR-010～011 的 DWG/DXF 输入与解析前单位/范围/异常比例显式确认、LM-FR-012 的 CAD 图层/块清单、LM-FR-013 的逐层 Override 与租户私有 Mapping Profile、LM-FR-014～016 的七类语义/来源证据/稳定质量问题、LM-FR-019/019A 的自动 Workspace、六类 Typed Changeset、静态元素原子 Apply 与业务布局 RuleOnly 交接均已闭环。当前 CAD + Excel 已可在同一工作台完成上传、扫描等待、Mapping Profile、预检、权威匹配、Lease Apply 与刷新恢复；历史 CAD Source/Parse Job 候选目录及“当前结果加载、旧结果重新解析”的显式重新关联也已闭环。三条路径主链还须审计 LM-FR-002 的模板来源后续项和 LM-FR-003 的生产等价安全扫描证据，并完成真实 Provider/文件/浏览器接受。上述仓库闭环不自动把 WP4 标回 Complete，也不得用 Mock/fixture 替代真实 DWG/DXF/Excel/PDF、Provider、WMS 与 Pilot 接受。
- AutoCAD 2025 Core Console 的实验型 DWG→DXF→CAD IR 链已在本机通过并登记到 WP3 实现证据，但它不是已认证 Site Provider：下一步需先通过 Autodesk 更新/修复处理 GUI `acad.exe` 的 `HashMismatch`，再确认许可证允许的 Worker/自动化部署边界、禁网或出口控制、客户/Site 批准，并与至少一个独立备用 Provider 在同一 20 份真实黄金集上评分。未完成这些条件前不得注册生产运行时、填写 `acceptedEvidence` 或关闭 WP3/WP7。
- 单人开发可使用 `00001`～`00005` 的 `DevelopmentSeed` 完成本地角色切换与权限测试，但它们不能计入 `CORE_TEAM_ALLOCATION`、Pilot 或五方签字。进入正式 GA 前仍必须登记具有真实身份和审批权的 Product、QA、WMS、Architecture、Security 签字人；如后续需要可登录开发账号，应另立身份/最小权限任务，不在证据人员册中保存密码或 Token。
- 核心 GA 证据索引与失败关闭校验已建立，当前派生结果为 `NoGo`：5 类外部输入、WP0–WP8 九个接受门禁和 5 个实名签字均 Pending。下一步由真实 Owner 按 `kickoff-evidence-protocol.md` 填写结构化开工 Manifest：登记产品/QA/WMS/架构/安全签字人、2+2+1 核心团队、20 CAD 候选、至少两条 Provider 审批链/隔离 Worker、Greenfield/Retrofit 双仓和 WMS 窗口，再将各完成分区绑定到索引。任何 Complete/Accepted/Signed 证据均必须使用新证明对象；模板、fixture、一份泛化说明、本地哈希不一致或受控 URI/真实接受人/UTC 不完整不得改状态，也不得通过删除 Gate、把角色名当实名或把仓库自动化标为 Accepted 来绕过。
- WP3 的 Site 级认证数据模型、管理/查询接口、ADR-0001 资格证据、80 分门槛、同基线确定性主备排名、合规故障切换、向导能力展示和 `ICadConverter` 共合同执行器已完成仓库基础。下一张 Provider 任务卡必须在现有 Provider 注册边界实现真实 ODA、APS 或评分后替代者的适配器与隔离 Worker 注册，并且只通过 `SpaceCadConverterContractRunner` 执行；不得直接调用适配器、把供应商类型写入领域层，或允许客户端提交任意 Provider Key。
- Site 认证、运行注册、Preparation 输出和当前 Parse v5 已绑定同一 Provider Version；真实适配器注册必须使用被评分和批准的精确版本，升级 Worker 前必须重新评分、认证并替换 Site 配置，不得在同一 Provider Key 下静默换版。历史空版本认证按设计失效，不能手工回填猜测值。
- Preparation → Parse 的 Mapping Replay Snapshot 与 v5 payload 已完成；真实 Provider 适配器必须加载快照绑定的不可变 Profile ID/Version、核对 Definition Hash、使用完整 Layer Overrides 重建 Mapping Preview，并在输出语义工件前执行 `SpaceCadMappingReplaySnapshot.ValidateReplay`。不得只信任期望 Preview Hash、忽略覆盖内容或让 Worker 使用当前 Profile 代替冻结版本。
- 使用已交付的 `CP6.Space.CadExperiment qualify-providers` 在同一 20 份授权黄金集、同一冻结 Worker 和同一规则 `cad-provider-adr-0001-v1` 上评测所有真实候选；保存每个 Provider 版本、Preflight/产物/环境哈希和六维原始依据。只有工具产出唯一 Primary/Backup 且两者均不低于 80，才可把报告交给 Site 管理接口；当前没有真实评分报告，示例或人工改写 JSON 不算证据。
- 为每个启用 CAD GA 的 Site 接入并实测一个主 DWG/DXF Provider 和一个同合同、同黄金集、同 Site 审批的备用 Provider；补齐法务、安全、数据区域、删除保留、Secret 管理和审批证据。当前默认运行注册为空，能力接口会失败关闭，不能作为真实 CAD 验收。
- `SpaceCadProviderSqlServerTests` 已在 SQL Server LocalDB 3/3、0 skipped，关闭并发替换、唯一 Current Revision、历史追加、认证不可变、旧资格/版本失败关闭和迁移幂等的仓库真库门禁；生产等价 SQL、真实 Provider 和 Site 认证仍须随主备链外部验收执行，不能用 LocalDB 替代。
- CAD 起始向导、sealed Preparation、parse start fence、Site 能力检查和 Rack/Element 画布拖动精调已完成仓库内闭环；拖动复用带 Lease、Floor/Content Revision 与幂等 Fence 的 Design V1 `MoveObject`，Zone/Aisle 继续走 Layout 合同，旧 `FloorEditor` 不继续发展为第二套权威。仍须完成独立人工 UX、辅助技术和 Pilot 签字。
- WP4 的图片底图标定入口、Excel–CAD 深链审核/确认、DWG/DXF 分格式浏览器合同及异常对象改类型/删除/合并/拆分/重画已有仓库内自动化；WP4 仍为 Partial，须继续审计统一 Typed Changeset/三路径其余详细条目，并用授权真实 DWG、DXF、Excel 和 PDF/图片在两条已认证 Provider 链及 CP6 WMS 环境完成端到端证据。Mock/fixture 结果不得计入黄金 CAD、性能、恢复或 Pilot 完成度。
- WP7 的正式黄金 CAD Manifest、证据协议、失败关闭专项校验和总 GA 组合门禁已完成；模板、fixture、DevelopmentSeed 和人工改写汇总不能冒充正式证据。仍须用 20 份授权真实黄金 CAD 执行 10/5/5 Calibration/Validation/Holdout，完成双标注/QA 仲裁，在同一 Source Set/冻结 Worker 上运行主备 Provider，产出 release-eligible 质量/Wilson/人工操作、Holdout Blocking 与 50 MiB/Ready P95 的受控证明。
- 工作台 GA 快捷键、问题定位、标准 tab 焦点、窄屏 3D 保持、字号/主要热区、2D/3D 同源选择和逐 Version+Floor 视角恢复已有仓库内自动化；Iris Xe/WebGL2/500 货架/10,000 库位正式 Viewer 性能门槛已在 `bd206ff8` 以 30 次冷 Context、3,000 次命中拾取和原始证据关闭。生产 Viewer 的代码与合同已统一为 Current Published Design Revision，并由结构守卫禁止回接可变旧 floor/scene API。仍须在配置真实 SQL、已发布仓库数据和生产等价身份/部署的环境运行 Published/Draft 隔离与 Viewer E2E，完成 4.5:1 对比度、真实键盘/辅助技术与 1440×900/1280×720 人工 UX 签字。性能输入、数据集、Three.js/浏览器主版本或生产渲染路径发生实质变化时必须重跑，不得沿用旧报告。
- WP6 已完成 Warning 集合绑定的 Preview → 显式确认 → Publish fence，交付固定低基数恢复指标、15 分钟/4 小时 Prometheus 规则和运行手册，并以 Customer/Supplier/3PL 自动化矩阵关闭仓库侧 Draft/Source/Upload/Lease/Validate/Publish/AI 外部主体边界。完整 Space Integration 已在本机 SQL Server LocalDB 实际运行 426/426、0 skipped，发布编排真库 3/3 及 CP6.Tests Space/WMS SQL 15/15 通过；这只关闭仓库真库自动化 skip。仍须在生产等价观测链实际加载规则并验证通知路由，在生产等价 SQL Server 与真实 CP6 WMS 运行发布成功、超时自动恢复、部分写入对账、同 PublishPlan 重试无重复、历史重发及旧 Published 持续服务证据，并完成备份恢复、真实 IdP HTTP 负向、独立渗透测试和安全签字。
- WP8 的双仓 Pilot 模板、证据协议、失败关闭专项校验和总 GA 组合门禁已完成；模板/fixture 不能冒充接受证据。仍须真实执行一个绿地仓和一个存量仓各连续 14 天 Pilot，按版本化 Manifest 记录逐日运行、建模/人工修改、缺陷、2D/3D/WMS 一致性、15/240 分钟恢复、业务结果和客户/实施确认，再完成产品、QA、WMS、架构、安全签字后才可声明核心 GA。
## 已完成：OpenAPI 原生客户端漂移门禁

- PowerShell 版本差异与全局无关 schema 导致的假阳性已消除；门禁现在只哈希原生客户端路径及递归可达 schema，并使用 Node.js 稳定规范化。
- Node 20/22 单测、真实 Swagger check、.NET/Client/Web 和 R2 source gate 均已通过。该项不再作为 CRM PR #5 的归因问题；修复合入 `main` 后应更新 PR #5 基线并重跑 GitHub Actions。

## P0：Azure DevOps Release/CD 演进

- 外部 Readiness Pipeline 已命名为 `CP6 Deploy Agent`；可再补全为 `CP6 Deploy Agent Readiness`，并保持 `CP6-Deploy` Pool 未对所有 Pipelines 开放。
- Readiness Build ID `10` 及后续 #89/#105 已通过；ODBC 17 `sqlcmd`、备份目录 ACL、最小化 `cp6_dev_backup`、锁定 `CP6_DEV_DB_BACKUP_PASSWORD`、Pipeline 定向授权和 Exclusive lock 均已完成验收。
- 当前 7 份 `.bak` 均保留且不自动删除；后续需单独确认保留数量、最小保留期、磁盘告警和可恢复证据后再实现清理策略。
- 当前用户目录已安装并登录 Azure CLI 2.89.1 与 Azure DevOps 扩展 1.0.6；`CP6 DEV CD`、`CP6-Deploy`、`cp6-dev-secrets`、`cp6-dev` Environment、定向授权与 Exclusive lock 均已配置。当前自动开关为 `true`，公网验证开关为 `false`。
- 三次手动 Run、低内存失败关闭、同 Stage 重试和最终自动 #133 已证明根 `cp6`/`CP6DB` 未受影响；自动开关保持 `true`。后续每次自动发布继续保留 readiness、备份、部署、attempt-aware Artifact 与宿主基线证据。
- 本机 DEV/UAT/PROD-LAB Docker 运行边界已建立并实际验证；Azure DevOps 的 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 也已由 2026-08-11 外部截图确认创建。下一步在详情页核对三者 Resource 为空，并确认没有录入 Secret。
- DEV 学习 Pipeline 已有独立 deployment job；UAT/PROD-LAB 不得复制本机重新 Build 方案。Registry/发布权威现已固定为 GitHub R2 + GHCR，Azure 不创建第二套候选；后续推广必须读取同一 Schema 2 manifest/digest，并为 UAT/PROD-LAB 配置审批与 exclusive lock。单人学习期 PROD-LAB 可自批，真实生产必须换独立批准人。
- 当前 Azure `azure-pipelines.yml` 是 `Default` self-hosted 轻量 Artifact 桥、`main` trigger、`pr: none`；完整编译/测试在 GitHub `client-contract`。仍需补 Agent 运维边界和 PR 门禁归属，不把 Artifact 绿灯描述为上线。
- CP6 通用 `ADR-DEVOPS-001` 已冻结 GHCR/R2 唯一权威、Schema 2 candidate chain、Azure 非权威 Shadow、等价矩阵和回退；CRM Draft PR #7 的多仓 System Manifest/M0 审批仍是独立范围，不能用通用 ADR 冒充 CRM named approval。
- 下一张 DevOps 单任务卡是 Azure Release Shadow S0：新增无自动触发、无 Secret 的离线 parser/fixture/YAML 合同，证明错误来源/版本/SHA/hash/repository/digest 会失败关闭，且静态拒绝 Build/Push/Tag/Deploy 命令。
- S0 合入后再按独立任务推进 S1 真实候选只读元数据 → GHCR digest 验证 → 独立 Agent 上同 digest SBOM/Trivy 对比 → 三个连续候选等价报告 → 同一 digest 的 DEV/UAT/PROD 推广。
- 全阶段遵守 Build once：DEV/UAT/PROD 只推广同一 digest，不按环境重新 Build；Azure 与 GitHub 不得对同一版本生成两套权威候选。
- CRM V1 全周期固定使用 GitHub R2/GHCR 作为候选权威；Azure 即使达到等价也只能消费相同 digest 或做非权威验证。未来任何 ACR/权威切换都必须另立 ADR，不得在产品实现票中重开。

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
