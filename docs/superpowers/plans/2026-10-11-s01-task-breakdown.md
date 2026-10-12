# CP6 S01 精确值与身份采用任务计划

> **For agentic workers:** 执行时使用 executing-plans，逐任务推进。每个任务使用独立分支，以完整任务为一次集中审查和交付单位；本文件复用已批准设计，不重新发明业务合同。

**Goal:** 把 S01 拆为 12 个有输入、范围、依赖和验收证据的短任务，并登记 PostgreSQL 多租户设计前置任务 S02-00。

**Architecture:** 沿用各域原生 profile，在 Main/CRM/Platform 各自消费者中采用严格 wire 边界，保留原字节和版本身份。数值计算与 PostgreSQL 存储分离，FIN-CREDIT、FIN-INV、ERP-CREDIT 不混用规则；先交付 CRM 所需子集，其余按消费者采用。

**Tech Stack:** 现有 .NET 8、System.Text.Json、BigInteger、TypeScript、EF Core/Npgsql/PostgreSQL；不升级依赖，不增加 SQL Server 路径。现有类型资产曾以不同 SDK 独立编译，不能代替实际 .NET 8 项目验证。

## 范围、基线和登记

本次交付任务卡和登记工具，**没有实施以下业务任务**。状态以 [roadmap.json](../../development/roadmap.json) 的 `implementation_tasks` 为准，[任务进度表](../../development/07-detailed-tasks.md)由工具生成，卡片不重复保存可变状态。12 个 S01 任务完成数不能换算成全部 CP6 工作量百分比，也不会自动关闭 125 目标。

Main 基线 `2df45097c8ba18728d30bc85f12cc874921c25e4`；业务源码评估可复用 `5ca90701bfe1e886ea9e179f1e8e5f413bb44f43` 的未变部分。CRM 基线 `c778a3052a4416b82facb07b1244fc98fb6ad8a1`；Platform 基线 `30bd23af6808d217a23878bd9437513043c52834`。每个任务实际开工仍从届时确认的仓库最新 main 创建分支并复核漂移；不能用 Platform 本地候选 HEAD 代替已确认主线。

统一输入为[版本规则](../../design-handbook/remediation/01-version-and-authority.md)、[wire 补充](../../design-handbook/remediation/06-wire-types-and-fixtures.md)、[组合资产 manifest](../../design-handbook/remediation/assets/composed-derivatives.json)、[数值存储规则](../../design-handbook/remediation/assets/core-types/numeric-storage-1.0.0.json)、[修订验收目录](../../design-handbook/remediation/08-acceptance-catalog.json)及[逐目标原验收目录](../../development/assessments/20261011/original-acceptance-catalog.json)。[本切片输入索引](../../development/s01-input-index.json)只筛选来源，不复制或提升历史 PASS。

新文件路径在下面标为“新增”；现有入口标为“核对/修改”。这些计划路径尚未创建。仓库前缀 Main、CRM、Platform 分别指各自任务工作树根；跨仓按独立提交/PR发布，不以 Main 文档代替消费者采用。原归档程序不执行，只读取允许的 Schema、类型和测试数据。

## 依赖和推荐次序

- 最短 CRM 合同路径：S01-01 → S01-02 → S01-08。S01-09 在真实消息接线前完成；S02 真实 Owner 写入还依赖 S02-00 和自己的事务/当前授权准入。
- Main 财务精度：S01-01 → S01-02 → S01-03 / S01-04；S01-05 依赖共用向量，可独立实现 TS，再在 S01-12 比对 C#/TS。
- ATP 与原生身份：S01-06、S01-07；PostgreSQL 精度适配 S01-10 依赖 S01-03/04/06。ERP 贷项 S01-11 单独采用其原合同。
- S01-12 汇总上述精确范围。先完成 CRM 所需子集即可进入其后续任务，不等待整个财务/ATP 子集交付。

本轮后继已获用户授权使用多个对话：每个对话同一时间领取一个任务，以[全量分配规则](../../development/09-parallel-development.md)和最新台账依赖/资源为准；依赖图不表示任务已经启动。S01-01与S02-00可先分别准备，S01-02/08和真实Owner事务按所需已交付子集接续。S/M/L 仅比较范围，不是天数；跨仓采用和存储任务置信度低于纯类型任务。

## S01-01：冻结采用输入和共用向量（Main，S）

**交付：** 消费者能读取的 profile/source/vector 包，不改变金额或状态行为。无前置依赖。

**文件：** 新增 `eng/contracts/s01/profile-inputs.json`、`vectors.json`、`README.md`；核对上述输入索引、组合 manifest 和 `docs/design-handbook/remediation/assets/core-types/numeric-validation.json`。不复制机器绝对路径进新运行配置；原索引中的归档路径是来源定位，不是运行时依赖。

- [ ] 逐 profile 记录 Owner、准确原 SHA-256、补丁集合、组合派生 SHA-256、目标消费者和对应 Schema/类型身份。ATP DDL 同时包含 PLAN-IF-004/005，不能只取长度补丁而丢锁序补丁。
- [ ] 提取原数值 23 向量的输入和预期，另加 strict JSON/null/版本向量；为每条记录源 case ID、适用字段和测试层级。原静态 PASS 不复制为本次执行结果。
- [ ] FIN-CREDIT 至少有 38 位整数、30 位整数加 8 位小数、39 位总精度拒绝、`-0`/`1.0`/`1e3` 拒绝及三个符号 profile；FIN-INV 至少有 90 位整数、18/12 位小数边界、原 `-0.000` 保留。
- [ ] 保存 profile 对应原始验收定义引用；例如 CRM-INQ `AC-INQ-009/048/053` 和修订 generated adoption 场景，仅引用其适用子断言，不把整条业务流程记为通过。
- [ ] 对所选原件和派生文件重新计算 SHA-256；缺少必需输入则该子任务保留明确缺口，其他独立输入可继续，不搜索已声明不可恢复的历史原件。

本地核验（在 Main 工作树根运行；需要本机已有原归档与派生目录）：

```powershell
$s01Inputs = Get-Content -LiteralPath 'docs/development/s01-input-index.json' -Raw | ConvertFrom-Json
foreach ($s01Entry in $s01Inputs.sources) {
    $s01OriginalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $s01Entry.source_path).Hash
    $s01DerivedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $s01Entry.local_derived_path).Hash
    if ($s01OriginalHash -ne $s01Entry.source_sha256 -or $s01DerivedHash -ne $s01Entry.derived_sha256) {
        throw ('S01 input hash mismatch: ' + $s01Entry.source_path)
    }
}
```

预期所有 pin 匹配且无异常；交付 manifest、向量和哈希核对记录，无需编译业务系统。

## S01-02：严格 JSON、NullOnly 与结构相等（Main，M）

**依赖：** S01-01。**文件：** 新增 `CP6.Entity/Contracts/Wire/NullOnly.cs`、`StrictJson.cs`、`JsonSemanticEquality.cs`；新增叶子测试项目 `eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj` 和 `StrictJsonTests.cs`。项目引用 `CP6.Entity`，沿用仓库 net8.0/xUnit/Test SDK 版本；不为测试拉入全部 WebApi/Space。采用资产 `assets/core-types/DesignRevisionTypes.cs` 与 `DesignTypesCheck.cs` 中的准确规则。

- [ ] 先写失败用例：必填字段 `{}` 拒绝、`{"fixedNull":null}` 接受、0/false/字符串/对象拒绝；重复键在任意嵌套层和转义等价键名下拒绝。
- [ ] 实现读取边界，拒绝重复属性后再做 Schema/类型验证；required/null 不能合并为可省略字段。不对原报文重新序列化后计算业务摘要。
- [ ] 采用 BigInteger 数字结构相等：对象键顺序不影响相等、数组顺序影响；`1` 与 `1.0`、`-0.00` 与 `0e1000` 相等；字符串 `"1"` 与数字 1 不相等；38 位整数与科学形式比较无 double。
- [ ] 运行下述测试，先保存失败，再实现至所有明确用例通过。结构相等仅用于 uniqueItems 等 Schema 语义，不用于签名、Owner digest 或命令重放字节规范化。

```powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests
```

预期：实际 net8 项目成功、零失败/跳过；超大输入的资源边界不得静默缩小合法域，若需新增限额应先形成有版本补充。完成只证明边界工具，后续消费者必须接入才能记采用。

## S01-03：FIN-CREDIT 精确金额（Main，M）

**依赖：** S01-02。**文件：** 新增 `CP6.Entity/Contracts/FinCredit/CreditAmount.cs`、`CreditAmountJsonConverter.cs`；测试项目新增 `CreditAmountTests.cs`。核对 `CP6.Core/Services/Fin/CreditControlService.cs` 与现有信用接口，真实额度决策切换属于后续财务采用，不能直接把旧 decimal 参数换类型而漏写者。

- [ ] 绑定 DR-OPS-IF-002 的 FIN-CREDIT 原 Schema，按字段分别选择 signed/nonnegative/positive profile，绝不依据字段名猜测。
- [ ] 测试 38 个 9、负 38 个 9（仅 signed）、30 个 9 加 `.12345678` 接受；31 个 9 加 8 位小数拒绝；最小小数 `0.00000001` 接受；`-0`、`1.0`、指数、前导零拒绝，positive 不接受 0。
- [ ] 使用整数系数加 scale；计算中间值用任意精度，最终落字段才检查域，不经过 .NET decimal/浮点，不舍入、饱和或先截断。
- [ ] 验证 JSON string 往返及 profile 误用拒绝；不创建授信、应收或订单效果。

运行 `dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~CreditAmountTests`，预期全部向量和溢出中间计算用例通过。相关修订 POS/NEG 需等实际消费者和存储采用后才判断整条场景通过。

## S01-04：FIN-INV 数量、金额与分数（Main，M）

**依赖：** S01-02。**文件：** 新增 `CP6.Entity/Contracts/FinInventory/InventoryDecimal.cs`、`InventoryRatio.cs`；新增 `InventoryDecimalTests.cs`。核对 `CP6.Core/Services/Fin/StockFinBridge.cs`，它现有的 round2 桥不能冒充新成本账 Owner。

- [ ] 按 DR-OPS-IF-030 与 CLOSED-WIRE-SCHEMA_R10 分离 Decimal18/Qty12；整数部分无新增有限精度上限。系数 BigInteger、scale 和原 wire 分开存。
- [ ] 测试 90 位整数加 18 位小数、负 90 位整数加 12 位小数；Decimal18 的第 19 位小数拒绝、Qty12 指数形式拒绝；合法 `-0.000` 的数值为零且原 wire 仍可逐字节取回。
- [ ] MAVG 分子/正分母/余数保持任意精度，分母 0 拒绝，符号和整除/非整除用例覆盖。不能套 FIN-CREDIT 的尾零和负零拒绝规则。
- [ ] 验证序列化、比较和中间运算；成本过账、库存计价和旧账迁移属于后续 FIN-INV Owner 任务。

运行 `dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~InventoryDecimalTests`，预期原合法极值无损且非法精度拒绝。

## S01-05：Main TypeScript 精确值（Main，M）

**依赖：** S01-01；与 C# 共享向量，不依赖 C# 实现代码。**文件：** 新增 `cp6.web/src/contracts/exact/creditAmount.ts`、`inventoryDecimal.ts`、`profiles.test.ts`；核对 `cp6.web/package.json` 现有 Vitest/TypeScript 入口。

- [ ] 以同一 profile/field 输入验证字符串，使用 BigInt 运算；表单展示不经 Number、parseFloat 或隐式算术转换。两域保留不同 lexical 规则。
- [ ] 覆盖 JS 安全整数边界两侧、38/90 位极值、负数/尾零、错误 JSON token 类型及最终字段越界；对原 wire 的保留独立断言。
- [ ] 先接合同库和测试，逐页面数值输入迁移在实际消费者任务记录；不全局改 WMS/MES 所有现有输入框。

```powershell
npm --prefix cp6.web test -- src/contracts/exact/profiles.test.ts
```

预期指定文件实际被 Vitest 执行且无跳过；受影响项目再执行现有 type-check。S01-12 核对 C#/TS 同一向量结果，不使用一个实现给另一个动态生成预期。

## S01-06：ATP StableId（Main，M）

**依赖：** S01-02（复用已建立的叶子测试项目）。**文件：** 新增 `CP6.Entity/Contracts/PlanAtp/StableId.cs`、`cp6.web/src/contracts/plan-atp/stableId.ts`、`stableId.test.ts` 和 `StableIdTests.cs`；新增 `eng/contracts/s01/atp-stable-id.schema.json`。核对 `CP6.Core/Services/Plan/ISupplyService.cs`，不把旧 scalar supply 当成 R11 ATP Owner。

- [ ] 采用 `ATP-STABLEID-LEXICAL/20261011-R1`，C# 使用 `\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z`；TS/Schema 使用已接受的绝对结束约束 `^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}(?![\s\S])`。
- [ ] 覆盖 16/17/128 字符接受，129、空串、非 ASCII、前导符号和末尾换行拒绝；不能沿用 `$` 漏过末尾换行，也不能截断到 16。
- [ ] 记录 PostgreSQL 五列扩大到 128 的准确组合 DDL 输入，实际存储适配在 S01-10；不在本任务执行迁移。

运行叶子 `StableIdTests` 与 `npm --prefix cp6.web test -- src/contracts/plan-atp/stableId.test.ts`。预期 C#/TS/Schema 边界一致；原版与有版本后继身份分开，不将旧版说成已符合新规则。

## S01-07：原生 Ref/Scope 与 NCR/IPQC 类型（Main，M）

**依赖：** S01-02。**文件：** 新增 `CP6.Entity/Contracts/Quality/NcrContracts.g.cs`、`cp6.web/src/contracts/quality/ipqc.g.ts`、`ipqc.test.ts`、`QualityContractTests.cs`；核对现有 `QualityInspectionService.cs`、`DefectRecordService.cs` 对应服务目录 `CP6.Core/Services/Mes/`。生成记录引用准确来源和补丁，而非手改生成文件后丢来源。

- [ ] 采用 DR-NCR-TYPE-01 的 5 个原字段及 2 个 ObservationInput 字段 required NullOnly，以及 DR-NCR-FIELD-01 的 5 个准确 nullable 元数据。
- [ ] 采用 DR-OPS-IF-015 原生 Evidence/Uom；Scope/Ref 按模块 Schema 逐个列字段、版本和闭集，不建立一个接受所有对象的万能 Ref。
- [ ] NCR 的缺失、0、对象误入 null-only 拒绝；合法 nullable 值接受；IPQC 混用其他事件类型 payload 或错误单位/版本身份拒绝；严格重复 JSON 键规则保持。
- [ ] 保留真实 QA Owner 源摘要和业务写入接线给后续 QA 任务，单纯 DTO 编译不记 QA 目标 DONE。

运行叶子 `QualityContractTests` 与 `npm --prefix cp6.web test -- src/contracts/quality/ipqc.test.ts`；预期每个原生 Schema 的正反 fixture 均按各自规则判定。

## S01-08：CRM INQ/LEAD 合同采用（CRM，L）

**依赖：** S01-02 的准确边界版本；以来源固定的本仓实现或已发布包采用，不引用 Main 工作树路径。**文件：** 核对/修改 `src/CP6.CRM.Application/Contracts/CrmApi.g.cs`、`src/CP6.CRM.Api/CrmContractInputFilter.cs`、`apps/web/src/leads/types.ts`；新增 `tests/CP6.CRM.UnitTests/Contracts/InquiryContractAdoptionTests.cs`、`apps/web/tests/contracts-exact.test.ts`。核对 `apps/web/src/leads/api.ts`、`PublicInquiry.tsx` 与 `src/CP6.CRM.Infrastructure/Leads/CrmBusinessService.Intake.cs`，此次只改合同边界。

- [ ] 固定 INQ 的 OpenAPI 55 个 definitions、C#/TS 组合派生 pin；LEAD OwnerId 接受原 UUID/unassigned/null，不放宽任意字符串。
- [ ] INQ strict decoder 保留合法 boolean extension/unknown index 规则、结构 uniqueItems、重复属性拒绝；枚举、required、长度 codepoint 与原 Schema 一致。
- [ ] 采用前写 INQ 合法/非法字段、对象属性顺序和数字表示相等、数组顺序不同、OwnerId nullable 的失败测试，再更新生成资产/decoder 并完成实际 net8 编译。
- [ ] 下载 proof/当前会话/服务器 ETag、90/99 到期终态、H01 原子写入和完整负长度 HTTP 响应留在 S03 实际接口验收。记录本任务只覆盖 generated adoption 场景的类型子断言，整条修订场景继续 NOT_RUN。

```powershell
dotnet test tests/CP6.CRM.UnitTests/CP6.CRM.UnitTests.csproj --filter FullyQualifiedName~InquiryContractAdoptionTests
# 在 CRM/apps/web 目录运行：现有 npm test 显式枚举文件，新增文件须明确执行。
npx tsc -p tsconfig.test.json
node --test .test-dist/tests/contracts-exact.test.js
```

预期新增 C#/TS 用例真实执行。必要时准确增加 package test 文件列表，不能仅凭旧 npm test 成功宣称新用例已跑。跨仓证据登记 CRM 实际源码/远端 main，不用 Main SHA 冒领。

## S01-09：Platform 准确合同路由与原字节（Platform，M）

**依赖：** S01-01。**文件：** 核对/修改 `src/CP6.Platform.Messaging/Cp6ContractBundle.cs`、`Cp6CloudEventValidator.cs`；测试 `tests/CP6.Platform.UnitTests/CloudEventContractTests.cs`，新增 `NativeProfileContractTests.cs`。

- [ ] 复用现有 eventType/schemaId/schemaVersion 三元组严格匹配和 Schema/manifest 哈希核验；它们已经存在，不重复实现或误报版本校验缺失。
- [ ] 对实际拟接入 profile 固定配置和 bundle 版本；当前按 eventType 索引且不允许同包重复 eventType，若消费者确需多版本共存，交付显式有版本路由适配和兼容测试，不使用 latest/fallback。
- [ ] 未知版本、正确 eventType 配错 schemaId、manifest/hash 不匹配均拒绝；相同原字节重放保持摘要，结构相等不能用于重编码原命令。不得在闭集原生 envelope 中擅加 profile 字段。
- [ ] 仅接入 S01 已指定消费者需要的合同边界。Inbox 事务、未知 commit、原结果重放和当前授权由 S02 处理；本任务通过不授予业务效果权限。

运行 `dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --filter "FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~NativeProfileContractTests"`，预期原严格校验不退化，新 profile 正反例通过。

## S01-10：PostgreSQL 精确存储适配（Main，L）

**依赖：** S01-03/04/06；独立测试库可先做，真实租户布局采用须等待 S02-00。**文件：** 新增 `CP6.Persistence.PostgreSql/Contracts/ExactValueStorage.cs`、`eng/contracts/s01/postgresql-mappings.json`、`eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj`、`ExactValueStorageTests.cs`；核对 `eng/database-compatibility/OwnedTestDatabase.cs` 和现有 PostgreSQL 模型配置。采用既有独占测试资源机制，不硬编码连接 Secret。

- [ ] CREDIT 按准确字段把 coefficient 存 `numeric(38,0)`、scale 0..8，Npgsql 参数采用无损整数表示；不要用 .NET decimal 承载 38 位系数。
- [ ] INV 的 66 项映射保存 ASCII 有符号整数 text 加 scale，原 wire 独立保留，耦合 nullability 正确；实际数值不 CAST 到有限 numeric/SUM。MAVG 正分母和余数映射齐备。
- [ ] ATP 五列及 CHECK 采用同时含 PLAN-IF-004/005 的组合 DDL，完整反映 128 字符边界；测试 fixture 不是生产迁移。
- [ ] 实际 PG 库写入/读回合法极值、非法值拒绝、null 耦合、失败事务回滚和重连原值恢复；驱动/PG 实际版本、源码和隔离库归属入证据。未配置可用测试资源时明确 BLOCKED，不跳过后宣称通过。
- [ ] 固定将来每个 Owner 表采用所需迁移/旧写者检查清单；仅独立 fixture 成功不代表全部生产表/消费者已采用。

运行 `dotnet test eng/contracts/postgresql-tests/CP6.ExactValues.PostgreSqlTests.csproj --filter FullyQualifiedName~ExactValueStorageTests`，预期 PostgreSQL 实测零失败/跳过。无需 SQL Server。测试库创建和清理遵循仓库 owned-resource 规则，不接现有业务库。

## S01-11：ERP-CREDIT 原生金额与冻结重放（Main，M）

**依赖：** S01-02（复用已建立的叶子测试项目）。**文件：** 新增 `CP6.Entity/Contracts/ErpCredit/CurrentCreditCodec.cs`、`FrozenR8CreditCodec.cs`、`cp6.web/src/contracts/erp-credit/creditCodec.ts`、`creditCodec.test.ts`、`ErpCreditCodecTests.cs`；核对 `CP6.Core/Services/Erp/CreditNoteService.cs`、`CP6.Entity/DomainModels/Erp/CreditNote.cs` 和 `CP6.WebApi/Controllers/Erp/CreditNoteController.cs`。

- [ ] 根据 COM-CREDIT-CURRENT-BODY-MONEY-FIELD、FROZEN-R8-CODEC-CURRENT-ADAPTATION、OPENAPI-FROZEN-REPLAY-MAPPING 的准确组合资产采用 current-v1/frozen-R8 codec/TypeRegistry。
- [ ] 测试各自合法 Money 与错误版本字段拒绝，保留冻结 R8 原始命令字节；旧信息不足以证明现行映射时保持 READ_ONLY_UNPROVEN/MANUAL_RECONCILIATION，不能补造 Owner 凭据。
- [ ] 不把 FIN-CREDIT 的授信 decimal profile 套入 ERP 贷项 Money；仅交付 codec 和明确适配入口，真实贷项/退货/应收效果留给后续 Owner 任务。

运行叶子 `ErpCreditCodecTests` 与 `npm --prefix cp6.web test -- src/contracts/erp-credit/creditCodec.test.ts`，预期原生和冻结合同分别通过，跨版本误套被拒绝，原字节摘要未变。

## S01-12：采用证据与消费者交接（Main 汇总，M）

**依赖：** S01-02 至 S01-11。**文件：** 新增 `docs/development/evidence/s01-adoption/manifest.json`、`README.md`；更新 `docs/development/roadmap.json`、生成任务视图及四份项目记忆。各仓源码实际实现由前述独立 PR 提供。

- [ ] 汇总准确 Main/CRM/Platform 源码 SHA、profile/Schema/派生 hash、构建产物身份、PG 版本、向量结果与远端 main 包含性；输入不变的成功结果按真实来源复用，不因 merge SHA 改变重跑业务测试。
- [ ] 比对同一向量 C#/TS/PG 结果，列出全部已接消费者和仍未接的真实 Owner/writer；库存/财务效果、租户部署及完整 H01/H02 仍未验收。
- [ ] 逐修订 case 标注完整或部分覆盖。部分覆盖的场景继续 NOT_RUN，证据可挂任务；不能把 codec 编译填作整条业务场景 PASS。
- [ ] 更新本切片任务交付事实，保留125目标和完整原验收门。给 S02/S03/S05/S07/S08/S09 各输出“可用能力/准确版本/尚缺 Owner 门”交接表。

运行 `python docs/development/tools/manage_roadmap.py --write` 后不带 `--write` 再检查，预期台账/生成视图一致；业务结果引用前述实际测试记录。12/12 只表示本切片范围交付，不等于全项目或125目标完成。

## S02-00：PostgreSQL 多租户存储与事务布局设计（Main，M）

**依赖：** 无 S01 代码前置。**文件：** 新增 `docs/design-handbook/architecture/02-postgresql-tenancy.md`、`docs/development/acceptance/postgresql-tenancy.json`；核对 Main `CP6Context.cs`、`TenantScope.cs`、CRM/Platform 各自持久化/消息事务入口，记录各自确切源码。输入为[用户数据库决定与六项设计要求](../../development/08-postgresql-baseline.md)。

- [ ] 盘点共享与租户表/身份/消息边界，评估并选定 schema 布局；保留现有 TenantId 与鉴权，给出版本、租户数量/升级成本假设及采用条件。
- [ ] 写清可信 schema 解析、运行/迁移角色、跨 schema 权限、连接池/事务失败恢复、EF 模型缓存、逐租户版本和迁移历史、单租户恢复与共享对象关系。
- [ ] 逐一设计 A/B 租户复用连接、显式跨 schema 访问、伪造租户、缺失上下文、后台重试、迁移中断和恢复用例，定义请求、身份、预期读写/拒绝及证据；全部 NOT_RUN。
- [ ] 集中审查 Owner 与 Inbox/Outbox 同一事务、权限不扩大、现有合同不丢失，登记所选布局及后续实现任务。此设计任务 DONE 只证明文档交付，不能把未运行隔离场景标 PASS。

本阶段验证 Markdown 链接、准确源码引用和 JSON 结构；不创建 schema、不搬数据。真实租户写入/迁移任务必须依赖此结论及相应实现验收。该任务不改变已接受业务目标数量。

## 每个任务的统一交付步骤

- [ ] 开工核对最新 main/远端和具体受影响入口，把任务置 IN_PROGRESS；依赖或输入不成立则登记明确原因，不清理别人的文件。
- [ ] 实现前先留所列正反例的失败或现有行为证据，完成本任务范围后集中运行相关检查。文档任务只做文档/结构检查，功能任务保留有效回归。
- [ ] 一次任务级完整 diff 审查，相关问题修正后仅复查影响部分。记录命令、环境、真实结果、源码/产物 pin 和未执行范围。
- [ ] 按各仓工作流检查后正常 PR/保护合并；核实远端 main 包含该提交和必要冒烟才记 DONE。未经新授权不触发 Actions，不部署生产。
- [ ] 在唯一台账登记任务验证/交付证据，生成视图并同步项目记忆。任务交付证据不自动提升其关联 target 或 case 状态。
