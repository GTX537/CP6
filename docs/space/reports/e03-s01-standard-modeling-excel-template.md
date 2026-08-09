# E03 S01 标准建模 Excel 模板完成报告

- 状态：Complete
- 日期：2026-08-02
- 功能提交：`033e8872`
- no-ff 集成提交：`8521a701`
- 起始基线：`72608157`
- 集成分支：`integration/space-v1-20260730`
- Migration：无
- API / OpenAPI / SDK：新增标准模板下载端点并同步 C#、TypeScript 客户端
- 范围：标准建模 Excel 生成、下载入口、稳定字段合同、填写说明与客户端校验

## 1. 交付结果

E03 S01 已完成并进入受控集成基线：

1. 后端按固定 schema `1.0` 动态生成 `cp6-space-standard-model-v1.xlsx`，不读取租户数据，也不依赖数据库状态。
2. 工作簿固定提供 `Instructions`、`Racks`、`RackLevels`、`Locations`、`Bindings`、`Attributes` 六张可见工作表，以及一张 very-hidden `_Lists` 枚举字典。
3. 说明页包含 50 MB 上限、毫米/千克/角度单位、Draft 导入边界、推荐流程、表用途、完整字段字典和 Owner / Batch / Container / Manufacturing 属性映射方法。
4. 业务页提供冻结表头、自动筛选、必填/可选视觉区分、宽度与换行设置，以及整数、数值范围和枚举下拉校验；数据区预留至第 50001 行。
5. 新增 `GET /api/space/design/v1/modeling-templates/excel/standard`，要求 `space:model:read`，返回标准 XLSX、固定文件名、schema 响应头、`private, no-store` 与 `nosniff`。
6. Design V1 楼层编辑器提供“下载标准 Excel”入口，通过已认证 HTTP 客户端获取 Blob，不使用会丢失授权头的直链。

## 2. 冻结字段合同

| Sheet | 字段 |
|---|---|
| `Racks` | `FloorCode`, `ZoneCode`, `RackCode`, `XMm`, `YMm`, `ZMm`, `WidthMm`, `DepthMm`, `HeightMm`, `RotationZDeg`, `RackTemplateCode`, `LifecycleStatus` |
| `RackLevels` | `RackCode`, `LevelNo`, `BottomZMm`, `ClearHeightMm`, `BinCount`, `DepthCount`, `LoadCapacityKg`, `LifecycleStatus` |
| `Locations` | `LocationCode`, `RackCode`, `ColumnNo`, `LevelNo`, `DepthNo`, `LifecycleStatus`, `LocationType` |
| `Bindings` | `WmsWarehouseCode`, `ExternalLocationId`, `LocationCode`, `BindingMode` |
| `Attributes` | `ObjectType`, `BusinessKey`, `Namespace`, `Key`, `Value`, `Unit` |

这些字段将作为 E03 S02 租户表头映射的目标字段，以及 E03 S03 必填、类型、重复键和引用预检的规范输入。模板不包含 `Quantity`、`MaterialNumber`、`LotNumber`、`ContainerNumber`、`BatchQuantity` 或 `TaskId` 等运行态字段。

## 3. 验收映射

| 验收要求 | 机器证据 |
|---|---|
| 包含货架 | `Racks` 字段合同、尺寸/坐标校验和模板编码 |
| 包含逐层规格 | `RackLevels` 支持层号、底高、净高、格口数、纵深数和额定承载 |
| 包含库位 | `Locations` 通过 RackCode、列、层、纵深定位，并提供生命周期与用途枚举 |
| 包含映射说明 | `Instructions` 说明 WMS Binding 与 Attributes 命名空间；`Bindings` 固化外部标识到 LocationCode 的映射 |
| 平台可下载 | 受权限保护的 binary OpenAPI 端点、生成 SDK 和前端 Blob 下载入口 |
| Excel 可直接使用 | Open XML 规范校验为零错误，Microsoft Excel 桌面端无修复打开并完成全工作簿 PDF 渲染 |

## 4. 验证证据

| 门禁 | 结果 |
|---|---|
| 新增模板/注册聚焦测试 | 4/4 passed |
| API、OpenAPI、权限聚焦测试 | 38/38 passed |
| CP6.Tests 全量 | 2722 passed / 17 既有环境门禁 skipped / 0 failed |
| Space IntegrationTests 全量 | 144 passed / 55 SQL 环境门禁 skipped / 0 failed |
| 前端新增 API 测试 | 1/1 passed |
| 前端全量 | 109 files / 613 tests passed |
| 前端 type-check 与 production build | passed；仅保留既有大 chunk 提示 |
| 完整 solution Release 非增量构建 | 0 error / 10 条既有 warning |
| Open XML Validator | 0 error |
| Microsoft Excel 原生验收 | 无修复打开，7 sheets；全可见工作表渲染检查通过 |
| SDK drift | `generate-space-design-sdk.ps1 -Check` passed |
| i18n 快照基线 | 仍为 843 个既有缺口；本卡未增加 `t()` 缺口 |
| 差异门禁 | `git diff --check` passed |

默认 SQL 跳过项只按环境门禁记录，未伪装为通过。本卡不涉及数据库模型或 Migration，模板生成和下载链路已经由纯服务、控制器合同和完整构建覆盖。

## 5. 边界与下一步

本卡只冻结标准模板与下载链路，没有实现来源 Excel 解析、租户自定义表头保存、预检、CAD 匹配或确认导入，也没有把运行时库存写入 Design Draft。

下一张为 E03 S02“Excel 字段映射方案”：以本报告中的字段合同为目标，完成自定义工作表/表头映射、样本预览、租户隔离保存和可复用方案选择；完成后再进入 E03 S03 预检。
