# ADR-DEVOPS-001：CP6 发布权威与 Registry

- 状态：**Accepted**
- 决策日期：2026-08-26
- 适用范围：当前 CP6 单仓候选、WMS R2 和 Azure Release/CD 演进
- 不包含：创建 ACR、购买 Azure 资源、生产部署、CRM 多仓 System Release Manifest
- 替代条件：只有新的受审 ADR 可以改变本决策

## 1. 决策

当前阶段继续使用 **GitHub R2 + GHCR** 作为 CP6 的唯一候选权威：

1. 候选 Registry 固定为 `ghcr.io/gtx537/cp6-api` 与 `ghcr.io/gtx537/cp6-web`。
2. 唯一构建入口是 `.github/workflows/r2-candidate.yml`。它只接受由 `.github/workflows/r2-freeze.yml` 创建、指向当前 `main` 的受保护 annotated `vX.Y.Z` Tag。
3. 唯一组件候选清单是 R2 Schema 2 `release-manifest.json`；`candidate-result.json` 是该清单、冻结快照、执行规范、版本和 Git SHA 的不可变根指针。
4. GitHub R2 继续负责一次构建、GHCR Push、provenance/SBOM、High/Critical 漏洞门禁、原生客户端签名、证据归档和受保护环境部署。
5. Azure 不为同一版本重新 Build、Push、签名或生成第二份候选清单。Azure 只允许读取 GitHub R2 已产生的同一 Tag、digest、清单和证据，生成明确标注 `Authority=Shadow` 的验证报告。
6. ACR 当前不创建、不授权、不作为复制目标。未来迁移 ACR 必须另立 ADR，并完成第 9 节全部门禁。

这项决策是对当前已实现发布链的收敛，不表示现有 R2 已取得真实生产输入、候选或上线批准，也不把本机 DEV 学习链升级为生产链。

## 2. 为什么选择 GHCR，而不是现在迁移 ACR

| 判断项 | 继续 GHCR | 现在迁移 ACR |
| --- | --- | --- |
| 已有可执行候选链 | 已有 R2 Freeze/Candidate/Deploy | 需要重新实现并证明等价 |
| Build once | 当前 Buildx 只生成一组 digest | 若 Azure 再 Build，会立即产生双重候选 |
| 清单与证据 | Schema 2、candidate result、Object Lock 链已存在 | 需设计复制/清单/证据的新权威 |
| 凭据与权限 | 现有 R2 已接通 GHCR | 需新增 ACR、Service Connection 与治理成本 |
| 当前基础设施 | 不增加付费资源 | 当前没有已批准的 ACR 或独立 Azure Release 主机 |
| 回退 | 维持现状，不改变部署输入 | 切换失败时需恢复 Registry、清单和入口 |

ACR 可能在未来的 Azure 原生生产架构中更合适，但“将来可能使用”不是现在引入第二真相源的理由。

## 3. 唯一权威对象

| 对象 | 唯一权威 | 身份规则 |
| --- | --- | --- |
| 源码 | GitHub `main` | 候选 Tag 必须指向创建时的当前 `main` 完整 SHA |
| 版本入口 | R2 Freeze 生成的 annotated `vX.Y.Z` Tag | 失败版本不删除重打，使用更高 patch |
| API/Web 镜像 | GHCR | 运行时只接受 `repository@sha256:digest` |
| 组件候选清单 | Schema 2 `release-manifest.json` | SHA-256 由 `candidate-result.json` 绑定 |
| 候选根指针 | `candidate-result.json` | 绑定版本、Tag、Git SHA、manifest、freeze、spec |
| 冻结输入 | `release-freeze.json` + versioned candidate spec | URI 与 SHA-256 写入 annotated Tag 和 manifest |
| 候选/部署证据 | R2 S3 兼容对象存储 | 版本控制、Object Lock、URI 与内容哈希；当前清单未记录 S3 VersionId |
| 生产部署权威 | `.github/workflows/r2-deploy.yml` | 受保护 Environment、同一 manifest/digest、身份复核 |
| Azure 影子输出 | Azure Pipeline Artifact | 必须标记非权威，不得被部署 Job 当作候选输入 |

Azure Artifact、Azure Run 号、GHCR 可变 Tag、本机 Docker image ID 和 DEV `deployment.json` 都不能替代上述候选链。

## 4. Azure 影子期

Azure 影子验证分为三步，任何一步都不改变候选：

### S0：仓库设计与离线合同

- 只提交设计、解析器和 fixture 合同。
- Pipeline 保持 `trigger: none`、`pr: none`，不创建 Registry Service Connection。
- 用固定 fixture 证明错误版本、错误 SHA、错误 manifest hash、可变 Tag、缺失 digest 和越权权限会失败关闭。

### S1：真实候选只读验证

- 手动选择一个已存在的 R2 `vX.Y.Z` 候选。
- 从权威对象存储读取 `candidate-result.json`、Schema 2 manifest、冻结快照和执行规范，并逐层重算 SHA-256。
- 读取 GitHub Tag/main 关系，确认版本、完整 Git SHA 和 annotated metadata 一致。
- 使用 GHCR 只读身份验证 API/Web digest 可解析、可拉取；不得 Push、重新标记或重建。
- 生成 `azure-shadow-report.json`，记录权威 URI、hash、digest、验证结果、Azure Run ID 和 `Authority=Shadow`。

### S2：等价观察

- 至少三个连续、不同 SemVer 候选完成 S1，失败场景能定位到矩阵中的具体门禁。
- 可在独立、容量受控的非部署 Agent 上对同一 digest 重跑 SBOM/漏洞扫描；结果只能作为对比证据，不能覆盖 R2 报告。
- S2 完成后仍不自动获得候选或生产发布权威。任何权威切换必须执行第 9 节。

## 5. GitHub R2 与 Azure 等价矩阵

| 能力 | 当前 GitHub R2 证据 | Azure 影子要求 | 当前结论 |
| --- | --- | --- | --- |
| 版本格式 | `vX.Y.Z` 校验 | 同规则解析 | 设计可复用 |
| Tag 创建权限 | Freeze Environment + 最小权限 GitHub App | 只读验证，不创建 Tag | Azure 禁写 |
| Tag 指向当前 main | candidate source gate | 读取并比对完整 SHA | 必须实现 |
| annotated freeze metadata | URI/hash/spec 写入 Tag | 逐字段与逐 hash 验证 | 必须实现 |
| 源码/客户端/Web 门禁 | R2 source gate | 引用权威报告，不重复宣称 | 只读 |
| SQL Server 集成 | `WmsProductionSqlServerTests` | 验证 TRX 证据哈希 | 只读 |
| WMS E2E | candidate source gate | 验证 source report 引用 | 只读 |
| 一次构建 | Buildx images job | 严禁 Azure Build | 已决策 |
| 版本/SHA 注入 | Docker build args | 核对运行身份字段和 manifest | 必须实现 |
| provenance | Buildx `--provenance=true` | 验证权威候选存在对应证明 | 后续实现 |
| SBOM | Buildx + Syft/CycloneDX | 验证文件哈希；可选独立对比 | 后续实现 |
| High/Critical 门禁 | Trivy exit code 1 | 验证报告哈希；可选同 digest 重扫 | 后续实现 |
| API/Web digest | GHCR Push 后读取 metadata | 只读解析/拉取 manifest digest | 必须实现 |
| 原生客户端签名 | Authenticode/APK signer 校验 | 验证 Schema 2 signer/制品哈希 | 必须实现 |
| Schema 2 manifest | `test-r2-artifacts.ps1` | 严格解析并拒绝未知/缺失关键字段 | 必须实现 |
| 不可变证据 | S3 versioning + Object Lock；URI/hash 已绑定，VersionId 尚未进入清单 | 按 URI 读取并重算内容哈希；对象被同 key 新版本遮蔽时失败关闭 | 必须实现 |
| db-init/ForwardOnly | manifest + deploy script | 核对 migration/init artifact，不执行 DB | 只读 |
| 受保护部署 | GitHub Environment + deploy runner | Azure 影子没有部署权限 | Azure 禁部署 |
| 运行身份核对 | API/Web/digest/migration/远程制品 | 引用权威 deployment evidence | 非候选门禁 |
| 证据保留 | R2 对象存储策略 | Shadow Artifact 仅辅助检索 | 不替代权威 |

Azure 的“成功”只表示它读到并验证了 GitHub R2 候选，不能表述为“Azure 生成候选”“Azure 发布成功”或“生产已上线”。

## 6. Service Connection 与身份边界

| 身份/连接 | 最小权限 | 明确禁止 |
| --- | --- | --- |
| GitHub repository connection | 读取 repo、Tag、commit、workflow metadata | Push、建删 Tag、改分支保护、写 Packages |
| GHCR shadow credential | 仅拉取 `cp6-api`/`cp6-web` 指定 digest | Push、delete、package admin、写可变 Tag |
| R2 evidence reader | 只读批准 bucket/prefix 的对象，并按候选记录的 SHA-256 验证 | Put/Delete、改 retention/Object Lock、列出无关 bucket |
| Azure Pipeline identity | 读取批准 Variable Group、发布 Shadow Artifact | Azure Environment 部署、PROD Secret、生产网络、数据库写入 |
| Azure Artifact storage | 写入当前 Run 的 `azure-shadow-report` | 作为 release manifest 或 deployment input |

Secret 只放在受保护的 Azure Variable Group/Service Connection 或外部 Secret Store；YAML、日志、summary、Artifact 和 Git 均不得包含 Token、Cookie、连接串或私钥。当前 `Default` CI Agent 和 `CP6-Deploy` 部署身份继续分离；Shadow 不复用 PROD 身份。

## 7. Phase 3 YAML 约束

Phase 3 的设计见 [Azure Docker Release Shadow 设计](../AZURE-DOCKER-RELEASE-SHADOW-DESIGN.md)。实现必须满足：

- 文件独立于 `azure-pipelines.yml` 和 `azure-pipelines-dev.yml`；
- 初始只允许手动运行，且默认不绑定任何 Registry 写连接；
- 只接受完整 SemVer 和权威 `candidate-result.json` 根指针；
- 每层对象先校验来源和 hash，再消费下层；
- 不调用 `docker build`、`docker buildx build --push`、`Docker@2 buildAndPush`、`az acr build/import` 或任何部署脚本；
- 输出名称必须包含 `shadow`，Schema 内固定 `Authority=Shadow`、`Deployable=false`；
- 未取得独立 Agent 容量证明前，不在承载本机 SQL/Docker 公网环境的 Agent 上拉取/扫描大型镜像。

## 8. 当前回退

当前变更只增加规则和未来只读影子设计，因此回退不需要改变候选或运行环境：

1. 禁用或删除 Azure Shadow Pipeline definition；
2. 撤销其只读 GHCR/evidence 凭据；
3. 保留 GitHub R2 Freeze/Candidate/Deploy、GHCR digest 和 Schema 2 清单不变；
4. 核对没有 Azure 创建的 Tag、Package、manifest 或 deployment；
5. 在 30 分钟内恢复到“只有 GitHub R2 可生成候选”的已知状态。

历史 Shadow 报告保留为审计证据，但没有部署资格。

## 9. 未来迁移 ACR 的硬门禁

以下条件全部满足前，不得创建生产用途 ACR Push/Import 或声明 Azure 成为发布权威：

1. 新 ADR 明确 ACR SKU/区域/保留/网络、唯一候选清单、权威 Pipeline 和切换时间。
2. Azure 对第 5 节全部门禁达到等价，并以至少三个连续候选通过独立验收。
3. ACR Service Connection 使用最小权限和工作负载身份，CI/Release/Deploy 身份分离。
4. 迁移采用受控 digest 复制/导入，不从源码重建同一版本；复制后逐平台 manifest digest 与内容身份可证明一致。
5. 若复制不能保持批准的 digest/身份链，必须创建新的 SemVer 候选，不能静默改写旧清单。
6. 切换窗口内只有一个写权威；旧 GitHub R2 保持可恢复但不并行生成同版本候选。
7. 回退脚本、权限撤销、DNS/部署输入恢复和最长恢复时间已在非生产环境演练。
8. Security、Release、Operations Owner 对固定决策内容和等价报告完成批准。

## 10. 验收结论

- Registry：**GHCR**。
- 候选权威：**GitHub R2**。
- 候选清单：**Schema 2 `release-manifest.json` + `candidate-result.json` 根指针**。
- Azure 模式：**只读 Shadow，不 Build、不 Push、不签名、不部署**。
- ACR：**未批准、未创建、未授权**。
- 生产影响：**无**。
