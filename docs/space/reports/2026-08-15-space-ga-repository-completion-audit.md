# Space Studio V1 核心 GA 仓库完成度审计

日期：2026-08-15
审计基线：`main@3dab2826aa099dbe69e8cdf995c93b73cea1bc8f`

## 结论

当前核心 GA 继续为 **72% / `NoGo`**。本次审计确认，AutoCAD Core Console 开发转换链已经进入远端 `main`，但它没有改变 WP3 的 `Partial/Pending`，也没有关闭任何真实 Provider、黄金 CAD、Pilot 或签字门禁。

仓库内可独立实现的 WP1、WP2、WP5、WP6 已保持 `implementationStatus=Complete`；详细 Spec 复核确认 LM-FR-017 的 CAD 异常对象拆分和重画仍缺失，改类型、删除与合并已有独立纵切，因此 WP4 保持 `Partial`。WP0 因实名治理输入缺失保持 `Partial`，WP3 因正式主备 Provider 缺失保持 `Partial`，WP7/WP8 保持 `ExternalExecution`。所有 Gate 的 `acceptanceStatus` 仍为 `Pending`。

审计期间发现 PowerShell 7.6 默认把 JSON ISO-8601 字符串解析为 `DateTime`，使证据校验器丢失原始 `Z` 表示并误拒合法证明。四个 GA 校验器现通过共享兼容层在支持 `-DateKind` 的宿主强制保留 JSON 字符串，在 Windows PowerShell 5.1 继续使用原生字符串行为；没有放宽时间格式、UTC 或未来时间校验。

## 逐工作包核对

| 工作包 | 实现状态 | 接受状态 | 当前证据结论 |
|---|---|---|---|
| WP0 基线与治理 | Partial | Pending | PR #4、主线门禁、证据索引和失败关闭校验已交付；实名 Owner、kickoff 和目标日期未登记 |
| WP1 Design V1 手工建模 | Complete | Pending | 空白画布、Layout/Element、编码、租约/Revision/幂等链有仓库自动化；独立 QA/Pilot 未签 |
| WP2 CAD 起始向导 | Complete | Pending | DWG/DXF Preparation、显式单位/坐标/Mapping 确认与解析启动已交付；真实授权文件 E2E 未接受 |
| WP3 Site 主备 Provider | Partial | Pending | Provider 合同、认证、评分、路由、SQL 门禁与 AutoCAD 开发适配器已交付；没有两条 Site 批准的生产链 |
| WP4 三路径闭环 | Partial | Pending | CAD、Excel、底图、空白画布和发布入口有仓库测试；异常对象改类型、删除和合并已交付，拆分和重画仍缺失；真实文件、WMS 和现场闭环未接受 |
| WP5 Viewer/可达性/性能 | Complete | Pending | Published-only 边界、交互、自动化与 Iris Xe 原始性能记录已交付；独立 UX/辅助技术和生产等价 E2E 未签 |
| WP6 发布/WMS/安全/恢复 | Complete | Pending | 发布 Fence、LocalDB 真库、权限矩阵、恢复指标与运行手册已交付；生产等价 WMS/告警/恢复演练未接受 |
| WP7 黄金 CAD | ExternalExecution | Pending | 证据协议与校验器已交付；20 份授权样本、双标注、10/5/5 和主备实测不存在 |
| WP8 双仓 Pilot/签字 | ExternalExecution | Pending | 证据协议与校验器已交付；两仓各 14 天运行和五方实名签字未发生 |

## AutoCAD 开发链的证据边界

- `accoreconsole.exe` 的本机安装型合同测试与确定性样例转换是 `DevelopmentEvidence`。
- Autodesk 安装样例不是授权黄金仓库 CAD，不进入 10/5/5 数据集。
- 开发转换器没有注册为 Site 运行 Provider，没有许可证/数据区域/保留删除/客户批准或隔离网络证明。
- 没有第二个合格 Provider，也没有同一黄金集和冻结 Worker 的 80 分以上主备排名。
- 因此 AutoCAD 开发报告只加入 WP3 的 `evidencePaths`，本审计报告加入 WP0 的 `evidencePaths`；两者都不加入 `acceptedEvidence`，也不修改实现或接受状态。

## 下一阶段退出条件

代码可以继续支持真实文件预检和缺陷修正，但下一次状态提升必须来自真实外部证据：

1. 登记具有审批权的实名 Product、QA、WMS、Architecture、Security 以及 kickoff/目标日期。
2. 提供授权真实 DWG/DXF，最终冻结 20 份 10/5/5 黄金集并完成双人标注。
3. 为目标 Site 批准两条 Provider/Worker 链，在同一冻结环境评分并注册 Primary/Backup。
4. 在生产等价 SQL、CP6 WMS、IdP 和告警环境完成发布恢复、安全与 Published-only E2E。
5. 完成绿地仓、改造仓各连续 14 天 Pilot，关闭缺陷并取得五方签字。

在以上证明进入受控证据链前，`./tools/Test-SpaceGaEvidence.ps1 -RequireGaReady` 必须继续失败，不能通过修改状态或使用本地样例绕过。

本报告合入前要求 PowerShell 7.6 与 Windows PowerShell 5.1 均运行证据门禁自测，普通 NoGo 索引通过且 `-RequireGaReady` 保持退出码 2。
