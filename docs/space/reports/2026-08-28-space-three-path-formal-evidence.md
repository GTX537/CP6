# Space Studio V1 WP4 三路径正式验收

- Gate：`WP4_THREE_PATH_END_TO_END`
- 结论：`Pass / Accepted`
- DeliveryOwner：`BUBAO.GAO`
- 被测应用基线：`9468f7f6b0a6aa82d4693f39f2f27004b5f908a9`
- 执行时间：`2026-08-28T04:08:08Z` ～ `2026-08-28T04:15:24Z`
- 环境：SQL Server Express LocalDB `17.0.4025.3`
- 生产部署：未执行
- 生产数据声明：无

## 结果

三条 V1 作者路径均通过统一 Draft / Typed Changeset 验收：

1. CAD：授权真实 `L1-C01.dwg` 与 `L1-C02.dxf` 均由已批准的 AutoCAD 2025
   Primary Worker 产生哈希绑定的 CAD IR；Preview 不写 Draft，显式 Apply 后才写入。
2. Excel–CAD：产品自身生成的 `cp6-space-standard-model-v1.xlsx` 在内存中生成、
   Open XML 校验、读取、预检、匹配及 Apply；输入哈希为
   `5efbe83c40f22acecebf9f51e4fb6ae4e9a1b9f0ce06a9fd4b2d972aad87c9a0`。
3. 手工：受控 PDF、PNG 底图及空白画布均覆盖 Preview / Apply、Revision、Lease、
   Idempotency 和审计语义。

完整 `CP6.Space.IntegrationTests` 在真实 SQL Server 引擎上执行：`465 passed / 0 failed /
0 skipped`，TRX SHA-256 为
`2a2db77d59067fadf440d2e056c37712ddbe0a0ec05e13cc0ce2fd377ae9d73c`。

正式结构化结论见
[`three-path-formal-evidence-v1.0.0.json`](../acceptance/v1.3-ga/three-path-formal-evidence-v1.0.0.json)。
原始 CAD、XLSX 物化结果和 TRX 不进入 Git；外部受控执行摘要 SHA-256 为
`a729996ca9209479d28fc01fda669d7bf1a0c652eecc5d7a9cd251a1d81e9fc3`。

## 边界

本次接受只关闭 WP4。受控 XLSX、PDF、PNG 与空白画布不是客户生产数据；本次没有执行
生产部署、生产 WMS 联调、Published-only Viewer 或恢复/安全发布演练，因此不关闭 WP5、
WP6、WP8，也不触发最终 GA 签署。
