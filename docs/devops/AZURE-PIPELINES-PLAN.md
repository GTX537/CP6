# Azure Pipelines 演进计划

本计划把 Azure DevOps 从当前 CI 扩展到可追踪的 Release/CD。每一阶段都必须单独交付、验证和记录，不允许一次提交同时改完 Registry、DEV 和 PROD。

## 状态标记

- `[x]`：仓库或用户提供的 Azure 运行上下文已确认完成。
- `[ ]`：尚未实现或缺少可复现证据。
- `GATE`：未满足时不得进入下一阶段。

## Phase 1：CI 基线

状态：**远程构建 + 本机轻量 Artifact 桥已验证，待 main 及稳定性闭环**。

- [x] GitHub 仓库已连接 Azure DevOps。
- [x] 根目录存在 `azure-pipelines.yml`。
- [x] 使用 `Default` self-hosted agent pool。
- [x] GitHub `client-contract.yml` 配置 .NET 8、Node.js 22、后端/客户端、OpenAPI、Web、Android 和 R2 source 检查，并创建完整 SHA 绑定的 DEV 运行包。
- [x] `main` 提交触发 CI。
- [x] 记录 Azure Pipeline 成功运行 URL/Run ID 和 Agent 能力清单：[`Run #92`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=92)，`CP6-Windows` / `Default`。
- [x] 确认本机编译与 SQL/Docker 共存不安全：#109/#111/#113/#115 均按内存或 SQL 门禁取消；Microsoft-hosted [`Run #110`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=110) 证明 Azure 组织当前没有 hosted parallelism，未启用计费。
- [x] 将 Azure 基础流水线收敛为轻量 Artifact 桥。分支 [`Run #117`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=117) 已从 GitHub 成功下载并验证工作流来源、完整 SHA、归档 SHA-256 与内部 manifest，再发布 Azure Pipeline Artifact；本机 SQL/公网容器基线不变。
- [x] `main@a5c6b5fa...` 的 GitHub client-contract 与 Azure [`Run #118`](https://dev.azure.com/gaobubao/japanese/_build/results?buildId=118) 成功；Manual DEV #120/#121 复用同一 Artifact，分别完成独立备份、部署、身份验证与证据发布，三次手动验收达到 3/3。
- [ ] 决定 PR 验证归属：启用 Azure PR trigger，或明确只依赖现有 GitHub PR 门禁；当前 `pr: none`。
- [ ] 比较 Azure CI 与 `client-contract.yml`/`wms-production-sql.yml`，登记未覆盖的 Space、OpenAPI/SDK、SQL、E2E、安全和 Android 门禁。
- [ ] 为 self-hosted Agent 定义更新、磁盘清理、离线告警、并发和工作区隔离规则。

`GATE`：Azure CI 连续运行稳定，失败能定位到具体门禁；任务分支仍按仓库规则经 PR/验证合入 `main`，不得恢复日常直接 Push `main`。

## Phase 2：发布权威与 Registry 决策

状态：**决策完成；实现边界已冻结**。

- [x] 唯一候选 Registry 继续使用 GHCR：`cp6-api` / `cp6-web`。
- [x] ACR 当前不创建、不授权；未来迁移必须另立 ADR，不能在实现票中隐式切换。
- [x] GitHub R2 保持唯一候选/部署权威；Azure 只允许读取同一候选进行非权威 Shadow 验证。
- [x] 版本入口沿用 Freeze 创建、指向当前 `main` 的受保护 annotated `vX.Y.Z` Tag。
- [x] 唯一组件候选链为 Schema 2 `release-manifest.json` + `candidate-result.json` 根指针及其 Object Lock 证据。
- [x] 已记录 R2/Azure 等价矩阵、只读 Service Connection 边界、30 分钟 Shadow 回退和未来 ACR 切换门禁。

权威决策见 [ADR-DEVOPS-001](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md)。Phase 3 只实现 [Azure Release Shadow](./AZURE-DOCKER-RELEASE-SHADOW-DESIGN.md)，不让两个系统对同一版本分别 Build。

`GATE`：**已关闭**。Registry、候选清单和发布权威均有唯一答案；任何 ACR/权威切换需重新打开为独立 ADR。

## Phase 3：Azure Release Shadow

状态：**S0 仓库合同已实现；S1 真实只读候选验证尚未开始**。

- [x] 设计独立 YAML 结构，初始固定 `trigger: none`、`pr: none`，只允许手动验证既有候选。
- [x] 设计 candidate result → Schema 2 manifest → freeze/spec/evidence → GHCR digest 的逐层验证顺序。
- [x] 定义 `Authority=Shadow`、`Deployable=false` 的唯一 Azure 输出语义。
- [x] 实现 S0 fixture/parser/YAML 合同：1 个有效 fixture、10 个失败关闭场景和静态能力门禁；不连接真实 GHCR 或证据存储。
- [ ] 实现 S1 真实候选只读元数据和 GHCR digest 验证，发布 Shadow report。
- [ ] 为完整镜像 pull/SBOM/Trivy 对比准备独立容量受控 Agent；不得与本机 SQL/Docker 公网环境争抢资源。
- [ ] 连续三个不同 SemVer 候选通过 Shadow 验收并形成等价报告。

实现注意：Azure 禁止调用 `docker build`、`Docker@2 buildAndPush`、ACR Build/Import 或部署脚本；provenance、SBOM、扫描和 digest 只读取 GitHub R2 的权威候选，独立重扫结果仅作对比证据。

`GATE`：Shadow 能证明同一 Tag/SHA/manifest/digest/证据链，且没有 Registry 写入、第二份候选或部署权限；完成不等于 Azure 获得发布权威。

## Phase 4：DEV 自动部署

状态：**生产等价路径待 Phase 3；本机学习链已独立验收**。

学习环境旁路（不计入 Phase 4 生产门禁）：`azure-pipelines-dev.yml` 现以同一实现支持手动发布和受 `CP6_DEV_AUTO_DEPLOY_ENABLED` 控制的 completion trigger。它只接受成功的 `GTX537.CP6/main` Run，自动跳过 superseded commit；从所选哈希 Runtime Artifact 封装本机 SHA 镜像，锁内最多等待 600 秒取得至少 2 GiB 可用内存及 3 次连续独立 SQL 登录，再对 `CP6_DEV` 执行 CHECKSUM 备份/VERIFYONLY、停旧 API/Web、前向迁移并逐层验证。独立 `cp6-public-tunnel` 和 `CP6DEV_IMPORT_*` 旁路导入不会触碰根 `cp6`/`CP6DB`。外部 Secret、定向资源权限和 Exclusive lock 已配置；Manual #95/#120/#121 已完成 3/3，#129 已证明低内存会在 SQL/备份前失败关闭，#131 的同 Stage 重试又暴露并修复固定证据 Artifact 名冲突。main CI #132 随后以 `main@08813896...` 自动触发 DEV #133，600 秒门禁、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份与 `cp6-dev-evidence-attempt-1` 全部成功，根 `cp6`/`CP6DB` 零漂移。该链没有 Registry/SBOM/签名，不能推广到 UAT/PROD-LAB，也不能把 Phase 3/4 标为完成；Tunnel 切换仍须单独授权。

- [x] 创建 `cp6-dev` Azure Environment，并只授权 `CP6 DEV CD`；Exclusive lock 与 Run #95 部署历史已验证。
- [x] 使用专用部署身份，不复用开发者 PC 的通用 CI 权限；`CP6-Deploy` Pool、`cp6_deploy_agent` 服务身份和强化后的 Readiness Run #89 已验证。
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

**任务：Azure Release Shadow S1 真实候选只读元数据**

范围：

1. 以 S0 parser/报告 Schema 为基础，手动选择一个已经存在的 R2 候选；
2. 设计并审批 R2 evidence reader 与 GitHub metadata reader 的只读身份，不授予 Registry/Tag/Environment 写权限；
3. 按权威 URI 读取 candidate result/manifest/freeze/spec，逐层重算 SHA-256，并验证 annotated Tag、完整 Git SHA 与冻结 main 关系；
4. 仍不拉取大型镜像、不重跑 SBOM/Trivy、不部署；GHCR digest 只读解析另立后续切片。

完成定义：一个真实、已存在候选在最小只读身份下生成 `Authority=Shadow`、`Deployable=false` 报告；对象来源/hash/Tag/SHA 任一不一致均失败关闭，且 Azure 没有创建 Tag、Package、manifest 或 deployment。

## 相关文档

- [CI/CD 架构](./CI-CD-ARCHITECTURE.md)
- [发布权威与 Registry ADR](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md)
- [Azure Release Shadow 设计](./AZURE-DOCKER-RELEASE-SHADOW-DESIGN.md)
- [发布流程](./RELEASE-PROCESS.md)
- [环境策略](./ENVIRONMENT-STRATEGY.md)
- [R2 签名与候选制品](../client/r2/02-signing-candidate-artifacts.md)
