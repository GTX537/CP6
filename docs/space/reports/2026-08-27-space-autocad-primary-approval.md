# Space AutoCAD Primary 批准与单 Provider 运行时对齐

日期：2026-08-27

## 结论

`BUBAO.GAO` 已批准当前 AutoCAD 2025 Core Console 作为 Space V1 唯一
Primary Provider。批准范围限于本机受控 CP6 开发、验证和 Release
Rehearsal；不扩张为 Autodesk 软件再分发、公共 SaaS 托管或生产部署授权。

本次同时移除两处与 Lean Core GA Schema 3 冲突的遗留限制：

- `qualify-providers` 现在只要求一个满足全部硬门禁、总分不低于 80 且
  排名唯一的 Primary；Backup 存在时仍可输出，但不再阻断 Core GA。
- Site capability 的 `CadGaReady` 现在由合格、在有效期内、运行版本一致并
  同时支持 DWG/DXF 的 Primary 派生；缺失或失效的可选 Backup 不再产生
  Core GA 阻断码。

## 许可与运行事实

| 项目 | 结果 |
|---|---|
| Core Console | `25.0.58.0.0` |
| SHA-256 | `d1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb` |
| Authenticode | `Valid` / Autodesk, Inc. |
| Autodesk Licensing Service | Running / Automatic |
| 真实安装型测试 | 2/2 passed |
| 候选 Worker 结果 | 4,424 实体 / 4,422 支持实体 |
| 测试后残留 | 0 DWG/DXF / 0 Attempt 目录 |

单 Primary 资格评测器为 9/9，Provider 路由为 17/17，SQL Server LocalDB
真实门禁为 3/3、0 skipped，完整 CAD Experiment（含真实安装门禁）为
58/58、0 skipped；GA 证据回归为 36/36。

批准记录为
`docs/space/acceptance/v1.3-ga/autocad-primary-approval-v1.0.0.json`。记录明确
区分 DeliveryOwner 的 CP6 内部使用批准与 Autodesk 自身授予的权利；没有
伪造订单、订阅编号或供应商合同。

## 当前边界

这一步关闭 Primary 选择和受控使用范围批准，不会把
`PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 直接标为 Complete。正式 SemVer
Worker、冻结环境/隔离证据和黄金集资格评分完成后，才可关闭该输入与 WP3。
原始受控 CAD 保持仓库外，未执行生产部署。
