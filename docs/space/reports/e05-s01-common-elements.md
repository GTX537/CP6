# E05-S01 通用空间元素和属性表完成报告

- 状态：**Complete**
- 证据日期：2026-07-30
- 功能提交：`5bb0cdfb`
- 集成提交：`49dbabe3`

## 1. 交付结论

`Space_ElementRevision` 现在对墙、柱、门、月台、托盘、设备及冻结设计中的其他通用元素使用稳定类型集，并在领域写入和更新入口验证版本化 Geometry。`Space_ElementAttribute` 对值类型、单位和运行态命名空间失败关闭。

元素、属性、楼层、父元素和版本关系继续使用 E01-S04 已建立的复合 Tenant 外键、唯一索引和全局查询过滤器。本卡没有新增表、Migration、HTTP、编辑命令、逐层货架、场景 DTO 或资产库。

## 2. 领域约束

### 2.1 ElementType

支持集合：

- 建筑：`Wall`、`Column`、`Door`、`Dock`、`Stair`、`Elevator`；
- 仓储：`Pallet`、`Device`、`Workstation`、`Conveyor`、`StaticEquipment`；
- 辅助/装饰：`Annotation`、`Dimension`、`Guide`、`RestrictedArea`、`Decoration`、`ImportedReference`。

输入按大小写不敏感匹配后保存规范名称；未知类型拒绝。元素不能把自己设为父元素。

### 2.2 Geometry v1

创建和更新共用同一验证器：

- `schemaVersion` 必须是数值 `1`；
- `kind` 只允许 `point/path/polygon/box/asset`；
- 坐标和尺寸使用整数毫米；
- path 至少两个点且 width 为正；
- polygon 外环和洞满足最小点数，height 为正；
- box 三维尺寸为正；
- asset 必须包含非空 `assetVersionId` 和 transform 对象。

未知版本、未知形态、缺字段、非整数坐标和非正尺寸均在写入前拒绝。资产可见性和资产版本附着仍属于 E05-S04，本卡未提前实现。

### 2.3 ElementAttribute

值类型固定为 `String/Integer/Decimal/Boolean/DateTime/Guid/Json`。写入和更新均执行：

- 数字使用 invariant 规范形式；
- Boolean 规范为小写；
- DateTime 规范为 UTC；
- Guid 规范为 `D` 格式且不能为空；
- JSON 必须可解析并规范化；
- Unit 只允许用于 Integer/Decimal。

`owner/lot/container/manufacturer/external-reference` 等设计和外部引用命名空间可用。`inventory`、`stock`、`task`、`runtime` 及其全部点分前缀被拒绝，防止把库存余额、任务状态或实时覆盖复制进设计快照。

## 3. 持久化与租户隔离

`Space_ElementRevision` 与 `Space_ElementAttribute` 已由 `20260726085852_SpaceE01S04PublishedClone` 创建，因为 Published→Draft 完整快照必须从底座阶段复制这些行。既有模型包含：

- `(TenantId, ModelVersionId, LogicalId)` 稳定快照身份；
- Floor、Parent Element、Attribute 与 ModelVersion 的 Tenant+Version 复合外键；
- `(TenantId, ModelVersionId, ElementRevisionId, Namespace, Key)` 活动属性唯一索引；
- Tenant 全局查询过滤器；
- Published/Superseded 快照写保护。

E05 聚焦 SQL 验证六类必需元素和规范化属性真实落库、重复属性键失败、另一租户默认不可见；伪造跨租户版本在发 SQL 前由 `SpaceContext` 失败关闭。EF 检查结果为 `No changes have been made to the model since the last migration.`，因此没有生成空 Migration。

## 4. 权限、错误与回滚

- 权限沿用 `space:model:edit`；本卡没有新增入口或权限种子。
- 跨租户属性抛出 `SpaceTenantScopeException`；未知租户版本抛出 `SpaceVersionStateException`。
- 类型、Geometry、属性值、单位或运行态命名空间无效时在领域写入前拒绝。
- 回滚时停止后续元素写入口即可；保留 E01 完整快照表和数据，不改变 Published。

## 5. 验证

| 检查 | 结果 |
|---|---|
| `CP6.Space.UnitTests` | 180 passed，0 failed，0 skipped |
| 默认 `CP6.Space.IntegrationTests` | 46 passed，37 SQL-gated skipped |
| E05 聚焦 SQL | 1/1 passed，0 skipped |
| 受影响的 Version Clone SQL | 6/6 passed，0 skipped |
| `dotnet build CP6.slnx -c Release --no-restore` | 0 errors，7 existing warnings |
| EF pending model | 无待提交模型变更 |
| 格式 | Domain、UnitTests 全项目通过；修改的两个 Integration 测试文件通过 |
| 范围污染 | 未新增 S02–S05、HTTP、WMS、Provider 或 Migration |

IntegrationTests 全项目格式检查仍会报告既有 `SpaceJobSqlServerTests.cs:299` 空白问题；本卡修改文件的聚焦格式检查通过，未借机修改无关文件。

## 6. 下一步

E05-S02 逐层货架规格只依赖已完成的 E01-S01，现已成为主链下一张无阻塞卡。它必须独立交付 RackLevel 领域约束、Tenant+Version+Rack+Level 唯一性、Migration 和真实 SQL；不得混入 E05-S03 场景 DTO 或 E05-S04 资产库。
