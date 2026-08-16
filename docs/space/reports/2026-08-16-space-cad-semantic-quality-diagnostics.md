# Space CAD 语义与质量诊断报告

日期：2026-08-16

任务分支：`codex/space-cad-semantic-quality-diagnostics`

## 结论

详细 Spec LM-FR-014～016 的仓库实现已闭环。现有确定性语义链本来已经覆盖墙、柱、门、月台、区域、巷道和货架，并在 Semantic Preview 与 Diagnostic Index 中保存逐提案 SourceRef、命中规则、几何规则、置信度和画布定位；本任务没有建立第二套解析器。

本任务补齐的是问题语义的精度：未映射和规则冲突继续由 Mapping 问题空间负责；零尺寸、无法闭合、越界和重叠现在都有明确、可定位、可自动化断言的稳定分类。该结论只覆盖仓库实现；真实主备 Provider、20 份授权黄金 CAD、双仓 Pilot 和五方签字仍未完成，核心 GA 保持 72% / `NoGo`。

## 问题分类

- `SPACE_CAD_MAPPING_SOURCE_UNMAPPED`：没有命中 Profile/Override 的图层或块，保留来源键和可定位范围。
- `SPACE_CAD_MAPPING_RULE_CONFLICT`：同优先级、同特异度规则歧义，确认前失败关闭。
- `SPACE_CAD_SEMANTIC_ZERO_SIZE`：零长度路径、少于三个独立点、零面积多边形、缺失/非正半径或退化块变换；对象保留 SourceRef 并作为 Rejected 提案进入问题清单。
- `SPACE_CAD_SEMANTIC_BOUNDARY_UNCLOSED`：ClosedBoundary 规则遇到开放 Polyline；不自动猜测闭合边。
- `SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED`：整份准备产物越过楼层边界时继续 Blocking；同时为每个越界实体追加 `SPACE_CAD_ENTITY_FLOOR_BOUNDARY_EXCEEDED`、SourceRef 和恢复定位，并通过 Preparation 合同进入起始向导的问题清单。
- `SPACE_CAD_SEMANTIC_GEOMETRY_OVERLAP`：同一语义目标的 Polygon/Circle 存在真实正面积重叠时，为双方各生成一个 Warning。只接触边界不算重叠；Zone 包含不同目标 Rack、无真实面积的 Path/Arc/Point 和降级 BlockInstance 不参与，避免用包围盒制造伪冲突。

## 算法与边界

- 重叠候选先按目标和 MinX 做确定性 sweep，移除已经不可能相交的活动对象，再做 Y Bounds 预筛和 Polygon/Polygon、Circle/Circle、Circle/Polygon 实际相交判断。
- 每个涉及重叠的对象最多生成一个诊断，DetailToken 绑定确定性的对方 PreviewObjectId，避免 N×N 问题膨胀；Diagnostic Index 继续提供当前对象的画布 Bounds/Anchor。
- 语义 Hash 会包含新问题；历史只读工件仍按自身内容 Hash 验证，不被重新解释或原地改写。
- 楼层越界仍在 Coordinate Preparation 阶段阻止 Parse；逐对象 Warning 是对现有全图 Blocking 的可定位补充，不降低门槛。

## 自动化证据

- Space Unit：544/544 passed；覆盖零尺寸、开放边界、双方重叠、重合多边形反向绕序、边界接触不误报、不同目标不误报、逐对象越界 SourceRef、确定性与 Diagnostic Recovery。
- CAD Preparation、Parse、BuildScene 与 Excel–CAD 集成聚焦：37/37 passed。
- Design V1 OpenAPI：55/55 passed；C#/TypeScript SDK 漂移检查通过。
- CAD 起始向导：4/4 passed；Vue TypeScript 检查通过。
- CAD 实验工具常规门禁：39 passed、1 个安装环境用例 skipped；配置安装环境后，AutoCAD Core Console 使用 `D:\AutoCAD 2025\accoreconsole.exe` 与 Autodesk `Floor Plan Sample.dwg` 另行实跑 1/1、0 skipped。
- CP6.Tests：2,933 passed、19 个既有环境门禁 skipped；完整 `CP6.slnx` Release build 为 0 warning / 0 error。
- Core Console 版本 `25.0.58.0.0`、Authenticode `Valid`；GUI `acad.exe` 仍为 `HashMismatch`，本任务没有启动或调用 GUI。

## 未关闭范围

- LM-FR-010～011、019/019A 仍须按详细 Spec 继续审计；统一 Excel 上传 UI 和真实三路径浏览器闭环仍待完成。
- AutoCAD Core Console 仍是开发转换链，不是 Site 已认证 Provider；许可/隔离/客户审批和独立备用 Provider 未关闭。
- 当前 Autodesk 样例不是授权业务黄金仓库 CAD，不能计入 20 份 10/5/5 数据集、准确率、50MB P95 或 Pilot。

因此 LM-FR-014～016 的仓库实现完成，但 WP4 继续 `Partial/Pending`，核心 GA 仍为 72% / `NoGo`。
