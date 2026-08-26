# CP6 CI/CD 架构

## 要解决的问题

CI 成功只证明某个提交能够构建和通过已配置的测试，不等于已经生成可部署版本，更不等于用户已经访问到新版本。CP6 需要把代码验证、候选制品和环境部署分成可审计的阶段，同时避免 Azure DevOps 与现有 GitHub R2 流水线各自生成一套“正式版本”。

## 当前架构

```text
任务分支 ── PR ──> GitHub Actions 合同/SQL 门禁
                     |
                     v
                   main
                     |
          +----------+-------------------+
          |                              |
          v                              v
GitHub client-contract             GitHub R2 Release
hosted build/test/android          protected vX.Y.Z tag
          |                        source + SQL + E2E gates
          v                        build/sign/scan/archive
 SHA runtime artifact                    |
          |                           GHCR digest
          v                              |
Azure self-hosted bridge                 |
verify + republish; no build/deploy      |
                                         v
                               protected environment deploy
```

当前代码编译与完整客户端合同由 GitHub-hosted `client-contract` 执行；Azure `Default` self-hosted Agent 只把同一完整 SHA 的成功 GitHub Runtime Artifact 做来源、摘要和内部清单验证后转存，避免本机 CI 与 SQL/Docker 争抢内存。GitHub R2 仍负责受保护 Tag、候选镜像、签名、SBOM、漏洞扫描、不可变证据、数据库初始化、生产部署和运行身份核对。

这些流程可以暂时并存，但职责不能模糊：Azure Artifact 绿灯不是 R2 候选，也不能替代生产批准。

## 目标架构

```text
开发分支
   |
   v
PR 验证
   |
   v
main CI -------------------------------+
   |                                   |
   | 全部门禁通过                       | 失败即停止
   v                                   v
受保护版本冻结                      无候选制品
   |
   v
一次构建 API/Web 镜像
   |  version + Git SHA + provenance/SBOM/scan
   v
唯一受控 Registry
   |  repository@sha256:digest
   +-------------> DEV 自动部署与验证
                         |
                         v
                    UAT/业务验收
                         |
                         v
                  PROD Environment Check
                         |
                         v
                 同一 digest 部署 PROD
                         |
                         v
                 健康、身份、迁移与证据核对
```

目标中的“唯一受控 Registry”已由 [ADR-DEVOPS-001](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md) 固定为 GHCR，GitHub R2 是唯一候选权威。Azure 只读取同一候选做非权威 Shadow 验证，不另建同版本镜像、不生成第二份清单、不部署。

未来若迁移 ACR，必须以新的 ADR 定义等价门禁、受控 digest 复制、单一写权威、切换窗口和 30 分钟回退；不能在普通 YAML 任务中隐式改变。

## 职责边界

### CI

CI 负责：

- 后端和客户端 restore/build/test；
- Web 依赖安装、类型检查、单测和生产构建；
- 后续逐步补齐 Space、OpenAPI/SDK drift、SQL、E2E 和安全门禁；
- 产出测试结果，不修改服务器或生产数据库。

### Release

Release 负责：

- 验证受保护版本与 Git 提交关系；
- 用已有 Dockerfile 一次构建 `cp6-api` 与 `cp6-web`；
- 注入 `RELEASE_VERSION` 和 `GIT_SHA`；
- 生成镜像 digest、SBOM、漏洞报告、来源证明和候选清单；
- 推送到唯一受控 Registry。

CP6 的两个 Dockerfile 都支持 `RELEASE_VERSION`/`GIT_SHA`，当前由 GitHub R2 Buildx 工作流注入。Azure Shadow 不调用 Docker Build/Push；若未来新 ADR 允许 Azure 成为构建权威，必须重新证明 build arguments、provenance、SBOM 和扫描门禁真实执行。

### CD

CD 负责：

- 只接收已验证的候选 digest，不重新构建；
- 先运行一次性 `db-init`，成功后再启动 API/Web；
- 使用 `deploy/production/compose/compose.yaml` 作为试点输入，未来多仓使用 `deploy/production/kubernetes/`；
- 核对 `/health/live`、`/health/ready`、`/health/release` 和 Web `/release.json`；
- 记录批准人、运行版本、Git SHA、镜像 digest、最新迁移和证据 URI。

## 关键设计决策

### 1. Build once, deploy many

DEV 和 PROD 重建“同版本”会产生无法证明相同的二进制。CP6 必须构建一次、记录 digest，然后在环境之间推广该 digest。

代价是候选制品和清单需要保留更久，并且版本一旦消费就不能静默覆盖。收益是生产内容可从 digest 精确追到源码和批准记录。

### 2. 标签用于人读，digest 用于运行

`v1.2.0` 和 Git SHA 便于定位；Docker tag 可变，不能作为生产身份。Compose 和 Kubernetes 已经要求 `repository@sha256:digest`，Azure 路线必须保持该约束。

### 3. CI Agent 与部署身份分离

当前 Azure Artifact 桥使用 `Default` self-hosted pool。YAML 未固定具体 Agent；该 Agent 只需 Checkout 服务连接与 Azure Artifact 权限，不执行编译，也不应自动获得 DEV/PROD Secret 或生产网络权限。

部署阶段应使用专用 Azure Environment 资源、专用部署 Agent 或受限 Service Connection。CI Agent 被攻陷时，不应因此获得生产发布能力。

### 4. 审批保存在资源侧

PROD 审批应配置在 Azure Environment/Service Connection 等受保护资源上，而不是写成可由 YAML 修改者删除的普通脚本条件。生产环境还应限制允许使用它的 Pipeline。

### 5. 迁移以门禁等价为准

迁移 Azure DevOps 的完成定义不是“能 Push 镜像”，而是至少等价保留现有 R2 的版本冻结、测试、SQL/E2E、SBOM、漏洞扫描、签名、清单哈希链、digest 部署、健康/身份核对和证据归档。等价证明前，GitHub R2 不退出。

## 非目标

- 当前规划不执行生产部署，不创建真实生产 Secret，也不购买 Azure 资源。
- 当前不直接上 AKS；试点优先复用生产 Compose 边界。
- 不把根目录开发 `docker-compose.yml` 或 `k8s/` 用作生产输入。
- 不以 `dotnet publish` 或 Docker Build 成功冒充用户已上线。

## 相关文档

- [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md)
- [发布权威与 Registry ADR](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md)
- [Azure Release Shadow 设计](./AZURE-DOCKER-RELEASE-SHADOW-DESIGN.md)
- [发布流程](./RELEASE-PROCESS.md)
- [环境策略](./ENVIRONMENT-STRATEGY.md)
- [R2 生产就绪主规范](../client/r2/README.md)
- [R2 Compose/Kubernetes 部署](../client/r2/03-compose-kubernetes-deployment.md)
