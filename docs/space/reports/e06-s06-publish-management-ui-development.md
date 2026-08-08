# E06-S06 版本发布管理 UI 开发报告

日期：2026-08-08  
状态：功能分支门禁通过，待 no-ff 集成  
集成基线：`088648b7e8051e59bcf01a0df607c41a9e257cc2`  
功能分支：`codex/space-e06-s06-publish-management-ui`  
功能提交：`69a8b77a`  
no-ff 集成提交：待集成后回填

## 1. 本卡边界

本切片交付 E06-S06 版本发布管理 UI，把 E06-S01 至 E06-S05 已有的权威校验、发布预览、发布编排、失败恢复、审计和历史版本重新发布能力组成一条可操作、可恢复的发布链。旧的库位发布工具继续保留在 `/space/location-publish`，新的 `/space/publish` 不改变旧流程的数据或路由语义。

本卡只新增一个只读发布活动查询端点，不新增数据库表或 Migration，不修改发布 Saga、WMS 写入、历史证据或 Published 指针规则。外部审批仍由组织现有审批系统负责，页面只收集审批引用并要求操作者明确确认，不伪造新的后端审批状态机。

## 2. 发布管理页面

- 顶部按站点和待发布生产候选版本建立操作范围，同时展示当前线上版本、模型模式和切换状态。
- 四阶段流程依次为“验证、差异预览、审批凭据、发布进度”。验证必须通过才可生成服务端权威预览；预览显示 WMS 影响、阻断项、动作/影响/楼层筛选和分页明细。
- 发布阶段保留审批引用和操作者确认，使用稳定幂等键提交；网络结果不确定时不会自动换键，避免重复创建发布尝试。
- 发布尝试按服务端状态轮询，展示当前步骤、Job 重试次数、下一次重试、批次进度、失败原因、对账问题和追加式审计时间线；允许恢复查看历史活动及在授权时发起人工重试。
- 历史 `Superseded` 版本通过 E06-S05 的重新发布入口创建新的生产候选和新的发布尝试，原版本、原计划、原尝试和原审计链保持只读。
- 页面覆盖空态、加载态、权限不足、409 冲突、422 业务阻断和不确定失败，并提供刷新、重新选择、恢复历史活动或返回修正的明确动作。

## 3. 权限、审计与恢复

- 查询使用 `space:model:read`；启动校验、发布和历史重新发布分别要求 `space:model:validate`、`space:model:publish`、`space:model:rollback`。
- 新增 `GET /api/space/design/v1/sites/{siteId}/publish-attempts`，按站点返回最近发布尝试、目标版本、Job、未解决对账问题及历史重新发布血缘，支持状态筛选、受保护游标和最大 100 条分页。
- 端点继续执行 Tenant、内部主体和 Site 范围检查，使用标准 Problem Details、读审计与精确权限白名单；外部主体和跨租户访问失败关闭。
- OpenAPI 操作数由 110 增至 111；C# 与 TypeScript SDK 已重新生成并通过漂移检查。

## 4. 兼容性与响应式结果

- `/space/publish` 切换为新管理页，原 `SpacePublishView` 和它的测试保留，路由迁移至 `/space/location-publish`。
- 桌面 1440 px 与手机 390 px 的实际浏览器检查均无横向溢出，页面标题、四阶段步骤和主要操作均可见。
- 手机端收敛步骤标题和说明，避免编号重复与拥挤；桌面选择器、活动侧栏和回退入口不遮挡主流程。
- 视觉检查使用完全本地的只读 API Mock，不代表生产 WMS 或外部审批系统验收。

## 5. 验证证据

| 门禁 | 结果 |
|---|---|
| Space Unit 全量 | 462/462 passed |
| CP6.Tests 全量 | 2804 passed / 17 environment-gated skipped / 0 failed |
| 默认 Space Integration 全量 | 263 passed / 94 SQL-gated skipped / 0 failed |
| 前端全量 | 130 files / 698 tests passed |
| 新旧发布页面聚焦回归 | 2 files / 15 tests passed |
| 新发布活动服务聚焦回归 | 2/2 passed |
| 发布活动 Controller/OpenAPI 聚焦回归 | 29/29 passed |
| Web TypeScript 类型检查与生产构建 | passed |
| WebApi Release | 0 error / 3 条既有 Core 可空性 warning |
| C# SDK Release | 0 warning / 0 error |
| OpenAPI/C#/TypeScript SDK drift | passed |
| 桌面/手机实际浏览器布局 | 1440/390 px 均无横向溢出，4/4 步骤可见 |
| `git diff --check` | passed |

全量 API 首轮曾由新增控制器触发控制器计数和只读权限精确清单守卫；补齐为 39 个 Space Controller 并把发布活动查询加入 `space:model:read` 唯一允许清单后，全量 2804 项通过。该失败属于安全守卫发现的接线缺口，不是被跳过或降级处理。

## 6. 尚未完成与下一步

1. 在生产等价 WMS 环境完成真实外部写入、超时、断点恢复、告警、人工对账和运维演练；本地 Mock 与单元/集成测试不能替代该签收。
2. 正式 CAD Provider、组织有权使用的 DWG/DXF 黄金集和精度/覆盖率证据仍未签收；E03-S05 权威 Match Artifact 写入链仍受此约束。
3. Beta/GA 仍需跨职能验收、容量、SLO、灾备、安全和发布证据，本卡不声明 E06、Beta 或 GA 整体完成。

本卡完成的是 E06-S06 开发闭环。完成 no-ff 集成和远端备份后，E06 的本地产品开发主链可视为已具备管理入口，但正式上线签收仍以上述生产证据为准。
