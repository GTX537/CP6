# ADR-CRM-R00：CP6 SaaS V1 发布权威公开镜像

<!-- public-r00-mirror-status: Complete -->

- 镜像状态：**Complete**
- 私有源状态：**Accepted**
- 私有决策 ID：`CP6-SAAS-R00`
- 私有 `decisionPayloadSha256`：`64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`
- 私有源合并提交：`07a7bb0b50f33b0cb70c18c14f83be77c725626d`
- 日期：2026-08-14
- 适用仓库：`CP6`、`CP6.Platform`、`CP6.CRM`、`CP6.Portal`
- 公开合同：[CP6 SaaS V1 公开工程契约](../../crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md)

下列标记内正文与私有已批准 R00 载荷逐字一致。公开镜像不复制私有个人审批身份；其摘要必须独立复算为上述私有 R00 摘要。公开合同已由 ProgramOwner 对精确摘要批准并同步为 Complete；P09/P10 实现仍为 Pending。

<!-- release-decision-payload:start -->
## 1. 决策

CP6 SaaS V1 的唯一候选 Registry/发布权威固定为 GitHub R2 流程与 GHCR。Azure Pipelines 可以执行 CI、DEV 学习链、影子验证或消费同一 digest，但不得为同一版本重新构建候选，也不得与 R2 同时宣称权威。ACR 或其他 Registry 迁移必须另立 ADR、迁移期和回退验收。

## 2. Build once, deploy many

- 候选只构建一次；DEV、UAT、PROD 推广同一 `repository@sha256:digest`。
- SemVer、受保护 Git Tag 和 Git SHA 只用于追踪，生产身份是不可变 digest 与精确对象版本。
- 四仓 System Release Manifest 固定各仓 Git SHA、包/镜像 digest、OpenAPI/event schema major 与 digest、Dapr component version、数据库 migration IDs、SBOM、签名、证据引用和 previous system manifest digest。
- 默认整套四仓回退。组件级例外必须有签名兼容证据；数据和迁移不回退。

## 3. 当前能力判断

| 能力 | 状态 | 解释 |
| --- | --- | --- |
| GHCR 候选镜像、SBOM、漏洞扫描、受保护 Tag | Existing | 继续作为 V1 权威基线，不因 Azure 计划弱化 |
| 证据存储版本控制与 Object Lock | Existing | 可以保留历史对象版本 |
| 候选对象不可变身份 | Partial / Gap | 现有固定 SemVer key/latest 读取未把 R2 `VersionId` 固定进消费合同，不能证明一版本一候选 |
| OCI 原生签名/四仓 System Manifest | Gap | 必须在 P07/P09/P10/CRM12 实现并签署 |
| Azure 与 R2 等价候选 | Not approved | Azure 只能非权威消费同一 digest |

不得把 Object Lock 本身描述为已完成的不可变候选身份。对象版本存在不等于发布者和消费者都固定了同一 VersionId。

## 4. 候选对象与 Locator

- 候选对象必须使用 content-addressed key，或对同一版本使用严格 first-writer-wins。重跑不得把新的 current object version 变成同版本的新权威候选。
- 每个被消费对象记录 `bucket + key + VersionId + SHA-256`，消费者按精确 VersionId 读取并复核 SHA-256，禁止读取未限定的 latest。
- `candidate-result` 内容绑定受保护 Tag、四仓 Git SHA 和 System Manifest digest；它不包含自身 VersionId，也不自引用。
- `candidate-result` 上传后，由独立、受保护、first-writer-wins、带签名的 `CandidateLocator` 固定其 `bucket + key + VersionId + SHA-256`。消费端先验证 Locator 的 Tag/签名/主体/唯一性，再精确读取 candidate-result。
- System Manifest 不声称包含自身对象 VersionId/digest；其上传后对象身份同样由 Locator 或外层签名 attestation 固定。

实际 R2 workflow/script 修复是独立 `P09/P10` 单仓实施票；本 ADR 只冻结合同，不授权顺手修改现有流水线。

## 5. 候选前置与候选后 Adoption

候选生成前必须关闭：四仓编译/测试、真实 SQL、真实 Dapr/Kafka、真实 C03 + 隔离 ERP、迁移恢复副本、Pilot UAT、Security/Performance/Resilience、Manifest/Locator 和候选对象身份 Gap。

Lead Adoption、Full Journey Adoption、移动 GA 和 90 日商业采用发生在生产切换后。它们作为 append-only 发布记录引用不可变候选 Manifest digest，不阻止候选生成，也不得修改候选 Manifest。技术候选完成不等于 V1 Epic 可以关闭。

## 6. 推广与回退

- PROD 只允许来自已验证 CandidateLocator 的 digest；CI Agent 与生产部署身份分离。
- 生产 Approval/Checks 配置在受保护 Environment/资源侧，不能由工作流作者绕过。
- 回退前验证旧二进制在当前 Schema 和真实升级后数据上的读、写、事件兼容；Schema 只前向。
- `previousSystemManifestDigest` 指向完整前一 System Manifest。组件级回退例外必须引用签名 compatibility evidence。
- 候选失败、门禁失败和部署失败均保留原证据；不得以覆盖对象或重写 Manifest 消除失败历史。

## 7. 验收

R00 只有在以下条件全部满足后可从 Proposed 变为 Accepted：

1. 唯一 `ProgramOwner` 对规范化决策载荷摘要批准；System Architect、Release、Security 等角色提供评审与证据，但不构成独立批准。
2. 外部 append-only 批准记录绑定本文件路径、`decisionPayloadSha256`、批准主体/角色/UTC 时间和不可变 evidence object identity。
3. 状态镜像更新不改变载荷摘要；只验证页首、批准记录和镜像中的摘要仍等于批准值。
4. 标记内正文变化必须重算摘要并使旧批准 Expired。
<!-- release-decision-payload:end -->

## 公开状态镜像（不进入私有 R00 载荷）

- 私有 R00：Accepted
- 公开工程镜像：Complete
- 公开同步：Complete
- P09/P10 implementation：Pending
- M0：No-Go
