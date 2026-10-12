# S01 精确合同输入包

包版本为 `CP6-S01-CONTRACT-INPUTS/1.0.0`。本目录供 Main、CRM、Platform 的后续合同测试读取；它冻结来源、字段所属 profile 和共用预期，不实现业务写入或宣布消费者采用通过。

- [profile-inputs.json](profile-inputs.json)：Owner/消费者、原件与组合派生 SHA-256、完整补丁集合、原生 Schema/类型身份、字段绑定和原验收引用。
- [vectors.json](vectors.json)：输入和预期；`execution=NOT_RUN`。原 23 条数值/单位向量使用 `NUM-001..023`，每条保留准确源 JSON pointer。这个编号是本包索引，原资产没有原生 case ID 的条目仍为 `source_case_id=null`。
- [交付证据](../../../docs/development/evidence/S01-01/README.md)：本地结构、来源、链接和差异检查，独立于业务执行结果。

## 消费方式

1. 同时读取两个 JSON，严格匹配 `package_version`。按 `profile_id` 选择向量，再按 `operation` 调用本仓已实现的测试入口；未实现的操作明确记为未运行，不能静默忽略后声称整个 profile 通过。
2. 生成代码或采用 Schema 时，使用 `selected_sources` 指定的 `source_id` 和 `variant`。`original` 核对 `original_sha256`，`composed` 核对 `composed_sha256`，并加载完整 `patch_bundles`；不得按文件名或最近修改时间挑选。
3. `selector`、`source_ref` 和验收引用中的 `path` 均相对仓库根，JSON pointer 遵循 `~0`/`~1` 转义；`line` 是一基行号。`hash_basis=git_blob_bytes` 的仓库来源 pin 约束 `git cat-file blob <git_blob>` 的准确字节，不能对可能经 CRLF 转换的工作树文件直接比较；核验器同时检查工作树经 Git 过滤后的 blob 一致。新包 JSON 固定 LF，`file_bytes` pin 及所有原件/组合派生摘要始终计算原始文件字节。归档路径只由证据索引在生成/核验时解析；消费者运行时不依赖开发机目录。
4. 数值 wire、任意精度系数、原始 JSON 和回放文本均保留字符串表示。只有 scale、计数等元数据使用 JSON number。不要先转成 C# decimal、JS Number 或浮点再喂给测试。
5. 将本仓真实运行结果另存证据，记录源代码 SHA、包与文件 hash、语言/运行环境、实际执行的 case ID。不要往本包写入 PASS，不把源资产的历史 PASS 当成本次结果，也不要用一端实现动态生成另一端预期。

## Profile 边界

| Profile | 固定边界 | 后续任务 |
|---|---|---|
| `fin-credit-signed/nonnegative/positive` | 每个字段使用原符号域，38 总位/最多 8 小数；系数与 scale 无损表示 | S01-03/05/10 |
| `fin-inv-decimal18/qty12` | 整数位不加上限，小数分别 18/12；原 `-0.000` 与尾零单独保留 | S01-04/05/10 |
| `atp-stableid-r1` | 明确后继 `ATP-STABLEID-LEXICAL/20261011-R1`，ASCII 1..128，末尾 LF/CRLF 拒绝 | S01-06/10 |
| `strict-json`、`null-only`、`json-semantic-equality` | 任意层重复/转义等价键拒绝，必填 null 与缺失不同；对象键序不影响结构相等，数组顺序影响 | S01-02 |
| `ncr-main-r6`、`ncr-cp4` | 固定当前组合中指定生成附件；七个 required const-null 字段及准确 nullable 元数据 | S01-07 |
| `ipqc-r4` | QAP 的扁平 Evidence 与原生 Uom；不能替换成 ExecEvidence | S01-07 |
| `crm-inq-r3-v15` | 准确 OpenAPI/生成附件组合，引用原 AC-INQ-009/048/053 的合同子断言 | S01-08/09 |
| `crm-lead-cp20` | 原文件名 CP20、实际命名空间 CP21；当前完整组合另有准确引用，ownerId 按具体字段的 Schema 判定 | S01-08 |
| `erp-credit-current-v1`、`erp-credit-frozen-r8` | 分别匹配准确注册表/codec；原生 Money 独立于 FIN-CREDIT，冻结命令按原字节保留 | S01-11/09 |

ATP 的 PostgreSQL DDL 同时包含 PLAN-IF-004 和 PLAN-IF-005，不能只取长度补丁。数值存储只采用 PostgreSQL 适用部分；历史 SQL Server 资料保留来源身份，不构成本轮新验收要求。

Owner 字段说明对应原生合同责任；生成附件中的跨域 Ref/Scope 仍由其原 Owner 定义。共享辅助类型没有业务 Owner，不授予任何数据写权。包版本仅标识本目录格式，不是向原生封闭 envelope 新加的字段。

## 向量操作和适用层级

| operation | input / expected | 适用范围 |
|---|---|---|
| `lexical` | 原 wire 字符串；accepted 及可选精确 pair | 字段词法和数值表示 |
| `strict_json_parse` | 原始 JSON 文本；accepted | 严格读取边界 |
| `required_null_property`、`required_null_field_projection` | raw_json、required_property；accepted | 必填 null 的单字段投影，后者不验证整个业务对象 |
| `required_owner_id_field_projection` | raw_json、required_property；accepted | 指定 LEAD required 字段，不能套到可选字段 |
| `json_semantic_equality` | 两份原始 JSON；equal | const/enum/uniqueItems 语义，不能用作摘要规范化 |
| `native_type_binding` | supplied_type；accepted | 准确类型接线，不能替代全对象 decoder |
| `native_schema_identity`、`native_registry_identity` | 准确版本/名称/hash；accepted | 采用工具的路由选择，不新增原生 wire 字段 |
| `original_utf8_retention` | original_utf8、replay_utf8；逐字节与 SHA-256 预期 | 构造的合同单元输入，仅验证原字节保持，不宣称业务命令合法或真实重放成功 |

`CONTRACT_UNIT` 和 `CONTRACT_ROUTING` 表示后续测试层级；`FIELD_SUBASSERTION_ONLY` 与 `TYPE_BINDING_ONLY` 必须保留到结果报告。未知版本、Schema 或 hash 应拒绝，不能回退到较宽松入口。

INQ 在本包提供准确来源/版本路由向量；完整字段一致性、boolean extension decoder、Unicode code point、权限投影、下载 proof/session/ETag、终态页面及 H01 仍由后续任务实施验证。原验收及修订 generated adoption 场景这里只引用适用部分，整条场景保持 NOT_RUN。同样，NCR 字段测试不证明 QA 业务通过，财务精度测试不证明真实 Owner 已采用。

## 本地复核

使用 Python 3.10+，在仓库根执行：

```powershell
python docs/development/evidence/S01-01/build-vectors.py --check
# 如需逐项读取准确原归档，追加 --verify-native-fields
python docs/development/evidence/S01-01/verify-package.py --out <新的核验报告路径>
python docs/development/tools/manage_roadmap.py
```

完整来源核验需要本机已有准确原归档及组合派生缓存；缺少某项时保留明确缺口，不借历史 PASS 放行。包的 JSON/向量读取本身不需要这些归档。核验不编译业务、不执行归档脚本或 SQL、不访问业务数据库、不触发 GitHub Actions。
