# Space Studio CAD 异常对象画布重画

日期：2026-08-15

需求：`LM-FR-017`

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

Space Studio 已支持在 2D 画布上重画一个 Active、非资产通用元素的多边形轮廓。绘制阶段只保存在浏览器本地，用户显式确认前 Draft 零写入；确认后复用 Design V1 的单条 `UpdateProperties` 命令原子替换几何，并保留原 LogicalId、ElementType、BusinessCode、业务链接、设计属性和 CAD SourceId/SourceRef。

该纵切关闭 LM-FR-017 的“重画”仓库实现。LM-FR-017 的改类型、删除、合并、拆分和重画现均有仓库实现；WP4 仍保持 `Partial/Pending`，因为授权真实 DWG/DXF、Excel、PDF/图片、两条 Site 已认证 Provider、真实 CP6 WMS 和正式接受证据尚未完成。核心 GA 继续为 72% / `NoGo`。

## 冻结交互与几何语义

- 仅允许单选 Active、非资产、有效 `schemaVersion=1` 的通用元素；Rack、Zone、Aisle 和资产实例不进入该工具。
- `R` 进入重画，画布点击依次添加世界毫米整数顶点；`Backspace` 回退一个顶点，`Enter` 完成，`Esc` 取消。命令栏同时提供可达的“重画/完成重画”按钮，状态栏持续显示顶点数与未保存状态。
- 多边形限制为 3–100 个互异顶点；重复顶点、零面积、自交、超出 Int32 的坐标或包络尺寸在写入前失败关闭。
- 保存时把世界轮廓规范化为局部逆时针 `polygon.outer`，以最小 X/Y 为元素原点，旋转归零，并由同一包络计算 Width/Depth；原 Height/Z 保留。
- 确认弹窗明确说明同一 LogicalId、元数据/CAD 来源保留以及保存后可撤销。确认前网络命令数为零。
- 正向、撤销和重做均为同一 LogicalId 的 `UpdateProperties`；继续受 Lease、Floor Revision、Content Revision/Hash、幂等和 Serializable 原子审计保护，没有新增重画专用写 API。
- 2D Konva 和 3D 参数化 Viewer 都消费保存后的同一 `polygon` 几何；草稿 3D 不引入运行态库存、人员或设备数据。

## 数据与接口影响

本任务没有数据库迁移、OpenAPI 或 SDK 合同变化。重画复用既有 `ApplySpaceElementCommandBatchRequest` 与 `UpdateProperties`，因此 CAD SourceId/SourceRef 继续由现有 Revision 保留，设计属性通过更新快照重写并接受同一审计链保护。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| 重画计划、几何拒绝、2D/3D 同源与覆盖层聚焦 Vitest | 6 passed |
| 前端全量 Vitest | 800 passed |
| Space Unit | 531 passed |
| OpenAPI 聚焦合同 | 44 passed |
| SQL Server LocalDB 原子重画/补偿/重做/来源属性审计 | 1 passed / 0 skipped |
| Space Studio Playwright（含确认前零写入） | 18 passed |
| Vue type-check / production build | passed |
| OpenAPI/C#/TypeScript SDK drift | passed |
| Release solution | passed / 0 warnings / 0 errors |
| GA 证据自测 | 36 passed / 0 failed |
| GA 普通/严格门禁 | exit 0 / expected exit 2 (`NoGo`) |

LocalDB、Mock 浏览器和仓库自动化只证明实现行为，不能替代真实授权 CAD、Site 主备 Provider、生产等价 WMS、双仓 Pilot 或正式签字。

## LM-FR-017 当前矩阵

| 能力 | 当前状态 | 证据边界 |
|---|---|---|
| 改类型 | 已实现 | `2026-08-15-space-cad-exception-retype.md` |
| 删除 | 已有实现 | `DeleteObject`、批量删除与补偿命令链 |
| 合并 | 已实现 | `2026-08-15-space-cad-exception-merge.md` |
| 拆分 | 已实现 | `2026-08-15-space-cad-exception-split.md` |
| 重画 | 已实现 | 本报告中的本地草稿、几何校验、原子保存、2D/3D、真 SQL 与 Playwright 证据 |

## 后续任务

1. 对 WP4 其余详细 LM-FR 条目重新做仓库实现审计，不因 LM-FR-017 闭环而自动把整个 WP4 标记 Complete。
2. 使用授权真实 DWG/DXF、真实 Excel/PDF/图片、两条 Site 已认证 Provider 和 CP6 WMS 形成正式端到端证据。
3. 按冻结索引完成 20 份黄金 CAD、生产等价性能/恢复、安全、双仓 14 天 Pilot 和五方签字后，才可提升核心 GA 百分比。
