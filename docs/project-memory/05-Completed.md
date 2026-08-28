# 已完成能力与近期里程碑

## 2026-08-28 Space WP0 基线与治理正式接受

- 将唯一 DeliveryOwner、Kickoff/目标 GA、全部 Gate/Input Owner、精确远端 main Commit 和 WP3/WP4/WP7 已接受依赖固化为正式结构化证据；不设置多人或独立复核门槛。
- PR #59 7/7 必需检查及合并后 11/11、42/42 冒烟已绑定；生产部署明确为 false。
- 新增 WP0 独立 9/9 与总 GA 47/47 失败关闭测试；正式 Manifest 由 `BUBAO.GAO` 接受，WP0 改为 Complete/Accepted。
- Core GA 继续 72% / NoGo（0 个输入、5 个 Gate、1 个签署 Pending）。

## 2026-08-28 Space WP4 三路径正式接受

- 复用 WP7 冻结 Source Set 和精确 AutoCAD Primary，把授权真实 DWG/DXF Package、产品内存生成 XLSX、受控 PDF/PNG 与空白画布绑定进同一正式三路径 Manifest；没有把受控数据写成生产数据。
- SQL Server Express LocalDB `17.0.4025.3` 完整 Space Integration 为 465/465、0 failed、0 skipped，覆盖 Preview 零 Draft 写入、显式 Apply、Typed Changeset、Lease、Revision 与 Idempotency。
- 新增三路径协议、模板、严格校验器、11 个专项失败模式测试及总 GA 组合验证；正式 Manifest 已由 `BUBAO.GAO` 接受，WP4 改为 Complete/Accepted。
- Core GA 继续 72% / NoGo（0 个输入、6 个 Gate、1 个签署 Pending）；没有执行生产部署、生产 WMS 联调或 WP8 发布演练。

## 2026-08-28 Space WP7 正式黄金 CAD 接受

- 新增正式业务评估器，以 Handle、类型、几何、Floor/Zone/Aisle 关系和冻结 Calibration 规则评测 20 份授权原创 CAD；Overall 覆盖率 99.0224%、准确率 98.7008%、Wilson 下界 98.1717%、人工操作下降 96.9043%，Out-of-sample 指标全部超过门槛，Holdout Blocking 遗漏为 0。
- 用授权原创 DXF 派生精确 50 MiB 标准性能包络，执行 1 次预热 + 20 次稳定观察；到可审查提案 P95 约 2.323 秒、首次 Ready P95 约 1.937 秒、零失败，并如实标明它不代表 50 MiB 客户复杂几何。
- 固化规则、可复现性能脚本、Manifest 生成器和正式 `golden-cad-formal-evidence-v1.0.0.json`；WP7 改为 Complete/Accepted。Core GA 继续 72% / NoGo（0 个输入、7 个 Gate、1 个签署 Pending），未执行生产部署。

## 2026-08-28 UTC CRM Platform P01/P10 签名里程碑对齐

- 将 P01 完成证据中的“包签名”修正为“可重复 pack + 不发布空包”，并明确正式签名候选属于 P10，关闭 P01 已完成状态与 P10 发布治理之间的公开合同冲突。
- 保持 P01 仅为生产者基础就绪；未发布包，也未授权 P02+、CRM 登录、云资源或生产部署。

## 2026-08-28 Space AutoCAD Primary V1 资格接受与 WP3 结案

- 将 Owner 确认的本地 V1 边界固化为机器合同：`LocalControlledProcess` 不要求 OS Firewall 出站 Deny，但必须无网络监听、无业务凭据、原始 CAD 临时保存后强制删除并输出可审计报告；生产、SaaS、远程托管和再分发继续另行审批。
- 用正式 `1.0.0` Worker、同一 20 份受控 CAD、Golden Dataset/Frozen Environment/Release/报告哈希完成 86/100 六维评分；`qualify-providers` 返回 `cadGaReady=true`、唯一 AutoCAD Primary 和 0 Blocking Code。
- 版本化本地边界批准、评分输入、资格输出和增量 Kickoff Manifest；`PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 改为 Complete，WP3 改为 Complete/Accepted，Backup 保持可选。
- WP7 与整体 GA 没有随之虚假关闭：业务准确率/精确率/Wilson、人工减少率、受训用户 Ready 时间、其余 8 个 Gate 和最终签署仍 Pending；派生状态为 72% / NoGo（0/8/1）。

## 2026-08-27 Space AutoCAD Primary 正式 Release 绑定评测

- 新增封存 Worker 的 `evaluate-release` 命令和严格报告 Schema；固定核验 20 份 10/5/5、DWG/DXF、Source SHA、Release/Provider 身份、双跑确定性、实体支持、SourceRef、Blocking、性能及 Attempt/原始 CAD 清理。
- PR #53 以 7/7 required checks 合并；从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 封存正式 `1.0.0`，Worker Release SHA 为 `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e`。
- 正式执行 20/20、确定性 20/20，支持实体 14,659/14,699（99.727873%），SourceRef/Blocking/残留为 0，首跑 P95 4.281 秒；报告与 Release Schema 均通过，完整真实 AutoCAD 回归 61/61。
- 报告仍诚实记录 OS 出站禁网尚未验证；本项完成正式 Release 的转换证据，但未提前关闭隔离审批、资格评分、WP3/WP7 或 GA。

## 2026-08-27 Space AutoCAD Primary 选择与单 Provider 合同

- `BUBAO.GAO` 已把 AutoCAD 2025 Core Console 批准为 V1 唯一 Primary，范围限定为本机受控开发、验证和 Release Rehearsal；不伪造 Autodesk 订单、订阅编号或 SaaS/生产许可。
- 固定版本/哈希/Autodesk 签名及运行中的 Licensing Service 已复核；安装型合同和候选 Worker 2/2 通过，输出 4,424/4,422 实体，测试后 0 CAD/0 Attempt 残留。
- 资格评测与 Site capability 已与 Lean Schema 3 对齐：一个合格、唯一最高分且覆盖 DWG/DXF 的 Primary 即可 Ready；Backup 保留为可选增强。
- 批准记录、正式 SemVer Worker 和 Release 绑定转换报告已版本化；完整 Provider 输入、WP3/WP7 和 GA 仍等待隔离/安全依据、资格评分和业务级黄金集指标。

## 2026-08-27 Space Lean Core GA 门禁重置

- 正式 GA 合同升级为 Schema 3：删除独立 Backup Provider、双仓各 14 天 Pilot、客户来源 CAD 和额外人员确认等首版过度流程门禁；这些能力保留为 GA 后增强。
- 保留 20 份冻结 CAD/Holdout、防调参泄漏、一个 Primary 的许可与隔离 Worker、资格/质量/Wilson/人工操作/性能阈值，以及 SQL Server/WMS/Published Viewer/恢复/安全受控发布演练。
- 外部输入收敛为 2 类，WP3 改为单 Primary，WP8 改为一次发布演练；新增独立失败关闭校验器和测试。当前仍为 72% / NoGo、1/9/1 Pending，没有把规则精简冒充正式验收。

## 2026-08-27 Space 原创黄金 CAD 候选

- 为单人开发口径建立 `ApprovedOriginalWork` 合法路径，不再以不存在的客户或第二复核人作为输入；禁止虚构客户来源，Owner/Author/Reviewer 均为实名 `BUBAO.GAO`。
- 使用 AutoCAD 2025 原生引擎在仓库外生成并冻结 20 份唯一 AC1032 CAD：10 DWG / 10 DXF、10/5/5、L1～L5 各 4；合计 14,659 个 Model Space 图元、2,455 个带 Handle 的标准答案元素。
- 逐份授权、脱敏、单位/坐标、格式/版本、预期答案/问题、Mapping/规则版本和复核证据齐全；源集与黄金集 SHA 分别为 `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`、`2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`。
- 产品 Converter Contract Runner 为 20/20 Pass；新增脱敏 Manifest、失败关闭验证器/测试并把候选输入登记为 Complete。原始 CAD 不入 Git。
- WP7、Primary Provider 和发布演练没有因此自动通过；按后续 Lean Schema 3，Core GA 仍为 72% / NoGo，当前为 1 类外部输入、9 个 Gate、1 个签署 Pending。

## 2026-08-27 Space Studio Development V1 100%

- 新增独立 `CP6_SPACE_STUDIO_DEVELOPMENT_V1` 验收轨；六个仓库/开发环境 Gate 全部 Passed，派生状态为 `DevelopmentComplete` / 100%，唯一 Owner 为 `BUBAO.GAO`，不设多人签字。
- 再生成并审计 20 份合成 DXF，L1～L5 各 4、五个 DXF 版本齐全；50 MiB 档为 53,190,207 bytes / 670,000 实体，百万实体档为 79,517,079 bytes。两个 JSONL 的再生差异仅为 Windows CRLF，内容归一化后相同。
- 新增失败关闭校验与 8 个正负场景：逐种子复核 SHA，禁止缺 Gate/缺证据/假 100%/改写正式快照，并阻止开发索引、合成数据与报告进入正式 GA accepted evidence；完整证据脚本 125/125、AutoCAD 安装回归 57/57、0 skipped。
- Development V1 的 100% 不计生产 GA；正式 Ready 审计继续因无真实黄金 20、10/5/5、DWG/版本矩阵失败，Core GA 保持 72% / NoGo。

## 2026-08-27 Space GA 单人 Owner 与输入盘点

- 已把 `BUBAO.GAO` 登记为唯一 DeliveryOwner 及全部输入/Gate 责任人，记录 `2026-08-27` Kickoff 与 `2026-09-27` 目标 GA；WP0 实现状态为 Complete，接受和签署仍 Pending。
- 已完成 `D:\CP6` CAD/授权/ODA 只读盘点：跟踪集为 28 DXF / 0 DWG；20 份同目录 CAD 被 Manifest 明确定义为 Synthetic DevelopmentSeed，不能计入 Release Gate；正式授权/脱敏证明只找到空模板。
- ODA 许可证变量未配置、Drawings SDK 包为 0；历史 File Converter 不冒充 Backup SDK。Pilot Site/WMS 窗口未知，三类外部输入与总体 72% / NoGo 状态不变。

## 2026-08-27 Space AutoCAD 候选 Worker Release 身份

- 可运行 Host 改为强制非 development Release：清单完整固定 Payload、源提交、Runtime、Core Console 哈希/版本和 DXF Converter 版本，并由外部完整 SHA 锚定。
- 启动前拒绝 Manifest/Payload/Core/Runtime/版本漂移；每次 DWG 供应商调用前再次复核 Core 完整哈希。远程协议 Schema 2 把部署批准 Manifest 的完整 Release SHA 贯穿 API 请求、Worker 前置核对和响应回显，健康端点也暴露完整身份。
- 真实 `win-x64` 发布演练封存 18 文件并通过 Schema；完整安装环境 CAD Experiment 57/57、远程协议 6/6、残留 0，主测试 2,939/19/0，整仓 0 warning / 0 error。
- PR #46 以 7/7 required checks 合并；合并后从精确 `main@4375c7c2fc1e297bf3fe845873b1af5af2cb5d66` 重建 `0.0.0-rehearsal.postmerge`，18 文件 Schema 通过，完整 Worker Release SHA 为 `c51c2ce8925f7bf2bf647dd2d958270d7903e6adc212eee37a668bfe9d82dc84`；post-merge Release/协议专项为 10/10 与 6/6。
- 两个 `rehearsal` 都不是正式 Release/批准证据；批准 SemVer、许可证/Site/隔离部署、独立 Backup 与黄金集仍 Pending，WP3/GA 状态不变。

## 2026-08-27 Space DXF 50 MiB 容量合同

- 托管 Parser 改为逐行严格 UTF-8 解析，底层流同步执行 64 MiB 上限与原始字节 SHA-256；移除整文件 byte[]、整份文本和 Split 行数组的同时驻留，999 注释验证后不保留。
- 精确 50 MiB 合法 DXF 容量包络成功；64 MiB+1 输入在解析前失败且无工件。DXF Converter 升为 1.1.0，组合 Provider 身份随之换版并要求重认证。
- 完整 CAD Experiment + 真实安装门禁 47/47、0 skipped，DWG 指标无回归，测试根残留 CAD/Attempt 为 0。
- 这是仓库容量合同，不是授权真实 50 MiB 性能/质量证据；WP3/WP7 与总体 GA 状态不变。

## 2026-08-27 Space AutoCAD 候选 Worker DXF 路径

- 同一隔离 Worker 候选现覆盖 DWG/DXF；DWG 经 Core Console + 托管 DXF Parser，原生 DXF 只运行 Parser且不启动 AutoCAD，两条内外 Converter 都经统一合同执行器。
- 组合 Provider Key/Version 同时绑定 Worker 链、Core Console 文件版本和 DXF Parser 版本；旧单版本请求在落盘前失败，链任一侧升级都必须重新评分和认证。
- 聚焦 4/4、安装环境完整 CAD Experiment 45/45、0 skipped；真实 DWG 指标保持 29/19/4,424/4,422，DXF 路径 Exporter 0 调用，测试根无残留 CAD/Attempt。
- 这里只完成 Primary 候选双格式仓库能力；真实授权 DXF、50 MiB、非 development Release、独立 Backup、批准和生产 Failover 未完成，WP3/GA 状态不变。

## 2026-08-27 Space Studio WP3 远程 CAD Worker 仓库切片

- 完成 CAD-only 远程 Worker 协议、HTTPS 流式客户端、生产 Provider 和显式运行注册；主 API 默认不注册供应商运行时，启用时严格绑定审批 Manifest 外部 SHA、精确 Provider/版本、mTLS 双证书和证书 Pin。
- Worker 边界不包含 Tenant/Site/用户/模型/数据库/Mapping/对象存储身份；冻结 Mapping Profile、完整 Override Replay、语义、诊断和 PreviewSet 均在 CP6 内复核生成，Worker 无 Draft 写权限。
- 交付可运行的 AutoCAD Core Console DWG 候选 Host，完整源 SHA 核对后才经统一合同执行器转换，响应前清除 Attempt 原始/派生目录；本机真实安装测试得到 29 图层、19 块、4,424 实体/4,422 支持实体。
- 远程 Provider 4/4、路由 16/16、候选安装 1/1、Space Unit 550/550、Space Integration + LocalDB 462/462 和完整 solution 通过。这里只完成 WP3 仓库切片；真实批准/隔离部署、DXF、独立 Backup、授权黄金集和 Site Failover 仍 Pending，因此 WP3 仍 Partial、GA 仍 72% / NoGo。

## 2026-08-27 Space Studio WP1 统一建模与模板制作

- 完成 Blank、PublishedVersion、SystemTemplate、TenantTemplate 四模式统一 Draft 创建；模板版本、Scope 和密封 ProposalHash 在服务端复核，全部楼层通过既有 Lease/Revision/Idempotency Fence 初始化。
- 新增 Draft 创建来源和模板 ID/版本/内容哈希持久化与数据库一致性约束；既有 Blank/Published 版本迁移后来源可追溯。
- 新增当前 Draft 的零写入租户模板预览和受控模板制作表单；仅规则、可无损表达的仓库布局可封装，不规则几何失败关闭。
- SQL Server LocalDB Version Clone 17/17、10,000 库位 System Template、Tenant Scope、OpenAPI 57/57、Web 19/19、完整 solution、EF/SDK/type-check/production build 均通过。WP1 实现 Complete，正式接受与整体 GA 仍 Pending / 72% / NoGo。

## 2026-08-26 Space Studio 单人交付门禁收口

- 正式 GA 由五角色签字与 2+2+1 团队配额收敛为一个实名 `DeliveryOwner`，允许同一人实现、自测、UX/可达性检查、安全负向、WMS 联调、接受证据和最终签署。
- 删除 `NAMED_GA_SIGNERS`、`CORE_TEAM_ALLOCATION` 两个人力外部输入；黄金 CAD 从双标注 + 独立 QA 仲裁改为单一实名复核，Pilot 的客户/实施确认允许同一位获授权人员兼任。
- 高风险发布仍需显式二次确认、前后快照、原因、结果和恢复点，但不再要求第二人；真实 CAD、Provider、SQL/WMS、性能、恢复和双仓运行事实保持硬门禁。
- GA、开工、黄金 CAD、Pilot 和开发角色种子专项回归全部通过；当前 72% / `NoGo` 只反映尚缺真实外部证据，不再反映人头不足。

## 2026-08-26 Release/CD 仓库与平台工程结案

- PR #32 已把 Shadow S0 合入 `main@9009abe6`；Azure Definition #5 / Run #145 以同一完整 SHA 在无 Secret、无 Registry/Environment 权限下完成离线 Shadow 验证并发布 `cp6-release-shadow-s0-145`，结果为 `Succeeded`。
- GitHub 已固定为唯一 PR 验证入口，Azure 保持 `pr: none`；CI/R2/Space 门禁责任矩阵和 self-hosted Agent 更新、磁盘、离线、单并发、clean checkout 与身份隔离规则已关闭 Phase 1 剩余治理项。
- Release/CD 工程状态为 Complete，但生产发行没有被伪造为完成：GitHub 当前无 R2 Release/版本 Tag/R2 Run/Environment/Secret，`v1.0.0` 仍是 Draft 且 20 项输入 Pending。S1、真实 DEV/UAT/PROD、灾备和多仓推广改为按候选/环境事件启动的发行执行任务。

## 2026-08-26 CRM V1 PRD 完整脱敏产品基线批准

- 将详细 CRM V1 PRD v0.2 与公开竞品研究迁移到最新主线，并与 Frozen 产品摘要、已完成公共合同和四仓权威边界统一。
- 合并前审查移除商业 cohort 数量/地域/名单、精确推广计划、数值 Pilot UAT 和私有数值商业/采用门禁，并从最新 `main` 建立不继承旧敏感提交祖先的干净候选分支。
- 最终全披露面审查同时清理 M0 Readiness、产品框架和可执行 Spec 中遗留的 Pilot/采用数值，自动发现并锁定全部公开 `docs/crm/**` 文件；新增未登记 CRM 文档失败关闭。工作流把 head 诊断与只读的受保护 base validator 分离，避免把 PR 自带脚本冒充独立 required check。
- 三次未合并的预审批尝试因脱敏声明不完整而作废；唯一 ProgramOwner 已批准 payload SHA-256 `5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a`、候选 commit/blob 和五项产品结论。M0 继续 No-Go；未创建业务代码、云资源、Secret、数据库、迁移、候选制品或部署，也未解锁 CRM01。

## 2026-08-26 Azure Release Shadow S0 仓库合同

- 新增手动、无 Secret 的独立 Azure YAML 与固定 v1.2.3 fixture；S0 只做本地 JSON/YAML 读取、SHA-256 和 Schema/身份合同验证，未连接真实 R2/GHCR 或创建 Azure Pipeline definition。
- parser 严格绑定 candidate result → Schema 2 manifest → freeze/spec，验证完整 Git SHA、GHCR allowlist/digest、签名制品、SBOM/漏洞/source/SQL 元数据与 ForwardOnly db-init，并硬编码 `Authority=Shadow`、`Deployable=false`。
- 行为合同完成 1 个有效和 10 个失败关闭场景；静态门禁拒绝自动触发、Service Connection、外部 fetch、镜像 Build/Pull/Push/Tag、ACR 和部署命令。R2 source gate 已纳入脚本解析与 S0 合同。

## 2026-08-26 发布权威与 Registry 决策

- 选择继续由 GitHub R2 + GHCR 作为当前 CP6 唯一候选权威；Schema 2 manifest + candidate result 是唯一候选链，Azure 不为同一版本重新 Build 或生成第二份候选。
- 完成 R2/Azure 20 项等价矩阵、Shadow S0/S1/S2 阶段、只读 GitHub/GHCR/evidence/Azure Artifact 权限边界和 30 分钟回退设计。
- Phase 3 设计改为 `trigger: none` 的 Azure Release Shadow，输出固定 `Authority=Shadow`、`Deployable=false`；ACR 当前未批准，未来迁移必须另立 ADR 并完成三个连续候选等价验收。
- 本任务仅修改文档和设计，没有创建 Registry/Service Connection、运行候选、拉取镜像、部署环境或切换 Cloudflare。

## 2026-08-26 CP6 SaaS V1 公开工程契约同步完成

- 私有产品冻结 merge commit `07a7bb0b50f33b0cb70c18c14f83be77c725626d`、Frozen 摘要 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b` 与 Accepted R00 摘要继续绑定到公开契约。
- ProgramOwner 在 PR #8 批准精确公开摘要 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`；append-only 记录绑定 GitHub 评论、证据 commit/blob、UTC 时间与私有源，公开合同和 R00 镜像达到 Complete。
- 同步验证器失败关闭核对批准角色、摘要、评论 URI、证据对象、私有源、脱敏声明与 M0 No-Go。该完成项不解锁 CRM01，也未创建云资源、Secret、数据库、迁移或部署。

## 2026-08-26 DEV 自动发布稳定性闭环

- #129 以 31 次低内存采样证明 readiness 会在 SQL/备份前失败关闭；600 秒恢复窗口保留 2048 MiB 与 3 次连续独立 SQL 登录的安全要求。
- #131 同 Stage 重试真实完成 readiness、备份、迁移和部署，但固定证据 Artifact 名在 attempt 2 冲突；PR #30 使用只读 `System.StageAttempt` 生成 `cp6-dev-evidence-attempt-<N>`，合同测试先红后绿，全部本地 DevOps 回归通过。
- PR #30 合入 `main@08813896...` 后，GitHub client-contract/SQL、Azure 基础 CI #132 与自动 DEV #133 全部成功；#133 的 `pipelineTriggerType=PipelineCompletion` 精确绑定 #132 和同一 main SHA。
- #133 readiness 为 2184/2383/2411 MiB 且三次 SQL=True；第 7 份 CHECKSUM/VERIFYONLY 备份为 2,600,960 bytes，SHA-256 `af4f48fd...d804c9de`。API/Web Healthy、完整 SHA 身份一致，`cp6-dev-evidence-attempt-1` 发布成功，根 API/DB 基线零漂移。

## 2026-08-26 DEV 备份前主机与 SQL 就绪门禁

- #127 在候选封装后、备份前因宿主内存使用 95.16% 导致 SQL prelogin 超时；无新备份、迁移或容器切换，失败后的 8/8 新 SQL 连接正常，定位为瞬时宿主压力而非 Secret/权限/数据库持久故障。
- 新增可测试的锁内就绪门禁：至少 2048 MiB 可用内存、3 次连续独立 `cp6_dev_backup` 登录、最多等待 600 秒；不满足时失败关闭且不执行有副作用的 BACKUP。
- 门禁保存逐次内存/SQL/连续成功证据，成功部署的 Schema 3 `deployment.json` 引用该证据；5 场景行为测试、7 场景 sqlcmd 测试和 DEV CD/数据安全合同均通过。
- 自动 #129 真实完成失败关闭验收：31 次采样为 1328～1861 MiB，SQL/备份/迁移/切换均未开始；主机随后自然恢复到 2 GiB 的总耗时约 8 分 40 秒，为 600 秒窗口提供实测依据。

## 2026-08-25 DEV 自动发布启用决策

- 在 #95/#120/#121 三次独立 Manual 成功后，用户明确授权启用 DEV 自动模式；`CP6_DEV_AUTO_DEPLOY_ENABLED=true` 生效，公网验证继续为 `false`。
- 基础 CI #124 completion 自动触发 DEV #125；REST 元数据证明它是 `resourceTrigger`，并真实完成 Artifact 校验/封装、CHECKSUM/VERIFYONLY 备份、迁移、API/Web 身份健康和 2 文件证据 Artifact，未用 Manual Run 冒充自动验收。
- 第 5 份备份为 2,572,288 bytes，SHA-256 `bcd9f228...a574`，本机重算一致。DEV 运行 `main@ecbad9e1...` 且 Healthy，根 API/DB 三项基线零漂移。GitHub R2/GHCR 生产权威、根 `cp6`/`CP6DB` 隔离和旧版本手动回退前关闭自动的规则均未改变。

## 2026-08-25 DEV 三次独立 Manual 验收闭环

- PR #24 合入 `main@a5c6b5fa...`；GitHub client-contract 与 Azure #118 成功生成/桥接同 SHA Runtime Artifact，自动 #119 在关闭状态安全跳过。
- Manual #120/#121 分别成功，和既有 #95 合计 3/3。两次都独立完成 Artifact 验证、runtime-only 封装、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份验证和 2 文件证据 Artifact；备份目录由 2 份增至 4 份。
- 最终 API/Web 均为 `0.0.0-dev.a5c6b5fa...59e6`，live/ready Healthy；8/8 SQL 查询成功，无新增 701/17300。公网七容器基线完全不变，自动/公网开关仍为 `false`，未切换 Tunnel。

## 2026-08-25 GitHub 远程构建与 Azure Artifact 桥分支验证

- `client-contract.yml` 现可手动运行并在 GitHub-hosted Runner 完成 .NET/客户端/OpenAPI/Web/Android/R2 source 门禁，发布与完整 Git SHA 绑定、3 天保留的 DEV Runtime Artifact。
- Azure 基础流水线不再本机编译；它使用授权 Checkout 凭证，验证 GitHub 工作流来源、成功结论、完整 SHA、归档 SHA-256、ZIP 路径和内部逐文件 manifest 后转存 Azure `cp6-dev-runtime`。
- Azure #116 在下载前失败并跳过 Publish，定位到 extraheader 查询缺少仓库路径；修复后 GitHub Run 32881647447 与 Azure #117 真实成功。全过程 SQL 与公网七容器基线未变，无部署副作用。main 与两次 Manual DEV 验收仍待完成。

## 2026-08-25 DEV 候选 CI Artifact 隔离

- 默认 self-hosted CI 在 #109/#111 两次触及本机内存/SQL门禁后均于 Artifact/Deploy 前取消；#110 证明当前 Azure 组织没有 hosted parallelism，未开启计费。低内存分支 #112 以非并行 restore、单节点 build/test、禁用持久/共享编译服务器和两个 Vue test worker 完整成功，发布哈希 Runtime Artifact；最低观测可用内存约 2.22 GiB，SQL 和根/旧 DEV 容器基线不变。该分支只验证实现，合并前不作为 DEV 候选。
- 首次主线 CI #108 在 `Verify runtime artifact contracts` 打印 passed 后仍被陈旧 `$LASTEXITCODE` 判为失败；所有 restore/build/test/artifact 步骤均跳过，根环境与 `CP6_DEV` 无变化。基础 CI 与 DEV 合同 Step 现统一依靠 PowerShell terminating error，并由静态回归禁止在 `.ps1` 成功后读取继承的 `$LASTEXITCODE`。
- Manual Run #98 在 API publish 内存达到 96.03% 后于 Deploy 前取消；没有备份、迁移或 DEV 镜像切换。Docker OOM 造成根 `cp6-db`/`cp6-api` 自动重启，因此该 Run 不计验收并保留为失败证据。
- API Docker publish 改为单 MSBuild 节点并关闭项目并行、共享编译服务器后，Manual Run #101 仍在 Docker VM 95.83% 使用率时于 Deploy 前取消；没有备份、迁移或 DEV 镜像切换。根 `cp6-db`/`cp6-api` RestartCount 分别增至 2/3，因此同样不计验收。
- DEV 候选现由部署 Agent 使用 .NET 8/Node 22 在 Windows 宿主机串行构建，Docker 只用两个 runtime-only Dockerfile 封装 publish/dist；Web 堆上限 768 MiB，Readiness 与 DEV CD 均固定工具版本并有合同覆盖。生产 R2 Dockerfile/工作流未改。
- 提交 `72ec0e70` 的本机完整构建成功生成 API/Web 不可变 image ID，临时上下文清零；Docker VM 采样始终保留约 1.9 GiB 以上，根 API/DB 的 ID、StartedAt、RestartCount 不变，宿主 SQL 无新增 701/17300。六组契约、PowerShell 解析、差异与凭据扫描通过；自动/公网仍关闭，手动验收仍为 1/3。
- CI #102、completion DEV #104 和 Readiness #105 均成功；Manual #106 因资源版本输入错误在 YAML 解析前失败。Manual #107 正确绑定 CI #102，但宿主重复 publish 达约 4.18 GiB 工作集并导致 `CP6_DEV` 连接超时，按门禁取消；没有备份/迁移/镜像切换，根 API/DB 不变，旧 DEV API RestartCount 16→17，因此不计验收。
- 基础 CI 现从同一次已通过测试的 API/Web build 生成带完整身份与逐文件 SHA-256 的 `cp6-dev-runtime` Artifact；DEV 只下载、验证并用 runtime-only Dockerfile 封装，不再重复编译。587 文件真实产物本机封装约 17 秒完成，根 API/DB、旧 DEV API 与 SQL 均保持稳定；篡改、清单外文件和身份错配回归均失败关闭。GH R2/生产候选权威未改。

## 2026-08-25 Azure CI 与首次手动 DEV 发布外部闭环

- Azure CI Run #92 在 `main@47ca8441` 完整成功；通用 `CP6-Windows` Agent 的 `.NET Restore` 假失败已定位为 PowerShell 7 `PSModulePath` 继承污染。新增安全前台启动器及合同测试，固定核对 `C:\agent`、Agent 名称和 `Default` Pool，并只隔离 Agent 子进程环境。
- Azure 外部资源闭环：`CP6 DEV CD` Definition ID `4`、定向 Pool/Variable Group/Environment 权限、Exclusive lock、两项 `false` 开关、最小权限 `cp6_dev_backup`/锁定 Secret，以及 Readiness Run #89 全部完成。自动 Run #93 已证明关闭状态只分类并跳过部署。
- Manual Run #94 正确失败关闭：先完成 CHECKSUM/VERIFYONLY 备份，再因宿主 SQL 已有 701/17300 内存耗尽事件而在 db-init 超时；未启动 API/Web。重启 `KOUSQLSERVER` 后，Manual Run #95 成功发布 `0.0.0-dev.92` / 完整 SHA `47ca8441...9dbe9c18`，健康、迁移、不可变镜像和 `cp6-dev-evidence` 均通过。
- Run #95 新备份长度 2,453,504 bytes、SHA-256 `58c6ff73...5079c23`、VERIFYONLY passed。根 `cp6` 七个容器与 `CP6DB` 未变；自动/公网开关继续关闭，当前手动验收计数为 1/3。
## 2026-08-25 CRM 公开产品对比与 PRD v0.2

- 新增 `docs/crm/CRM-COMPETITIVE-ANALYSIS.md`，以 Salesforce、HubSpot、Dynamics 365、Pipedrive、Zoho、Odoo、SAP Sales Cloud、纷享销客和销售易的官方公开资料为证据，归纳轻量销售、增长平台、企业平台、ERP 邻接和中国企业连接型五类产品。
- 将竞品观察冻结为 `CRM-COMP-001`～`007` 决策，明确 Lead Pilot 行动优先、稳定对象/状态、来源与 SLA、CRM/ERP 成交权威、公海/连接器 VNext、AI 权限边界和不按 Lead 制造漏记激励。
- `CRM-V1-PRD.md` 升为 v0.2 并建立竞品结论到现有 PRD ID 的追踪；状态仍为 Draft，Public Contract Sync、M0、业务代码和上线状态均未改变。

## 2026-08-24 CRM V1 产品需求草案

## 2026-08-25 DEV 首次运行前置审计与 sqlcmd 可发现性修复

- 实机确认专用 Agent、Docker、Compose、宿主机 SQL Server、`CP6_DEV` 与 TCP 端点存在；根 `cp6`/`CP6DB`/`cp6_cp6-db-data` 只读核对后保持原状。
- 创建 `C:\CP6Backups\CP6_DEV`，以显式 ACL 只授予 SQL Server 服务写入、部署 Agent 读取，以及维护身份/SYSTEM/Administrators 管理权限；未生成或删除任何 `.bak`。
- 修复服务身份不继承交互用户 PATH 时找不到 `sqlcmd` 的门禁缺口：备份脚本和 Readiness Pipeline 会探测 PATH 与三类标准安装路径。新增 7 场景无数据库副作用行为回归，覆盖 PATH、相对/缺失绝对路径、全候选缺失、标准目录回退、Secret 前置门和执行失败后的 `SQLCMDPASSWORD` 恢复；数据安全、Readiness、DEV CD 三组合同测试同步通过。
- 本节记录当时的前置审计；同日后续已完成 `cp6_dev_backup`、Azure Secret/Lock/变量、Readiness 重跑和首次手动发布。三次手动验收当前为 1/3。

## 2026-08-25 本机 DEV 双模式发布仓库闭环

- `CP6 DEV CD` 以单一 YAML 支持自动/手动两种入口，自动初始关闭；所选 CI Run 会经 Azure REST 核对成功状态、`main` 分支和完整 SHA，旧自动任务在分类阶段与 DEV 锁内跳过，旧版本手动回退要求先关闭自动。
- 从所选提交的隔离 worktree 构建 SHA 镜像并捕获不可变 Docker image ID；部署受 Azure 顺序锁和本机互斥锁保护。对 `CP6_DEV` 执行 CHECKSUM 备份和 VERIFYONLY 后，停止旧 Web/API、运行一次性 db-init、先验证 API 再启动/验证 Web。发布证据新增 trigger、备份 SHA-256、镜像与本机/可选公网身份。
- 新增专用 `cp6-public-tunnel` 控制器和 `CP6DEV_IMPORT_*` 旁路恢复工具；旧/新 Tunnel connector 不得同时运行，数据导入拒绝覆盖或合并 `CP6DB`。根 `cp6` 和 `cp6_cp6-db-data` 不在自动化作用域。
- Lab、DEV CD、数据安全和 Tunnel 四组契约测试已建立并通过；本任务没有运行真实部署、数据库备份/恢复、容器启停或 Cloudflare 切换，外部三次手动验收仍为待办。

## 2026-08-24 登录体验恢复与可访问性闭环

- 恢复包装制造运营登录页及五语言产品文案，保留账号密码、租户识别、SSO、2FA、菜单和登录后路由合同，并完成桌面/移动响应式布局验收。
- 修复 CSS 折叠 Tenant 输入仍可被 Tab 聚焦的问题：关闭时使用 `inert`/`aria-hidden`，主动展开或 `needTenant` 时聚焦组织输入；语言菜单采用按钮组语义。
- 密码登录与 SSO 使用同一互斥忙碌状态，阻止认证请求重入；无真实健康检查的“系统正常”动态状态改为中性安全访问标识。
- 门禁：组件聚焦 10/10、Web 全量 176 文件/902 测试、Vue 类型检查、production build；Chromium 1440×900 与 390×844 均无横向溢出，折叠/展开 Tenant 键盘焦点行为通过。

## 2026-08-24 Kafka 生产者安全退出

- 修复 Kafka Singleton 关闭顺序：`Flush` 与 producer handle 释放分别保护，刷新失败也保证继续释放；重复 `Dispose()` 幂等，不重复等待或释放。
- 关闭阶段的 Flush/Dispose 异常不会让 WebApi Host 退出失败，但会保留 Warning；5 秒刷新后仍未发送的队列长度也会被记录，避免原 WIP 静默吞错。
- 新增 4 个生命周期行为测试；聚焦 4/4、`CP6.Tests` 全量 2,938 passed / 19 项既有环境门禁 skipped / 0 failed。

## 2026-08-24 日期时间规范化恢复与 P4/P5 关闭

- 确认 P4 的通配 Vue SFC shim 对当前 Vue 3.5 + TypeScript 6 + `vue-tsc` 3.2 工具链并非必需；最新 `main` 在无 shim 时干净类型检查通过，因此不恢复会引入 `any` 的旧声明。
- 共享日期时间工具新增 Element Plus 单元格适配器；OA/PMS/WMS/Space 页面及 `VolTable`、`CpListPage` 的 datetime 列统一本地化，替代原始 ISO、直接 `toLocaleString()` 和分散截断。
- P5 不再把 `.sss` 加到全局 `long` 格式。普通业务 UI 固定为日期 + 时:分；高精度 .NET 输入会被解析但不向所有页面扩散秒/小数秒，未来精确审计格式须独立立项。
- 门禁：P4 干净基线 `vue-tsc --build`；日期时间聚焦 3 文件/53 测试、Web 全量 175 文件/892 测试、Vue 类型检查和 production build 全部通过。

## 2026-08-24 白天临时家庭测试服务器控制流程

- 新增可双击菜单 `cp6-daytime-server.bat` 及 PowerShell 控制器，统一提供 `start`、`start-build`、`status`、`close`、`stop` 五个入口；启动前检查 Docker、`.env`、Compose、Tunnel 配置和本机凭证文件。
- `close` 只停止 Compose 内的 `cp6-cloudflared`，保留本机 API/Web/基础服务；`stop` 使用 `docker compose stop` 安全停止全栈并保留所有命名卷。脚本不会自动结束主机上的其他 cloudflared 进程，也不会修改 Windows 睡眠或电源设置。
- 合同测试覆盖 PowerShell 语法、动作/入口映射、四个 HTTP 地址、Tunnel 单独关闭、数据保留停止、凭证预检和禁止电源修改；实机只读状态检查确认 7 个服务及本机/公网 Web/API 全部就绪、HTTP 200。为避免中断当前使用者，没有执行现场启停或重建。
## 2026-08-24 CRM V1 产品需求草案

- 完成 `docs/crm/CRM-V1-PRD.md` v0.1，把 Frozen SaaS V1 与当前 Foundation、旧三仓规划和 Lead Pilot 批准设计对齐为一份可评审产品合同。
- 文档明确前端 IA/页面状态/Lead Pilot 交互、后端状态机/事务/权限/幂等/并发/错误、四仓数据主权、CP6 ERP 与 ExternalEvidence 成交路径，以及 API/event/custom-field/channel 的升级边界。
- CRM 文档入口已补充 PRD，并更正私有 `GTX537/CP6.CRM` 已存在但仍为 docs-only 的事实。该里程碑只表示需求草案完成，不表示产品批准、Public Contract Sync、M0、业务代码或上线完成。

## 2026-08-24 Space GA 退出码假红修复

- 根因是五个 Space GA 负向测试辅助函数已正确消费预期失败的 validator 退出码，但都未清除 PowerShell 全局 `$LASTEXITCODE`；末项负向用例因此让 Actions 在断言全绿后仍返回 `1`。
- Attestation、Pilot、Golden CAD、Kickoff 和人员种子套件现在都先完成期望退出码与稳定错误码断言，再将已消费状态归零；各自汇总前新增全局退出码回归断言。真实 validator 失败仍会抛错，GA 证据失败关闭语义未改变。
- 直接 Actions 风格调用和独立进程调用均得到 Attestation 36/36、退出码 `0`；完整 Space GA 工作流另通过 Pilot 21/21、Golden CAD 31/31、Kickoff 28/28 和人员种子 8/8。

## 2026-08-24 仓库分支整顿与 WIP 恢复

- 建立整顿前全引用 Git bundle、各脏 worktree 状态/patch/原始未跟踪文件与 SHA-256 清单；根工作区安全恢复为干净 `main@0a14581f`，没有通过 reset/覆盖丢弃用户数据。
- 删除 61 个已进入 `main` 的远端分支和 9 个仅需归档的陈旧远端分支；关闭陈旧 PR #3；清除 10 个旧本地分支和 8 个旧 worktree。所有被删除引用均可从归档 bundle 或远端/patch 证据恢复。
- 把旧根目录混合 WIP 拆成登录体验、日期时间规范化、Kafka Dispose 三个当前-main分支并推送；分别完成 6/6 前端聚焦测试+类型检查、Web 174 文件/886 测试+类型检查+生产构建、CP6.Core Release 0 warning/0 error。
- CRM Draft PR #7/#8 均已合并当前 `main` 基线；PR #8 的公共契约本地校验通过。两者保持 Draft，不把治理文档同步冒充产品批准或可发布状态。
- `main` 已启用严格分支保护：要求最新主线、PR、`windows-and-web`/`android`/`sql-integration` 三项检查和对话解决；管理员同样受保护，force-push 与分支删除被禁止。
- 完整证据与分支逐项处置见 `docs/project-memory/11-Branch-Consolidation-20260824.md`。

## 2026-08-16 Space Tenant 私有整仓模板

- 新增租户私有整仓模板头与 append-only 版本表；租户内编码大小写不敏感唯一，版本保存规范计划 JSON、内容 SHA-256、各类对象计数和创建审计，复合租户外键阻止跨租户版本归属。
- Design V1 新增带幂等键的 Tenant 模板创建接口；服务端只接受 schema v1 类型化计划，并校验父链、唯一 Key/编码、坐标、尺寸整除、逐楼层命令上限和总库位上限。租户接口不能创建或改写 System 模板。
- 现有目录、密封 Preview 与逐层 Lease/Revision Apply 同时解析内置 System 和当前租户模板；另一个租户猜测模板/版本 ID 返回 NotFound，同一模板编码可在不同租户独立存在。
- Space Studio 工作台读取合并目录、显示“系统/租户私有”作用域，并只展示与当前所选模板一致的密封 Preview；API wrapper 为后续受控模板制作 UI 保留创建合同。
- 门禁：新增真实 SQL 聚焦 2/2；全量 Space Integration 456/456（0 skipped）、Space Unit 549/549、CP6.Tests 2,934 passed / 19 项既有环境门禁 skipped、Web 884/884、Space Studio Playwright 26/26；OpenAPI/权限聚焦 96/96、EF pending-model clean、双 SDK drift、Vue TypeScript、生产构建和完整 solution Release 均通过。完整证据见 `docs/space/reports/2026-08-16-space-tenant-warehouse-template.md`。
- Tenant 模板持久化与消费纵切已闭环；仓库人员模板制作表单、四模式统一 Draft 创建向导和 Template 创建来源持久化仍未完成，因此 LM-FR-001/WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space Studio 历史 CAD 审核结果目录

- Design V1 新增 Floor 级只读候选目录，只枚举同 Version/Floor、成功完成且来源格式为 DWG/DXF 的 CAD Parse Job；服务端从持久 Payload 读取冻结 Base Content Revision/Hash，不用请求时的当前值伪造新鲜度。
- 只有 Base Revision/Hash 与当前 Draft 一致、来源仍为 `PreviewReady` 且 PreviewSet Artifact 存在的候选返回 `canLoadReview=true`；历史候选仍可审计，但只能重新解析，不能直接加载或 Apply 到新 Revision。
- Space Studio 来源面板新增“选择已有 CAD 结果”；当前候选复用既有 Job 监控和 Review Workspace，历史候选带原 Source 进入起始向导重新解析。切换前统一清理旧 CAD/Excel/Preflight/Match 路由和本地状态，页面不暴露内部 ID 输入。
- 只读用户可查看目录，只有可编辑状态才能触发重新解析；实际 Workspace 加载仍执行来源安全状态、SHA、Artifact 和身份链校验，目录不能绕过现有 Trust Boundary。
- 门禁：Space CAD Integration 15/15、OpenAPI/权限 95/95、双 SDK drift、Web 882/882、Space Studio Playwright 26/26、Vue TypeScript 与生产构建通过。完整证据见 `docs/space/reports/2026-08-16-space-cad-review-candidate-catalog.md`。
- 历史 CAD 候选可发现与显式重新关联的仓库 UI 边界已闭环；真实 Provider/文件/WMS/黄金集/Pilot 未因此关闭，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space Studio 当前 CAD + Excel 统一工作流

- 来源模式新增“上传 Excel 并匹配当前 CAD”；入口只消费本楼层已自动加载且与当前 Draft Revision 一致的 CAD Review Workspace，不要求用户填写 SourceId、ParseJobId、FloorId 或 Revision。
- `.xlsx` 上传继续走既有 Design V1 隔离来源链；工作台等待服务器 Ready、选择服务器 Mapping Profile、自动轮询预检，并展示行数、有效数、Info/Warning/Blocking 与工作表/行/列恢复提示。Blocking 或服务器不可确认时失败关闭。
- Excel Source/Preflight Job 持久在 URL 中支持刷新恢复；显式复核后，匹配绑定当前 CAD/Excel/Floor/Content Revision，自动轮询到权威结果并进入既有 Lease/Revision/Artifact Apply 与统一撤销/重做链。确认前 Draft 零写入。
- Web 全量 878/878、Space Studio Playwright 25/25、Vue TypeScript 和生产构建通过。详见 `docs/space/reports/2026-08-16-space-excel-current-cad-workflow.md`。
- 当前工作会话的统一 Excel 上传 UI 已闭环；历史 CAD 候选目录已由同日后续纵切闭环。真实 DWG/DXF+Excel/Provider/WMS/Pilot 接受仍未关闭，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space CAD 待审变更集与 RuleOnly 交接

- LM-FR-019 保持 Job → Clean PreviewSet → 绑定 Source/Transform/Mapping/Base Revision 的自动 Workspace 加载；用户不再下载或重传 JSON，stale 继续返回 `SPACE_PARSE_CHANGESET_STALE` 且零写入。
- LM-FR-019A 的工作台完整显示并筛选新增、修改、删除、冲突、低置信度和未识别六类变更；客户端验证 Change Summary、可 Apply 类型和选择一致性，并在密封 Workspace 变化时重置旧选择。
- 通用静态元素的 CAD Apply 使用内部专用上限 10,000 项，公开手工 Element Command 仍为 100 项；101 项服务集成用例验证单事务、一次 Floor/Content Revision、完整 Undo/Redo 和一个幂等批次。
- Zone/Aisle/Rack 保持设计态领域权威，不伪装成 `Space_Element`；对应冲突可从同一审核面板一键进入既有 RuleOnly/Proposal Review/Atomic Apply，并自动预选当前 CAD 来源。
- 门禁：Space Cad Parse Integration 15/15、Space Unit 546/546、CP6.Tests 2,933 passed / 19 environment-skipped、Web 873/873、OpenAPI 55/55、Space Studio Playwright 24/24、生产 Web 构建、完整 solution Release 0 warning / 0 error 与双 SDK drift 通过。完整证据见 `docs/space/reports/2026-08-16-space-cad-review-changeset-handoff.md`。
- LM-FR-019/019A 仓库实现闭环；真实 Provider、黄金 CAD、三路径现场浏览器、WMS 和 Pilot 不因此关闭，核心 GA 继续 72% / `NoGo`。

## 2026-08-16 Space CAD 输入与坐标确认

- LM-FR-010 延续唯一 Design V1 来源链：工作台文件选择器接受 `.dwg/.dxf`，客户端显式提交 `Dwg/Dxf`，服务端按扩展名、声明 MIME 和文件签名失败关闭，再进入隔离扫描与同一 CAD IR/Preparation/Parse 合同。
- LM-FR-011 的服务端确定性分析继续提供建议单位、mm 比例、原始范围、建议毫米范围、合理性和稳定问题；起始向导现完整展示 X/Y/宽高、比例与异常原因，而不是只显示“合理/需复核”。
- 单位/原点/旋转/楼层转换与映射语义继续分开显式确认；修改任一输入或逐层 Override 都会使旧 Preview 和确认失效，Parse 只消费服务端密封的 Start Request。
- 门禁为 Space Unit 546/546、Web 869/869、Vue TypeScript、Web 生产构建和完整 solution Release 0 warning / 0 error。
- 安装型 AutoCAD 2025 Core Console 使用真实 Autodesk DWG 的开发合同用例 1/1、0 skipped；它不是 Site 已认证生产 Provider。仓库自动化见 `docs/space/reports/2026-08-16-space-cad-input-coordinate-confirmation.md`。
- LM-FR-010～011 仓库实现闭环；生产主备 Provider、20 份黄金 CAD、真实浏览器三路径、Pilot 与签字仍为 GA 门禁，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space CAD 语义与质量诊断

- 复核确认 LM-FR-014 的墙/柱/门/月台/区域/巷道/货架目标和 LM-FR-015 的 SourceRef、命中规则、几何规则、置信度及画布位置已由现有 Semantic Preview/Diagnostic Index 权威覆盖。
- LM-FR-016 补齐稳定分类：零长度、零面积、缺失半径与退化变换进入 `SPACE_CAD_SEMANTIC_ZERO_SIZE`；开放边界进入 `SPACE_CAD_SEMANTIC_BOUNDARY_UNCLOSED`；楼层越界保留全图 Blocking，并追加逐对象 SourceRef Warning，经 Preparation/OpenAPI/双 SDK 直接进入 CAD 起始向导的问题清单。
- 新增同目标面积几何重叠检查：Polygon/Circle 使用边界预筛和实际相交判断，只为真实正面积重叠的双方生成可定位 `SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP`，边界接触、不同目标包含和降级 BlockInstance 不报重叠。
- 门禁为 Space Unit 544/544、CAD Preparation/Parse/BuildScene/Excel 集成聚焦 37/37、CAD 实验工具常规门禁 39 passed / 1 个安装环境用例 skipped、OpenAPI 55/55、CAD 向导 4/4、Vue TypeScript、CP6.Tests 2,933、完整 solution Release 0 warning / 0 error；配置安装环境后，签名有效的 AutoCAD 2025 Core Console 真实 Autodesk DWG 用例另行 1/1、0 skipped。详见 `docs/space/reports/2026-08-16-space-cad-semantic-quality-diagnostics.md`。
- LM-FR-014～016 仓库实现闭环；真实主备 Provider、20 份授权黄金 CAD、双仓 Pilot 与五方签字仍未完成，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space 租户私有 CAD Mapping Profile

- 新增 `Space_LayerMappingProfile` 与 append-only `Space_LayerMappingProfileVersion`：Tenant 过滤、复合外键、唯一名称/版本、RowVersion、规范 Profile JSON、Definition SHA-256、复制来源和创建审计均持久化；已发布迁移未修改。
- System Profile 保持只读；租户可复制系统/本租户版本、结构化维护图层与块匹配规则、启停方案，并以 `ExpectedRowVersion + Idempotency-Key` 追加新版本。跨租户读取/复制返回稳定 NotFound，旧版本更新/删除由 `SpaceContext` 失败关闭。
- Design V1 新增 CAD Profile 管理 list/get/save，Preparation Catalog 自动消费当前租户版本；OpenAPI、C#/TypeScript SDK、权限矩阵和 Problem Details 同步。CAD 起始向导无需填写内部 ID，可复制/编辑规则并在保存后自动刷新选中新启用版本。
- 门禁为 Space Unit 540/540、Space Integration 真 SQL 453/453（0 skipped）、CP6.Tests 2,933、Web 866、Vue TypeScript、production build、OpenAPI/双 SDK、EF 无 pending model changes，以及完整 solution Release 0 warning / 0 error。详见 `docs/space/reports/2026-08-16-space-tenant-cad-mapping-profiles.md`。
- LM-FR-013 仓库实现闭环；WP4 仍需真实多路径、Provider、黄金 CAD、WMS 与 Pilot 接受证据，核心 GA 保持 72% / `NoGo`。

## 2026-08-16 Space CAD 图层/块审核与逐层 Override

- Design V1 CAD Preparation Preview 复用现有确定性 Inventory/Mapping 权威，新增面向审核的完整图层与块清单；原始 CAD 字节和逐块引用明细不进入浏览器。
- CAD 向导可搜索并查看图层颜色、线型、可见性、对象/支持/未支持计数和块定义/引用/属性计数；映射 Profile 明确显示系统公共或租户私有 Scope。
- 每个图层可显式沿用 Profile、忽略或覆盖语义目标，并调整几何规则和置信度。单位、坐标、Profile 或 Override 变化都会撤销确认并阻止使用旧 Preview 启动 Parse，重新预览后由服务端密封完整 Override Snapshot。
- 门禁为 Space Unit 540/540、Space Integration 真 SQL 447/447（0 skipped）、CP6.Tests 2,932、Web 863、Vue TypeScript、production build、OpenAPI/双 SDK 和完整 solution Release 0 warning / 0 error。详见 `docs/space/reports/2026-08-16-space-cad-inventory-layer-overrides.md`。
- LM-FR-012 仓库实现闭环；当时 LM-FR-013 只关闭逐层 Override 与 Scope 展示，租户私有 Profile 已由同日后续任务闭环。WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space 来源移除引用预检

- 新增 Design V1 来源移除预检与确认 Apply：预检按“阻断/保留”分类返回 Draft、任务、生成、底图、设计对象/元数据和历史审计引用，Apply 使用 Expected ContentRevision、Expected Source RowVersion、Idempotency-Key 与 Serializable 事务重新复核。
- 活动引用或预检后的并发变化统一零写入；成功只软删除来源记录。物理文件、终态 Job、工件、问题、标定和导入审计继续受原有保留权威管理，不级联删除。
- Space Studio 来源面板展示引用计数、只读保护和明确保留提示；OpenAPI、C#/TypeScript SDK、权限矩阵和稳定 `SPACE_SOURCE_REFERENCED` 错误同步。
- 全量门禁为 Space Unit 540/540、Space Integration 真 SQL 447/447（0 skipped）、CP6.Tests 2,932、Web 862、OpenAPI/双 SDK/EF/production build 和完整 solution Release 0 warning / 0 error。详见 `docs/space/reports/2026-08-15-space-source-removal-preflight.md`。
- LM-FR-005 仓库实现闭环；LM-FR-010～016、019/019A 与真实多路径接受证据仍待完成，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space 上传重复内容复用提示

- CAD 前端上传合同补齐服务端 `file/reused` 事实；CAD 与 PDF/图片底图检测到重复内容时明确提示按 SHA-256 复用受控文件或当前来源，不会重复保存原文件。
- 复用判断仍完全来自隔离上传服务；客户端不生成哈希、不跳过安全扫描，重复底图继续按 Clean/Scanning/Rejected 状态进入既有挂接链。
- 聚焦测试 10/10、Vue TypeScript、Web 全量 858/858 和 production build 通过。详见 `docs/space/reports/2026-08-15-space-upload-reuse-notice.md`。
- Excel 后端/SDK 已有 `Reused` 合同；该条记录时缺失的当前 CAD + Excel 上传 UI 与历史 CAD 候选目录均已由 2026-08-16 后续纵切闭环。LM-FR-005 已由后续来源移除预检纵切闭环。WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Draft 来源与阻断摘要

- Design V1 Version 列表与详情新增来源、创建者、创建/更新时间和 Open Blocking 数；现有 Blank/PublishedVersion 创建路径返回稳定来源语义，历史创建者为空时不伪造姓名。
- Space Studio 活动 Draft 卡片直接展示这些字段；Blocking 数量使用文字与阻断语义色，日期按浏览器区域格式显示。
- Space Integration 真库 444/444、Space Unit 537/537、CP6.Tests 2,926（19 个既有环境门禁跳过）、Web 856/856、OpenAPI/双 SDK、EF、类型检查、生产构建及完整 solution Release 0 warning / 0 error 通过。详见 `docs/space/reports/2026-08-15-space-draft-summary-metadata.md`。
- 当前已支持的 Draft 创建路径 LM-FR-002 摘要缺口关闭；System/Tenant Template 创建来源须随四模式向导持久化，创建者显示名解析也仍为后续边界。LM-FR-001/WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space System 整仓模板按楼层写入 Draft

- 新增 `POST /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/templates/{templateId}:apply`，只接受服务端内置模板版本和密封 Proposal，按一个模板楼层生成确定性的 Zone/Aisle/Rack/逐层规格/Location 命令。
- Apply 绑定目标 Site、页面 Lease/ClientInstance、Floor/Content Revision 与 CommandBatch；整批和 Floor 边界在同一 Serializable 事务中提交，正常 Layout 命令仍保持 100 条上限，受控模板内部上限为 300 条。
- Space Studio「构件」面板可预览模板、按目标 Floor 编码优先选择模板楼层、显示逐楼层计数并显式确认；状态未知时冻结选择并按原命令包安全重试，窄屏、只读、无租约和 Revision 冲突禁止写入。
- Space Unit 537/537、Space Integration 真库 443/443、CP6.Tests 2,925（19 个既有环境门禁跳过）、Web 856/856、OpenAPI/双 SDK、权限、EF、类型检查、生产构建及完整 solution Release 0 warning / 0 error 通过。详见 `docs/space/reports/2026-08-15-space-system-template-floor-apply.md`。
- Tenant 私有模板和 Blank/Published/System/Tenant 四模式统一向导仍未完成；LM-FR-001/WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space System 整仓模板目录与预览纵切

- 新增 Design V1 整仓模板 GET 与实例化预览 POST 合同；平台内置标准模板固定模板/版本/内容 SHA，并只包含 2 层、7 区、20 巷道、500 货架与 10,000 库位的设计布局。
- 预览返回完整 Floor/Zone/Aisle/Rack 父级计划和 Proposal Hash，固定 `writesDraft=false`；旧模板版本、非法 scope、未知模板和外部主体失败关闭。
- Space Studio 项目入口可以展示平台模板计数并查看密封预览；OpenAPI、双 SDK、权限矩阵和前端类型同步。
- Space Unit 536/536、CP6.Tests 2,924 通过、Web 851/851、契约/SDK/EF/GA 证据门禁和生产构建通过；完整 solution Release 0 warning / 0 error。
- 本纵切不实现租户私有模板、Template → Draft Apply 或四模式统一向导，LM-FR-001/WP1 仍为 Partial/Pending，核心 GA 仍为 72% / `NoGo`。详见 `docs/space/reports/2026-08-15-space-system-template-catalog.md`。

## 2026-08-15 Design V1 Floor shell 与项目入口纵切

- Space 首页新增按 Site 进入 `Space Studio` 的用户入口；页面自动读取活动 Draft 与活动设计楼层，不再要求用户手工拼 VersionId/FloorLogicalId。
- 新增 Design V1 Floor GET/POST 合同。创建必须显式提交编码、名称、层级、标高、层高和 Expected Content Revision，并以 Version 级 SQL 锁、Serializable 事务、Content Revision 与 Idempotency-Key 原子提交。
- Floor 创建后直接进入既有 `DesignUnderlayView`，后续继续遵循 Floor Lease、Floor Revision 与 Command Batch；低于 1280px 的入口禁止写入。
- 真 SQL 聚焦 4/4、Space Unit 534/534、Space Integration 真库全量 441/441、CP6.Tests 2,923 通过、Web 全量 848/848、OpenAPI/双 SDK/EF/GA 证据门禁、Vue TypeScript 与生产构建通过；完整 solution Release 0 warning / 0 error。详见 `docs/space/reports/2026-08-15-space-design-floor-shell.md`。
- 本纵切不实现整仓 System/Tenant 模板或四模式统一创建向导，LM-FR-001/WP1 仍为 Partial/Pending，核心 GA 仍为 72% / `NoGo`。

## 2026-08-15 Design V1 空白 Draft 初始化纵切

- `POST /api/space/design/v1/sites/{siteId}/versions` 新增 `Blank` 模式；草稿不继承 Published 内容，拒绝 `BasedOnVersionId`，保留线上指针并占用唯一活动 Draft 槽。
- 新增 `InitializeVersion` 完成态 Job/Attempt 和 `space-blank-v1` 初始化身份；Version Operation fence、请求 Hash、SQL 事务及既有 Idempotency-Key 共同保证重放返回同一 Version/Job，不同输入失败关闭。
- 领域聚焦 7/7、SQL Server LocalDB 聚焦 2/2、Space Integration 真库全量 437/437 且 0 skipped。详细报告见 `docs/space/reports/2026-08-15-space-design-blank-draft.md`。
- 该版本纵切不创建或猜测 Floor；Floor 初始化/选择随后由独立纵切交付。平台/租户整仓模板仍缺，LM-FR-001 与 WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio LM-FR-025～029 最终工作台 UX 要求

- 保存后的同一 Design Scene 直接驱动 2D/3D；选择与逐楼层相机跨模式保留。切到 3D 不再清除 2D 未保存重画，标题持续标记、3D 禁止误提交，切回 2D 后保留全部点集。
- 首次四步任务清单默认展开并可折叠重开，补齐 44px 热区、焦点环、符号和可访问完成状态；右侧问题严重度筛选控件同样补齐 44px 热区。
- 聚焦单测 5/5、Web 843/843、Space Studio Playwright 23/23、production build 和完整 Release solution 通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-final-ux-requirements.md`。
- LM-FR-025～029 仓库实现闭环；WP4 保持 Partial/Pending，WP5 保持 Complete/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio 两点实距标定工作流

- 底图标定明确为 P1 原点、P2 比例点和独立验证点 V；用户直接输入真实距离、世界原点、旋转和 V 世界坐标，不再手工换算 P2 世界坐标。
- 工作台以栅格 Y-up 坐标和整数毫米请求合同计算比例/旋转/偏移，预览第三点误差与 `max(50mm, 实距×0.2%)` 阈值；无效或超限输入在提交前失败关闭。
- 保存继续复用 Design V1 租约、双 Revision、数据库 UTC、幂等 CommandBatch 和公共撤销/重做。Web 841/841、Space Studio Playwright 23/23、production build 和完整 Release solution 通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-underlay-calibration-workflow.md`。
- LM-FR-021 仓库实现闭环；真实 PDF/图片、多路径、Provider、WMS 和 Pilot 接受仍未完成，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio 托盘与静态设备构件库

- 构件库与既有 Zone/Aisle/Rack/Location 表单同页，补齐墙、柱、门、月台、托盘和输送线、AGV、叉车、工作台、电子秤、充电站六类固定静态设备。
- 每个预设固定领域类型、尺寸、业务编码前缀、目录/设备子类和 `Static` 设计属性；不引入实时状态、运动或第二套领域权威。
- 创建复用 Design V1 租约/Revision/Hash/幂等命令链，并以 Delete/Restore 进入公共撤销/重做历史。Web 837/837、Space Studio Playwright 23/23、production build 和完整 Release solution 通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-static-component-library.md`。
- LM-FR-022 仓库实现闭环；真实多路径、Provider、WMS 和 Pilot 接受仍未完成，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio 底图图层控制

- “图层”模式提供底图显示/隐藏、0～100% 透明度和锁定/解锁；控制直接重绘 Konva 底图，44px 热区、键盘焦点、状态文本和无底图禁用状态同步。
- 锁定会阻止比例/坐标标定，新挂接底图自动解锁，标定成功后自动锁回；视图偏好按版本/楼层保存在当前浏览器标签页，不推进 Draft Revision。
- floor view schema v1 向后兼容地增加可选底图状态并校验边界；单测、类型检查及 Playwright 覆盖实际画布变化和重载恢复。详细报告见 `docs/space/reports/2026-08-15-space-studio-underlay-layer-controls.md`。
- LM-FR-020 仓库实现闭环；真实 PDF/PNG/JPG、三条路径、Provider、WMS 和 Pilot 接受仍未完成，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio 底图统一撤销/重做

- PDF/PNG/JPG 底图的挂接、替换、标定和显式移除统一携带页面实例、编辑租约、Floor/Content Revision、CommandBatch 与幂等键；未取得当前会话租约或 Revision 已变化时零 Draft 写入。
- 服务端复用不可变 Element Command Batch/Record 密封底图 Source、Calibration 和变换前后态；Undo/Redo 只接受原批次、方向与历史 Hash，校验当前状态后恢复追加式标定指针，并写入新的不可变补偿批次。
- 工作台把底图操作加入既有公共历史栈；OpenAPI/C#/TypeScript SDK、真 SQL、Web、Playwright 和构建门禁同步。详细报告见 `docs/space/reports/2026-08-15-space-studio-underlay-history.md`。
- LM-FR-024 的 CAD、Excel–CAD 和底图可逆历史仓库实现已闭环；真实多路径、Provider、WMS 和 Pilot 接受仍未完成，WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio Excel–CAD 确认统一撤销/重做

- Excel–CAD v2 Apply 结果从实际不可变 Command Record 密封历史 Hash 与数量；客户端不能提交可信补偿正文，旧 v1 成功结果仍可读取但不会伪装成可撤销历史。
- 新增服务器 Undo/Redo 补偿链，统一验证页面租约、Floor/Content Revision、内容 Hash、原 Apply 工件链、当前 Rack/层/库位/绑定/属性/Source 状态与幂等键；每次补偿形成新的不可变审计批次，介入编辑时零写入。
- 工作台把确认结果加入现有统一历史栈。OpenAPI/双 SDK、后端、真 SQL 1/1、Web 817/817、Space Studio Playwright 21/21、Release solution 0 warning/0 error 与 SDK drift 通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-excel-cad-apply-history.md`。
- LM-FR-024 的 CAD 与 Excel–CAD 历史已完成，仍剩底图挂接/标定可逆合同；WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio Excel–CAD 确认 Lease/Revision Fence

- Excel–CAD 确认请求强制携带 `clientInstanceId`、`leaseId`、Floor Revision 和 Content Revision；确认服务与后台 Worker 均在实际写入前验证同一请求人、同一页面实例和未过期租约，并与普通 Design V1 编辑复用 Floor application lock。
- SQL Server 租约到期判断使用 `SYSUTCDATETIME()`；换会话、释放/过期租约或双 Revision 漂移均失败关闭，且不创建 Rack、CommandBatch 或部分层级数据。历史成功 payload 可读，未完成的无租约旧 payload 不会继续写入。
- OpenAPI/C#/TypeScript SDK 和工作台门禁同步；后端聚焦 14/14、Space Unit 533/533、契约/Controller 50/50、CP6.Tests 2919 passed、Web 814/814、Space Studio Playwright 21/21、完整 Release solution 0 warning/0 error及 SDK drift 通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-excel-cad-apply-lease.md`。
- 该任务只关闭 Excel–CAD 统一历史前的写入安全前置条件；补偿命令和底图可逆合同仍待完成。WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 确认批次撤销/重做

- CAD Typed Changeset 显式 Apply 后，服务端按实际提交结果密封统一历史：新增为 Delete/Restore，删除为 Restore/Delete，修改为提交前/后的完整 Update 快照；多项撤销逆序，LogicalId 保持稳定。
- Element Command 幂等响应持久保存首次修改前的元素和属性快照；CAD Apply 回放返回同一撤销/重做集合。工作台只接受白名单命令和完整数量，异常历史会保护性切换只读。
- OpenAPI/C#/TypeScript SDK 同步。门禁通过：CAD 2/2、SQL Server LocalDB 1/1 且 0 skipped、OpenAPI 45/45、Space Unit 533/533、Web 813/813、Space Studio Playwright 21/21、Vue production build、完整解决方案 0 warning/0 error及 SDK 二次生成无漂移。详细报告见 `docs/space/reports/2026-08-15-space-studio-cad-apply-history.md`。
- 该纵切只关闭 LM-FR-024 的 CAD 确认批次；Excel–CAD 确认和底图挂接/标定仍待接入统一历史。WP4 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 人工校正锁定

- CAD 来源通用元素可在属性检查器中原子保存并锁定/解除锁定；Design Revision 持久保存锁状态、单调校正版本、最后操作者与 UTC 时间，锁定后的继续编辑递增版本，撤销/重做显式恢复锁状态。
- 重新解析同一 SourceRef 时，锁定对象的修改或删除转为不可应用的 Blocking Conflict；审核空间展示并定位校正版本，Design V1 对任何携带 CAD Changeset 身份且指向锁定对象的命令执行最终 409 Fence，保证零写入。
- 新增加法迁移与版本克隆映射，OpenAPI/C#/TypeScript SDK 同步。门禁通过：Space Unit 533/533、CAD reparse 1/1、OpenAPI 45/45、Web 809/809、Space Studio Playwright 20/20、SQL Server LocalDB 1/1 且 0 skipped、Vue production build、EF 无模型漂移及 Release solution 0 warning/0 error。详细报告见 `docs/space/reports/2026-08-15-space-studio-manual-correction-lock.md`。
- LM-FR-018 仓库实现已闭环；WP4 仍为 Partial/Pending，真实 CAD/Provider/WMS/Pilot/签字不因本项自动完成，核心 GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio 对象复制

- 批量检查器可复制 1–100 个 Active 通用元素和货架，允许混合选择；确认前零写入，确认后 `CreateElement` 与 `GenerateRackArray` 使用同一 Lease/Revision/Content Hash/幂等原子批。
- 通用元素副本保留几何、类型、父级和设计属性，但清除唯一业务编码、业务链接及 CAD 来源；货架副本复制 Active RackLevel 与空编码、Generated/Unbound Location，不复制 WMS 绑定语义，并生成 Zone 内新编码。
- 撤销/重做只对既有新 LogicalId 执行 Delete/Restore，不重复 Create。复制聚焦 4/4、面板 3/3、前端全量 805/805、真 SQL 1/1、Space Studio Playwright 19/19、Space Unit 531/531、OpenAPI 44/44、类型检查、构建、SDK drift 与 GA 自测通过。详细报告见 `docs/space/reports/2026-08-15-space-studio-object-copy.md`。
- LM-FR-023 的对齐、等距分布、复制、旋转和阵列仓库实现现已闭环；WP4 保持 Partial/Pending，GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 异常对象画布重画

- 单个 Active 非资产通用元素可在 2D 画布进入重画模式；R/Enter/Backspace/Esc 与命令栏按钮可达，状态栏持续显示本地未保存顶点。3–100 点、重复、零面积、自交和 Int32 包络校验均在显式确认前完成，取消时 Draft 零写入。
- 确认后只提交同一 LogicalId 的 `UpdateProperties`，将世界轮廓规范化为局部多边形并保留类型、BusinessCode、业务链接、设计属性及 CAD SourceId/SourceRef；撤销/重做仍为同一 ID 的补偿更新，2D/3D 消费同一几何。
- 门禁通过：重画聚焦 6/6、前端 800/800、Space Unit 531/531、OpenAPI 44/44、SQL Server LocalDB 1/1 且 0 skipped、Space Studio Playwright 18/18、Vue type-check、production build、Release solution 0 warning/0 error、SDK drift 和 GA 证据 36/36。详细报告见 `docs/space/reports/2026-08-15-space-cad-exception-redraw.md`。
- 该纵切关闭 LM-FR-017 的“重画”，五项异常处理仓库能力现均已实现。WP4 仍保持 Partial/Pending，须继续复核其它 LM-FR 并取得真实 CAD/Excel/PDF/Provider/WMS 接受证据；GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 异常对象拆分

- 单个 Active 非资产 `group` 元素可拆成 2–100 个独立元素：首部件保留当前 LogicalId，其余部件分配新 LogicalId，并继承类型、父级、BusinessCode、业务链接、设计属性和成对的 CAD SourceId/SourceRef。
- 正向/撤销/重做分别复用 `UpdateProperties + CreateElement`、`UpdateProperties + DeleteObject`、`UpdateProperties + RestoreLogicalObject` 原子批；重做保持相同新 LogicalId且不会重复 Create。组合整体移动/旋转按参数化渲染器同一坐标变换展开，2D/3D 拆分前后等价。
- `SpaceCreateElementDto` 以可选成对字段补齐业务链接继承，OpenAPI、双 SDK 和零写入验证同步。门禁通过：Space Unit 531/531、前端 794/794、SQL Server LocalDB 1/1 且 0 skipped、Space Studio Playwright 17/17、Vue type-check、production build、Release solution、SDK drift 和 GA 证据 36/36。详细报告见 `docs/space/reports/2026-08-15-space-cad-exception-split.md`。
- 该纵切关闭 LM-FR-017 的“拆分”；画布重画已由后续独立纵切关闭。WP4 保持 Partial，GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 异常对象合并

- 新增受约束的 `group` 组合几何，允许 2–20 个通用元素在保留首选 LogicalId 的前提下合并；来源 LogicalId/SourceId/SourceRef、相对位置、旋转、尺寸和原始几何均保留，资产、元数据或属性冲突失败关闭。
- 合并复用现有 `UpdateProperties + DeleteObject` 原子命令，撤销复用 `UpdateProperties + RestoreLogicalObject` 补偿批次，没有第二套写接口；2D/3D 递归渲染共享同一 LogicalId，工作台提供显式确认和可达按钮。
- 门禁通过：Space Element 50/50、Space Unit 531/531、前端聚焦 21/21 与全量 788/788、Vue type-check、SQL Server LocalDB 1/1 且 0 skipped、Space Studio Playwright 16/16、完整 Release solution 0 warning/0 error、production build、SDK drift 和 GA 证据 36/36。详细报告见 `docs/space/reports/2026-08-15-space-cad-exception-merge.md`。
- 该纵切关闭 LM-FR-017 的“合并”；拆分与画布重画已由后续独立纵切关闭。WP4 保持 Partial，GA 保持 72% / `NoGo`。

## 2026-08-15 Space Studio CAD 异常对象改类型

- Design V1 `UpdateProperties` 增加可选 `ElementType`，支持通用元素在保留 LogicalId 的情况下切换到受支持语义类型；资产实例拒绝改型，未知类型在零写入前失败。
- 工作台属性检查器增加构件类型选择；保存、场景刷新、命令审计及撤销/重做继续复用 Lease、Floor/Content Revision 与幂等命令批，没有建立第二套设计权威。
- 生成 OpenAPI、C# 与 TypeScript SDK 已同步；真 SQL 1/1、Space Unit 全量 526/526、OpenAPI 44/44、前端全量 780/780、Vue type-check 和 Space Studio Playwright 全量 15/15 通过。
- 该纵切只关闭 LM-FR-017 的“改类型”；删除为既有能力，合并、拆分与重画已由后续独立纵切关闭。WP4 仍为 Partial，GA 保持 72% / `NoGo`。

## 2026-08-15 Space AutoCAD Core Console 开发转换链

- 新增实验型 `convert-autocad-dev-ir` 和 `ICadConverter`，以显式 Core Console 路径把原生 DWG 转为中间 DXF，再复用既有确定性 CAD IR 转换与共合同执行器。
- 原始 DWG/中间 DXF 仅进入 D 盘每次唯一 `attempts` 目录，校验源哈希与 Core Console 文件版本；Activity Insights 持久运行包进入不允许出现 DWG/DXF 的独立缓存。子进程无 Shell，具备超时/取消进程树终止和原始数据目录清理重试。
- 本机签名有效的 AutoCAD 2025 Core Console 安装型测试 1/1 通过；Autodesk Floor Plan 样例连续两次得到相同 CAD IR SHA，4,424 个实体中 4,422 个受支持。该结果仅为开发证据，不计 Provider 认证、黄金 CAD 或 GA 完成度。
- GA 总索引已把该开发报告登记到 WP3，并把 2026-08-15 仓库完成度审计登记到 WP0，路径校验通过；共享 JSON 兼容层让四个证据校验器在 PowerShell 7.6 保留 ISO 时间字符串，同时保持 Windows PowerShell 5.1 行为和原有严格门禁。证据保持实现态，不进入接受证明，核心 GA 仍为 72% / `NoGo`。

## 2026-08-15 Space Studio 单人开发人员种子

- 新增 `00001`～`00005` 五个 `DevelopmentSeed` 虚拟人员，为一名真实开发者提供产品、后端、前端/3D、QA、WMS、架构、安全和 DevOps 的本地流程视角。
- 人员册固定 `formalGaEligible=false`、无生产访问、无正式签字资格；专项校验阻止开发编号进入正式 GA 人员或证据字段，总 GA 与开工人名校验也拒绝纯数字及开发/测试身份。
- 本项仅完成开发测试人员配置和防误报护栏，不创建登录凭据，不证明真实 2+2+1 团队或五方审批，正式进度保持 72% / `NoGo`。

## 2026-08-14 Space Studio M0 开工证据语义门禁

- 五类外部输入新增共享的结构化开工 Manifest、复制模板、协议和专项校验器，可按分区增量完成，不要求一次性伪造全绿。
- 门禁覆盖五角色实名/审批权、2 Backend + 2 Frontend3D + 1 QA 与共享角色、20 份授权 CAD 候选、至少两条 `ICadConverter` 审批链和隔离 Worker、Greenfield/Retrofit 双仓与 CP6 WMS 窗口。
- 总 GA 校验器要求每个 Complete 输入绑定并证明 Manifest 自身哈希，复核分区 Owner 及签字人索引一致；专项 26/26、组合证明链 34/34。真实输入仍全部 Pending，不计外部执行完成。

## 2026-08-14 Space Studio 正式黄金 CAD 证据门禁

- WP7 新增正式黄金 CAD Manifest、模板、协议和专项校验器，组合已有离线质量评估和 Provider 资格输出，不重造指标算法。
- 强制 20 份授权样本、10/5/5、L1～L5、DWG/DXF、双标注/QA 仲裁、主备同 Source Set/Worker、release-eligible、五项质量阈值、Holdout 零 Blocking 遗漏和 50 MiB/Ready P95。
- 总 GA 校验器要求授权 CAD、Provider/Worker 外部输入和 WP3 验收先完成，再复核 Manifest 自身哈希；专项 31/31、组合证明链 29/29。真实 CAD/Provider 仍 Pending，不计正式执行。

## 2026-08-14 Space Studio 双仓 Pilot 证据门禁

- WP8 新增结构化 Pilot Manifest、复制模板、现场证据协议和专项校验器；强制一个 Greenfield、一个 Retrofit、各连续至少 14 天并逐日列出不可重复/不缺日的不可变记录。
- Gate 语义覆盖零 S1/S2、每个 S3 的可用绕行与全关闭、2D/3D/WMS 100% 一致、自动/人工恢复 15/240 分钟、旧 Published 持续可用、Published-only/无长期双写及客户仓库代表和实施负责人实名确认。
- 通用 GA 校验器在 WP8 Accepted 时先要求五方内部签字均 Signed 且签字接受人与登记姓名一致，再复核 Manifest 自身哈希和专项语义，并拒绝模板、Manifest/嵌套证明中的 fixture、未来窗口和 Pilot 结束前预签；Pilot 专项 21/21、通用证明链 23/23。真实 Pilot 仍为 Pending，不计为现场完成。

## 2026-08-14 Space Studio 2D 画布拖动精调

- Rack 和通用 Element 可在选择工具下直接拖动，屏幕位移按 Zoom 转换成整数世界毫米；已选对象保持多选整体移动，选择修饰键不会误提交拖动。
- 写入复用 `MoveObject`，携带 Lease、Client Instance、Floor Revision、Content Revision/Hash 和幂等批次；撤销提交反向命令，失败重新渲染权威场景。Zone/Aisle 继续使用 Design V1 Layout 合同。
- 门禁通过：拖动/命令聚焦 14/14、前端全量 780/780、Space Studio Playwright 14/14、拖动连续复跑 5/5、Vue type-check 和 production build。

## 2026-08-14 Space CAD Provider SQL Server 门禁

- `SpaceCadProviderSqlServerTests` 已在 SQL Server 17.0.4025.3 LocalDB 真实执行，3/3 passed、0 failed、0 skipped，运行后无 `CP6SpaceCadProviders_*` 临时数据库残留。
- 覆盖并发 Replace、唯一 Current Revision、历史追加、认证证据不可变、幂等记录、路由/资格/版本迁移脚本重复执行，以及旧资格或 Provider Version 缺失时失败关闭。
- 该项关闭 WP3 仓库 SQL 自动化 skip；测试 Provider 不是 ODA/APS，真实主备适配、客户审批、黄金 CAD、冻结 Worker 和 Site 故障切换仍为 Pending。

## 2026-08-14 Space/WMS CP6.Tests 真库门禁

- 在 SQL Server 17.0.4025.3 LocalDB 上单独运行 CP6.Tests 的 SpaceSqlIntegration、WmsProductionSqlServer 和 SpaceIntegrationEventOccurredAtUtc 三个集合，15/15 passed、0 failed、0 skipped。
- 证据覆盖过滤唯一索引、NULL 草稿码、两阶段换码、rowversion、Control Tower SQL、WMS Move/Replenish/Serial/LPN/Feature Flag 原子事务，以及两连接 Session applock/UTC 回填释放。
- 全套 CP6.Tests 开启 SQL 后为 2932 passed / 2 unrelated failed / 1 intentional skipped；两个失败来自 OA/PUR 要求显式共享隔离 Stage，未被普通 LocalDB 绕过，也不混入 Space/WMS 通过口径。本项不替代生产 CP6 WMS 或生产等价 SQL 接受。

## 2026-08-14 Space Studio 全量 SQL Server LocalDB 门禁

- 首次把 `CP6_TEST_SQLSERVER` 指向 SQL Server 17.0.4025.3 LocalDB 后，完整 Space Integration 从原先的环境 skip 变为真实执行；第一次结果 424/426，实际暴露发布恢复查询翻译和 Published Viewer 测试夹具违反不可变快照顺序两项问题。
- 发布查询已独立修复；Viewer 夹具改为在 Draft 阶段保存完整 Published 楼层，再执行 Validation → Ready → Publishing → Published，未放宽任何生产不可变护栏。Published Scene SQL 聚焦 7/7 通过。
- 最终全量复跑 426/426 passed、0 failed、0 skipped。该证据关闭本机真实 SQL 自动化执行缺口，但不把 LocalDB 冒充生产等价 SQL、CP6 WMS、IdP、告警、恢复演练或 Pilot。

## 2026-08-14 Space Studio WP6 发布恢复指标真库查询修复

- 启用 `CP6_TEST_SQLSERVER` 后，全量真库门禁暴露发布恢复指标的复合键 GroupJoin 无法由 SQL Server Provider 翻译；查询改为显式 TenantId、AttemptId 与 AttemptStatus 相关子查询，继续忽略租户查询过滤器做无标签跨租户聚合。
- WMS 首次超时 → WaitingRetry 指标 → 旧 Published 保持 → 正式重试完成的真库场景恢复通过；恢复指标单测 6/6、发布编排 SQL Server 3/3 通过。
- 本项不把 LocalDB 冒充生产等价 SQL/WMS 或告警链接受；首次全量 SQL 的独立 Published Viewer 数据准备失败已由后续任务修复，最终 426/426 见全量真库门禁报告。

## 2026-08-14 Space Studio WP3 CAD Converter 共合同执行器

- 新增 `SpaceCadConverterContractRunner` 作为 `ICadConverter` 强制执行边界：Source Stream 对适配器只读且所有权保留，Sink 只能按 Document → Layer/Block → Entity → Complete 顺序单线程写入，并验证唯一 ID、逐层数量、汇总与 Bounds。
- 转换 Result 必须与 Sink 实际提交的 Source SHA、Provider Key/Version、Artifact SHA、Summary 和 Issues 完整一致；适配器捕获并忽略 Source 写入或 Sink 协议异常仍会以稳定内部码失败关闭。公共合同补齐未定义枚举和负计数拒绝，开发转换入口已接入 Runner。
- 门禁通过：Runner/CAD IR 合同聚焦 23/23、Space Unit 525/525、CAD Experiment 34/34、完整 Release solution 0 warning / 0 error。
- 本项没有实现或认证真实 ODA/APS，不接受 Mock/fixture 为生产证据；真实适配器、隔离 Worker、20 份黄金 CAD、双链 Site 审批和故障切换仍为 Pending，WP3 保持 Partial/Pending，核心 GA 保持 72% / No-Go。

## 2026-08-14 Space Studio WP0 GA 证据证明链加固

- Signed Signer、Complete External Input 和 Accepted Gate 现在统一校验受控 URI、SHA-256、真实接受人和 UTC 时间；仓库内证据会重算内容哈希。
- 越界/不存在/哈希不一致文件、不安全 URI、原始 DWG/DXF 仓库路径、非 UTC/未来时间和占位人名均失败关闭；新 CI 工作流运行当前索引及 16 个正反向自测。
- 本项只关闭证据造假/漂移通道，没有接受任何外部输入或 Gate；核心 GA 仍为 72% / No-Go。

## 2026-08-14 Space Studio WP3 CAD Provider 版本认证围栏

- Site Provider 认证和运行时注册新增必填 `ProviderVersion`，能力查询、执行路由和 Provider 输出身份均要求 Key + Version 完全一致；版本不一致时能力显示明确阻断并在调用 Provider 前失败关闭。
- 当前 Parse payload 升级为 v5，除 Mapping Replay Snapshot 外继续封存 Preparation 的 Provider Version；评分工具生成的 Site 认证输入携带同一候选版本，CAD 向导显示主备认证版本，OpenAPI/C#/TypeScript SDK 同步必填合同。
- 新增独立可回滚迁移和幂等 SQL；历史认证行不猜测补版本并按不完整资格失败关闭。仓库版本围栏完成不等于真实 ODA/APS 或替代者已认证，真 SQL、黄金 CAD、Site 审批和 Pilot 仍为 Pending，WP3 保持 Partial/Pending。

## 2026-08-14 Space Studio WP3 CAD 映射确定性重放快照

- sealed Preparation 现在保存服务器生成、规范排序并由 SHA-256 密封的 Mapping Replay Snapshot，绑定 Tenant、Source、不可变 Profile、Inventory/Structure/Preview Hash 和用户确认的完整 Layer Overrides；客户端不能提交或替换快照。
- 历史 Parse Job v4 首次携带同一快照；当前 v5 继续携带快照并新增 Provider Version 围栏。启动服务与 Worker 分别在入队前和 Provider 调用前验证身份/哈希。损坏或缺失的当前快照/版本零 Job 或零 Provider 写入，历史 v2–v4 保持显式兼容读取。
- 新增可回滚迁移、幂等 SQL、合同说明与聚焦自动化。当前 18 个 Mapping 单测和 15 个 Preparation/Parse 集成测试通过；全量 Space Unit 512/512、Integration 313 passed / 106 environment skipped、CP6.Tests 2,916 passed / 19 environment skipped，Release solution 0 warning / 0 error。真实适配器仍须按快照重放并执行结果核验，ODA/APS、黄金 CAD、Site 审批和 Pilot 均未发生，因此 WP3 仍为 Partial/Pending。

## 2026-08-14 Space Studio WP3 Provider 评分与选型工具

- `CP6.Space.CadExperiment qualify-providers` 已把 ADR-0001 的六维 25/20/15/15/15/10 评分、80 分门槛、四项硬门禁、同黄金集/同冻结环境和唯一第一/第二名规则机器化；输入异常、门禁缺失、基线混用或名次并列均失败关闭。
- Pass 只生成一主一备两条受选择报告 SHA-256 绑定的 Site 认证输入；No-Go 报告保留逐候选阻断码但认证输入为空，工具本身不写 Site 配置、不读取 Secret 值。
- 聚焦工具测试 34/34 通过。该项只关闭仓库评分与审计工具，不代表真实候选、客户审批、黄金 CAD、冻结 Worker 或目标 Site 已通过，因此 WP3 接受状态仍为 Pending。

## 2026-08-14 Space Studio WP3 Provider 资格与确定性主备排名

- Site Provider 新认证必须同时记录 Licensing/Security/Data Region/Deletion-Retention 四项通过状态、ADR-0001 总分、规则版本、黄金集 SHA、冻结环境 SHA 和资格证据引用；四项门禁缺一、总分低于 80 或证据字段不完整均返回 422 且零写入。
- 两条链必须绑定同一规则、黄金集和冻结环境；服务端拒绝 Primary 低于 Backup 以及同分无法唯一排序的配置。旧认证迁移后不补造资格证据，能力接口输出资格阻断码，执行路由不再使用这些记录。
- 数据库使用独立可回滚迁移，OpenAPI、C#/TypeScript SDK、前端能力类型和自动化同步。本卡只完成资格合同与路由防线；真实 Provider 适配、冻结环境评分、Site 审批和双链证据仍为 WP3 No-Go 门禁。
- 门禁通过：Release solution 0 warning / 0 error；Provider 聚焦 12/12，Space Unit 506/506、Space Integration 310 passed / 106 environment skipped、CP6.Tests 2,916 passed / 19 environment skipped、Client 71/71、Web 775/775、Space Studio Playwright 13/13、Vue type-check、production build、OpenAPI/双 SDK drift、EF pending-model、GA 索引和 diff whitespace 均绿色。新增真 SQL 场景因本机无 `CP6_TEST_SQLSERVER` skipped，不计为接受证据。

## 2026-08-14 Space Studio WP0 核心 GA 证据索引

- 新增唯一核心 GA 索引、说明和校验器，固定 72% 基线、100% 派生规则、5 类外部输入、WP0–WP8 九个不可删除 Blocking Gate，以及产品/QA/WMS/架构/安全五个实名签字角色；代码完成、真实证据接受和签字不再混为同一状态。
- 校验器拒绝绝对/越界/不存在的仓库证据路径、缺失 Gate/Input/Signer、无实名/证据却标记 Complete/Accepted/Signed，以及与派生状态不一致的 `GaReady`。结构校验正常退出，`-RequireGaReady` 当前按设计以退出码 2 No-Go；自动化 2/2 通过。
- 当前索引诚实记录 5 项外部输入、9 个接受门禁和 5 个签字 Pending；未虚构 Owner、Provider、Site、黄金 CAD、Pilot 或签字，因此本卡只关闭 WP0 的索引与机器门禁，不声明核心 GA 完成。

## 2026-08-14 Space Studio WP5 生产 Viewer Published-only 边界

- 新增内部只读 `GET /api/space/design/v1/sites/{siteId}/published-scene`，服务端只接受模型当前 `CurrentPublishedVersionId` 指向的 Production/Published 版本，按有效楼层返回不可变 Design Revision，并固定 `runtimeOverlayIncluded=false`；无 Published 指针或读取期间权威漂移时失败关闭。
- 单层 Viewer、跨层 Viewer、Control Tower 和楼层列表全部切换为该聚合合同，不再消费可变旧 floor/scene API。Location 从 Published Rack/RackLevel/Location 的 LogicalId、逐层规格与尺寸确定性投影，继续支持拾取和库存着色；Draft/跨版本注入、不完整几何或跨层部分失败不会显示半仓。
- OpenAPI、C#/TypeScript SDK、`space:model:read` 权限和边界结构守卫同步更新；聚焦 Web 12/12、权限/OpenAPI 82/82、全量 Web 775/775、CP6.Tests 2,914 passed / 19 skipped、Space Unit 506/506、Vue type-check、production build 与 SDK drift 通过。真实 SQL Published/Draft 隔离用例已加入但本机 `CP6_TEST_SQLSERVER` 未配置而 skipped，不冒充生产或真库证据。
- 本卡关闭仓库实现的 Published-only Viewer 权威边界；生产等价部署、真实 Published 数据、独立 QA/UX/辅助技术验收与 Pilot 仍在 Todo，不能据此声明 WP5 或核心 GA 100%。

## 2026-08-14 Space Studio WP5 Viewer GA 性能复验

- 将硬件执行器从一次性截图升级为可审计正式门禁：1 次预热单独报告，30 次全新浏览器 Context 形成稳定分布；每次执行 100 次实际命中拾取、30 次 10,000 库位着色和 180 帧轨道渲染，并保存原始样本、P50/P95/最大值、失败率、提交 SHA、输入文件哈希、浏览器/OS/GPU/驱动和截图。
- 正式证据绑定干净提交 `bd206ff8`，环境为 Windows 11、Chrome 151、Intel Iris Xe 31.0.101.4502、ANGLE D3D11/WebGL2、1920×1080。30/30 冷运行成功、3,000/3,000 拾取命中、0 console errors、0 软件渲染；可交互 P95 62.3ms、帧 P95 8.2ms、拾取 P95 0.3ms、着色+渲染 P95 2.0ms、36 draw calls，全部 PASS。
- 聚合器对样本不足、SwiftShader、非 WebGL2、渲染器切换、console error、拾取 miss、数据规模漂移、性能超限和脏跟踪工作区失败关闭；证据算法 5/5、CPU 性能 1/1、Web 763/763、Vue type-check 和 production build 通过。报告见 `docs/space/reports/2026-08-14-space-viewer-v13-ga.md`。
- 本卡关闭当前仓库 SHA 的 Iris Xe/WebGL2/500 货架/10,000 库位性能门禁；生产 Published-only Viewer 核验、独立 UX/辅助技术验收、双仓 Pilot 和 GA 签字仍未完成。

## 2026-08-14 Space Studio WP6 外部主体安全矩阵

- Design V1 控制面新增统一授权阶段 fail-closed 过滤器，早于功能权限、模型绑定、上传体读取、Controller 和服务数据访问；外部主体即使被误授内部权限，也稳定返回 `SPACE_EXTERNAL_SUBJECT_DENIED`，并引导至 Published-only 门户。
- Customer、Supplier、3PL 均覆盖 Draft、Source、Upload、Lease、Validate、Publish Preview、Publish 和 AI；Published-only `SpaceExternalPortalController` 是唯一显式放行，反射守卫禁止新增隐式或 Action 级例外。
- 门禁通过：聚焦矩阵 30/30、权限/OpenAPI/主体边界聚焦 111/111、CP6.Tests 2,913 passed / 19 environment skipped、Space Integration 305 passed / 104 environment skipped、完整 Release solution 0 warning / 0 error。真实身份提供方 HTTP 负向、生产等价 SQL 跨租户、独立渗透测试和安全签字仍是 GA 门禁。

## 2026-08-14 Space Studio WP6 发布恢复可观测性基础

- 发布恢复聚合器以不可变 Publish Audit 的状态进入时间为主、Attempt 启动时间为旧记录回退，跨租户汇总 `WaitingRetry`、`ManualIntervention` 和 `ReconciliationRequired`，只输出固定状态标签，不暴露 Tenant、Site、Version 或 Attempt。
- `/metrics` 新增活动数量、最老等待时长、SLO 超时数量和固定目标秒数；Prometheus 规则覆盖自动恢复超过 15 分钟、人工恢复/对账超过 4 小时及指标缺失，运行手册冻结旧 Published 连续服务、幂等 Retry/Reconcile 和证据要求。
- 门禁通过：聚焦合同测试 6/6、CP6.Tests 2,883 passed / 19 environment skipped、Space Unit 506/506、Client 71/71、Space Integration 305 passed / 104 environment skipped、完整 Release solution 0 warning / 0 error。真实 SQL WMS 超时用例因未配置 `CP6_TEST_SQLSERVER` skipped，生产等价规则加载、通知路由、真实 WMS 演练和 15 分钟/4 小时结果仍是 GA 门禁。

## 2026-08-14 Space Studio WP6 发布 Warning 明确认领

- Publish Preview 现在返回 `validationWarningCount`，并在存在 Warning 时返回绑定 ValidationRun 与完整 Warning Issue ID 集的 SHA-256；顺序变化不改变哈希，Run 或集合变化会改变证据。
- 发布页把 Warning 认领和既有通用风险确认分开：用户必须逐项复核后显式勾选，Publish Attempt 才携带 `warningAcknowledgementHash`。服务端缺失哈希返回稳定 422，哈希或持久 Issue 摘要不一致返回 409，二次事务校验阻止预览后竞态。
- 历史版本重发若新 ValidationRun 产生 Warning，会失败关闭并保留生成的 Ready 版本，要求操作者打开正式发布预览确认；不会由后台任务或旧审批引用自动接受新风险。OpenAPI、C#/TypeScript SDK、错误码、Spec 与自动化已同步。
- 门禁通过：完整 Release solution 0 warning / 0 error；策略单测 5/5、OpenAPI 42/42、发布 UI 5/5；Space Unit 506、CP6.Tests 2,877 passed / 19 environment skipped、Client 71、Space Integration 305 passed / 104 environment skipped、Web 763、Vue type-check、production build 与 SDK drift。发布 SQL 用例因未配置 `CP6_TEST_SQLSERVER` skipped，本卡不代表 WP6 或核心 GA 完成。

## 2026-08-14 Space Studio WP4 底图与 Excel–CAD 工作台路径闭环

- 从远端 `main@8d66d773` 建立独立 `codex/space-studio-path-e2e`，为底图上传后的工作台增加明确标定入口；已挂接底图可重新进入同一标定流程，窄屏或失租只读时入口禁用，不新增第二套底图权威。
- Excel–CAD 权威匹配现在可由 `matchJobId` 深链直接打开问题域；用户可从匹配行定位当前 Draft Rack，并通过既有两阶段确认合同显式 Apply。匹配读取不会自动写 Draft，确认继续绑定 Artifact Hash 与 Expected Content Revision。
- Space Studio Playwright 分别覆盖图片上传→挂接→三点标定、Excel–CAD 审核→定位→确认，以及 DWG、DXF 各自经服务器 Preview 和双确认启动解析。Web 762/762、Playwright 13/13、Vue type-check、production build 与 `git diff --check` 通过。
- 本卡关闭的是仓库内 UI 与浏览器合同路径，不代表 WP4 或核心 GA 已完成；浏览器场景使用受控 API fixture，真实主备 Provider、授权 DWG/DXF、真实 Excel、发布到 CP6 WMS 与恢复证据仍在 Todo。

## 2026-08-14 Space Studio WP5 2D/3D 同源选择与逐楼层视角恢复

- 从远端 `main@548c4077` 建立独立 `codex/space-studio-3d-interaction`，为草稿参数化 3D 场景增加 raycast 拾取；Element/Zone/Aisle/Rack 直接返回同一 Design LogicalId，RackLevel 归一到所属 Rack，不引入第二套选择或模型权威。
- 3D 点击通过工作台既有 `selectObjects` 与 2D、问题定位和检查器同步；Ctrl/Command 支持切换选择，超过 4px 的 Orbit 拖动不会触发点选。3D 画布可 Tab 聚焦并提供操作说明，2D 选中继续驱动 3D 高亮。
- 2D pan/zoom、2D/3D 投影模式和 3D camera/target 使用带 schema 的 Version+Floor `sessionStorage` 状态；相同楼层刷新恢复，切层前 flush，损坏/越界状态失败关闭，新楼层无已存状态时重新 framing。
- 自动化覆盖拾取目标归一、相机状态校验、点击/拖动区分、组件事件、逐楼层 key、损坏状态拒绝及真实浏览器刷新恢复。Web 761/761、Space Studio Playwright 10/10、Vue type-check、production build 与 `git diff --check` 通过。
- 本卡只关闭 WP5 的工作台 2D/3D 交互与视角恢复，不表示 WP5 或核心 GA 完成；Iris Xe/WebGL2 500 货架/10,000 库位性能、独立 UX/辅助技术验收和 Published Viewer 真机证据仍在 Todo。

## 2026-08-14 Space Studio WP5 工作台键盘与可达性闭环

- 从远端 `main@1a30a601` 建立独立 `codex/space-studio-accessibility-ga`，补齐检查器 roving tab、方向键/Home/End、工具 `aria-pressed`、快捷键声明、状态播报、2D 画布焦点和工作台统一 `focus-visible`。`G` 按 Blocking → Warning → Info 循环定位下一个 Open 问题；窄屏只读定位只同步对象选择，不会把 3D 强制切回隐藏的 2D。
- CAD 审核、Excel–CAD 匹配、通用属性、WMS 采纳和 3D 预览的核心控件统一到 Space Studio token、16px 正文/问题说明、13–14px 元数据和 44px 主要点击/焦点热区；右侧固定宽度面板在 324px 检查器内收敛，不再继承白底/白字或溢出。
- 自动化新增真实浏览器键盘问题循环、选择同步、tab 焦点、焦点环、字号/热区和窄屏保持 3D 的证据。Web 754/754、Space Studio Playwright 9/9、Vue type-check、production build 与 `git diff --check` 通过。
- 本卡只完成 WP5 的仓库内工作台键盘/可达性能力，不代表完整 WP5 或核心 GA；Iris Xe/WebGL2 500 货架/10,000 库位性能、独立 4.5:1 对比度审计、真实输入设备/辅助技术验收仍在 Todo。

## 2026-08-14 Space Studio WP3 Site CAD Provider 认证与路由基础

- 从远端 `main@1c64e577` 建立独立 `codex/space-cad-provider-routing`，新增 Tenant/Site 级版本化 Provider 配置、Primary/Backup 认证明细、专用管理权限和只读 CAD 能力接口。配置保存部署模式、数据边界、审批证据引用、有效期、格式范围及 Secret 引用；Secret 内容不经查询契约返回，认证记录不可变，配置历史追加保留。
- `SpaceCadProviderRouter` 成为 Preparation/Parse 的统一入口，只把原始 CAD 交给当前 Site 已认证、未过期、格式与部署边界一致且当前可用的运行注册。Primary 只在可重试资源故障时切换到同一配置的 Backup；未认证 Provider、未批准云边界、同一配置之外的运行注册和从 Backup 反向回 Primary 均失败关闭。
- sealed Preparation 现记录实际 Provider Key/Version；Parse payload v3 绑定 Preparation 的 Provider Key 与 Semantic Preview Hash，审核产物继续校验完整来源、映射、坐标和语义链。Space Studio 向导显示 Site 配置 Revision、主备状态和阻断码，没有可用认证链时不轮询扫描，也不能生成 Preview。
- OpenAPI、C#/TypeScript SDK、稳定 Problem Details、权限矩阵、EF 迁移、幂等 SQL 脚本、领域/路由/契约/前端/E2E 自动化同步交付。完整 Release solution 0 warning / 0 error；.NET 3,753 passed / 123 environment-gated skipped，Web 754/754、Space Studio Playwright 8/8，Vue type-check、生产构建、SDK drift、EF pending-model 和 diff whitespace 通过。
- 本完成项是 WP3 的仓库路由基础，不是 Provider 认证完成或 CAD GA。默认运行注册为空并失败关闭；真实 ODA/APS（或经评分替代者）适配、受控 Worker、客户/安全/法务批准、同一 Site 两条实链和真 SQL 执行证据仍在 Todo。新增 `SpaceCadProviderSqlServerTests` 因本机未配置 `CP6_TEST_SQLSERVER` 而 skipped，未冒充真库通过。

## 2026-08-14 Space Studio WP2 CAD 起始向导

- 在独立 `codex/space-cad-start-wizard` 中交付扫描状态、服务器 Mapping Profile、CAD preparation preview 和原有 parse start 的完整向导链；没有增加第二个解析启动接口，也没有允许客户端构造 Profile、Transform 或 Preview Hash。
- 服务端通过受控 `ISpaceCadPreparationProvider` 读取隔离文件并生成确定性坐标、Inventory、Mapping 与 Semantic Preview；只为无 Blocking 的预览保存两小时 sealed Preparation，绑定 Source SHA、楼层、Base Content Revision/Hash 和全部确认 Hash。`StartSpaceCadParseRequest.preparationId` 必填，过期、篡改和 Draft 前进均失败关闭。
- Space Studio 上传后自动进入向导，扫描、楼层、单位、原点/旋转、Profile、语义对象与低置信/阻断摘要在同一流程展示；用户必须分别确认转换和映射才能启动，关闭或失败不改变当前 Draft。OpenAPI、C#/TypeScript SDK、权限矩阵、迁移与自动化同步更新。
- 仓库默认 Preparation Provider 仍是 fail-closed unavailable；真实 DWG/DXF 能力必须由下一张 WP3 Site 主备 Provider 认证卡接入，并由真实黄金 CAD/隔离 Worker 证据验收。本卡完成仓库内向导和 fence，不代表 CAD GA 或核心 GA。
- 自动化证据：完整 Release solution 0 warning / 0 error；.NET 全量 3,744 passed / 122 个既有环境用例 skipped，Space Unit 501/501，CAD 准备/解析聚焦 12/12，OpenAPI/权限/Controller 81/81；Web 752/752、Space Studio Playwright 8/8、Vue type-check、生产构建、SDK drift、EF pending-model 与 diff whitespace 全部通过。真实 SQL Server 未配置，因此没有把 skipped 场景计为通过。

## 2026-08-13 Space Studio WP1 设计态库位批量编码

- 从远端 `main@fdfb404e` 建立独立 `codex/space-layout-bulk-coding`，新增 Design V1 `location-codes:preview` / `location-codes:apply` 两阶段合同；规则仍由现有编码规则库按 Zone → Floor → Tenant 默认优先级选择，客户端不能提交任意规则或直接调用旧运行态编码服务。
- Preview 对当前 Draft 的 LocationRevision 生成完整差异与 Proposal Hash，确认前零写入；Apply 在同一 Floor applock 和 Serializable 事务中复算规则与 Proposal，并以租约、Floor/Content Revision、Proposal Hash、commandBatchId 关闭 stale 与重复写入。只允许修改 Active/Unbound/Generated 编码，WMS Bound、Adopted、Imported 和 Manual 均显式列为 protected；重建时通过两阶段置空支持安全换码，审计保留真实 before/after。
- Space Studio 批量检查器支持填空/重建、整层/单库区范围、规则与修改/保持/保护统计、逐项编码差异和显式确认 Apply；失败请求保留同一幂等包用于安全重试，Revision 变化后要求重新 Preview。OpenAPI、C#/TypeScript SDK、权限、稳定错误码、领域/SQL/API/组件/E2E 自动化同步交付。
- 当前证据：Space Unit 501/501、Space Web/API 聚焦 501 passed / 7 个既有 SQL 环境用例 skipped、Web 749/749、真实 SQL 编码闭环 1/1、OpenAPI/权限 73/73、Space Studio Playwright 7/7、完整 Release solution 0 warning / 0 error、Vue type-check、生产构建、SDK drift 和 diff whitespace 通过；本卡不表示核心 GA 完成，Provider、黄金 CAD、Viewer 真机、WMS 演练、双仓 Pilot 和五方签字仍为硬门槛。

## 2026-08-13 Space Studio WP1 布局修改与级联删除

- 从远端 `main@35147b85` 建立独立 `codex/space-layout-update-delete`，在既有 Design V1 Layout Command 中增加 Zone/Aisle/Rack 的完整定义修改与删除；写入继续受租约、Floor/Content Revision、幂等和原子事务保护，不调用旧运行态服务，也不直接改 Published/WMS。
- Rack 修改按确定性身份协调 RackLevel/Location：保留仍存在库位的 LogicalId、编码和 WMS 绑定，新增库位保持未编码，移除的层/库位进入设计态 `RemoveRequested`。存在子对象的 Zone/Aisle/Rack 默认拒绝删除并返回显式恢复动作，用户确认 `cascade=true` 后才删除整个设计态子树；空父对象可非级联删除。
- Space Studio 画布现可选择 Zone/Aisle，右侧属性域支持三类布局对象编辑；级联删除必须再次确认，键盘 Delete 也不会绕过确认。OpenAPI、C#/TypeScript SDK 和权限/错误恢复合同同步更新。
- 自动化证据为真实 SQL 聚焦回归 1/1、OpenAPI 38/38、Web 全量 744/744、Space Studio Playwright 6/6、Vue type-check、生产构建、SDK drift、完整 solution 0 warning / 0 error 和 `git diff --check` 通过。本卡不表示 WP1 或 GA 完成；批量编码 Preview → Apply 仍在 Todo。

## 2026-08-13 Space Studio WP1 工作台创建接入

- 从远端 `main@fbc1b4e5` 建立独立 `codex/space-layout-workbench-create`，将已合入的 Layout Command 接入 Space Studio 单一“构件”上下文；库区、巷道、货架均提供显式编码、几何和父级表单，货架另提供 1–50 层逐层规格、库位数预览和可选编码前缀。
- 所有创建继续使用 Design V1 租约、Floor/Content Revision、幂等命令包和本地失败恢复；成功后刷新同一 Design scene。Zone/Aisle 现由共享参数化渲染计划投影到 2D/3D，机器清单验证与 Rack/Element 同源；它们暂作为布局上下文，不误接旧通用 Element 的修改/删除命令。
- 自动化覆盖表单禁用/父级约束/逐层 payload、前端包络校验、确定性几何、上下文单开和 Zone/Aisle 2D/3D 一致性；Web 全量 740/740、Space Studio Playwright 6/6、Vue type-check、生产构建及 `git diff --check` 通过。设计态修改/删除和批量编码仍在 Todo，本卡不表示 WP1 或 GA 完成。

## 2026-08-13 Space Studio WP1 Layout Command 创建链

- M0 PR #4 已完成完整门禁、合入并推送远端 `main@9c320a74`；本任务从该最新基线建立独立 `codex/space-layout-command-v1` 分支。
- 任务分支已形成独立 `/layout-commands` 契约和原子领域写链：Zone → Aisle → Rack → 逐层 RackLevel → Location，不借用通用 `Space_Element`；服务端生成层/库位确定性 LogicalId，统一执行租约、Floor/Content Revision、幂等回放和命令审计。
- 分支门禁：完整 Release solution 0 warning / 0 error，Space Unit 497/497、真实 SQL Space Integration 397/397（0 skipped）、CP6.Tests 2868 passed / 19 个既有环境门禁 skipped、Web 729/729、Vue type-check 与生产构建通过，SDK/EF/diff drift clean。
- 任务提交 `77256dd9` 已通过 merge commit `289b51d0` 合入并推送远端 `main`；这只完成 WP1 创建链，不表示 WP1 或 GA 完成，工作台接入、修改/删除和批量编码仍在 Todo。

## 2026-08-12 Space Studio v1.3 核心切片

- 低成本 3D 建模详细 Spec 以 v1.2 完整正文为底稿增量合并 v1.3；原有领域、接口、恢复、权限与验收细节保留，新增冻结结论逐节落位并由 RFC-003 记录变更。
- 交付 Space Studio 冻结布局、2D/3D 本地草稿切换、首次任务清单、属性/批量/问题检查器、显式保存/租约状态、窄屏只读和恢复草稿导出。
- 交付强 Floor 编辑租约与命令批 `leaseId` 契约，包括 SQL 唯一槽/数据库时钟、续租、释放、浏览器会话 fence、过期复用、双权限强制接管和不可变审计；真实 SQL 自动化覆盖生命周期、过期重申请、续租/接管竞争、并发唯一槽和审计不可变。
- 交付 CAD 上传、Job 状态监控/取消/重试和 PreviewSet → typed 审核变更集自动加载；工件读取验证文件状态、SHA、Job/Source/Version/Floor 与冻结 BaseContentRevision，确认 Apply 受租约、Revision、ContentRevision、变更集哈希和幂等键共同保护，成功、精确重放和 stale 零写入均有集成测试。
- 交付空白画布/底图的墙、柱、门、月台和静态设备直接创建，以及 2D/3D 同源选中与视角保持；正式“校验并发布”入口只自动启动 Validation，不绕过 Preview、审批确认或发布权限。
- 自动化与构建门禁为 Space Unit 497/497、真实 SQL Space Integration 396/396（0 skipped）、CP6.Tests 2867 passed / 19 个既有环境门禁 skipped、Web 727/727、Space Studio Playwright 5/5、SDK/EF drift clean，以及完整 Release solution 0 warning / 0 error。
- 此完成项不包含真实 Provider 认证、黄金 CAD/Pilot/生产签字，不把开发完成记作核心 GA 完成。
## 2026-08-13 CRM V1 T1 对抗审阅收口

- 修订产品框架与可执行 Spec，关闭隔离提交状态/权限、原 ReceivedAt 双 SLA、同源 BFF 四重身份、attempt/replay/tombstone 和回执 Cookie 字节预算歧义。
- 固定 Azure SQL AZ/PITR 连续性与 Emergency Intake 实施合同，并把 previous System Manifest、机器兼容矩阵、系统整体默认回退和数据/Schema 前向边界纳入发布门禁。
- 明确 CRM 产品商城/订阅/客户产品中心不属于 V1；`CP6.CRM` 仓库只在 CRM01-S01 前置满足后创建。本完成项仍仅代表规范闭环，不代表任何业务实现或云资源完成。
- 本地工程/设计与合入前 fallback 复核修正 Dapr 调用图、IntakeDeptId/PII 权限、实际 migration ID 和首次切换回退边界后无剩余 Critical/High；正式 gstack 交互审阅因宿主接口缺失未签发。CRM Foundation 16/16、Markdown 相对链接和差异格式门禁通过。

## 2026-08-13 OpenAPI 原生客户端漂移门禁修复

- 确认 `main` 与 CRM PR #5 的 `client-contract` 以相同 expected/actual 指纹失败，根因属于主线门禁，不由 CRM 文档 diff 引入。
- 用 Node.js 稳定规范化替代 PowerShell JSON 序列化，并把指纹范围收敛为原生客户端路径及其递归可达 schema；无关模块新增 schema 不再造成假阳性，真实客户端合同变化仍失败关闭。
- 新增 4 个 Node 单测并在 Node 20/22 通过；真实 Swagger 指纹两次生成一致且 check 模式通过。完整门禁为 CP6.Tests 2859 passed / 19 environment-gated skipped、Client 71/71、Web 719/719、类型检查/生产构建和 R2 source gate 全绿。

## 2026-08-12 CRM V1 规范批准 T1

- 从 `main/origin/main@c68d9b53` 创建独立规范分支，按已批准的工程、QA 和采用/设计审阅证据修订 CRM 产品框架、可执行工程 Spec 与文档入口；旧根工作区未修改。
- 冻结 API 幂等/并发与稳定错误语义、Pilot C 分栏、公开站点 IA/视觉/受控 CMS/回执 Cookie、真实 ERP UAT、分层性能、GHCR/R2 唯一权威，以及 Observation/Pilot/Lead/Full Journey 不可豁免门禁。
- 本完成项只代表 T1 规划规范可用于拆票；业务代码、新仓、云资源、迁移、候选镜像和部署均未实施。下一项是 M0/R00 ADR 与 named Owner/cohort/Observation 输入。
- 门禁：最新 `main` 基线与三份审阅工件 SHA-256 复核一致；CRM Foundation 聚焦回归 16/16 passed、0 failed/skipped；Markdown 相对链接与 `git diff --check` 通过。

## 2026-08-11 Azure DEV 自动部署仓库配置

- 新增 `azure-pipelines-dev.yml`：以 `GTX537.CP6` 的成功 `main` Run 为唯一 completion resource，绑定 `CP6-Deploy/LAPTOP-3QQ44FJS`，核对服务身份、分支和完整 Git SHA 后构建一次本机 API/Web 镜像。
- `DeployDev` deployment job 绑定 `cp6-dev`，只在部署任务内映射 `cp6-dev-secrets` 的四个锁定 Secret；先运行 db-init，再启动 API/Web，最后校验 live/ready/release identity 并发布不含 Secret 的 `cp6-dev-evidence`。
- Lab 脚本新增 Azure 进程环境 Secret 与参数化 ReleaseVersion/GitSha，同时保留原 DPAPI 人工模式；Azure RabbitMQ 使用独立 volume，避免已有人工 Lab 密码状态冲突。PowerShell/YAML 解析、Lab/DEV CD 合同和 Secret/SHA 失败关闭检查已通过；既有 DEV/UAT/PROD-LAB 仍为 live/ready Healthy 且 API/Web 身份一致。
- 本里程碑只表示仓库能力配置完成。外部 `CP6 DEV CD` 创建、三个资源的最小 Pipeline 授权和首次实际 Run 尚未完成；该本机学习链不构成 Registry、UAT/PROD-LAB 或生产发布能力。

## 2026-08-11 CRM 产品框架与可执行 Spec

- 在 `main == origin/main == f149c75e` 的独立任务 worktree 完成 Foundation 事实核对；CRM 聚焦测试 16/16 通过，并明确当前实现仅含模型、状态机、迁移和菜单权限种子，不把它误记为 API、Next.js 或端到端能力。
- 交付 `docs/crm/CRM-PRODUCT-FRAMEWORK.md` 与 `docs/crm/CRM-V1-EXECUTABLE-SPEC.md`，完整定义产品定位、角色/渠道/旅程、V1/VNext、三仓微服务架构、领域与状态机、数据/API/事件、权限/PII/租户、前端、ERP、迁移、SLO、安全、测试、发布和 DoD。
- 把实现工作拆为 Platform P01–P10、CP6 C01–C04、CRM01–CRM12，并给出依赖、门禁、切换预算和硬停止条件。该完成项仅代表规划和审阅材料交付；新仓库、服务、前端、迁移、云资源和生产发布仍未实施。

## 2026-08-11 Azure 专用部署 Agent 基础

- Azure DevOps `CP6-Deploy` Pool 已创建，专用 Agent `LAPTOP-3QQ44FJS` 以 `cp6_deploy_agent` 非管理员 Windows 服务身份 Online/Idle；未覆盖或迁移 `Default` Pool 的通用 CI Agent。
- 新增无 Secret、手工触发的部署 Agent readiness YAML、静态合同测试和操作文档，验证身份、Docker Desktop Linux engine、Compose 与 SQL TCP 可达性。
- Azure readiness Pipeline 已从 `main` 创建；Build ID `10` / Run `20260811.1` 的完整 Job 与验证 Step 成功，截图和本机 Worker 日志已交叉确认。

## 2026-08-11 本机 DEV/UAT/PROD-LAB Docker 环境

- 建立三个独立 Compose project 和端口/网络/volume/消息资源边界；共用同一组 `cp6-api:lab-local`、`cp6-web:lab-local` 镜像，环境间不重新构建。
- `db-init` 与 API 分别接收 migrator/runtime SQL 身份；SQL 和应用 Secret 从 DPAPI vault 临时渲染，命令结束后删除明文文件。三套数据库都完成 Core/Space 迁移。
- 三套环境各 5 个容器全部健康，API live/ready 均为 Healthy，API/Web 统一报告 `0.0.0-lab` 和相同 Git SHA；合同测试覆盖三环境配置、Secret 分离、端口隔离和 Docker 构建上下文。
- 修复 API Docker restore 依赖图和 Web 仓库级 SDK 构建上下文，使本机 Lab 与现有 R2 candidate 使用同一可构建 Dockerfile。
- 2026-08-11 外部截图确认 Azure DevOps 已创建 `cp6-dev`、`cp6-uat`、`cp6-prod-lab`，当前均为 `Never deployed`；逻辑名称创建已完成，权限、审批与实际部署不计入本里程碑完成项。

## 2026-08-11 Azure DevOps CI/CD 项目规划固化

- 新建 `docs/devops/README.md`、架构、Azure 演进计划、发布流程和环境策略五份文档，并从 `AGENTS.md` 与根 README 建立入口。
- 明确 Azure 当前只完成基于 `Default` self-hosted pool 的 CI；Docker Release、Registry、DEV/UAT/PROD 仍为待办，避免把构建测试成功描述为生产上线。
- 把现有 GitHub R2 的 protected tag、GHCR、SBOM/扫描、签名、证据和 digest 部署定义为迁移期间的生产权威；ACR 只作为待决策候选。
- 固化 Build once、digest 推广、CI/部署身份分离、资源侧审批、前向迁移和生产资产边界。本项完成的是项目记忆与路线图，不代表 Azure CD 或生产部署完成。

## 2026-08-10 CRM V1 Foundation

- 冻结并记录 CRM V1 范围、漏斗、赢单门槛、租户公开路由、官网发布、反垃圾、归因和 24 个月 PII 匿名化边界。
- 新增 20 张 CRM/CMS 实体表、固定线索/商机状态机、聚合外键、租户过滤与唯一索引、PII 擦除元数据，以及 `CrmFoundation` EF 迁移。
- 新增 CRM 菜单/动作种子：6 个菜单节点、22 个动作、租户管理员幂等授权；页面未交付前保持禁用，并保留后续显式启用状态。
- 新增状态转换、Accepted/Won 守卫、租户隔离、公开路由共享边界和种子幂等测试。此项只标记 Foundation 完成，业务 API、Vue 页面、公开官网与运营任务仍按 CRM 待办推进。

## 2026-08-10 Space 单格货位码 Zone 级 rackSeq

- `CodeEngineService.GenSingleAsync` 不再把当前货架序号硬编码为 `1`，而是加载目标 Zone 的全部货架，与批量 `GenerateAsync` 复用同一套 `(X, Y, Id)` 确定性排序。
- 相同几何坐标以货架 `Id` 作稳定兜底；非首架单格生成现在与整层批量重建得到相同编码，避免与 Zone 首架重复或漂移。
- 未改变规则模型、API、数据库或前端。CodeEngine/LocationPublish 聚焦回归 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，任务 diff 与新增行格式检查通过。

## 2026-08-10 FIN BudgetLine 版本级并发控制

- 预算行新增、编辑、删除与 Excel 确认导入统一使用 `BudgetVersion.RowVersion` 作为聚合令牌；API 对缺失或非法版本令牌失败关闭，陈旧令牌返回 `E-A5-CONCURRENCY-001`。
- 单行 upsert 的行头与 12 期明细已纳入同一事务；Excel 批量确认也为单一事务，检查每次内部 upsert 结果，避免部分持久化。前端在成功或冲突后同时刷新版本与预算行令牌。
- SQL Server 真库测试用两个独立 `DbContext` 验证：修改不同预算行时旧版本令牌仍被拒绝，刷新后可重试；旧版本令牌的删除整体回滚。门禁为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped、`KOUSQLSERVER` 1/1、前端 3/3、Vue type-check、WebApi Release 0 warning / 0 error。

## 2026-08-10 PLAN/PUB Attachment 宿主业务权限补强

- Attachment 不新增暗菜单键；`Attachment:EnforceBizPermission` 缺省为 true，list/upload/download/preview/delete/rebind 均按请求或持久化 `BizType` 回查宿主菜单，拒绝时不读物理流、不执行删除或转正。
- rebind 读取 draft token 下全部附件，要求当前用户为上传人并拥有所有宿主菜单；显式 false 仅保留受控兼容。`IAttachmentService` 增加只读元数据和草稿查询，避免鉴权前打开文件。
- `PubUpload.writePermission` 接收宿主 action key；无写权限时隐藏上传/删除，保留下载/预览。门禁为后端聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 skipped / 0 failed、前端聚焦 3/3、全量 716/716、Vue type-check 与 production build 通过、WebApi Release 0 warning / 0 error。

## 2026-08-09 WF 通知定向推送与遗留广播清理

- 确认生产注册链路早已由 `PersistentWfNotifier` 写 outbox、`WfNotificationDispatchWorker` 提交后派送；worker 通过 `Clients.User(row.UserId.ToString())` 只触达通知接收人，`NotifyHub` 要求认证。
- 删除未注册的旧 `SignalRWfNotifier`，并从 `PersistentWfNotifier` 移除 outbox 入队后永远不可达的 SignalR 广播、邮件直发和重复持久化回退；通知器依赖收敛为 `INotificationService` 与 `IPrefService`。
- 项目记忆不再把该项误报为待办或已知隐私问题。通知聚焦测试 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。

## 2026-08-09 分支优先规范与本地配置优先级修复

- `04eaf42d` / `e4e33364` 新增并合入仓库级分支规则：日常开发必须从最新 `main` 创建单任务分支，脏工作区使用独立 worktree，测试通过后才能合并并推送远端。
- `e3bf2420` 把 Local JSON 配置源排序提取为 `LocalJsonConfiguration`，准确插在无前缀环境变量源之前，保留主机前缀源顺序以及环境变量/命令行的高优先级。
- 新增四条测试覆盖插入位置、环境变量覆盖、后置命令行覆盖和缺少无前缀环境源的回退；同时忽略机器本地 `.claude/settings.local.json`，不提交个人 SQL Server 启动配置。
- 门禁：配置 4/4、OpenAPI 30/30、CP6.Tests 2832 passed / 18 skipped / 0 failed、WebApi Release 0 warning / 0 error、whitespace 与 diff 检查通过。

## 2026-08-09 E13 无锁 Zone 父关系确定性推导

- `d19a5300` 基于 `main@6bbdd760` 新增 `warehouse-rule-only-v2`；Aisle/Rack 无人工父关系锁时，只有恰好一个确定性 Zone Polygon 完整包含子几何才生成 `relations.zoneSourceKey`，并记录 `DeterministicRule` 与 `RULE:ZONE_GEOMETRY_CONTAINMENT_V1`。
- 零候选或多候选产生 Blocking `SPACE_RULE_ONLY_PARENT_REQUIRED`，不接受 AI 猜测绕过；凹多边形按完整线段验证。人工锁优先，AI 冲突保留问题，融合字段与 AI Relation 一并进入父关系环检测，BuildScene 不重复持久化同一缺失父关系。
- v1 冻结 Run/恢复链保持原行为；不同 SourceHash 的几何匹配与人工确认仍未实现。无 Migration、HTTP/OpenAPI/SDK、前端、Provider、网络、Usage、High Accept 或 Draft 自动写入。
- 验证为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 skipped、完整 Release/AOT 0 warning / 0 error。完整证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## 2026-08-09 `main` 同步与 P2.5 受控整合

- PR #2 以 `8045d872` 把 `codex/main-sync-20260808` 受保护合入 `main`；原集成 tip `f8c3bae8`、2 个 OA 冲突、3 个项目记忆冲突和 Docker HTTP Cookie 修复均按权威边界收敛。
- OA 保留 `formApi.submit`；客户端心跳时序断言与周期语义解耦，定向重复 50/50、完整客户端 71/71。`CP6Context` 14 个和 `SpaceContext` 36 个幂等迁移包及 preflight/postflight 已在 LocalDB 从 main 基线连续双执行通过。
- P2.5 Analytics Control Tower 没有整段合并历史分支；`030a97b9` 在当前 E10 Runtime/Viewer 真相源上选择性整合控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器。
- 历史迁移 `20260720035903` 被替换为基于当前 ModelSnapshot 的 `20260809092206_SpaceAnalyticsControlTowerCurrent`；`b2a91680` 补齐 Space 权限、菜单种子和配置文档对齐。
- 完整门禁为 Release/AOT 0 warning / 0 error、前端 711、CP6.Tests 2816、Space Unit 487、默认 Integration 288、EF drift clean；P2.5 另通过 WebApi build、前端 type-check/生产 build、全量 Vitest、Space UnitTests 和 CP6.Tests。完整合并前证据见 `docs/space/reports/2026-08-08-main-merge-readiness.md`。
- `e4e33364` 已进入远端 `main` 并强制分支优先开发流程；这不等于 R2 标签、生产数据库建立或生产部署获批。

## 2026-08-08 E13 RackGenerationProfile 权威版本链

- `19d32650` 交付独立的 System/Tenant 方案头、不可变版本、规范化层定义与 SHA-256、Tenant-only 幂等创建、列表/精确读取、复合外键和真实 SQL 幂等迁移。
- Generation Run 只冻结已验证的 Active/Ready 精确版本；RuleOnly Worker 以 `ExplicitSelected` 消费并派生 RackLevel/Location。Web 显式可选、绝不自动选择；空选择继续 Blocking。
- 三条 API 的权限、读写审计、统一 Problem Details、OpenAPI 118 operations 与 C#/TypeScript SDK 已同步。真实 SQL 1/1、前端 711/711、Space Unit 487/487、Integration 288 passed / 95 skipped、CP6.Tests 2816 passed / 17 skipped、完整 Release/AOT 0 warning / 0 error。
- 无外部 Provider、网络、Secret、Usage、High Accept 或 Draft 自动写入。完整证据见 `docs/space/reports/e13-rack-generation-profile-authority.md`。
- `19d32650` / `6f12a19e` / `70dd670d` 完成功能、报告与 no-ff 远端集成；合并态复验 9/9、63/63 与 SDK drift。删除已合并的本地/远端临时分支，清理 38 个可再生成目标、29,418 个文件并释放约 1.85 GiB；`main` 未修改。

## 2026-08-08 E13 Generation Run 建模 Web 入口

- `52bb3a29` / `282d4e54` / `2871df1b` 完成功能、报告与 no-ff 受控集成；编辑器接入统一 `CreateGenerationRun`，从已确认 DWG/DXF Preview 启动 RuleOnly Run，显示排队/进度后进入审核与原子 Apply；Failed/Stale 恢复也改用同一并发、幂等和 BasedOn 合同。
- Run DTO 补齐冻结 Source/Mapping/Rack Profile 标识；Web 使用当前 Draft `If-Match` 和 ContentRevision，409/422 后重读权威状态，中间态不提前请求 Review/Proposal。
- 前端聚焦 11/11、全量 710/710、type-check/build、OpenAPI/审计 31/31、Space Unit 484/484、Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK drift/strict 与完整 Release/AOT 0 warning / 0 error。
- 无 Migration、Provider、Secret、网络、Usage、High Accept 或 Draft 自动写入；完整证据见 `docs/space/reports/e13-generation-run-web-entry.md`。
- 合并态复验前端 11/11、OpenAPI 29/29 与 SDK drift；清理 38 个可重建目标、29,416 个文件，释放 1,982,552,577 bytes（约 1.85 GiB）。远端祖先链确认后已删除本地/远端临时分支，`main` 未修改。

## 2026-08-08 E13 首次 Generation Run 创建入口

- `770bdc96` / `bbcaf6fe` / `9d0971f4` 完成功能、报告与 no-ff 受控集成；统一 `CreateGenerationRun` 让首次 RuleOnly 与 BasedOn recovery 共享版本并发、权限、审计和公开幂等域，同键冲突、业务复用和 replacement Run 均失败关闭。
- Version、DWG/DXF Clean Source、SourceHash、坐标、活动 Floor、Mapping 与成功 PreviewSet 重新校验；Job 固定 Preview Artifact ID/SHA，恢复继承固定点。未经权威存储验证的 RackProfile 不进入 Run。
- OpenAPI、C# 与 TypeScript SDK 已同步；聚焦 9/9、合同 31/31、Space Unit 484/484、Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK strict/drift 与完整 Release/AOT 通过；最终构建 0 error / 7 条未改动测试文件既有 warning，C# SDK 0 warning / 0 error。
- AiAssisted 与外部 Provider 继续失败关闭；无 Migration、Provider、Secret、网络、Usage 或 Draft 自动写入。完整证据见 `docs/space/reports/e13-generation-run-create-production.md`。
- 合并后聚焦 9/9、OpenAPI/审计 31/31 复验通过；清理 36 个可重建目录、8,622 个文件，释放 1,666,117,627 bytes（约 1.55 GiB），`main` 未修改。

## 2026-08-08 E13 纯规则 BuildScene 生产执行链接线

- `36cc0241` / `89c6fb2a` / `9e7f7e0a` 完成功能、报告与 no-ff 受控集成；生产默认 BuildScene executor 可让 RuleOnly recovery 从权威 PreviewSet 走完 12 步并到达 AwaitingReview。
- local-only 稳定特征快照、同 SourceHash 已确认 locked facts 重映射、Serializable Proposal/Issue 幂等持久化、Blocking 问题和零 Provider/Usage/Draft 写入均已落地；Provider-backed 模式继续失败关闭。
- 验证为规则/融合 21/21、BuildScene 2/2、Space Unit 484/484、默认 Integration 277 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 Release 0 warning / 0 error；合并后重点复验 24/24。
- 首次 Generation Run 创建服务/API、不同 SourceHash 人工确认继承、权威 RackGenerationProfile、确定性父关系、外部 Provider 与正式 CAD/黄金集证据仍属待办。
- 清理当前隔离工作区 36 个可重建 `bin/obj` 目录和 6,108 个文件，释放 1,209,344,722 bytes（约 1.13 GiB）；`main` 未修改。

## 2026-08-08 E13-S14 离线评估工程切片

- `e69b3bca` / `9261d59a` / `292a26ed` 完成功能、报告和 no-ff 集成；稳定 SourceKey 一对一匹配、覆盖率/语义准确率/人工下降率、高置信度 Precision 与 Wilson 下界，Calibration-only 选阈值且 Validation/Holdout 不参与调参。
- 20 资产、L1～L5、10/5/5、唯一 hash、授权/脱敏、版本/标注/验收/不可变/完整性审计均进入失败关闭门禁；合成 DevelopmentSeed 永远不能发布。
- 新增规范报告哈希、现有 Draft Proposal 适配和 `evaluate-ai-offline` 命令。核心 11/11、命令 1/1、工具 26/26、Space Unit 482/482、Integration 275 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 Release 0 warning / 0 error。
- 这里完成的是可复用工程能力；E13-S14 正式黄金数据签收、S15/S18/S19 外部运行与审批证据仍属待办。
- 远端集成祖先链核验后删除本地/远端临时分支；41 个可重建 `bin/obj` 目录和 6,982 个文件已清理，释放约 1.29 GiB，`main` 未修改。

## 2026-08-06 Version Clone 必填字段前向修复

- 以 `0564afad` / `01eba1b7` 完成功能和 no-ff 受控集成；Zone/Aisle/Rack 的 `Name` 与 Rack `RackType` 现在随 Published → Draft 快照保真复制。
- 缺陷已在 `ac9c977c` 独立基线复现：非空 `Name` 遗漏先阻断三类父记录插入，再连锁触发 RackLevel/Location 外键失败；修复只补齐 SQL 映射，无 Migration。
- 回归测试先失败后通过；Version Clone 7/7、Space Unit 430/430、真实 SQL Space Integration 336/336 且 0 skipped，Unit/Integration Release build 均 0 warning / 0 error。
- 远端集成祖先链已验证，临时功能分支已在本地/远端删除；16 个可重建 `bin/obj` 目录已清理并回收 513,840,161 bytes（约 0.479 GiB），`main` 未修改。完整证据见 `docs/space/reports/version-clone-required-fields-forward-fix.md`。

## 2026-08-06 E13-S17 迁移、前向修复与保留清理

- 以 `12db5531` / `e7720df4` 完成功能与 no-ff 受控集成；交付 Tenant 级可恢复保留 Job、90 天生成载荷净化、365 天 Usage 逻辑归档、Run 保留锁和 SQL 同租户并发租约。
- Published/Superseded/Publishing/Reconciliation、Decision、Locked Fact、CommandBatch、预算与审计不删除；Staging 只清空临时大 JSON 并软删除，重复执行零副作用。
- Migration 只增加 5 列和 4 索引，幂等 SQL 双执行通过；`Down` 失败关闭并要求更高版本 forward-fix，禁止破坏性回滚审计数据。
- 门禁为本卡 unit 6/6、内存/迁移 4/4、KOUSQLSERVER 3/3、Space Unit 430/430、默认 Integration 255 passed / 81 SQL-gated skipped、Release 0 error、EF 无漂移。E13-S14/S15/S19 与依赖 S15 的 S18 继续等待正式外部证据。
- 远端集成祖先链已验证，临时功能分支已删除；16 个可重建 `bin/obj` 目录已清理并回收约 0.488 GiB，`main` 未修改。

## 2026-08-06 E13-S13 外部用户拒绝与数据外发门禁

- 以 `37bf5c37` / `e1682efc` 完成功能和 no-ff 受控集成；Gateway 在读取策略、申请配额和调用 Provider 前再次拒绝外部主体，真实 External Provider 发送前还必须通过精确字段白名单与最小化 Token 语法门禁。
- 4 个 AI 控制器的 16 个端点均具备显式审计元数据，7 个 GET 均启用读审计；Customer、Supplier、3PL × 16 操作形成 48 条稳定 403 拒绝断言，并验证无数据访问和持久化副作用。
- 门禁为 Space Unit 424/424、Provider/最小化 34/34、外部/管理 8/8、审计/OpenAPI/权限 87/87、非 SQL 10/10、KOUSQLSERVER 21/21；Application Debug/Release 0 warning/0 error，WebApi Release 仅保留 3 条既存 Core nullable warning。
- CSO 范围审计未确认当前可利用漏洞；生产 BuildScene、真实 External Provider、网络端到端、正式数据/供应商/影子运行证据仍失败关闭或待办。验证后清理 28 个可重建目录并回收约 0.989 GiB；`main` 未修改，下一张建议卡为 E13-S17。

## 2026-08-06 E13-S11 Generation Run 恢复产品化

- 以 `dcbbfca8` / `c695850f` / `d3c2da75` 完成功能、证据和 no-ff 受控集成；交付取消、同输入重试、废弃、CommandBatch 对账、Failed/Stale 新 Run 与 RuleOnly 降级，并同步 Design V1 OpenAPI、C#/TypeScript SDK 和前端操作面。
- 运行中取消由 Worker 安全点确认，不拆分原子 Apply；安全重试沿用同一 Job/Run/输入/检查点/ApplyPlan 且只接受 Transient/Resource/Bug。未知 Apply 结果只以当前 revision 和冻结计划匹配的已提交 CommandBatch 为权威。
- replacement Run 保留 lineage 与冻结输入，旧 Decision/审计不删除；Failed current 源先退役后插入 replacement，同键并发只执行一次。规则降级仅对 BuildScene Provider 故障提示，普通 Apply 资源失败不会误导为关闭 Provider 重建。
- 门禁为状态机/分类 42/42、OpenAPI/权限 52/52、AI Apply/Recovery 真实 SQL 14/14、前端 129 files/695 tests、聚焦 6/6、type-check、production build、SDK 生成与 WebApi build 全绿。
- 功能分支已推送远端备份并进入集成祖先链；34 个可重建目录已清理，回收约 0.939 GiB，`main` 未修改。真实 BuildScene/Provider/CAD/黄金集与发布证据仍是独立缺口。

## 2026-08-06 E13-S10 Staging 与原子 AI Apply

- 以 `43dc5534` / `fbc59fb3` / `5be724cf` 完成功能、既有审核基线更新纠偏和证据记录，并以 no-ff 提交 `0c587d4c` 进入受控 Space 集成分支；新增 Staging、ApplyPlanHash、ApplyGeneration Worker、Design V1 Apply/Run API、OpenAPI/SDK 与前端轮询刷新闭环。
- Apply 同时支持 Added 与同逻辑身份的 Modified/Unchanged：Zone/Aisle/Rack/Element 原位更新，RackLevel/Location 确定性协调；跨类型/跨楼层/资产 Element 冲突和 WMS 绑定库位移除均失败关闭。成功只推进一次 Floor Revision 与 ContentRevision，Published/WMS/设备状态不变。
- Serializable Queue、租户 + Run `sp_getapplock`、固定锁序、双重 revision/review/唯一/引用/边界/碰撞校验、不可变 Staging/ApplyPlan、CommandBatch before/after 审计和故障回滚已落实；同键并发只创建一个 Job。
- 门禁为 E13-S10 真实 SQL 7/7、Space Unit 413/413、默认 Space Integration 248 passed / 71 SQL-gated skipped、CP6.Client 71/71、CP6.Tests 2783 passed / 17 environment-gated skipped、前端 129 files / 694 tests；完整 solution、EF、幂等 SQL、OpenAPI/C#/TypeScript SDK、type-check、production build 与 diff check 均通过。
- 功能分支已推送远端备份；验证依赖清理回收约 0.31 GB，`main` 未修改。下一张独立卡为 E13-S11 取消、重试、降级和 Stale 恢复产品化。

## 2026-08-05 E13-S09 决策与人工锁定修正

- 以 `c87289f2` / `382d5722` / `396ee38b` 完成追加式 Accept/Reject/Modify 决策、并发与幂等控制、问题解决血缘、同源人工锁定继承、Migration、Design V1 API/OpenAPI/SDK 和实时审核面板，并 no-ff 进入远端受控集成。
- 批量上限 1,000 且 High 自动批量 Accept 默认关闭；Modify 只接受 RFC 6902 `replace` 精确白名单和 1～32 个唯一锁定字段。所有写入服务端重验租户、Site、Run、Draft revision、Proposal rowversion 与 ReviewEtag；不写 Draft、Published、WMS 或设备控制数据。
- 全量门禁为 Space Unit 413/413、Space Integration + KOUSQLSERVER 312/312、CP6.Tests 2779 passed / 17 environment-gated skipped、CAD 25/25、前端 128 files / 692 tests、完整 solution Release 0 error / 10 条既有 warning；EF、Migration/幂等 SQL、OpenAPI/C#/TypeScript SDK、type-check 与 production build 均通过。
- 功能历史已备份并进入远端集成祖先链；本地/远端临时功能分支已删除，清理 38 个可重建目录并回收约 1.42 GiB，`main` 未修改。下一张独立实施卡为 E13-S10 Staging + 原子 Apply。

## 2026-08-02 E12-S05 标准 GLB 交换格式导出

- 以 `dd505f6f` / `c4b139ab` 完成 glTF 2.0 单文件 GLB 导出与 no-ff 受控集成；仅 Ready/Succeeded/Production Isolated 的内部规划分支可下载，生产指针和运行态保持只读隔离。
- 导出覆盖楼层、区域、巷道、货架、货架层、库位和通用元素；共享带面法线盒体网格，CP6 毫米 Z-up 坐标确定性转换为 glTF 米制 Y-up，语义进入 `extras.cp6`。总数据节点上限 50,000，Serializable 快照、稳定排序、SHA-256 与失败关闭边界已落实。
- 新增 1 个 API、1 个权限和四个五语页面词条，OpenAPI 增至 84 operations；C#/TypeScript SDK、Blob 下载入口、完整性响应头和 no-store/nosniff 护栏已同步。该能力不是 DWG 回写、CAD authoring、运行态快照或生产发布。
- 全量门禁为 Space Unit 272、默认 Space Integration 247 passed / 63 SQL-gated skipped、CP6.Tests 2777 passed / 17 environment-gated skipped、前端 123 files / 676 tests、非增量 Release 0 error / 10 条既有 warning；双 EF、SDK、TypeScript 与生产构建通过。
- 合并态 GLB 2/2、权限/API/OpenAPI 65/65、同树前端 9/9 及双 EF/SDK 通过。功能工作树及本地/远端临时分支已删除，释放约 2.672 GiB；`main` 未修改。E12-S06 继续等待正式样本、DWG SDK/供应商授权和可审计试验环境。

## 2026-08-02 E12-S04 多场景比较与决策记录

- 以 `7b919b4b` / `a9298bad` / `577168e3` 完成同源不可变仿真证据比较、交付文档与 no-ff 受控集成；显式基线/阈值、基线差值、风险标记和追加式人工决策均已进入远端集成历史。
- 强制 2～10 个不同生产隔离分支共享站点、模型、基础 Published 版本、历史样本和仿真口径；容量假设差异显式呈现，不计算总分、不排名、不推荐且不允许生产回写。
- 新增 6 个 API、4 个权限、四张租户隔离证据表，OpenAPI 增至 83 operations；规划页提供跨分支证据矩阵和 Selected/Deferred/RejectedAll 决策链。47 个新增词条具备五语运行时种子，既有 i18n 欠账仍为 908。
- 全量门禁为 Space Unit 272、默认 Space Integration 245 passed / 63 SQL-gated skipped、CP6.Tests 2775 passed / 17 environment-gated skipped、前端 123 files / 674 tests、非增量 Release 0 error / 10 条既有 warning；双 EF、SDK、TypeScript 与生产构建通过。
- 合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 66/66、前端 10/10、类型检查、双 EF、SDK 与 TypeScript 门禁通过。功能工作树、临时依赖链接及本地/远端功能分支已删除，释放约 2.348 GiB；`main` 未修改。下一张独立实施卡为 E12-S05 标准交换格式导出。

## 2026-08-02 E12-S03 距离、拥堵、容量、吞吐和成本仿真

- 以 `ab21aed4` 完成确定性仿真引擎、不可变证据、迁移、API/UI/权限/SDK，文档 tip `2cd1faed` 先行推送远端备份；no-ff 受控集成提交为 `f2d68897`。
- 距离、拥堵、容量、吞吐、人工和成本均有显式规划口径与未知覆盖；只读生产隔离场景和脱敏历史数据集，不读取实时运行态、不排名方案且不允许生产回写。
- 新增 3 个 API、2 个权限、两张租户隔离证据表，OpenAPI 增至 77 operations；规划页提供容量/时间桶/币种/单价配置、五类 KPI、热点和哈希证据。41 个新增词条具备五语运行时种子，既有 i18n 欠账仍为 908。
- 全量门禁为 Space Unit 268、默认 Space Integration 242 passed / 63 SQL-gated skipped、CP6.Tests 2771 passed / 17 environment-gated skipped、前端 122 files / 670 tests、非增量 Release 0 error / 3 条既有 warning；双 EF、SDK、TypeScript 与生产构建通过。
- 合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 65/65、前端 7/7 及 EF/SDK/TypeScript 门禁通过。功能工作树及本地/远端临时分支已删除，释放约 2.03 GiB；`main` 未修改。下一张独立实施卡为 E12-S04 多场景比较与决策记录。

## 2026-08-02 E12-S02 脱敏历史任务数据集与回放时钟

- 以 `4fb6941d` / `d89919b8` / `c8ccbf56` 完成数据/时钟/迁移、API/UI/权限/SDK 与 no-ff 受控集成；最多 10,000 条历史任务可固定到克隆成功且生产隔离的场景。
- 合同只接受不可逆 SHA-256 task/worker token 和显式脱敏确认，位置必须存在于场景快照；数据集、任务与回放证据不可变且不允许生产回写。
- 新增 3 个 API、2 个权限、两张租户隔离表、Migration/幂等 SQL，OpenAPI 增至 74 operations；规划页提供 Ready 场景导入、列表和确定性回放证据。
- 全量门禁为 Space Unit 264、默认 Space Integration 239 passed / 63 SQL-gated skipped、CP6.Tests 2767 passed / 17 environment-gated skipped、前端 121 files / 667 tests、非增量 Release 0 error / 10 条既有 warning；双 EF、SDK、TypeScript、生产构建和合并态复验通过。28 个新增页面词条具备五语运行时种子，既有 i18n 欠账仍为 908。
- 功能历史已备份并进入远端受控集成；功能工作树及本地/远端临时分支已删除，释放约 2.03 GiB，`main` 未修改。下一张独立实施卡为 E12-S03 距离、拥堵、容量、吞吐和成本仿真。

## 2026-08-02 E12-S01 生产隔离规划分支

- 以 `c673b7ec` / `8d75e79e` / `0ac603d4` / `3d41c8d9` 完成隔离模型、功能、no-ff 集成与五语收口；内部规划人员可从当前生产 Published 快照创建多个不可变血缘的异步克隆场景。
- `PlanningScenario` 版本不占生产 Draft/Published 指针，领域与数据库均拒绝其进入生产发布生命周期；生产后续发布不会改变已固定的场景基础快照。
- 新增 3 个 planning API、2 个权限、动态菜单、场景工作区、Migration/幂等 SQL、OpenAPI 71 operations 与同步的 C#/TypeScript SDK。
- 全量门禁为 Space Unit 261、默认 Space Integration 235 passed / 63 SQL-gated skipped、CP6.Tests 2763 passed / 17 environment-gated skipped、前端 120 files / 664 tests、完整 solution Release 0 error / 10 条既有 warning；EF/SDK/TypeScript/生产构建通过。20 个页面词条具备五语运行时种子，既有 i18n 静态欠账仍为 908。
- 功能历史进入远端受控集成后已删除功能工作树及本地/远端临时分支，释放约 2.68 GiB；`main` 未修改。下一张独立实施卡为 E12-S02 脱敏历史任务数据集和回放时钟。

## 产品能力

- ERP 销售主线、MES 制造执行、WMS 仓储物流及 ERP→MES→WMS 闭环已成型。
- FIN、PUR、OA/WF、PUB、PLAN、Space、多租户和安全底座已有大规模实现，不再是 README 早期描述的“仅待编码”。
- 五语 i18n、动态菜单、角色/动作权限、操作日志、SignalR、后台 worker、Docker/K8s 部署均已落地。
- Space 已完成发布、查看器、库存覆盖、多楼层、路径与成本等多波建设。
- WF 已完成信箱、通知、引擎硬化、服务任务、触发器、基础设施、子流程等多波建设。

## 2026-07 横切收口

- 多模块权限写端点贴点与种子已覆盖 OA/WF、ERP、MES、WMS、FIN、PUR、PLAN/PUB、Space 等主要域。
- HttpPatch 已纳入八套权限反射扫描。
- 新增“后端贴点必须存在于种子”的跨模块互锁测试。
- WF 审批归属校验已下沉引擎：本人、有效委派或系统 Actor 才能操作；admin 不再天然越权。
- 标准一般用户角色 `RoleId=10` 已按租户幂等预置，含 OA 最小菜单与动作集合。

## 2026-07-30 Space V1 受控集成基线

- 将散落工作树中的未提交 Space 后续实现固化到安全检查点 `0d25da4d`，完成敏感信息、异常大文件和生成物审计；原 E01 S03 分支保持未污染。
- 从当前交付基线 `dcc1ac9a` 建立唯一集成分支 `integration/space-v1-20260730`，以 no-ff 方式合入 E00 S01–S04 与 E01 S01–S03，形成提交 `539d56de`。
- 合并冲突按双侧约束共存处理：保留 WMS 序列追踪不可降级、Definition 不可变、Space 审计追加写三套保存护栏；解决方案同时保留 Mobile 与六个 Space 项目。
- 集成态验证通过：Release build 0 error；Space Unit 35 passed；Space Integration 7 passed / 18 SQL-gated skipped；CP6 主测试 2664 passed / 17 environment-gated skipped；前端 86 files / 539 tests、type-check 与 production build 全通过。
- E01 S04 Published→Draft Clone 已从候选中重建为最小切片：功能提交 `bac76444`，no-ff 集成提交 `85792161`；未夹带后续 BeamHeight、资产范围、规划场景或历史重发布能力。
- S04 功能态全量回归通过；合并态 Space Unit 41 passed、Space Integration 9 passed / 22 SQL-gated skipped，`dotnet ef migrations has-pending-model-changes` 确认模型与 Migration 一致。
- E01 S05 Design API v1 已按冻结边界重建：功能提交 `3258d47f`，no-ff 集成提交 `36f534d9`；交付 6 条路径/8 个操作、Problem Details、RBAC/外部主体闸、Site cutover、cursor 分页、24 小时幂等重放及 90 天保留索引。
- S05 同步交付可重复生成的 OpenAPI、C# SDK 和 TypeScript SDK；漂移检查、C# build、TypeScript strict compile 均通过。合并态全解构建 0 error，Space Unit 44 passed、Space Integration 9 passed / 24 SQL-gated skipped、Design API/权限聚焦 17 passed，EF 模型无待迁移变更。
- E01 S06 文件安全与保留已按冻结边界重建：功能提交 `6daf1aeb`，no-ff 集成提交 `2ccdff7a`；交付 Quarantined→Scanning→Clean/Rejected、失败关闭扫描、隔离 Worker 契约、引用感知墓碑和对象删除补偿。
- S06 合并态 Release 全解构建 0 error，Space Unit 52 passed、Space Integration 17 passed / 29 SQL-gated skipped，EF 模型、SDK drift 与 TypeScript strict 检查通过；新增 5 个真实 SQL Server 测试因本机认证门禁记作 skipped，不记作 passed。
- E02 S01 的中立实验门禁已按非生产边界重建：功能提交 `fe959066`，no-ff 集成提交 `3742fbff`；交付数据包完整性/版本审计、确定性压力资产、适配器子进程证据、ODA/APS fail-closed preflight 与隔离 Aspose 淘汰复现，不包含生产 `ICadConverter` 或 E02 S02。
- E02 工具 10/10 测试通过，50MiB/100 万实体压力生成通过；Aspose 26.6.0 复验为 25 次中 20 次成功、5 次 L5 崩溃，且 20 个成功观察均只保留图层 `0`。E02 S01 最终选型仍因正式黄金集、授权、供应商环境和冻结 Worker 缺失而阻塞，不计作完整签收。
- E07 S01–S03 已按冻结边界从候选重建：功能提交 `d06a8bd1`，no-ff 集成提交 `6e67a9d1`；交付 `space-wms-adapter-v1`、CP6 真实适配器、`T_SpaceWmsOperation` 幂等账本、标准内存模拟器、同构库存/任务查询和五类故障注入。
- E07 功能态与合并态验证通过：Release 全解构建 0 error，Space Unit 73 passed，Space Integration 35 passed / 30 SQL-gated skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型无待迁移变更；未夹带 E07 S04/S05、E08、E13、Workload 或发布 Saga。
- E07 S04 标准仓已按第 9 节冻结协议独立重建：功能提交 `74577015`，no-ff 集成提交 `6d751e0c`；交付确定性 500 货架、10,000 库位、SKU/库存/批次/容器、100 个拣货任务、DXF/底图/期望答案、WMS seed 与 6 个固定故障样本。
- S04 两次独立生成 17 个文件逐字节差异为 0，干净检出后的 Manifest 哈希错误为 0；合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed、Space Integration 40 passed / 30 SQL-gated skipped、CP6 主测试 2680 passed / 17 environment-gated skipped、Client 71 passed。DWG 外部门禁继续归 E02，不伪造资产；E07 S05 仍等待 E04 S04。
- E13 S01 已按 ADR-0002 和 AI Schema v1 冻结边界独立实现：功能提交 `8f7fc25e`，no-ff 集成提交 `ea161975`；交付 Provider/确定性端口、强类型输入输出、Provider 别名注册表、租户/Site/数据策略/外部开关门禁与原子配额租约端口。
- S01 默认依赖注入为租户 Disabled、Provider 空注册和配额失败关闭；新增 `space:model:generate-ai` / `space:model:review-ai` 权限及四个稳定 AI 错误码。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 97 passed、Space Integration 41 passed / 30 SQL-gated skipped、CP6 主测试 2680 passed / 17 environment-gated skipped、Client 71 passed；未新增 Migration、HTTP、外部适配器或 Provider 凭据。
- E13 S02/S03/S12 已分别以 `cff25a25` / `94822669`、`cebd401a` / `dca6e19c`、`54456946` / `b33929fb` 完成受控实现与集成：交付可审计 Run/Proposal/Decision/Usage 模型、Import/BuildScene 可恢复 Worker 控制面，以及数据库三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验、融合和 Apply 仍保持关闭。
- E05 S01–S04 已按独立边界交付通用元素、非均匀逐层货架、Design Revision 权威场景和 System/Tenant 版本化资产库；功能/集成提交依次为 `5bb0cdfb` / `49dbabe3`、`2fc03681` / `3d554852`、`00021f0a` / `a1edecef`、`85b57960` / `888de795`。
- E05 S05 以功能提交 `856f138c`、no-ff 集成提交 `a3864d9c` 交付 `space-parametric-v1` 确定性前端渲染链：逐层货架、box/path/polygon/point/asset、安全资产占位和稳定拾取映射均已覆盖；point 缺 Z、未知资产字段和运行态载荷失败关闭。
- E05 最新验证：Space Unit 203 passed；默认 Integration 46 passed / 41 SQL-gated skipped，真实 SQL 聚焦链 11/11 passed；前端 type-check、88 files / 546 tests 和 production build 通过，仅保留既有大 chunk 提示。
- E04 S01 以功能提交 `1d57a3b5`、no-ff 集成提交 `e8e84853` 交付 PDF/PNG/JPG 底图上传、E01 文件安全扫描复用、Ready/Clean 楼层挂接、受权 Blob 内容读取及 PDF.js/Konva 渲染；显隐、透明度和锁定已覆盖，S02 标定与 S03/S04 编辑命令未提前混入。
- E04 S01 验证：Space Unit 205 passed；默认 Integration 48 passed / 42 SQL-gated skipped，真实 SQL 6/6 passed；CP6.Tests 2685 passed / 17 environment-gated skipped；前端 type-check、90 files / 557 tests 和 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S02 以功能提交 `20ee0af0`、no-ff 集成提交 `c1043d15` 交付两点等比标定、第三控制点动态阈值验证、坐标确认、append-only 审计记录、Floor/Version revision、来源复合外键与 Published→Draft Clone 保真；没有混入 S03/S04 编辑命令。
- E04 S02 验证：Space Unit 210 passed；默认 Integration 48 passed / 43 SQL-gated skipped，真实 SQL 9/9 passed；CP6.Tests 2687 passed / 17 environment-gated skipped；API/权限 20/20，前端聚焦 3 files / 15 tests、全量 91 files / 561 tests、type-check 与 production build 通过；合并态完整 solution 0 warning / 0 error。
- E04 S03 以功能提交 `b322e84a`、no-ff 集成提交 `39146c38` 交付通用元素 2D 单选、属性面板、RemoveRequested 删除、`UpdateProperties`/`DeleteObject` schema v1 原子命令批次、Floor/Version revision、持久化幂等响应与逐命令 before/after 审计；没有混入 S04 多选、对齐、分布、阵列或撤销栈。
- E04 S03 验证：Space Unit 213 passed；默认 Integration 48 passed / 44 SQL-gated skipped，命令闭环真实 SQL 1/1 passed；API/OpenAPI/权限 21/21；前端聚焦 4 files / 8 tests、全量 95 files / 569 tests、type-check 与 production build 通过；SDK/EF drift 通过。完整 solution 0 error / 10 个既有 warning；CP6.Tests 的 6 个 RFQ 固定日期失败已在 S03 前基线复现。
- E04 S04 以功能提交 `9a87dc30`、no-ff 集成提交 `f9c7fd21` 交付货架/通用元素统一多选、套索、对齐、等距、旋转、批量删除、货架阵列和保存后补偿式撤销/重做；阵列复制 Active 设计层与空编码、Generated/Unbound 库位，不复制 WMS 绑定语义。
- E04 S04 验证：Space Unit 213 passed；默认 Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；API/OpenAPI/权限 25/25；前端全量 96 files / 575 tests、type-check 与 production build 通过；SDK/EF drift 通过，完整 solution 0 error / 10 个既有 warning。
- `0d25da4d` 中尚未独立提取的剩余范围仍为候选，不计入已完成实现，后续必须按依赖顺序逐项提取。

## 当前 GR-VP 波已完成

- T1：`StandardRoleSeed`，每租户创建一般用户角色，4 菜单、8 动作，insert-only；对应 7 个测试。
- T2：OA/WF 共 40 个按钮、17 个视图完成 `v-permission` 铺设。
- T3：ERP 共 39 个按钮、16 个视图完成 `v-permission` 铺设。
- T4：MES 共 31 条指令、12 个视图、24 个真实写权限键完成 `v-permission` 铺设；设备、工单、质检的新增/编辑模式已精确分流。
- T5：FIN 共 66 条指令、16 个视图、51 个真实权限键完成 `v-permission` 铺设；预算行内编辑在无 edit 权时保留只读值。
- T6：PUR/PLAN/PUB 扫描 12 个目标视图，37 个页面级权限声明覆盖 33 个唯一写权限键；`VolTable` 的 Seq 桌面/移动 CRUD 入口及异步权限加载完成守权，并新增 6 条权限回归测试。
- T1–T6 均有 SDD 报告与审查记录，Git 提交见 `CHANGELOG-AI.md`。
- T7：T6 已合入并推送 `main`；API/Web 从干净提交构建并部署。冒烟修复 OA 表单提交误依赖 `draft:add` 的链路，改走 `wf/flow/submit`；`qa_general` 的 4 菜单/8 动作、本人审批、他人待办拒绝和无权端点 403 均已实测。测试流程与临时定义清理归零。
- T7 数据环境仅注册 `DEFAULT/A1`，已验证全部现存租户；没有为满足旧计划的“四租户”描述而创建虚构租户。

## 换机资产

- 三个 SQL Server 数据库已在 2026-07-18 完成压缩、checksum 备份和 VERIFYONLY。
- `.bak` 已通过 Git LFS 上传至私有 GitHub 仓库。
- 所有本地分支、历史 marker 和迁移标签已推送。
- 恢复标签：`migration-2026-07-18-ready`。
