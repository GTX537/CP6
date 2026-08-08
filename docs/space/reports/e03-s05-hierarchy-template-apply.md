# E03-S05 Excel 层级与货架模板权威 Apply 扩展报告

日期：2026-08-08  
集成基线：`677f8df5`（`integration/space-v1-20260730`）  
功能提交：`cb802cf6`

## 交付结论

E03-S05 的权威 Apply 已从仅写 `Racks` 扩展为在同一个 Serializable 事务中写入 `Racks → RackLevels → Locations`。后台执行器仍重新打开私有 Match Artifact 和原始 Excel、重算规范投影、核验 Draft/Floor 修订，并只提升一次 Floor Revision 与一次 ContentRevision；重复执行继续复用同一 CommandBatch，不产生重复层级对象。

标准工作簿没有 Zone 或 Aisle 工作表，因此本扩展不伪造新格式：`Racks.ZoneCode` 必须唯一解析到目标 Floor 中已有 Zone；Rack 继续保留既有可空 Aisle 关系。正式写入范围与 E03-S01 的实际模板合同保持一致。

## 层级写入规则

- Rack 沿用 E03-S04 Match Artifact 的 New/Update/Unchanged 决策与稳定 LogicalId。
- RackLevel 以 `RackCode + LevelNo` 对齐既有对象；新对象按 CommandBatch 与规范行身份确定性生成 LogicalId。
- Location 以 `LocationCode` 对齐既有对象；跨 Floor 同码失败关闭，新对象使用确定性 LogicalId，并标记 `CodeOrigin=Imported`。
- `CellWidth = Rack.Width / BinCount`、`CellDepth = Rack.Depth / DepthCount`，采用整数毫米向下取整；不足 1 mm 时拒绝写入。Location 的宽、高、深和载荷分别继承目标 RackLevel 的 CellWidth、ClearHeight、CellDepth 与 MaxLoad。
- 映射方案包含 RackLevels/Locations 时，这些工作表是该批次的权威子集；已存在但被省略的活动子对象改为 Disabled。WMS 已绑定 Location 不允许因工作簿省略而禁用。
- 每个 Rack、RackLevel、Location 的创建、更新或禁用均追加稳定 CommandRecord，序号连续；任一引用、模板、维度、绑定或修订失败时整批回滚。

## RackTemplateCode 解析

非空 `RackTemplateCode` 现在解析可见、活动的版本化 Space Asset：

1. 同码 Tenant 资产优先于 System 资产；
2. 所选资产必须唯一；
3. 固定到 VersionNo 最大的 Ready 不可变版本；
4. Apply 后把具体 `SpaceAssetVersion.Id` 写入 Rack.TemplateVersionId。

Excel 中的显式 Rack 尺寸和 RackLevels 行仍是本次导入的几何权威；模板版本是固定渲染/方案血缘，不会在 Apply 时静默改写工作簿数值。

## 继续失败关闭的字段

以下输入仍不会被静默忽略：

- `Bindings`：当前合同缺少 `WmsWarehouseCode → Site/Adapter` 的权威解析，非空行返回 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`；
- `Attributes`：现有 `Space_ElementAttribute` 只归属通用 ElementRevision，无法安全挂到 Rack/RackLevel/Location；
- `Locations.LocationType`：版本化 Location 模型尚无对应持久字段。

这些是下一轮数据模型/适配器合同任务，不属于本扩展的安全写入范围。

## 变更面

- 扩展 `SpaceExcelCadApplyJobStepExecutor` 的工作簿范围校验、模板解析、层级计划、稳定身份、审计和省略禁用逻辑；
- 为 Location 增加导入规格更新入口，保护 WMS 已绑定代码并保留绑定来源；
- 无新数据库表、Migration、HTTP、OpenAPI、SDK 或前端变化；
- `main` 未修改。

## 验证证据

| 检查 | 结果 |
|---|---|
| Location 导入领域聚焦 | 3/3 passed |
| Excel/CAD Match + Apply 聚焦 | 9/9 passed |
| Space Unit 全量 | 467/467 passed |
| 默认 Space Integration | 272 passed / 94 SQL-environment-gated skipped / 0 failed |
| CP6.Tests 全量 | 2811 passed / 17 environment-gated skipped / 0 failed |
| 完整 `CP6.slnx` Release（Desktop/Android AOT） | 0 warning / 0 error |
| 任务文件 whitespace 与 `git diff --check` | passed |

第一次完整 Release 命令因工具 124 秒上限被终止，未产生编译错误；放宽时限后同一命令利用已完成产物在 36.52 秒内以 0 warning / 0 error 完成。

## 剩余边界与下一步

生产 Processing Worker 已能认领 ExcelCadApply，但正式 CAD/Excel 签收仍等待获授权的原生 DWG/DXF Provider、组织黄金集，以及真实大文件、故障和性能证据。下一项可在本地继续的是为 Bindings、Attributes 与 LocationType 建立不含歧义的版本化持久合同；在合同完成前继续失败关闭。
