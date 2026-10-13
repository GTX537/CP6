# S01-08 独立接收审查

结论：**暂不可合并或登记 DONE**。已有集中审查与大量本地结果的源码适用性成立；本次发现两项有限、明确的未闭合门禁：一项已接受合同被反向采用，一项 web 工具链不符合约定。只需解决相关问题及对应回归，不重新启动全仓审查/测试。

本报告只读核对候选 `2ff7f1e4b3df8a4d492d971ffd8f19ff1ea77243`，相对基线 `4d08db119333a0586e69cb13a0824ed3f1c8e51c`；工作树 `D:/CP6.CRM-worktrees/s01-08-20261012` 干净、HEAD 相符。未执行测试、构建、导入器、网络、Git 写操作或 Actions；只计算文件/Git blob 摘要、解析既有日志/TRX和读取关键源码，输出本 scratch 报告。

## 1. [P1] status 99 合同采用倒置，不能仅留给 S03

这不是“原 positive 样例与天然权威 Schema 冲突，所以拒绝样例即可”。准确修订已经作出明确决定：

- Main `docs/design-handbook/remediation/06-wire-types-and-fixtures.md:57`：PrivacyExportView 的 **90/99 终态公开 expiresAtUtc/artifactId/downloadProof 均为 null**；历史到期时刻留在内部原记录；页面内嵌对象执行同一语义。
- Main `docs/design-handbook/remediation/00-disposition-register.json:2961` 的 `DR-COM-INQ-GENERATED-CONTRACT-ADOPTION/UI-TERMINAL-NULL`，decision 为 `Terminal protected expiry uses exact null-only wire`。
- Main `docs/design-handbook/remediation/01-version-and-authority.md:11` 明确不得按文件形式天然优先；必须采用本系列明确的新决定与范围。
- 已冻结 core-types bounded patch 明确将 UI `/46/value/export/expiresAtUtc` 从 `2031-04-05T10:00:00Z` 改成 null，decision 为终态99公开时间置空、历史留内部。因此组合 UI 的 null 是有意的已接受修订，不能当作错误 positive。

本次确认共享组合来源确有漏同步：OpenAPI 补丁仅修复 55 处引用，未更新 `PrivacyExportView/allOf/3/then/properties/expiresAtUtc` 及相关 x-semantic；C#/TS 内嵌 Schema 同样仍保留 status99 string 规则。这是上游修订资产未把明确决定同步到全部载体的缺陷，需要 COORD 按共享来源窗口处理。

候选不仅保留来源，还反向改动实现来服从旧 Schema：

- `scripts/import-s01-contracts.py:191-193` 将原 helper 的 90/99 一起要求 null 改为 99 必须非 null，变换名为 “authoritative schema retained expiry clock”。
- `apps/web/src/contracts/inquiry/index.ts:109-117` 对 status99 要求 completed/expiry string，随后比较历史时钟。
- C# 生成的 `InquiryContracts.g.cs:244-245,265` 及生成模板执行同方向规则。
- `tests/CP6.CRM.UnitTests/Contracts/InquiryContractAdoptionTests.cs:106-114,294-298` 和 `apps/web/tests/contracts-exact.test.ts:327-337,354-362` 将已经修订的 null 样例作为预期拒绝，并明确期待 status99 公共 expiry string 接受。

因此，保留原始文件/pin和如实记录 NOT_RUN 是正确的证据纪律，但不足以使这段**与已接受决定相反的生产 decoder**可合并。现有168/128通过是真实执行，受影响 expiry 断言的预期却不能支持已接受合同。既有 review.md 的“技术上可合并，无剩余问题”未发现此来源优先级问题，不能覆盖本次新发现。

建议处理边界：COORD 固定该共享资产问题，按已有 null-only 决定提供准确有界后继/适配版本，写清源 hash、原值、JSON pointer、后值、组合派生 hash及采用身份；不要覆盖历史原件、改旧日志或让消费者继续宣称旧 Schema hash代表新内容。协调跨消费者输入/manifest/vector身份影响后，D仅采用这一准确后继，恢复90/99公开字段null语义并删除反向“修正”及预期拒绝例外。至少定向覆盖 root PrivacyExportView 和嵌套 PrivacyExportUiState、90/99 null接受、公开非null拒绝、30仍保留正常完成/到期关系、原bytes保留与生成无漂移。生成输入变化后复核对应 importer/schema/type输出；其他无关成功结果继续按字节复用。

S03 仍负责真实服务器到期处理、内部历史时钟保留、下载 proof/当前会话/ETag、页面终态、权限和事务；**S01-08 已实现的公开合同类型/decoder必须先与既定null-only决定一致**。不能把当前合同不一致全部下放S03，再将本任务视为完成。

## 2. [P2] Node/npm 环境不符合任务约定，保留为条件门禁 OPEN

`package.json` engines 为 Node `>=22.13.0 <23`、npm `>=10.9.0 <12`，packageManager 为 npm10.9.4；执行卡要求遵循现有 engines。`ts-verification.json` 和 `ts-report.md` 明确记录实际 Node20.19.0 / npm10.8.2，`npm-restore.txt` 还直接保存了相应 EBADENGINE。不是元数据拼写错误，也不是128通过虚构。

这份运行结果可证明该实际旧环境的观察，不能证明约定环境通过。COORD 已确认将本地补齐受支持Node22/npm，仅补 web测试和typecheck，另存日志与环境/制品摘要，不覆盖旧记录，不重跑C#/其他无关检查。关闭本门禁以新证据为准；本审查未代为执行。若P1随后改变TS生产/生成输入，新环境结果也必须覆盖最终修改后的相关代码，不能提前用旧代码通过解除后续回归责任。

## 3. 既有证据可以复用的准确范围

独立重算下列摘要全部一致，无缺失：

| 记录 | 实际核对 |
| --- | --- |
| csharp-executed-source.json | 249份源码/配置/资源、2份实际DLL；候选字节相同 |
| ts-verification.json | 6份输入、13份TS源码/测试/package文件；候选字节相同 |
| web-executed-artifacts.json | 6份配置、11份实际编译JS；字节相同 |
| importer-executed-source.json | 5份实际导入器/测试/模板/manifest输入；候选字节相同 |
| generation-manifest.json | 22份来源pin、13份生成输出全部相同；日志的14 outputs包含manifest本身 |
| 跨记录合并核对 | 276份唯一已提交输入的候选Git blob SHA-256与执行记录完全一致，无未跟踪执行输入 |

C#最终TRX实际168执行/168通过/0失败/0跳过，含全部30固定ID；保留149旧GREEN与19/19失败的联合类型RED，未被伪装为最终运行。TS完整日志实际128通过/0跳过，记录为68本任务+60既有；30固定ID齐全。导入器完整性日志有6项实际通过。web typecheck日志完成并exit0，但环境限制按第2项。

已有集中审查的两项规格修复（C#正则尾LF、原生对象数组uniqueItems）和三项类型/联合修复（错误具体分支、LEAD根/嵌套联合、接口Encode丢payload）的代码与最终证据对应，可复用其准确结论；本次未从零重新审查这几项或复跑测试。定向读取了生产 decoder版本装载、严格预读、泛型分支验证、Schema条件/附加属性、LEAD原生类型及namespace绑定、生成前来源校验和只读检查入口，未发现另一个需要扩大验证的新问题。以上不表示所有可能缺陷均已排除。

代表性证据 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `docs/evidence/s01-08/csharp-executed-source.json` | `780ae4223548a195dfd4fed33b6f790eeb0149d3fa3cca731cafa6abb24c34ad` |
| `docs/evidence/s01-08/csharp-final.trx` | `670c2728e3eab28b62996756abe50fcb1831f042d9fdc423ed42bd3d9f2e8f89` |
| `docs/evidence/s01-08/ts-verification.json` | `91416d9ad6bf06b332913cb72fde3cea9550e113f1951ef84ec77658fda6ef78` |
| `docs/evidence/s01-08/ts-green.txt` | `8f58d06f01583ab5ca7513669c1c1df85fe8555b219318c2ade55c88181729bd` |
| `docs/evidence/s01-08/web-executed-artifacts.json` | `d21a01cfa9e0db88121bec259ef1feee800aaff781b1ba9865d71d284246a2b9` |
| `docs/evidence/s01-08/importer-executed-source.json` | `3a11a3f10174e0c474486a21fc527b7d891fbb94789c702e944bd471eaf3ce8e` |
| `contracts/s01/generation-manifest.json` | `b6b342167e1841fe6c694a08dff439f34f8cae6c53de000d42bd87dcd9e60aea` |
| `docs/evidence/s01-08/review.md` | `2b09d515493e915461b36d55ddf55867a765d014698dd6a6063156da0908b743` |
| `docs/evidence/s01-08/npm-restore.txt` | `12247c2c3e6754222ad10aa613a3281d75fe4b07debe65c04bf83e6a0a386481` |

## 4. 准入、范围与身份核对

111个变化文件在已分配目录或允许的两项配置例外内：web package仅追加实际测试入口，C# csproj仅添加3份便携资源复制。现有first-slice合同/生成器/端点/业务写入、依赖锁及工作流未改；不是把44-schema旧API替换为原生INQ。原生decoder实际位于Application/web生产源码，原始输入与helper不依赖Main工作树ProjectReference。

独立静态统计原/组合OpenAPI均38定义、257引用；原55个`#/$defs/`，组合为0，全部目标存在。三份Main Wire仅有记录的namespace替换。七组所选原/组合来源固定完整；profile/vectors仍分别为`2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e`和`b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`，共享包NOT_RUN未回写。

全部30固定case为JSON-001..007、NULL-001..007、EQUAL-001..007、LEAD-OWNER-001..005、INQ-VERSION-001..004。固定向量本身没有status99公共null义务的完整覆盖，故30项通过不推翻P1。原生Owner required/optional区分、boolean扩展、结构对象uniqueItems、codepoint、重复属性及版本入口有生产测试记录；其未受P1影响部分可复用。

CI前置证据记录远端main为本次base，candidate工作流与base逐字节相同且仅workflow_dispatch。执行者的队列为空记录是历史观察，本次没有网络复核，交付push/PR前仍由COORD执行实时复核。

原件保存、失败历史和真实NOT_RUN边界整体清楚；full INQ/LEAD、AC-INQ-009/048/053整业务、HTTP、H01、下载/权限、租户与PostgreSQL、生产验收均未被本次独立核对宣称PASS。当前状态应保持待修复/待验证，由COORD更新台账；待P1准确后继采用与P2最终环境证据闭环后，再进行修改部分复查和正常串行交付。
## 5. COORD 后续指示：最小显式版本 overlay 设计（尚未实施）

COORD 已决定按既定 null-only 修订纠正，不另请求用户改变设计。本节仅给修复窗口分析；第1/2门禁仍 OPEN，候选仍 BLOCKED。数值协调审查助手负责 Main `addenda/CRM-INQ-TERMINAL-20261013-R1` 的静态来源后继，CRM采用应绑定实际交付的准确文件/hash，不能提前把本节建议当作已交付。

### 准确 overlay 边界

保持 `CP6-S01-CONTRACT-INPUTS/1.0.0`、全部原件和现有composed文件/pin、原30向量expected不变。对准确 source OpenAPI SHA256 `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349` 叠加具名 `CRM-INQ-TERMINAL-20261013-R1`，只改两个Schema位置：

1. `/components/schemas/PrivacyExportView/allOf/3/then/properties/expiresAtUtc`：完整before节点是现有 `{type:string, format:date-time, pattern:准确原UTC最多7位小数pattern}`，整节点改为 `{type:null}`；不能只改type后留下不适用的生成语言表示，也不能漏断言完整旧值。
2. `/components/schemas/PrivacyExportView/allOf/3/then/x-semantic`：精确旧字符串 `requestedAtUtc <= completedAtUtc < expiresAtUtc; expiry tombstones artifact and proof but preserves clocks`；新语义明确为 `requestedAtUtc <= completedAtUtc`、公开expiresAtUtc/artifactId/downloadProof为null、内部历史expiry独立保留且不得充当当前下载授权。

根 `required` 保留expiresAtUtc必需；根属性可保持string/null联合，因为30与其他状态不同；99的completedAtUtc继续必需合法string。状态30的allOf/1及 `requestedAtUtc <= completedAtUtc < expiresAtUtc`不改；90的allOf/2现已要求public expiry/artifact/proof null，不改。两个替换不会增加/删除definition或ref，38/55/257库存断言继续成立。

精确引用受影响范围只有七个原生 `$ref`：两个HTTP响应定义（仅合同检查，不接线）以及 `PrivacyExportUiState/oneOf/1..5/properties/export`。后五项包括ready/requesting/completed/rejected/expired的内嵌export，均须解析同一effective Schema；不能只修独立 `PrivacyExportView` decoder。现有UI `/46`已符合既定修订，不改其bytes；修正新profile预期为合法接受并真实round-trip。full fixture冻结原件不清洗，受影响提取断言通过显式revision身份表示，不改历史结果。

### 身份与入口：来源身份不等于有效合同身份

在CRM新增只属于本任务的 `contracts/s01/revisions/CRM-INQ-TERMINAL-20261013-R1/`，保存准确overlay与authority来源pin、生成effective schema、source/effective身份manifest、变换before/after及输出hash。可用原有导入器扩展进行来源检查及确定性生成；不要覆盖 `upstream/composed` 或让冻结package的原hash变成修订输出身份。

建议将身份拆为两个显式对象：

- `sourceIdentity`：原OpenAPI/title/version/root/source_sha256，原4条INQ-VERSION仍只判定该native source locator是否正确。
- `effectiveRevisionIdentity`：准确revision ID、overlay hash、绑定的source hash、effective schema hash及采用表示版本；任一未知/不匹配均拒绝，无latest/fallback。

实际decoder必须通过新具名入口取得，例如 C# `LoadRevision(sourceIdentity, effectiveRevisionIdentity)`、TS `loadInquiryRevision(sourceIdentity, effectiveRevisionIdentity)`；具体命名可由执行者决定，但调用者必须明确选择revision。原 `Load(sourceIdentity)` 不得悄悄改变语义：可以保留为清楚标明的source-locator只读入口或显式原生兼容入口，必须没有默认升级，也不得被当作本次修订已采用的入口。所有S01-08生产合同采用测试与后继接线文档都指向新revision decoder。source-only入口若仍可decode，应明确其原版本范围及不能代表此次R1采用，不能作为失败后的回退。不要在原封闭业务payload上加profile字段。

C#可继续保留原source的base64/hash并额外生成effective Schema；TS也保留source locator metadata，另导出effective document与identity。原source hash只能说明原source输入，不得用来声称effective document逐字节身份相同。新的effective hash应在构建导入阶段计算/校验并绑定两端生成输出；C#运行时可重算嵌入effective字节，TS按现有browser-safe装载方案在build检查实际bytes、运行时强匹配source和revision metadata，不宣称未实现的浏览器SHA能力。

### CRM 最小代码与测试窗口

沿用原已批准Contracts/Inquiry、相关wire、生成模板、导入器、任务tests/evidence窗口，增加上述revisions输入资产目录；不改first-slice/API/UI/数据库/依赖。具体修正：

- importer移除把status99改成非null的反向变换；生成Schema/类型helper采用显式overlay，保存输出identity。
- C#/TS privacy semantic拆分：30保留三时钟有序；99只校验requested<=completed且公开expiry/artifact/proof null；90维持现有终态null。不要对null调用时间比较。
- 当前root-only semantic调用不足以证明内嵌99的时钟约束。对上述5个原生UI内嵌export位置执行同一具名PrivacyExportView语义，或在明确native引用遍历中挂接该semantic；避免将任意对象按字段名猜成privacy对象，也不扩散成通用业务规则引擎。
- 纠正旧测试的“99string接受/null拒绝”和对UI expired样例的人工拒绝例外。新增source/effective身份混淆、unknown revision、effective hash错误、old Load无隐式升级的负例；保留全部30固定expected并明确四条INQ-VERSION处在新revision入口的source验证层。
- 定向运行root+5个内嵌privacy分支的90/99null接受和public非null拒绝、缺失字段拒绝、99 requested>completed拒绝、30时钟规则、UI `/46`真实通过与raw bytes/round-trip、生成无漂移。输入版本改变后的importer6项完整性测试与对应生成输出检查需要更新；不因此重做数据库/HTTP/全仓业务测试。
- 两端修正后再完成受支持Node22/npm的相关web测试与typecheck，保留旧Node20观察和全部旧失败记录。新证据将准确source/effective身份、源码hash和结果一并绑定；只复查变化与影响。其余C#/LEAD/无关向量成功证据按未变byte范围复用。

交付结论直到上述纠正和条件门禁证据完成前仍为BLOCKED。Main共享overlay交付、CRM真实采用、S03真实业务运行分别记录，不能互相替代。