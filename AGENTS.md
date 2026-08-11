# CP6 仓库开发规则

## 分支优先（强制）

- `main` 只用于集成、发布准备和已验证任务的合并，不作为日常开发分支。
- 除非任务存在必须直接在 `main` 完成的明确技术原因，否则每个开发、修复、重构和文档任务都必须从最新 `main` 创建独立分支。
- 必须直接在 `main` 操作时，开始前要说明原因，并在提交或项目状态记录中保留依据。紧急、方便或改动很小本身不构成例外。
- 一个分支只承载一个任务。不得把其他人的未提交改动、临时文件或顺手修复混入提交。
- 根工作区有未提交改动时，优先使用独立 Git worktree；禁止通过清理、覆盖或暂存他人改动来腾空工作区。

## 标准交付流程

1. 确认 `main` 与 `origin/main` 的状态，并从当前已确认的 `main` 基线创建任务分支。
2. 在任务分支实现改动；只暂存明确属于该任务的文件，禁止使用不加检查的 `git add -A`。
3. 运行与风险相称的测试、构建、格式、契约、迁移或端到端门禁。任何必需门禁失败时不得合并。
4. 审查分支相对 `main` 的完整 diff，确认没有敏感信息、机器专属配置、调试残留和范围漂移。
5. 以可审计提交保存任务；必要时先把任务分支推送远端供检查。
6. 将已验证分支合并到 `main`。合并后执行必要的冒烟或集成验证，再推送 `main` 到远端。
7. 核对远端 `main` 已包含任务提交后，才可声明完成或清理任务 worktree/分支。

## 完成定义

- 功能代码、测试和必要文档在同一任务范围内闭环。
- 新增行为有自动化覆盖；不能自动化的验证必须留下可复现证据。
- 不以合成数据、跳过项或候选实现冒充生产验收。
- 涉及项目状态的任务同步更新 `docs/project-memory/PROJECT_STATE.md`、`05-Completed.md`、`06-Todo.md` 和 `CHANGELOG-AI.md`。
- 未经明确授权不得 force-push、重写共享历史、删除远端分支或执行生产部署。

## CP6 DevOps 上下文

- DevOps 入口为 `docs/devops/README.md`；处理 CI、Release、Registry、部署或环境任务前必须阅读该目录，并交叉核对 `docs/client/r2/README.md`。
- 当前 `azure-pipelines.yml` 只完成 CI：`main` 触发、`pr: none`、`Default` self-hosted pool，执行 .NET 8/Node 22 的 restore、build、test、Vue type-check/test/build。它尚未构建镜像或部署任何环境。
- 现有 GitHub R2 流水线仍是生产候选与部署的权威实现，包含受保护 Tag、SQL/E2E、镜像、SBOM、漏洞扫描、签名、不可变证据、digest 部署和运行身份核对。Azure 迁移未通过等价验收前不得删除、绕过或弱化这些门禁。
- Release 必须遵守 **Build once, deploy many**：API/Web 镜像只构建一次，DEV/UAT/PROD 推广同一 `repository@sha256:digest`；SemVer 和 Git SHA 用于追踪，不以可变 Tag 作为生产身份。
- 聊天规划建议 ACR，但仓库当前 R2 使用 GHCR。实现 Azure Docker Release 前必须先确定唯一 Registry、候选清单、迁移期和回退方案，禁止两套系统对同一版本分别 Build 并同时宣称权威。
- CI Agent 与部署身份分离；不得让开发者 PC/通用 CI Agent 自动持有 PROD Secret 或生产管理权限。PROD Approval/Checks 配置在 Azure Environment/受保护资源侧，不由 YAML 作者自行取消。
- 生产部署只使用 `deploy/production/compose/compose.yaml` 或 `deploy/production/kubernetes/`；根 `docker-compose.yml` 与 `k8s/` 仅供开发。数据库先运行一次性 `db-init`，只前向迁移，应用回退必须证明 Schema 兼容。
- 当前下一阶段不是直接上线，而是完成 `docs/devops/AZURE-PIPELINES-PLAN.md` 中的“发布权威与 Registry 决策”，再实现 Docker Release、DEV、UAT、PROD。

