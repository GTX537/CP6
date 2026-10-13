# S01-11 协调准入审查

**BLOCKED：1 项 P1，必须修复后再合并。** 候选 b27fbe5422bbe487efce2aa4de1bf14713bd396a，基线 e20740e37e20bc0210e0b32c5670c2bce962002a。本轮已审完整 93 个新增路径、全部产品/测试代码及生成来源/交付证据。文件 Git blob、SHA-256、审查方式、原始命令与哈希绑定见[机器证据](S01-11-original-validation.json)，未复制原 catalog。

## P1 / S01-11-R1：Buffer 别名破坏 Schema 身份及冻结原字节

位置：[creditCodec.ts:270](D:/CP6-worktrees/s01-11-20261012/cp6.web/src/contracts/erp-credit/creditCodec.ts:270)、[load:293](D:/CP6-worktrees/s01-11-20261012/cp6.web/src/contracts/erp-credit/creditCodec.ts:293)。同根因还涉及第 17、272、282、295、303、305、312、314、318 行。协调方已登记 [Issue #196](https://github.com/GTX537/CP6/issues/196)。

公开参数接受 Uint8Array，Node Buffer 符合此类型。代码把调用方的 .slice() 当作复制操作，但 Buffer.slice() 返回共享内存的 view。实际只读内存探针确认：

1. 合法完整 PostInput 用 Buffer 解码后，修改原输入将保存字节首字节从 123 改成 0；修改 originalUtf8() 返回值又改变内部第二字节。sha256 仍是 82371237…，保存字节的新摘要已经是 37113784…，完整 value 则仍是旧对象。
2. 用 fs.readFileSync 返回的 Buffer 加载准确 Schema，在 factory 调用后立刻把 WMS 正则 {0,29} 改成 {0,99}。哈希校验捕获原 b324a9b6…，随后解析的是已改为 6992b21a… 的字节；factory 成功，90 位整数 Money 被接受。这直接绕过原 schema pin。
3. frozen captureReplay 后修改原请求 Buffer 的 old→nld，并把响应首字节改为 0，保存对象仍接受改变后的请求并以 HTTP200 返回已改正文。

修复所有输入所有权转移及返回副本的位置，使用保证分配独立基础 Uint8Array 的复制方式，避免调用输入子类可覆写的 .slice()。补 Buffer/子类的加载、解码、原字节保留、C01/C02 重放回归，覆盖异步等待期间修改输入及返回值修改。现有 TS 测试用 new Uint8Array/TextEncoder，未覆盖 Buffer。

两次探针原 stdin 和原 stdout 均已写入机器证据的 probes。最小复现无需新文件或构建：

~~~powershell
Set-Location 'D:\CP6-worktrees\s01-11-20261012'
$creditReview = Get-Content -Raw 'C:\Users\tt\CP6-planning-reports\20261013-wave4-integration\S01-11-original-validation.json' | ConvertFrom-Json
$creditReview.probes[0].stdin | & 'C:\Users\tt\AppData\Local\nvm\v20.19.0\node.exe' -
$creditReview.probes[1].stdin | & 'C:\Users\tt\AppData\Local\nvm\v20.19.0\node.exe' -
~~~

探针仅用已安装 TypeScript 6.0.2 在内存转译候选源码，Node 20.19.0，退出 0 表示成功观察上述缺陷，不表示功能通过。没有修改 worker 文件。

## 可复用的原验证

原始结果真实存在且与候选受测输入绑定一致；本轮没有重跑这些门禁。运行目录均位于 docs/development/evidence/S01-11/implementation/。

| 原运行 | 结果 | 独立核对 |
|---|---|---|
| 20261013T060354Z-dotnet-final-407086e6 | C# 417/417，0 失败/跳过 | 303 个相关输入、TRX 完整输入对象、实际 .NET 8.0.29 与 6 个编译产物 |
| 20261013T060526Z-ts-final-5254b7ee | TS 416/416，0 失败/pending | 30 个相关输入、410 个固定/私有 ID 唯一实际记录 |
| 20261013T060619Z-typecheck-83ec4cb2 | exit 0 | 12 个任务 TS/config/lock 输入及原输出 |
| 20261013T060706Z-design-f299e68e | 5/5，0 失败/跳过 | 13 个相关输入、343 投影/60 篡改/18 Money/29 路由/10 扩展块的准确子断言范围 |

实际原命令含 TRX/Vitest 报告参数，完整保存在机器证据 original_validation[].command；门禁主体为：

~~~powershell
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~ErpCreditCodecTests
npm --prefix cp6.web test -- src/contracts/erp-credit/creditCodec.test.ts
npm --prefix cp6.web run type-check
~~~

四个原最终运行的相关输入执行前后均相同，当前原字节仍吻合。313 个提交输入的 Git blob 与 b1a93ec… 源码绑定回执及最终候选均相同，3 个生成依赖输入和 6 个编译产物哈希吻合；b1a93ec… 到候选只新增 DELIVERY/source-receipt。全 93 个候选文件与工作区字节相同，git diff --check 通过，共享项目/依赖配置未变。历史 RED、首次 type-check 失败与较早窄范围成功均保留。

这些结果不涵盖新发现的 Buffer 别名场景。修复 TS 后只重跑受影响的 TS 合同测试、type-check 和新增别名回归；C# 与设计资产等输入不变时，继续引用现有结果，不必因新 SHA 重跑。

## 其余审查结论与边界

- 20 项所选原始/组合/native 来源、10 个原生成源码范围摘要、5 个采用资产和 5 个原正文逐字节匹配。343 个设计投影逐对象回读一致；只排除了声明的历史 SQL recipe。没有执行归档程序。
- 两版本各 63 个已发布定义的三文件闭包与 surface.json 一致，14 个完整对象根、2 个金额标量及 2 个专用 replay 根准确。F09/C06 三个额外根不属于原发布 factory/本任务新增入口，拒绝它们符合本轮 scope；不能据此宣称任意 TypeRegistry 或完整 C06 采用。
- WMS Money 30/8、FIN-AR D8 20/8、原生 -0/1.0、严格重复键、required/null-only、闭合字段、版本语义及 net8 结构相等均按准确合同实现；Schema 固定本身受 R1 影响。
- C# 使用独立 byte[] 和 JsonElement；TS 普通 Uint8Array 路径及完整对象冻结已覆盖，但所有输入类型的原字节不可变承诺被 R1 推翻。
- Owner/current AR/GL/member/finite、Store/issuer 证明不足继续 UNPROVEN / MANUAL_RECONCILIATION / UNKNOWN；无释放、派发或 FOUND 授权入口。C01/C02 正文保存意图及合同根正确，实际字节完整性须先修 R1。
- 原 SPEC/QUALITY 独立审查与最终产品/测试哈希一致，它们的 PASS 只保留原审查事实，不能替代本次 R1 修复准入。已知非阻断 Q-NB-01（O(n²) 区间检查，最大合法规模与 C# 容量未测）继续保留，真实接线前处理。

247 完整业务运行场景继续 NOT_RUN；41 历史条目 REFERENCE_ONLY；ITER/REL 缺失原件与 WP05 PROPOSED 不变。真实 Owner、权限/租户、数据库/迁移/事务、HTTP 宿主及生产部署均未验收。原 type-check 快照只记录任务 TS/config/lock；未声称恢复所有历史机器状态，其他前端源码在候选及干净工作区中保持不变。

本次仅写指定 scratch 报告。未执行 Git 写入、网络发布、Actions、数据库或状态文件更新。PR195、最新远端 main、共享状态和保护合并由 root 核对。初次默认 Git status 的长路径警告经 core.longpaths=true 重读为干净；没有实际删除。首个只读闭包枚举脚本因默认旧 Python 不支持 removeprefix 退出 1，改用兼容写法后通过；不属于产品测试结果。
