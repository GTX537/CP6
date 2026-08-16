# Space Studio 当前 CAD + Excel 工作流报告

日期：2026-08-16

任务分支：`codex/space-studio-excel-upload-flow`

## 结论

Space Studio 已把现有 Design V1 Excel 上传、来源扫描、Mapping Profile、预检、Excel–CAD 权威匹配和确认 Apply 接成一个无需内部 ID 的当前工作会话流程。用户从已加载且未过期的 CAD 审核结果进入，上传 `.xlsx`、复核服务器预检、显式确认匹配，再进入既有 Lease/Revision 保护的 Apply 面板。确认 Apply 前 Draft 零写入。

该结论关闭“当前 CAD 工作会话”的统一 Excel 上传 UI 缺口，不代表真实 CAD+Excel 现场接受完成，也不提供历史 CAD 候选目录。WP4 继续 `Partial/Pending`，核心 GA 继续 72% / `NoGo`。

## 工作流与权威边界

1. 入口只在 URL 中的 CAD Source/Parse Job 已自动加载为当前 Floor 的新鲜 Review Workspace 时启用；过期或未加载的 CAD 不能静默匹配。
2. 浏览器只接受 `.xlsx` 且限制 50 MiB，上传继续使用既有 Design V1 Excel Source 接口；服务端扫描状态达到 `Ready/PreviewReady` 后才启动预检。
3. Mapping Profile 从服务器目录选择。客户端不生成 Profile、权威 Hash、默认内部 ID 或解析结果。
4. Excel Source/Preflight Job 写入 URL，可在刷新后恢复后台预检；失败或超时不改变 Draft。
5. 预检展示数据行、有效行、Info/Warning/Blocking 和工作表/行/列恢复提示；Blocking 或 `canConfirm=false` 时禁止生成匹配。
6. 用户显式勾选复核确认后，匹配请求绑定当前 CAD Source/Parse Job、Floor 和 Content Revision。匹配 Job 自动轮询到终态，再交给既有服务器权威结果面板。
7. 最终 Apply 继续要求当前页面实例、有效 Lease、Floor/Content Revision 和服务器 Artifact 身份；成功后进入统一撤销/重做历史。
8. 删除当前 CAD 来源会同时清除依赖的 Excel/Preflight/Match 路由状态；删除当前 Excel 来源会清除其 Preflight/Match 状态，避免恢复失效链。

## 自动化证据

- 新向导单测覆盖上传、扫描 Ready、预检、显式确认、精确 CAD/Excel/Revision 请求、刷新恢复和 Blocking 禁用。
- 上下文面板单测覆盖“无当前 CAD 禁用、有新鲜 CAD 启用”。
- Excel–CAD 面板单测覆盖后台匹配自动轮询；Apply 状态仍保留人工刷新恢复入口。
- 聚焦 Web：14/14 passed。
- Web 全量：173 个测试文件、878/878 passed。
- Space Studio Playwright：25/25 passed；新增用例从当前 CAD 进入 Excel 上传、预检、匹配并完成 Apply，且不填写内部 ID。
- Vue TypeScript 通过；生产 Web 构建通过。构建只保留既有大 Chunk 警告，无新增错误。

## AutoCAD 本机输入状态

本机 AutoCAD 2025 的 Core Console `25.0.58.0.0` 仍为签名有效的开发转换工具；GUI `acad.exe` 的 Authenticode 仍为 `HashMismatch`。本任务没有启动 GUI，也没有把安装路径写入应用配置。既有开发报告继续是 `DevelopmentEvidence`，不能替代 Site 主备 Provider、许可/安全/客户批准或黄金 CAD。

## 未关闭范围

- 历史 CAD Source/Parse Job 的可搜索候选目录与显式重新关联仍未实现；当前流程只消费本楼层当前已加载 CAD。
- 必须用授权真实 DWG/DXF + Excel 在两个 Site 已批准 Provider 链、生产等价扫描 Worker 和 CP6 WMS 中完成浏览器 E2E。
- 20 份黄金 CAD、50 MiB P95、受训用户 Ready、双仓 14 天 Pilot 和五方签字仍是硬门槛。
