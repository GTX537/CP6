# E08-S03 物料、批次与容器定位验收交付报告

- 状态：已完成并进入 Space 受控集成分支
- 功能分支：`codex/space-e08-s03-locate`
- 功能提交：`8d8f7e011cd4ce132ec930c23b4caca38cbf34ef`
- no-ff 集成提交：`dfb6e93ba79855ce6da63f5d42a6a5f2cd700c12`
- 起始基线：`faeacd4bba51ad8d99729a3c6b71ada06f70f7e0`

## 1. 交付结果

E08-S03 已把 3D Viewer 的物料定位从旧
`/api/space/stock/locate` 迁移到 E08-S01 统一 WMS 运行源。新增端点：

`GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`

端点支持精确的 `materialNumber`、`lotNumber`、`containerNumber`。至少需要
一个条件；同时提供多个条件时固定按 AND 匹配。查询范围是当前 Published
版本中的全部 Active Space 库位，并继续使用 E07-S05 采纳后的 WMS 逻辑身份、
500 个位置分块和 10,000 位置上限。

响应以 Space 逻辑位置为稳定身份，每个库位只返回一条定位候选，并包含：

- Space/WMS 双 LogicalId、双编码及编码一致性；
- 楼层 LogicalId、编码、名称和层号；
- 物理量、分配量；
- 匹配的物料、批次和容器事实；
- 命中库位数与楼层数；
- E08-S02 的来源、观察/接收时间、延迟和时钟超前元数据。

可用来源且零命中是权威空结果；`Unavailable` 来源仍是独立状态，前端不会把
它解释为“确实没有库存”。

## 2. 适配器与失败关闭

- `SpaceWmsInventoryQuery` 新增可选定位条件，筛选发生在 WMS 边界，而不是把
  全站库存下载到浏览器后筛选。
- CP6 真实适配器对物料/批次查询正库存 Stock 行；容器查询读取未发运、数量
  为正的 Pallet，并可继续叠加物料与批次 AND 条件。
- 标准模拟器执行相同的精确 AND 和正库存语义。
- 服务端重新验证适配器返回的每条事实确实满足条件；不满足条件、非正库存，
  或同一 WMS 逻辑身份返回多个位置编码时，以
  `SPACE_WMS_RUNTIME_CONTRACT_VIOLATION` 失败关闭为 502。
- 本卡纯读，无数据库迁移，不把库存、批次或容器事实复制进 Design Revision。

## 3. Viewer 行为

搜索框现支持按编码、物料、批次和容器四种模式。库存定位不再自动跳到第一条：

1. 多结果显示命中库位数和楼层数，并按楼层分组；
2. 每条候选显示 Space 库位编码、数量及匹配事实；
3. Space/WMS 编码不一致时明确警告，但导航仍使用 Space 权威编码；
4. 用户点击具体候选后才复用 06 的 Locator，完成异步跨层加载、飞行和脉冲高亮；
5. 空结果与来源不可用分别显示；
6. 较旧的并发搜索响应不能覆盖较新的查询。

## 4. 验证证据

### 功能分支门禁

| 门禁 | 结果 |
|---|---|
| Runtime/adapter 聚焦 | Unit 23 passed；Integration 63 passed；最终 runtime service 44 passed |
| Space Unit Release 全量 | 220 passed / 0 failed / 0 skipped |
| Space Integration Release 默认全集 | 101 passed / 0 failed / 48 SQL 环境门禁 skipped |
| Design V1 OpenAPI/SDK 聚焦 | 18 passed / 0 failed |
| 前端 E08-S03 聚焦 | 2 files / 5 tests passed |
| 前端全量 | 103 files / 597 tests passed |
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
| Runtime service | 44 passed / 0 failed |
| Design V1 OpenAPI/SDK | 18 passed / 0 failed |
| 前端 E08-S03 | 2 files / 5 tests passed |
| 前端类型检查 | passed |

.NET 输出中的 warning 均来自本卡未修改的既有 OA/WMS/测试文件。

验证期间 D 盘空间不足。只清理了 E08-S02、E08-S03 功能工作树和受控集成
工作树内明确验证过、可重新生成的 `bin/obj/dist/TestResults`；源码、提交、
Git LFS 备份、用户主工作区和其他用户文件均未删除。E08-S03 前端工作树通过
NTFS Junction 只读复用受控集成工作树现有 `node_modules`，没有重复安装依赖。

## 5. 范围与后续

本卡没有修改旧 `/api/space/stock/locate` 的兼容端点，也没有混入拣货任务、
路径、优化顺序或工作量。下一张建议卡为 E08-S04：基于统一运行源完成拣货
任务与路径验收，解释实际顺序、优化顺序、跨区/跨层与工作量。
