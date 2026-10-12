# S01-02 集成前集中审查

结论：**建议合并。无可操作的 P0/P1/P2 阻止项。** 本次结论针对 S01-02 合同边界工具及其测试、证据，不代表业务消费者、数据库或生产验收完成。协调方仍须按正常保护流程完成共享状态同步、合并和远端 main 包含性核对。

审查日期：2026-10-12。按协调方请求，独立执行 requesting-code-review 的一次任务级集中审查。只读工作树 `D:/CP6-worktrees/s01-02-20261012`；唯一写入为本报告。未修改仓库、未运行 Git mutation、未访问远程、未触发或取消 Actions、未重新编译或运行测试。

| 身份 | 固定值 |
| --- | --- |
| 评审基线 | `4ab979e2f391c5a5645da0d1586781e824a9f8cd` |
| 候选 HEAD | `3890b4fdbda47c3762b9f51ea75b8c156540c69f` |
| 实现及原始证据提交 | `2c0ef2129012a8117b0b5b058ac3c470e12d73fd` |
| PR | 协调方指定 draft #184；本审查不查询远程状态 |
| 输入包 | `CP6-S01-CONTRACT-INPUTS/1.0.0`，3 个 profile，21 个原固定向量 |

已读取根 AGENTS.md、第二轮 S01-02 执行包、原任务卡、输入包说明与对应 profile/向量、DesignRevisionTypes.cs/DesignTypesCheck.cs；复核了完整候选变更范围及全部生产源码、测试、项目配置和证据。相对基线的 31 个新增文件均在授权的 Wire、exact-values-tests、S01-02 evidence 三个目录。实现提交之后仅 README、delivery、source-receipt 三份说明/回执变化；没有源码、依赖、工作流、共用输入或共享状态变更。`git diff --check` 返回 0，候选工作树干净。

## 行为与合同核对

- **严格 JSON 顺序正确。** [StrictJson.cs:20](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/CP6.Entity/Contracts/Wire/StrictJson.cs#L20) 先复制原输入，完成有效 UTF-8、JSON 语法和逐对象重名检查，再创建文档/调用目标转换器。[StrictJson.cs:43](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/CP6.Entity/Contracts/Wire/StrictJson.cs#L43) 的对象栈覆盖数组内对象和任意对象嵌套，各对象各自维护 ordinal 名称集合；数组不会误建共享属性作用域。名称经完整转义解码，原字符与 `\u`、`\/` 等等价拼写相遇会被拒绝。孤立 surrogate 按 code unit 区分，不以替换字符造成碰撞。
- **必填与 null 值职责分开。** [NullOnly.cs:10](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/CP6.Entity/Contracts/Wire/NullOnly.cs#L10) 仅接受 null 并写出 null；[StrictJsonTests.cs:230](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/eng/contracts/exact-values-tests/StrictJsonTests.cs#L230) 用 `required` 元数据证明缺失拒绝，单独的 optional carrier 证明不是由 converter 误判存在性。7 个 NULL 固定输入包含显式 null 接受及缺失、0、1、false、字符串、对象拒绝。
- **精确相等没有数值窄化。** [JsonSemanticEquality.cs:59](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs#L59) 保留十进制有效系数字符串，以 BigInteger 调整指数，不经 decimal/double，不展开巨大幂。负零统一；小数位与尾零指数调整正确。对象按已解码名字排序、数组保留顺序；类型标签与长度帧确保属性名/值/容器边界不会因分隔符内容发生混淆。外部 JsonElement 也须重新通过严格重名检查。
- **原报文独立保留。** [StrictJson.cs:115](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/CP6.Entity/Contracts/Wire/StrictJson.cs#L115) 返回原 UTF-8 的防御性副本，输入方修改原数组或消费者修改返回数组均不改变内部报文。语义 key 没有接入摘要、签名、Owner digest 或重放路径；代码与文档均明确它只用于 Schema 相等。
- **合法输入边界没有新增业务限额。** reader/document 默认深度显式为 Int32.MaxValue，语义遍历非递归；深度 2048 和巨大正负指数有实际测试。严格层保持大小写、Unicode code-unit 差异，不做 Unicode 正规化。目标类型/自定义 converter 与调用者显式非零 MaxDepth 的限制有披露，未伪装成通用 Schema 验证。

## 已独立核验的证据

本审查执行了只读 Python/Git blob/hash/XML 检查，结果如下；这些是本次证据检查，不是重新执行 77 项业务测试。

| 检查 | 结果 |
| --- | --- |
| entry-check 七份来源 SHA-256 | 全部匹配候选 Git blob，且与基线 blob 相同 |
| 三个相关 profile 的支持资产、规则、验收引用 | 3 个去重后的 SHA-256/Git blob pin 全部匹配候选路径 |
| green 五份源码/测试项目 SHA-256 | 同时匹配实现提交、候选提交和当前工作树；与 source-receipt 相同 |
| green 三份现存制品 SHA-256 | 测试 DLL、CP6.Entity DLL、runtimeconfig 全部匹配 |
| 测试输出目录 Inputs | vectors.json、dispatch.json 均与当前仓库原文件逐字节相同 |
| 固定向量集合 | dispatch、entry-check、三个 profile 的原 vectors 集合相同，21 个 ID 无缺漏 |
| green.trx | 77 执行、77 Passed、0 Failed、0 NotExecuted；21 个原 ID 全部实际出现 |
| red/red-final TRX | 各 77 执行、76 Failed（均含 NotImplementedException）、1 Passed；原 21 个 ID 均在场 |

复用来源为 [green-metadata.json](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/docs/development/evidence/S01-02/implementation/green-metadata.json)、[green.trx](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/docs/development/evidence/S01-02/implementation/green.trx) 和 [green.log](https://github.com/GTX537/CP6/blob/3890b4fdbda47c3762b9f51ea75b8c156540c69f/docs/development/evidence/S01-02/implementation/green.log)。原执行时间为 `2026-10-12T10:51:40.2444090Z` 至 `10:51:45.0487096Z`；SDK 10.0.302、目标 net8.0、实际测试运行时 .NET 8.0.29。测试时源码未提交，以逐文件 hash 确认适用范围；没有把候选 SHA 冒称为当时执行 HEAD。

21 项原向量直接读取冻结预期；56 项补充测试涵盖非法语法/UTF-8、解码重复名、转换器前置扫描、选项隔离、required/optional 区别、原字节所有权、2048 层深度、极大指数、超过 binary64 精度的相邻整数、Unicode/类型/分隔符边界。测试项目仅引用 CP6.Entity 并使用任务指定的 Test SDK/xUnit 版本。未修改原包 NOT_RUN 或生成实现导出的动态预期。

## 适用范围与剩余验证

没有足以要求重跑既有成功结果的新风险或输入变化，因此按本仓验证策略复用上述执行。没有新的补充探针，也没有将既有独立审查的临时探针记为本次运行。

Key 的深层容器重复复制可能导致较高内存/时间开销；实现报告已准确披露，未承诺经过吞吐或资源耗尽基准测试。部署入口预算属于后续采用，本任务没有通过新增限制缩小原合同合法域。调用者必须保留 StrictJsonDocument 生命周期，并为业务必填字段声明正确的 required/JsonRequired 元数据。

本次没有验证 CRM/Platform 实际采用、业务 Owner 写入、权限/租户/事务、页面、PostgreSQL 存储/迁移、性能或生产发布。未查询当前远端 main、PR 保护状态或 Actions 状态，合并前远程与触发器复核由协调方执行。本地边界工具审查支持合并；不应据此登记跨仓采用或完整业务场景通过。
