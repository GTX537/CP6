# E05-S02 逐层货架规格完成报告

- 状态：**Complete**
- 证据日期：2026-07-30
- 功能提交：`2fc03681`
- 集成提交：`3d554852`

## 1. 交付结论

`Space_RackLevelRevision` 现在完整表达同一货架内各层相互独立的规格：

- `LevelNo`：从 1 开始的层号；
- `BottomZ`：相对货架 Z 原点的底面标高，整数 mm；
- `ClearHeight`：可用净高，正整数 mm；
- `BinCount`：本层格口数；
- `DepthCount`：本层单/双深等深度数；
- `CellWidth` / `CellDepth`：本层单元尺寸，正整数 mm；
- `BeamHeight`：横梁高度，非负整数 mm；
- `MaxLoad`：可选、非负的本层承重。

不同层不共享统一参数。`UpdateSpecification` 对创建和更新使用同一组失败关闭约束，并在全部参数验证成功后一次性写入，避免无效更新造成部分字段变化。

本卡不生成 Location、不扩展场景 DTO、不实现资产库，也不物化旧运行态 Rack 摘要。

## 2. 兼容与领域约束

既有 `Create` 的可选 `maxLoad` 参数位置保持不变，`beamHeight` 追加为默认 `0`，避免破坏已有位置参数调用。

创建和更新统一拒绝：

- `LevelNo/ClearHeight/BinCount/DepthCount/CellWidth/CellDepth <= 0`；
- `BottomZ/BeamHeight < 0`；
- `MaxLoad < 0`；
- 空 Rack LogicalId。

一次更新中任何字段无效时，原层号、标高、尺寸、横梁和承重均保持不变。

## 3. Migration 与持久化

既有 E01-S04 表已经提供：

- `(TenantId, ModelVersionId, LogicalId)` 稳定快照身份；
- Rack 的 Tenant+Version+LogicalId 复合外键；
- `(TenantId, ModelVersionId, RackLogicalId, LevelNo)` 活动行唯一索引；
- Tenant 查询过滤器和 Published/Superseded 快照写保护。

新 Migration `20260731001924_SpaceE05S02RackLevelSpecification`：

1. 为已有行以安全默认值 `0` 新增非空 `BeamHeight`；
2. 将数据库 Check Constraint 收紧为：
   - `LevelNo/ClearHeight/BinCount/DepthCount/CellWidth/CellDepth > 0`；
   - `BottomZ/BeamHeight >= 0`；
   - `MaxLoad IS NULL OR MaxLoad >= 0`。

Migration 同时交付单迁移幂等 SQL。若历史数据存在负 `BottomZ`，添加新约束会失败并要求先审计修复，不在迁移中静默改写。

Published→Draft 集合式克隆 SQL 已加入 `BeamHeight`，真实 SQL 测试验证 `BeamHeight`、`MaxLoad` 和 LogicalId 保真。

## 4. 权限、错误与回滚

- 权限沿用 `space:model:edit`；没有新增 HTTP 或权限种子。
- 非法规格在领域写入前抛出 `ArgumentOutOfRangeException`。
- 数据库唯一索引、复合外键、Check Constraint 和租户过滤器提供第二道边界。
- Down Migration 删除 `BeamHeight` 并恢复旧约束；执行前必须确认下游不再依赖非零横梁高度，避免字段信息丢失。

## 5. 验证

| 检查 | 结果 |
|---|---|
| `CP6.Space.UnitTests` | 191 passed，0 failed，0 skipped |
| 默认 `CP6.Space.IntegrationTests` | 46 passed，38 SQL-gated skipped |
| E05-S02 聚焦 SQL | 1/1 passed，0 skipped |
| 受影响的 Version Clone SQL | 6/6 passed，0 skipped |
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors，7 existing warnings |
| EF pending model | 无待提交模型变更 |
| 格式 | Domain、UnitTests 及本卡修改的 Infrastructure/Integration 文件通过 |
| 范围污染 | 未新增 S03 场景 DTO、S04 资产库、Location 生成、HTTP、WMS 或渲染 |

## 6. 下一步

E05-S03 的两个依赖 E05-S01/S02 已完成，现成为主链下一张无阻塞卡。它必须只统一 Design Revision 场景读取契约和旧 Published 运行态物化边界，不提前实现 E05-S04 资产库或 E05-S05 参数化渲染。
