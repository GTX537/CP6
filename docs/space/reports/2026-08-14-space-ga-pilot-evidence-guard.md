# Space Studio V1 双仓 Pilot 证据门禁

日期：2026-08-14

范围：核心 GA / WP8 双仓 Pilot 与现场确认

结论：WP8 已具备独立的结构化证据合同、失败关闭校验器和 CI 自测入口。该能力只保证将来的真实 Pilot 证据满足冻结门槛，不表示两仓 Pilot 已开始或完成；WP8 继续为 `ExternalExecution/Pending`，核心 GA 继续为 72% / No-Go。

## 已关闭的证据漏洞

此前 WP8 的 `evidencePaths` 为空。通用 GA 校验器只能核对证明对象路径、哈希、接受人和时间，无法判断一份被引用的说明是否真的包含两个不同类型 Site、连续 14 天、零 S1/S2、S3 关闭、恢复 SLO、一致性和现场确认。理论上，一份结构正确但内容过薄的泛化文件可能被登记为 Accepted。

现在：

- `pilot-evidence-template.json` 冻结绿地仓、存量改造仓的统一字段；模板本身明确为 Pending，并被正式门禁禁止充当证据。
- `Test-SpaceGaPilotEvidence.ps1` 校验恰好一个 Greenfield 和一个 Retrofit Site、唯一不透明 Site URN、至少连续 14 个日历日及逐日不可变记录。
- 严格拒绝任何 S1/S2、缺少可用绕行或未关闭的 S3、低于 100% 的 2D/3D/对象清单或 WMS 一致性、超过 15/240 分钟的恢复、旧 Published 中断、Viewer 非 Published-only 或长期双写。
- 每仓必须提供运行日志、指标、缺陷关闭、业务结果、开放问题附录五类可哈希证明，并由客户仓库代表和实施负责人分别实名确认。
- `Test-SpaceGaEvidence.ps1` 在 WP8 被标记 Accepted 时强制要求五个内部签字人已经 Signed 和 `verificationManifest` 已登记，确认该 Manifest 自身已被 Gate 的 `acceptedEvidence` 哈希证明，再调用 Pilot 校验器复核；模板、Manifest 路径和嵌套证明中的 `tools/test-fixtures` 都被显式拒绝。
- GitHub Actions 在协议、模板、校验器、测试或证据索引变化时执行两套门禁。

## 自动化

- Pilot 专项：21/21，包括仅在显式测试模式可用的合成双仓正向 fixture、正式模式拒绝 fixture，以及单仓、重复类型/身份、13 天、缺失/重复日记录、未来窗口、过早接受证据、S1/S2、S3 无绕行或未关闭、一致性不足、自动/人工恢复超时、旧 Published 中断、Viewer 边界、占位确认人、确认人不一致、缺失证明对象和哈希不一致。
- 通用 GA 证明链：23/23；新增签字人与签字证据接受人一致性，以及 WP8 缺 Manifest、五方签字未完成、有效 Manifest、Manifest 未被证明、语义无效 Manifest 和空模板组合场景。
- 当前权威索引输出保持：`NoGo`、5 个 Pending External Inputs、9 个 Pending Gates、5 个 Pending Signers。
- `-RequireGaReady` 继续以退出码 `2` 失败，证明本任务没有把准备工具冒充现场完成。

## 使用边界

正式执行时从模板复制到新的版本化文件，使用不透明 `urn:cp6-space-site:*`，原始 CAD、库存明细、客户名称和凭据不得进入仓库。现场原始日志可以保存在受控 HTTPS/URN 存储中，Manifest 只保存指标、哈希、真实接受人和 UTC 时间。

该门禁不能生成真实日历时间、业务结果或签字，也不能替代生产等价 SQL/WMS/观测链、两个客户现场、真实 IdP、安全测试和五方内部审批。
