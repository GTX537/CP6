# P10 Platform 候选参考

用途：P10 S06 CLI 与工作流参考。适用版本：正式包 `0.10.1`。核验日期：2026-09-08。当前状态以[项目台账](../project-memory/PROJECT_STATE.md)为准；本文不是候选发布或生产验收证据。操作步骤见 [How to](HOWTO-P10-PLATFORM-CANDIDATE.md)。

## 范围与固定身份

此工具通过正式 `CP6.Platform.Release [0.10.1]` 实现非部署型 Platform 候选的发现、验证和受控发布。未复制 Platform Schema 或源码，没有修改 CP6 应用注册，也不提供 System 候选或部署入口。

| 项目 | 当前固定值 / 权威来源 |
| --- | --- |
| Platform 源码 | `3ff27e26962dcfd722887afb80a4306010dd9ee1` |
| 正式包发布 | `p10-formal-packages.yml`，run `34126521193`，attempt `1` |
| 七包版本 | Abstractions、AspNetCore、Contracts、Deployment、EntityFramework、Messaging、Release，均为 `CP6.Platform.* 0.10.1` |
| 包摘要与 feed | [S06ReleaseIdentity](../../tools/p10/ReleaseVerifier/S06ReleaseIdentity.cs)；唯一 feed 为 `https://nuget.pkg.github.com/GTX537/index.json` |
| 正式发布记录 SHA-256 | `8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961` |
| CRM 消费实现 | PR #46，merge `a31ca0e323418f7e4108cc6220c0f5fa132e7fc2`，main run `34134695003` |
| CRM 留存索引 SHA-256 | `1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914`；同时核对 PR #47 的前向归档 |
| 唯一 OCI 仓库 | `ghcr.io/gtx537/cp6-p10-verifier`；只消费 `repository@sha256:digest` |
| Locator/OCI trust | [pinned-trust-store.v1.json](../../eng/p10/trust/pinned-trust-store.v1.json)，版本 `1`；用途分离的两把公钥 |
| NuGet trust | [p10-formal-nuget-trust-store.v1.json](../../eng/p10/trust/p10-formal-nuget-trust-store.v1.json)，版本 `1` |
| 构建工具 | .NET SDK `8.0.424`、cosign `3.1.3-cp6.2`、Syft `1.51.1`、Trivy `0.74.0`；二进制摘要由代码/YAML 固定 |

cosign 是 owner 授权的 CP6 安全衍生构建，**不是未修改的 Sigstore 官方二进制**。固定上游签名源码 commit、Go 1.26.8、九项受审模块更新、完整 lock/hash 和隔离构建定义在 [eng/p10/cosign](../../eng/p10/cosign/README.md)。`cp6.2` 相对 `cp6.1` 仅将 gRPC 升至 `1.83.2`，处理真实云端发现 `CVE-2026-84445`；两次独立构建必须复现相同 Linux/Windows 摘要，实际进度见[构建记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)。三条 workflow 都重现并校验该 helper，签名/发布 Secret 不进入编译环境；候选镜像仍只在获批 hosted validation 构建一次，publication/audit 不重建镜像。原 trust、公钥用途分离、真实签名检查及 HIGH/CRITICAL 门禁保持不变。

正式作者证书使用 `PinnedSelfSigned`：`publicCaTrusted=false`、`internallyTrusted=true`，固定指纹 `1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d`。所有正式包仍要求实际 RFC3161 时间戳；作者的固定自签信任例外不适用于 TSA 链。正式发布与 CRM 证据只按精确身份消费，不能拿历史 `0.10.0`、本地重包或合成证书替代。

TSA 使用系统信任、在线全链吊销检查和签署时刻验证，随后只接受 [S04 已独立核验的两条完整路径](../superpowers/plans/2026-09-08-p10-s06-cross-platform-timestamp.md)。Windows 的四证书路径与 Linux 的三证书路径共享同一叶证书和中间证书；历史生产者和当前消费者可以选择不同的已核验路径，但包摘要、作者、时间戳及其他不可变字段仍精确相等。此兼容性不允许任意信任根、混拼路径或绕过实际密码学验证。

## CLI

入口是 `tools/p10/ReleaseVerifier/CP6.P10.ReleaseVerifier.csproj`，发布后的执行形式是 `dotnet CP6.P10.ReleaseVerifier.dll COMMAND ...`。参数数量和命令大小写必须精确匹配，无通用 endpoint、trust、成功标志或跳过门禁参数。

| 命令 | 作用 / 结果 |
| --- | --- |
| `canonicalize INPUT NEW_OUTPUT` | 规范化有界 JSON，只创建新文件；stdout 为输出 SHA-256，不表示候选被接受 |
| `inspect SCHEMA_ID EXPECTED_SHA256 INPUT` | 检查原始字节摘要和指定合同；始终 `candidateAccepted=false` |
| `prepare-validation NEW_STAGE` | 在当前真实 validate 作业收集 15 个公开交接文件；不进行远端写入 |
| `finalize-validation STAGE IMAGE_INPUTS NEW_ARTIFACT` | 在同一实际 OCI 中重验输入，创建 24 文件验证交接；不预测作业最终结论 |
| `prepare-publication TAG VALIDATION_RUN VALIDATION_ATTEMPT VALIDATION_ARTIFACT NEW_STAGE` | 只消费已完成且同源的验证，按内容摘要条件创建 24 个 R2 对象，保存一次生成的 Locator |
| `store-publication-bundle TAG SIGNED_STAGE NEW_INTENT` | 认证并条件保存/安全复用固定 bundle，创建两文件签名意图 |
| `commit-publication TAG INTENT_ARTIFACT` | 下载当前发布作业的真实 immutable artifact，启动清空环境的只读子进程，成功后条件创建最终 Locator |
| `confirm-platform-intent TAG ARTIFACT_ID` | 当前发布作业的 pre-commit 验证；不是普通消费者接受入口 |
| `confirm-platform-published TAG` | 当前发布作业中的新进程只读 postcheck；不宣称发布工作流已完成 |
| `verify-platform TAG` | 普通只读入口：经权威发现路径检查候选、全部必要证据及两条已完成工作流 |

`TAG` 是候选身份字符串，不会创建 Git tag：长度 1–128，无空白/控制字符/`..`，符合 `vMAJOR.MINOR.PATCH[-suffix]` 的固定包规则。Run/Artifact IDs 为正十进制 Int64，无正负号或前导零；attempt 不大于 Int32 最大值。

本地交接目录必须为绝对路径，新输出目录不得存在。交接文件集合必须完全匹配，不接受链接、额外文件或更深目录。最多 32 文件、总共 64 MiB，控制文件各不超过 4 MiB，NuGet 包各不超过 8 MiB。失败可能留下新建的部分本地目录或尚未被 Locator 引用的远端对象；工具不清理、覆盖或修复它们。

`inspect` 支持 `https://schemas.cp6.dev/release/` 下的 `platform-release-candidate.v1`、`candidate-locator.v1`（仅 Platform）、`release-gate-result.v1`、`evidence-record.v1`、`build-invocation-provenance.v1` 和 `pinned-trust-store.v1`。输入必须非空、至多 4 MiB；expected hash 必须与原始输入匹配。合同检查不替代签名、远端内容和运行身份核验。

退出码：`0` 命令成功，`1` 验证或 I/O 失败，`2` 命令形式错误。成功摘要是单行规范 JSON（canonicalize 只输出 hash），失败只输出安全错误码，不打印凭据、原始网络错误或私有日志。

## 凭据与执行环境

所有凭据仅从进程环境读取，长度 1–4096，禁止控制字符。不得写入 JSON、仓库、命令参数或审核记录。普通读取需以下五项：

| 名称 | 来源 / 权限 |
| --- | --- |
| `P10_COSIGN_PATH` | 摘要已核验的实际 cosign 文件路径 |
| `P10_GITHUB_READ_TOKEN` | 读取公开 CP6/Platform 源码、运行与 artifact |
| `P10_FEED_READ_TOKEN` | 读取固定 GitHub Packages/GHCR |
| `P10_R2_CONSUMER_ACCESS_KEY_ID` | 固定 bucket 的只读身份 |
| `P10_R2_CONSUMER_SECRET_ACCESS_KEY` | 该只读身份的 secret |

`prepare-validation` 只需 GitHub/feed 和额外 `P10_CRM_READ_TOKEN`；工作流将已有 Environment secret `P10_CRM_ACTIONS_READ_TOKEN` 映射到后者。私有原始 CRM 响应不进入公开候选，后续消费者验证公开、经签名候选绑定的净化结果，无需 CRM 凭据。`finalize-validation` 只需 GitHub/feed/cosign。

发布准备需 GitHub/feed/cosign 与 `P10_R2_PUBLISH_ACCESS_KEY_ID`、`P10_R2_PUBLISH_SECRET_ACCESS_KEY`；bundle/commit 另需 consumer pair。签名由独立 workflow step 使用 `P10_OCI_COSIGN_PRIVATE_KEY/PASSWORD` 或 `P10_LOCATOR_COSIGN_PRIVATE_KEY/PASSWORD`，不是 CLI 功能。私钥通过 `env://` 传给 cosign，不落盘。

读取入口拒绝携带上述 signing/publisher secrets、`COSIGN_PASSWORD`、CRM 私有读取变量。发布 CLI 也拒绝 signing/CRM secrets。pre-commit 子进程清空继承环境，只保留明确只读输入、必要系统变量和真实作业身份。运行在 GitHub 上的收集/发布阶段还会逐项核对 [16 项当前作业环境](../../tools/p10/ReleaseVerifier/S06CurrentWorkflowChecks.cs)以及真实 GitHub API；人工填写这些变量不能模拟成功运行。

## 三条工作流

三者仅 `workflow_dispatch`、GitHub-hosted Ubuntu 24.04、受保护 `p10-platform-candidate` Environment，需人工批准。只有验证工作流有 `packages: write`；发布与审计均为 `packages: read`。固定 action commit，不允许自动跳过错误。

| 工作流 | 输入 | 输出 |
| --- | --- | --- |
| [p10-platform-validation.yml](../../.github/workflows/p10-platform-validation.yml) | `expected_sha` | 一次构建的 OCI digest、实际 SBOM/扫描/签名、immutable validation artifact |
| [p10-platform-candidate.yml](../../.github/workflows/p10-platform-candidate.yml) | `expected_sha`、`release_tag`、`validation_run_id`、`validation_attempt`、`validation_artifact_id` | 固定 Locator/bundle、签名意图 artifact、pre/postcheck |
| [p10-platform-audit.yml](../../.github/workflows/p10-platform-audit.yml) | `expected_sha`、`release_tag` | 已完成候选的只读验证结果，按结果原始字节 SHA-256 命名 |

Artifact 名称固定为 `p10-s06-{validation|intent|audit}-{fullSHA}-{runID}-{attempt}`，保留 90 天、禁止覆盖、缺文件失败。GitHub Artifact 不是永久归档或 Object Lock；最终审计须将决策与所需公开证据持久留存。

运行时间校验区分执行与 attempt 记录创建。attempt 1 保持 `created_at <= run_started_at <= updated_at <= cutoff`。重跑额外通过固定 GitHub API 读取同一 run 的 attempt 1，严格匹配 run/repository/head repository/source/branch/workflow/event，要求其已完成且原始时间顺序成立、结束不晚于选中重跑开始；重跑记录创建位于原始创建与本次更新之间。本次 `start <= updated <= cutoff`、精确 attempt/job 和成功结论仍须成立。前一次失败不等于本次失败，也不能反过来用前一次成功替代本次成功。固定 CRM PR #46/#47 仍只消费已批准的 attempt 1，不扩展选择范围。

## 发现、写入与状态

权威发现路径为 `candidates/platform/TAG/candidate-locator.v1.json` 和相邻 `candidate-locator.v1.sigstore.json`；证据与候选对象使用 `objects/sha256/{hash前两位}/{完整hash}/{fileName}`，摘要与文件名绑定规则见 [ContentAddress](../../tools/p10/ReleaseVerifier/ContentAddressedObjects.cs)。固定 trust store 定义 R2 account/bucket/authority，候选不能指定任意 URL 或账号。

每次创建都是单次 `PutObject(If-None-Match: *)`，不 multipart、不 overwrite/delete。对象冲突必须完整回读相等；bundle 可复用不同 ECDSA 字节，但必须验证它签署同一精确 Locator；Locator 冲突只有原始字节完全相等且签名/策略有效才幂等成功。[R00 勘误](adr/ADR-CRM-R00-RELEASE-AUTHORITY.md)明确本路径不声称 R2 提供 S3 VersionId/Object Lock。

`PublishedUnconfirmed`、`PreCommitVerified`、`PostCommitConfirmed` 都不代表候选已被普通消费者接受。`verify-platform` 的 `VerifiedNonDeployable` 表示当前密码学、内容与已完成工作流验证成功，仍不自动产生跨仓 `Frozen` 决定。所有 Platform 候选验收结果都保持 `deployable=false`。

HIGH/CRITICAL 扫描、签名/时间戳、对象摘要或运行身份错误都失败关闭。不能以修改扫描报告、伪造时间、跳过测试或重写 Locator 恢复。现有 WMS R2、Azure Shadow、PROD 审批及部署路径保持原约束。

## 实现与验证入口

- [Program](../../tools/p10/ReleaseVerifier/Program.cs)、[普通验证](../../tools/p10/ReleaseVerifier/VerifiedPlatformCandidate.cs)、[发布协议](../../tools/p10/ReleaseVerifier/S06Publisher.cs)。
- [本地测试工程](../../tools/p10/ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj)、[正式 restore 配置](../../eng/p10/NuGet.formal.config)、[runtime-only Dockerfile](../../eng/p10/verifier.Dockerfile)。
- [操作步骤与失败处理](HOWTO-P10-PLATFORM-CANDIDATE.md)、[DevOps 入口](README.md)、[已批准 P10 设计](https://github.com/GTX537/CP6.Platform/blob/3ff27e26962dcfd722887afb80a4306010dd9ee1/docs/superpowers/specs/2026-09-01-p10-release-governance-design.md)。
