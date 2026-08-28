# Space Studio V1 Core GA 三路径正式证据协议

适用 Gate：`WP4_THREE_PATH_END_TO_END`

WP4 只要求一次受控、可哈希、可复现的三路径验收，不要求客户仓、双仓、多人复核或
14 天 Pilot。生产部署、生产 WMS 窗口、发布恢复和安全演练属于 WP8，不在 WP4 重复执行。

正式包必须绑定完整应用 Commit、WP7 已接受的 Source Set、Golden Dataset 和 Worker
Environment，并同时满足：

- 至少一份授权真实 DWG 和一份授权真实 DXF 经同一已批准 Primary 产生有效 CAD IR；
- CAD、Excel–CAD、PDF/PNG 底图加空白画布三条路径都使用统一 Draft/Typed Changeset；
- 三条路径在显式 Apply 前均证明 Draft 未变化，Apply 后才产生受 Revision、Lease 和
  Idempotency 保护的写入；
- Excel 输入是受控 `.xlsx`，PDF/PNG 是受控验收资产；可以是 CP6 自有的确定性验收数据，
  但必须明确 `productionDataClaimed=false`，不得冒充生产 WMS 或客户现场数据；
- 真实 SQL Server 引擎执行结果 `failed=0` 且 `skipped=0`；
- 四类证据均由唯一真实 `DeliveryOwner` 在执行后接受。

原始 DWG/DXF、TRX 和汇总执行摘要保留在受控外部证据区。XLSX 可以由产品模板服务
在内存中确定性生成并哈希；CP6 自有 PDF/PNG 可以作为版本化受控验收资产。正式 Manifest
只保存输入 SHA-256、不透明证据引用、结构化结论和非敏感报告，不复制原始 CAD 或执行
产物。模板、`tools/test-fixtures`、绝对路径和 `:test:` URN 不能关闭正式 WP4。

从 [`three-path-evidence-template.json`](./three-path-evidence-template.json) 复制版本化 Manifest，
然后执行：

```powershell
./tools/Test-SpaceGaThreePathEvidence.ps1 `
  -ManifestPath <三路径正式 Manifest> `
  -ExpectedOwnerName BUBAO.GAO
```

通过 WP4 不代表 WP8 或生产部署完成。
