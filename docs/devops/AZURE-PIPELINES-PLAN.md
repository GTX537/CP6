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

状态：**待决策，必须先于 Docker Release**。

- [ ] 决定唯一候选 Registry：继续 GHCR，或迁移到 ACR。
- [ ] 若选择 ACR，创建/审批 Azure Container Registry 和最小权限 Service Connection；不把凭据写入 YAML/Git。
- [ ] 决定 Azure 与 GitHub R2 的迁移模式：影子验证、候选复制或最终切换。
- [ ] 确定版本入口，默认沿用受保护 `vX.Y.Z` Tag 和当前 `main` 校验。
- [ ] 确定候选清单的唯一格式和存储位置，避免 Azure/GitHub 各产一份冲突清单。
- [ ] 写出 GitHub R2 退出条件和一键恢复旧发布链的时限。

建议：Azure Release 先以影子模式产出非生产候选并比较 digest、SBOM、扫描和清单；通过等价验收后再切换唯一权威。不要让两个系统对同一版本分别 Build。

`GATE`：Registry、候选清单和发布权威均有唯一答案，并通过安全/运维评审。

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

- [x] 创建 `cp6-dev` Azure Environment。（2026-08-11 外部截图验证；Pipeline 权限仍待配置。）
- [ ] 使用专用部署身份，不复用开发者 PC 的通用 CI 权限；`CP6-Deploy` Pool 与 `cp6_deploy_agent` 服务身份已 Online，待 Readiness Run 和 Pipeline permission 证据后关闭本项。
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

**任务：Azure Release Authority & Registry Decision**

范围：

1. 输出 GHCR 与 ACR 的选择记录；
2. 定义 Azure/GitHub 影子期和唯一候选清单；
3. 盘点现有 R2 门禁，形成 Azure 等价矩阵；
4. 设计 Phase 3 YAML，但暂不触发生产部署。

完成定义：评审通过的决策文档、等价矩阵、Service Connection 权限边界和可回退迁移方案。没有该决策，不直接实现 ACR Push。

## 相关文档

- [CI/CD 架构](./CI-CD-ARCHITECTURE.md)
- [发布流程](./RELEASE-PROCESS.md)
- [环境策略](./ENVIRONMENT-STRATEGY.md)
- [R2 签名与候选制品](../client/r2/02-signing-candidate-artifacts.md)
