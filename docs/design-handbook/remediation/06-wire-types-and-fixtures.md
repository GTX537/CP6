# Wire、类型、物理表示和样例修订 R1

适用 `CP6-DESIGN-REVISION-20261011/R1`。本页决定与原文件的局部修订资产一起采用；旧归档保持原字节。跨 Owner 的同名 Money、Ref、UOM 不是共享同一个 DTO 的依据。

## 1. 金额与数量的无损表示

### FIN-CREDIT：保留 38 位总有效数字、最多 8 位小数的合法 wire

`OPS-IF-002` 的新决定是保留原 Schema 的 positive/nonnegative/signed 各自词法域。原主文 `decimal(38,8)` 不能承载合法的 38 位纯整数，故**撤销其作为所有金额值唯一物理列表示的局部决定**，改为 `(UnscaledValue decimal(38,0), Scale smallint)`；Scale 为 0..8，整数系数的十进制位数至多 38。这两个值必须同一行/同一事务持久化，不另设可独立修改的近似金额列。

解析顺序：原字段 Schema 词法校验 → 取符号、移除小数点得 BigInteger 系数 → 原小数位数为 Scale → 无损往返检查。`-0`、指数、前导零或多余尾零仍按原 Schema 拒绝；零的规范形式仍为 `0`。SQL Server 和 PostgreSQL 均可用 numeric/decimal(38,0) 存系数；不能先解析为 .NET decimal、JS number、float/double 或 decimal(38,8)。

加减比较先将系数提升到相同 Scale，用任意精度整数运算；中间聚合不限 38 位，不能逐项截断。写回某个受限公共金额字段时，先按该字段原 Schema 检查准确结果；超出原域按原错误合同拒绝整意图并零业务写，不饱和、不截断、不新增默认舍入。FX 与币种舍入只按已绑定的原政策版本执行；本修订不发明税率或币种精度。

旧库有限精度金额只有在逐行验证精确等值、原 Scale 与回执可恢复后才可移入新表示；不能从已截断值重建原额。保留可信原 wire/回执字节，未知旧值不得标迁移成功。迁移脚本和真实库演练属于后续编码任务。

### FIN-INV：保留开放整数位与固定小数上限

`OPS-IF-030` 的 Decimal18/Qty12 原词法允许任意整数位，不能统一收窄到 decimal(38,18)/(38,12)。本修订采用 `UnscaledDigits`（ASCII 十进制有符号整数文本）＋`Scale`；SQL Server 为 varchar(max)，PostgreSQL 为 text，Scale 分别 0..18/0..12。物理表中原有限精度列不再作权威数量/金额；可选检索投影必须标派生、可为空、不参与财务判定。

MAVG 的分子、正分母和余量同样使用大整数文本和应用端 BigInteger 精确运算，保留原算法对符号、余数和分配的约束；不生成 SQL Server 不支持的 decimal(76,0)。数据库只作原子保存与版本/身份约束，不能将文本 CAST 成有限 numeric 后 SUM。单请求资源限额按原传输入口处理，不伪装为缩小业务数字域。

INV 原字符串允许的尾零/负零形式不因 CREDIT 规则而被拒绝。数值表示用于算术，原 wire 与被摘要的准确字节独立保存，禁止算术归一化后重建历史摘要或重放内容。

### 基础验证向量

| 输入/动作 | CREDIT | INV |
|---|---|---|
| 38 个 9 的纯整数 | 接受，Scale=0，系数精确保留 | 接受 |
| 30 个 9＋`.12345678` | 接受，38 位系数/Scale=8 | 接受 |
| 39 个 9 | 原金额域拒绝 | 整数域接受 |
| `0.00000001` | 接受 | 接受 |
| `25.0`、`-0` | 原 CREDIT 规范拒绝 | 依原字段 Schema 接受，原文本保留 |
| 两个大值相加后再减去一个 | 中间值任意精度，最终值再核原域 | 任意精度，禁止溢出后恢复 |
| 正分母为 0 | 不适用 | MAVG 原除法语义拒绝，不以零填充 |

## 2. ATP 单位标识

`PLAN-IF-004` 保留完整 ASCII 1..128 标识域。审查发现原 `$` 锚点在.NET/Schema所用正则语义下可接受最后换行，因此本轮明确发布 `ATP-STABLEID-LEXICAL/20261011-R1` 后继：C# 使用 `\A[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}\z`；Schema/TS 使用真正末尾否定 `^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}(?![\s\S])`，Schema同时明列minLength1/maxLength128及新$id。原历史profile和原字节保留，不能默称原Schema已严格拒绝末尾LF。

Quantity.uom、转换/原生单位标识按准确新profile采用；五个PostgreSQL varchar(16)列改为varchar(128)，同时限制原字符集合并排除任何非法字符。中文、空白、空串、129长度及短/128字符后附LF或CRLF均拒绝；所有完整合法ASCII1..128标识保持无损。旧异常值不截断换行冒充合法值，保存原证据并由Owner显式更正后再产生新效果。此Schema词法修订与当前C#/TS必须一起登记，不静默替换历史消费者。

## 3. null-only、可空元数据与未绑定名称

- `NCR-TYPE-01`：三处 C09FailedProblem.originalQuery、Main6 非成功 guardRef、CP4 非成功 guardRef 均必须在 JSON 出现且仅为 null；采用 `Cp6.DesignRevision.NullOnly` 和严格 JSON converter。数字 0、缺字段和对象都不合法。属性继续 required，不能靠 getter 丢弃错误输入。
- `NCR-FIELD-01`：MainResult CLOSE_MAIN.priorHeadRef 的 nullable 改 false；InitialSourceOptionQuery 的 sourceKind/keyword/cursor 和 InitialSourceOptionPage.nextCursor 改 true。准确 shape 和 producer 权限不改，editable 元数据不提供浏览器写权限。
- `COM-LEAD-GENERATED-CS-NULLABLE`：OwnerId 改为单层 `string?`，原 UUID/unassigned/null 严格 Schema 校验继续存在，任意字符串不因 CLR 为 string 而合法。
- `OPS-IF-015`：按准确 canonical 成员解析 Evidence/Uom 后建立具名别名；不是按词面把全部 Evidence 都映射同一种 Ref。修订资产记录具体类型/指针与比较证据。

## 4. INQ 生成附件

`COM-INQ-GENERATED-CONTRACT-ADOPTION` 拆为引用解析、语言类型、uniqueItems、终态页面、样例 transport/ETag/负例前置六个修订点：

1. OpenAPI 3.1 的 55 个 `#/$defs/` 引用改为已有 `#/components/schemas/` 对应指针；校验所有引用存在，保持类型内容不变。
2. 三个 SourceExact 事件的 additionalProperties 原规则为布尔值，不能给含固定 string/number/object 成员的 TS 对象加 boolean 全字符串索引。TS 表示改为 unknown 索引＋现有严格 Schema decoder；未知键仍只能是 boolean。编译类型放宽不改变 wire 合法域，必须通过 decoder 才可成为消费对象。
3. C# uniqueItems 按现有结构 Equivalent 比较整个 JSON 值；对象键顺序不影响相等，数组顺序影响相等，重复 JSON key 在解析阶段拒绝。不得继续用 GetRawText 比较；本修订不把该相等算法充作签名摘要 canonical 算法。
4. PrivacyExportView 的 90/99 终态公开 expiresAtUtc/artifactId/downloadProof 均为 null；历史到期时刻保留在内部原记录，不能作为当前下载授权。页面内嵌对象同样执行该语义，终态样例同步。
5. 下载使用 CRM session 的 Authorization 与独立 X-Download-Proof；UI 使用本次服务器响应 ETag；长度负例先提供合法幂等键，否则只能证明幂等键拒绝。

## 5. 连续样例修订纪律

对同一意图、回执、effect、消费行、版本或原始事实，先确定原规范的准确权威字段，再同步所有相关载体。修订资产必须指明原值、后值、准确位置与新版本；涉及摘要的变更使用原算法和完整 preimage 复算，不用手填同一个 hash 使表面一致。

规范算例不能悄悄变成另一场景：例如已发生的 Draft 作废后仍有历史效果；采购叶总体 40/60 不拿母域 100 作政策量带；RMA 头 SHIP-100 不等于行 SHIP-100-L1。对已有设计明确的状态或 wire，修样例/适配，不扩大原枚举掩盖矛盾。

每个细项的决定、精确补丁和验收结果由统一处置表及本目录 assets 中的修订清单列出。未完成摘要重算或相应验证的样例不得登记为修订完成。

## 6. 来源

- CREDIT：原 `FIN-CREDIT-WIRE.schema.json` 的 `$defs/positiveDecimal`、`nonNegativeDecimal`、`signedDecimal`；原 R4 正文 537 行。完整路径和 SHA 见 `OPS-IF-002`。
- INV：原 `CLOSED-WIRE-SCHEMA_R10.json` 25–35 行、DATA-DICTIONARY MovingAveragePoolRevision，见 `OPS-IF-030`。
- ATP：R11 Schema StableId、C# 194 行、DDL 五个 uom 列，见 `PLAN-IF-004`。
- NCR/LEAD/INQ：见原发现台账对应编号及修订资产逐文件源 SHA；保留原历史接受，运行状态不变。

## 7. 本轮独立编译发现与验证范围

为验证生成附件可用性，对修订派生副本做了独立编译，没有引用业务项目或运行归档程序。ATP、LEAD、INQ、NCR Main/CP4 五份C#附件及IPQC/INQ两份TypeScript附件已通过独立编译；LEAD保留5条非可空初始化警告，原严格Schema仍是必需入口。C#采用本机 .NET10 SDK，不能据此宣称.NET8目标/运行集成通过。

此检查在同一已登记问题内发现并修正：INQ TS 缺失JsonObject别名及never函数声明；INQ C#七处空数组缺显式string类型；ATP JsonDocument.Parse对ReadOnlySpan的重载错误；NCR CP4 的 ObservationInput 数字分支code、分类分支value另外两处必须为required const-null。后两字段已核准确 `SOURCE-QA-SCHEMAS.json#/$defs/ObservationInput`，采用与原五处同一NullOnly转换器。

失败原记录、修复和后续结果分别保存在 `local-check-history.json` 与 `assets/core-types/attachment-compile-validation.json`。另外14项新NullOnly/结构相等辅助类型测试、23项精确数字/单位设计向量通过；这些局部结果不能代替整套Owner实例、数据库迁移或业务验收。

CREDIT current-v1 的C#/TS也分别做了独立编译，初次发现5项/6项诊断，合并7条最小有界修订后对准确最终字节编译通过。修订包括C#字节Parse重载、局部变量遮蔽/显式多变量类型，TS大整数加法返回成员、WebCrypto准确字节视图复制和已验证PENDING判别投影。原TYPE/native pin与接受域不变；复用证据按新派生SHA绑定在 `assets/commercial-fixtures/codec-compile-validation.json`，没有执行codec业务程序。

## 8. 修订样例与采用入口

| 资产组 | 修订内容 | 采用规则 |
|---|---|---|
| `assets/commercial-fixtures/` | RETURN四叶数量及完整父摘要、EST政策/TTL、CREDIT Money/Store/codec/OpenAPI及回放 | frozen-R8与current-v1分包；旧command原bytes不重写，真实Store/Owner资格另证 |
| `assets/planning-fixtures/` | EXC四个新sealed世界及native/Case引用、SC06义务范围/状态、PO事件与WO请求摘要 | 新DEMO身份与旧历史快照分离；完整本地物化recipe可复现，不把旧ledger冒充新运行证据 |
| `assets/operations-fixtures/` | INV状态与财务连续回执、IQC叶政策/测量闭包、IPQC写集与投递、RMA围栏和准确行引用 | 新样例必须含完整对应证据；原政策/原评审保留，原不合法示例作为负例而不发明历史成功 |
| `assets/fixtures/` | PR冻结批准/时序、CREDIT阶段/父键/RV、PO OwnerProfile与字段主键、AC步骤 | 每条修改有精确前置值；影响contractBody的改动采用新版本与新pin |
| `assets/core-types/` | 无损值类型、生成附件、null元数据、LEAD AC后继映射 | 原60 AC身份保留；修订目录按原ID解释当前预期，不重标旧执行 |

准确原件、新资产和组合派生SHA见 [manifest](assets/manifest.json)；复杂样例的算法、每节点依赖和局部验证见各组recipe/manifest/validation。完整派生文件只写独立本地缓存，归档保持只读。
