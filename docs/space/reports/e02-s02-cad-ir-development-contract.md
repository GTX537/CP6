# E02-S02 CAD IR 开发契约

日期：2026-08-02
范围：E02-S02 开发侧契约与合成 DXF 适配器；不代表正式 CAD 适配器验收

## 交付结论

CP6 已在合成 CAD 图纸基线 `08fe896a` 上完成第一段可执行的 CAD IR 链路，功能提交为 `89759cec`，验证文档提交为 `8f3e9252`，no-ff 受控集成为 `9e8cf4af`。现在可以把开发用 DXF 转换为版本化、供应商中立且可验证的 CAD IR，不需要在开发电脑安装 AutoCAD。

本交付解决的是“在正式供应商选择前，如何继续开发并冻结下游边界”。它没有解除 E02-S01 的原生 DWG、商业授权、隔离 Worker 和正式黄金集门禁，因此不把 E02-S02 标记为正式完成。

## 契约与边界

- `CP6.Space.Contracts/SpaceCadIrContracts.cs` 定义 CAD IR v1：来源哈希/格式、CAD 版本、单位与毫米换算、坐标系、边界、图层、块、图元、仿射变换、受控属性、问题和汇总。
- `CP6.Space.Application/SpaceCadConversion.cs` 定义 `ICadConverter`、只写 `ISpaceCadIrSink`、转换请求/结果和失败关闭验证器；WebApi、Draft 仓储和供应商 SDK 类型都不进入该边界。
- `docs/space/contracts/cad/v1` 提供 JSON Schema、最小示例和不变量说明。DXF 与未来经批准的 DWG 适配器必须输出同一逻辑契约。
- 每个图元必须有稳定 `sourceRef`；缺失 Handle 时生成确定性引用并记录问题。图层/块 ID、引用、计数、边界、哈希与转换器身份均受验证。
- 不支持的 HATCH、SPLINE、ELLIPSE、DIMENSION 或未知图元保留在 IR 中，设置 `isSupported=false` 并产生显式问题，禁止静默丢弃。

## 开发 DXF 适配器

`DevelopmentDxfCadConverter` 仅面向仓库内合成/开发图纸：

- 只接受 UTF-8/ASCII DXF，单文件上限 25 MiB；原生 DWG 固定拒绝；
- 在解析前核对实际字节 SHA-256 与请求哈希；
- 支持 LINE、POLYLINE/LWPOLYLINE、CIRCLE、ARC、INSERT/ATTRIB、TEXT/MTEXT；
- 识别毫米、厘米、米、英寸和英尺，并归一化为毫米；未知单位不猜测，输出 Blocking 问题；
- XRef 路径只生成 SHA-256 截断令牌，不输出原始本地路径；
- 小型开发 JSON sink 在完成前重新验证整个包，并返回确定性 CAD IR SHA-256。

命令：

```powershell
dotnet run --project tools\CP6.Space.CadExperiment -c Release -- `
  convert-dev-ir `
  --input docs\space\acceptance\development-v2.0.0\seeds\13-automated-warehouse.dxf `
  --output tmp\e02-s02\13-automated-warehouse.cad-ir.json
```

样例 13 的来源 SHA-256 为 `aa573f04e39345b4e03bfce9304f0916a973da47f1ab19b8e17645bc731fb106`，CAD IR SHA-256 为 `f080ac0cf1666a0b9a50c2315346cdb5e8ce7519f9d120e8afaff9fac820a9ba`；输出包含 8 个图层、1 个块、22 个受支持图元和 0 个问题。

## 验证证据

- 20/20 合成 DXF 成功转换并通过完整包契约验证；
- 合计 130 个图层记录、23 个块、292 个图元；其中 278 个受支持，14 个不支持图元全部对应 14 个显式问题，缺失/合成 sourceRef 为 0；
- 同一源文件重复转换得到相同 CAD IR SHA-256；来源哈希不一致固定拒绝；
- 英寸、英尺、厘米、米四种非毫米输入的坐标和比例归一化通过；
- `CP6.Space.CadExperiment.Tests`：19 passed / 0 failed / 0 skipped；
- `CP6.Space.UnitTests`：281 passed / 0 failed / 0 skipped，其中 CAD IR 契约聚焦门禁 9/9；
- `dotnet build CP6.slnx -c Release --no-restore`：0 error / 10 条既有 warning；
- `git diff --check`：通过。

## 尚未解除的正式门禁

- 经法务/采购批准的 ODA、APS 或其他原生 DWG 适配器及生产部署权利；
- 冻结的隔离 Worker、网络/身份/资源限制、秘密管理和供应链证据；
- Calibration 10 / Validation 5 / Release Holdout 5 的独立正式黄金集及原生 DWG 保真证据；
- 生产规模 streaming sink、50 MiB、100 万图元和 200 MiB 上限压力门禁；
- 正式适配器与本契约的一致性测试、评分、选择和审计签字。

下一步应先完成 E02-S01 的正式供应商试验条件；随后把选定适配器接到同一 `ICadConverter`/streaming sink 边界，完成正式 E02-S02，再进入 E02-S03 语义解析链。
