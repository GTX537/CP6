# Space Lean Core GA 门禁重置

日期：2026-08-27

DeliveryOwner：`BUBAO.GAO`

## 结论

Core GA 证据合同升级为 Schema 3。首版正式验收继续失败关闭，但删除了与单人开发首版不相称的流程性阻塞：独立 Backup Provider、Greenfield/Retrofit 双仓、每仓连续 14 天 Pilot、客户来源 CAD 和额外人员确认不再是 Core GA 前置条件。

这些能力没有被禁止：独立 Backup 与现场 Pilot 被移到 GA 后的韧性、推广和采用增强轨道，可按业务需要单独执行。

## 首版保留的硬门禁

- 20 份授权且不可变的 CAD，固定 10/5/5、L1～L5、DWG/DXF、Source Set 与 Golden Dataset SHA；Release Holdout 不参与调参。
- 一个真实获批准的 Primary Provider，绑定精确版本、许可、隔离 Worker、保留/删除策略、Secret 引用和不可变环境身份。
- Primary 资格分至少 80；覆盖率、准确率、高置信精确率、Wilson 下界、人工操作下降、Blocking 遗漏与 50 MiB/Ready P95 阈值不降低。
- 一次受控发布演练绑定应用 Commit、Source Set、Golden Dataset 和 Worker Environment；在 SQL Server、CP6 WMS、Published-only Viewer 中完成 DWG/DXF、三路径、发布、恢复、安全负向和无重复写验证。
- 开放 S1/S2/Blocking S3 为 0，自动/人工恢复分别不超过 15/240 分钟；最终由唯一 DeliveryOwner 签署。

## 当前状态

`AUTHORIZED_GOLDEN_CAD_CANDIDATES` 已 Complete。剩余一个外部输入 `PRIMARY_PROVIDER_AND_ISOLATED_WORKER`、WP0～WP8 九个正式接受 Gate 和一个 DeliveryOwner 签署仍 Pending，因此状态保持 72% / `NoGo`；本次重置没有伪造 Provider 批准、发布演练或生产部署。

旧 Schema 2、双 Provider 和双仓 Pilot 报告只保留历史追溯，不再定义当前 Core GA。权威入口为 `docs/space/acceptance/v1.3-ga/ga-evidence-index.json`。
