# Space System 整仓模板按楼层写入 Draft 报告

日期：2026-08-15
任务分支：`codex/space-template-draft-apply`

## 结论

Design V1 已把上一纵切的不可变 System 整仓模板预览接入既有 Draft 权威。用户先显式创建 Draft 和目标 Floor，再在 Space Studio 对一个模板楼层执行一次确认；服务端从模板内容生成 Zone、Aisle、Rack、RackLevel 和 Location，不接受客户端上传任意布局对象。

标准模板两个楼层分别提交，单个楼层各自原子；本纵切不把两个目标 Floor 包成跨楼层事务。F1 的真实 SQL Server 结果为 3 个 Zone、10 个 Aisle、250 个 Rack、1,250 个 RackLevel 和 5,000 个 Location，并同步设置目标 Floor 边界。新 Location 保持未编码，继续进入既有批量编码 Preview → Apply 链。

## 合同与事务边界

- 新端点：`POST /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/templates/{templateId}:apply`。
- 请求必填 Site、模板版本、Proposal Hash、模板楼层 Key、CommandBatch、ClientInstance、Lease、Expected Floor Revision 和 Expected Content Revision。
- 服务端重新读取当前内置模板和版本，验证 Proposal Hash，再确定性生成 LogicalId/CommandId；客户端无法伪造 System 模板内容。
- Apply 复用 Layout Command 权威和同一 Serializable Floor 锁。正常交互批仍保持 100 条上限；内置模板专用路径允许最多 300 条，当前 F1 为 263 条、F2 为 264 条。
- 错误租约优先失败；过期 Proposal、Floor/Content Revision 冲突、重复 LogicalId 或任一命令失败均零写入。完成态同 CommandBatch 重放返回原结果且不重复创建对象。
- 本纵切没有数据库 Schema 或 Migration 变化；OpenAPI、C# 与 TypeScript SDK 同步。

## 工作台行为

- 「构件」上下文先列出 System 模板并生成密封预览；预览持续展示 Proposal Hash，确认前 Draft 零写入。
- 按当前 FloorCode 优先选中同编码的模板楼层，用户仍可显式选择其他模板楼层；界面显示该楼层的 Zone/Aisle/Rack/Location 数量。
- 确认对话框明确这是大批量 Draft 写入，不修改 Published/WMS。无租约、非 Draft、窄屏、Revision 冲突或其他保存进行中时禁止 Apply。
- 网络结果未知时保留原 CommandBatch，冻结模板和楼层选择，只允许同批安全重试；状态未确认期间禁止路由切层并启用浏览器离开提示。
- 成功后重新加载同一 Design Scene，2D/3D 不建立第二套模板场景。

## 自动化证据

- Space Unit：537/537；模板聚焦 3/3，验证确定性身份、计数、父级和命令上限。
- Space Integration 真实 SQL Server LocalDB：443/443、0 skipped；模板聚焦验证租约优先、过期 Proposal 零写入、5,000 库位整批、Floor 边界及幂等重放。
- CP6.Tests：2,925 passed、19 个既有外部环境门禁 skipped、0 failed；OpenAPI/权限聚焦 90/90。
- Web：167 个测试文件、856/856；新增 API 与模板面板 8/8，Vue TypeScript 和 production build 通过。
- OpenAPI/C#/TypeScript SDK 漂移检查通过；EF `has-pending-model-changes` clean。
- 完整 `CP6.slnx` Release 以非增量、单线程、禁用节点复用和共享编译方式通过：0 warning / 0 error；Web production build 同步通过。

## 未关闭范围

- Tenant 私有整仓模板的持久化、版本不可变、跨租户隔离和 System 模板只读治理尚未实现。
- Blank、Published、System Template、Tenant Template 尚未收敛为一个四模式创建向导；当前模板路径要求先创建 Draft/Floor。
- 本报告是仓库实现证据，不是独立 QA 接受、真实 Provider、黄金 CAD、双仓 Pilot 或五方签字。

因此 LM-FR-001/WP1 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。
