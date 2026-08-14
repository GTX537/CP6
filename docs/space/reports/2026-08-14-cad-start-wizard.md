# Space Studio WP2 CAD 起始向导交付记录

日期：2026-08-14
分支：`codex/space-cad-start-wizard`

## 范围

本卡补齐 `上传/扫描 → 楼层/单位/坐标 → Mapping Profile → 语义预览 → 显式确认 → Parse Job`。解析仍只由现有 `POST .../cad-parses` 启动；Preparation Preview 不是第二条解析启动链。

## 权威边界

- 浏览器只提交用户选择，不生成 Coordinate Transform、Mapping Definition 或 Mapping Preview Hash。
- 服务端通过 `ISpaceCadPreparationProvider` 在受控隔离边界检查原始 DWG/DXF；默认实现返回 `SPACE_CAD_PREPARATION_UNAVAILABLE`，不会在未认证 Site 静默处理 CAD。
- Ready Preview 保存 `Space_CadParsePreparation`，绑定 Tenant、Version、Source/SHA、Floor、坐标元数据、Profile/Definition/Preview Hash、Semantic Hash、Base Content Revision/Hash 和 UTC 过期时间。
- Parse Start 必须携带 Preparation ID，并逐字段匹配服务器保存值；过期、篡改或 Draft 前进均失败关闭。
- Preview、失败、取消和超时不修改 Draft；后续 typed changeset 确认仍使用既有租约与 Revision 原子 Apply。

## 用户体验

- 上传后自动恢复到同一向导，并轮询安全扫描状态。
- 单位和 Mapping Profile 均要求显式选择；原点、旋转和当前楼层在预览前可复核。
- 展示库存、映射冲突、低置信/阻断摘要及前 20 个语义对象。
- “转换确认”和“映射确认”两项均勾选后才能启动解析。

## 证据边界

仓库自动化覆盖扫描终态同步、成功 Preview、sealed Start、请求篡改拒绝、Draft stale 零 Job 写入、OpenAPI required 字段、双权限、API wrapper 和组件显式确认。

- 完整 Release solution：0 warning / 0 error。
- .NET 全量：3,744 passed；122 个既有环境用例 skipped。
- Space Unit：501/501；CAD 准备/解析聚焦：12/12；OpenAPI/权限/Controller：81/81。
- Web：752/752；Space Studio Playwright：8/8；Vue type-check 与生产构建通过。
- C#/TypeScript SDK drift、EF pending-model 与 `git diff --check` 通过。

本机未配置的真实 SQL Server、真实主备 Provider 和授权 CAD 不计为通过证据；它们分别由 WP3、WP7 和最终 GA 门禁关闭。
