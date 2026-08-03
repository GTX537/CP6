# E02-S03 CAD 坐标确认开发切片

日期：2026-08-03
范围：单位分析与明确确认、源/目标原点、旋转、楼层归属和解析门禁；不代表正式 CAD 链验收

## 交付结论

CP6 已在 E02-S02 开发 CAD IR 基线 `97d6871f` 上完成 E02-S03 开发切片，功能提交为 `09b26b87`，证据提交为 `d78b3b09`，并通过 no-ff 提交 `7741da61` 集成到 `integration/space-v1-20260730`。20 份合成 DXF 现在都能连续完成：DXF 转换 → 单位/范围分析 → 用户确认 → 楼层归属 → `LOCAL_MM_Z_UP` 整数毫米结果。

当前基线中的 E03-S01～S03 与 E13-S16 已经完成，因此没有重复生成 Excel 模板或重做字段映射。本切片直接推进仍未完成的 CAD 主链，同时继续保留 E02-S01 正式供应商和黄金集门禁。

## 分析与确认合同

- 分析结果分别展示原始 CAD 单位范围和建议毫米范围，不把已经换算的毫米边界伪装成源单位边界。
- 已识别单位只作为建议；无论建议是否合理，进入解析前仍要求 `unitConfirmed=true` 且确认记录必须绑定精确来源 SHA-256。
- 未知单位不猜测比例，产生 `SPACE_CAD_UNIT_UNKNOWN` Blocking；确认后才按毫米、厘米、米、英寸或英尺的固定比例处理。
- 默认图纸单边合理范围为 1 m～5 km；缺少边界或超出范围产生 Blocking，阈值通过版本化 limits 合同传入。
- `coordinate-confirmation.schema.json` 冻结源原点、目标楼层原点、逆时针 Z 旋转、Floor LogicalId/Code/Level/Elevation、`LOCAL_MM_Z_UP` 和楼层边界输入。

## 确定性坐标变换

变换先从转换器已应用的检测比例还原源坐标，再应用用户确认比例，因而可以纠正错误或不可信的 CAD 单位头。随后执行：

```text
deltaMm = (sourceCoordinate - sourceOrigin) * confirmedScaleToMillimeters
floorXY = floorOriginXY + RotateCCW(deltaMmXY)
floorZ  = floorOriginZ  + deltaMmZ
```

- 所有输出点、半径、边界和偏移量按 AwayFromZero 规则量化为整数毫米；
- 正角从 `+X` 朝 `+Y` 逆时针，角度规范化到 `[0, 360)`；
- 普通图元输出已规范化点并保持 Identity transform，避免下游重复应用全局变换；
- BlockReference 将来源块变换与全局楼层变换复合，保留可重复实例位置；
- 准备结果超出目标楼层边界 50 mm 容差时产生 `SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED` Blocking；
- 来源、检测/确认单位、原点、旋转、楼层、边界和仿射矩阵共同形成小写 SHA-256 `TransformSha256`，相同输入得到相同结果。

## 来源解析门禁

`SpaceModelSource` 现在对 DWG/DXF 失败关闭：

- `ConfigureImport` 必须收到与来源哈希一致的 schema v1 坐标元数据、已确认单位/比例、目标 Floor LogicalId、`LOCAL_MM_Z_UP` 和规范变换哈希；
- `BeginParsing` 在单位、比例、变换或楼层确认缺失时拒绝进入 Parsing；
- Excel、底图、编辑器和模板来源的既有配置路径不受 CAD 专用门禁影响；
- 本切片复用已有 `Unit`、`ScaleToMillimeters`、`TransformJson` 字段，不新增数据库迁移。

## 开发命令与样例

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  prepare-dev-coordinate `
  --input tmp\e02-s03\13.cad-ir.json `
  --confirmation docs\space\contracts\cad\v1\examples\development-coordinate-confirmation.json `
  --output tmp\e02-s03\13.prepared.json
```

样例 13 的来源 SHA-256 为 `aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`；确认后归属 `F01`，范围为 `(0,-1200)～(36000,24000)` mm，22 个图元、0 个问题，`TransformSha256=b1223a8f2406ac28023d35d300adb6b729403cd1bdb85b12eaba58ae2353cfba`，状态为 ReadyForParsing。

## 验证证据

- E02-S03 坐标聚焦测试：13 passed / 0 failed / 0 skipped；
- CAD 实验工具：20 passed / 0 failed / 0 skipped；
- 20/20 合成 DXF 的检测单位与范围合理，全部经明确确认后完成楼层准备且无 Blocking；
- Space UnitTests：294 passed / 0 failed / 0 skipped；
- 完整 solution Release：0 error / 10 条既有 warning；允许读取本机已安装 Windows/Android SDK 后最终增量复验为 0 warning / 0 error；
- JSON Schema 与确认样例可解析，CLI 端到端输出通过，`git diff --check` 通过。

## 尚未解除的正式门禁

本交付不把 E02-S03 标记为正式完成，也不提前启用生产 CAD Parse、外部 AI Provider 或 Draft Apply。仍需：

- E02-S01 的授权原生 DWG/DXF 适配器、冻结隔离 Worker 和正式黄金集；
- 正式 E02-S02 streaming adapter 与本 CAD IR/坐标合同的一致性证据；
- 真实多楼层/错误单位/大坐标/旋转/XRef 样本和正式验收阈值；
- 目标 Floor 与来源同租户、同 ModelVersion 的持久化服务校验及 API/权限/审计链；
- E02-S04～S07 正式图层、块、映射、语义和来源置信度链。

在等待外部解阻包期间，下一张可继续做 E02-S04 的开发侧图层/块清单，复用现有 20 份合成 CAD IR；其结果继续明确标记为非正式验收。
