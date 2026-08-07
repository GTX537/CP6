# Space Version Clone 必填字段前向修复

日期：2026-08-06

范围：`EfSpaceVersionCloneProcessor` 的 Published → Draft 快照复制

功能提交：`0564afad`

no-ff 集成提交：`01eba1b7`

## 结论

Version Clone 已恢复真实 SQL Server 全路径可用。Zone、Aisle、Rack 的必填 `Name` 会随版本快照复制，Rack 的可选 `RackType` 也会保真复制；不需要数据库迁移，不改变 LogicalId、层级关系或已有 RowId 重映射语义。

## 根因与基线证据

- E13-S17 全量真实 SQL 检查最初为 333 passed / 3 failed；其中 Job processor 数量断言随新增 retention processor 更新后通过，剩余两个失败均位于 Version Clone。
- 在 E13-S17 起始提交 `ac9c977c` 的独立 detached worktree 中重复运行 Clone 用例，得到相同失败，确认问题不是 retention Migration 引入。
- 临时诊断暴露的首个 SQL 错误为向 `Space_ZoneRevision`、`Space_AisleRevision`、`Space_RackRevision` 的非空 `Name` 插入 `NULL`；Rack 插入失败后又连锁触发 RackLevel/Location 外键失败。
- 同一手写 Clone SQL 还漏掉可空 `RackType`。它不会触发约束错误，但会在成功克隆时造成静默数据丢失。

## 修复

- Zone `INSERT ... SELECT` 增加 `Name`。
- Aisle `INSERT ... SELECT` 增加 `Name`。
- Rack `INSERT ... SELECT` 增加 `Name` 与 `RackType`。
- SQL 回归种子使用与编码不同的名称，并设置 `RackType=Selective`；断言 RowId 重映射、LogicalId/层级关系、Name 和 RackType 全部保持一致。

## 验证

- 修复前新增回归：1 failed / 1 total，稳定复现 `The clone snapshot could not be completed`。
- 修复后同一回归：1 passed / 1 total。
- `SpaceVersionCloneSqlServerTests`：7/7 passed，0 skipped。
- Space Unit Release：430/430 passed。
- Space Integration + `KOUSQLSERVER` Release：336/336 passed，0 skipped，0 failed。
- IntegrationTests 与 UnitTests Release build：均 0 warning / 0 error。
- `git diff --check`：通过。

## 边界

- 本修复只补齐既有快照 SQL 列映射，没有 Schema 变化，也不需要 Migration。
- E13-S17 报告中对当时基线失败的描述仍是历史事实；本报告记录其后续修复与全量复验。
- 正式 CAD 黄金集、真实 External Provider、影子运行和生产发布证据仍是独立外部门禁，本修复不改变这些状态。
- `main` 未修改。

## 集成与清理

- 远端 `integration/space-v1-20260730` 已推进到证据提交 `08e3fe40`，并验证包含功能提交 `0564afad` 和 no-ff 提交 `01eba1b7`。
- 已合并的 `codex/space-clone-name-forward-fix` 在本地和远端删除；后续需要修改时从受控集成祖先链重新开分支。
- 验证后删除当前 worktree 内 16 个可重建 `bin/obj` 目录，回收 513,840,161 bytes（约 0.479 GiB），剩余目标 0。
