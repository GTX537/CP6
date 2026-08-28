# Space AutoCAD Primary V1 资格接受

日期：2026-08-28
Owner：`BUBAO.GAO`
范围：V1 本机受控 `LocalControlledProcess`

## 结论

`BUBAO.GAO` 确认以下 V1 边界：

> V1 不把 OS Firewall 出站禁网作为阻断门禁；以无业务凭据、无网络监听、临时 CAD 强制删除和可审计报告作为本地受控使用边界。生产或 SaaS 部署另行审批。

该决定只处理 AutoCAD 2025 Core Console 在本机受控 V1 中的安全边界，不授权
生产部署、公共 SaaS、远程托管或 Autodesk 软件再分发。远程/生产 Worker 仍须
独立完成网络隔离、身份、Secret、mTLS 和环境审批。

在此边界下，正式 `1.0.0` Worker 的许可、不可变身份、真实 DWG/DXF 转换、
确定性、删除行为和六维资格评分已齐备。`qualify-providers` 返回
`cadGaReady=true`，AutoCAD Worker 以 86/100 成为唯一合格 Primary；Backup 为
可选增强，不阻断 V1 Core GA。外部输入
`PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 与 WP3 可接受。

## 不可变证据

| 证据 | 值 |
|---|---|
| 正式 Worker 来源 | `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` |
| Worker Release SHA-256 | `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e` |
| Provider Version | `1.0.0+worker.c794e9c0ebbb.autocad.25.0.58.0.0.dxf.1.1.0` |
| Golden Dataset SHA-256 | `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15` |
| Frozen Environment SHA-256 | `c9bbbe362a01e951379d60990f227fc4d5634ac9c86534f009f1d7e87d601717` |
| 正式评测报告 SHA-256 | `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350` |
| 资格选择 SHA-256 | `d7b9645d915f28e165209b71f69386305711301a6a2fecf7422c15cbcc2a0faa` |

正式评测为 20/20 文件、10 DWG/10 DXF、10/5/5；双跑确定性 20/20，
14,659/14,699 个实体受支持（99.727873%），缺失 SourceRef、Blocking Issue、
Attempt/CAD 残留均为 0，首跑 P95 为 4.281 秒。

## 六维评分

| 维度 | 分数 | 保守依据 |
|---|---:|---|
| 图元、块和属性覆盖 | 24/25 | 正式 20 文件覆盖 99.727873%；40 个不支持的 VIEWPORT 已显式报告，未给满分 |
| 几何、单位和坐标保真 | 18/20 | 合同检查单位、坐标、SourceRef 和确定性；WP7 的业务准确率/精确率尚未完成 |
| 性能、内存和稳定性 | 13/15 | P95 4.281 秒、双跑确定、无残留；未把生产长期运行或生产容量冒充本次证据 |
| 安全隔离与可运维性 | 12/15 | 无监听、无业务凭据、临时 CAD 强制删除；OS Firewall/mTLS 未验证，因此扣分 |
| SaaS 授权和总成本 | 11/15 | Owner 已批准本机受控使用，Core 签名与 Licensing Service 已核验；不含 SaaS、生产和再分发授权 |
| 供应商支持、版本和退出 | 8/10 | 官方安装引擎、完整版本/哈希和 Adapter fence 已固定；独立 Backup/退出演练属于 GA 后增强 |
| **总分** | **86/100** | 高于 80 分门槛 |

评分输入是
`docs/space/acceptance/v1.3-ga/autocad-primary-scorecard-v1.0.0.json`，机器输出是
`docs/space/acceptance/v1.3-ga/autocad-primary-qualification-v1.0.0.json`。工具只生成
Primary 认证输入，不直接修改任何 Site 配置。

## 接受边界与剩余项

- WP3 只接受精确版本的 V1 本地 Primary Provider 链；静默升级必须重新评分和接受。
- 没有使用合成数据替代正式评测；原始 CAD 与 Worker 二进制仍在仓库外。
- WP7 继续 Pending：尚需业务级总体准确率、高置信精确率/Wilson 下界、人工操作减少率和受训用户首次 Ready 时间。
- WP0～WP2、WP4～WP8 与 DeliveryOwner 最终 GA 签署不因 WP3 接受而自动通过。
- 本次没有生产部署、Site 配置写入或生产 mTLS/Firewall 声明。

## 自动化

- 本地边界 Kickoff Manifest 校验：Pass。
- Kickoff 失败关闭套件：21/21 passed。
- Provider 资格评估专项：10/10 passed；完整真实 AutoCAD 回归：62/62 passed、0 skipped。
- GA 组合/证明失败关闭套件：36/36 passed；当前派生结果为 `NoGo`、0/8/1 Pending。
- `qualify-providers`：退出码 0，1 个合格候选，唯一 Primary，0 Blocking Code。
