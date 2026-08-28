# Space AutoCAD 候选 Worker 不可变 Release 身份

> 当前口径：本文原始退出清单中的独立 Backup 是历史要求。Lean Core GA
> Schema 3 与 `cad-provider-adr-0001-v2` 已改为一个合格 Primary 即可；
> Release 身份、隔离、黄金集评分和质量门槛仍必须真实完成。

日期：2026-08-27

## 结论

AutoCAD 候选 Worker 的可运行 Host 不再以 `cp6-autocad-worker-development` 启动。发布目录必须先生成 `cp6-space-cad-worker-release.json`，启动时再用部署外部提供的完整 SHA-256 验证清单，并逐一核验所有发布文件、源提交、Runtime、真实 `accoreconsole.exe` 的完整哈希/文件版本以及托管 DXF Converter 版本。通过后才派生 `cp6-autocad-worker` 和包含 Release 哈希前 12 位的 Provider Version；远程协议 Schema 2 另把部署批准 Manifest 的完整 Release SHA 贯穿 API 请求、Worker 前置核对与响应回显验证。

这关闭的是可复现、不可变和可路由的候选 Release 身份合同，不是正式 Provider/Site 接受。仓库没有许可证批准、授权黄金 CAD、独立 Backup、生产 mTLS/禁网/身份/临时盘证据，也没有生成可写入 `acceptedEvidence` 的正式 Release；WP3 继续 `Partial/Pending`，Space 总体继续 72% / `NoGo`。

## 失败关闭边界

- Release Manifest 固定 Schema 1、非 development Provider Key、SemVer、40 位源提交、Runtime、Core Console 版本/哈希、DXF Converter 版本以及按 ordinal 排序的完整文件清单。
- Manifest 必须位于发布根目录，最大 2 MiB；Payload 最多 5,000 个普通文件，不接受 reparse point、绝对/穿越路径、重复、乱序、缺失入口 DLL、额外文件或 Manifest 自包含。
- `CP6_SPACE_CAD_WORKER_RELEASE_SHA256` 必须是精确小写 SHA-256；Manifest、Payload、Runtime、Core Console、DXF 版本任一漂移均在 Kestrel 启动前失败。
- DWG 每次转换前由 `ReleaseBoundAutoCadDwgExporter` 再次核验 Core Console 完整哈希；启动后替换可执行文件不会进入供应商调用。DXF 不启动 AutoCAD，但仍受同一 Worker Release 身份约束。
- `/health/live` 暴露完整 `workerReleaseSha256`、源提交与 Runtime；部署批准 Manifest 的 `workerReleaseSha256` 必须与其完全一致。API 客户端拒绝不匹配批准运行时的请求，Worker 在落盘前拒绝错误完整 SHA，API 也拒绝不同 Release 的响应。Provider Version 中的 12 位前缀只用于路由可见性，不能替代完整哈希。
- Payload 目录只保存发布文件与 Release Manifest；证书、配置、日志、工作数据和 AutoCAD Runtime Cache 均在目录外。
- Worker 根目录绝对路径最大 120 字符；更长路径在启动时失败关闭，避免 Core Console 到转换中途才用“文件名无效”拒绝嵌套脚本路径。

## 真实本机发布演练

从任务工作树发布 framework-dependent `win-x64` Payload，再由发布后的可执行文件生成并立即复核清单：

| 项目 | 结果 |
|---|---|
| 演练版本 | `0.0.0-rehearsal`（明确不可接受为正式 Release） |
| Payload 文件 | 18 |
| Worker Release SHA-256 | `0df5e933860f7b677bc70d5f5a0d2f406efba99d764175fe030547ce5080aefa` |
| Provider Key | `cp6-autocad-worker` |
| Provider Version | `0.0.0-rehearsal+worker.0df5e933860f.autocad.25.0.58.0.0.dxf.1.1.0` |
| Core Console | `D:\AutoCAD 2025\accoreconsole.exe`, File Version `25.0.58.0.0` |
| Release Schema | PowerShell `Test-Json -SchemaFile` = `True` |

演练清单的 `sourceCommit` 使用当时 `main@5a7b95c1ec5846707319659a4e097f2457899c3a`，而任务实现尚未合并，因此该演练刻意不具备正式 Release 身份。正式候选必须在本任务合并后的精确提交上重新 publish/seal，并将完整新哈希带入部署批准链。

### 合并后精确主干重建

PR #46 在 7/7 required checks 通过后合并为 `main@4375c7c2fc1e297bf3fe845873b1af5af2cb5d66`。随后从该精确主干重新 publish/seal，并再次执行 Schema 验证：

| 项目 | 结果 |
|---|---|
| 演练版本 | `0.0.0-rehearsal.postmerge`（明确不可接受为正式 Release） |
| Payload 文件 | 18 |
| Worker Release SHA-256 | `c51c2ce8925f7bf2bf647dd2d958270d7903e6adc212eee37a668bfe9d82dc84` |
| Provider Key | `cp6-autocad-worker` |
| Provider Version | `0.0.0-rehearsal.postmerge+worker.c51c2ce8925f.autocad.25.0.58.0.0.dxf.1.1.0` |
| Core Console | File Version `25.0.58.0.0`，SHA-256 `d1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb` |
| Release Schema | PowerShell `Test-Json -SchemaFile` = `True` |

这证明合并后的源码可以生成与精确主干提交绑定的不可变制品；版本仍带 `rehearsal`，且没有许可证、Site 批准或生产等价隔离部署，因此不能写入正式 `acceptedEvidence`。正式候选仍须使用批准的 SemVer，从届时精确主干重建并把完整新哈希写入同版本部署批准 Manifest。

## 自动化验证

| 门禁 | 结果 |
|---|---:|
| Release 生成/重载、Payload/Core/Manifest 篡改、非 development 身份、错误绑定 Exporter/超长工作根拒绝、每次 DWG 前 Core 复核 | 9/9 |
| 完整 CAD Experiment + 两项真实安装门禁 | 57/57，0 skipped |
| 远程 Schema 2 最小请求、完整 Release 请求/响应绑定、批准 Manifest | 6/6 |
| 真实 Core Console DWG 回归 | 29 图层、19 块、4,424 实体、4,422 支持实体 |
| 安装测试根残留 DWG/DXF / 非空 Attempt | 0 / 0 |
| `CP6.Tests` | 2,939 passed / 19 environment-gated skipped / 0 failed |
| `CP6.slnx` Release | 0 warning / 0 error |

PR #46 的 7/7 required checks 已通过；合并后 Release 身份专项 10/10、远程 Schema 2 专项 6/6。GA 验证器继续正确返回 `NoGo`：3 个外部输入、9 个结果门禁和 1 个单人签署 Pending。

## 仍需真实关闭

1. 从合并后的精确 Git 提交构建正式 SemVer Release，并把完整 Worker Release SHA 写入同一版本的部署批准 Manifest。
2. 取得 AutoCAD Worker 商用/SaaS/客户数据处理的许可证、Security、Region、Retention/Deletion 与 Site 批准。
3. 在真实 mTLS、禁网、专用身份和加密临时盘环境部署，证明健康身份、Raw 清除和 Failover。
4. 接入技术、供应商和故障域独立的 Backup，在同一 20 份授权黄金集与 50 MiB 标准 CAD 上冻结评分。

上述实现、运行与签署可由同一实名 `DeliveryOwner` 完成，不要求多人门禁。
