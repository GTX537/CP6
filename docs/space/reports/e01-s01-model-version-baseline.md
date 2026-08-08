# E01-S01 模型版本和状态机交付报告

- 状态：Implemented
- 基线：`3f2912bf`（E00-S01～E00-S04）
- 分支：`codex/space-e01-model-version`
- Migration：`20260726064940_SpaceE01S01ModelVersionBaseline`

## 1. 交付范围

本卡只交付 E01-S01：

- 建立 `CP6.Space.Contracts`、`Domain`、`Application`、`Infrastructure` 分层边界。
- 新增 `Space_Model` 和 `Space_ModelVersion`。
- 实现 Draft、Validating、Ready、Publishing、Published、Superseded、
  ReconciliationRequired 状态机。
- 实现租户隔离、单活动 Draft 指针、单调版本号、Published 不可变和
  SQL Server rowversion 并发。
- 使用独立 `SpaceContext` 和 `__EFMigrationsHistory_Space`。

本卡不包含来源文件、Job Ledger、强类型 Revision、Design API 或旧运行态物化。

## 2. 数据库约束

| 约束 | 实现 |
|---|---|
| 每租户/Site 一个活动模型 | `UX_Space_Model_Tenant_Site_Active` |
| 每模型一个活动 Draft 指针 | `Space_Model.ActiveDraftVersionId` + 领域冲突检查 |
| Draft 指针属于同租户同模型 | 三列复合 FK |
| Published 指针属于同租户同模型 | 三列复合 FK |
| BasedOn 属于同租户同模型 | 三列自引用复合 FK |
| 版本号单调且不重复 | `(TenantId, ModelId, VersionNo)` 唯一索引 |
| 模型和版本并发 | SQL Server `rowversion` |
| 租户读取隔离 | `SpaceContext` 全局查询过滤 |
| 租户写入隔离 | SaveChanges 当前值和原始值双重校验 |

新 Model 与首个 Version 的持久化顺序固定为同一事务内：

1. 插入无版本指针的 Model。
2. 插入属于该 Model 的 Version。
3. 更新 Model 的 Draft 或 Published 指针。

该顺序避免循环 FK，同时保留数据库级租户和模型归属约束。

## 3. 状态机

- 编辑 Draft 或 Ready 会递增 `ContentRevision`，清除全部验证绑定并回到 Draft。
- Ready 必须绑定 `ContentHash + RuleSetVersion + WmsCapabilityHash`。
- Publishing 只有在验证证据完整且未过期时允许进入。
- 外部尚未提交时可以安全回到 Ready。
- 外部可能已提交时进入 ReconciliationRequired，不允许伪装成普通失败。
- Published 只允许转换为 Superseded；领域层和 SaveChanges 层都拒绝修改历史。
- DesignV1 激活后不允许静默 ReopenLegacy。

## 4. Migration 与脚本

- Migration：
  `CP6.Space.Infrastructure/Migrations/20260726064940_SpaceE01S01ModelVersionBaseline.cs`
- 幂等脚本：
  `CP6.Space.Infrastructure/Migrations/Scripts/20260726064940_SpaceE01S01ModelVersionBaseline.sql`
- `Up` 只创建两张 Design 表、索引和约束。
- 不创建、修改或删除 Legacy `Space_Site/Floor/Zone/Aisle/Rack/Location/Marker`。
- 独立 Migration 历史不污染 `CP6Context` 的 `__EFMigrationsHistory`。

生产环境失败处理遵循向前修复原则：停止 Site 激活并追加修复 Migration。
`Down` 仅用于尚无业务数据的本地或临时验收数据库。

## 5. 测试证据

| 测试层 | 结果 | 覆盖 |
|---|---:|---|
| Domain/Application 单元测试 | 11 passed | 合法/非法转换、单 Draft、租户边界、不可变、切换回退 |
| EF 非真库集成测试 | 5 passed | 查询过滤、写入 fail closed、审计盖章、模型元数据 |
| SQL Server LocalDB 集成测试 | 10 passed | Migration、独立历史、唯一索引、复合 FK、rowversion、不可变 |
| CP6 既有全量回归 | 2,528 passed / 1 skipped | E00、Legacy Space 和平台模块无回归 |

SQL Server 测试使用唯一临时数据库，结束后自动删除。
新增六个项目均通过 `dotnet format --verify-no-changes`；全量构建只保留
既有 CP6 项目的 nullable/xUnit analyzer 警告，新增项目为零警告。

## 6. 权限、错误码和偏差

- 本卡没有新增 HTTP 入口，因此没有新增运行时权限或菜单。
- Contracts 已固定：
  `SPACE_VERSION_CONFLICT`、`SPACE_VERSION_STATE_INVALID`、
  `SPACE_TENANT_SCOPE_DENIED`。
- Problem Details 映射留给 E01-S05。
- 与冻结契约无行为偏差。
- E00 配置型兼容 Gate 保持不变；数据库 resolver 替换留给后续集成卡。

## 7. 估算

E01-S01 的 3 工程师日基线不变。本实现没有提前消耗 E01-S02～S06 范围。
