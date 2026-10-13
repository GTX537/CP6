# S01-06 ATP 稳定标识实现与验证

本任务采用 `ATP-STABLEID-LEXICAL/20261011-R1` 的标量子集。C# `StableId.Parse` 返回不可变值，JSON converter 只接受非空 JSON 字符串并按字符串写出；TS `stableId` 从 `unknown` 检查后返回 branded string。两端均保留原字符串与大小写，接受 1–128 位 ASCII，首位为字母或数字，后续允许字母数字及 `._:/-`，不 trim、截断、转换类型或正规化。

C# 使用 `\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z`；TS 和 Schema 使用 `^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}(?![\s\S])`。16、17、128 位接受，129 位、前导标点、Unicode、空白、尾 LF/CRLF 和错误 token 拒绝。字符串 JSON 的 Unicode 转义可以解码为合法标识；重新序列化表示值，不能代表原始 wire 字节。载体测试复用 S01-02 生产 `StrictJson`，另行断言重复解码键拒绝及原 UTF-8 保留。

局部 [Schema](../../../../eng/contracts/s01/atp-stable-id.schema.json) 只提取源 `$defs/StableId` 的 type/minLength/maxLength/pattern 四个断言；其 ID 为 `urn:cp6:plan-atp:stable-id:20261011-r1`。原完整 ATP wire ID、版本、指针及 composed SHA 写在 `$comment` 中。Schema 测试用 ECMA-262 正则及 Unicode code-point 长度执行这四个断言，并逐项比较固定源定义；未使用通用 JSON Schema 引擎，也不声称完整 ATP wire 验证。

## 固定输入与范围

- 分配时分支从当时 `origin/main@d7a210256783e45291a5e724ea068ed4fc165ecc` 创建，该提交也是固定输入基线；实际开工前在干净树快进到已确认的 Main `8569b669fed56ddc7b53b7bb24666c7fb93d544a`。二者间只有协调文档增量，无源码、输入或依赖漂移。
- 包 `CP6-S01-CONTRACT-INPUTS/1.0.0`，profile `atp-stableid-r1`，全部 22 个分配向量。manifest SHA-256 `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`；vectors SHA-256 `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。
- 写入仅限分配的 PlanAtp C#/TS、`StableIdTests.cs`、局部 Schema 和本任务证据。共享 Wire、csproj、wave2 dispatch 复制配置、共同输入、包/锁、台账及四状态页保持只读；协调对话统一更新任务状态并合并。
- [source-provenance.json](source-provenance.json) 核对 composed-13/14/24/25 的原件、派生件、索引、全部登记 patch bundle 的 Git 字节 SHA。PG 原 DDL 在内存按完整两组 patch 重组，9 个操作的结果与 composed-14 完整 SHA 相等，包含 PLAN-IF-004 及 PLAN-IF-005 的五个恢复/重放锁顺序和绑定替换。

| PG 表 | 单位列 | 可空 | composed 定义 |
|---|---|---|---|
| plan_atp.evaluation | result_uom | 是 | varchar(128) |
| plan_atp.segment | uom | 否 | varchar(128) |
| plan_atp.basis | native_uom | 否 | varchar(128) |
| plan_atp.basis | result_uom | 否 | varchar(128) |
| plan_atp.reservation_ledger | uom | 否 | varchar(128) |

上述表列只读核对，DDL/数据库/迁移均 `NOT_RUN`，采用留给 S01-10。当前 `ISupplyService` 仍是旧 decimal 标量 MRP 接口；本任务没有接入 ATP R11 Owner。

## 实际本地验证

环境为 Windows、.NET SDK 8.0.424 / net8.0、Node 20.19.0、npm 10.8.2。在本任务独立 worktree 按现有锁安装/恢复依赖；未升级，未复用其他 worktree 的可写 bin/obj/node_modules。NuGet locked restore 见 [restore-dotnet.log](restore-dotnet.log)；npm ci 安装 426 个锁定依赖，只有现有 glob 弃用提示。

| 阶段 | C# | TS 与 Schema | 跳过 | 退出码 |
|---|---:|---:|---:|---|
| RED，生产标量仍为未实现桩 | 3 通过 / 57 失败 | 40 通过 / 41 失败 | 0 | 均 1 |
| GREEN，实际实现 | 60 通过 / 0 失败 | 81 通过 / 0 失败 | 0 | 均 0 |

RED 已编译并实际发现全部测试，失败来自缺失的标量行为，不是编译或发现失败。GREEN 同时执行 22 个 C# 固定向量、22 个 TS 固定向量和 22 个 Schema 标量断言，以及边界/错误 token/载体断言。`npm --prefix cp6.web run type-check` 退出 0，完整输出见 [green-type-check.log](green-type-check.log)。

可复现命令（首条使用本机已安装的 SDK 8.0.424 `dotnet`）：

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --no-restore --filter FullyQualifiedName~StableIdTests --logger 'trx;LogFileName=green-csharp.trx' --results-directory docs/development/evidence/S01-06
npm --prefix cp6.web test -- src/contracts/plan-atp/stableId.test.ts --reporter=verbose --reporter=json --outputFile=../docs/development/evidence/S01-06/green-ts.json
npm --prefix cp6.web run type-check
```

[case-matrix.json](case-matrix.json) 从实际 TRX/Vitest 结果提取全部 22 个逐 ID 的三端断言结果；缺失、重复或不支持的分配用例不能被当作通过。[validation-summary.json](validation-summary.json) 记录实际发现数、成功/失败/跳过数；[red-state.json](red-state.json) / [green-state.json](green-state.json) 记录各阶段实际工作树源码、输入、配置、依赖与 DLL 哈希，明确 HEAD 不是未提交运行源码的完整身份。原始 `red/green-csharp.trx`、`red/green-ts.json` 和日志均保留。`summarize-validation.py` 只是汇总既有结果，不冒充重新运行测试；`prepare-inputs.py` 需要已登记的本机只读源归档，运行合同测试不依赖这些归档。

S01-02 的 77/77 和 21 个向量只作为原交付来源，本次没有把它们改称新执行。完整业务 247 场景、静态 41 条 `REFERENCE_ONLY`、原包 `NOT_RUN`、历史接受和旧 30/48 原件不可恢复记录都保持原范围。本次未运行 SQL/数据库、完整业务/E2E/全仓测试、生产候选、签名、发布或部署。
