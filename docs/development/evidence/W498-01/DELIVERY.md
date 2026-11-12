# W498-01 询盘 C# 集合所有权

承接 W487 已验收的 20 个命名列表和三种 UI 集合，核对 R-054 所指的 H01 Calendar、LeadCreationReceipt 及签名授权集合，再验证三类源事件的 envelope 与 data 扩展。现行类型解析读取生成注册表，签名信任入口负责复制 `PublicKey` 并拒绝空项。 [W487](../W487-01/DELIVERY.md)

修前 `collections-red-01.trx` 五项失败。对 `Extensions` 的读取暴露原字典，外部清空改变实际编码。`with` 也保留调用方字典和原 `JsonDocument` 的生命周期，null 字典及 null/undefined 项可进入 CLR 对象。 [CRM Issue #187](https://github.com/GTX537/CP6.CRM/issues/187)

在 S01 生成入口为六处扩展对象生成私有序列化存储。赋值复制字典和 JSON 值，公共读取返回副本。首版的 `JsonIgnore` 占用了合法布尔扩展名 `extensions`，三个往返用例揭示问题。生成器将被忽略的 CLR 成员绑定到既有租户字段名，保留合法扩展键及严格解码顺序。

| 本地 .NET 8 证据 | 实际结果 |
| --- | --- |
| `collections-green-02.trx` | 260 项通过，三个源事件的 `extensions` 往返失败，0 跳过。 |
| `collections-wire-03.trx` | 受影响的五项通过，含三类事件及 data、大小写与扩展键、`with`、别名与 JSON 文档释放、空项，0 跳过。 |
| `scripts/import-s01-contracts.py --check` | 24 个已固定输入、38 个定义和 17 个生成输出通过。 |

两轮最终覆盖 263 个不同用例，包含原列表、UI、签名权限和严格 wire。日志、命令和原始失败保留在 `D:/CP6-test-artifacts/W498-01`。本次属于合同库验证，适用范围为实际生成的 C# 类型、JSON 和签名边界。

CRM #188 已合入 `42fea57e387aa0b4dfc99ae4fc85afb8b29f786d`，已核对采用源 `4e28f52b6b091ffaab077142919b33fb6c75624e` 和相同源码树。一次集中审查覆盖生成差异、合法输入、原发布件 pin、扩展值和信任边界。CRM 本地与远端任务分支已定向清理，DEV-2 干净且已释放。主仓收尾见本窗口 PR。 [CRM PR #188](https://github.com/GTX537/CP6.CRM/pull/188)

采纳 AC-INQ-054，原询盘 45 PASS / 15 NOT_RUN。后继继续核原接口映射、ACT 复用边界、生产采用状态及其余运行验收。Actions 运行与取消均为 0，部署 0。
