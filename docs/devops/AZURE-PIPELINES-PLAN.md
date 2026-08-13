# Azure Pipelines 演进计划

本计划把 Azure DevOps 从当前 CI 扩展到可追踪的 Release/CD。每一阶段都必须单独交付、验证和记录，不允许一次提交同时改完 Registry、DEV 和 PROD。

## 状态标记

- `[x]`：仓库或用户提供的 Azure 运行上下文已确认完成。
- `[ ]`：尚未实现或缺少可复现证据。
- `GATE`：未满足时不得进入下一阶段。

## Phase 1：CI 基线

状态：**已接入，待补强**。

- [x] GitHub 仓库已连接 Azure DevOps。
- [x] 根目录存在 `azure-pipelines.yml`。
- [x] 使用 `Default` self-hosted agent pool。
- [x] 配置 .NET 8、Node.js 22、后端/客户端测试和 Web 检查。
- [x] `main` 提交触发 CI。
- [ ] 记录 Azure Pipeline 成功运行 URL/Run ID 和 Agent 能力清单。
- [ ] 决定 PR 验证归属：启用 Azure PR trigger，或明确只依赖现有 GitHub PR 门禁；当前 `pr: none`。
- [ ] 比较 Azure CI 与 `client-contract.yml`/`wms-production-sql.yml`，登记未覆盖的 Space、OpenAPI/SDK、SQL、E2E、安全和 Android 门禁。
- [ ] 为 self-hosted Agent 定义更新、磁盘清理、离线告警、并发和工作区隔离规则。

`GATE`：Azure CI 连续运行稳定，失败能定位到具体门禁；任务分支仍按仓库规则经 PR/验证合入 `main`，不得恢复日常直接 Push `main`。

## Phase 2：发布权威与 Registry 决策

状态：**一般 CP6/Azure 路线待决策；CRM V1 决策已记录、批准待完成**。

CRM V1 由 [ADR-CRM-R00](./adr/ADR-CRM-R00-RELEASE-AUTHORITY.md) 固定为 GHCR/GitHub R2 唯一 Registry/候选权威。该选择不得在实现票中重开；R00 仍为 Proposed，M0 状态见 [CRM M0 就绪清单](../crm/CRM-M0-READINESS.md)。

- [x] 记录 CRM V1 唯一候选 Registry 为 GHCR，权威工作流为 GitHub R2。
- [x] 记录 Azure 在 CRM V1 中只可执行 CI、DEV 学习、非权威影子验证或消费同一 digest。
- [x] 区分当前 CP6 Schema 2 组件清单与未来 CRM 三仓 System Release Manifest。
- [x] 盘点现有 R2 与 CRM 目标能力，明确 OCI 签名、三仓清单、兼容范围、精确对象版本和分阶段采用证据 Gap。
- [ ] 取得 Release Owner、System Architect、Security Owner、SRE Owner 对 R00 固定 digest 的批准。
- [ ] 为 R00 候选前置 Gap 建立有 DRI/reviewer/验证命令的独立 P10/CRM12 任务；对象证据必须实现 content-addressed key、每版本 first-writer-wins、`VersionId` 固定、签名 `CandidateLocator` 根指针和精确版本读取。
- [ ] 把切换后的 Lead/Full Journey Adoption 作为 append-only `SystemReleaseEvidenceRecord` 关联候选 Manifest digest；不得把它设为候选前置或改写已签发 Manifest。
- [ ] 若一般 CP6/Azure 路线未来选择 ACR，另立 ADR 固定复制而非重建、影子期、切换/退出门禁和恢复时限。

Azure 影子链只能重跑验证或消费 R2 digest；它产生的源码构建物必须明确标为非候选且不可推广。禁止 GitHub 与 Azure 对同一版本分别 Build 并都声称正式候选。

`GATE`：CRM V1 必须先让 R00 获有效批准并关闭 M0；一般 ACR 迁移必须另有 Accepted ADR。任何路线都只能有一个 Registry、一个候选清单签发者和一个发布权威。

## Phase 3：Docker Release

状态：**下一实现阶段，尚未开始**。

- [ ] 新增独立 Release stage/job，只在完整 CI 和版本冻结通过后运行。
- [ ] API 使用根构建上下文与 `CP6.WebApi/Dockerfile`。
- [ ] Web 使用仓库根构建上下文与 `cp6.web/Dockerfile`，确保可读取版本化 TypeScript SDK。
- [ ] 传入 `RELEASE_VERSION` 与完整 `GIT_SHA`。
- [ ] 为两个镜像保存 SemVer、Git SHA 和 Pipeline 追踪标签。
- [ ] 生成 provenance、SBOM 和 High/Critical 漏洞门禁，至少与现行 R2 等价。
- [ ] Push 后读取并验证 Registry digest；候选清单保存 `repository@sha256:digest`。
- [ ] 发布测试结果、镜像元数据和安全报告，定义保留期。

实现注意：CP6 需要 Docker build arguments；不要直接使用会忽略 `arguments` 的 `Docker@2 buildAndPush` 组合模式。应拆成 build/push，或使用登录后的受控 Buildx 脚本。

`GATE`：同一 Pipeline Run 只生成一组 API/Web digest；从 Registry 可按 digest 拉取；扫描和候选清单验证通过。

## Phase 4：DEV 自动部署

状态：**待 Phase 3**。

学习环境旁路（不计入 Phase 4 生产门禁）：仓库已新增 `azure-pipelines-dev.yml`，在 `GTX537.CP6` 的 `main` CI 成功后，于唯一 `CP6-Deploy` Agent 上按完整 Git SHA 构建一次本机镜像并部署 `cp6-dev`。它用于练习 completion trigger、Variable Group、deployment job 和证据归档；没有 Registry/SBOM/签名，不能推广到 UAT/PROD-LAB，也不能把 Phase 3/4 标为完成。`cp6-dev-secrets` 已由 2026-08-11 外部截图确认创建；外部 `CP6 DEV CD`、三类资源授权和首次成功 Run 仍待验收。

- [x] 创建 `cp6-dev` Azure Environment。（2026-08-11 外部截图验证；Pipeline 权限仍待配置。）
- [x] 使用专用部署身份，不复用开发者 PC 的通用 CI 权限；`CP6-Deploy` Pool、`cp6_deploy_agent` 服务身份和 Readiness Run Build ID `10` 已验证。
- [ ] 配置外部 SQL Server、Redis、消息服务和 S3；不把它们塞进生产 Compose。
- [ ] 从候选清单读取 digest，不从源码重新 Build。
- [ ] 复用 `deploy/production/compose/compose.yaml` 与受控部署/验证脚本，或记录与其等价的新实现。
- [ ] 先执行 `db-init`，成功后启动 API/Web。
- [ ] 验证 live、ready、release、Web release identity 和最新迁移。
- [ ] 归档 DEV deployment evidence。

`GATE`：DEV 部署记录能回答版本、源码、digest、迁移、部署时间和验证结果；重复部署同一候选不会生成新镜像。

## Phase 5：UAT 与 PROD

状态：**待 Phase 4**。

- [x] 创建 `cp6-uat` 和 `cp6-prod-lab` Azure Environments。（2026-08-11 外部截图验证。）
- [ ] UAT 使用 DEV 验证过的同一 digest，记录业务验收证据。
- [ ] 在 `cp6-prod-lab` 资源上配置 Approvals and checks；单人学习期允许本人批准，真实生产禁止自批。
- [ ] 配置分支控制、允许的 Pipeline、超时、维护窗口和 exclusive lock。
- [ ] PROD 从同一候选清单部署同一 digest。
- [ ] 执行生产健康、发布身份、迁移和远程制品核对。
- [ ] 生成不可变部署记录与 Release Notes。

`GATE`：没有审批不能开始 PROD；审批后仍要通过部署和健康门禁；失败不得把发布标记为成功。

## Phase 6：回滚、灾备与运营

状态：**待 Phase 5**。

- [ ] 保存上一组 Schema 兼容的 API/Web digest。
- [ ] 证明应用回退与当前 Schema 兼容；数据库不执行 Down。
- [ ] 自动健康失败时停止流量切换，并运行受控应用回退或前滚修复流程。
- [ ] 演练 Agent 离线、Registry 不可用、db-init 失败、API 不健康和 Web 身份不匹配。
- [ ] 定义证据保留、告警、审计查询和发布复盘流程。

`GATE`：在非生产环境完成可复现演练并保存证据后，才可把自动回退描述为可用。

## Phase 7：AKS / 多仓推广

状态：**远期**。

- [ ] 在 Compose 试点稳定后使用 `deploy/production/kubernetes/`。
- [ ] 保持 digest、pre-deploy db-init Job、TLS、探针、PDB、资源限制和拓扑分散。
- [ ] 每个仓库独立重跑 Go/No-Go 与环境门禁。

## 当前下一张任务卡

**CRM 路线：关闭 M0 外部输入，不实施 Docker Release。**

1. 由受控项目系统冻结八类 M0 硬角色与 backup；System Architect/Platform/DBA/CRM/Design/QA 按 R00、DEC 或后续里程碑分别冻结；
2. 完成 R00、DEC-CRM-002–007 的有效批准和精确 evidence object identity 引用；
3. 完成 Observation baseline、Pilot cohort/task manifest、评价规则和证据合同；
4. 由 SRE/DBA/Security 审批 Azure SQL/Emergency Intake 的目标拓扑、账户/容量/身份、连续性合同、DRI、测试与真实环境/演练计划；M0 不要求资源已创建或演练已执行；
5. M0 全部 Approved 后，再在 P01 提供 runner/合同；此后才可由 CRM01-S01 创建 CRM 私有仓。

一般 Azure Release/ACR 演进保持独立任务，不得借 CRM V1 绕过 R00、重新选择 Registry 或直接实现 ACR Push。

## 相关文档

- [CI/CD 架构](./CI-CD-ARCHITECTURE.md)
- [发布流程](./RELEASE-PROCESS.md)
- [环境策略](./ENVIRONMENT-STRATEGY.md)
- [R2 签名与候选制品](../client/r2/02-signing-candidate-artifacts.md)
