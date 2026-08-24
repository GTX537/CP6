# Space Studio Excel–CAD Apply 统一撤销/重做实现报告

日期：2026-08-15
任务分支：`codex/space-studio-excel-apply-history`

## 结论

Excel–CAD 权威确认已接入 Space Studio 既有统一撤销/重做历史。确认成功后，服务端从实际提交的不可变 Command Record 密封 v2 历史摘要；工作台只保存公开历史引用，Undo/Redo 均调用受租约、双 Revision、内容 Hash、当前对象状态和幂等键保护的服务端补偿端点。

该纵切关闭 LM-FR-024 的 Excel–CAD 历史缺口。LM-FR-024 仍剩 PDF/图片底图挂接与标定的可逆合同，WP4 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 实现范围

- `SpaceExcelCadApplyResult` 升级到 schema v2，保存服务器计算的 `historySha256` 和 `historyCommandCount`；既有 v1 成功结果仍可读取，但不能伪装成可安全补偿的历史。
- 原始 Apply 的 Rack、RackLevel、Location、WMS Binding、Design Attribute 和 Source 状态均由已持久化 Command Record 形成确定性历史。历史 Hash 覆盖有序记录身份、命令类型、目标和快照内容。
- 新增 `:compensate` Design V1 端点，支持 `Undo` / `Redo`。服务端复核租户与权限、活动页面租约、原 Apply 工件链、历史 Hash、Floor/Content Revision、当前对象状态和幂等回放；任一失败零写入。
- Undo 逆序恢复原始快照，Redo 正序恢复 Apply 后快照；每次补偿自身生成新的不可变 Command Batch 与 Command Record，并且只推进一次 Floor/Content Revision。
- 工作台将 Excel–CAD 成功结果加入与普通 Design V1/CAD Apply 相同的历史栈；请求失败保留同一补偿批次 ID 供幂等重试，成功写入后即使场景刷新失败也不会污染 Undo/Redo 栈。
- OpenAPI、C# SDK 和 TypeScript SDK 已同步；请求必填字段和稳定 Problem Details 已由契约测试保护。

## 关键安全与一致性边界

- 客户端不能提交补偿命令正文，也不能自行计算可信前态；服务端仅接受原 Apply ID、密封历史 Hash、方向和 Fence。
- 原 Apply 之后若对象、绑定、属性、Source 或 Revision 被其他操作改变，补偿返回 409，不覆盖后续编辑。
- SQL Server 中 `decimal(9,4)` 的数值状态通过强类型快照比较，避免 `0` 与 `0.0000` 的 JSON 文本差异造成误冲突。
- 并发数据库异常只返回稳定的通用冲突说明，不向客户端泄露底层 SQL 消息。

## 验证证据

- 完整 Release solution build：成功，0 warning / 0 error。
- Space Unit：533/533 通过。
- CP6.Tests：2921 通过，19 个既有环境跳过。
- Space Integration 默认环境：323 通过，112 个 SQL/外部环境用例按配置跳过。
- Excel–CAD 补偿 SQL Server LocalDB：1/1 通过，0 skipped；覆盖真实 Apply → Undo → Redo。
- Excel–CAD 聚焦后端：7/7 通过，并覆盖完整 Rack/层/库位/绑定/属性快照与介入编辑零写入。
- OpenAPI/权限聚焦：86/86 通过；SDK 二次生成检查无漂移。
- Web Vitest：817/817 通过；Vue typecheck 与 production build 通过。
- Space Studio mocked Playwright：21/21 通过；包含 Excel–CAD Confirm → Undo → Redo 与双 Revision 递增。

完整 Playwright 全项目尝试中的 Space Studio 项目全部通过；与本任务无关的 `setup/auth.setup.ts` 因本机 Playwright 1.62.1 所需浏览器 revision 未安装而中止，因此本报告不宣称全项目 Playwright 门禁通过，也不将其冒充产品失败。

## 剩余项

- 为 PDF/图片底图的挂接、替换、标定和撤销/重做冻结服务器可验证的可逆合同；完成后才能关闭 LM-FR-024。
- 使用已安装的 `D:\AutoCAD 2025` 开展独立 Provider 实机验证、许可证/隔离边界和 Site 认证；该工作属于 WP3/WP7，不由本纵切替代。
- 真实授权 CAD、第二条 Provider、双仓 Pilot、WMS 生产等价恢复和五方实名签字仍是核心 GA 100% 的外部门禁。
