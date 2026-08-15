# Space Studio CAD 人工校正锁定

日期：2026-08-15

需求：`LM-FR-018`

证据类别：RepositoryImplementation

接受结论：不构成 GA 接受证据

## 结论

CAD 来源通用元素现在可以在 Space Studio 属性检查器中“保存并锁定”人工校正。锁状态、单调递增的校正版本、最后校正人和 UTC 时间持久化在 Design Revision；重新解析同一 SourceRef 时，锁定对象只能进入不可应用的 Blocking Conflict，不能作为修改或删除写回 Draft。用户显式解除锁定后，后续解析才可重新提出可应用变更。

锁定、解除锁定和锁定后的继续编辑均复用现有 `UpdateProperties` 命令批，继续受 Lease、Floor Revision、Content Revision/Hash、幂等与 Serializable 事务保护，没有新增旁路写接口。该纵切关闭 `LM-FR-018` 的仓库实现；WP4 仍为 `Partial/Pending`，核心 GA 保持 72% / `NoGo`。

## 冻结行为

- 只有带成对 `SourceId` / `SourceRef` 的 CAD 来源元素可以锁定；手工空白画布元素请求锁定时在写入前失败关闭。
- 首次锁定把 `UserCorrectionVersion` 从 0 推进到 1；锁定状态下的属性、移动、旋转、删除或恢复继续递增版本，并更新最后操作者和 UTC 时间。
- 解除锁定保留历史版本和最后校正信息，不把历史清零；撤销/重做通过补偿命令显式恢复锁状态。
- CAD PreviewSet 重新生成 Typed Changeset 时，命中锁定 SourceRef 的 Modify/Delete 转为 `Conflict`，`CanApply=false`，稳定码为 `SPACE_CAD_MANUAL_CORRECTION_LOCKED`。
- 审核空间为每个锁定冲突生成可定位的 Blocking 问题并展示校正版本；选中不可应用冲突不会产生 Draft 写入。
- 服务端 Element Command Batch 也执行最终 Apply Fence：任何携带 CAD Changeset 身份、并指向锁定元素的变更命令都返回稳定 409，防止客户端绕过审核 UI。
- 版本克隆保留锁状态与校正元数据；新增加法迁移及 SQL Check Constraint，旧数据默认保持未锁定、版本 0。

## 接口与数据影响

- `SpaceUpdateElementPropertiesDto` 增加可选 `manualCorrectionLocked`，使锁定切换和当前表单在同一原子命令内保存。
- `SpaceSceneElementDto` 增加锁状态、校正版本、最后操作者和时间；`SpaceCadChangeV1` 增加必填锁状态与校正版本。
- OpenAPI、C# SDK 和 TypeScript SDK 已重新生成；字段级契约测试锁定 CAD Change 的 required 语义。
- 新迁移：`20260815201701_SpaceCadManualCorrectionLock`；没有修改已发布迁移。

## 可复现门禁

| 门禁 | 结果 |
|---|---:|
| Space Element 领域锁定与版本规则 | 52 passed（聚焦类） |
| Space Unit 全量 | 533 passed |
| CAD 重新解析锁定冲突与零写入 | 1 passed |
| SQL Server LocalDB 持久化与最终 Apply Fence | 1 passed / 0 skipped |
| OpenAPI/SDK 契约 | 45 passed |
| 前端全量 Vitest | 809 passed |
| Vue type-check / production build | passed |
| Space Studio Playwright（锁定、撤销、重做） | 20 passed |
| EF pending model changes | none |
| Release solution | passed / 0 warnings / 0 errors |

Mock 浏览器与 LocalDB 只证明仓库行为。真实 DWG/DXF、主备 Provider、黄金 CAD、生产等价 CP6 WMS、双仓 Pilot 和五方签字仍须按 GA 证据门禁执行。

## 后续任务

1. 审计 `LM-FR-024` 的统一撤销/重做，重点核对 CAD/Excel/底图导入确认批次是否全部可补偿且保持稳定身份。
2. 继续逐项审计 `LM-FR-020`～`LM-FR-029` 与三条建模路径，按独立纵切关闭真实仓库缺口。
3. 在两条已认证 Provider 链和授权真实 CAD 上验证人工锁定、重新解析、冲突处置与最终发布全链。
