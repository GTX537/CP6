# E13 无锁 Zone 父关系确定性推导

日期：2026-08-09

功能提交：`d19a5300`

实现基线：`main@6bbdd760`

## 交付范围

- 新建 Generation Run 使用 `warehouse-rule-only-v2`。当 Aisle 或 Rack 没有人工锁定的 `relations.zoneSourceKey` 时，只从同一权威 CAD Semantic Preview 中的确定性 Zone Polygon 推导父关系。
- 只有一个 Zone 被证明完整包含子对象几何时，才写入 `relations.zoneSourceKey`；字段来源固定为 `DeterministicRule`，证据码为 `RULE:ZONE_GEOMETRY_CONTAINMENT_V1`。
- 候选为零或多于一个时不猜测，分别产生 Blocking `SPACE_RULE_ONLY_PARENT_REQUIRED`，细节为 `no-containing-zone` 或 `ambiguous-containing-zones`。
- 人工锁定继续高于规则；与确定性父关系冲突的 AI Relation 不进入最终关系，并产生既有融合冲突问题。已解析父关系也进入父关系环检测。
- BuildScene 持久化复用融合层问题，避免对同一缺失父关系重复落库。

## 几何与版本边界

- 先以毫米 Bounds 做必要条件过滤，再验证实际 Polygon：Point/BlockInstance 验证所有点；Path/Polygon 验证每一段在凹多边形内；Circle 验证圆心及到所有边的最短距离。
- 边界视为包含；相邻 Zone 共享边界而同时命中时会形成多候选并失败关闭。Arc 或不能证明包含的几何不推断。
- `warehouse-rule-only-v1` 的冻结 Run 和从它恢复的新 Run 继续沿用 v1，不新增父关系，也不改变旧 ProposalSet 的可重放语义。
- 本切片不做不同 SourceHash 的几何匹配、建议继承或自动 Apply；跨 SourceHash 仍必须通过独立产品卡和人工确认闭环。

## 验证证据

- 融合聚焦：16/16，通过唯一包含、重复包含、凹多边形路径穿出、AI 冲突和 v1 回放场景。
- BuildScene 聚焦：3/3；无包含 Zone 的 Rack 只持久化一条 `SPACE_RULE_ONLY_PARENT_REQUIRED`。
- Space Unit：492/492。
- 默认 Space Integration：288 passed / 95 SQL 环境门禁 skipped。
- `dotnet build CP6.slnx -c Release`：0 warning / 0 error，包含 Desktop 与 Android AOT。
- `git diff --check`：通过。

## 未改变的生产边界

- 无 Migration、数据库模型、HTTP、OpenAPI、SDK、前端、Provider、网络、Secret、Usage、High Accept 或 Draft 自动写入变化。
- 外部 Provider、正式 CAD/黄金集、S14/S15/S18/S19 签收、R2 标签和生产部署仍由各自门禁控制。
