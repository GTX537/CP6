# 02 签名与候选制品

## 1. 触发与权限

候选只由受保护 `vX.Y.Z` Tag 触发
`.github/workflows/r2-candidate.yml`。流水线必须确认 Tag 指向当前 `main`
提交，且 Tag、Desktop 版本、Android display version 一致。签名 Job 只在
受保护 `r2-candidate` Environment 的 `[self-hosted, Windows, X64,
cp6-release]` runner 上运行。

证书私钥、keystore 密码和生产解析配置由 Environment Secret/受控 runner
提供。GitHub Summary 只记录版本、Git SHA、清单哈希、证据 URI、环境和
批准执行人。

## 2. 候选顺序

1. 执行 `scripts/test-r2-source-gate.ps1`，包含 .NET/Web/客户端/部署契约、
   依赖漏洞和 EF 模型检查。
2. 对 SQL Server 2022 执行 `WmsProductionSqlServerTests`。
3. 构建并推送 API/Web OCI 镜像，生成 provenance、SBOM 和漏洞报告；存在
   High/Critical 漏洞时失败。
4. Windows runner 调用 `CP6.Desktop/scripts/publish-msix.ps1` 和
   `CP6.Mobile/scripts/publish-apk.ps1` 完成正式签名。
5. 调用 `scripts/test-r2-artifacts.ps1` 严格核验制品并生成 Schema 2
   `release-manifest.json`。
6. 调用 `scripts/publish-r2-evidence.ps1` 以 SSE、SHA-256 checksum 和
   Object Lock COMPLIANCE 归档。

不在本规范复制脚本中的具体哈希和签名算法；脚本及其契约测试是可执行事实。

## 3. 发布清单 Schema 2

清单至少包含：

- `ReleaseVersion`、40 位 `GitSha`、`GeneratedAtUtc`；
- MSIX、AppInstaller、APK 的文件名、字节数、SHA-256、签名身份和 HTTPS 地址；
- API/Web 镜像 repository 与不可变 `sha256` digest；
- SBOM、漏洞报告、源码门禁报告和 SQL Server 集成报告的 SHA-256；
- 最新 EF 迁移、初始化制品 SHA-256 和 `ForwardOnly=true`；
- `EvidenceRootUri`。

最新迁移由候选提交内的实际迁移生成，不得在文档或脚本常量中维护“最新迁移
名称”。

## 4. 候选验收

- Windows Publisher 与证书 subject 完全一致，MSIX 和 AppInstaller 均验证；
- Android signer SHA-256 与批准值一致，禁止 Debug 证书；
- 下载地址均为最终 HTTPS 地址；
- 镜像已推送并可按 digest 拉取；
- SBOM/漏洞报告/源码门禁/初始化制品哈希完整；
- 清单及全部引用证据在对象存储可读、受版本控制与保留策略保护；
- 清单哈希进入发布审批记录。

任何制品、配置 URL、镜像或证据变化都必须产生新候选，不允许覆盖原清单。
