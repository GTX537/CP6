# Space AutoCAD 候选 Worker DWG/DXF 双格式链

日期：2026-08-27

## 结论

AutoCAD Core Console 候选 Worker 已从仅 DWG 扩展为同一 Provider 身份下的 DWG/DXF 双格式链。DWG 继续由精确 Core Console 版本导出后进入 CP6 托管 DXF Parser；原生 DXF 直接进入同一 Parser，不启动 AutoCAD。两条路径的完整源 SHA-256、统一合同执行、CAD IR-only 响应、Provider Version Fence 和 Attempt 清理均在隔离 Worker 边界内失败关闭。

这关闭的是 Primary 候选的 DXF 仓库路径，不是 WP3 正式接受。输入仍是开发 fixture 与 Autodesk 安装样例，没有授权真实 DXF、50 MiB 性能证据、非 `development` Release 身份、独立 Backup、许可证/Site 批准或生产隔离部署，因此 WP3 继续 `Partial / Pending`，Space 总体继续 72% / `NoGo`。

## 组合链身份

- Provider Key：`cp6-autocad-worker-development`。
- Provider Version：`{accoreconsole file version}+cp6-dxf-1.0.0`。
- DWG Inner Converter：精确 AutoCAD Core Console 文件版本。
- DXF Inner Converter：`cp6-development-dxf/1.0.0`。

外层组合 Converter 与内层 DWG/DXF Converter 都只能经 `SpaceCadConverterContractRunner` 执行。最终 CAD IR 绑定组合链身份，而不是把原生 DXF 错报为 AutoCAD 转换；任一内层版本变化都会改变外层 Provider Version，并要求重新评分和 Site 认证。旧的仅 AutoCAD 版本请求会在落盘前被拒绝。

## 行为与清理

- Worker 先把完整源写入每次唯一 Attempt，核对 SHA-256 后设为只读，再启动组合 Converter。
- DWG 路径实际启动 Core Console；DXF 路径的自动化断言 Exporter 调用次数为 0。
- 两条路径只输出 CAD IR；Mapping、语义和 PreviewSet 仍由 CP6 生产侧 Provider 重放生成。
- 成功、输入错误或版本错误都不得保留 Attempt 原始/派生 CAD；清理不跟随 Reparse Point。

## 验证

| 门禁 | 结果 |
|---|---:|
| 候选 Worker 聚焦测试 | 4/4，0 skipped |
| 完整 CAD Experiment（安装型门禁开启） | 45/45，0 skipped |
| 真实 Core Console DWG | 29 图层、19 块、4,424 实体、4,422 支持实体 |
| 原生 DXF 不启动 AutoCAD | 通过 |
| 旧组合版本落盘前拒绝 | 通过 |
| 测试根残留 DWG/DXF | 0 |
| 测试根残留 Attempt 条目 | 0 |
| `CP6.Tests` | 2,939 passed / 19 environment-gated skipped / 0 failed |
| `CP6.slnx` Release | 0 warning / 0 error |

真实 Core Console 输入仍为 Autodesk 安装样例 `Floor Plan Sample.dwg`，SHA-256 `19270c23e56e407aab2ade3644e8f301c34e390638d99c3f0cc4f2d3a6516792`；原生 DXF 输入为仓库测试 fixture。二者均不能计入授权黄金集。

## 剩余门禁

1. 将组合链冻结为非 `development` Worker Release，内容寻址并重新生成批准 Manifest。
2. 用授权真实 DWG/DXF 运行同一 20 份 10/5/5 黄金集；当前托管 DXF Parser 的开发输入上限为 25 MiB，尚不满足 50 MiB 正式性能场景，必须以受控流式/更高上限实现和资源证据关闭。
3. 交付一个技术、供应商和故障域独立且同时覆盖 DWG/DXF 的 Backup Provider。
4. 完成许可证、客户/Site、安全、区域、身份、证书、无出口和删除保留批准，并在真实 mTLS 部署执行 Site 主备故障切换。

以上证据可由同一个实名 `DeliveryOwner` 执行与签署，不要求多人审批。
