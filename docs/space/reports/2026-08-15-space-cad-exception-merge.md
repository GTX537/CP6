# Space Studio CAD 异常对象合并

日期：2026-08-15

需求：`LM-FR-017`

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

Space Studio 已支持把 2–20 个同类型通用 CAD 异常元素合并为一个可编辑组合元素。合并保留用户首选对象的 LogicalId，把其余来源对象标记为 `RemoveRequested`；保存、撤销和重做均继续使用现有 Design V1 命令批，因此共享编辑租约、Floor/Content Revision、幂等、Serializable 事务和命令审计边界。

该实现没有增加第二条写接口。正向批次由一个 `UpdateProperties` 和若干 `DeleteObject` 组成，补偿批次由原属性 `UpdateProperties` 和对应 `RestoreLogicalObject` 组成。真实 SQL Server LocalDB 用例证明正向与补偿批次分别只推进一次 Floor/Content Revision，并分别产生完整命令记录。

## 冻结合并语义

- 第一个选中元素是幸存对象，LogicalId、业务父关系和属性权威保留。
- 仅允许 Active、非资产、同 Floor、同 Parent、同 ElementType、同 BusinessCode、同业务链接和同设计属性的通用元素合并；冲突时写入前失败。
- 组合几何使用 `schemaVersion=1`、`kind=group`，逐部件保存来源 LogicalId、可选 SourceId/SourceRef、相对坐标、旋转、尺寸和原始嵌套几何。
- 资产几何不能进入组合；最多 100 个部件、最多 8 层组合嵌套，未知或畸形输入失败关闭。
- 2D 与 3D 从同一组合几何递归生成图元；所有部件继续映射到幸存 LogicalId，多部件边界会聚合后参与后续移动、对齐和选择。
- 组合元素允许整体移动、旋转、改类型和属性编辑；组合尺寸不允许在属性面板中直接改写，避免缓存包围盒与内部几何发生静默漂移。

## LM-FR-017 当前矩阵

| 能力 | 当前状态 | 证据边界 |
|---|---|---|
| 改类型 | 已实现 | `2026-08-15-space-cad-exception-retype.md` |
| 删除 | 已有实现 | `DeleteObject`、批量删除与补偿命令链 |
| 合并 | 已实现 | 本报告中的领域、真 SQL、前端、2D/3D 与 E2E 自动化 |
| 拆分 | 已由后续纵切实现 | `2026-08-15-space-cad-exception-split.md` |
| 重画 | 已由后续纵切实现 | `2026-08-15-space-cad-exception-redraw.md` |

WP4 继续为 `Partial/Pending`，核心 GA 继续为 72% / `NoGo`。拆分和画布重画均已由后续纵切关闭；仓库实现也不替代真实 DWG/DXF、主备 Provider、黄金集、生产等价 WMS、双仓 Pilot 或五方签字。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| Space Element 几何聚焦单测 | 50 passed / 0 failed |
| 前端合并、组合渲染、属性和画布聚焦测试 | 21 passed / 0 failed |
| Space Unit 全量回归 | 531 passed / 0 failed / 0 skipped |
| 前端 Vitest 全量回归 | 788 passed / 0 failed |
| Vue TypeScript 检查 | passed |
| SQL Server LocalDB 合并与补偿 | 1 passed / 0 failed / 0 skipped |
| Space Studio 合并、撤销、重做 Playwright | 1 passed / 0 failed |
| Space Studio Playwright 全量回归 | 16 passed / 0 failed |
| 完整 Release solution | 0 warnings / 0 errors |
| 前端 production build | passed |
| OpenAPI/C#/TypeScript SDK 漂移 | passed |
| GA 证据自测 | 36 passed / 0 failed |
| GA 普通/严格门禁 | exit 0 / expected exit 2 (`NoGo`) |

上述门禁均在任务分支执行。`-RequireGaReady` 继续失败是正确结果：5 类外部输入、9 个接受 Gate 和 5 个签字仍为 Pending。

## 后续任务

1. 异常对象拆分已由 `2026-08-15-space-cad-exception-split.md` 关闭。
2. 在独立分支实现画布重画与原子几何替换。
3. 重画完成后复核 LM-FR-017 及 WP4 其余详细条目；只有仓库实现和真实外部接受都满足时才能提升状态。
