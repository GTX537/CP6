# E02-S04 CAD 图层与块清单开发切片

日期：2026-08-03

## 交付结论

CP6 已在 E02-S03 集成基线 `01a59696` 上完成 E02-S04 开发切片，功能提交为 `b77faf96`。已确认坐标的 CAD IR 现在可以生成确定性、可分页查询的图层/块清单；开发 DXF 转换器也从仅列出“有对象的图层”提升为保留完整 `TABLES/LAYER` 元数据和空图层。

这不是正式 E02-S04 验收。实现继续使用 20 份纯合成 DXF，不声明已具备授权原生 DWG、生产持久化、租户 API/权限或正式黄金集能力。

## 本次实现

1. `SpaceCadIrLayerV1` 向后兼容地增加颜色、线型和可见性；旧 v1 JSON 缺少新字段时仍默认可见。
2. 开发 DXF 转换器读取声明图层的名称、ACI/RGB 颜色、线型、开关状态和对象数；零对象图层不再丢失。实体引用未声明图层时合成清单记录并产生 `SPACE_CAD_LAYER_METADATA_MISSING` Warning。
3. `SpaceCadInventoryV1` 绑定来源 SHA-256、E02-S03 坐标 Transform SHA-256、目标 Floor 和 Inventory SHA-256，包含：
   - 图层对象总数、支持/不支持数、图元类型数、块引用数、属性对象数与范围；
   - 块定义、XRef 状态、定义对象数、引用数、属性名/覆盖引用数/不同值数与引用范围；
   - 每个块引用的稳定 SourceRef、图层、受控属性和值及范围；
   - 空图层、未定义块、带属性块引用和总对象汇总。
4. 清单只接受 `ReadyForParsing=true` 且无 Blocking 的坐标准备结果；来源、楼层、范围或坐标元数据被篡改时失败关闭。记录和属性规范排序，同一输入产生同一清单哈希。
5. 图层、块、块引用查询支持名称/ID、显隐、图元类型、XRef、图层、块名和属性键值过滤；Offset/Limit 分页最多 200 条，拒绝无上限查询和仅给属性值的歧义查询。
6. CAD 实验工具新增：
   - `build-dev-inventory`：从坐标准备产物构建清单；
   - `query-dev-inventory`：查询 layer、block 或 reference，并可输出 JSON 证据。
7. 新增 `inventory.schema.json`；无数据库 Migration、WebApi、Draft 写入或供应商 SDK 依赖。

## 样例 13 连续证据

输入：`docs/space/acceptance/development-v2.0.0/seeds/13-automated-warehouse.dxf`

- Source SHA-256：`aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；
- 含完整图层元数据的 CAD IR SHA-256：`b6aa6501ea67e9e3b9622c8838eb28b4e3569ca79f777ff77fe44987e0614310`；
- Coordinate Transform SHA-256：`b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`；
- Inventory SHA-256：`634329583747825b5c40c37402e03cdfa046c6f3e54f3d0ae2a4eb8faa9697a9`；
- Floor：`F01`；范围：`(0,-1200)～(36000,24000)` mm；
- 15 个声明图层，其中 7 个空图层；1 个已定义块；8 个块引用且全部带属性；22 个对象，22 supported / 0 unsupported；
- `layer=RACK, attribute=RACK_ID, value=R-01-01` 查询精确返回 `sourceRef=H:110`、块 `RACK_UNIT`、位置 `(3000,3500)` mm。

E02-S02 样例 13 的旧 CAD IR 哈希对应当时只输出 8 个有对象图层的历史合同。当前哈希变化来自 E02-S04 对 15 个声明图层及其显示元数据的完整保留，不是来源文件变化。

## 门禁

- E02-S04 聚焦：9 个清单行为测试 + 1 个 v1 向后兼容测试全部通过；
- 20/20 合成 DXF 完成转换、坐标确认和清单构建；每份均保留声明图层颜色/线型，包含空图层，无未定义块；
- CAD 实验工具完整测试：22 passed / 0 failed / 0 skipped；
- Space Unit 完整测试：304 passed / 0 failed / 0 skipped；
- 完整 solution Release 非增量构建：0 error / 10 条既有 warning；
- 三份 CAD JSON Schema、样例清单和 `git diff --check` 通过。

## 正式边界与下一步

正式 E02-S04 仍等待：

- E02-S01 授权原生 DWG/DXF 适配器、冻结隔离 Worker 和独立正式黄金集；
- 生产规模 streaming 清单存储、索引和压力证据；
- 清单与 Source/ModelVersion/Floor 的同租户持久化校验；
- WebApi 分页、权限、审计、脱敏和 UI 查询链；
- 真实图纸中的 ByLayer/ByBlock、复杂颜色/线型、匿名/动态块、嵌套块、XRef 和属性场景验收。

等待外部解阻期间，可继续 E02-S05 开发侧图层映射方案：以本清单为输入，冻结 System/Tenant 隔离、匹配优先级、复用键和失败关闭规则；仍不得提前声明正式 CAD 主链完成。
