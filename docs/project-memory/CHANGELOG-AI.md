# AI 可读变更日志

> 依据 Git log 汇总，不替代完整 Git 历史。重点记录影响接手判断的里程碑。

## 2026-08-28：Space Studio V1 Core GA 100% 结案

- Added a deterministic controlled release-rehearsal runner bound to clean application commit `21a81767`, the frozen Source Set/Golden Dataset/Worker Environment, and ten exact source Git blob identities.
- Revalidated the accepted WP2/WP4/WP7/WP5 manifests, then passed 8/8 real SQL Server Publish/WMS/Published-isolation/recovery tests and 1/1 Kestrel signed-JWT security test with zero failures or skips.
- Observed five-second automatic recovery and a 3.6480777-second CHECKSUM backup/restore with `DBCC CHECKDB`; verified restored Published hash/WMS write count, old-Published continuity, reconciliation, idempotency and cleanup.
- Accepted WP6 and WP8 and recorded DeliveryOwner `BUBAO.GAO` as Signed. The derived Core GA state is now `GaReady` / 100% with 0 pending inputs, gates or signers. Production data, production WMS, Pilot and production deployment remain explicitly unclaimed and separate.

## 2026-08-28：Space WP5 Viewer 正式接受

- Added production Viewer keyboard/focus semantics and a deterministic browser gate for Current Published-only requests, 1440×900/1280×720, native keyboard operation, Chromium accessibility-tree names and 4.5:1 contrast.
- Replaced the machine-specific default Iris Xe regex with an optional diagnostic while retaining mandatory hardware WebGL2, no software renderer, one consistent renderer, fixed absolute budgets, zero console errors and complete pick integrity.
- Formal evidence on `4b774bb4` passed Published boundary 12/12, Viewer browser 4/4, 30/30 cold performance runs and 3,000/3,000 picks; repository regression passed 176 files / 906 tests, strict type-check and production build.
- WP5 is Complete/Accepted. UI fixtures remain explicitly Simulated and do not claim production data/WMS; Core GA remains 72% / NoGo with 0 pending inputs, 2 pending gates and 1 pending signer, and no production deployment occurred.

## 2026-08-28：Space WP2 CAD Start 正式接受

- Added a standalone controlled acceptance runner that verifies the frozen authorized DWG/DXF, exact AutoCAD Primary Worker release and Core executable, then executes the product Preparation/Parse chain on disposable SQL Server.
- Both formats passed explicit Floor/Unit/Transform/Mapping selection, zero-write Preview, sealed Preparation, Parse Start and idempotent replay; a tampered mapping hash was rejected with jobs unchanged at 2, and Worker raw-CAD/attempt residuals were zero.
- Focused backend tests passed 21/21, Web Wizard/API tests 14/14, strict type-check passed, and the formal validator rejected 16 failure modes. The overall GA attestation suite remains green at 52/52.
- WP2 is Complete/Accepted. Core GA remains fixed at 72% / NoGo with 0 pending inputs, 3 pending gates and 1 pending signer; no production data, WMS or deployment is claimed.

## 2026-08-28 UTC：CRM Platform P02 冻结可消费

- Recorded Platform PR #3 at `main@6004decd2a4e41d9d502738dc5d9063bef9b37b7`, successful dual-platform validation/publication, and three immutable `0.2.0-alpha.1` packages with per-package SHA-256 evidence.
- Recorded CRM PR #21 at `main@72c405b4e6ab0ab708cfa1b579b8821a1402dcfe`, exact Abstractions consumption, TenantId-to-OrganizationId mapping, no-default-tenant coverage, and credential-free read-only Actions authentication.
- Consumer PR run 33144894103 attempt 2 and main run 33146816096 passed private-package restore and the full CRM gate. P02 is now `Frozen / Consumable`; P03, login, business work, cloud resources and deployment remain outside this closure.

## 2026-08-28：Space WP1 手工建模正式接受

- Added a formal single-owner WP1 evidence package bound to `main@b0164a15` and canonical Git blob/SHA-256 identities for two SQL and six Web test sources.
- Ran the focused SQL Server Express LocalDB 17.0.4025.3 gate at 20/20 with zero failures/skips and the six focused Web files at 25/25. The earlier environment-missing 17-skip run was explicitly discarded.
- Proved Blank/Floor creation, System/Tenant templates, a complete coded warehouse, zero-write Preview, explicit Apply, Lease/Revision/Idempotency fences, atomic failure and Published isolation.
- Added a 12-case standalone validator and overall WP1 attestation checks; WP1 is Complete/Accepted. Core GA remains 72% / NoGo with 0 pending inputs, 4 pending gates and 1 pending signer; no production deployment occurred.

## 2026-08-28：Space WP0 基线与治理正式接受

- Added a formal WP0 baseline/governance Manifest bound to `main@162d1108`, the sole DeliveryOwner, Kickoff/target dates, both Complete external inputs, and Accepted WP3/WP4/WP7 dependencies.
- Bound PR #59's 7/7 required checks and clean post-merge 11/11 and 42/42 evidence smoke while explicitly recording that no production deployment occurred.
- Added a 9-case standalone validator and expanded the overall GA attestation suite to 47 cases, rejecting templates, unattested evidence, missing dependencies, and input/Owner/date/Commit drift.
- WP0 is Complete/Accepted. Core GA remains 72% / NoGo with 0 pending inputs, 5 pending gates, and 1 pending signer.

## 2026-08-28：Space WP4 三路径正式接受

- Added a fail-closed WP4 evidence protocol and validator for exactly three authoring paths: authorized real DWG/DXF Primary output, controlled Excel–CAD, and controlled PDF/PNG underlay plus blank canvas.
- Bound the run to `main@9468f7f6`, the frozen WP7 Source Set/Golden Dataset/Worker, exact Primary packages, a product-generated 12,466-byte XLSX, and controlled underlay hashes; no production data or deployment is claimed.
- Ran the complete Space integration suite on SQL Server Express LocalDB 17.0.4025.3: 465 passed, 0 failed, 0 skipped. Added 11 focused validator tests and integrated WP4 prerequisite/attestation checks into the overall GA validator.
- Accepted `three-path-formal-evidence-v1.0.0.json` under DeliveryOwner `BUBAO.GAO`; WP4 is Complete/Accepted. Core GA remains 72% / NoGo with 0 pending inputs, 6 pending gates and 1 pending signer.

## 2026-08-28：Space WP7 正式黄金 CAD 接受

- Added a deterministic formal business evaluator for the frozen 20-file authorized original CAD set, including exact source/hash binding, geometry and hierarchy checks, calibration-only threshold selection, out-of-sample reporting, operation reduction and Holdout omission detection.
- Overall quality passed at 99.0224% coverage, 98.7008% semantic/high-confidence precision, 98.1717% Wilson lower bound and 96.9043% manual-operation reduction; out-of-sample quality passed at 99.2352%, 98.9828%, 98.3541% and 97.5781%, with zero unreported Holdout Blocking omissions.
- Ran one excluded warmup plus 20 stable observations against an exact 50 MiB authorized-original-derived DXF envelope; review-ready P95 was about 2.323 seconds, first-Ready P95 about 1.937 seconds, with zero failures. The evidence explicitly does not claim 50 MiB customer geometry complexity or a production deployment.
- Added reproducible performance/manifest tooling and accepted `golden-cad-formal-evidence-v1.0.0.json`; WP7 is Complete/Accepted. Core GA remains 72% / NoGo with 0 pending inputs, 7 pending gates and 1 pending signer.

## 2026-08-28 UTC：CRM Platform P01/P10 签名里程碑对齐

- Aligned the CRM V1 executable specification so P01 requires reproducible pack verification and no empty-package publication, while formal signing candidates remain a P10 release-governance responsibility.
- Collapsed the controlled digest-rotation window back to the single new executable-spec digest and synchronized project memory without adding runtime implementation or publication authorization.

## 2026-08-28：Space AutoCAD Primary V1 资格接受与 WP3 结案

- 新增本地受控 `LocalControlledProcess` 机器合同：Owner 可接受 OS Firewall 出站策略未验证，但无网络监听、无业务凭据、临时 CAD 强制删除和可审计报告不可放宽；生产/SaaS/远程托管/再分发继续排除。
- 正式 AutoCAD `1.0.0` Worker 以同一 20 份受控 CAD、冻结环境和不可变 Release/评测哈希完成保守六维评分 86/100；`cad-provider-adr-0001-v2` 输出 `cadGaReady=true`、唯一 Primary、0 Blocking Code。
- 新增边界批准、评分输入、资格输出和 Provider 增量 Kickoff Manifest；外部 Provider 输入 Complete、WP3 Complete/Accepted，Backup 不作为 V1 阻断项。
- 正式 Core GA 仍为 72% / NoGo（0 个输入、8 个 Gate、1 个签署 Pending）；WP7 业务准确率/Wilson/人工减少/受训用户时长、发布演练及最终签署没有被转换评测替代，未执行生产部署。

## 2026-08-27：Space AutoCAD Primary 正式 Release 绑定评测

- 新增 `evaluate-release` 与严格报告 Schema：复核封存 Worker/Core 后，对受控 20 份 CAD 双跑并固定检查数据集身份、Package 确定性、99% 支持率、SourceRef、Blocking、120 秒上限和临时数据清理。
- PR #53 以 7/7 required checks 合并；从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 封存正式 `1.0.0`，Worker Release SHA 为 `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e`。
- 正式评测 20/20、确定性 20/20；支持实体 14,659/14,699（99.727873%），SourceRef/Blocking/残留为 0，P95 4.281 秒；完整安装回归 61/61，Release/报告 Schema 均通过。
- 正式报告仍标注 OS 出站策略未验证，未冒充生产 mTLS/Firewall、完整 Provider 接受或生产部署。Core GA 保持 72% / NoGo。

## 2026-08-27：Space AutoCAD Primary 批准与单 Provider Ready

- `BUBAO.GAO` 批准 AutoCAD 2025 Core Console 为 V1 唯一 Primary；范围限定为本机受控 CP6 开发、验证和 Release Rehearsal，不扩写为软件再分发、公共 SaaS 托管或生产部署授权。
- 复核 Core `25.0.58.0.0`、固定 SHA、有效 Autodesk 签名和运行中的 Licensing Service；真实安装型合同/Worker 2/2、4,424/4,422 实体、0 CAD/Attempt 残留。
- `qualify-providers` 与 Site `CadGaReady` 改为一个满足硬门禁、至少 80 分、唯一最高分且覆盖 DWG/DXF 的 Primary 即可 Ready；机器规则显式升级为 `cad-provider-adr-0001-v2`，可选 Backup 不阻断 Core GA。
- 新增版本化批准记录；正式 SemVer Worker 与 Release 绑定转换报告已在后续任务完成，外部 Provider 输入仍等待隔离/安全依据、资格评分和业务级黄金集指标，GA 仍为 72% / NoGo，未执行生产部署。

## 2026-08-27：Space Lean Core GA Schema 3

- 移除首版过度流程门禁：独立 Backup Provider、Greenfield/Retrofit 双仓、各 14 天 Pilot、客户来源 CAD 和额外人员确认；Backup 与现场 Pilot 转为 GA 后增强。
- 外部输入从三类减为两类，WP3 改为一个 Primary Provider/隔离 Worker，WP8 改为一次受控 SQL Server/WMS/Published Viewer/恢复/安全发布演练。
- 20 份冻结 CAD/Holdout、Provider 真实许可与不可变身份、资格分、质量/Wilson/人工操作/性能、Blocking 遗漏、恢复和安全门槛保持失败关闭。
- GA、Kickoff、Golden CAD 与新发布演练验证器/测试同步升级。当前 CAD 输入 Complete，正式状态仍为 72% / NoGo（1/9/1 Pending）；未执行生产部署。

## 2026-08-27：Space 原创黄金 CAD 候选

- 将单人开发的数据资格明确为 `ApprovedOriginalWork`：CAD 由 `BUBAO.GAO` 原创、授权并实名复核，不要求不存在的客户或第二复核人，也禁止虚构客户来源。
- AutoCAD 2025 原生生成并在仓库外冻结 20 份唯一 AC1032 CAD，10 DWG / 10 DXF、10/5/5、L1～L5 各 4；合计 14,659 个 Model Space 图元和 2,455 个标准答案元素。
- 每份源 SHA、授权、脱敏、单位/坐标、格式/版本、答案/问题、Mapping/规则和复核证据已绑定；Source Set SHA 为 `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`，Golden Dataset SHA 为 `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`。
- 产品 Converter Contract Runner 20/20 Pass；新增脱敏候选 Manifest、独立失败关闭验证器/测试和总 GA 组合校验，原始 DWG/DXF 不进入 Git。`AUTHORIZED_GOLDEN_CAD_CANDIDATES` 改为 Complete。
- 该完成项不自动接受 WP7；按后续 Lean Schema 3，Primary Provider/隔离 Worker、受控发布演练、质量/性能和签署仍 Pending，Core GA 保持 72% / NoGo（1/9/1 Pending）。

## 2026-08-27：Space Studio Development V1 100%

- 建立与正式 GA 隔离的 `CP6_SPACE_STUDIO_DEVELOPMENT_V1`：六个开发 Gate 全部 Passed，派生 `DevelopmentComplete` / 100%；唯一 Owner 为 `BUBAO.GAO`，没有多人门禁。
- 20 份合成 DXF 再生与完整性审计通过，L1～L5 各 4、五个 DXF 版本齐全；53,190,207 bytes / 670,000 实体和 79,517,079 bytes / 1,000,000 实体两档容量门禁通过。两个 JSONL 仅受 Windows CRLF 检出影响，归一化内容完全一致。
- 新增失败关闭验收校验器、8 个正负场景与 GitHub 门禁，逐种子复核 SHA、从 Gate 派生百分比，锁定验收当日正式 72%/3/9/1 快照，并禁止开发数据/报告进入正式 accepted evidence；完整证据脚本 125/125，AutoCAD 安装回归 57/57、0 skipped。
- `formalGaEligible=false`、`countsTowardProductionGa=false`；正式 E02 Ready 负向审计仍正确失败，Core GA 保持 72% / NoGo、3/9/1 Pending。

## 2026-08-27：Space GA 单人 Owner 与外部输入盘点

- `BUBAO.GAO` 已登记为唯一 DeliveryOwner、三类输入与 WP0～WP8 责任人；Kickoff/目标 GA 固定为 `2026-08-27` / `2026-09-27`。WP0 实现改为 Complete，但接受与唯一签署仍 Pending。
- `D:\CP6` 跟踪 CAD 为 28 DXF / 0 DWG；正好 20 份的 development corpus 明确是小型合成 DevelopmentSeed 且不计 Release Gate，正式授权/脱敏证据只找到空模板。
- ODA 许可证变量未配置、Drawings SDK 包未发现；历史 File Converter 不是 Backup SDK。Pilot Site/WMS 窗口未知，三类外部输入、九个接受 Gate、一个签署和总体 72% / NoGo 均保持不变。

## 2026-08-27：Space AutoCAD 候选 Worker 不可变 Release

- 新增 Schema 1 Worker Release Manifest 与生成/启动复核：完整封存 Payload、源提交、Runtime、Core Console 哈希/版本及 DXF Converter 版本；可运行 Host 只广告 `cp6-autocad-worker` 非 development 身份。
- Manifest 外部完整 SHA 是权威身份，Provider Version 只嵌入 12 位可见前缀；Schema 2 远程协议把完整 SHA 贯穿批准运行时、请求、Worker 前置核对和响应回显。每次 DWG 转换前还会再次核对 Core 完整哈希。
- 真实 `win-x64` 演练封存 18 文件，Schema 通过；安装 CAD Experiment 57/57、远程协议 6/6，主测试 2,939/19/0，整仓 0 warning / 0 error，测试残留 0。
- PR #46 在 7/7 required checks 后合并；从精确 `main@4375c7c2fc1e297bf3fe845873b1af5af2cb5d66` 重建的 `0.0.0-rehearsal.postmerge` 再次封存 18 文件并通过 Schema，完整 Worker Release SHA 为 `c51c2ce8925f7bf2bf647dd2d958270d7903e6adc212eee37a668bfe9d82dc84`，合并后专项 10/10 与 6/6。
- 两次演练都不写 `acceptedEvidence`。批准 SemVer、许可证/Site/部署、独立 Backup 与授权黄金集仍缺，整体保持 72% / NoGo。

## 2026-08-27：Space DXF 50 MiB 受控容量

- `DevelopmentDxfCadConverter` 从 25 MiB 整文件三重驻留改为 64 MiB bounded hashing stream + 严格 UTF-8 逐行解析；源哈希继续覆盖原始字节，999 注释不进入语义内存。
- 新增精确 50 MiB 成功和 64 MiB+1 解析前拒绝门禁；失败不创建 CAD IR。Converter Version 升为 1.1.0，AutoCAD 组合 Provider 自动换版。
- 安装环境完整 CAD Experiment 47/47、0 skipped；真实 Core Console DWG 指标无回归，残留 CAD/Attempt 为 0。
- 该结果只证明合成输入容量，不替代授权真实 50 MiB 的 P95、资源、准确率、主备评分与批准；总体保持 72% / NoGo。

## 2026-08-27：Space AutoCAD 候选 Worker DWG/DXF 双格式

- 新增组合 `ICadConverter`：DWG 内链为 AutoCAD Core Console，DXF 内链为托管 Parser；外层组合与两个内层都只能经 `SpaceCadConverterContractRunner`。
- 候选 Provider 身份改为 `cp6-autocad-worker-development/{core-version}+cp6-dxf-1.0.0`，不把原生 DXF 冒充 AutoCAD 结果；旧版本在原始文件落盘前拒绝。
- Worker 健康能力现声明 DWG/DXF；原生 DXF 自动化证明不会调用 Exporter，并与 DWG 一样执行源哈希、只读 staging、CAD IR-only 响应和 Attempt 清理。
- 聚焦 4/4、安装环境 CAD Experiment 45/45、残留 CAD/Attempt 为 0。真实授权 DXF、25→50 MiB 能力、Release 身份、独立 Backup、批准与生产 Failover 仍 Pending，整体保持 72% / NoGo。

## 2026-08-27：Space Studio WP3 远程隔离 CAD Worker Provider

- 新增最小化 CAD-only Worker 协议、受限 HTTPS 流客户端和生产 Provider；跨边界只传原始 CAD、SHA-256、格式、Attempt 与精确 Provider 身份，不传 CP6 业务/数据身份。
- 运行时默认关闭；启用时以外部 SHA 固定批准 Manifest，严格核对版本、资格分、黄金集/环境/Worker Release 哈希、审批引用、有效期、mTLS、无出口/无业务凭据、清理和合同执行器声明；客户端另做 CA/主机名、吊销与服务端证书 SHA-256 Pin。
- Mapping Profile 精确版本、完整 Layer Override Replay、语义、诊断和 PreviewSet 留在 CP6；隔离 Worker 不能选择 Mapping 或写 Draft。
- 新增可运行的 AutoCAD Core Console DWG 候选 Worker；真实安装型测试通过完整 Worker 边界并清除 Attempt 原始/派生 CAD。它仍是开发候选，不支持真实 DXF，也没有许可证/Site/隔离部署/Backup/20 份黄金集正式证据。
- 仓库切片验证覆盖远程 Provider 4、路由 16、候选 Worker、Space Unit 550、Space Integration + LocalDB 462 和完整 solution。WP3 保持 Partial/Pending，`acceptedEvidence` 为空，整体保持 72% / NoGo。

## 2026-08-27：Space Studio WP1 统一建模与模板制作

- Design V1 统一支持 Blank、PublishedVersion、SystemTemplate、TenantTemplate；模板模式核对当前不可变版本和密封 ProposalHash，并幂等初始化全部楼层。
- 版本新增创建来源、模板 ID/版本/内容 SHA-256 持久化及 SQL 一致性约束；当前 Draft 可经零写入预览创建租户私有不可变整仓模板。
- 空白首层显式采集宽度/深度，模板创建只接受可无损表达的规则布局；重试不会覆盖已被修改的未完成楼层。
- LocalDB 17/17、10,000 库位 System Template、Tenant Scope、OpenAPI 57/57、Web 19/19、完整 solution、EF/SDK/type-check/production build 通过。WP1 实现改为 Complete，但接受和整体 GA 继续 Pending / 72% / NoGo。

## 2026-08-26：Space Studio 单人交付门禁

- 核心 GA 证据合同升为 Schema 2：正式签字收敛为一个 `DeliveryOwner`，删除五角色实名签字及 2 Backend + 2 Frontend3D + 1 QA 人力配额。
- M0 外部输入从五类收敛为授权 CAD、Provider/隔离 Worker、双仓/WMS 窗口三类；同一实名 Owner 可拥有并接受全部输入。
- 黄金 CAD 从双标注 + 独立 QA 仲裁改为单一实名 `reviewedBy`；Pilot 的客户/实施确认允许同一获授权人员兼任；高风险发布改为同一 Owner 的显式二次确认和可恢复证据。
- 真实 CAD、主备 Provider、SQL/WMS/Published Viewer、性能、恢复、安全负向和双仓 14 天 Pilot 标准保持不变。当前仍为 72% / NoGo，但人头不足不再是阻塞项。
- GA 总门禁 35、开工 22、黄金 CAD 31、Pilot 21、开发角色种子 8 个专项场景全部通过。

## 2026-08-26：Release/CD 仓库与平台工程结案

- Shadow S0 经 PR #32 合入 `main@9009abe6`；Azure 新建定向授权的 `CP6 Release Shadow` Definition #5，Run #145 绑定同一 SHA 并在无 Secret、无 Registry/Environment 权限下完成离线验证，结果为 `Succeeded`，Artifact 为 `cp6-release-shadow-s0-145`。
- PR 验证唯一归属 GitHub；Azure 继续 `pr: none`。补齐 CI/R2/Space 责任矩阵和 `CP6-Windows` / `CP6-Deploy` 更新、离线、单并发、磁盘、clean checkout 与身份隔离规则。
- 新增人类/机器可读结案证据及失败关闭合同：工程状态为 Complete 时仍强制 `v1.0.0` 保持 Draft、20 项发行输入 Pending、生产状态 No-Go，避免用工程结案冒充候选或部署成功。
- 当前 GitHub R2 外部状态为零 Release、零受保护版本 Tag、零 R2 workflow Run、零 Environment、零仓库 Secret；S1、UAT/PROD、灾备和多仓推广仅在真实候选/环境/批准人到位后按事件建立单任务卡。

## 2026-08-26：CRM V1 PRD 完整脱敏产品基线批准

- 合入前审查发现三次未合并 payload 仍公开了应保留在私有仓的商业 cohort、精确推广时间表或私有数值采用门禁；三次预审批均作废。
- 从最新 `main` 新建不继承旧敏感提交祖先的干净候选分支，并扩大脱敏范围到 PRD、竞品基线和项目记忆；旧证据仅保留在未合并 PR 审计轨迹中。
- 最终审查继续移除公开 M0 Readiness、产品框架和可执行 Spec 中遗留的 Pilot 样本、采用窗口与 KPI 数字；自动发现并锁定全部 `docs/crm/**` 文件，新建未登记 CRM 文档失败关闭。
- `crm-v1-prd` 工作流拆为 PR head 诊断和受保护 base 验证；required context 只由 `pull_request_target` 的只读 base validator 对精确 PR head 产生，引导 PR 不把自带脚本当作独立信任边界。
- 唯一 ProgramOwner 已批准完整脱敏 payload、候选 commit/blob 和五项产品结论；当前状态为 `Approved product requirements baseline`，Public Contract Sync 保持 Complete，M0 保持 No-Go，没有实现或部署副作用。

## 2026-08-26：Azure Release Shadow S0 仓库合同

- 新增 `azure-pipelines-release-shadow.yml`：固定手动触发，在无 Secret 条件下验证仓库内固定 fixture 并只发布非权威 Shadow Artifact；未加入 Service Connection、网络下载、镜像或部署能力。
- 新增严格 candidate parser，逐层验证 Schema 1 candidate result、Schema 2 manifest、freeze/spec SHA-256、版本/Tag/Git SHA、GHCR allowlist/digest、签名/供应链/db-init 元数据，并固定输出 `Authority=Shadow`、`Deployable=false`。
- 新增 1 个有效与 10 个失败关闭场景，覆盖错误来源/版本/SHA/Tag/hash/repository/digest/freeze/Deployable 越权；R2 source gate 同步执行脚本解析和静态无 Build/Push/Pull/Tag/Deploy 能力合同。
- 当前只完成仓库 S0，不代表 Azure 已创建 Pipeline、读过真实候选或获得发布权限；S1 真实候选只读元数据仍是下一独立任务。

## 2026-08-26：发布权威与 Registry 决策

- 新增 `ADR-DEVOPS-001`：当前 CP6 唯一候选 Registry 为 GHCR，唯一候选/部署权威为 GitHub R2；Schema 2 `release-manifest.json` + `candidate-result.json` 是唯一候选链。
- Azure Phase 3 收敛为只读 Release Shadow，不 Build/Push/签名/生成第二清单/部署；输出必须标记 `Authority=Shadow`、`Deployable=false`。
- 记录现有 R2 门禁与 Azure 影子要求的等价矩阵、GitHub/GHCR/evidence/Azure Artifact 最小权限、S0/S1/S2 验收、30 分钟回退及未来 ACR 切换硬门禁。
- 当前未创建 ACR、Service Connection 或生产资源，未运行候选、镜像操作、部署或 Cloudflare 切换；下一任务是无 Secret 的 Shadow S0 离线合同。

## 2026-08-26：CP6 SaaS V1 公开工程契约同步完成

- ProgramOwner 已在 PR #8 批准 `CP6-SAAS-V1-PUBLIC-CONTRACT` 精确摘要 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`；append-only 记录固定评论 URI、UTC、证据 commit/blob、私有 Frozen 产品摘要 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b` 与 R00 摘要。
- 公开合同与 R00 镜像同步为 Complete；验证器核对角色/摘要/证据、脱敏边界和私有 Frozen/Accepted 源，四仓边界、API/事件/数据、安全、迁移、SLO、候选身份与 M0 规则未改变。
- M0 仍为 No-Go；DEC-001、DEC-003 至 DEC-009、SQL 容量、真实 Pilot cohort、专业证据、Critical/High 与私有仓库保护继续失败关闭。没有解锁 CRM01 或创建云资源、数据库、迁移、候选和部署。

## 2026-08-26：DEV 自动发布稳定性闭环

- #131 attempt 1 在 61 次低内存采样后于 SQL/备份前失败；同 Run 重试 Deploy 后实际完成备份、迁移、健康与 `50a1db6d...` 身份，只因 attempt 1/2 复用固定 Artifact 名而最终失败。证据 Artifact 现按只读 `System.StageAttempt` 命名，保留每次尝试且不冲突。
- PR #30 合入 `main@08813896...` 后，GitHub client-contract/SQL 与 Azure 基础 CI #132 成功；#132 通过 Pipeline Completion 自动触发 #133，同 SHA、main 与来源 CI 关联均通过分类和锁内新鲜度检查。
- 自动 #133 readiness 三次为 2184/2383/2411 MiB 且 SQL=True；第 7 份 CHECKSUM/VERIFYONLY 备份为 2,600,960 bytes、SHA-256 `af4f48fd...d804c9de`。API/Web Healthy、完整 SHA 身份一致，`cp6-dev-evidence-attempt-1` 发布成功，根 API/DB ID、StartedAt、RestartCount 未变。
- 自动开关保持 `true`，公网验证保持 `false`；没有切换 Cloudflare，生产发布权威仍为 GitHub R2/GHCR，本机 DEV 结论不外推到 UAT/PROD。

## 2026-08-26：DEV 备份前主机/SQL 就绪门禁

- 自动 #125 已真实完成首次 `ResourceTrigger` 发布；随后 #127 在 runtime-only 镜像封装后收到主机内存使用 95.16% 告警，首次 SQL prelogin 超时并在备份前失败。无新备份、迁移或容器切换，旧 DEV 健康；失败后 8/8 新连接快速成功，根因锁定为瞬时宿主压力与缺少恢复门禁。
- DEV 锁内现最多等待 600 秒，要求至少 2048 MiB 可用内存和 3 次连续独立备份身份 SQL 登录；不满足即在 BACKUP 前失败，并归档逐次 `backup-readiness.json`，不重试有副作用的备份。
- 成功部署证据升级为 Schema 3 并嵌入 readiness 记录；行为、sqlcmd、DEV CD 与数据安全回归通过。生产 R2/GHCR、公网开关和根 `cp6`/`CP6DB` 边界未改变。
- main CI #128 成功后，自动 #129 的 31 次采样始终只有 1328～1861 MiB；SQL/备份/迁移/切换全部未启动，证明门禁真实失败关闭。主机约 8 分 40 秒后才恢复到 2 GiB，因此等待窗口由 300 扩为 600 秒而不降低安全阈值。

## 2026-08-25：DEV 自动发布开关启用

- 三次独立 Manual DEV 验收 3/3 后，用户明确授权继续自动闭环；Azure `CP6_DEV_AUTO_DEPLOY_ENABLED=true`，公网验证仍为 `false`。
- 基础 CI #124 completion 真实触发 DEV #125 / `resourceTrigger`；Artifact 校验/封装、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份和 2 文件证据均成功。第 5 份备份为 2,572,288 bytes，SHA-256 `bcd9f228...a574`，本机复算一致。
- DEV 已运行 `main@ecbad9e1...` 且 Healthy，根 API/DB 基线零漂移。生产发布权威仍是 GitHub R2/GHCR；本机 DEV 自动模式不推广到 UAT/PROD，旧版本手动回退前仍须先关闭自动。

## 2026-08-25：DEV 三次独立 Manual 验收 3/3

- PR #24 合入 `main@a5c6b5fa...`；GitHub main client-contract 与 Azure #118 成功，自动 completion #119 在 `CP6_DEV_AUTO_DEPLOY_ENABLED=false` 时安全跳过 Package/Deploy。
- Manual #120/#121 复用 #118 的同一不可变 Runtime Artifact，各自独立完成分类、验证/封装、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份与 2 文件 `cp6-dev-evidence`。备份目录 2→4；SHA-256 分别为 `c90a3db2...19a3a`、`9fc35ca1...414fb`。
- #95/#120/#121 现为 3/3。最终 API/Web 为 `0.0.0-dev.a5c6b5fa...59e6`，8/8 SQL 查询成功且无新增 701/17300；公网七容器零漂移、旧 Tunnel 保持运行。自动/公网开关仍为 `false`，未宣称公网切换或生产部署。

## 2026-08-25：GitHub 远程构建与 Azure 轻量 Artifact 桥

- #113/#115 证明降低并发或拆分项目仍不能让本机完整编译与 SQL/Docker 安全共存；#110 又证明 Azure 组织没有 hosted parallelism。完整 .NET/Web/客户端/Android/R2 source 门禁因此迁至 GitHub hosted `client-contract`，运行包按完整 SHA 命名、内部逐文件哈希、保留 3 天。
- Azure 基础流水线改为只验证合同、使用已有授权 Checkout 凭证下载同 SHA 成功工作流的产物、核对 GitHub 归档 SHA-256/ZIP 安全/内部 manifest，再发布 Azure Pipeline Artifact；不本机编译、不部署。
- GitHub Run 32879704210 首次成功；Azure #116 因 extraheader 查询缺少仓库路径在下载前安全失败。修复后 GitHub Run 32881647447 与 Azure #117 完整成功，SQL 与公网七容器基线不变。分支 Artifact 不计 DEV，仍须 main 成功和两次独立 Manual DEV；自动/公网关闭、R2/GHCR 生产权威不变。

## 2026-08-25：DEV 复用 CI 哈希运行时产物

- #109/#111 暴露 self-hosted 基础 CI 的并行 MSBuild 内存竞争，均在 Artifact 与 DEV 部署前取消；#110 的 Microsoft-hosted 探测因组织没有 hosted parallelism 在 Checkout 前失败，未启用计费。#112 以非并行 restore、单节点 build/test、禁用持久/共享编译服务器和两个 Vue worker 完整成功并发布 Runtime Artifact，最低观测可用内存约 2.22 GiB，SQL/受保护容器基线不变；该分支证据不能替代成功的 main 候选。
- 首次主线 CI #108 暴露 PowerShell 编排假红：合同脚本已输出 passed，但 Step 随后读取了进入脚本前遗留的非零 `$LASTEXITCODE`。修复删除 `.ps1` 后的外部进程码判断，改由 terminating error 传播，并让基础 CI/DEV 静态合同拒绝该反模式；#108 在 restore/build 前结束，无环境副作用。
- CI #102、关闭状态 completion #104 与 Readiness #105 成功；Manual #106 因资源版本输入错误在 YAML 解析前失败，无副作用。Manual #107 正确绑定 CI #102，但 DEV 中重复宿主 publish 达约 4.18 GiB 工作集并导致 `CP6_DEV` 新连接超时，按门禁取消；没有备份/迁移/候选切换，根 API/DB 不变，旧 DEV API 重启，因此不计验收。
- 基础 Azure CI 现从同一次通过测试的 API/Web build 收集 runtime payload，生成带版本、完整 SHA、逐文件长度和 SHA-256 的 `cp6-dev-runtime` Pipeline Artifact；DEV 下载所选 CI 的 Artifact，拒绝篡改、额外文件或身份错配，只用 runtime-only Dockerfile 封装并捕获不可变 image ID。
- 真实 145,966,387 bytes API 与 7,473,275 bytes Web 共 587 个文件已完成本机哈希与约 17 秒封装验证，根 API/DB、旧 DEV API 和 `CP6_DEV` 均保持稳定。GH R2/GHCR 生产权威、自动/公网关闭状态不变，手动成功计数仍为 1/3。

## 2026-08-25：DEV 候选宿主机构建与 Docker 运行时封装

- Manual Run #98 在 API Docker publish 报宿主内存使用 96.03% 后于 Deploy 前取消；没有新备份、迁移或 DEV 候选切换。Docker OOM 造成根 `cp6-db` 重启一次、`cp6-api` 累计重启两次，因此不计手动验收。
- API Dockerfile 关闭持久 build server、限制单 MSBuild 节点并禁用项目并行/共享编译后，Manual Run #101 仍在 Docker VM 使用率 95.83% 时取消；Deploy Skipped、无备份/迁移/候选切换，但根 `cp6-db`/`cp6-api` RestartCount 增至 2/3，因此同样不计验收。
- DEV 候选改为部署 Agent 使用固定 .NET 8/Node 22 在 Windows 宿主机串行构建 API/Web，Docker 只以 runtime-only Dockerfile 封装预构建产物；Web Node 堆限制为 768 MiB。GitHub R2 与生产 Dockerfile/工作流不变。
- 提交 `72ec0e70` 的本机完整构建生成并核对两个不可变 image ID，临时上下文清零；Docker VM 采样保留约 1.9 GiB 以上，根 API/DB 三项元数据不变且宿主 SQL 无新增 701/17300。自动/公网仍关闭，手动成功计数保持 1/3。

## 2026-08-25：Azure CI 与首次手动 DEV 发布外部闭环

- Azure CI Run #92 在 `main@47ca8441` 完整成功。此前 `.NET Restore` 的 PowerShell 类型数据冲突来自 `CP6-Windows` Agent 继承 PowerShell 7 `PSModulePath`；清空父环境后同一提交通过，仓库新增前台启动器和合同测试固化该运行方式。
- `CP6 DEV CD`、定向 Pool/Variable Group/Environment 权限、Exclusive lock、两项关闭开关、`cp6_dev_backup` Secret/权限和 Readiness Run #89 已完成；completion Run #93 成功证明自动关闭时 Build/Deploy 安全跳过。
- Manual Run #94 在备份通过后因宿主 SQL Server 已有 701/17300 内存耗尽事件而失败关闭；重启数据引擎后，Manual Run #95 成功发布 `0.0.0-dev.92` / `47ca8441...9dbe9c18`，备份、迁移、不可变镜像、本机健康与证据 Artifact 均验证通过。
- 根 `cp6`/`CP6DB`/旧 Tunnel 未修改；自动和公网验证仍关闭，当前手动 DEV 验收为 1/3，未宣称 UAT/PROD 或公网切换完成。
## 2026-08-25：CRM 竞品分析与 PRD v0.2

- 新增 `docs/crm/CRM-COMPETITIVE-ANALYSIS.md`，使用 9 个公开 CRM 的官方产品和价格页面建立市场分型、业务主链、横向能力及商业成本假设。
- 新增 `CRM-COMP-001`～`007` 决策追踪，将行动优先 Lead Pilot、稳定语义、来源/SLA、CRM/ERP 权威、VNext 连接器、AI 边界和套餐原则映射到 PRD。
- `CRM-V1-PRD.md` 升为 v0.2，仍为 `Draft for Product Review`；本次只补产品研究和决策依据，不改变 M0 `No-Go`、Public Contract Sync 或实现状态。

## 2026-08-24：CRM V1 产品需求草案

## 2026-08-25：DEV 首次运行前置审计与 sqlcmd 路径修复

- 实机确认 Docker/Compose、专用 Azure Agent、`KOUSQLSERVER`、`CP6_DEV` 与 SQL TCP 端点可用；创建并收紧 `C:\CP6Backups\CP6_DEV` ACL，根 `cp6`、`CP6DB` 和命名卷未变更。
- 修复 `sqlcmd` 只存在于交互用户 PATH、服务 Agent 可能找不到的问题：备份脚本与 Readiness YAML 兼容 PATH、Go sqlcmd、ODBC 18/17 标准目录；7 场景行为回归覆盖发现/失败分支和 `SQLCMDPASSWORD` 恢复，三组合同测试同步通过。
- Azure CLI/DevOps 扩展已装入当前用户目录；本条保留当时的审计状态。同日后续已完成设备登录、`cp6_dev_backup`、Azure Secret/Exclusive lock/变量、Readiness 重跑及首次真实发布，以上方外部闭环记录为当前事实。

## 2026-08-25：本机 DEV 双模式发布闭环

- 把 Azure DEV CD 收敛为同一条自动/手动 Pipeline：验证成功 main CI、在分类阶段和 DEV 锁内跳过 superseded 自动任务、关闭自动后才允许旧版本手动回退，并从所选提交的隔离 worktree 构建完整 SHA 镜像、捕获不可变 Docker image ID。
- 发布先对 `CP6_DEV` 执行 COPY_ONLY/COMPRESSION/CHECKSUM 备份和 RESTORE VERIFYONLY，再停止旧 API/Web、前向迁移、逐层启动与核对 release identity；证据记录触发、备份 SHA-256、镜像和健康结果。
- 新增只连接 `cp6-dev_default` 的独立 Cloudflare connector，以及只允许 `CP6DEV_IMPORT_*` 新库的 DEV 快照导入工具；根 `cp6`/`CP6DB`/`cp6_cp6-db-data` 保持隔离。外部 Azure Run 与 Tunnel 切换未在本次执行。

## 2026-08-24：登录体验恢复与可访问性闭环

- 恢复面向包装制造运营的五语言登录体验和桌面/移动布局，同时保留既有账号密码、Tenant、SSO、2FA、菜单和路由合同。
- CSS 折叠 Tenant 输入现以 `inert`/`aria-hidden` 退出键盘和辅助技术导航，展开及 `needTenant` 会聚焦正确输入；语言选择器使用按钮组语义，密码与 SSO 流程互斥。
- 删除无健康检查支撑的实时“系统正常”宣称，改为中性安全访问标识；组件 10/10、Web 176 文件/902 测试、Vue 类型检查、production build 和 Chromium 桌面/移动验收通过。

## 2026-08-24：Kafka 生产者安全退出修复

- 将 Kafka Singleton 的限时 Flush 与 producer Dispose 拆成独立失败边界，确保刷新异常后仍释放底层 handle，并通过幂等门防止重复关闭。
- 关闭异常继续保持旁路语义，不让 WebApi Host 因 Kafka 清理失败而退出失败；同时对刷新异常、释放异常和 5 秒后剩余消息数记录 Warning，替代旧 WIP 的静默吞错。
- 新增 4 个生命周期回归；聚焦 4/4、`CP6.Tests` 全量 2,938 passed / 19 skipped / 0 failed。

## 2026-08-24：日期时间规范化恢复与 P4/P5 决策

- P4 经最新 `main` 干净类型检查确认无需恢复 `env.d.ts` 的通配 `*.vue` 声明；当前 Vue/TypeScript/`vue-tsc` 工具链原生处理 SFC，旧 `any` shim 仅保留在分支整顿归档。
- 恢复共享日期时间格式化与 Element Plus 单元格适配器，统一 OA/PMS/WMS/Space 及两个通用列表组件的 datetime 输出，避免直接暴露高精度 .NET ISO 字符串。
- P5 将全局 `long` 合同锁定为普通业务 UI 的日期 + 时:分，不再全局显示秒或 `.sss`。五语言回归、Web 175 文件/892 测试、Vue 类型检查和 production build 通过。

## 2026-08-24：白天临时家庭测试服务器流程

- 新增 `cp6-daytime-server.bat` 与 PowerShell 控制器，提供复用镜像启动、重建启动、状态检查、仅关闭公网 Tunnel 和安全停止全栈；启动前失败关闭检查 Docker、Compose、`.env` 及 Cloudflare Tunnel 配置/本机凭证。
- 停止流程只使用 Compose `stop` 并保留容器和命名卷；关闭公网只影响 `cp6-cloudflared`。没有加入 Windows 防睡眠、电源计划、计划任务或自动结束主机 cloudflared 的行为。
- 新增静态合同测试；实机只读验收确认 7 个 Compose 服务就绪，本机及公网 Web/API 均返回 HTTP 200。当前运行环境未被重启或重建；`estimate` Worker Git 集成仍为独立外部待办。
## 2026-08-24：CRM V1 产品需求草案

- 新增 `docs/crm/CRM-V1-PRD.md` v0.1，按 Frozen SaaS V1 长期范围和 Lead Pilot 首个切片，定义 CRM 前端效果、后端命令/状态、数据主权、权限/PII/Entitlement、失败恢复、验收和升级接口。
- 更新 CRM 文档入口与项目状态，明确私有 CRM 仓目前为 docs-only，当前 `main` 仍只有 Foundation；产品草案不冒充已实现功能。
- PRD 状态为 Draft。Public Contract Sync 仍 Pending，M0 仍 No-Go；必须先完成产品评审和治理门禁，才可拆实施票。

## 2026-08-24：Space GA 退出码假红修复

- 修复 Attestation、Pilot、Golden CAD、Kickoff 和人员种子五个负向套件在断言全绿后仍向 GitHub Actions 泄漏末个预期失败子进程退出码 `1` 的问题；根因是 PowerShell 全局 `$LASTEXITCODE` 未在负向用例完成断言后清除。
- 五个套件均新增汇总前退出码回归断言；只清除已经被测试消费的子进程状态，不放宽任何证据错误码或核心 GA `NoGo` 校验。
- Actions 风格直接调用与独立进程调用均为 Attestation 36/36、退出码 `0`；完整 Space GA 顺序验证为 36/36、21/21、31/31、28/28、8/8，所有进程退出码均为 `0`。

## 2026-08-24：仓库分支整顿与 WIP 当前-main恢复

- 以 `main@0a14581f` 为基线完成分支审计；整顿前 105 refs、脏 worktree patch、原始未跟踪文件和校验数据已本地归档。
- 远端删除 61 个已合并分支和 9 个归档型陈旧分支，关闭 PR #3；旧本地 worktree/分支已清理，根工作区恢复为干净 `main`。
- 旧根目录 WIP 被拆为登录体验、日期时间规范化、Kafka Dispose 三个独立当前-main分支并推送；每个分支只保留单一职责和独立验证证据。
- CRM Draft PR #7/#8 合并当前 `main` 后继续保留 Draft；PR #8 公共契约验证已恢复为绿色。仓库可见性保持 Public，未执行生产部署。
- 整顿记录经 PR #9 合并为 `main@2abf451d`；随后启用并回读 `main` 严格保护，要求 PR、最新主线、三个常驻检查及对话解决，管理员不得绕过，force-push/删除关闭。
- 详细恢复与后续开发边界见 `docs/project-memory/11-Branch-Consolidation-20260824.md`。

## 2026-08-16：Space Tenant 私有整仓模板

- 新增当前租户整仓模板与不可变版本持久化，保存规范化计划、内容 SHA、计数、审计、租户内唯一编码及复合租户外键；System 模板保持代码内置只读。
- Design V1 新增幂等创建，目录/密封 Preview/Lease+双 Revision 逐层 Apply 同时支持 System/Tenant；服务端验证父链、尺寸、命令和库位上限，跨租户模板 ID 猜测失败关闭。
- Space Studio 合并显示系统与租户私有模板，切换模板后不会误用前一个密封 Preview。全量 Space Integration 456、Space Unit 549、CP6.Tests 2,934、Web 884、Space Studio Playwright 26，以及 OpenAPI/权限 96、EF/SDK drift、Vue TypeScript 和生产构建均通过。
- Tenant 模板持久化纵切闭环；模板制作表单、四模式统一向导和 Template 创建来源仍 Pending，LM-FR-001/WP1 与 GA 保持 Partial/Pending、72% / `NoGo`。

## 2026-08-16：Space Studio 历史 CAD 审核结果目录

- Design V1 新增 Version/Floor 级 CAD Review Candidate 目录，按持久 Parse Payload 的 Base Content Revision/Hash 判定新鲜度；当前且 Artifact 完整的结果可加载，旧结果只允许重新解析。
- Space Studio 用户可从来源面板选择已有结果，无需填写 SourceId/JobId；切换候选会清理旧 CAD/Excel/Preflight/Match 状态，只读用户可查看但不能触发重新解析。
- Space Integration 15、OpenAPI/权限 95、双 SDK drift、Web 882、Space Studio Playwright 26、Vue TypeScript 与生产构建通过。该纵切关闭历史候选目录的仓库 UI 边界，不替代真实 Provider/文件/WMS/Pilot；WP4 与 GA 保持 Partial/Pending、72% / `NoGo`。

## 2026-08-16：Space Studio 当前 CAD + Excel 统一工作流

- 来源模式接通当前新鲜 CAD Review Workspace → `.xlsx` 上传/扫描等待 → 服务器 Mapping Profile → Excel 预检 → 显式确认 → 权威匹配 → 既有 Lease/Revision Apply，无需用户填写内部 ID。
- Excel Source/Preflight Job 写入 URL 支持刷新恢复；Blocking 预检失败关闭，匹配 Job 自动轮询，删除 CAD/Excel 来源会同步清理依赖路由状态。确认 Apply 前 Draft 零写入。
- Web 878、Space Studio Playwright 25、Vue TypeScript 和生产构建通过。当前工作会话 UI 已闭环，历史 CAD 候选目录已由同日后续纵切补齐；真实 Provider/文件/WMS/Pilot 仍 Pending，WP4 与 GA 保持 Partial/Pending、72% / `NoGo`。

## 2026-08-16：Space CAD 待审变更集与 RuleOnly 交接

- LM-FR-019/019A 深审闭环：六类变更独立汇总/筛选，客户端验证 Change Summary 与选择语义，Workspace 更新不再沿用旧选择。
- CAD 静态元素专用 Apply 上限提高到 10,000，仍复用租约、Floor/Content Revision、幂等与单事务；101 项纵切证明一次 Revision。公开手工命令的 100 项边界未放宽。
- Zone/Aisle/Rack 保持设计态领域模型，通过审核面板显式交接既有 RuleOnly → Proposal Review → Atomic Apply，并预选当前 CAD 来源，未新增第二套布局权威。
- Space Integration 15、Space Unit 546、CP6.Tests 2,933 passed / 19 environment-skipped、Web 873、OpenAPI 55、Space Studio Playwright 24、生产 Web 构建、完整 solution Release 0 warning / 0 error 与 SDK drift 通过。仓库闭环不替代真实 Provider/黄金 CAD/Pilot；核心 GA 仍为 72% / `NoGo`。

## 2026-08-16：Space CAD 输入与坐标确认

- LM-FR-010 明确以同一受控上传链接受 DWG/DXF：前端显式格式、服务端扩展名/MIME/签名校验、隔离扫描和 CAD IR/Preparation/Parse 权威不变。
- LM-FR-011 起始向导新增自动建议单位、mm 比例、原始图纸 X/Y/宽高、自动换算毫米范围、异常状态与原因展示；两类人工确认及 Preview 失效机制保持失败关闭。
- Space Unit 546、Web 869、Vue TypeScript、生产构建与完整 solution Release 0 warning / 0 error 通过；AutoCAD 2025 Core Console 真实 DWG 开发合同用例 1/1 通过。LM-FR-010～011 仓库实现闭环，但生产主备 Provider 与核心 GA 仍为 Pending、72% / `NoGo`。

## 2026-08-16：Space CAD 语义与质量诊断

- 复核并关闭 LM-FR-014/015 的仓库口径：七类核心 CAD 语义及逐提案 SourceRef、规则、置信度、位置均继续由同一 Semantic Preview/Diagnostic Index 提供。
- LM-FR-016 新增零尺寸、无法闭合和实际面积重叠的稳定问题代码；楼层越界在既有全图 Blocking 之外追加逐对象 SourceRef，并经 Preparation/OpenAPI/双 SDK 展示到 CAD 起始向导。
- Space Unit 544、CAD Preparation/Parse/BuildScene/Excel 集成聚焦 37、CAD 实验工具常规门禁 39 passed / 1 个安装环境用例 skipped、OpenAPI 55、CAD 向导 4、CP6.Tests 2,933、完整 solution Release 0 warning / 0 error，以及配置安装环境后的 AutoCAD 2025 Core Console 真实 DWG 1/1 通过；LM-FR-014～016 仓库实现闭环，WP4 与核心 GA 仍为 Partial/Pending、72% / `NoGo`。

## 2026-08-16：Space 租户私有 CAD Mapping Profile

- Design V1 新增 CAD Mapping Profile 管理权威：系统版本只读，租户复制后以 RowVersion、幂等键和 append-only 版本表保存规则快照、Definition SHA-256、复制来源与审计。
- Preparation Catalog 自动合并 System 与当前租户 Profile；跨租户读取/复制失败关闭。CAD 起始向导新增结构化规则管理、启停、复制和追加版本，无需手填 Profile ID/Version。
- 新增可回滚 EF 迁移、OpenAPI/双 SDK、权限与管理 UI 自动化；Space Unit 540、真 SQL Space Integration 453（0 skipped）、CP6.Tests 2,933、Web 866、生产构建和完整 solution Release 0 warning / 0 error 通过。
- LM-FR-013 仓库实现闭环；WP4 与核心 GA 仍为 Partial/Pending、72% / `NoGo`。

## 2026-08-16：Space CAD 图层/块审核与逐层 Override

- Design V1 CAD Preparation Preview 新增审核清单，向导可查看和搜索图层名称、颜色、线型、可见性、对象计数，以及块定义、引用和属性引用计数。
- 映射 Profile 选择器明确 System/Tenant Scope；逐图层可沿用 Profile、忽略或覆盖语义目标、几何规则和置信度。输入或 Override 变化会使旧 Preview 失效，重新预览后才允许启动 Parse。
- OpenAPI、C#/TypeScript SDK 和前端类型同步；Space Unit 540、真 SQL Space Integration 447（0 skipped）、CP6.Tests 2,932、Web 863、类型检查、生产构建和完整 solution Release 0 warning / 0 error 通过。
- LM-FR-012 仓库实现闭环；LM-FR-013 的 Tenant 私有 Profile 持久化/管理仍待完成。WP4 与核心 GA 仍为 Partial/Pending、72% / `NoGo`。

## 2026-08-15：Space 来源移除引用预检

- Design V1 新增来源移除预检和确认 Apply；活动任务、生成、底图及当前设计引用会阻断，历史 Job/工件/问题/标定/导入审计明确保留。
- Apply 绑定 ContentRevision、Source RowVersion、Idempotency-Key 与 Serializable 事务；确认只软删除来源，物理文件继续由 Retention/Tombstone 权威管理。
- 工作台来源面板、稳定 `SPACE_SOURCE_REFERENCED`、OpenAPI、C#/TypeScript SDK、权限与外部主体边界同步；全量门禁为 Space Unit 540、Space Integration 真 SQL 447（0 skipped）、CP6.Tests 2,932、Web 862，EF、production build 和完整 solution Release 0 warning / 0 error。
- LM-FR-005 仓库实现闭环；WP4 与核心 GA 仍为 Partial/Pending、72% / `NoGo`。

## 2026-08-15：Space 上传重复内容复用提示

- CAD 上传前端合同不再丢弃服务端 `Reused`；CAD 与 PDF/图片底图重复内容会明确提示按 SHA-256 复用受控文件或当前来源。
- 客户端不计算权威哈希、不跳过扫描；重复底图继续执行同一 Clean/Scanning/Rejected 与挂接流程。
- 聚焦测试 10、Vue TypeScript、Web 全量 858 及 production build 通过。该条记录时缺失的当前 CAD + Excel UI 已由 2026-08-16 后续纵切闭环；LM-FR-005 已由后续来源移除预检纵切闭环，WP4 和 GA 72% / `NoGo` 不变。

## 2026-08-15：Space Draft 来源与阻断摘要

- Design V1 Version 合同新增来源、创建者、创建/更新时间和 Open Blocking 数量；列表和详情使用同一聚合语义，Open Blocking 不包含已解决问题或 Warning。
- Space Studio 活动 Draft 摘要直接展示这些字段，历史创建者缺失时明确显示系统/历史数据，不伪造人员姓名。
- Space Integration 真库 444、Space Unit 537、CP6.Tests 2,926、Web 856 及 OpenAPI/双 SDK/EF/生产构建通过，完整 solution Release 0 warning / 0 error。
- 当前 Blank/PublishedVersion 创建路径的 LM-FR-002 摘要缺口关闭；System/Tenant Template 创建来源仍随四模式向导处理，LM-FR-001/WP1 和 GA 72% / `NoGo` 不变。

## 2026-08-15：Space System 整仓模板按楼层写入 Draft

- Design V1 新增模板楼层 Apply：服务端从不可变 System 模板确定性生成 Zone/Aisle/Rack/逐层规格/Location，以 Site、Proposal Hash、Lease、双 Revision 和 CommandBatch 失败关闭；一个模板楼层一个 Serializable 原子事务。
- Space Studio「构件」面板接通密封预览、模板楼层选择、数量确认、只读保护和状态未知时的原批安全重试；完成前 Draft 零写入，成功后继续由同一 Design Scene 驱动 2D/3D。
- 标准 F1 真库验证 3 区、10 巷道、250 货架、1,250 层定义和 5,000 库位；Space Unit 537、Space Integration 真库 443、CP6.Tests 2,925、Web 856 及 OpenAPI/双 SDK/EF/生产构建通过。
- Tenant 私有模板和四模式统一创建向导仍未完成，LM-FR-001/WP1 保持 Partial/Pending，GA 72% / `NoGo` 不变。

## 2026-08-15：Space System 整仓模板目录与预览

- Design V1 新增 System/Tenant 整仓模板目录和实例化预览合同；首份 System 模板从确定性标准仓布局生成，固定 2 层、7 区、20 巷道、500 货架和 10,000 库位。
- 预览密封模板/版本/内容/Proposal SHA 与完整父级计划，明确 `writesDraft=false`；外部主体、旧版本、未知模板及非法 scope 失败关闭，OpenAPI/双 SDK/前端目录同步。
- Space Unit 536/536、CP6.Tests 2,924、Web 851/851 与完整 solution Release 0 warning / 0 error；SDK/EF/GA 证据门禁通过。
- Tenant 私有模板、Template → Draft Apply 和四模式统一向导未完成，LM-FR-001/WP1 仍为 Partial，GA 72% / `NoGo` 不变。

## 2026-08-15：Design V1 Floor shell 与项目入口

- Space 首页新增 Site 级 Space Studio 入口，可发现活动 Draft、列出/选择活动设计楼层；没有 Draft/Floor 时分别显式创建 Blank 与 Floor shell。
- 新 Floor 合同要求全部业务字段、Expected Content Revision 和 Idempotency-Key，使用 Version 级 SQL application lock 与 Serializable 事务提交；创建后进入既有 Floor Lease 工作台。
- 真 SQL 聚焦 4/4、Space Unit 534/534、Space Integration 真库全量 441/441、CP6.Tests 2,923 通过、Web 全量 848/848，并通过 OpenAPI/双 SDK/EF/GA 证据门禁、类型检查与生产构建；完整 solution Release 0 warning / 0 error。整仓 System/Tenant 模板和四模式统一向导仍缺，LM-FR-001/WP1 为 Partial，GA 72% / `NoGo` 不变。

## 2026-08-15：Design V1 空白 Draft 初始化

- 版本创建接口新增 `Blank` 模式：强制无基线、不继承线上快照、不移动 Published 指针，并保留唯一活动 Draft 约束。
- 新增可审计的完成态初始化 Job/Attempt；Operation fence、请求 Hash、SQL 事务和 Idempotency-Key 关闭重复或异参重放。
- 领域聚焦 7、真 SQL 聚焦 2、Space Integration 真库全量 437 通过且 0 skipped。该纵切当时未创建楼层；楼层初始化/选择随后由独立纵切补齐，平台/租户整仓模板仍待实现。LM-FR-001/WP1 为 Partial，GA 72% / `NoGo` 不变。

## 2026-08-15：Space Studio LM-FR-025～029 最终工作台 UX 要求

- 2D 未保存重画现在可跨 3D 切换保留点集、选择和标题标记；3D 禁止误提交，回到 2D 可继续完成。既有同源场景与逐楼层相机恢复不变。
- 首次四步清单补齐 44px 展开热区、焦点环及符号/可访问完成状态；问题严重度筛选补齐热区和 Blocking/Warning/Info 自动化。
- Web 843、Space Studio Playwright 23、production build 与完整 Release solution 通过。LM-FR-025～029 仓库实现闭环；WP4/WP5 接受状态和 GA 72% / `NoGo` 不变。

## 2026-08-15：Space Studio 两点实距标定工作流

- 底图标定改为 P1 原点、P2 比例点和独立验证点 V 的明确流程；用户填写真实距离、P1 世界原点、旋转与 V 世界坐标，工作台生成 P2 世界毫米坐标。
- 预览与保存统一使用整数世界坐标，展示第三点误差和 `max(50mm, 实距×0.2%)` 阈值，超限时禁止提交；原有 Lease/Revision/幂等与可逆历史权威不变。
- Web 841、Space Studio Playwright 23、production build 与完整 Release solution 通过。LM-FR-021 仓库实现闭环，WP4 和 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio 托盘与静态设备构件库

- 构件库补齐墙/柱/门/月台、托盘和输送线、AGV、叉车、工作台、电子秤、充电站，预设固定领域类型、尺寸、业务编码前缀与 Design 属性。
- 六类设备明确标记 `runtimeBehavior=Static` 和设备子类，不混入实时运行态；创建继续复用 Design V1 Lease/Revision/Hash/幂等 Fence 并进入公共撤销/重做。
- Web 837、Space Studio Playwright 23、production build 与完整 Release solution 通过。LM-FR-022 仓库实现闭环，WP4 和 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio 底图图层控制

- “图层”模式接通底图显示/隐藏、透明度和锁定控制，直接驱动真实 Konva 画布；锁定阻止标定，新挂接自动解锁、标定成功自动锁回。
- 底图显示偏好随现有 floor view schema v1 按版本/楼层保存在浏览器标签页，旧数据兼容，非法状态失败关闭且不推进 Draft Revision。
- 单测、类型检查与 Playwright 已覆盖实际画布变化和重载恢复。LM-FR-020 仓库实现闭环，WP4 与 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio 底图统一撤销/重做

- 底图挂接、替换、标定和移除统一进入 Lease、Floor/Content Revision、数据库 UTC、CommandBatch 与幂等 Fence；Attach 合同以必填但可空的 `sourceId` 表达显式移除。
- 服务端以不可变 Command Record 密封 Source/Calibration/变换前后态，Undo/Redo 复核历史 Hash 和当前状态后写新补偿批次；工作台接入公共历史栈并可恢复替换前的旧标定。
- 真 SQL、OpenAPI/双 SDK、Web 和 Playwright 已覆盖。LM-FR-024 仓库实现闭环，但 WP4 与 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio Excel–CAD 确认统一撤销/重做

- Excel–CAD Apply schema v2 以不可变 Command Record 密封历史 Hash/数量；服务器而非客户端持有 Rack、层、库位、绑定、属性和 Source 的可信前后态。
- 新增受 Lease、Floor/Content Revision、内容 Hash、当前状态、原工件链和幂等键保护的 Undo/Redo 补偿端点；每次补偿生成新的不可变审计批次，介入编辑与旧 v1 历史均失败关闭。
- 工作台接入共享历史栈，OpenAPI/双 SDK、真 SQL、Web、Playwright 与 Release build 门禁通过。LM-FR-024 只剩底图挂接/标定可逆合同，WP4 与 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio Excel–CAD 确认 Lease/Revision Fence

- Excel–CAD 确认合同新增必填页面实例、编辑租约和 Floor Revision，并保留 Content Revision；工作台无当前自有租约时只允许审阅。
- 确认入队和 Worker 实际写入都在统一 Floor 锁内重新验证同一租约；SQL Server 使用数据库 UTC。会话更换、租约释放/过期或 Revision 漂移均零 Draft 写入，旧未完成无租约 payload 不再执行。
- OpenAPI/双 SDK、后端、前端和 Playwright 聚焦门禁通过。该项只是 Excel–CAD 统一撤销/重做的安全前置条件，LM-FR-024、WP4 与 GA 72% / `NoGo` 状态不变。

## 2026-08-15：Space Studio CAD 确认批次撤销/重做

- CAD Typed Changeset Apply 响应新增服务器密封的 undo/redo 命令；Create/Delete 使用稳定 LogicalId 的 Delete/Restore，Modify 使用命令提交前后的完整属性快照。
- 通用 Element Command 幂等响应保存首次修改前态；工作台验证历史数量和命令白名单后接入既有统一命令栈，异常历史保护性切换为只读。
- CAD、真实 LocalDB、OpenAPI、Space Unit、Web、Playwright、构建和 SDK 漂移门禁通过。LM-FR-024 仅完成 CAD 纵切；Excel–CAD 确认及底图挂接/标定仍待完成，WP4 与 GA 状态不变。

## 2026-08-15：Space Studio CAD 人工校正锁定

- CAD 来源通用元素新增持久人工校正锁、单调版本、最后操作者和 UTC 时间；锁定/解除锁定继续走现有 Lease/Revision/幂等 `UpdateProperties` 原子批，锁定后的人工编辑递增版本。
- 重新解析命中锁定 SourceRef 时只生成不可应用的 Blocking Conflict，审核空间可定位并显示版本；CAD Changeset 最终 Apply Fence 返回稳定 `SPACE_CAD_MANUAL_CORRECTION_LOCKED`，防止绕过 UI 覆盖。
- 加法迁移、版本克隆、OpenAPI/双 SDK、Space Unit 533、Web 809、Playwright 20、真实 LocalDB 1、CAD reparse 1、Release build 与 EF 模型门禁通过。LM-FR-018 仓库实现闭环；WP4 仍为 Partial/Pending，GA 保持 72% / `NoGo`。

## 2026-08-15：Space Studio 对象复制

- 批量检查器新增 1–100 个 Active 通用元素/货架复制，并允许在一个 Design V1 原子命令批中混合 `CreateElement` 与 `GenerateRackArray`；确认前零 Draft 写入。
- 元素副本清除唯一 BusinessCode、业务链接和 CAD 来源但保留设计几何/属性；货架副本复制 Active 层及 Generated/Unbound 空编码库位。撤销/重做只 Delete/Restore 原新 LogicalId，不重复创建。
- 复制聚焦、前端全量、真 SQL 混合批、Playwright、Space Unit、OpenAPI、类型检查、构建、SDK drift 和 GA 自测通过。LM-FR-023 仓库实现闭环；WP4 仍为 Partial/Pending，GA 保持 72% / `NoGo`。

## 2026-08-15：Space Studio CAD 异常对象画布重画

- 工作台新增单个 Active 非资产通用元素的 2D 多边形重画；本地绘制在显式确认前零 Draft 写入，3–100 点、重复、零面积、自交和 Int32 包络均失败关闭。
- 保存、撤销和重做复用同一 LogicalId 的 Design V1 `UpdateProperties`，继续受 Lease/Revision/Content Hash/幂等/审计保护，并保留类型、业务链接、属性及 CAD 来源；2D/3D 消费同一多边形。
- 聚焦 Web 6、全量 Web 800、Space Unit 531、OpenAPI 44、真实 LocalDB 1、Space Studio Playwright 18、type-check、production build、完整 Release solution 和 SDK drift 通过。LM-FR-017 五项仓库能力已闭环；WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。

## 2026-08-15：Space Studio CAD 异常对象拆分

- 工作台可把一个 Active 非资产组合元素拆成 2–100 个独立元素；首部件保留当前 LogicalId，其余部件分配新 LogicalId，并继承类型、父级、业务链接、设计属性及 CAD 来源。
- 拆分、撤销和重做使用同一 Design V1 Lease/Revision/幂等原子批；重做通过 Restore 保持已分配身份，不重复 Create。组合整体旋转/移动后的坐标与 2D/3D 渲染器保持一致。
- `CreateElement` 以可选成对字段补齐业务链接继承，OpenAPI 与双 SDK 同步。Space Unit 531、Web 794、Playwright 17、真 SQL 1、Release build、production build 和 SDK drift 通过；重画已由后续独立纵切关闭，WP4 保持 Partial，GA 保持 72% / `NoGo`。

## 2026-08-15：Space Studio CAD 异常对象合并

- 新增 `schemaVersion=1/kind=group` 的受限组合几何，逐部件保留来源身份和原始几何；限制 100 部件、8 层嵌套并拒绝资产子几何。
- 工作台可显式合并 2–20 个语义和属性一致的通用元素，保留首选 LogicalId；正向与撤销分别复用现有 Design V1 原子命令和补偿命令，2D/3D 均消费同一组合几何。
- Space Unit 531、前端 788、Space Studio Playwright 16、真实 LocalDB 1、完整 Release solution、production build、SDK drift 和 GA 证据自测 36 均通过；无数据库 Schema 或 OpenAPI 变化。拆分与重画已由后续独立纵切关闭；WP4 保持 Partial，核心 GA 保持 72% / `NoGo`。

## 2026-08-15：Space Studio CAD 异常对象改类型

- `UpdateProperties` 现在可在同一 Design V1 命令批内改变通用元素语义类型，保留 LogicalId 并继续使用 Lease、Revision、幂等及审计 Fence；资产实例和未知类型失败关闭。
- 工作台属性检查器、撤销/重做、OpenAPI、C#/TypeScript SDK 与自动化同步；真 SQL、领域、契约、前端和 Playwright 聚焦门禁通过。
- 详细 Spec LM-FR-017 的删除已有实现，合并、拆分与重画由后续独立纵切关闭；WP4 从过宽的 Complete 校正为 Partial，核心 GA 仍为 72% / No-Go。

## 2026-08-15：Space AutoCAD Core Console 开发转换链

- 新增实验型 `ICadConverter`/`convert-autocad-dev-ir`，通过显式本机 Core Console 路径将 DWG 导出为 DXF，并继续进入既有确定性 CAD IR 和共合同执行器。
- 转换绑定原始 DWG SHA 与 Core Console 文件版本，原始/中间文件只在 D 盘唯一 `attempts` 目录存在；Activity Insights 持久运行包进入拒绝 DWG/DXF 的独立缓存，子进程无 Shell并可超时取消。
- 签名有效的 Core Console 本机测试 1/1 通过，Floor Plan 样例两次 CAD IR SHA 一致；该链未获 Site/客户/法务批准、无主备评分且 GUI 签名仍需修复，因此 GA 保持 72% / `NoGo`。
- GA 总索引日期同步到 2026-08-15，WP0 新增仓库完成度审计，WP3 新增 AutoCAD 开发报告；四个 GA 校验器通过共享 JSON 兼容层适配 PowerShell 7.6 的日期自动转换，并保持 5.1 严格语义。状态仍为 `Partial/Pending`，没有生成或接受正式 Provider 证据。

## 2026-08-15：Space Studio 单人开发人员种子

- 增加 `00001`～`00005` 五个开发虚拟人员，供一名真实开发者执行本地角色切换、权限矩阵和任务归属；人员册明确为 `DevelopmentSeed`，无生产访问或正式签字资格。
- 新增专项校验与 CI 门禁，并加固总 GA/开工人名校验，纯数字及开发/测试身份不能成为正式 Owner、接受人或签字人。
- 该变更不创建登录凭据，不把单人开发冒充 2+2+1 团队或五方批准，Space Studio 核心 GA 保持 72% / `NoGo`。

## 2026-08-14：Space Studio M0 外部输入失败关闭

- 新增 M0 开工 Manifest、模板、协议和机器校验器，将实名签字人、2+2+1 团队、20 CAD 候选、Provider/隔离 Worker 审批、双仓/WMS 窗口从泛化附件提升为五个可独立关闭的结构化分区。
- 总 GA 索引为每个外部输入增加 `verificationManifest`；Complete 时必须由输入证据证明 Manifest 自身哈希，通过对应语义校验，并保持 Owner/签字人登记一致。专项 26/26、组合证明链 34/34。
- 本改动没有填写任何真实人名、CAD、Provider、Worker、Site 或 WMS 窗口；GA 保持 72% / No-Go，防止用模板、fixture 或一份汇总说明冒充开工输入。

## 2026-08-14：Space Studio 正式黄金 CAD 证据失败关闭

- WP7 新增结构化 Manifest、模板、协议和机器校验器，把授权 20 份、10/5/5、L1～L5、DWG/DXF、标注/仲裁、主备同源冻结链、离线质量/Holdout/性能报告收敛为一条可验证证据链。
- 总 GA 校验器只在授权 CAD、Provider/Worker 输入和 WP3 已完成后允许 WP7 Accepted，并要求 Gate 哈希证明 Manifest 本身；模板、fixture 和语义不合格报告均拒绝。专项 31/31、组合证明链 29/29。
- 该项只关闭证据结构与误报，不代表真实 CAD、Provider 或 Worker 已交付；GA 保持 72% / No-Go，5 类外部输入、9 个 Gate、5 个签字仍 Pending。

## 2026-08-14：Space Studio 双仓 Pilot 证据失败关闭

- WP8 新增最终 Pilot Manifest 合同、模板、协议和机器校验器，覆盖 Greenfield/Retrofit、连续 14 天、逐日记录、S3 可用绕行/缺陷关闭、100% 一致性、恢复 SLO、Published 边界和两类现场实名确认。
- 总 GA 校验器在 WP8 Accepted 时强制要求五方内部签字完成且签字接受人与登记姓名一致，以及被 Gate 自身哈希证明的结构化 Manifest；空模板、Manifest/嵌套证明中的测试 fixture、未来窗口、缺日/重复日或 Pilot 结束前预签均拒绝。专项 21/21、组合证明链 23/23。
- 该项只关闭将来现场证据的结构与误报漏洞，不代表 Pilot 已执行；GA 保持 72% / No-Go，5 类外部输入、9 个 Gate、5 个签字仍 Pending。

## 2026-08-14：Space Studio 2D 画布拖动精调

- Rack/Element 现在可直接拖动并按整数世界毫米保存；选中样式改为就地更新，不再在 pointerdown 销毁 Konva 节点，多选拖动与选择修饰键语义分离。
- 拖动和撤销均提交带 Lease、Floor Revision、Content Revision/Hash 与幂等标识的 `MoveObject`，失败恢复权威场景；Zone/Aisle 保持 Layout 领域权威。
- 前端全量 780、Space Studio E2E 14、拖动重复 5、type-check 和 production build 通过。独立 UX/Pilot 尚未发生，核心 GA 仍为 72% / No-Go。

## 2026-08-14：Space CAD Provider SQL Server 门禁

- SQL Server 17.0.4025.3 LocalDB 独立执行 `SpaceCadProviderSqlServerTests`，3/3 passed、0 skipped，关闭此前环境门控的 Provider 认证真库自动化。
- 覆盖并发配置替换、唯一 Current Revision、历史追加、认证不可变、迁移重复执行和旧资格/版本失败关闭；临时数据库已清理。
- 测试 Provider 仍是合同替身，真实 ODA/APS、授权黄金 CAD、冻结 Worker 和 Site 双链审批未发生；WP3 保持 Partial/Pending，核心 GA 保持 72% / No-Go。

## 2026-08-14：Space/WMS CP6.Tests 真库门禁

- SQL Server LocalDB 独立执行 SpaceSqlIntegration、WmsProductionSqlServer 和 IntegrationEvent UTC 回填集合，15/15 passed、0 skipped。
- 覆盖 Space 过滤唯一索引/换码/rowversion/SQL 翻译，WMS Move/Replenish/Serial/LPN/Feature Flag 事务及 Session applock。
- 全套 CP6.Tests 的两个 OA/PUR 共享 Stage 拒绝不属于 Space/WMS，未绕过或冒充通过；生产 CP6 WMS/SQL 接受仍为 Pending，核心 GA 保持 72% / No-Go。

## 2026-08-14：Space Studio 全量 SQL Server LocalDB 门禁收敛

- 完整 Space Integration 首次在 SQL Server 17.0.4025.3 LocalDB 真实执行，424/426 暴露两个此前被环境 skip 隐藏的问题；发布恢复查询已独立修复。
- Published Viewer 失败属于测试夹具先把版本发布、再追加楼层；夹具改为 Draft 阶段封存楼层后再发布，保留并证明 Published/Superseded 快照不可变保护。Scene SQL 7/7 通过。
- 最终完整复跑 426/426、0 failed、0 skipped。LocalDB 不替代生产等价 SQL/WMS/IdP/告警或 Pilot，核心 GA 保持 72% / No-Go。

## 2026-08-14：Space Studio WP6 发布恢复指标 SQL Server 翻译修复

- 真 SQL 全量门禁暴露恢复指标的复合键 GroupJoin 无法翻译；改为显式 TenantId/AttemptId/AttemptStatus 相关子查询，保持跨租户无标签聚合与 Audit 状态进入时间语义。
- 恢复指标单测 6/6、发布编排 SQL Server 3/3 通过，覆盖 WMS 首次超时、WaitingRetry、旧 Published 保持和正式重试完成。
- LocalDB 不替代生产等价 SQL/WMS；首次完整真库的独立 Viewer 场景随后已修复并取得 426/426，但仍不能声明核心 GA 完成。

## 2026-08-14：Space Studio WP3 CAD Converter 共合同执行器

- 新增供应商无关的 `SpaceCadConverterContractRunner`，把 Source 只读、流式 Sink 顺序/唯一性/计数/完成协议和 Result → 实际 Artifact 证明绑定为所有 `ICadConverter` 的共同执行边界；开发转换入口已迁移。
- 公共 CAD IR 校验补齐未定义枚举、负计数和规范 Artifact SHA 拒绝；适配器即使吞掉 Source 写入或 Sink 协议异常也会失败关闭。
- 验证通过：Runner/CAD IR 合同聚焦 23/23、Space Unit 525/525、CAD Experiment 34/34、完整 Release solution 0 warning / 0 error。
- 真实 ODA/APS、隔离 Worker、黄金 CAD、Site 双链审批与故障切换仍未发生，WP3 保持 Partial/Pending，核心 GA 保持 72% / No-Go。

## 2026-08-14：Space Studio WP0 GA 证据证明链加固

- GA 校验器现在对 Signed Signer、Complete External Input 和 Accepted Gate 重算仓库证据 SHA-256，并校验受控 HTTPS/CP6 URN、真实接受人及 ISO-8601 UTC 时间。
- 不存在/越界/哈希不一致文件、原始 DWG/DXF 仓库路径、不安全 scheme、占位/角色/团队人名和非 UTC/未来时间均失败关闭；新 GitHub Actions 运行当前索引和 16 个正反向自测。
- 没有任何真实 Owner、Provider、黄金 CAD、Pilot 或签字被填写；核心 GA 仍为 72% / No-Go。

## 2026-08-14：Space Studio WP3 CAD Provider 版本认证围栏

- Site 认证、运行时注册、能力查询、Preparation 输出和新 Parse payload v5 统一绑定 `ProviderVersion`；配置、路由与产物身份都要求 Key + Version 完全一致，避免同名 Worker 升级绕过原黄金集和审批证据。
- 评分工具输出的 Site 认证输入携带候选版本；CAD 向导显示主备版本，OpenAPI、C#/TypeScript SDK、新迁移和幂等 SQL 同步。历史认证只迁移为空版本并失败关闭，不猜测回填。
- 仓库测试覆盖版本错配零调用、输出漂移拒绝和封存解析版本漂移。真实 Provider、真 SQL 接受、20 份黄金 CAD、Site 审批和 Pilot 未发生，WP3 保持 Partial/Pending，核心 GA 保持 No-Go。

## 2026-08-14：Space Studio WP3 CAD 映射确定性重放快照

- CAD Preparation 新增服务器拥有、SHA-256 密封的 Mapping Replay Snapshot，保存不可变 Profile/Source/Inventory/Structure/Preview 身份和完整 Layer Overrides，修复后台 Parse 只有结果 Hash、没有重放输入的问题。
- 新 Job payload 升级为 v4；启动服务和 Worker 在入队/调用 Provider 前双重验证，v4 快照缺失或篡改失败关闭，历史 v2/v3 明确兼容。新增独立迁移、幂等 SQL 和聚焦测试。
- 本变更不实现真实 ODA/APS；真实适配器仍须取得冻结 Profile、重放并校验 Preview。WP3 保持 Partial/Pending，核心 GA 保持 NoGo。

## 2026-08-14：Space Studio WP3 Provider 评分与选型工具

- 新增严格、失败关闭的 `qualify-providers` 命令，将 ADR-0001 六维权重、80 分门槛、四项审批证据、同黄金集/冻结环境和唯一第一/第二名规则转换为可重复执行的选择报告。
- 只有 Pass 才输出受报告 SHA-256 绑定的一主一备 Site 认证输入；No-Go 不产生可写入认证。工具拒绝未知/重复字段、非法哈希/枚举、评分越界、门禁缺失、基线混用和并列名次，并且不写 Site、不接收 Secret 值。
- 工具聚焦测试 34/34 通过。真实 Provider、授权黄金 CAD、冻结 Worker、客户审批和目标 Site 认证均未发生，WP3 与核心 GA 保持 No-Go。

## 2026-08-14：Space Studio WP3 Provider 资格与确定性主备排名

- 扩展 Site Provider 认证合同，保存四项硬门禁、ADR-0001 资格分、规则版本、黄金集/冻结环境 SHA 和资格证据；新认证必须全部通过且总分至少 80。
- 服务端要求主备使用同一冻结评测基线，并拒绝 Primary 分数较低或最高分并列；能力接口公开非敏感资格状态，CAD 路由只消费资格完整记录。历史记录不自动升级，因此缺少新证据时按设计 No-Go。
- 新增独立可回滚 EF 迁移、幂等 SQL、OpenAPI/双 SDK 和聚焦自动化。真实 ODA/APS、黄金集评分、Site 审批、真 SQL 运行与双链认证仍未完成，不据此提升 WP3 接受状态。
- 验证通过：Release solution 0 warning / 0 error；Provider 聚焦 12、Space Unit 506、Space Integration 310 passed / 106 skipped、CP6.Tests 2,916 passed / 19 skipped、Client 71、Web 775、Space Studio Playwright 13、Vue type-check、production build、SDK/EF/GA 索引与 diff 门禁。环境 skip 未冒充正式证据。

## 2026-08-14：Space Studio WP0 核心 GA 证据索引

- 新增 `v1.3-ga` 核心 GA 索引，冻结 72%→100% 规则、5 类外部输入、WP0–WP8 九个 Blocking Gate 和产品/QA/WMS/架构/安全五方实名签字；实现状态、真实证据接受和正式签字分离。
- 新增 PowerShell 5.1/7 兼容校验器和 2 个自动化测试，拒绝删除门禁、缺失 Owner/证据、越界路径和不自洽的 `GaReady`。普通校验通过；`-RequireGaReady` 当前按设计返回 No-Go 退出码 2，因为 5 项输入、9 个门禁和 5 个签字仍 Pending。

## 2026-08-14：Space Studio WP5 生产 Viewer Published-only 边界

- 新增 Site 级 Published 聚合场景合同，只从模型当前 Production/Published 指针读取不可变 Design Revision，明确不内嵌库存、人员或设备 runtime overlay；无 Published 或权威漂移时返回稳定 Problem Details。
- 单层、跨层和 Control Tower 三条生产查看链统一消费该合同，旧可变 floor/scene API 仅保留给遗留编辑能力。客户端绑定 Site/版本/状态，按 RackLevel 权威生成可拾取 Location；Draft 注入、几何缺失与跨层部分失败均失败关闭。
- OpenAPI、C#/TypeScript SDK、权限、投影与结构守卫自动化同步。聚焦 Web 12、全量 Web 775、权限/OpenAPI 82、CP6.Tests 2,914、Space Unit 506、type-check、production build 和 SDK drift 通过；SQL 隔离测试因当前未配置 `CP6_TEST_SQLSERVER` skipped，生产等价 E2E、独立 QA/UX、Pilot 与签字仍未完成。

## 2026-08-14：Space Studio WP5 Viewer GA 性能复验

- 硬件门禁改为 1 次独立预热 + 30 次冷浏览器 Context，保存每次原始帧、标签、拾取、着色样本，并记录 P50/P95/最大值、失败率、代码 SHA、输入哈希、浏览器/OS/GPU 驱动与截图；样本不足、软件渲染、非 WebGL2、拾取 miss、console error、渲染器切换或脏跟踪工作区均失败关闭。
- 在干净提交 `bd206ff8`、Chrome 151、Intel Iris Xe 31.0.101.4502、ANGLE D3D11/WebGL2、1920×1080 上正式运行：30/30 成功、3,000/3,000 拾取命中、0 console errors；可交互 P95 62.3ms、帧 P95 8.2ms、拾取 P95 0.3ms、10,000 库位着色+渲染 P95 2.0ms、36 draw calls，全部通过冻结门槛。
- 证据算法 5、CPU 性能 1、Web 763、Vue type-check 与 production build 通过。该结果只关闭当前仓库 SHA 的 Viewer 性能门禁；Published-only 生产 Viewer、独立 UX/辅助技术验收、Pilot 与 GA 签字仍未完成。

## 2026-08-14：Space Studio WP6 外部主体控制面隔离

- Design V1 新增全局授权阶段主体过滤器，外部账号在功能权限、模型绑定和 Controller 之前被稳定拒绝；避免误授权限让 Customer、Supplier 或 3PL 触达 Draft、Source、Upload、Lease、Validate、Publish 或 AI。
- Published-only 外部门户成为唯一显式例外，反射守卫锁死例外集合；内部用户路径保持不变。聚焦矩阵 29/29 通过。
- 仓库自动化不替代真实 IdP、生产等价 SQL 跨租户、独立渗透测试和安全签字，本卡不等于 WP6 或核心 GA 完成。

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
