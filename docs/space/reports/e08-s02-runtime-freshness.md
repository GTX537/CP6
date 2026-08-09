# E08-S02 库存来源、时间与延迟展示交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e08-s02-freshness`
- 功能提交：`9a478c7a8727b7fb9097541ab576da5c6dcc7e61`
- no-ff 集成提交：`d4cd8a820f9c2f0bf2670dd3228555bee938fcb4`
- 起始基线：`bbe77f3e5f964495d238f237497cf3567958ddee`

## 1. 交付结果

E08-S02 已让 3D Viewer 的库存快照具备完整、显式的可信度信息。用户现在可以直接判断：

1. 库存来源是 `Real`、`Simulated` 还是 `Unavailable`；
2. 来源系统 `DataSourceId` 与运行连接 `AdapterId` 分别是什么；
3. 数据观察时间与 CP6 系统接收时间；
4. 快照延迟与来源时钟超前量；
5. 本次 Viewer 会话的最近成功时间；
6. 最近一次刷新失败是当前仍失败还是已经恢复，以及安全错误码和失败时间。

Viewer 库存覆盖层不再读取旧的 `/api/space/floor/{floorId}/stock` 快照，而是按当前楼层已渲染的 Space 逻辑库位 ID 调用 E08-S01 统一运行源：

`GET /api/space/design/v1/sites/{siteId}/runtime/inventory`

物料、批次、容器行按 `LocationLogicalId` 聚合为库位级覆盖层。Space 逻辑身份是渲染权威；WMS 编码不一致时不会被静默当成 Space 编码使用。

## 2. 后端与契约

`SpaceWmsRuntimeSourceDto` 新增并冻结为必填字段：

- `AdapterId`
- `ReceivedAtUtc`
- `DelayMilliseconds`
- `ClockSkewMilliseconds`

`ObservedAtUtc` 继续使用多分块查询中的最早观察时间。完整结果排序和组装后，服务使用 UTC 系统时钟记录一次 `ReceivedAtUtc`；延迟与时钟超前互斥且都不为负数。运行连接 ID 同样执行非空和长度合同校验。

Design V1 OpenAPI、生成 C# SDK 和 TypeScript SDK 已同步再生成，required/non-null 保证由运行态 schema 冻结表保护。生成器 `-Check` 无 drift，C# SDK Release 构建为 0 warning / 0 error。

## 3. Viewer 行为

- `SpaceViewer` 只读公开当前楼层的逻辑库位 ID/编码列表。
- API 使用重复 `locationLogicalId` 参数，客户端先去重。
- 可用来源中没有库存行的已请求库位明确显示为空库位。
- 统一源目前没有容量、锁定和拣货流程状态，因此 UI 只显示“空/有库存”，利用率模式明确标为“占用估算”；不会伪造满、锁定或在拣状态。
- 空楼层不会意外退化为无边界全站点查询，而是显式显示 `Unavailable / EMPTY_FLOOR_SCOPE`。
- 楼层切换会使旧的在途刷新失效；慢到达的旧楼层响应不能覆盖新楼层。
- 刷新失败保留最后一份成功快照及其来源/时间，同时把会话失败状态标为 active；后续成功后标为 recovered 并保留最近失败证据。

会话历史不写数据库，也不声称是跨实例或跨浏览器的持久健康历史。这一边界在设计中明确记录。

## 4. 验证证据

### 功能分支门禁

| 门禁 | 结果 |
|---|---|
| Runtime service 聚焦集成测试 | 40 passed / 0 failed |
| Space Unit Release 全量 | 220 passed / 0 failed / 0 skipped |
| Space Integration Release 默认全集 | 96 passed / 0 failed / 48 SQL 环境门禁 skipped |
| Design V1 OpenAPI/SDK 聚焦 | 18 passed / 0 failed |
| 前端 E08-S02 聚焦 | 5 files / 20 tests passed |
| 前端全量 | 102 files / 593 tests passed |
| 前端类型检查 | passed |
| 前端生产构建 | passed；仅既有大 chunk 提示 |
| C# SDK Release build | 0 warning / 0 error |
| OpenAPI/C#/TypeScript SDK drift | `-Check` exit 0 |
| 暂存差异 whitespace | `git diff --cached --check` exit 0 |

### 合并态聚焦门禁

| 门禁 | 结果 |
|---|---|
| Runtime service | 40 passed / 0 failed |
| Design V1 OpenAPI/SDK | 18 passed / 0 failed |
| 前端 E08-S02 | 5 files / 20 tests passed |
| 前端类型检查 | passed |

.NET 构建显示的 warning 均来自本卡未修改的既有文件：`FlowAdminService.cs`、`InboundService.cs`、`SpaceRetryLeaseMigrationTests.cs`、`PendingCookieTests.cs`、`BudgetVsActualTests.cs` 和 `InboxServiceTests.cs`。

验证期间 D 盘两次被新工作树中的可再生成 `bin/obj/dist` 填满。失败发生在测试执行前的文件复制阶段，不是代码或断言失败；只清理了 E08-S02 功能工作树及受控集成工作树中明确验证过的构建产物，随后原命令复验通过。主工作区、源码和用户文件均未删除。

## 5. 范围与后续

本卡没有数据库迁移，没有修改旧库存接口，也没有迁移物料反查或任务路径。下一张建议卡为 E08-S03：基于统一运行源完成物料、批次、容器定位验收，并解释多结果、空结果和跨层结果。
