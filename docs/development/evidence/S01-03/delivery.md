# S01-03 候选交付

交付 FIN-CREDIT 精确金额值库，绑定原生 signedDecimal / nonNegativeDecimal / positiveDecimal。原生字符串在解析时检查词法与符号域；BigInteger 系数和 scale 保留精确值，加减/累加允许任意精度中间量，最终字段输出再次校验 38 位/最多 8 位小数及目标符号域。没有 decimal/浮点中转、舍入、截断或钳位。

## 可消费 API

- `CreditAmount.Parse(text, profile)`：拒绝原生非法词法，不把 `1.0`、`-0`、指数或空白规范化后放行。
- `Coefficient`、`Scale`：规范化后的精确系数和非负 scale；默认值为 `(0,0)`。
- `FromIntermediate(coefficient, scale)`、`Add`、`Subtract`、`Sum`、`CompareTo`：精确中间计算；`FromIntermediate` 明确不构成字段有效性证明。
- `ToFieldString(profile)`：最终字段检查与规范字符串输出。`最大38位整数 + 1 - 1` 可输出；`最大38位整数 + 1` 拒绝输出。解析 signed 后输出到 positive/nonnegative 仍检查实际目标域。
- `CreditAmountJsonConverter(profile)`、`CreditAmountFieldAttribute(profile)`：显式配置字符串 JSON 边界，支持同一 carrier 中不同原生 profile。未配置 profile 的实际序列化/反序列化失败；必填标量拒绝 null 和非字符串 token。required/nullable/optional 由所在合同声明。

```csharp
var delta = CreditAmount.Parse("-0.00000001", CreditAmountProfile.Signed);
var requested = CreditAmount.Parse("1", CreditAmountProfile.Positive);
var projected = requested.Add(delta);
var wire = projected.ToFieldString(CreditAmountProfile.NonNegative);
```

字段绑定与完整 pointer 清单见 [entry-check.json](entry-check.json)；当前冻结来源为 `CP6-S01-CONTRACT-INPUTS/1.0.0`。金额类型不会根据字段名猜 profile。JSON carrier 必须使用现有 `StrictJson.Parse/Deserialize` 预先拒绝重名/转义等价键；重放/摘要原字节从 `StrictJsonDocument.GetOriginalUtf8()` 独立取得，解码后金额字符串不证明原 token 字节相等。

## 实际验证

准确本地命令：

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~CreditAmountTests
```

GREEN：实际 .NET 8.0.29 / net8.0、SDK 10.0.302，205 执行 / 205 通过 / 0 失败 / 0 跳过；25 个固定向量和 180 个补充用例。固定 ID、全部用例结果、源码/输入/依赖/产物 hash 见 [validation.json](validation.json)及 [green metadata](implementation/green-metadata.json)、[完整 TRX](implementation/green.trx)。源码提交绑定在准备发布时写入 `source-receipt.json`；执行时 base_head 只是基础提交，实际未提交文件由原始字节摘要标识。

RED：原始及修正记录器后的实际 TRX 均为 205 执行 / 204 个未实现失败 / 1 个环境检查通过 / 0 跳过。原 PowerShell 错误处理导致初次控制台日志不完整且无 metadata，详见 [真实失败说明](runner-initial-failure.json)；原件保留，未补造 metadata。[red-runtime metadata](implementation/red-runtime-metadata.json)记录修正后的完整日志、实际非零退出和未变源码；两套桩/测试快照均保留。该记录器修复不更改产品代码、工作流或测试失败含义。

S01-02 原 77/77 结果仅按 `docs/development/evidence/wave2-integration-20261012/S01-02/` 的实际来源复用；未重复执行或改称本轮结果。本轮金额 carrier 的严格 JSON/原始字节用例属于上述 205 个实际执行项。未扩大到全仓/TS/数据库测试。

## 发布与范围

本任务的[独立集中审查](review.md)已按规范→质量顺序通过，无实质发现和返工项。当前为任务候选，等待 COORD 审查接收、共享台账/生成页/四状态页同步和正常 PR 串行合并。准确代码提交、最终候选 SHA、PR 和固定 Handoff DELIVERY 由提交回执/交付消息定位；没有把尚未合入远端 main 的候选称为 DONE。

现有 `CreditControlService.CheckCreditAsync(string, decimal)`、`ICreditControlService`、`CreditCheckResult` 及账务舍入保持原合同。真实授信/应收/订单写者、Owner、授权/租户/事务、存储及跨仓包采用仍 NOT_RUN。S01-10 只能在本 API 真实合入后采用 PostgreSQL 存储适配；本任务没有运行 DB、迁移、包发布或部署。中间系数/项数没有应用上限，实际容量受框架整数/集合类型及可用内存约束。

完整 DR-OPS-IF-002 POS/NEG、247 业务运行场景、原包 NOT_RUN、历史接受、41 静态 REFERENCE_ONLY 与旧 30/48 缺失记录保持原范围。未触发/重跑/取消 Actions，未改工作流触发器或保护设置；候选/main 工作流在每次 push/PR 前按实际树核对。
