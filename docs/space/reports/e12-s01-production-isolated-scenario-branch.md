# E12-S01 与生产版本隔离的方案分支交付报告

- 状态：功能分支全量门禁、远端备份、no-ff 受控集成、合并态复验、五语补齐与临时资源清理全部完成
- 起始基线：`dca591884f8724c058c6bbea4df675a38ffd099d`
- 隔离模型与迁移提交：`c673b7ec`
- 功能提交：`8d75e79e`
- no-ff 集成提交：`0ac603d4`
- 五语收口提交：`3d41c8d9`
- 原功能分支：`codex/space-e12-s01-planning-scenarios`（历史进入远端受控集成后已删除）
- Migration：`20260802204901_SpaceE12S01PlanningScenarioBranches`

## 1. 交付结果

E12-S01 新增内部规划方案分支。规划人员可以从站点当前生产 `Published` 快照创建多个独立场景，查看异步克隆任务及固定的来源/目标版本血缘；克隆完成后的场景版本仍可复用现有 Draft 编辑与校验能力，但不会占用生产 `ActiveDraftVersionId`，也不会改变 `CurrentPublishedVersionId`。

场景创建使用调用方稳定的 `branchId`、基础 Published 版本、规范化名称和 `space-planning-scenario-v1` 定义生成 SHA-256 请求身份。相同 ID 与相同请求返回 `Duplicate`，同一 ID 改变请求则稳定冲突；分支永久固定基础版本、场景版本、克隆任务和定义版本。若 Worker 启动前基础版本已因正常发布变为 `Superseded`，仍只克隆该固定历史快照，不自动追随新生产版本。

## 2. 生产隔离护栏

- `SpaceModelVersion.Purpose` 明确区分 `Production` 与 `PlanningScenario`；既有版本迁移时固定回填为 `Production`。
- 领域层拒绝把场景版本设为生产 Draft/Published/Bootstrap 指针，并拒绝其进入 Publishing、Published 或 Superseded 生命周期。
- 数据库 `CK_Space_ModelVersion_Purpose` 拒绝场景版本持久化为生产发布状态或携带发布人/发布时间证据。
- 正常生产版本列表排除场景版本；按 ID 读取场景版本时显式返回 `purpose = PlanningScenario`。
- 场景克隆完整校验 Tenant、Site、Model、基础版本、目标版本、分支和 Job 绑定；生产克隆原有指针规则保持不变。

## 3. API、权限与持久化

内部端点为：

- `PUT /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches/{branchId}`
- `GET /api/space/planning/v1/sites/{siteId}/scenario-branches`

写入要求 `space:planning:scenario:create`，读取要求 `space:planning:scenario:read`；外部主体在查询数据前失败关闭。菜单 `/space/planning` 和两项动作由 Space 权限种子按租户幂等授予管理员。

`Space_PlanningScenarioBranch` 保存不可变的租户/站点/模型、固定基础版本、场景版本、克隆任务、名称、定义版本和请求哈希，使用租户复合外键、唯一场景版本/克隆任务索引和不可变检查约束。迁移同时提供增量幂等 SQL 脚本。

Design V1 OpenAPI 从 68 增至 71 个 operation，C# 与 TypeScript 生成客户端已同步场景端点和 DTO。

## 4. 前端与多语言

Space 首页和动态菜单新增规划方案入口。页面支持站点选择、当前生产版本识别、场景创建、固定血缘、场景版本、克隆 Job、隔离状态展示，以及 Queued/Running 状态的自动轮询；创建按钮受 `space:planning:scenario:create` 指令保护。页面始终显示“不占用生产草稿、不能进入生产发布流程”的隔离声明。

20 个 `space.planningScenario.*` 词条已提供简体中文、繁体中文、英语、日语和韩语运行时种子，并有唯一性与五语完整性测试。静态 i18n 门禁仍报告 908 项既有快照欠账，本卡没有增加该欠账；生成快照仍为 4,615 个 key。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 261 passed / 0 failed |
| Space Integration 默认全集 | 235 passed / 0 failed / 63 SQL 环境门禁 skipped |
| CP6.Tests 全量 | 2,763 passed / 0 failed / 17 环境门禁 skipped |
| 前端全量 | 120 files / 664 tests passed |
| 前端严格类型检查与生产构建 | passed；仅既有大 chunk 提示 |
| 完整 solution Release | 0 error / 10 条既有 warning |
| `SpaceContext` 与 `CP6Context` EF pending model | 均无待迁移模型变化 |
| Design V1 SDK drift | passed |
| TypeScript SDK strict no-emit | passed |
| 新增 C# 文件 whitespace 验证 | passed |
| i18n 静态门禁 | 908 项既有欠账；本卡净新增 0 |
| Git 差异检查 | passed |

默认集成测试未连接 SQL Server，因此 63 项 SQL 测试按既有约定跳过，不能记作通过；其中包含本卡“生产后续发布后仍克隆固定历史且不占生产指针”的真实 SQL 用例。部署前仍需在发布 SQL Server 环境执行完整真实 SQL 门禁和迁移/回滚演练。

合并树与功能 tip `8d75e79e` 完全一致。合并态再次通过：场景领域 3/3、服务 3/3、权限/合同/OpenAPI/种子 62/62、前端 2 files / 4 tests、类型检查、EF pending model、SDK drift 与 TypeScript SDK strict no-emit。五语补齐另有聚焦测试 1/1。

## 6. 明确未做与下一步

本卡不采集或脱敏历史任务，不提供回放时钟，不计算距离、拥堵、容量、吞吐或成本仿真，不做方案对比/决策记录、交换格式导出或 DWG 回写。场景不会自动同步后续生产变化，不会自动合并回生产，不会进入发布生命周期，也不修改生产库存、任务、订单或 WCS/PDA 事实。

下一张可独立实施卡为 E12-S02“脱敏历史任务数据集和回放时钟”。E03-S04、E04-S05、E06 与 E13 的 CAD 后续链仍必须等待正式黄金集、授权供应商证据及冻结 Worker，不得由场景分支绕过。

## 7. 远端备份与资源清理

功能 tip `8d75e79e` 先推送远端备份，再以 `--no-ff` 合入 `integration/space-v1-20260730`，集成提交为 `0ac603d4`。确认功能 tip 是远端集成分支祖先且工作树干净后，已删除远端功能分支、本地功能分支和功能工作树；受控集成历史完整保留。本轮移除的功能工作树占用 2,877,403,216 字节（约 2.68 GiB）。共享前端依赖目标已恢复并由集成工作树继续复用，`main` 未被本轮操作修改。
