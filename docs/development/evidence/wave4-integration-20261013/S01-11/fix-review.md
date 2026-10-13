# S01-11 / Issue 196 定向修复复查

**ACCEPT（定向静态复查）**。未发现新增阻塞；S01-11-R1 的 Buffer 共享内存根因已被修复。此次只审相对 b27fbe5422bbe487efce2aa4de1bf14713bd396a 的两份 TS 文件和相关原始验证记录，不重做原 93 文件全审查，不执行任何探针或产品命令测试。

分支 codex/bug-196-credit-byte-ownership，审查时 HEAD 仍为基线，修复未提交。结论绑定以下实际工作字节，不把基线 SHA 当作修复候选身份：

| 文件 | SHA-256 |
|---|---|
| cp6.web/src/contracts/erp-credit/creditCodec.ts | f7108b4f2dd1d2e09ba8e38bda8d52dbb991b7d671ec25bccdf0aa4fa4dd36f5 |
| cp6.web/src/contracts/erp-credit/creditCodec.test.ts | f4b05aef2944ddae2486aca7765d16d1267b6e8b629325ba6efb4abb5fe18b49 |

统一 copyBytes 使用 new Uint8Array(raw)，确保 Buffer/Uint8Array 输入的可见字节被独立复制，不调用输入的 slice。load 在第一次 await 前复制全部三个 Schema 文件，随后 hash 与 parse 使用同一私有快照；decode、captureOriginalBytes、C01/C02 capture/replay 也在异步边界前取得字节所有权。originalUtf8、originalRequestUtf8 与重放 bodyUtf8 返回独立副本。相关定位：creditCodec.ts:18–19、272–274、284、295、305–320。

修改未触及 Schema/hash 身份、根类型集合、原生约束或 Owner fence，接受域不变。五处旧断言由 TypedArray 深相等改为数组值相等，继续逐字节比较长度、顺序和内容；它们不再依赖 Node/jsdom 的 TypedArray 原型相同。

| 已有原始记录（仅读取） | 核对结果 |
|---|---|
| 完整 RED：20261013T103604Z-ts-bug196-red-complete-f6cf63b1 | 428 项中旧 416 PASS、新 12 全 FAIL，0 pending；生产源码 hash 等于基线原字节。 |
| 中间运行：20261013T103806Z-ts-bug196-green-3e6985e2 | 12 新回归均 PASS；149 个失败全部来自旧 TypedArray 深相等断言，原失败日志保留。 |
| 最终：20261013T103957Z-ts-bug196-final-3c3e1efc | 428/428 PASS，0 failed/pending/todo；428 个唯一名称，旧 416 名称无移除，只新增 12 项。 |
| type-check：20261013T103957Z-typecheck-edf96295 | exit 0，源/测试 hash 与最终运行和当前已审文件完全一致。 |

12 项覆盖 current/frozen 的解码期间及解码后输入变更、两版本 Schema pin/解析快照、captureOriginalBytes 期间及之后、C01/C02 请求/响应捕获期间及之后，并验证返回副本变更不影响内部存档。仅反向恢复最终测试中的五处数组比较及注释即可得到完整 RED 测试 hash c92d8ae9be9f702d0999263b08ae7a0ff4ddaaaf7588aee0488dcb91809ea5e5；12 项回归正文没有在 RED 后改弱。

已核对 metadata 的执行前后输入一致、原始日志/Vitest JSON hash 及逐项结果。最终本地环境为 Node 20.19.0、npm 10.8.2、Windows 11 10.0.26200；TS 最终执行 10:39:57–10:40:13 UTC，type-check 至 10:40:54 UTC。原命令、证据绝对路径/hash、12 项名称和源码绑定详见 [S01-11-fix-review.json](./fix-review.json)。所有结果均是既有记录，没有在本次复查中重新执行。

根协调方继续独立核对原复现步骤、后续提交与此处 hash 的一致性并决定 Issue 关闭。未受影响的 C# / 设计结果按原审查范围复用；Q-NB-01 容量限制、真实 Owner/权限/租户/Store/issuer、数据库、HTTP 宿主和生产验收的既有边界继续保留。未修改源码、共享状态或 Git，未调用网络、数据库、Actions。


Final source binding: `ad27d09bd288082fd7a6e813afda7128a3d416f1`; reviewed code/test bytes unchanged.

Final evidence attachment binding: `6dbc1a00cec1349c2ff7fdc2fe633d64ce2cf82d`; five original output.log files explicitly added despite global log ignore. All metadata evidence hashes matched; source/test unchanged.
