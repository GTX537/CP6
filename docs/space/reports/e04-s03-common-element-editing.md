# E04 S03 通用元素选择与属性编辑完成报告

- 状态：Complete
- 日期：2026-07-30
- 功能提交：`b322e84a`
- no-ff 集成提交：`39146c38`
- 集成分支：`integration/space-v1-20260730`
- Migration：`20260731035237_SpaceE04S03ElementCommands`
- 范围：Design V1 通用元素单选、属性编辑、删除标记、原子命令批次与逐命令审计

## 1. 交付结果

E04 S03 已把统一 Design Scene 中的通用元素接入可写楼层编辑器：

1. 2D 画布复用 `space-parametric-v1` RenderPlan，只投影 Active 通用元素，并与底图共用毫米坐标、固定缩放和 Y 轴翻转。
2. 点击墙、柱、门、限制区、辅助线等通用元素后建立唯一单选；空白点击清空选择，标定模式会禁用元素拾取。
3. 属性面板编辑整数毫米位置/尺寸、RotationZ、业务编码、业务实体关联和类型化设计属性。
4. 保存使用 `UpdateProperties`；删除使用 `DeleteObject`，领域状态转为 `RemoveRequested`，不物理删除修订历史。
5. 非 Draft 版本只读；写按钮受 `space:model:edit` 控制，只读或保存中时字段整体禁用。
6. 窄屏下画布与属性面板纵向排列，属性面板限制为 45vh，避免遮蔽整个编辑区域。

本卡保持单选和“一个批次中一个目标只出现一次”的边界，没有混入 E04 S04 的多选、对齐、分布、阵列或撤销栈。

## 2. 命令协议与并发

新增端点：

`POST /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands`

请求固定为 schema v1 命令批次，包含：

- `commandBatchId`：协议级幂等身份；
- `clientInstanceId`：编辑器实例身份；
- `expectedFloorRevision`：楼层乐观并发基线；
- 1–100 条强类型命令；S03 只接受 `UpdateProperties` 和 `DeleteObject`。

服务端先完整验证 schema、命令/目标身份、类型与 payload，再在 Serializable 事务中加载全部目标。任一目标不存在、不是 Active、payload 无效或 revision 冲突时，整个批次不写元素、不推进 revision，也不留下命令账本。

成功批次只推进一次 Floor revision 和 Version content revision。相同 `commandBatchId` 与相同完整请求稳定回放；相同 ID 搭配不同请求返回 `SPACE_COMMAND_CONFLICT`。陈旧 revision 返回 `SPACE_FLOOR_REVISION_CONFLICT`，客户端需重新加载楼层场景。

命令端点没有叠加通用 `Idempotency-Key` HTTP 头；`commandBatchId`、请求哈希和持久化响应就是本协议的幂等边界。

## 3. 数据一致性与审计

- `Space_ElementCommandBatch` 保存版本、楼层、客户端、预期/结果 revision、SHA-256 请求哈希、响应 JSON、时间与操作者。
- `Space_ElementCommandRecord` 为每条命令保存顺序、类型、目标、强类型 payload JSON、完整 before JSON 和 after JSON。
- 命令记录 append-only；已完成批次不可修改或删除，只允许在同一事务中从 Pending 状态完成一次。
- 数据库复合外键约束 Batch 的 Tenant + Version + Floor，Record 的 Tenant + Batch；唯一索引固定批次内命令顺序。
- 属性替换使用 namespace + key 不区分大小写唯一；被移除属性软删除，新属性继续继承元素的 Tenant/Version/Floor 边界。
- Published/Superseded 快照不可变护栏继续生效；写端点只接受精确 Draft。
- EF 幂等迁移脚本可生成，`has-pending-model-changes` 证明模型与 Migration 一致。

OpenAPI、C# SDK 与 TypeScript SDK 已同步生成并通过 drift 检查。

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| Space UnitTests | 213/213 passed；合并态再次 213/213 |
| 默认 Space IntegrationTests | 48 passed / 44 SQL-gated skipped |
| `KOUSQLSERVER` E04-S03 命令闭环 | 1/1 passed，无跳过；覆盖更新、属性替换、幂等回放、revision 冲突、批次失败回滚、删除标记与 before/after 审计 |
| API / OpenAPI / 权限聚焦 | 21/21 passed |
| 前端 E04-S03 聚焦 | 4 files / 8 tests passed |
| 前端全量 | 95 files / 569 tests passed；合并态再次通过 |
| 前端 type-check | passed；合并态再次通过 |
| 前端 production build | passed；仅保留既有大 chunk 提示 |
| 合并态 `dotnet build CP6.slnx --no-restore` | 0 error；10 个既有 OA/WMS/测试 nullable/analyzer warning |
| SDK | OpenAPI、C#、TypeScript drift check passed |
| EF Migration 一致性 | 无待迁移模型变化；独立幂等 SQL 脚本生成通过 |
| 差异门禁 | `git diff --check` passed；3 个 `.bak` Git LFS 假修改未暂存 |

`CP6.Tests` 全量本轮为 2682 passed、6 failed、17 environment-gated skipped。6 个失败均在采购 `RfqServiceTests`，原因是固定测试报价有效期 `2026-07-31 00:00` 已早于进程 `DateTime.Now`；同一失败已在未含 S03 的 `f8dff096` 基线独立复现，因此不是本卡回归。E04-S03 涉及的 OpenAPI/权限 21 项全部通过，本卡没有扩大范围修改采购模块。

默认 SQL 跳过项仍是环境门禁，不记作已通过；E04-S03 数据库测试已使用本机 `KOUSQLSERVER`、Windows 集成认证和独立临时数据库真实执行，结束后自动清理。

## 5. 下一步

下一张独立卡固定为 E04 S04：多选、对齐、分布与阵列命令。它应扩展当前命令批次和选择模型，并补充组操作原子性、并发、审计和撤销边界；不得把 E07 S05 WMS 运行态或 E13 AI 生成能力混入编辑器。
