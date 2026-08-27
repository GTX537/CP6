# Space Studio WP3 远程隔离 CAD Worker Provider

日期：2026-08-27

## 结论

本任务补齐了 CP6 生产侧远程 CAD Provider 接入层，并交付一个可运行的 AutoCAD Core Console DWG 候选 Worker。Design API 不加载供应商 SDK、不启动 CAD 子进程；运行注册默认关闭，只有精确 Provider 版本、部署批准 Manifest、Manifest 外部 SHA-256、mTLS 客户端证书和服务端证书 Pin 全部有效时才会启动。

这是 WP3 的仓库实现切片，不是 WP3 或 3D Space GA 的正式接受。当前仍没有授权黄金 CAD、许可证与客户/Site 批准、生产隔离部署证明、真实 DXF 链、独立 Backup Provider 或生产等价故障切换证据，因此 WP3 保持 `Partial / Pending`，`acceptedEvidence` 保持为空，Space 总体保持 72% / `NoGo`。

## 已交付

- CAD-only Worker 协议 v1：请求只含 Attempt、源文件 SHA-256、DWG/DXF 格式和精确 Provider Key/Version；不发送 Tenant、Site、用户、模型、数据库、Mapping Profile、对象存储或业务凭据身份。
- HTTPS 流式客户端：限制源文件和响应大小、墙钟超时、固定媒体类型，复核响应身份、CAD IR 文档身份及规范化 Package SHA-256；错误不记录原始 CAD 或响应正文。
- 生产 Provider：Preparation 接收 Worker CAD IR；Parse 在 CP6 内加载不可变 Mapping Profile 精确版本，复核 Definition Hash 和完整 Layer Overrides，重放 Mapping Snapshot 后生成 Layer Inventory、语义、诊断与 PreviewSet。Worker 不能选择 Mapping，也不能写 Draft。
- 失败关闭的运行注册：部署 Manifest 必须与 Provider、版本、Endpoint、格式、部署模式、数据边界和证书完全一致；资格分数至少 80，并包含黄金集、冻结环境、Worker Release、许可证、安全、区域、删除保留、身份和证书证据。过期、篡改、占位或缺失证据在启动时被拒绝。
- mTLS 与证书固定：客户端证书必须来自指定证书库、唯一、有效且含私钥；服务器同时通过 CA/主机名验证和证书原始 DER SHA-256 固定，并开启吊销检查。
- AutoCAD 候选 Worker：只接受原生 DWG，完整落盘并核对源 SHA-256 后，才通过 `SpaceCadConverterContractRunner` 执行 Core Console；只返回 CAD IR，并在成功返回前删除每次 Attempt 的原始和派生目录。清理不跟随 Reparse Point。

协议与部署合同见：

- `docs/space/contracts/cad/v1/remote-worker-protocol.md`
- `docs/space/contracts/cad/v1/remote-worker-approval.schema.json`
- `docs/space/acceptance/v1.3-ga/remote-worker-approval-template.json`
- `tools/CP6.Space.CadWorker.AutoCadCandidate/README.md`

模板故意包含过期和占位值，不能作为批准 Manifest；生产配置也默认 `Enabled=false`。

## 本机真实候选验证

安装型测试实际调用 `D:\AutoCAD 2025\accoreconsole.exe`，版本 `25.0.58.0.0`，输入 Autodesk 安装样例 `Floor Plan Sample.dwg`，源 SHA-256 为 `19270c23e56e407aab2ade3644e8f301c34e390638d99c3f0cc4f2d3a6516792`。通过完整远程 Worker 服务边界得到：

| 指标 | 结果 |
|---|---:|
| 图层 | 29 |
| 块 | 19 |
| 实体 | 4,424 |
| 支持实体 | 4,422 |
| Provider 版本绑定 | 通过 |
| 每次 Attempt 原始/派生 CAD 清除 | 通过 |

该文件是 Autodesk 开发样例，不是客户授权黄金集；结果只能证明候选 Worker 真实调用边界和清理合同，不能计入 ADR-0001 评分、WP7 或 GA 接受。

## 自动化与构建

- 远程 Provider/协议/HTTP/批准 Manifest：4/4 通过。
- 既有 Provider 路由：16/16 通过。
- 候选 Worker 常规测试：2 通过；安装环境测试独立运行 1/1、0 skipped。
- CAD Experiment：41 通过；两个安装门禁分别独立运行 1/1、0 skipped。
- Space UnitTests：550/550、0 skipped。
- Space IntegrationTests + SQL Server LocalDB：462/462、0 skipped。
- `CP6.Tests`：2,939 passed / 19 environment-gated skipped / 0 failed。
- `CP6.slnx` Release：0 warning / 0 error，包含候选 Worker。

GA 当前状态验证和 35/21/31/22/8 个证据、Pilot、黄金 CAD、开工与单人身份失败关闭场景均通过。合并候选还必须通过完整差异审查和远程 required checks；最终结果以 PR 和 post-merge 记录为准。

## 未完成与下一门禁

1. 形成发布身份，不再以 `development` Provider Key 作为生产候选，并冻结 Worker Release/环境哈希。
2. 明确 AutoCAD 自动化部署许可证边界，取得安全、客户、Site、数据区域、删除保留、身份和证书批准；在真实隔离环境证明无出口、无业务凭据、加密临时盘和清除失败关闭。
3. 补齐真实 DXF 支持，并选择一个技术和故障域独立的 Backup Provider。
4. 在同一 20 份授权真实黄金集、同一冻结规则上评测 Primary/Backup；两者均达到 80 分以上并由 Site 认证。
5. 使用真实 mTLS 部署、Site 当前配置和生产等价 SQL 执行 DWG/DXF、主链故障切换、无未批准云传输、恢复与观测 E2E。

上述事项可由同一个实名 `DeliveryOwner` 执行、复核并签署，不再要求多人门禁；真实输入、可重复运行和失败关闭证据标准不降低。
