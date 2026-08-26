# Azure Docker Release Shadow 设计

本文把 ADR-DEVOPS-001 转换为 Phase 3 可实现的 Azure YAML 结构。它是设计合同，不是已创建的 Pipeline，也不会触发构建、Push 或部署。

## 1. 目标与非目标

目标是让 Azure 对 GitHub R2 已生成的候选执行独立、只读、可审计的验证，并量化未来迁移能力差距。

非目标：

- 不从源码构建 API/Web；
- 不 Push GHCR/ACR；
- 不创建或修改 Tag；
- 不生成第二份 `release-manifest.json`；
- 不调用 DEV/UAT/PROD 部署；
- 不读取生产 Secret 或连接生产数据库。

## 2. 建议文件与触发器

建议后续独立实现：`azure-pipelines-release-shadow.yml`。

```yaml
trigger: none
pr: none

parameters:
  - name: releaseVersion
    type: string
  - name: candidateResultUri
    type: string

stages:
  - stage: ValidateAuthorityInputs
  - stage: VerifyCandidateChain
  - stage: VerifyRegistryDigests
  - stage: PublishShadowEvidence
```

初始只能从 Azure UI 手动运行。不得添加 `main`、Tag 或 Pipeline completion 自动触发，直到 S1 至少一次真实候选验收通过且资源 Owner 单独批准。

## 3. Stage 合同

### ValidateAuthorityInputs

- `releaseVersion` 必须严格匹配 `X.Y.Z`；Pipeline 内转换为 `vX.Y.Z`。
- `candidateResultUri` 必须位于批准的 R2 evidence bucket/prefix，路径以 `/vX.Y.Z/candidate-result.json` 结束。
- 从 GitHub 读取 Tag 对象，确认是 annotated Tag、目标为候选记录的完整 SHA，并与当前/冻结时 `main` 关系匹配。
- 拒绝 branch ref、轻量 Tag、短 SHA、可变 Tag、不同版本路径和未批准域名/桶。

### VerifyCandidateChain

- 以只读方式按权威 URI 读取 `candidate-result.json`、manifest、freeze snapshot 和 candidate spec，并立即核对记录的 SHA-256；如果同一 key 的当前对象已变化则失败关闭。
- 验证 candidate result Schema 1、release version、Tag、40 位 Git SHA、manifest URI/hash、freeze URI/hash、spec path/hash。
- 验证 release manifest Schema 2、Git SHA、API/Web repository+digest、SupplyChain、Database、ExecutionSpec 和 EvidenceRootUri。
- 下载引用证据时逐项核对 SHA-256/bytes；不接受同一路径下 hash 不匹配的新版本替代对象。
- 不重写或重新上传任何权威文件。

### VerifyRegistryDigests

- 使用 GHCR pull-only credential 解析 `ghcr.io/gtx537/cp6-api@sha256:...` 与 `cp6-web@sha256:...`。
- 读取 OCI manifest/config 并核对平台、digest、版本和 Git SHA 标签/注解。
- 元数据验证可运行在轻量 Agent；完整镜像 pull/SBOM/Trivy 对比必须等待独立容量证明，且不得影响本机 SQL/Docker 公网服务。
- 禁止任何带写入语义的 Registry 命令。

### PublishShadowEvidence

只发布 `azure-shadow-report.json` 和人类可读 summary。建议 Schema：

```json
{
  "SchemaVersion": 1,
  "Authority": "Shadow",
  "Deployable": false,
  "ReleaseVersion": "1.2.3",
  "GitSha": "40-hex",
  "CandidateResult": { "Uri": "s3://...", "Sha256": "64-hex" },
  "Manifest": { "Uri": "s3://...", "Sha256": "64-hex" },
  "Images": {
    "Api": { "Repository": "ghcr.io/gtx537/cp6-api", "Digest": "sha256:..." },
    "Web": { "Repository": "ghcr.io/gtx537/cp6-web", "Digest": "sha256:..." }
  },
  "Checks": [],
  "Azure": { "RunId": 0, "Pipeline": "CP6 Release Shadow" },
  "VerifiedAtUtc": "ISO-8601"
}
```

Artifact 名固定包含 `shadow` 和 Run ID，保留期可比权威证据短，但不得被任何部署流程读取。

## 4. 失败关闭规则

以下任一情况必须失败且不发布 `Deployable=true`：

- 版本、Tag、Git SHA、路径或来源不一致；
- candidate result、manifest、freeze、spec 或证据 hash 不一致；
- manifest 不是 Schema 2；
- image repository 不在 allowlist，或 digest 不是完整 `sha256`；
- GHCR digest 不存在、无权读取或解析到不同 manifest；
- 缺少 SBOM、漏洞、source、SQL、db-init 或签名字段；
- 需要写权限才能继续；
- Agent 容量不足或会与本机 SQL/Docker 竞争。

失败报告必须指出具体 gate，但不得打印 Secret 或响应认证头。

## 5. Agent 与并发

- 元数据阶段优先使用独立轻量 Agent；若暂用现有 self-hosted Agent，只允许网络/哈希操作。
- 镜像 pull/scan 需单独 Pool，例如 `CP6-Release-Shadow`，与 `Default` CI 和 `CP6-Deploy` 分离。
- 并发键按 `releaseVersion` 固定，同一候选不允许两个 Shadow Run 同时执行。
- 不使用 `cancel-in-progress` 终止已开始的证据读取；后来的重复 Run 应排队或安全跳过。

## 6. 后续实现切片

1. **S0 合同**：fixture + parser + 静态 YAML contract，无外部凭据。
2. **S1 元数据**：真实 candidate chain 只读验证，发布 Shadow report。
3. **S1 Registry**：GHCR digest 只读解析，验证 API/Web OCI 身份。
4. **S2 对比**：独立 Agent 对同一 digest 重跑 SBOM/漏洞扫描并登记差异。
5. **迁移评审**：三个连续候选后只提交等价报告；是否迁移 ACR 仍由新 ADR 决定。

每个切片使用独立分支/PR，不能把 Service Connection、真实候选执行和 ACR 创建混在同一个任务中。
