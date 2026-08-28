# Space Studio V1 Core GA 手工建模正式证据协议

适用 Gate：`WP1_DESIGN_V1_MANUAL_MODELING`

WP1 是单人 DeliveryOwner 的功能结果接受，不要求独立 QA、第二签字人、现场 Pilot 或多人
门禁。正式证据必须绑定已进入远端 `main` 的精确应用提交，并以真实 SQL Server 执行和
可重复 Web 操作面测试证明：

- 从 Blank Draft 显式创建楼层，并可原子创建 Zone、Aisle、Rack、RackLevel 与 Location；
- 完整仓库可形成确定性库位编码，编码 Preview 零写入，Apply 后才修改当前 Draft；
- System/Tenant Template 可预览、实例化和重放，租户 Scope 失败关闭；
- 所有写入受 Lease、Floor Revision、Content Revision 和 Idempotency Fence 保护；
- 失败批次零部分写入，Published 数据不被 Draft 建模路径修改；
- SQL Server 结果 `failed=0`、`skipped=0`，Web 结果 `failed=0`、`skipped=0`；
- 测试源文件逐项绑定仓库相对路径和 SHA-256，由唯一 DeliveryOwner 在执行后自验收。

自动化使用确定性受控测试数据，不宣称生产数据、客户现场验收或生产部署。首次因缺少
`CP6_TEST_SQLSERVER` 而产生的 skipped 运行不得计入证据；只有显式连接真实 SQL Server
后的无跳过运行可以关闭 WP1。

从 [`manual-modeling-evidence-template.json`](./manual-modeling-evidence-template.json) 复制
版本化 Manifest，然后执行：

```powershell
./tools/Test-SpaceGaManualModelingEvidence.ps1 `
  -ManifestPath <WP1 正式 Manifest> `
  -ExpectedOwnerName BUBAO.GAO
```

通过 WP1 只代表手工建模结果已接受，不代表 WP2、WP5、WP6、WP8、最终 GA 签署或生产
部署完成。
