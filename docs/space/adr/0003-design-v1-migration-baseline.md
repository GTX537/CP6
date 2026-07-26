# ADR-0003：Design V1 候选实现整合与 Migration 基线

- 状态：**Accepted**
- 日期：2026-07-25
- 关联：E00、E01、D08、D09、D12

## 1. 已核验事实

当前主工作树：

- 分支：`feat/gr-vp-t6`。
- 核验提交：`1524289fbac6f94b81b69a6fe1ce2f48fceb02dd`。
- 主工作区存在大量 OA/ERP/WF 未提交改动。
- Legacy Space 已存在运行态实体、编辑、发布和 Viewer。

候选工作树 `tmp/worktrees/space-volume1`：

- 与当前核验提交同源，分支为 `codex/space-volume1`。
- `CP6.Space.Contracts/Domain/Application/Infrastructure/Worker` 及三层测试均为未提交文件。
- 已有 Design V1 版本状态机、命令协议、租约、校验、运行态物化和本地发布环。
- `SpaceJobType.BuildScene/Import` 枚举存在，但 Job Runner 不查询也不 Dispatch 这两类任务。
- AI、文件来源和外部 Portal 仍未实现。

## 2. 决策

不整体合并候选工作树。采用“盘点、分层、验证、独立提交、再集成”的方式：

1. 固定主分支提交和候选目录文件哈希。
2. 将候选文件映射到冻结契约和 E00/E01 子任务。
3. 删除 `bin/obj` 等生成物，只保留源文件、Migration 和测试。
4. 按下列提交单元拆分。
5. 每个提交在干净工作树单独构建和测试。
6. 通过后按顺序集成，不暂存或修改主工作区其他模块文件。

## 3. 固定提交单元

| 顺序 | 内容 | 允许文件 |
|---|---|---|
| M1 | Contracts 与错误码 | `CP6.Space.Contracts` |
| M2 | Domain、状态机和修订模型 | `CP6.Space.Domain` |
| M3 | Application 端口与服务 | `CP6.Space.Application` |
| M4 | Infrastructure、DbContext 和 Migration | `CP6.Space.Infrastructure` |
| M5 | Worker 与 Job Ledger 运行 | `CP6.Space.Worker` |
| M6 | Web API、Problem Details 和 DI | Space Design Controller、中间件、Program 的最小差异 |
| M7 | Unit/Integration/Acceptance Tests | 三个 Space 测试项目 |
| M8 | TypeScript/C# SDK 生成骨架 | `sdk` 与契约生成工具 |

任何提交不得包含 OA、WF、PUR、FIN 或无关 WMS 页面改动。

## 4. Migration 基线

- Design V1 使用独立 `SpaceContext` 和独立 Migration 历史。
- 第一条正式 Migration 从受控整合后的模型重新生成并审查。
- 候选工作树中的 Migration 只能作为对照，不能无审查直接成为生产基线。
- E13 的 AI 表必须在 E01 版本/来源/Job 基线之后新增。
- 旧 `Space_*` Published 表的业务语义不由 E01/E13 Migration 修改。
- 每个租户/Site 的切换使用 `LegacyOpen → Frozen → Bootstrapping → Verified → DesignV1`。

## 5. 兼容和开关

- 默认 Site 继续 `Legacy`。
- Design API v1 可以部署但在 Site 切换前拒绝 Design 写操作。
- 切换后 Legacy 写 API 返回 `SPACE_LEGACY_WRITE_DISABLED`。
- Legacy 读 API继续读取 Published 运行态。
- 不建立长期双写；Bootstrap 后通过哈希、数量和抽样几何验证。

## 6. 验证顺序

1. `CP6.Space.Contracts` 和 Domain 单元测试。
2. Application 幂等、租约、Revision 和状态机测试。
3. SQL Server Migration 与租户过滤测试。
4. Worker 抢占、重试、DeadLetter 和恢复测试。
5. Design API Problem Details 与权限测试。
6. Legacy 回归测试。
7. 单 Site Bootstrap、验证、切换和失败回退演练。

InMemory 测试不能替代 SQL Server Migration、唯一索引、并发和事务验证。

## 7. 回退

- DesignV1 激活前失败：保持 Site Frozen，修复后重试；经审批可 `ReopenLegacy`。
- DesignV1 激活后不自动回切 Legacy，使用历史 Published 和修复发布恢复。
- Migration 失败：停止切换，不删除旧运行态数据；回滚应用并使用向前修复 Migration。
- 候选提交失败：只撤销对应提交，不重置或清理主工作区用户改动。

## 8. 完成证据

ADR 执行完成必须保存：

- 候选文件清单及 SHA-256。
- 每个文件到 E00/E01/E13 的归属。
- 正式 Migration ID 和数据库脚本。
- 干净构建、测试和 SQL Server 报告。
- Legacy/DesignV1 切换与失败恢复证据。
- `git status` 证明无关文件未被暂存或修改。
