# E08-S04 拣货任务与路径验收交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e08-s04-task-path`
- 功能提交：`9f7e38f8af4c5ee00789fe0e91050a725ffe534f`
- no-ff 集成提交：`994339a639d529ed3d8941f6b052d64c204afcd0`
- 起始基线：`944e465f41094688a5b8d0ea2039b104d0d66a5f`

## 1. 交付结果

E08-S04 已把 3D Viewer 的拣货任务路径从旧楼层级任务查询迁移到 E08-S01
统一 WMS 运行源。新增只读端点：

`GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`

`taskId` 是必填查询参数，而不是 URL 路径段，因此包含 `/` 等业务字符的真实
WMS 任务号不会被路由器或网关截断。服务端统一执行 trim + 大写规范化，并把
任务号下推到 `SpaceWmsTaskQuery.TaskIds`；适配器若返回筛选范围外任务，按运行
源合同违例失败关闭。

响应继续沿用当前 Published/Active Space 范围、E07-S05 采纳后的 WMS 身份、
500 个位置分块、10,000 位置上限及 E08-S02 来源新鲜度，并提供：

- WMS 实际停靠顺序以及 Space/WMS 双 LogicalId、双编码；
- 楼层、库区、货架与位置坐标；
- 停靠点、可定位点、楼层、库区和实际跨层/跨区切换次数；
- 总数量工作量以及按楼层/库区分组的停靠数和数量；
- 当前 Published 版本的涉及楼层与巷道中心线拓扑。

可用来源且零停靠是权威空结果；`Unavailable` 来源仍单独展示。实际序号重复会
使路径语义不确定，因此以 `SPACE_WMS_RUNTIME_CONTRACT_VIOLATION` 失败关闭。

## 2. Viewer 验收行为

Viewer 现只从新统一端点读取任务事实，不再用旧
`/api/space/floor/{floorId}/pick-path` 作为任务来源：

1. 面板同时展示任务类型/状态、来源延迟、WMS 实际顺序、仅演示的优化顺序；
2. 显示同层/跨层、同区/跨区及实际切换次数；
3. 按楼层/库区显示停靠点数和数量工作量；
4. 单层任务复用现有巷道距离算法，跨层任务复用现有时间优化算法；优化结果
   明确标注“仅演示，不回写 WMS”；
5. 当前 Design Revision 没有连接体拓扑，跨层段明确降级为近似直连并提示，
   不把近似结果伪装成精确路线；
6. 任一权威停靠点缺坐标时，不对残缺任务做优化，但仍显示实际顺序和工作量；
7. 点击实际停靠点复用 Locator 完成异步切层、飞行和高亮，并在切层后恢复任务
   验收覆盖层；
8. Space/WMS 编码不一致会在停靠点明确警告，导航仍使用 Space 权威编码。

## 3. 契约与安全边界

- 本卡纯读，不下发任务、不重排 WMS 任务、不复制运行任务到 Design Revision，
  也没有数据库迁移。
- 优化顺序只存在于浏览器内的 what-if 展示，未增加任何写回端点。
- OpenAPI 将 `taskId` 冻结为必填查询参数；C# 与 TypeScript SDK 已重新生成。
- 旧高级可视化端点继续保留兼容，但 Viewer 的 E08-S04 路径已不消费其任务源。
- 跨层连接体仍是后续建模范围；当前结果通过降级标志和警告保持可解释。

## 4. 验证证据

### 功能分支门禁

| 门禁 | 结果 |
|---|---|
| Runtime 合同/服务聚焦 | Unit 2 passed；Runtime service 47 passed |
| Space Unit Release 全量 | 220 passed / 0 failed / 0 skipped |
| Space Integration Release 默认全集 | 105 passed / 0 failed / 48 SQL 环境门禁 skipped |
| Design V1 OpenAPI/SDK 聚焦 | 18 passed / 0 failed |
| 前端 E08-S04 聚焦 | 3 files / 9 tests passed |
| 前端全量 | 105 files / 603 tests passed |
| 前端类型检查 | passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| WebApi Release build | 0 warning / 0 error |
| C# SDK Release build | 0 warning / 0 error |
| TypeScript SDK | strict no-emit compile passed |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| 暂存差异 whitespace | `git diff --cached --check` exit 0 |

### 合并态门禁

| 门禁 | 结果 |
|---|---|
| Runtime service | 47 passed / 0 failed |
| Design V1 OpenAPI/SDK | 18 passed / 0 failed |
| 前端 E08-S04 | 3 files / 9 tests passed |
| 前端类型检查 | passed |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |

.NET 输出中的 warning 均来自本卡未修改的既有 OA/WMS/测试文件。

验证期间只清理了 E08-S04 功能工作树中明确验证过、可重新生成的
`bin/obj/dist/TestResults`。源码、提交、依赖目录、用户主工作区和其他用户
文件均未删除。功能工作树通过 NTFS Junction 复用受控集成工作树现有
`node_modules`，没有重复安装前端依赖。

## 5. 范围与后续

E08-S01 至 E08-S04 的统一运行态数据、来源新鲜度、库存定位和任务路径验收已
闭环。下一张建议卡为 E08-S05：建立 10,000 库位性能基线，锁定场景交互、
标签和批量查询门槛。
