# WP2 CAD Start 正式验收协议

WP2 只接受可重复的真实 CAD Start 结果，不以 Mock 路由、合成文件或
组件截图替代。正式包必须同时绑定一份授权 DWG、一份授权 DXF、冻结的
Primary Worker Release、真实 SQL Server 和被测应用提交。

受控运行必须完成：文件哈希复算、安全扫描状态同步、Floor/Unit/Transform/
Mapping Profile 显式选择、语义 Preview、Preparation 封存、Parse Start、
同一幂等键重放，以及篡改请求零新增 Job。Preview 期间 Draft 的内容修订和
内容哈希不得变化。

原始 CAD 和完整执行报告保留在受控外部目录。仓库只记录样本 URN、SHA-256、
授权/脱敏哈希、Provider Package 哈希和审计字段，不提交 `.dwg`/`.dxf`。
正式包不得宣称生产数据、生产 WMS、生产部署、远程 mTLS 或公共 SaaS。

单人交付模式下，`DeliveryOwner` 可执行并自验收；不要求第二个人签字。
校验命令：

```powershell
./tools/Test-SpaceGaCadStartEvidence.ps1 `
  -ManifestPath docs/space/acceptance/v1.3-ga/cad-start-formal-evidence-v1.0.0.json
```
