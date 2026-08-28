# 当前待办与优先级

## P0：Platform P03 与后续跨仓合同

- P02 已以三个 `0.2.0-alpha.1` 包、逐包 SHA-256、CRM 固定版本引用及 PR/main 远端消费证据达到 `Frozen / Consumable`；不要再把 P02 写成 Absent 或 Candidate。
- 下一项跨仓前置是 P03 RS256/JWKS/RFC 9457 认证合同；其后仍需 P04 CloudEvents 与 CRM-F3-CONTRACT。每项必须有版本化 locator、实现、负向测试、不可变产物及生产者/消费者门禁，不能因 P02 完成而提前启动登录或业务切片。

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
- CRM 旧 Draft PR #7 已关闭，替代 PR #8 已合并且公共契约主线冒烟通过。Cloudflare Workers `estimate` 外部失败继续单独归因，不影响受保护的 CRM 合同、Windows/Web、Android 和 SQL 必需检查。
- 完成整顿后把本机归档复制到第二介质，再考虑清理 `D:\CP6-archives\2026-08-24-branch-consolidation`；在此之前禁止删除 bundle、patch、原始未跟踪文件或 SHA-256 清单。

## P0：Space Studio Lean Core GA 剩余门禁

- 当前正式派生状态为 72% / `NoGo`：两类外部输入均 Complete，WP0/WP1/WP2/WP3/WP4/WP5/WP7 已 Accepted；剩余 WP6、WP8 两个接受 Gate 和 1 个 DeliveryOwner 最终签署。下一条主线是把 WP6/WP8 收口为一次受控 SQL Server/WMS/恢复/安全发布演练；不再重复 WP2、WP5、Backup、Pilot、本地 OS Firewall 或冻结黄金集工作。
- Development V1 的仓库/开发环境功能已 100% 结案，不再新增开发版功能 Gate。正式 Core GA 使用 Schema 3；独立 Backup 与双仓 14 天 Pilot 已转为 GA 后增强。不得用 DevelopmentSeed、未批准 Provider、Mock 或 fixture 冒充正式发布演练。
- 唯一 DeliveryOwner 已登记为 `BUBAO.GAO`，Kickoff/目标 GA 为 `2026-08-27` / `2026-09-27`，同一人拥有全部输入与 Gate；WP0/WP1 已正式接受，最终签署仍 Pending。单人原创数据使用 `ApprovedOriginalWork`，不再追问不存在的客户或第二复核人。
- 20 份仓库外原创 AC1032 CAD 候选已经冻结并登记为 Complete：10 DWG/10 DXF、10/5/5、L1～L5 各 4，逐份授权/脱敏/答案/问题/Mapping/规则/复核证据齐全，产品 Converter 20/20 Pass。原始 CAD 不入 Git；后续必须使用同一 Source Set SHA `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`。
- 不再强制 ODA 或第二供应商。`BUBAO.GAO` 已选择并批准当前 AutoCAD 2025 Core Console 为唯一 Primary；本机 V1 的许可、精确版本/哈希、本地安全边界、保留/删除、正式 SemVer Worker、转换评测和 86/100 资格评分均已版本化并接受。如果未来改用 ODA、云服务或远程 Worker，再按其实际部署边界补许可证、身份、证书、Secret、网络与 SaaS 批准。
- 正式 `1.0.0` 已从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 封存并对 20 份 CAD 双跑通过，99.727873% 支持、P95 4.281 秒、SourceRef/Blocking/残留为 0；报告 SHA 为 `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350`。WP7 已在其上补齐业务质量、人工操作和 Ready 性能证据；两份证据共同构成当前 Primary 基线。
- LM-FR-001/WP1 已 Complete/Accepted：正式 Manifest 绑定 `main@b0164a15`、8 个测试源 Git Blob/SHA-256、SQL Server 20/20 无跳过和 Web 25/25；Blank/模板/完整编码仓库及 Lease/双 Revision/Idempotency 已由 `BUBAO.GAO` 可重复自验收。确定性测试数据不宣称生产数据，生产发布和最终签署仍失败关闭。
- WP2 已 Complete/Accepted：授权真实 DWG/DXF 经冻结 AutoCAD Primary 重新转换，在 SQL Server 产品链完成显式 Floor/Unit/Transform/Mapping、sealed Preparation、Parse Start、幂等重放与篡改零写入；后端 21/21、Web 14/14、Worker 残留 0。后续只在 Source Set、Provider/Worker、CAD Start 合同或应用源变化时升版重跑。
- WP4 已 Complete/Accepted：授权真实 DWG/DXF Primary Package、产品自身生成并解析的受控 XLSX、受控 PDF/PNG 与空白画布已绑定同一 Draft/Typed Changeset 正式 Manifest；SQL Server 完整套件 465/465、0 failed、0 skipped。该接受不包含生产数据、生产 WMS、Published-only Viewer 或发布恢复/安全演练；这些边界继续由 WP5/WP6/WP8 关闭。
- AutoCAD 2025 Core Console 已以正式 `1.0.0` Worker 覆盖 DWG/DXF；WP7 已用同一 20 份 Source Set 完成业务准确率/精确率/Wilson、人工减少率、Holdout Blocking 和 50 MiB/Ready P95 并正式接受。后续三路径与发布演练必须复用精确 Provider Version，不得反向修改 WP7 冻结规则。
- 当前直接评测无网络监听、无业务凭据且临时 CAD 已清除；`BUBAO.GAO` 已接受本地 V1 不以 OS Firewall 出站 Deny 阻断。该口径不得外推为生产禁网、mTLS 或 SaaS 安全证明；若改为远程/生产部署，必须另行提供这些证据。
- 单人开发可使用 `00001`～`00005` 的 `DevelopmentSeed` 完成本地角色切换与权限测试；这些虚拟编号不能冒充真实 Owner、正式证据接受人或 `DeliveryOwner`，但正式 GA 不再要求团队人数或多角色独立签字。
- 核心 GA 当前派生结果为 `NoGo`：两类外部输入均 Complete，WP0/WP1/WP2/WP3/WP4/WP5/WP7 已 Accepted；WP6、WP8 两个结果门禁和 1 个 DeliveryOwner 签署 Pending。下一步推进 WMS/恢复/安全发布演练和最终签署。
- WP3 已以精确 AutoCAD Primary、本机受控边界、正式 SemVer Release、DWG/DXF 双格式评测和 86/100 评分结案。评分工具未写 Site 配置；后续发布演练只能通过受控接口写入同一 Provider Version，Backup 另列 GA 后韧性任务。
- Site 认证、运行注册、Preparation 输出和当前 Parse v5 已绑定同一 Provider Version；真实适配器注册必须使用被评分和批准的精确版本，升级 Worker 前必须重新评分、认证并替换 Site 配置，不得在同一 Provider Key 下静默换版。历史空版本认证按设计失效，不能手工回填猜测值。
- Preparation → Parse 的 Mapping Replay Snapshot 与 v5 payload 已完成；真实 Provider 适配器必须加载快照绑定的不可变 Profile ID/Version、核对 Definition Hash、使用完整 Layer Overrides 重建 Mapping Preview，并在输出语义工件前执行 `SpaceCadMappingReplaySnapshot.ValidateReplay`。不得只信任期望 Preview Hash、忽略覆盖内容或让 Worker 使用当前 Profile 代替冻结版本。
- `CP6.Space.CadExperiment qualify-providers` 已在同一 20 份授权黄金集、冻结 Worker 和规则上完成：Primary 86/100，资格选择 SHA `d7b9645d915f28e165209b71f69386305711301a6a2fecf7422c15cbcc2a0faa`。后续升级版本必须重新评分，示例或人工改写 JSON 仍不算证据。
- 获批准的本地 Primary DWG/DXF Provider 已实测并接受。远程运行注册继续默认关闭；若启用远程/生产模式，没有对应部署批准 Manifest、身份和 Secret 引用时能力接口必须失败关闭。
- `SpaceCadProviderSqlServerTests` 已在 SQL Server LocalDB 3/3、0 skipped，关闭并发替换、唯一 Current Revision、历史追加、认证不可变、旧资格/版本失败关闭和迁移幂等。正式验收可在明确标识的受控 Release Rehearsal SQL Server 环境执行，不要求生产部署，但必须绑定真实 Primary 和不可变证据。
- CAD 起始向导、sealed Preparation、parse start fence、Site 能力检查和 Rack/Element 画布拖动精调已完成仓库内闭环；仍须由 DeliveryOwner 在发布演练中留下人工 UX、辅助技术和端到端结果证据，不要求独立人员签字。
- WP4 三路径正式证据已接受并冻结；后续只在 Source Set、Primary Version、应用基线或三路径合同变化时升版本重跑，不把 WP8 的生产 WMS/发布演练反向塞回 WP4。
- WP7 已 Complete/Accepted：正式 Manifest 绑定同一 Source Set/Worker、冻结规则、业务总体与 OOS 指标、Holdout Blocking、50 MiB/Ready 20 次稳定观察及应用提交。未来只有数据、答案、Provider、Worker、Parser、规则或应用 Commit 改变时才升版本重跑。
- WP5 已 Complete/Accepted：Current Published-only 边界、硬件 WebGL2 30 次冷启动、两个视口、键盘、Chromium Accessibility Tree 与 4.5:1 对比度均已由 DeliveryOwner 接受。后续只有生产入口、性能输入/预算或可达合同变化时才升版重跑；不要把 WP6/WP8 的 WMS/恢复/安全演练反向塞回 WP5。
- WP6/WP8 在同一次受控发布演练中验证通知、SQL Server + CP6 WMS 发布、部分写入对账、幂等重试、旧 Published 持续服务、备份恢复、IdP HTTP 负向和 15/240 分钟恢复；五类证据完成后由 BUBAO.GAO 单一签署。生产部署和现场 Pilot 继续独立，不阻断 Core GA。
## 已完成：OpenAPI 原生客户端漂移门禁

- PowerShell 版本差异与全局无关 schema 导致的假阳性已消除；门禁现在只哈希原生客户端路径及递归可达 schema，并使用 Node.js 稳定规范化。
- Node 20/22 单测、真实 Swagger check、.NET/Client/Web 和 R2 source gate 均已通过。该项不再作为 CRM PR #5 的归因问题；修复合入 `main` 后应更新 PR #5 基线并重跑 GitHub Actions。

## 已完成：Release/CD 仓库与平台工程建设

- GitHub PR/main 验证、Azure 轻量 Artifact 桥、DEV 自动链、GHCR/GitHub R2 唯一权威、生产候选/部署工作流与模板、Azure Shadow S0 均已闭环；PR #32、`main@9009abe6` 和 Azure Definition #5 / Run #145 构成最新证据。
- Phase 1 剩余治理项已关闭：GitHub 是唯一 PR 绿灯，Azure 保持 `pr: none`；CI/R2/Space 门禁责任矩阵和 self-hosted Agent 运维/隔离规则已写入结案报告。
- 该项不再作为长期 P0。完整结案口径与机器证据见 `docs/devops/RELEASE-CD-ENGINEERING-CLOSEOUT.md` 和 `release-cd-engineering-closeout.json`。

## P0：首个 R2 生产发行执行（事件触发）

- 当前没有可执行的 S1 或 PROD 任务：GitHub 无 R2 Release、受保护版本 Tag、R2 workflow Run、Environment 或仓库 Secret；`v1.0.0` 为 Draft，20 项生产/签名/设备/Pilot 输入均 Pending，Freeze gate 按预期失败关闭。
- 真实 Owner 批准并补齐 `candidate.yaml` 后，依次执行 Freeze → `vX.Y.Z` protected Tag → R2 candidate → Compose DEV/UAT → R2A/R2B；不得创建模拟 Secret、空批准或虚构候选来“结案”。
- 首个权威 candidate result/manifest 出现后，另开 S1 只读元数据任务；S1 之后的 GHCR digest、SBOM/Trivy 对比和三个候选等价报告继续逐卡验收。
- 当前 7 份 DEV `.bak` 不自动删除；保留数量、最小保留期、磁盘告警和恢复证据由独立运维任务批准后再实现清理策略。
- 全阶段继续遵守 Build once、同 digest 推广、前向迁移、环境侧审批、CI/Deploy 身份分离和不可变证据规则。

## P0：CRM V1 端到端交付

- 干净分支中的完整脱敏 `docs/crm/CRM-V1-PRD.md` v0.2 摘要 `5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a` 已由唯一 `ProgramOwner` 对候选 commit/blob 和五项产品结论批准；三次未合并预审批均已作废，不得复用。任何改变 V1 范围、状态语义、商业规则或数据主权的修改必须升版本并重新审批。
- `docs/crm/**` 已作为完整公开披露面自动发现并内容寻址；后续新增或修改 CRM 公开文档必须走受控清单/摘要更新和对应审批，不得在项目记忆或新文件中旁路恢复私有 Pilot/商业数字。`crm-v1-prd` 进入 `main` 后必须保持为受保护 base 产生的 required context，不能以 PR head 自带验证器替代。
- Public Contract Sync 已由 PR #8 完成并合入主线；继续以公开摘要 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9` 作为工程边界，不得选择性恢复历史三仓范围。
- R00 已 Accepted 且公开镜像 Complete。M0 继续关闭 Azure SQL/Emergency Intake、System Manifest 整体回退、各专业证据 DRI、Pilot cohort、Observation Gate、Critical/High、私有仓保护和必需检查；任一缺失即 No-Go。
- 当前 `main` 的 20 表、状态机、迁移、6 个禁用菜单和 22 个动作只作为迁移源与兼容语义；私有 `GTX537/CP6.CRM` 已存在但仍为 docs-only，不得把仓库存在或菜单种子描述为应用开工。
- M0 Go 后先交付每组织数据库、身份/授权/Entitlement 投影、Website/Manual Intake 和 Lead Pilot：SLA 队列、分配/移交、客户面对型 Activity、412 草稿恢复、两租户负向和真实 SQL/Kafka/Dapr 性能 Smoke。Pilot UAT 通过前不铺开完整菜单。
- Pilot 后交付 Account/Contact、转换、Opportunity、CP6 ERP/ExternalEvidence、Dashboard/报表、Import/Export、Site/CMS/Offering、Portal 商业协作和五语言；所有读写共享 Organization、DataScope、PII、Entitlement、幂等和审计语义。
- CRM09/对应产品切片开始前批准公开站点、管理台和移动端关键流程高保真稿；完整 UAT 使用真实 C03 与隔离 ERP SQL，Mock 只用于单元测试。
- 单次切换后依次完成设计伙伴、Web GA、移动 GA、Lead Adoption、Full Journey 和最终采用门禁；精确推广时间表与数值窗口保留在私有采用 Manifest。技术绿灯、部署成功或菜单可见都不能关闭 V1 Epic。

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
- E13-S14 的离线评估器、Calibration-only 阈值校准、样本外 Wilson 门禁、规范报告哈希和命令入口已完成；原创 20 份黄金 CAD、10/5/5、L1～L5、实名单人复核、完整性审计及获批准 Primary 的正式版本转换输出也已关闭。正式 S14 仍需业务真值评测、人工操作实测和结果签署；Backup/影子双链与现场试点按 Lean Schema 3 转为 GA 后增强。
- E13-S10 已消费 E13-S09 Decision 并原子写入 Draft；真实 Worker 的同 SourceHash、已确认 `LoadLockedFacts` 已自动接入 RuleOnly 融合并重映射名称、allowlisted 属性和父关系。不同 SourceHash 的确定性几何建议继承与人工确认仍未完成，不能用猜测匹配绕过失败关闭。
- RackGenerationProfile 权威头/不可变版本、Tenant-only 创建、System/Tenant 读取、Run 冻结、Worker 消费和 Web 显式选择已由 `19d32650` 完成；不要再以 Asset 或任意 GUID 替代。无人工锁时的确定性 Zone 父关系推导已由 `d19a5300` 完成。下一张独立产品卡可处理不同 SourceHash 的几何匹配、建议展示与人工确认；在确认闭环完成前不得自动继承或 Apply。现有方案追加 v2、System 配置和完整管理 UI 也保持独立，仍不能发明默认尺寸或关系。
- 继续保持批量 High Accept 默认关闭、原始 CAD 不外发、外部 Provider 默认关闭、配额失败关闭、规则路径不依赖 Provider，以及 Draft/Published/WMS/设备边界隔离。
- E02 S01 中立实验工具、20 文件原创黄金候选、DWG/DXF 版本/实体矩阵以及获批准 Primary 的冻结 Release 运行均已完成；下一步消费同一输出做业务真值和人工效率评测。
- AutoCAD Primary 的本机许可、Release/Worker 哈希、本地受控边界和 86/100 评分已接受；只有实际选择 ODA、云 Provider 或远程/生产模式时才补对应 SDK、DPA、区域、身份、网络和 SaaS 证据，不预先强制第二供应商。
- WP7 继续在同一冻结环境补正式 50 MiB/资源、业务准确率/精确率/Wilson、人工操作减少率和受训用户 Ready P95；不得用当前转换支持率直接替代这些指标。
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
