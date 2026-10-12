# S01-09 集中合并前审查

结论：**可以进入协调串行合并窗口；未发现阻塞正确性、合同、兼容性或证据完整性的问题。** 本结论针对候选代码及本地合同验收，不表示已合入远端 main 或业务验收完成。

- 审查日期：2026-10-12。
- Platform 基线：`30bd23af6808d217a23878bd9437513043c52834`。
- 实现提交：`708253360269396a43c2b25cce1c87cd9f59f9d2`。
- 最终候选：`753696c8333325d9faa5d80ea429ab8b9480e71c`，工作树干净。
- 本地 `main`、`origin/main` 均为上述基线；未联网刷新，远端包含性由协调合并流程确认。
- 输入 Main：`dfc043b1473dcea51d34ff87a7808692349c278a`。另读取当前 `D:/CP6/docs/development/dispatch/20261012-wave2/S01-09.md` 与 `dispatch.json`；派工文件晚于该输入基线，未将其冒称为该提交内文件。
- 已读取 Main AGENTS、原 S01-09 任务计划及 requesting-code-review 技能，按一次任务级审查执行。候选工作树未发现自己的 AGENTS.md。

## 审查发现与兼容性

无必须修复项，无需为纯风格改变本候选。

1. [Cp6NativeContractCatalog.cs:28](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/NativeContracts/Cp6NativeContractCatalog.cs#L28) 先复制并验证固定 manifest 字节，再解析包版本和三个原生身份。`ResolveOpenApi` 比较 OpenAPI、title、version、schema root、hash 的完整 record，并对实际 schema bytes 再算 SHA-256；`ResolveRegistry` 同时比较指定 profile 的 codec namespace、registry filename、hash，并验证实际 registry bytes。比较没有忽略大小写、latest 或跨 current/frozen 回退。profile 是调用配置，没有新增原生 envelope 字段。
2. [Cp6ContractBundle.cs:45](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/Cp6ContractBundle.cs#L45) 新增显式 bundle version/manifest SHA256 pin，同一份已校验 manifest bytes 被解析。原单参数入口保留。既有 eventType/schemaId/schemaVersion 三元组匹配、Schema 元数据、Schema/example hash、五例矩阵及禁止同包重复 eventType 的规则保留，没有宣称它们是新功能。没有实际消费者要求本次扩展 CloudEvent 多版本 adapter。
3. [Cp6OriginalJson.cs:21](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/NativeContracts/Cp6OriginalJson.cs#L21) 复制并严格解析原输入，SHA256 来自该原始副本；`GetBytes` 返回防御性副本，`Matches` 使用字节比较。CRLF、Unicode、空白、键序及数字拼写不会被重构。输入和返回值的突变有直接测试。
4. [Cp6NativeJson.cs:19](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/NativeContracts/Cp6NativeJson.cs#L19) 在解释前以异常型 UTF8 解码器检查整个字节流，禁注释和尾逗号；递归按解码后的属性名做 ordinal 去重，覆盖转义等价键及数组内对象。自有副本保证 JsonDocument 不借用调用者可变内存。
5. [Cp6NativeJson.cs:43](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/NativeContracts/Cp6NativeJson.cs#L43) 的必填 null 投影同时核对对象根、属性存在与 Null token。语义相等与原字节 API 分离；私有 key 使用类型标签、对象键排序和保持数组序，数字使用 BigInteger 系数和指数，未经过 double/decimal。实现与 Main 固定 `DesignRevisionTypes.cs` 的语义一致，未依赖待交付的 S01-02 代码。
6. [Cp6CloudEventValidator.cs:34](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/src/CP6.Platform.Messaging/Cp6CloudEventValidator.cs#L34) 让完整验证及解码使用同一份已拥有的输入。P08 根层 `traceparent`/`tracestate` 重复例外只由内部 `ParseCloudEvent` 开启；递归进入属性值后立即回到严格模式。现有 `Cp6TraceContextCodec` 仍统计重复根 tracing 并返回无父上下文。保留的 TraceContext/Dapr 消费回归覆盖了业务继续接收、重复 tracing 丢弃路径；native 根及嵌套业务重复不获例外。
7. 新解析器保持 `MaxDepth = 64`，与旧 `Cp6CloudEventValidator` 和 tracing parser 相同。此次以源码差异核对这一兼容边界，没有额外执行深度边界测试或将其声称为新测试结果。

## 固定来源与执行证据

只读执行了 `verify-delivery.py` 的核验逻辑：内存加载脚本，并拦截、捕获它对 `validation.json` 的唯一 `Path.write_bytes` 调用。原文件字节未变化，未直接调用会覆盖证据的脚本入口。

独立核验五个导入文件的工作树、已提交 blob、Main 固定来源和对应原生归档/组合资产；同时核对 selector 的固定 Git blob/hash 和 patch bundle hash。结果全部一致：

| 文件 | SHA-256 |
|---|---|
| profile-inputs.json | `2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e` |
| vectors.json | `b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01` |
| crm-inq.openapi.json | `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349` |
| erp-current.registry.json | `564706ce0247d84dc8056002d540f17eccdcab7c5b5e4b123c09121ad407fbda` |
| erp-frozen.registry.json | `62effa1e0542784a5fa936d11961465db4da522ed4d28fc9c9bb5351cf40b6f1` |

固定语义设计资产 SHA256：`a7ab417cabf201e8ec7b716360539b18fc0db751d180bb05b0b18f7dc3a3c3db`。测试 fixture 使用 scoped `-text` 属性保持原字节；测试运行只读自己的输出资源，没有 D 盘归档运行依赖。

复用 [task-pass.trx](https://github.com/GTX537/CP6.Platform/blob/753696c8333325d9faa5d80ea429ab8b9480e71c/docs/development/evidence/S01-09/test-results/task-pass.trx) 原运行：2026-10-12 07:00:45.2634897 至 07:00:48.0769886（UTC-04:00），110 executed / 110 passed / 0 failed / 0 skipped。该文件 SHA256 为 `ca0f47ac800a12c65202d28a4cc41ffb76c1aa28a8568aafaa46c7bc52991290`。

- CloudEventContractTests 21；NativeProfileContractTests 53；MessagingTelemetryTests 7；TraceContextContractTests 13；DaprKafkaContractTests 14；TransactionalMessagingTelemetryTests 2。
- 35 个固定 case ID 恰好各出现一次：JSON-001..007、NULL-001..007、EQUAL-001..007、INQ-VERSION-001..004、VERSION-001..008、BYTES-001..002。逐项读取原 input/expected 后调用生产边界；未知 operation 明确失败，没有动态生成预期或静默跳项。
- 其中 strict/null/equality 21 项；原生身份/原字节 14 项。BYTES 明确是构造的合同单元输入，没有冒称真实命令或重放成功。
- 当前源码/测试/合同输入与实现提交无漂移。实现到最终候选的增量仅为本任务的证据/说明；测试项目变动仅有 TestData/S01 复制设置，没有包版本、TFM 或共享构建配置改变。
- 当前 Messaging DLL：`33f16c8939371bfdfb96cf9e13826a005f0540ee61fa2873651b8c2e19e24b42`；UnitTests DLL：`f374665ecce657139fd292fa8a954eaf1fb6d93d14a2c0a75f6842473a437fc4`。均等于保留的 validation.json 产物 hash。
- 原执行 HEAD 是基线加当时工作树修改，随后原样提交为实现 SHA；README/validation 如实记载这一关系。此次复用旧测试结果，不将新提交或审查时间改称新测试执行。
- 历史失败完整保留并与记录一致：red 49 failed，red-bundle 3 failed，green-initial 3 failed，red-utf8 1 failed，consumer-regression 1 failed。已读取关键失败信息，分别包含 API scaffold、缺少 manifest/duplicate 规则、过窄异常断言、lazy UTF8 漏检和 P08 tracing 回归；最终相关病例全部成功。早期 contracts-pass 的 72 项成功保持为早期范围。

当前只读核验还确认：全 diff 36 文件均在派工写入范围；`git diff --check` 无问题；候选与本地 origin/main 的八个工作流内容吻合作者证据，七个仅手动，RFC3161 的两个准确 PR paths 均不在本任务 diff。没有网络调用，没有启动/取消 Actions，没有工作流变更，没有 Git 写操作或作者工作树变更。唯一写入为本审查报告。

## 合并建议与未验范围

推荐按正常保护路径合并此候选。协调者继续负责合并前当前远端/工作流复核、Main 台账与四份状态文件同步、必要合并后检查及远端 main 包含性；本报告不替代这些步骤。

未重跑任何 dotnet 测试、全仓构建或远程检查。未验证真实 Owner 权限、Inbox/Outbox 原子性、数据库持久化/迁移、完整 INQ/ERP codec 或命令合法性、真实冻结命令重放、下游 NuGet/runtime 采用、包发布或部署；候选文档也将这些保持为 NOT_RUN。PostgreSQL 后续基线未受本任务影响。
