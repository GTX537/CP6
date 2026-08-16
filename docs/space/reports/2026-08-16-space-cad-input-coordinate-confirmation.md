# Space CAD 输入与坐标确认报告

日期：2026-08-16

任务分支：`codex/space-cad-input-coordinate-confirmation`

## 结论

详细 Spec LM-FR-010～011 的仓库实现闭环。DWG 与 DXF 使用同一受控来源链和统一 CAD 中间表示；用户在 Parse 前能看到服务端确定性产生的单位、比例、图纸范围与异常依据，并必须显式确认转换与映射。该结论不把本机 AutoCAD 开发链冒充生产 Provider，也不改变核心 GA 72% / `NoGo`。

## LM-FR-010：DWG/DXF 输入

- 工作台文件选择器只接受 DWG/DXF 扩展名和已知 MIME；客户端根据文件扩展名显式提交 `Dwg` 或 `Dxf`，不把未知格式静默当成 DXF。
- 服务端分别校验 `.dwg/.dxf`、允许的声明 MIME 和真实文件签名；DWG 要求 `AC10xx` 头，文本 DXF 要求规范化后的 `0 SECTION` 起始，二进制 DXF 要求 Autodesk 标识。
- 校验通过的文件仍先进入 Quarantine/Scan，Clean 后才可 Preparation；解析统一进入 CAD IR、坐标准备、Mapping、Semantic Preview 与 Typed Changeset。
- `D:\AutoCAD 2025\accoreconsole.exe` 已通过 Authenticode，使用 Autodesk `Floor Plan Sample.dwg` 的安装型合同测试为 1/1、0 skipped。该链只登记为 Development Converter，不注册为 Site GA Provider。

## LM-FR-011：建议与显式确认

- 服务端返回自动建议单位、`mm/source-unit` 比例、来源坐标范围、建议毫米范围、合理性和稳定问题代码。
- 工作台展示原始 X/Y 最小/最大值、宽高、自动换算毫米范围、建议比例，以及“合理”或“异常”的明确文案。
- `SPACE_CAD_UNIT_UNKNOWN`、`SPACE_CAD_EXTENT_IMPLAUSIBLE`、楼层越界等问题显示可理解的恢复提示；确认后的实际坐标问题与自动分析提示分区展示。
- 用户仍须分别勾选单位/原点/旋转/楼层转换和映射语义确认；输入、Profile 或逐层 Override 变化会清除确认并要求重新生成服务端 Preview。

## 自动化证据

- Space Source/Coordinate 聚焦：30/30 passed；覆盖 DWG/DXF 扩展名、MIME、签名与单位/范围建议。
- Web API + CAD 起始向导：13/13 passed；覆盖 DWG/DXF 格式提交、非 CAD 文件前置拒绝、比例/范围显示、异常原因和显式确认。
- Space Unit 全量：546/546 passed；Web 全量：869/869 passed。
- Vue TypeScript 与 Web 生产构建通过；完整 `CP6.slnx` Release build 为 0 warning / 0 error。
- AutoCAD Core Console 安装型真实 DWG：1/1 passed、0 skipped。

## 未关闭范围

- AutoCAD Core Console 的许可、隔离 Worker、客户批准和生产自动化边界未获正式签字。
- Site 仍须有两条同黄金集、同审批标准、合格且可用的 Provider 链；当前本机安装不能计入 WP3/WP7 完成。
- 20 份授权真实黄金 CAD、50MB P95、受训用户 Ready、双仓 Pilot 与五方签字仍待真实证据。

因此 LM-FR-010～011 只关闭仓库功能实现；WP4 继续 `Partial/Pending`，核心 GA 继续 72% / `NoGo`。
