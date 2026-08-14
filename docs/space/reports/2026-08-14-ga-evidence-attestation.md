# Space Studio GA 证据证明链加固

日期：2026-08-14

范围：Space Studio V1 核心 GA / WP0 治理门禁

结论：证据索引已从“字段形状校验”加固为可追溯证明链；所有真实 Owner、验收和签字仍为 Pending，核心 GA 仍为 No-Go。

## 已关闭的漏洞

原校验器只检查 `acceptedEvidence.sha256` 是否为 64 位十六进制字符，不核对仓库文件是否存在、实际内容是否与声明哈希相同，也没有约束 URI scheme、UTC 时间和占位接受人。因此，不存在的文件和任意哈希理论上也可以通过 Accepted 元数据校验。

## 实现

- Signed Signer、Complete External Input 和 Accepted Gate 统一使用 `uri/sha256/acceptedBy/acceptedAtUtc` 证明对象。
- 仓库相对路径必须位于当前仓库、文件存在，并且重算 SHA-256 与声明值一致。
- 外部受控证据只允许 HTTPS 或 `urn:cp6-space-ga-evidence:*`；拒绝 `file:` 等不安全 scheme、用户信息和越界路径。
- 拒绝把原始 DWG/DXF 放入仓库证据路径，保持客户 CAD 数据边界。
- 接受人不能为 `TBD/Pending/Unknown/N/A/待定/未定`；接受时间必须为以 `Z` 结尾的 ISO-8601 UTC，不能在未来。
- 新 GitHub Actions 工作流在证据索引、报告、项目状态或校验器改变时自动运行当前 No-Go 校验和正反向自测。

## 自动化

`Test-SpaceGaEvidence.Tests.ps1` 覆盖 16 个场景：当前诚实 No-Go、本地证据正确哈希、受控 HTTPS/CP6 URN、Signer/Input 证明对象结构、Signer/Input/Gate 占位 Owner、哈希不一致、文件不存在、原始 CAD 路径、不安全 URI、非 UTC/未来时间和占位接受人。测试只使用明确标记的合成文本 fixture，不会产生 GA 接受证据。

## 状态边界

本变更只保证“将来被标记为 Accepted/Complete/Signed 的证据可追溯”。它没有填写任何真实人名、Provider、黄金 CAD、Site、Pilot 或签字，不提升 72% 基线，也不改变 5 项外部输入、9 个 Gate 和 5 个签字 Pending 的 No-Go 结论。
