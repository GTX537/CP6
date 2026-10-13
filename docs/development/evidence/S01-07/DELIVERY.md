# S01-07 NCR/IPQC 质量合同交付

本任务交付原生质量合同的类型、严格读写边界、可移植输入和本地验证证据。C# 的 24 个入口及完整 92 声明闭包实际编译；TS 保留 composed-19 全部 203 个类型别名，为其中 23 个完整对象提供运行时解码。真实 QA Owner、现有质量服务、写入事务和数据库接线未纳入本次合同库验收。

业务分支为 `codex/s01-07-quality-contracts-20261012`，实施和测试基线为 `e20740e37e20bc0210e0b32c5670c2bce962002a`；固定输入基线为 `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`。两者间只有协调文档。测试在未提交任务文件上执行，真实 HEAD 和逐文件 SHA-256 已记录；最终候选 SHA 随提交后发布的 Handoff DELIVERY 固定，不能把提交后的证据复用描述为重新执行测试。

## 实现及来源

| 交付 | 范围与可核对入口 |
| --- | --- |
| NCR C# | [合同说明](../../../../CP6.Entity/Contracts/Quality/README.md)、[生成记录](../../../../eng/contracts/s01-07/csharp/generation-manifest.json)、[逐字段清单](../../../../eng/contracts/s01-07/csharp/native-field-inventory.json)：24 个原生入口、92 个声明、102 个完整 schema 节点；MainResult 仅暴露完整 oneOf/14 分支，其余未采用表面未被宣称可解码。 |
| IPQC TypeScript | [生成记录](../../../../eng/contracts/s01-07/ts/generation.json)、[解码入口](../../../../cp6.web/src/contracts/quality/ipqc.ts)：21 个 native registry 对象及 2 个从准确 TS 声明适配的 R4 对象；五种事件按原 canonical 身份路由。两个 R4 对象没有伪造 native registry 身份。 |
| 原始来源 | [sources.json](../../../../eng/contracts/s01-07/sources.json) 固定 32 份资产：8 份 original/composed、10 份原 Schema 闭包、6 份原协议、8 份 Git 字节 pin/补丁。原件、组合件、成员序号、容器 hash 与来源 Git blob 可追溯。 |
| 生成与复现 | [输入说明](../../../../eng/contracts/s01-07/README.md)；生成器先核来源 hash，遇未支持关键词或冲突路由失败。运行及测试仅读取仓内副本，不需要原 D 盘归档。 |
| 后续接线 | [QA Owner/消费者清单](OWNER-CONSUMERS.md) 和 [15 份只读源码 hash](owner-consumers.json)，保留各模块实际责任与现有服务/控制器/前端入口。 |

START 时导入 26 份来源，随后补齐同一已接受 R12 容器的六份原协议，形成 32 份最终清单；没有替换既有来源或修改原 expected。最终来源清单 SHA-256 为 `bbd32a7c63ce81af6967f5e9d47bc04dbd5779e3b6012e3c7fbe8aaca397b9e4`。原设计的接受、`PROPOSED` 和 `RequiredFenced` 状态保持原义。

七个字段保持 required NullOnly：Main C09 三个 `originalQuery`、Main IPQC blocked/unknown `guardRef`、CP4 同类 `guardRef`、ObservationInput numeric `code` 和 categorical `value`。缺失、0、对象及字符串 `"null"` 均拒绝，合法其他 nullable 分支继续允许值或 null。五项元数据均 required、editable=false；MainResult oneOf/14 的 `priorHeadRef` 为 nonnullable，InitialSourceOptionQuery 的 `sourceKind/keyword/cursor` 和 InitialSourceOptionPage 的 `nextCursor` 为 nullable。

各模块保留原生 Evidence/Uom/Ref/Scope 字段和闭集，未构造万能 Ref。读写均校验完整对象及 union 身份，拒绝字面与转义等价重复键。数量和版本字符串保留原有精度；原 pattern 保持原文，.NET 的末尾锚适配只纠正运行时方言差异。日期格式保留四位年份、原始小数和 offset；闰秒检查只证明可表示位置，不证明 IERS 历史事件。

## 实际验证

| 门禁 | 最终结果 | 证据 |
| --- | --- | --- |
| NCR C# 合同测试，net8.0 | 657 / 657 PASS；0 失败、0 跳过；实际 .NET 8.0.29，SDK 10.0.302 | [verification.json](csharp/verification.json)、[原始 TRX](csharp/quality-contracts-format-final.trx)、[原始日志](csharp/native-format-final-gate.log) |
| IPQC TypeScript 合同测试 | 130 / 130 PASS；0 失败、0 跳过；Node 22.22.0 / npm 10.9.4 | [最终执行记录](ts/08-date-regression-validation.json)、[逐项日志](ts/08-date-regression-1.log) |
| 完整 Web type-check | PASS，exit 0 | [原始日志](ts/08-date-regression-2.log) |
| 来源与两个生成器 --check | 32 份来源一致；C# 8 个生成文件一致；TS 203 别名、23 roots、5 routes 一致 | [来源清单](../../../../eng/contracts/s01-07/sources.json)、两端生成记录、[最终完整性核对](final-integrity.json) |
| 任务级独立规格与代码质量审查 | 两项 PASS，无遗留实质问题 | [集中审查](REVIEW.md) |

C# 包括 594 份完整对象 fixture（102 合法、492 非法），覆盖全部 92 个已公开声明的正例、required 缺失和闭集拒绝。TS 使用五份独立原始 publication body 和十八份手写完整 fixture，逐入口验证合法对象、每个必填字段缺失、额外字段和错误根类型；另覆盖全部错误事件组合和关键嵌套身份。构造 fixture 只作为合同测试，未称为真实 Owner 数据或生产验收。

全部 26 个冻结 ID 逐项执行：`NCR-NULL-001..020` 在适用完整对象中执行 28 次字段断言，仍保留 `FIELD_SUBASSERTION_ONLY` 范围；`IPQC-TYPE-001..006` 在完整 R4 对象中替换对应字段，仍保留 `TYPE_BINDING_ONLY` 范围。原始向量文件 hash 为 `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01`。逐 ID、原断言和实际记录见 [fixed-vectors.json](fixed-vectors.json)。完整 decoder 的结论来自另外执行的完整对象测试，不由 26 条投影推断。

历史失败完整保留：C# declaration、native、writer、regex、RFC3339 的真实 RED 见 [C# 验证说明](csharp/README.md)。TS 初始三项行为 RED 在 `ts/01-red.log`；Vite 动态 URL 路径失败在 `02-native-tests.log`；`05-harness-failure.md` 记录 Python 输出编码失败，未冒充完整门禁通过；`06-final-validation.json` 记录五个错误的新增末尾锚预期。实际 Node 行为确认后修正新增测试，未改冻结向量或源合同。`07-final` 和 `08-date-regression` 保留修正后的真实结果。

## 交付窗口与未验收范围

全部变更仅在五个获准路径内：`CP6.Entity/Contracts/Quality/`、`cp6.web/src/contracts/quality/`、`eng/contracts/exact-values-tests/QualityContractTests.cs`、`eng/contracts/s01-07/`、本证据目录。共享 Wire、项目/依赖/锁文件、原输入包、业务服务/控制器、工作流和共享台账均只读。

[工作流审计](workflows.json) 记录候选与远端 main 各 13 份工作流的事件、过滤、hash 和依赖：普通工作流保留手动入口，R2 保留 tag 候选发布门禁。普通分支 push 不触发远程编译/测试；实际 push 前再次核对候选与新获取的远端主线，并在 Handoff 绑定最终 SHA。没有触发、重跑或取消 Actions，没有改变工作流触发或设置，没有发布包、操作数据库或部署。

未验收：已采用闭包以外的完整原类型面；QualityInspectionService/DefectRecordService 与 HTTP/前端消费者接线；Owner currentness、权限、租户、真实跨对象业务规则及原子写入；数据库/迁移/真实宿主/生产推广。TS 接受原 IPQC Schema 未禁止的 JSON 转义孤立 UTF-16 代理值，C# 原生 materializer 按其来源拒绝；不宣称这些不同 profile 的全部字符串可互换，也未借 CP4 规则收窄 IPQC。后续数据库仅以 PostgreSQL 为目标。本地结果不把 247 项完整运行场景自动变为 PASS；41 项历史静态 REFERENCE_ONLY、ITER/REL 原件缺失、WP05 PROPOSED 按原记录保留。

COORD 拥有唯一 roadmap、生成视图和四份状态文件的串行更新窗口，以及正常保护合并和远端 main 包含核对。本报告和 Handoff DELIVERY 是候选交付证据，不能替代 COORD 的最终 DONE 登记。
