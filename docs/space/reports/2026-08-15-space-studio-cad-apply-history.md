# Space Studio CAD 确认批次统一撤销/重做

日期：2026-08-15

需求：`LM-FR-024`（部分纵切：CAD Typed Changeset Apply）

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

CAD 待审变更集显式确认并原子合入 Draft 后，现在会进入 Space Studio 已有的统一撤销/重做命令栈。服务端根据实际提交结果密封补偿命令：新增使用 Delete/Restore，删除使用 Restore/Delete，修改使用提交前后的完整属性快照进行 Update/Update；客户端只接受这三类服务器命令，不重新推导 CAD 几何或身份。

该纵切关闭 `LM-FR-024` 中 CAD 确认批次的仓库缺口，但不代表整条需求完成。Excel–CAD 确认以及底图挂接/标定仍未接入同一历史栈，必须继续作为独立任务关闭。WP4 保持 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 冻结行为

- CAD Apply 仍是唯一的 Typed Changeset 原子入口，继续绑定 Lease、Client Instance、Floor Revision、Content Revision/Hash、Workspace Hash 和命令幂等身份。
- 通用 Element Command 结果保存首次修改前的元素及属性快照；同一命令批幂等回放返回完全相同的前态，避免用当前场景猜测撤销内容。
- Create 的撤销删除已分配 LogicalId，重做只 Restore；不会再次 Create 或产生新身份。
- Delete 的撤销 Restore 原 LogicalId，重做再次 Delete。
- Modify 的撤销和重做都使用完整 `UpdateProperties` 快照，覆盖几何、语义类型、尺寸、业务字段、业务链接和设计属性。
- 多项 CAD 变更的撤销命令逆序保存，重做保持正序；命令批继续要求目标身份唯一。
- 客户端验证应用数量、历史命令数量、目标身份、命令白名单和 Update 快照。若服务端返回不可验证历史，已提交 Draft 会重新加载，但工作台停止租约续租并保护性切换为只读，防止继续叠加不可恢复修改。

## 接口影响

- `SpaceElementCommandResultDto` 增加可选 `beforeElement` 与 `beforeAttributes`，并随命令批响应持久化到幂等回放记录。
- `ApplySpaceCadChangesetResponse` 增加必填 `undoCommands` 与 `redoCommands`。
- 新增 `SpaceSavedElementCommandDto`，只承载 `DeleteObject`、`RestoreLogicalObject` 或带完整快照的 `UpdateProperties`。
- OpenAPI、C# SDK 和 TypeScript SDK 已重新生成并通过二次生成漂移校验。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| CAD Apply / replay / Modify 前后快照 | 2 passed |
| SQL Server LocalDB 命令前态、原子写与幂等回放 | 1 passed / 0 skipped |
| OpenAPI / SDK 契约 | 45 passed |
| Space Unit 全量 | 533 passed |
| Web Vitest 全量 | 813 passed |
| Space Studio Playwright（CAD Apply → Undo → Redo） | 21 passed |
| Vue type-check / production build | passed |
| 完整解决方案构建 | passed / 0 warnings / 0 errors |
| SDK 二次生成 | working diff unchanged |

浏览器 E2E 使用受控 API fixture，LocalDB 只证明仓库事务行为；它们不替代真实 DWG/DXF、Site 主备 Provider、黄金 CAD、生产等价 CP6 WMS、双仓 Pilot 或五方签字。

## 后续任务

1. 将 Excel–CAD 权威匹配确认批次接入同一服务器密封的撤销/重做历史，保持权威匹配身份和 Revision Fence。
2. 明确底图挂接、解除挂接和标定的可逆合同，再接入同一历史栈；不能只保存前端临时状态。
3. 完成上述两项后重新审计 `LM-FR-024`，再决定是否关闭整条仓库需求。
