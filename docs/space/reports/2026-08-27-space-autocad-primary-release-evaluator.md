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

在任务分支提交 `b88e135c8067f0c7c814d25b8987e74ff02af249` 上封存
`1.0.0-rc.1`，仅用于在合并前验证新评测器，不作为最终正式 Release：

| 项目 | 结果 |
|---|---:|
| Worker Payload + Manifest 文件 | 19 |
| Worker Release SHA-256 | `b8581fb15236a005230bcc7eea33d81add3de8239d89399e78fa5c72aac5013f` |
| Provider Version | `1.0.0-rc.1+worker.b8581fb15236.autocad.25.0.58.0.0.dxf.1.1.0` |
| 数据集 | 20/20，10 DWG + 10 DXF，10/5/5 |
| 双跑确定性 | 20/20 |
| 实体 | 14,699 总计 / 14,659 支持 / 40 个已报告 VIEWPORT |
| 支持比例 | 99.727873% |
| 缺失 SourceRef / Blocking Issue | 0 / 0 |
| 首跑 P95 / Max | 3.952 秒 / 3.954 秒 |
| 残留 Attempt / 原始 CAD | 0 / 0 |
| 报告 Schema | Pass |
| 报告 SHA-256 | `2c1e5482cfd0494403b6296624957f0fd2cb7cb67b279bae3f8458102030b547` |

RC 报告位于仓库外
`D:\CP6-Cad-Evidence\space-autocad-primary\1.0.0-rc.1-b88e135c\evaluation.json`；
原始 DWG/DXF 与 Worker 二进制均未提交 Git。

## 证据边界

这次命令以封存 Worker 的直接合同模式运行，没有启动网络监听；原始 CAD
仅进入逐 Attempt 临时目录并已清除。报告诚实记录
`outboundNetworkPolicy=NotVerifiedAtOsBoundary`：当前用户不是 Windows 管理员，
没有创建或伪造 OS Firewall 禁网证明。因此 RC 可以证明 Release 身份、转换
质量、确定性、性能和删除行为，不能冒充生产 mTLS、OS 禁网或生产部署。

最终 `1.0.0` 必须在本实现合并后的精确 `main` 上重新 publish/seal，并用新
Release SHA 对同一 20 份数据重跑。完成前 `PRIMARY_PROVIDER_AND_ISOLATED_WORKER`、
WP3/WP7 与 Core GA 继续 Pending/NoGo。

## 自动化

- 新评测器正向 20 文件双跑、源文件篡改失败和清单外 CAD 拒绝：3/3。
- 完整 CAD Experiment（含两项真实 AutoCAD 安装门禁）：61/61、0 skipped。
- Worker Release Manifest 与评测报告 JSON Schema：Pass。
