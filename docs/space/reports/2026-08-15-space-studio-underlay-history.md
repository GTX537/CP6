# Space Studio 底图统一撤销/重做实现报告

日期：2026-08-15
任务分支：`codex/space-studio-underlay-history`

## 结论

PDF/PNG/JPG 底图的挂接、替换、标定和显式移除已接入 Space Studio 公共撤销/重做历史。所有写入统一使用页面编辑租约、Floor Revision、Content Revision、CommandBatch 和幂等键；服务器从实际提交前后态密封历史，客户端不能提交可信恢复快照。

该纵切关闭 LM-FR-024 的最后一个仓库实现缺口。CAD、Excel–CAD 与底图现在共享同一用户历史入口，但 WP4 的真实多路径、Provider、CP6 WMS 和现场接受仍未完成，因此 WP4 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 实现范围

- 扩展既有 Attach 与 Calibration 合同，必填 `clientInstanceId`、`leaseId`、Floor/Content Revision 和 `commandBatchId`；Attach 的 `sourceId` 字段必须显式出现，非空表示挂接/替换，`null` 表示移除。
- Attach、Replace、Calibrate 与 Detach 在 SQL Serializable 事务及统一楼层 application lock 内验证数据库 UTC 租约和双 Revision，然后同时推进 Floor/Content Revision。
- 复用不可变 `SpaceElementCommandBatch` / `SpaceElementCommandRecord` 保存底图 Source、Calibration 指针和变换的强类型前后态；没有新增第二套历史表或第二个设计权威。
- 新增 `underlay:compensate` 端点。Undo/Redo 只接收原 CommandBatch、方向、密封 Hash 和当前 Fence；服务端复核历史、当前底图状态与追加式 Calibration 行后恢复精确指针，并生成新的不可变补偿批次。
- 幂等回放只在 Floor/Content Revision 仍等于首次响应时成立，Draft 后续变化不会返回陈旧成功；租约过期和回放期限均使用 SQL Server `SYSUTCDATETIME()`。
- 工作台在来源面板提供“移除底图”，并把挂接、替换、标定和移除加入既有 `SavedCommandHistory`；失败重试保留补偿批次 ID，Undo/Redo 后重新加载权威场景。
- OpenAPI、C# SDK、TypeScript SDK、权限 Problem Details 与契约断言同步；`sourceId` 在 OpenAPI 中为 required + nullable。

## 关键验证

- Space Unit：533/533 通过。
- CP6.Tests：2922 通过，19 个既有环境门控用例跳过；OpenAPI/权限聚焦 87/87 通过。
- Space Integration 默认环境：323 通过，112 个 SQL/外部环境用例按配置跳过。
- 底图 SQL Server LocalDB：2/2 通过，0 skipped；覆盖 Attach/Replay、历史篡改拒绝、跨页面会话拒绝、Undo/Redo、Detach/Undo、Calibration Undo/Redo、Replace 清除标定及 Undo 精确恢复旧标定。
- Web Vitest：821/821 通过；Vue typecheck 通过。
- Space Studio mocked Playwright：21/21 通过；新增用例实际执行上传 → 挂接 → 标定 → Undo → Redo → 移除 → Undo 恢复。
- OpenAPI/C#/TypeScript SDK 已重新生成，二次漂移门禁通过；完整 `dotnet build CP6.slnx -c Release --no-restore` 通过，0 warning / 0 error。

## AutoCAD 本机状态

- 已核实 `D:\AutoCAD 2025\accoreconsole.exe`，版本 `25.0.58.0.0`，Authenticode 签名有效，可继续用于现有实验型 DWG → DXF → CAD IR 开发链。
- `D:\AutoCAD 2025\acad.exe` 版本 `R25.0.58.0.0` 仍为 `HashMismatch`。正式 GUI 使用、Provider 认证或 GA 证据前必须由 Autodesk 修复/重装，或由安全与客户形成受控例外；本报告不把它标为通过。
- AutoCAD 安装不替代第二条 Provider、Site 批准、20 份授权黄金 CAD、隔离 Worker 或 Provider 评分。

## 后续

- 继续逐项审计 LM-FR-020～029 与三条建模路径，选择下一条仍缺仓库实现的最小纵切。
- 独立推进 AutoCAD Core Console 的许可证/隔离边界和 Site Provider 试验；GUI HashMismatch 未解决前失败关闭。
- 真实授权 CAD、主备 Provider、CP6 WMS 生产等价恢复、双仓 14 天 Pilot 和五方实名签字仍为核心 GA 100% 的硬门槛。
