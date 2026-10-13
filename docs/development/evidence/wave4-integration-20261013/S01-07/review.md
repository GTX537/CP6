# S01-07 协调准入审查

**ACCEPT** — 候选 d0a3ae93e23aeb48128b0519dbf585e64c508ccf，基线 e20740e37e20bc0210e0b32c5670c2bce962002a。未发现阻止合并的正确性、合同、权限或数据一致性问题；findings 为空。仅接受本次已公开的合同类型/decoder，QA 业务与生产验收仍 NOT_RUN。

按 requesting-code-review 与 D:/CP6/AGENTS.md 完成一次任务级审查。已读原 S01-07、wave4 执行包/准入报告及原语言、Schema、canonical/publication 来源。全部 110 个新增路径纳入审查：产品运行时、测试和工具完整审阅；所采用 native 闭包逐谓词对原件核对；未采用的大型历史输入仅作来源/字节核验，未重新认定其业务语义。core.longpaths=true 下工作树干净，完整 diff --check 通过。

| 核对 | 结论 |
|---|---|
| required NullOnly | Main 4 + CP4 3（含 ObservationInput 两项）；显式 null 合法并保留，缺失、0、对象和字符串 null 拒绝。 |
| 五项字段元数据 | 全部 required=true、editable=false；priorHeadRef nullable=false，sourceKind/keyword/cursor/nextCursor nullable=true。合法其他 nullable 分支保留。 |
| C# 原生闭包 | 24 roots / 92 declarations / 102 schemas，8 unions / 12 wrappers；原谓词与引用闭集核对一致。MainResult 仅公开完整 oneOf/14。 |
| IPQC 原生身份 | 21 registry roots 逐项等同原 Schema，另 2 R4 roots 按准确 TS 声明适配；QAP/Exec/Out Evidence、各 Uom/Scope/版本身份分开，五事件按 canonical 路由。 |
| 严格 JSON 与读写 | 重复/转义等价/嵌套键、未知字段、缺必填、错类型与错 union/event 均拒绝；C# 具体分支 writer 和 TS encode 同样校验。 |
| 来源与生成 | 32 资产、8 Git pin 字节/blob、16 容器成员序号/路径/hash 匹配；四份组合资产独立重放原补丁且组成索引匹配；两生成器 --check 通过。 |

| 原始验证（复用，未重跑） | 真实结果 |
|---|---|
| C# QualityContractTests | 657 唯一结果全部 PASS，0 failed / 0 skipped；19 个测试方法全部发现。594 个完整 fixture ID 与 TRX StdOut 精确一致（102 正例、492 反例）。 |
| TS ipqc.test.ts | 130 条逐项 PASS，0 failed / 0 skipped；23 roots 的 69 项完整对象检查，5 原 publication + 18 手写 fixture，26 个事件路由用例。 |
| Web type-check | npm run type-check → vue-tsc --build，原始 exit 0。 |
| 固定向量 | NCR 20 ID / 28 字段断言，IPQC 6 ID 各执行；仍是 FIELD_SUBASSERTION_ONLY / TYPE_BINDING_ONLY，未外推业务验收。 |

原始环境：.NET SDK 10.0.302 / net8.0 / .NET 8.0.29、Windows 10.0.26200；Node 22.22.0 / npm 10.9.4。C# TRX 时间为 2026-10-13 01:45:56–01:45:58 -04:00；TS 为 05:46:03–05:46:34 UTC。执行时 HEAD 是审查基线且任务文件未提交；110 个候选 Git blob 与工作字节一致，C# 15 份代码/依赖和 14 份记录产物、TS 49 份输入及 3 份日志 hash 均匹配。没有把候选提交后的证据复用改称重新运行。生成产物/原始日志有 hash，原证据未固定执行 DLL hash，因此不声称独立重现二进制身份。

本次只执行来源/生成 --check、Git 只读差异/字节和 Python/XML/JSON 证据核对。报告脚本的 GBK 输出、xUnit 截断显示名和 Python 3.8 写文件参数问题已修正，均非产品测试失败。完整命令、时间、计数、hash、绝对证据路径与全部 changed paths 见 [S01-07-original-validation.json](./original-validation.json)。

只读确认现有 QualityInspectionService / DefectRecordService、控制器和 Web 继续使用旧 DTO，15 份消费者源码 hash 匹配。真实 Owner 授权/currentness、Tenant 隔离、跨对象规则、原子写入、HTTP/前端接线、数据库/迁移/生产均 NOT_RUN。原 50 IPQC AC、247 运行场景、41 REFERENCE_ONLY、ITER/REL 缺原件、WP05 PROPOSED 不因本次结果升级。后续数据库仅 PostgreSQL。

未执行 Git 写、业务数据库、网络发布或 Actions 查询/触发/重跑/取消，未改变触发配置。远端当前状态不由本审查证明；COORD 负责共享状态、最终 workflow guard、保护合并和远端 main 包含确认。

全部 changed paths 摘要（均新增 A，逐文件 SHA-256 见机器报告）：

| 范围 | 文件数 |
|---|---|
| C# contract boundary | 9 |
| TypeScript contract boundary | 7 |
| Task delivery and original evidence | 46 |
| C# contract tests | 1 |
| Import/generator/fixture/evidence tooling | 15 |
| Immutable source/protocol/Git pin | 32 |

| 路径 | 摘要 |
|---|---|
| CP6.Entity/Contracts/Quality/.gitattributes | C# contract boundary |
| CP6.Entity/Contracts/Quality/Consumer.g.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/NativeContractCatalog.g.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/NcrFieldMetadata.g.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/NcrJson.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/OwnerWire.g.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/QaNcrMainR6Declarations.g.cs | C# contract boundary |
| CP6.Entity/Contracts/Quality/README.md | C# contract boundary |
| CP6.Entity/Contracts/Quality/SourceQA.g.cs | C# contract boundary |
| cp6.web/src/contracts/quality/.gitattributes | TypeScript contract boundary |
| cp6.web/src/contracts/quality/ipqc.g.ts | TypeScript contract boundary |
| cp6.web/src/contracts/quality/ipqc.test.ts | TypeScript contract boundary |
| cp6.web/src/contracts/quality/ipqc.ts | TypeScript contract boundary |
| cp6.web/src/contracts/quality/ipqcSchemas.g.ts | TypeScript contract boundary |
| cp6.web/src/contracts/quality/schema.ts | TypeScript contract boundary |
| cp6.web/src/contracts/quality/strictJson.ts | TypeScript contract boundary |
| docs/development/evidence/S01-07/.gitattributes | Task delivery and original evidence |
| docs/development/evidence/S01-07/DELIVERY.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/OWNER-CONSUMERS.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/PLAN.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/REVIEW.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/README.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/environment.txt | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/first-codec-gate.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/native-final-gate.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/native-format-final-gate.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/native-gate.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/node-regex-dialect.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/quality-contracts-final.trx | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/quality-contracts-format-final.trx | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/quality-contracts.trx | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/red-declaration-boundary.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/red-full-native.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/red-regex-dialect.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/red-rfc3339-format.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/red-writer-identity.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/csharp/verification.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/final-integrity.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/fixed-vectors.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/owner-consumers.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/scope-review.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/00-npm-ci.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/01-red.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/02-native-tests.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/03-native-tests.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/04-type-check.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/05-final-0.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/05-final-1.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/05-harness-failure.md | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/06-final-0.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/06-final-1.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/06-final-2.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/06-final-validation.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/07-final-0.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/07-final-1.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/07-final-2.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/07-final-validation.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/08-date-regression-0.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/08-date-regression-1.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/08-date-regression-2.log | Task delivery and original evidence |
| docs/development/evidence/S01-07/ts/08-date-regression-validation.json | Task delivery and original evidence |
| docs/development/evidence/S01-07/workflows.json | Task delivery and original evidence |
| eng/contracts/exact-values-tests/QualityContractTests.cs | C# contract tests |
| eng/contracts/s01-07/.gitattributes | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/README.md | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/check_workflows.mjs | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/capture_evidence.py | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/full-object-fixtures.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/generate.py | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/generation-manifest.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/make_fixtures.py | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/csharp/native-field-inventory.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/import_sources.py | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/inputs/composed/composed-15.cs.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/composed/composed-16.cs.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/composed/composed-17.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/composed/composed-19.ts.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/NCR-R6-FENCED-OWNER-SHAPES.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/NCR-R6-MES-DISPATCH-SHAPE.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/NCR-R6-WIRE.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/NCR-R9-IQC-SOURCE-PROJECTIONS.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/consumer-cp4-r1/CONSUMER-WIRE.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/consumer-cp4-r1/references/owner-addenda-cp2/CONTRACT-SCHEMAS.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/consumer-cp4-r1/references/owner-addenda-cp2/source-contracts/SOURCE-QA-SCHEMAS.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/finance-r10/CLOSED-WIRE-SCHEMA_R10.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/initial-sources/QA-IPQC-01/SCHEMA_REGISTRY_RECOVERY_R4_SYSTEMIC.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/native/design/references/initial-sources/WMS-RMA-01/WMS-RMA-01_CANONICAL-CONTRACT.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/original/composed-15.cs.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/original/composed-16.cs.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/original/composed-17.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/original/composed-19.ts.txt | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/2026-10-11-s01-task-breakdown.md | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/bounded-patches.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/composed-derivatives.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/container-members.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/profile-inputs.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/required-inputs.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/s01-input-index.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/pins/vectors.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/ACCEPTANCE.md | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/CANONICAL_CONTRACT_RECOVERY_R4_SYSTEMIC.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/LANGUAGE-CONTRACTS.md | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/NCR-R6-LANGUAGE-CONTRACTS.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/PAYLOAD_PUBLICATIONS_RECOVERY_R4_SYSTEMIC.json | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/inputs/protocol/QA-IPQC-01-SPEC.md | Immutable source/protocol/Git pin |
| eng/contracts/s01-07/sources.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/ts/generate.mjs | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/ts/generation.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/ts/manual-fixtures.json | Import/generator/fixture/evidence tooling |
| eng/contracts/s01-07/ts/validate.py | Import/generator/fixture/evidence tooling |
