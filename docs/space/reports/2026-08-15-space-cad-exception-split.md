# Space Studio CAD 异常对象拆分

日期：2026-08-15

需求：`LM-FR-017`

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

Space Studio 已支持把一个 Active、非资产的 `schemaVersion=1/kind=group` 通用元素拆分为 2–100 个独立元素。首个部件沿用当前组合对象 LogicalId，其余部件由客户端分配新的 LogicalId；保存、撤销和重做继续使用 Design V1 同一命令批写链，没有新增第二套领域权威或拆分专用写接口。

该纵切关闭 LM-FR-017 的“拆分”仓库实现。LM-FR-017 仍缺画布重画，因此 WP4 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。

## 冻结语义

- 仅允许单选 Active、非资产、有效 `group` 几何；组合必须包含 2–100 个部件，来源 LogicalId 必须唯一，未知或畸形输入在写入前失败关闭。
- 首个部件保留当前 LogicalId；其余部件不复用历史来源 LogicalId，而是分配新的唯一 LogicalId，避免与被合并后仍留在审计历史中的 `RemoveRequested` 对象冲突。
- 每个新元素继承组合的 ElementType、ParentLogicalId、BusinessCode、业务链接和设计属性；部件自己的 SourceId/SourceRef 成对保留。创建合同因此以向后兼容的可选字段补齐 LinkedEntityType/LinkedLogicalId。
- 组合整体平移或旋转后，拆分使用与参数化渲染器相同的局部到世界坐标变换；前端自动化证明拆分前后 2D 与 3D 的几何、尺寸和旋转一致。
- 正向批次为“更新幸存对象 + 创建新对象”；撤销为“恢复组合 + 删除新对象”；重做为“恢复首部件 + Restore 新对象”。重做不会再次 Create，也不会更换 LogicalId。
- 三个批次都使用当前 Lease、Floor Revision、Content Revision/Hash、命令批幂等和 Serializable 原子审计边界；任一验证失败零写入。

## 接口变化

`SpaceCreateElementDto` 新增两个可选字段：

- `linkedEntityType`
- `linkedLogicalId`

两者必须同时为空或同时提供，LogicalId 不能是空 Guid，类型最长 100 字符。OpenAPI、C# 与 TypeScript SDK 已同步；命令 schemaVersion 仍为 1，既有调用方不受影响。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| 拆分、继承、拒绝、2D/3D 等价聚焦单测 | 5 passed |
| 批量工具面板聚焦单测 | 2 passed |
| 前端全量 Vitest | 794 passed |
| Space Unit | 531 passed |
| SQL Server LocalDB 原子拆分/补偿/重做/非法业务链接零写入 | 1 passed / 0 skipped |
| Space Studio Playwright | 17 passed |
| Vue type-check / production build | passed |
| OpenAPI 字段与 TypeScript required/optional 合同 | passed |
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
| 拆分 | 已实现 | 本报告中的合同、真 SQL、2D/3D、前端与 E2E 自动化 |
| 重画 | 未实现 | 仍需画布绘制交互、几何校验、原子替换和撤销合同 |

## 后续任务

1. 在独立分支实现异常对象画布重画与原子几何替换。
2. 重画完成后复核 LM-FR-017 和 WP4 其它详细条目；仓库实现闭环不等于外部接受完成。
3. 使用授权真实 DWG/DXF、真实 Excel/PDF/图片、两条 Site 已认证 Provider 和 CP6 WMS 形成正式端到端证据。
