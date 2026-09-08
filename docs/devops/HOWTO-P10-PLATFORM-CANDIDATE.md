# How to 验证、发布并审计 P10 Platform 候选

用途：完成非部署型 `0.10.1` Platform 候选的受控操作。核验日期：2026-09-08。CLI/权限定义见[参考页](P10-PLATFORM-REFERENCE.md)。本文提供操作步骤，不代表以下真实工作流已经执行。

## 前提

- 三条 P10 workflow 及其受审实现已经按正常 PR 流程合入并通过 exact-main 必需检查。工作目录的 HEAD 不能代替远端 main。
- 既有 `p10-platform-candidate` Environment 仍只允许 main，required reviewer 为 owner `GTX537`；操作人必须在 GitHub 审查并批准每个实际 run。助手不自批，不降低保护。
- 已有九个 Environment secret 名称可用，值不导出；consumer 为 bucket-only read，publisher 为 bucket-only read/write；OCI 和 Locator 私钥分离。
- 使用实际 `0.10.1` 七包与已核验 CRM S05 身份。缺少 feed、私有 CRM 读取、TSA、GHCR 或 R2 访问时停止处理该错误，不能以本机包/日志替代。
- GitHub 仓库公开不代表其 Packages 均公开。2026-09-08 逐包查询只有 `CP6.Platform.Release` 为 public，其余六包为 private；使用 CP6 的 `GITHUB_TOKEN` 时，需在各包的 **Manage Actions access** 授予 `GTX537/CP6` **Read**。Owner 已报告补齐；是否实际可读仍由新的 hosted validation 验证，不据此声称包已公开。
- 以下示例在 PowerShell 中运行，需要已登录的 GitHub CLI；所有候选 Tag 由操作人选择，不能盲用示例身份。该 Tag 不会被推送为 Git tag。

## 1. 验证最终源码并构建一次 OCI

当前先停止重跑：2026-09-08 的原生预检确认已有镜像内 cosign 3.1.3 含 **1 CRITICAL / 14 HIGH** 扫描发现。官方当日最新 release 仍是 3.1.3，尚无更新的官方二进制；在完成受审依赖修复前，下列 dispatch 不能用于反复试跑。不得过滤发现、移除镜像内验签工具以避开扫描，或未经批准替换受信二进制来源。详细原始摘要、命令和证据边界见[预检记录](../superpowers/plans/2026-09-08-p10-native-scan-preflight.md)。

读取远端 main 并提交验证请求：

```powershell
$taskSha = gh api repos/GTX537/CP6/branches/main --jq .commit.sha
if ($LASTEXITCODE -ne 0 -or $taskSha -cnotmatch '^[0-9a-f]{40}$') { throw 'main lookup failed' }
gh workflow run p10-platform-validation.yml -R GTX537/CP6 --ref main -f "expected_sha=$taskSha"
if ($LASTEXITCODE -ne 0) { throw 'validation dispatch failed' }
gh run list -R GTX537/CP6 --workflow p10-platform-validation.yml --limit 20 --json databaseId,headSha,status,conclusion,url
```

在所选 run 页面完成 Environment 审批。工作流执行真实正式包和 CRM 输入验证、全量测试、一次 runtime-only image 构建、原生 SBOM/完整漏洞报告、OCI 签名，并在实际 digest 容器内完成验证。它不发布 Locator。

同源失败运行可在排除原因后重跑，每个 attempt 必须重新取得实际批准。重跑记录的 `created_at` 不是执行开始时间；验证器额外读取并绑定该 run 的 attempt 1，确认原始执行已结束，再检查当前精确 attempt 和 job 的时间。若修复了源码，必须对新的 exact main 发起新 run，不能重跑旧 SHA 来验收新代码。历史失败及其 artifact 保留，不改写成功结论。

输入收集失败时，先查看 stderr 的 `p10-validation-stage`：依次为 `current-workflow`、`platform-source`、`formal-packages`、`crm-consumer`、`publication-archive`、`package-provenance`、`final-current-workflow`，只有输入校验及本地写入完成才输出 `prepared`。阶段标记代表进入该阶段，不代表该阶段成功，也不是候选验收证据。

GitHub 非 200 响应会在原有 `github-http-status` 前输出 `p10-github-read target=<固定类别> status=<数字>`。类别只含 `cp6` / `platform` / `crm` 和 `workflow` / `main` / `compare` / `run` / `jobs` / `archive` / `pull-request` / `commit` / `artifact`；缺少目标上下文时为 `unspecified`。不输出 URL、路径、SHA、请求 ID、Token、响应正文、ReasonPhrase 或 headers。stdout 合同结果、退出码、超时、取消和失败关闭门禁不变。

先按真实阶段、目标类别和状态码调查，不能仅凭 403/404 就扩大权限，也不能要求粘贴 Secret。旧 [run 34229610628](https://github.com/GTX537/CP6/actions/runs/34229610628) 没有这些诊断，只能证明 GitHub 非 200；不能回溯推定其具体状态码、失败仓库或七包验证结论。使用诊断补丁的新 exact-main run 才能定位。

后续 [run 34234554610](https://github.com/GTX537/CP6/actions/runs/34234554610) attempt 1 定位到 `crm.pull-request` 403；owner 调整 CRM 专用 token 的 Pull requests Read 后，attempt 2 的实际输入收集、七包/CRM 验证、1634 项全量测试及 OCI push 均成功。它随后因 Trivy `--image-src registry` 非法而失败，sign/finalize/artifact 全部跳过。Trivy 正确参数是 `--image-src remote`，Syft 则仍使用自己的 `registry:` 前缀，两者不能混用。对已有 digest 的完整原生扫描又建立了上述 cosign 漏洞阻塞；不能把参数修正或本地报告当作 hosted validation 成功。

只有整个 run `completed/success` 后，才能使用它的验证 artifact。保存 run、attempt、job、artifact ID、archive digest 和 image digest。以下读取选中 run 的精确元数据，不用“最近成功”自动替换：

```powershell
$taskValidationRun = Read-Host '输入刚才核对的 validation run ID'
if ($taskValidationRun -cnotmatch '^[1-9][0-9]*$') { throw 'invalid run ID' }
$taskRun = gh api "repos/GTX537/CP6/actions/runs/$taskValidationRun" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'run lookup failed' }
if ($taskRun.head_sha -cne $taskSha -or $taskRun.path -cne '.github/workflows/p10-platform-validation.yml' -or
    $taskRun.status -cne 'completed' -or $taskRun.conclusion -cne 'success') { throw 'validation not accepted' }
$taskAttempt = [string]$taskRun.run_attempt
$taskArtifacts = gh api "repos/GTX537/CP6/actions/runs/$taskValidationRun/artifacts?per_page=100" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'artifact lookup failed' }
$taskArtifactName = "p10-s06-validation-$taskSha-$taskValidationRun-$taskAttempt"
$taskArtifact = @($taskArtifacts.artifacts | Where-Object { $_.name -ceq $taskArtifactName -and -not $_.expired })
if ($taskArtifact.Count -ne 1) { throw 'exact immutable validation artifact missing' }
$taskArtifactId = [string]$taskArtifact[0].id
```

CLI 会进一步复核真实 archive SHA-256、作业时间、24 个原始文件及证据绑定；此脚本的元数据选择不是验收替代品。

## 2. 独立批准并条件发布

选择未使用的新候选身份；如果 main 已前进，停止这次组合并对新的 source 重新验证。发布必须与已完成 validation 使用相同源码。

```powershell
$taskReleaseTag = Read-Host '输入未使用的 Platform 候选身份，例如 v0.10.1-p10.1'
$taskCurrentMain = gh api repos/GTX537/CP6/branches/main --jq .commit.sha
if ($LASTEXITCODE -ne 0 -or $taskCurrentMain -cne $taskSha) { throw 'main changed; validate the new source first' }
gh workflow run p10-platform-candidate.yml -R GTX537/CP6 --ref main -f "expected_sha=$taskSha" -f "release_tag=$taskReleaseTag" -f "validation_run_id=$taskValidationRun" -f "validation_attempt=$taskAttempt" -f "validation_artifact_id=$taskArtifactId"
if ($LASTEXITCODE -ne 0) { throw 'publication dispatch failed' }
gh run list -R GTX537/CP6 --workflow p10-platform-candidate.yml --limit 20 --json databaseId,headSha,status,conclusion,url
```

审查真实 run 后人工批准。流水线依次执行内容寻址对象回读、Locator 签名/bundle 验证、immutable intent upload、清空环境的只读 pre-commit、最终条件 Locator 创建及新进程只读 postcheck。

不要手工跳过步骤、粘贴本地 Locator 到 commit 命令或把 `PublishedUnconfirmed` 当作 Frozen。即使 Locator 已创建，也必须等整个发布 workflow 成功结束。

## 3. 运行发布后的普通只读验证

确认所选发布 run 和 `publish` job 已完成成功，再读取审计实现的当前 main 并提交只读审计：

```powershell
$taskAuditSha = gh api repos/GTX537/CP6/branches/main --jq .commit.sha
if ($LASTEXITCODE -ne 0 -or $taskAuditSha -cnotmatch '^[0-9a-f]{40}$') { throw 'main lookup failed' }
gh workflow run p10-platform-audit.yml -R GTX537/CP6 --ref main -f "expected_sha=$taskAuditSha" -f "release_tag=$taskReleaseTag"
if ($LASTEXITCODE -ne 0) { throw 'audit dispatch failed' }
gh run list -R GTX537/CP6 --workflow p10-platform-audit.yml --limit 20 --json databaseId,headSha,status,conclusion,url
```

该 job 仅使用只读凭据，运行普通 `verify-platform`，不重建 OCI、不签名、不写 R2。成功输出 `VerifiedNonDeployable`、`candidateAccepted=true`、`deployable=false`，并绑定 validation/publication run IDs 和候选 SHA-256。审计 artifact 中只有按原始结果字节 SHA-256 命名的 JSON 文件；hash 包括 CLI 最后的换行，不是候选 hash。

如需从其他已授权只读环境复核，使用受审源码发布的 CLI 与参考页的五项只读变量，执行：

```text
dotnet CP6.P10.ReleaseVerifier.dll verify-platform TAG
```

不得从 Environment 导出 publisher/signing secrets 到本机，也不能在同一进程混入它们。

## 4. 留存真实审计决定并同步项目状态

在审计 workflow 完成后，独立核对其完整 source SHA、workflow blob、run/attempt/job 的成功结论、artifact ID/expiry/archive digest，以及内部结果文件的 hash。继续对账正式七包、Platform/CRM 精确 PR/main 门禁、当前 trust policy、候选/Locator/evidence hash 和 OCI digest。

只有这些实际证据齐全，才能新增 `Frozen / Consumable` 决定。以新的原始 JSON 审计条目记录决定、UTC、前一条目 hash（若存在）、精确候选身份、验证结果 hash、全部运行/artifact 身份及仍为 `deployable=false` 的边界；按条目原始字节 SHA-256 命名并追加到版本管理的审计目录。首次建立目录时同时添加 README。旧条目只读，不更新或覆盖；缺证据就记录 Candidate / No-Go，不能预填运行成功。

同一次状态任务更新四份[项目台账](../project-memory/PROJECT_STATE.md)、[已完成](../project-memory/05-Completed.md)、[待办](../project-memory/06-Todo.md)和[变更日志](../project-memory/CHANGELOG-AI.md)，通过正常分支/PR/必需检查，并确认远端 main 包含审计提交。GitHub Artifact 90 天后会过期；在到期前留存所需公开原始证据及其摘要，不能只保留一个即将失效的链接。永久留存不等于当前消费者可以跳过它要求的实时验证。

该决定只完成 P10 非部署型 Platform 引用候选；不授权 WMS/CRM System release、DEV/UAT/PROD、数据库迁移或部署。

## 故障处理

| 故障 | 处理 |
| --- | --- |
| `s06-current-*`、源 SHA/环境不匹配 | 检查真实 main、dispatch、job 与审批，不人工伪造环境变量 |
| `s06-current-time`、重跑时间顺序 | 核对精确 attempt 与同 run 的 attempt 1；历史 run `34221083970` attempt 2 的记录创建比开始晚两秒，旧实现误拒绝。使用已合入重跑校验修复的新源码；不得删除时间校验或伪造原始记录 |
| feed/CRM 401 或 403 | 核对既有读取身份的实际仓库/package 授权；不要改用宽权限生产凭据 |
| TSA、作者指纹、包 hash 错误 | 保留错误和原包，按正式发布/信任的前向修正流程处理 |
| SBOM/scan 格式或 HIGH/CRITICAL 不通过 | 查实际报告和镜像；修正源码/依赖后新验证，不过滤原始发现 |
| `publication-tag-used` / bundle 或 Locator 冲突 | 保留原对象；只有工具验证精确意图可幂等，整次重跑选择新候选身份 |
| pre-commit 失败 | 没有权威 Locator 提交；孤立内容对象不构成候选，不自动删除 |
| postcheck / normal verify 失败 | 不覆盖 Locator；Candidate / No-Go，确认内容错误后记 Rejected，并前向使用新身份 |
| artifact 过期/丢失、实际工作流不成功 | 不用复制 JSON 或本地日志冒充 immutable handoff；重新产生受验证的新身份 |
| 输出目录已存在/有额外文件 | 使用新的受控临时目录；不要递归清理用户目录来绕过限制 |

[返回 DevOps](README.md) · [参考页](P10-PLATFORM-REFERENCE.md)
