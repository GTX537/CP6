# Space Studio 底图与 Excel–CAD 路径闭环证据

日期：2026-08-14
范围：WP4 仓库内图片底图、Excel–CAD、DWG 与 DXF 工作台路径

## 结论

本任务关闭两个实际可发现性缺口，并把四条建模入口固化为独立浏览器场景：

- 图片上传并挂接后，工作台显示明确的“标定底图”入口；用户完成三点标定和误差预览后才保存。
- 带 `matchJobId` 的工作台深链自动打开问题域和 Excel–CAD 权威匹配；匹配行可定位 Rack，只有显式确认才进入 Apply。
- DWG 与 DXF 分别验证上传、服务器 Preparation Preview、转换确认、映射确认和原有 Parse 启动接口。
- 所有操作继续使用既有 Design V1 权威，没有新增平行的底图、匹配或解析写接口。

这份报告不是 WP4 或核心 GA 的完成声明。浏览器测试使用受控 API fixture，不代表真实 Provider 转换准确率、真实 Excel 匹配质量、CP6 WMS 发布恢复或现场 Pilot。

## 实现边界

- `SpaceStudioContextPanel` 在存在底图时公开标定动作；只读状态失败关闭，已标定时显示“重新标定底图”。
- `DesignUnderlayView` 复用既有 `beginCalibration`，不复制标定状态机或数据合同。
- `matchJobId` 的立即监听会调用既有权威匹配面板，并显式切换检查器到问题域。
- Excel–CAD 确认继续携带 `artifactId`、`artifactPayloadSha256` 和 `expectedContentRevision`；确认前零 Draft 写入。
- DWG/DXF 测试只分别证明前端文件选择、服务器 Preview 与确认门槛，不声称测试文件经过真实转换器。

## 自动化证据

- `SpaceStudioContextPanel.spec.ts`：有底图时显示/触发标定动作，已标定标签与只读禁用状态。
- `space-studio.spec.ts`：图片上传→挂接→加载→三点标定→保存，断言像素尺寸与 Floor Revision。
- `space-studio.spec.ts`：Excel–CAD 深链→加载权威产物→定位 Rack→显式确认→Succeeded。
- `space-studio.spec.ts`：DWG、DXF 各自上传→服务器 Preview→双确认→Parse 启动。

门禁结果：

- Vue type-check：通过。
- Web 单元测试：148 files，762 passed。
- Web production build：通过；既有大 chunk 警告保持，不是本任务新增失败。
- Space Studio Playwright：13 passed，其中本卡新增或拆分 4 条路径。
- `git diff --check`：通过。

## 仍未关闭

- 每个启用 CAD GA 的 Site 的两条客户批准真实 Provider 链及隔离 Worker。
- 20 份授权黄金 CAD 的 10/5/5 校准、验证和 Holdout 指标。
- 授权真实 DWG、DXF、Excel、PDF/图片通过真实服务、SQL Server、CP6 WMS 的端到端运行。
- Iris Xe/WebGL2 500 货架/10,000 库位性能、WMS 恢复演练、双仓各 14 天 Pilot 与五方签字。
