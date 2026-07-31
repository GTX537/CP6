# E04 S04 多选、对齐、分布与阵列统一命令完成报告

- 状态：Complete
- 日期：2026-07-31
- 功能提交：`9a87dc30`
- no-ff 集成提交：`f9c7fd21`
- 集成分支：`integration/space-v1-20260730`
- Migration：无；复用 E04-S03 append-only 命令批次与逐命令审计表
- 范围：货架与通用元素统一多选、批量移动/旋转/删除、对齐、等距分布、货架阵列及保存后补偿式撤销/重做

## 1. 交付结果

E04 S04 已把货架和通用元素接入同一套 Design V1 编辑协议：

1. 2D 画布继续复用 `space-parametric-v1`，只把 Active 货架 envelope 与 Active 通用元素投影为可选对象，RackLevel 图元不会造成重复选择。
2. 普通点击替换选择，Ctrl/Meta/Shift 点击切换选择；空白拖框执行套索多选，空白点击清空选择。标定模式仍会关闭对象拾取。
3. 同一批选区可执行左/中/右、上/中/下对齐，水平/垂直等距分布，±90° 旋转和批量删除；工具栏在写入前显示对象数与世界坐标边界。
4. 所有对齐和分布结果先在客户端确定为强类型 `MoveObject` 命令，再作为一个原子批次提交；服务端不会重新解释 UI 意图。
5. 货架阵列把所选模板货架计为第一个单元，支持行、列、行距、列距、奇数行错列、编码前缀、起号和补零位数预览。
6. 阵列复制货架几何、模板引用与 Active RackLevel；设计库位使用新 LogicalId、空 LocationCode、`Generated` 和 `Unbound`，不复制 WMS 绑定语义。
7. 属性保存、批量移动/旋转/删除和阵列均进入同一保存后历史栈；撤销/重做提交新的补偿批次，不回写旧审计、不倒转数据库时间。

本卡没有引入 WMS 运行态、AI、CAD 问题定位、发布校验或 2D/3D 同源预览。E07-S05 的前置依赖已解除，但其拉取、绑定和差异对账仍是下一张独立卡。

## 2. 命令协议与原子性

端点保持不变：

`POST /api/space/design/v1/versions/{versionId}/floors/{floorLogicalId}/commands`

schema v1 在向后兼容 S03 的基础上新增：

- `MoveObject { x, y, z }`
- `RotateObject { rotationZ }`
- `RestoreLogicalObject`
- `GenerateRackArray { rows, columns, rowGap, columnGap, staggerOffset, codePrefix, startNumber, codeDigits }`

`UpdateProperties` 和 `DeleteObject` 继续有效。每条命令只能携带与类型匹配的唯一强类型 payload；一个目标在同一批次最多出现一次。

服务端在 Serializable 事务内预加载全部 Element/Rack 目标及货架层/库位，先验证目标类型、生命周期、楼层 revision、阵列数量、层/库位上限和整批编码冲突，再执行任何变更。成功批次只推进一次 Floor revision 与 Version content revision；任一目标不存在、编码冲突、坐标溢出、payload 无效或 revision 陈旧时整批回滚并清理 EF 跟踪态。

协议继续以 `commandBatchId + requestHash` 作为幂等边界。新响应保留 S03 的 `affectedObjects`，并增加 `affectedRacks`、`affectedRackLevels` 和 `affectedLocations`，使阵列首次生成后可以获得稳定 LogicalId，并用 Delete/Restore 补偿完成重做。

## 3. 生命周期、编码与审计

- 删除货架会把货架及其 Active 层/库位转为 `RemoveRequested`；原本 Disabled 的子对象保持 Disabled。
- 恢复只把该次删除留下的 `RemoveRequested` 子对象恢复为 Active，LogicalId、货架编码和历史记录不变。
- 编码冲突检查覆盖同版本同 Zone 的全部货架，包括已标记待移除但仍占用数据库唯一索引的货架。
- 阵列总数含模板，限制为 2～100 个；单批生成最多 2,000 层、5,000 库位。
- 每条混合命令继续写入原 append-only Record；货架审计包含几何、生命周期、层与库位规范，阵列 after 值包含全部生成对象。
- Published/Superseded 仍不可写；权限继续使用 `space:model:edit`，Tenant/Site 数据范围和 Draft 精确状态门禁未放宽。

OpenAPI、C# SDK 与 TypeScript SDK 已同步生成并通过 drift 检查；本卡没有修改 EF 模型，因此没有新增 Migration。

## 4. 验证证据

| 门禁 | 合并态结果 |
|---|---|
| 完整 solution build | 0 error；10 个既有 OA/WMS/测试 nullable/analyzer warning |
| Space UnitTests | 213/213 passed |
| 默认 Space IntegrationTests | 48 passed / 45 SQL 环境门禁 skipped |
| `KOUSQLSERVER` Design Scene 真实事务链 | 3/3 passed，无跳过 |
| E04-S04 新真实 SQL 闭环 | 混合 Element/Rack 批次、补偿撤销、2×2 阵列、层/库位复制、Delete/Restore 重做、缺失目标整批回滚、编码冲突回滚和 before/after 审计均通过 |
| API / OpenAPI / 权限聚焦 | 25/25 passed |
| 前端全量 | 96 files / 575 tests passed |
| 前端 type-check | passed |
| 前端 production build | passed；仅保留既有大 chunk 提示 |
| SDK | OpenAPI、C#、TypeScript drift check passed |
| EF | `has-pending-model-changes` 返回无模型变化 |
| 差异门禁 | `git diff --check` passed；三个 `.bak` Git LFS 假修改未暂存 |

默认 SQL 跳过项仍按环境门禁记录，未伪装为通过。Design Scene 三项测试使用本机 `KOUSQLSERVER`、Windows 集成认证和独立临时数据库真实执行，结束后自动删除。

## 5. 下一步

E04 S04 已完成并解除 E07 S05 的采用前置条件。下一张建议卡为 E07 S05“存量 WMS 采纳与绑定”：复用现有 E07 S02 真实适配器和本卡统一对象选择/补偿命令，完成拉取、放置、绑定、未绑定与差异对账；不得把 WMS 运行态直接写入 Design Revision。
