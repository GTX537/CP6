# How to 发布 CP6

本指南描述 CP6 从权威候选到环境推广的标准流程。当前 [ADR-DEVOPS-001](./adr/ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md) 固定 GitHub R2 + GHCR 为唯一候选权威；Azure 已完成基础 CI 和不具生产权威的本机 DEV 学习链，下一阶段只做只读 Shadow 验证。下列 UAT/PROD 步骤仍是目标操作规程，不是已经可执行的生产声明；生产发布继续遵循 [WMS R2 主规范](../client/r2/README.md)。

## 前置条件

- 任务改动已在从最新 `main` 创建的独立分支完成并验证。
- PR 门禁通过，且变更已按仓库规则合入 `main`。
- 版本使用 `major.minor.patch`；当前仓库约定受保护 Tag 为 `vX.Y.Z`。
- 唯一 Registry 为 GHCR，候选权威为 GitHub R2，候选链为 Schema 2 manifest + candidate result；Azure Shadow 输出不可部署。
- DEV/UAT/PROD Azure Environments、Service Connections、审批人和 Secret 已由资源 Owner 配置。
- 生产配置、域名、证书、外部服务和证据位置已按 R2 输入规范冻结。

## 步骤

1. **确认源码基线**

   核对待发布提交就是远端 `main`，并确认版本未被使用。日常开发不得直接向 `main` 提交；发布只消费已经验证并合入的提交。

2. **运行完整 CI**

   后端、客户端、Web、Space、契约、SQL、E2E 和安全门禁必须按批准的等价矩阵全部通过。Azure 当前的基础 CI 不覆盖全部这些门禁，因此在补齐前必须继续依赖现有 GitHub 门禁。

3. **冻结版本**

   创建受保护的 annotated `vX.Y.Z` Tag，并把版本、完整 Git SHA、执行规范和冻结输入绑定在审批记录中。失败版本不删除重打，使用更高补丁版本。

4. **只构建一次发布镜像**

   使用 `CP6.WebApi/Dockerfile` 和 `cp6.web/Dockerfile` 构建：

   ```text
   cp6-api:v1.2.0
   cp6-api:<full-git-sha>
   cp6-web:v1.2.0
   cp6-web:<full-git-sha>
   ```

   构建时传入 `RELEASE_VERSION=1.2.0` 和完整 `GIT_SHA`。同一版本不得在 DEV、UAT、PROD 分别重建。

5. **验证并登记候选制品**

   生成 provenance、SBOM 和漏洞报告，Push 到批准的 Registry，然后读取 Registry 返回的 API/Web digest。候选清单至少记录：

   - 版本、完整 Git SHA 和 Pipeline Run ID；
   - API/Web repository 与 `sha256` digest；
   - 测试、SQL、E2E、安全扫描和 SBOM 证据；
   - 最新 EF 迁移和 `ForwardOnly=true`；
   - 生成时间、批准输入和证据 URI。

6. **自动部署 DEV**

   DEV deployment job 从候选清单读取 digest：

   ```text
   db-init（同一 API digest）
       ↓ 成功
   API/Web rollout
       ↓
   health + release identity verification
   ```

   不从源码重新 Build，不使用 `latest`，也不把开发用 Compose/Kubernetes 资产投入环境。

7. **验证 DEV**

   至少核对：

   - API `/health/live` 为 Healthy；
   - API `/health/ready` 及必需依赖检查为 Healthy；
   - API `/health/release` 的 version、Git SHA、API/Web digest 和最新迁移与候选清单一致；
   - Web `/release.json` 与候选版本/Git SHA 一致；
   - 关键业务 Smoke Test 通过；
   - DEV deployment evidence 已归档。

8. **推广到 UAT**

   UAT 拉取 DEV 已验证的同一 digest。业务 Owner 完成验收并记录结果；任何制品变化都要产生新候选，不能直接替换原 digest。

9. **批准 PROD**

   `cp6-prod` Azure Environment 在资源侧暂停 Pipeline。批准人检查版本、变更、DEV/UAT 证据、数据库影响、回退兼容证明和维护窗口。审批记录必须保留批准人和时间。

10. **部署并验证 PROD**

    PROD 使用同一候选清单和 digest，先 db-init，后 API/Web，再执行与 DEV 等价的健康、身份、迁移和远程制品核对。只有全部通过才标记发布成功。

11. **归档与通知**

    归档生产 deployment evidence、Release Notes、批准记录和 Pipeline 链接。通知中区分“部署完成”和“现场/业务验收完成”。

## 验证清单

- [ ] 生产版本可追到唯一 Git SHA。
- [ ] PROD digest 与 DEV/UAT 验证过的 digest 完全一致。
- [ ] 没有可变 `latest` 或同版本重建。
- [ ] 数据库初始化只运行一次，且先于 API/Web。
- [ ] API/Web 发布身份与候选清单一致。
- [ ] Azure Environment 保存批准人和部署历史。
- [ ] 失败阶段没有被错误标记为成功。

## 回滚

应用回退只能选择已有 Schema 兼容证明的旧 digest。若 db-init 失败，不启动新 API；若 rollout 或身份核对失败，停止流量切换并恢复上一兼容应用 digest。数据库不执行 Down，使用更高版本迁移前滚修复。每次回退或前滚都生成新的部署证据，不覆盖原记录。

## 故障排查

### Pipeline 排队但 Job 不启动

检查 `Default` pool 是否有在线且已授权的 self-hosted Agent，以及 Agent capabilities 是否满足 PowerShell、Git 和 Azure Artifact 桥要求。基础流水线不再要求本机 .NET/Node 编译；Docker 与 SQL 能力属于独立 `CP6-Deploy` Agent。YAML 未绑定具体 Agent 名称。

本机 `CP6-Windows` 必须通过仓库脚本以前台方式启动：

```powershell
.\scripts\Start-Cp6CiAgent.ps1
```

不要直接从 PowerShell 7 环境运行 `C:\agent\bin\Agent.Listener.exe run`。PowerShell 7 的
`PSModulePath` 会被 Agent 继承，并使任务中的 Windows PowerShell 5.1 重复加载类型数据；脚本会先核对
`.agent` 必须为 `CP6-Windows` / `Default`，只对 Agent 子进程清空该变量，退出后恢复当前终端环境。

### Azure 桥找不到或不能下载 GitHub Runtime Artifact

先确认 GitHub `client-contract.yml` 对同一完整 SHA 已 `completed/success` 并上传未过期的 `cp6-dev-runtime-<sha>`。Checkout 必须设置 `persistCredentials: true`，并从仓库专属 `http.https://github.com/<owner>/<repo>.extraheader` 读取凭证；不得输出凭证。接收器还会失败关闭于工作流路径/事件/SHA 不符、归档 SHA-256 不符、ZIP 越界或内部 manifest 不符。

### 镜像版本显示 unknown

确认 Docker build 实际收到 `RELEASE_VERSION` 和 `GIT_SHA`。使用 `Docker@2 buildAndPush` 时，额外 `arguments` 会被忽略；改用拆分 build/push 或受控 Buildx。

### DEV 成功但 PROD digest 不同

立即停止部署。这说明发生了重建、错误 tag 解析或候选清单漂移，违反 Build once。不要用“功能看起来相同”放行。

### API ready 但 release identity 不匹配

按失败处理。核对环境变量、Compose/Kubernetes 渲染值、实际运行容器镜像和最新迁移；不要只看 HTTP 200。

## 相关文档

- [Azure Pipelines 演进计划](./AZURE-PIPELINES-PLAN.md)
- [环境策略](./ENVIRONMENT-STRATEGY.md)
- [R2 Compose/Kubernetes 部署](../client/r2/03-compose-kubernetes-deployment.md)
