# Space Studio V1 WP2 CAD Start 正式接受

- Gate：`WP2_CAD_START_WIZARD`
- 结论：`Pass / Accepted`
- DeliveryOwner：`BUBAO.GAO`
- 测试提交：`e6e93a260d8221d82edb1017c3946d259f637c70`
- 生产部署：未执行

## 受控真实执行

验收运行器复算受控清单，并从仓库外读取授权原创 `L1-C01/source.dwg` 与
`L1-C02/source.dxf`。两份源文件的大小、源 SHA-256、授权和脱敏证据 SHA-256
均与冻结 20 份 CAD Manifest 一致。

精确 Worker Release 为 `cp6-autocad-worker/1.0.0`，Release SHA-256 前缀
`c794e9c0ebbb`，AutoCAD Core Console 为 `25.0.58.0.0`。两个文件均重新经过
冻结 Worker 转换；Package SHA-256 分别为 `e7d5a673…c48a` 与
`4ffc211c…6d57`。Worker 执行后残留 Attempt 目录和 DWG/DXF 文件均为 0。

产品链在 SQL Server Express LocalDB `17.0.4025.3` 上完成：

- 显式选择 `F01`、`Millimeter`、零原点/零旋转 Transform 和版本化 Mapping Profile；
- 两个 Preview 都生成唯一 sealed Preparation，Draft Revision/Hash 保持不变；
- 两个 Parse Start 都创建审计 Job，同一幂等键重放返回原 Job；
- 篡改 Mapping Preview SHA 被 `SPACE_CAD_PREPARATION_INVALID` 拒绝，Job 数保持 `2 → 2`；
- 后端聚焦回归 `21/21`、Web Wizard/API `14/14`、Vue strict type-check 全通过。

外部受控执行报告以
`urn:cp6-space-ga-evidence:wp2-cad-start:e6e93a26:v2` 登记，SHA-256 为
`e1398d700afab85f783c70480fe2ae8a10e345def1c940496dcdf95498a34fec`。
仓库正式 Manifest SHA-256 为
`03e38a47b84097b72067b30786078e3e6b6f8ab479b84a89e9007cff700b94b7`。

## 校验与边界

正式 Manifest 独立校验通过，16 个模板、身份、格式、选择、审计、篡改、源码和
生产边界失败模式均正确拒绝；总 GA 组合门禁为 `52/52`。

本次不提交原始 CAD，不宣称生产数据、生产 WMS、生产部署、远程 mTLS 或公共 SaaS。
Core GA 固定保持 72% / `NoGo`；剩余 WP5、WP6、WP8 与唯一 DeliveryOwner 最终签署。
