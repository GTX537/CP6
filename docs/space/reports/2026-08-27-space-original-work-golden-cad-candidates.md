# Space 原创黄金 CAD 候选数据集

日期：2026-08-27

Owner / Reviewer：`BUBAO.GAO`

结论：`AUTHORIZED_GOLDEN_CAD_CANDIDATES = Complete`

## 结论与边界

单人开发场景采用 `ApprovedOriginalWork`：CAD 由 Owner 使用 AutoCAD 2025 原生引擎自行设计、生成、授权和复核，不依赖不存在的客户或第二开发者，也不虚构客户名称、地址或授权关系。原始 CAD 和逐份完整证据保存在仓库外受控证据区；Git 只保存脱敏 Manifest、SHA-256 和 `urn:cp6-space-ga-evidence:*` 引用。

这只关闭 Lean Schema 3 两类外部输入中的“授权黄金 CAD 候选登记”。它不等于 WP7 正式接受，也不证明 Primary 批准、受控发布演练、质量阈值或性能阈值。Core GA 因此仍为 `72% / NoGo`。独立 Backup 与双仓 Pilot 已在后续门禁重置中转为 GA 后增强。

## 数据集事实

- 正好 20 份唯一 CAD：10 DWG + 10 DXF，均由 AutoCAD 2025 原生保存为 `AC1032`。
- 固定分组：Calibration 10、Validation 5、Release Holdout 5；只有 Calibration 标记为可调优。
- 布局覆盖：L1～L5 各 4 份。
- 20 份合计 14,659 个 Model Space 图元、2,455 个带 AutoCAD Handle 的逻辑标准答案元素。
- 每份均登记源文件 SHA-256、原创授权、脱敏证明、单位、坐标系、布局类别、格式/版本、标准答案、预期问题、Mapping Profile、规则版本及实名复核人。
- Release Holdout 与整个数据集同时冻结；Manifest 声明 `isImmutable=true`、`rawCadCommittedToGit=false`。

## 不可变身份

- Source Set SHA-256：`7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`
- Golden Dataset SHA-256：`2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`
- 完整性审计 SHA-256：`efd943ed0fd4999a74dc7d6c34dfc2816d7ad2fda450886af6e960a52d013ecd`
- 产品 Converter Contract 验证报告 SHA-256：`c9ab4724b79c9495ea30957d5c75189c388b5275b47b197458bff6b2a62f9a1d`
- 仓库脱敏 Manifest SHA-256：`813630db234d06519a2e84321ae340233334c15cf40820914f661d5d7d28e657`

产品现有 Converter Contract Runner 对 20/20 文件读取成功；报告记录 14,699 个读取实体和 40 个非阻断 Paper Space `VIEWPORT` 提示。该读取验证证明文件可被当前产品链消费，不替代后续 Primary 的正式资格评分。

## 仓库门禁

[`authorized-golden-cad-candidates-v1.0.0.json`](../acceptance/v1.3-ga/authorized-golden-cad-candidates-v1.0.0.json) 是仓库内脱敏登记。`Test-SpaceGaGoldenCadCandidates.ps1` 重新计算并验证唯一性、20/10/5/5、L1～L5、DWG/DXF、证据绑定、复核人、冻结状态、Source Set 和 Golden Dataset 摘要；总 GA 校验器在对应外部输入为 Complete 时强制调用它。

后续应在同一 Source Set、冻结 Worker、相同 Mapping Profile/规则版本上运行获批准的 Primary，再按正式黄金 CAD 协议提交质量、Wilson、人工操作、Holdout Blocking 和性能证据。
