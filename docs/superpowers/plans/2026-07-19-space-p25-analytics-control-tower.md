# Space P2.5 — 货场分析与控制塔执行计划

日期：2026-07-19
状态：已实现，待部署
分支基线：`main@adbe7bc`

## 目标

在既有 Space 3D 楼层模型、WMS 库存快照和 `WmsHub.StockChanged` 之上，交付一个可日常使用的货场分析层：3D 查看器支持结构、库存状态、利用率、存储类型、ABC 五种互斥模式；新增独立控制塔页面；ABC 结果按租户定时快照并支持手工重算；库存变化在两秒窗口内合并后增量刷新。

## 范围与原则

- Space 只通过集成查询接口读取 WMS 数据，不让 WMS 依赖 Space。
- 复用 `WmsHub.StockChanged`，断线重连后重新订阅并执行一次权威全量刷新。
- 抽取共享 ABC 分类器，消除 Report Center 与 Slotting 中的重复算法。
- 发布库位时把 `capacity`、`capacityUom` 写入 WMS Bin 属性；旧数据仍可回退到 Space 库位容量。
- 五种着色模式必须互斥，切换时恢复基础材质，不能残留颜色。
- 本阶段不包含 SP6 电梯/连接器并发排程，不部署生产。

## 执行顺序

1. 建立隔离分支，固化本计划。
2. 新增分析配置与 ABC 快照模型、共享分类器、租户安全的定时计算 Worker。
3. 新增利用率、存储类型、ABC、库存增量查询和控制塔聚合 API。
4. 扩展 Floor Viewer 五模式、实时刷新、图例与异常提示；新增控制塔路由和页面。
5. 补齐权限、菜单种子、国际化错误码和 EF 迁移。
6. 跑定向测试、完整后端测试、前端类型检查/构建和浏览器回归。
7. 审阅 diff、修复问题并在隔离分支本地提交。

## API 草案

- `GET /api/space/sites/{siteId}/analytics/config`
- `PUT /api/space/sites/{siteId}/analytics/config`
- `POST /api/space/sites/{siteId}/analytics/abc/rebuild`
- `GET /api/space/floors/{floorId}/analytics/utilization`
- `GET /api/space/floors/{floorId}/analytics/storage-types`
- `GET /api/space/floors/{floorId}/analytics/abc`
- `GET /api/space/floors/{floorId}/stock/delta?locationCodes=...`
- `GET /api/space/sites/{siteId}/control-tower`

## 验收门槛

- 五模式可切换且无颜色串扰；利用率按容量单位计算并显示缺失/冲突警告。
- ABC 默认统计 90 天出库数量，A/B 累计阈值默认 80%/95%，每日自动与手工重算均可用。
- `StockChanged` 正常情况下三秒内反映；重连后无陈旧数据。
- 控制塔对空数据、部分配置、API 失败均有可理解的降级状态。
- 未授权访问返回 403；租户数据严格隔离。
- 后端测试、前端类型检查与生产构建通过；关键交互完成浏览器验证。
