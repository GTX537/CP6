# WP2：字符串语义真库对照

状态，2026-10-03：**文本 PG46/46、SQL22/22按未变四consumer hash复用；WP2本地映射/迁移/真实初始化门禁LocalVerified，8个owned库清理/不存在核对完成，远端交付Pending。** 当前task HEAD为main `94c0f8c9cac63a72008c4e6b242c94358f25b305` 加未提交WP2输入；本文原报告SourceBase仍d607、SourceState仍是当时未提交源码，不改称当前提交执行。PG36/36与另一次报价PG2/2、最终SQL升级2/2/full35/35、真实两库首次/重复种子保留各有独立来源，详见[实施记录](WP2-IMPLEMENTATION.md)；BUG137/139/141/143均Closed，完整业务验收在WP3–WP6。工具为 [TextProbe](../../../tools/CP6.DatabaseCompatibility.TextProbe/README.md)。文本样例在新建且核对Task/Owner的本机临时库执行，探针对象/数据事务回滚并确认不存在。

SQL Server 实际默认排序规则为 `Chinese_PRC_CI_AS`，并非假设的 Unicode SC 排序规则。真实查询确认大小写、宽度及 NFC 等价、重音区分、U+0020 尾空格参与 PAD SPACE；LEN 将 supplementary 标量按两个 UTF-16 单元计数，但剔除尾空格。PostgreSQL 默认 varchar 即使加 ICU CI/AS 仍不能保留上述键语义。

| 实际运行 | 结果 | 适用范围 |
| --- | --- | --- |
| 初始 SQL text 参考 | 15 Passed | 早期断言未包含固定 padding/完整 LEN 对照，由下行扩展证据取代 |
| 可恢复的初始 PG RED | 11 Passed / 5 Failed | 16 项实际检查；普通 varchar 的参数、数组、FK/unique 尾空格差异 |
| 初始 adapted PG | 16 Passed | 早期输入存在 LEN 标量数与空格数碰巧相同的断言缺口；已扩展为双尾空格和 TrimEnd 后长度，不作为最终 LEN 证明 |
| 扩展 SQL 参考 | 19 Passed / 0 Failed | 原始值、固定 ASCII padding、参数/数组、排序、字符串操作、UTF-16 LEN、FK、unique、rollback cleanup |
| 扩展 PG RED | 30 Passed / 7 Failed | 相同编译工具；明确暴露 scalar/array PAD SPACE、UTF-16 LEN、FK/unique 差异；数据库/排序规则对象回滚成功 |
| 扩展 adapted PG | 37 Passed / 0 Failed | 同一 executable/input；另含 CP936 七个原生边界、十个 linguistic/NULL 断言和 collation cleanup |
| 后续 ORM / operation-result PG | 45 Passed / 0 Failed | build-8；增加真实 EF key comparer/fixup、C key 大小写区分、普通值 case-only 修改与操作结果 collation，不覆盖下一行显式 empty-range 断言 |
| 最终 SQL 参考 | **22 Passed / 0 Failed** | `wp2-text-sql-reference-orm-boundaries.json`；原生参考另含 concat/substring 的 case/width/NFC 操作结果比较；不把 PG ORM 五项记成 SQL 已执行 |
| 最终 adapted PG | **46 Passed / 0 Failed** | `wp2-text-pg-adapted-orm-and-empty.json`；build-9，同一 executable，包含显式 empty-range、真实 EF tracking/fixup/hash、两原生 C keys 同 context 共存与 case-only 值写入 |

采用 [consumer type mapper](../../../CP6.Core/Persistence/PostgreSqlTextTypeMappingSource.cs) 为 unbounded `bpchar` 及固定 `character(n)` 绑定 Npgsql Char 参数，保留 CLR string 与 MaxLength metadata，禁止 mapper TrimEnd 和参数 Size 截断。普通字符串的变量容量由独立 check 控制。字符串操作使用 [consumer SQL generator](../../../CP6.Core/Persistence/PostgreSqlTextQuerySqlGenerator.cs)，通过 `convert_from(pg_catalog.bpcharsend(column),'UTF8')` 读取完整文本，避免隐式 cast 丢尾空格；LEN 另外按 SQL 的 UTF-16/U+0020 合同计算。

[ManagedTextV1](../../../CP6.Core/Persistence/PostgreSqlManagedTextV1.cs) 提供需纳入迁移的版本化函数。`cp6_text_range_v1` 按已验证 ICU 排序逐标量比较 SQL grammar 的 hex/alpha 范围；确认 source 接受 `A/Ｆ/ä/é/ć`、拒绝 `ß/g/space`，currency CI 接受 `usd`，NULL 保持 unknown。此证据不声称两个排序引擎在所有语言/所有 Unicode 上完全相同。`cp6_cp936_length_v1` 用 GBK 字节和 CP936 Euro 特例、不可编码 UTF-16 fallback 单元保留长度容量；实际 SQL 对照 emoji/rare Han 为两个 `?`、Euro 一个字节、中文/全角两个字节。容量函数不会让 PostgreSQL 数据静默变成问号；非 ASCII 的固定字符 padding、写入替换与全部 caller 输入范围仍需真实模型/业务门禁，不能以这些样例证明全域等价。

固定字段现由 [83 项精确 ASCII manifest](../../../CP6.Core/Persistence/PostgreSqlFixedTextDomainsV1.cs) 限定支持域（Core 六项、Space 77 项；Identity/ERP 零项，无 nchar）。未知 fixed/nchar 或容量不符会拒绝模型构建；该规则不推广到普通 variable text，也不声明非 ASCII fixed padding 与 SQL 通用输入等价。现合法空 hash/NULL 不因 ASCII check 被全局改成 hex64 强约束。普通 UTC DateTime 与 DTO Offset=0 仍严格；Min/Max ticks 是明确保留的 sentinel 例外，PG microsecond 上限不等于 SQL 100ns 精度。

失败记录全部保留。第一次编译发生 `DbType` 实例成员名称遮蔽，修为限定类型；扩展编译发生变量 `value` 作用域冲突，已改连接变量名。首次 PG RED 的 FK 错误中止事务并导致 fixture 清理路径异常，没有生成有效 JSON；改为每 case savepoint、外部事务先回滚再销毁 context 后重新得到可恢复 RED/cleanup。一次扩展 build 失败后误运行了旧 binary 的 `pg-adapted-range` 报告，**明确排除为新源码证据**；实际有效扩展运行是 `pg-adapted-range-verified`，适用 build-5 success（0 warnings / 0 errors）。未删除失败或更改原始报告内容。

后续首次同时追踪两个合成 SQL BIN2 key 的 fixture 抛出 `InvalidOperationException`，真实失败保留；改为原生插入再由 PG context materialize，验证数据库中 C key 的共存。它没有证明既有 CP6 SQL 业务缺陷，也未授权改 SQL model。针对 provider 的定向审查修复了错误的统一 OrdinalIgnoreCase key comparer、字符串操作丢失 column collation、empty-range 返回 false 三项缺口；最终 46 项覆盖这些修复，普通值 comparer 保持精确比较以保存 case-only 修改。

原始本机证据仍位于未跟踪根 `D:\CP6\tmp`，各自同名 log。初期 `wp2-text-sql-reference-extended.json`、`wp2-text-pg-red-extended.json`、`wp2-text-pg-adapted-range-verified.json` 与 worktree `tmp/db-compat-wp2-tests/text/build-5.log` 保留；SQL 原生 CP936 对照为 `wp2-cp936-reference-verified.log`。最终 `wp2-text-pg-adapted-orm-and-empty.json` SHA-256 为 `2D6313D1AA88FCE4096442F8274DE2A046E43044A81B5C5B463CD2912C5A37BA`，`wp2-text-sql-reference-orm-boundaries.json` 为 `9B8D2DF932EF4B1CB81B4EFDD6DE550CD5AEBDDF19C162EA83CBD39590A4D282`。两报告记录 executable `E977424DE97C6B4BF7CECE0637ED453B3B61A79E2E138C60A23D527FE846156B`，对应 build-9（0 warnings / 0 errors）。当前 comparer / ManagedTextV1 / query SQL generator / type mapping source 四个源码哈希均与报告一致，因此直接复用此次真实成功，不因后续迁移/文档/HEAD 变化重复 text gate，也不改写旧 JSON。

独立历史fresh PG33/33及后来既有PG36/36、最终SQL full35/35各保留真实catalog/write范围；[Core manifest](WP2-CORE-MAPPING.md) 和[实施记录](WP2-IMPLEMENTATION.md)区分输入。这些运行不替代本文文本fixture，本文也不证明整个模型或完整业务验收。实际compiled两库first/repeat初始化已验证各自全表/历史和自定义种子保留；任务级30/45审查及定向修复复查已完成。当前8个owned库清理及不存在核对完成（[实际原件](wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)），原件已按142份[manifest](wp2-native/manifest.json)完成字节归档核对，WP2远端交付Pending；全文原失败、排除的旧binary及文本适用边界保持，不重复build或门禁。
