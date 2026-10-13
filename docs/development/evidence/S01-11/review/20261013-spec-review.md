# S01-11 独立规格审查

结论：**PASS（本任务限定的合同采用与重放 guard 范围）**。未发现应阻止本任务进入 QUALITY 阶段的规格缺失、越界实现或验收范围夸大。本报告不代表 QUALITY、合并、共享状态同步或真实业务验收完成。

审查对象为 `codex/s01-11-erp-credit-20261012` 工作区新增内容；审查开始时均未暂存，按 `git ls-files --others --exclude-standard` 逐目录纳入，结束时协调代理已暂存其中 32 个生产/测试/合同包文件。HEAD、main、origin/main 的确认基线均为 `e20740e37e20bc0210e0b32c5670c2bce962002a`；设计输入仍为 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`。具体执行字节及来源核验见 [独立审计数据](20261013-spec-audit.json)。所有 Git 调用使用 `-c core.longpaths=true`。

审查依据为第四轮 S01-11 派工、准入 Markdown/JSON、原任务计划 S01-11 条款，以及任务 PLAN、entry、design-references、owner-boundary、surface、provenance。原始和组合生成源码只按准入定位读取为数据，未执行、复制完整归档程序或运行数据库操作。

## 规格逐项结论

| 要求 | 结论与证据 |
| --- | --- |
| 准确 current-v1 / frozen-R8 身份及源组合 | PASS。独立重验 20 项所选原始/组合/native 来源字节、10 个生成源码范围摘要、5 个采用资产。两个版本准确固定 namespace/profile、native schema、registry 和 root，无 latest 或自动回退。实现见 `CreditCodecSet.cs:29`、`:44`、`:79`，TS `creditCodec.ts:29`、`:290`。 |
| 完整原生对象与有界发布面 | PASS。14 个原设计发布根均返回完整不可变 NativeCreditDocument；另外 2 个 Money/D8 辅助入口、2 个 C01/C02 专用重放根明确分开。独立遍历两个版本各 63 个定义，三文件闭包与 surface 清单完全一致，未发现缺失的 JSON Schema 断言关键字实现。F09 依赖的三个 C06 根未开放。见 `CreditCodecSet.cs:52`、`:100`、`:130` 与 TS `creditCodec.ts:262`。 |
| 原生金额与字段域 | PASS。WMS 原始 `{value,currency}`、30 位整数/8 位小数与 FIN-AR 20 位整数/8 位小数独立保留；schema 原字节不变，合法 `-0`、`1.0` 保留。完整对象正反例、30 位金额及原设计 18 处 Money 投影均执行；没有套用 FIN-CREDIT canonical profile。金额、范围和数量运算使用 BigInteger/bigint，不经 decimal/浮点。见 `NativeCreditSemantics.cs:109`、TS `creditCodec.ts:211`。 |
| 严格 JSON、结构相等、required/null-only、封闭对象 | PASS。C# 复用 S01-02 StrictJson/JsonSemanticEquality；未调用 net8 不存在的 JsonElement.DeepEquals。TS 保留 exact number token 后再按已确认的小整数域 materialize，使用 decoded 键集合与 own-property 检查。JSON Schema 的 ref sibling、无 type 的条件 min/maxItems、嵌套 native 语义均执行。见 `NativeSchemaValidator.cs:28`、`:48`、`:93`、`:116`，TS `creditCodec.ts:44`、`:58`、`:99`、`:145`。 |
| 版本特有结果语义 | PASS。current APPLIED 必须 Ref、REJECTED 禁 Ref、NO_CHANGE 可带 Ref；frozen 保留非 APPLIED 禁 Ref 的旧行为。FinArObservation 用准确版本 registry 验证，FAILED_NO_EFFECT 另验 base64 原字节、严格 JSON、SHA 与结构相等；FOUND/PENDING 投影一致性受检。见 `NativeCreditSemantics.cs:43`、`:94` 与 TS `creditCodec.ts:250`。 |
| 冻结历史字节与 C01/C02 重放 | PASS。保存完整首次 HTTP201 COMMITTED CommercialDraftView；key 和原请求字节匹配后 HTTP200 返回完全相同的已存正文，不用 TerminalCommandReceipt 替代，不重命名/重建/再次写入。C01/C02 的请求本身仅作原字节保留，不冒称获授权 Owner 命令。见 `FrozenR8CreditCodec.cs:8`、`CreditCodecSet.cs:156`，TS `creditCodec.ts:280`、`:307`。 |
| 证明不足时维持限制 | PASS。适配入口始终报告 READ_ONLY_UNPROVEN / MANUAL_RECONCILIATION、UNKNOWN，CanRelease/CanDispatch/EstablishesFound 均为 false。代码没有可用自报布尔绕过的授权入口。原始完整文档保留。见 `CurrentCreditCodec.cs:8`、TS `creditCodec.ts:321`。 |
| 原设计正文/Store 依赖链及 OpenAPI | PASS，范围仅为设计子断言。独立按准入来源 JSON pointer 对照 343 个投影，除明确排除的历史 SQL recipe 字段外逐对象一致；5 个正文逐字节一致。已读取设计审计的 60 个篡改拒绝、18 处 Money、29 路由、10 扩展块及 C01/C02 200/201 根映射结果。未将符号 receipt 视为真实签发/Store 证明。 |
| 任务范围与既有 Owner 边界 | PASS。新增文件均在允许的五类路径内。已读取 CreditNoteService、CreditNote 实体和 Controller 并复核 owner-boundary 文件摘要；旧查询/金额存储未接线或改动。共享 Wire、原 S01 包、项目依赖、工作流、roadmap/状态文件未被本实现改变。 |

## 复用的真实验证记录

本审查读取原日志及报告并独立核摘要、运行期间输入不变和当前受测源码相同；**没有重跑**任何功能门禁。

| 记录（均在 `../implementation/`） | 独立回读结论 |
| --- | --- |
| `20261013T060354Z-dotnet-final-407086e6` | 417/417 Passed；零失败、未执行或跳过。TRX 实际记录 `.NET 8.0.29`。 |
| `20261013T060526Z-ts-final-5254b7ee` | 416/416 Passed；零失败或 pending。 |
| `20261013T060619Z-typecheck-83ec4cb2` | type-check exit 0，输出摘要吻合。 |
| `20261013T060706Z-design-f299e68e` | 5/5 设计 fixture 审计；零失败、跳过。 |

两语言各执行 400 个私有用例（362 个构造输入、38 个选定设计投影）及 VERSION-001..008、BYTES-001..002 全部 10 条固定向量，另有各自 6 个边界 guard；C# 另含 1 个实际 net8 断言。已从 TRX 的完整 `item` JSON 独立恢复并逐对象核对全部 400 个私有输入与 10 个固定输入，TypeScript 逐 ID 核对唯一执行记录。长 ID 的 xUnit 显示截断/转义导致审查脚本首次显示名匹配失败；改用既有完整 item 数据后通过，未改写测试结果。该审查脚本失败范围保留在审计数据中。

BYTES 两条固定向量的 `expectedRowVersion:"1"` 不满足完整 PostInput；实现测试明确断言 full-native 拒绝，独立保留类型报告 `NativeSchemaValidated=false`，没有将字节保留成功称为合法业务输入。

运行后仅观察到 `eng/contracts/s01-11/.gitattributes` 的非行为属性调整，生产/测试/fixture 字节未变。阶段末 `git diff --cached --check` 报 `provenance.json` 原 CRLF 的 trailing whitespace；这是 `-text` 保留真实受测字节后的属性表现，已告知协调代理用局部 `whitespace=cr-at-eol` 处理，不建议重写证据字节或重跑测试，不作为规格阻断。

## 未验收范围

真实 Owner/current AR/GL/member/finite、issuer/permission/Store 收据、持久化、租户授权、PostgreSQL、事务恢复、HTTP 宿主和业务写入均未验收。三文件包不提供任意 registry 或 F09/C06 根解析；C01/C02 guard 不认证 key、请求身份或 Store 来源。DecisionComponentInput 的跨组件业务规则、原已存记录的真实性与可重放权限仍归可信生产/存储集成，不能由本次历史正文 schema/字节核验替代。

247 完整运行场景继续 NOT_RUN；41 历史静态条目继续 REFERENCE_ONLY；ITER/REL 原件缺失、WP05 PROPOSED 不变。旧 RED、首次 type-check 失败和较早绿色记录继续保留其真实范围。本审查未执行 Actions、数据库、提交、push、部署或共享文件修改，仅新建本目录审查报告。

规格阶段已结束；QUALITY 仍待协调代理安排。
