# Space AutoCAD Primary Release 绑定黄金集评测

日期：2026-08-27

## 结论

候选 Worker 新增 `evaluate-release` 命令。它先完整复核已封存的 Worker
Manifest、Payload、Source Commit、Runtime 和 AutoCAD Core Console，再把
受控 20 份黄金 CAD 逐份通过 `AutoCadCandidateConversionService` 和
`SpaceCadConverterContractRunner` 转换两遍。报告绑定精确 Worker Release、
Provider Version、数据集 Manifest、Source Set、Golden Dataset 和冻结环境
哈希，不再把 development converter 的 20/20 报告冒充 Release 结果。

评测器固定检查：

- 20 份、10 DWG/10 DXF、10/5/5、L1～L5 各 4 和 AC1032；
- 原始文件大小/SHA、不可变原创授权口径和 Holdout 禁止调参；
- 每次响应的完整 Worker Release SHA、Provider 身份和 CAD IR 合同；
- 逐文件双跑 Package SHA 确定性、单位/坐标、SourceRef、Blocking Issue；
- 总支持实体比例至少 99%、单文件不超过 120 秒；
- `finally` 清理后的 Attempt 目录和 DWG/DXF 残留均为 0。

输出受
`docs/space/contracts/cad/v1/autocad-primary-evaluation.schema.json` 约束，
报告使用 `CreateNew`，已有证据文件不会被静默覆盖。

## 真实 RC 预演

在加入清单外 CAD 拒绝检查后的任务分支提交
`1dfd176734e1950cc55fd6ffc3abf7db913b8a81` 上封存 `1.0.0-rc.2`，
仅用于在合并前验证新评测器，不作为最终正式 Release：

| 项目 | 结果 |
|---|---:|
| Worker Payload + Manifest 文件 | 19 |
| Worker Release SHA-256 | `281cef51e06b522fa191d2f8cdd52bd124dc98124fcdd6b53ea9b096b885644d` |
| Provider Version | `1.0.0-rc.2+worker.281cef51e06b.autocad.25.0.58.0.0.dxf.1.1.0` |
| 数据集 | 20/20，10 DWG + 10 DXF，10/5/5 |
| 双跑确定性 | 20/20 |
| 实体 | 14,699 总计 / 14,659 支持 / 40 个已报告 VIEWPORT |
| 支持比例 | 99.727873% |
| 缺失 SourceRef / Blocking Issue | 0 / 0 |
| 首跑 P95 / Max | 3.941 秒 / 4.562 秒 |
| 残留 Attempt / 原始 CAD | 0 / 0 |
| 报告 Schema | Pass |
| 报告 SHA-256 | `6f8ca448502181a493aaf142fbedaa11a3261a7a622ba696aa74453d228aa597` |

RC 报告位于仓库外
`D:\CP6-Cad-Evidence\space-autocad-primary\1.0.0-rc.2-1dfd1767\evaluation.json`；
原始 DWG/DXF 与 Worker 二进制均未提交 Git。

## 正式 1.0.0 评测

PR #53 在 7/7 required checks 通过后合并。随后从精确
`main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 重新 publish/seal，
没有复用 RC Payload、Release SHA 或报告：

| 项目 | 结果 |
|---|---:|
| Worker Payload + Manifest 文件 | 19 |
| Worker Release SHA-256 | `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e` |
| Provider Version | `1.0.0+worker.c794e9c0ebbb.autocad.25.0.58.0.0.dxf.1.1.0` |
| 数据集 | 20/20，10 DWG + 10 DXF，10/5/5 |
| 双跑确定性 | 20/20 |
| 实体 | 14,699 总计 / 14,659 支持 / 40 个已报告 VIEWPORT |
| 支持比例 | 99.727873% |
| 缺失 SourceRef / Blocking Issue | 0 / 0 |
| 首跑 P95 / Max | 4.281 秒 / 4.374 秒 |
| 残留 Attempt / 原始 CAD | 0 / 0 |
| Release / 报告 Schema | Pass / Pass |
| 冻结环境 SHA-256 | `c9bbbe362a01e951379d60990f227fc4d5634ac9c86534f009f1d7e87d601717` |
| 报告 SHA-256 | `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350` |

完整、无机器路径和原始 CAD 内容的报告已版本化为
`docs/space/acceptance/v1.3-ga/autocad-primary-evaluation-v1.0.0.json`；
仓库外封存报告位于
`D:\CP6-Cad-Evidence\space-autocad-primary\1.0.0-d2d0a0d1\evaluation.json`，
两者 SHA-256 完全一致。Worker 二进制继续保留在仓库外。

## 证据边界

这次命令以封存 Worker 的直接合同模式运行，没有启动网络监听；原始 CAD
仅进入逐 Attempt 临时目录并已清除。报告诚实记录
`outboundNetworkPolicy=NotVerifiedAtOsBoundary`：当前用户不是 Windows 管理员，
没有创建或伪造 OS Firewall 禁网证明。因此报告可以证明 Release 身份、转换
质量、确定性、性能和删除行为，不能冒充生产 mTLS、OS 禁网或生产部署。

正式 `1.0.0` 与同一 20 份数据的 Release 绑定转换评测已经完成。该报告不含
黄金答案的业务准确率/精确率、受训用户首次 Ready 时间，也不证明 OS 隔离和
Site 安全配置；因此 `PRIMARY_PROVIDER_AND_ISOLATED_WORKER`、WP3/WP7 与 Core GA
继续 Pending/NoGo，不能仅凭本报告写入 `acceptedEvidence`。

## 自动化

- 新评测器正向 20 文件双跑、源文件篡改失败和清单外 CAD 拒绝：3/3。
- 完整 CAD Experiment（含两项真实 AutoCAD 安装门禁）：61/61、0 skipped。
- Worker Release Manifest 与评测报告 JSON Schema：Pass。
