# S01-08 / Issue189 修补定向独立审查

结论：**ACCEPT（合同库修补与本地证据范围）**。原接收报告的终态合同采用倒置、Node/npm 环境两项阻塞已在本轮修补与证据中闭合。没有发现要求重启整项审查或全量测试的新实质问题。此结论不等于 CRM 已合并、Issue 已关闭或真实业务验收通过；最终提交、保护合并、远端包含核对及状态登记仍由 COORD 完成。

审查对象为 `D:/CP6.CRM-worktrees/s01-08-coord-20261012` 修补工作字节，相对已审候选 `2ff7f1e4b3df8a4d492d971ffd8f19ff1ea77243`。复用此前完整 S01-08 审查和 Main overlay 独立审查，只读取本次 decoder、生成模板、importer、新增/修改测试及最终证据；没有重审原 111 文件、运行测试/编译/importer、联网或修改仓库。仅输出本 scratch。COORD 后续要求核清旧日志换行，故额外执行了一次只读 `git -c core.longpaths=true show <原候选>:docs/evidence/s01-08/ts-verification.json`，无 Git 写操作。

## 实现与身份

- C# `NativeInquirySchema.cs:18,42,50` 新增独立 revision 身份及 `LoadRevision(sourceIdentity, revision)`；TS `index.ts:142` 同时校验 source 与 revision 的每个必需分量。旧 `Load` / `loadInquiryContracts` 已删除，缺失/未知 profile、错误 patch/effective hash、source/effective hash 混淆均拒绝，无 source-only 升级或失败回退。业务 payload 未添加 profile 字段。四个冻结 INQ-VERSION 仍仅验证显式 revision 入口内的 native source locator。
- 原 source hash `740b840c697b60ebf51ceb0bd9a1898238814a49e36c2afe1d819c473b7df349` 与 effective hash `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d` 分开保存。C# 重算两份嵌入字节的 SHA；TS 运行时严格比较固定身份，字节核对由 importer/build 完成，文档未虚称浏览器实时 SHA 校验。revision 元数据在模块初始化时冻结，新增失败/回归证据保留。
- `StrictSchema.cs.txt:12,20` 和 TS `index.ts:98,118` 在实际生产 decode 路径对 root PrivacyExportView 与五个具名 PrivacyExportUiState export 分支执行同一规则，不按任意对象字段猜测类型。99 要求 `requestedAtUtc <= completedAtUtc`，公开 expiry/artifact/proof 必需且 null；30 保留 `requested <= completed < expires` 与下载字段；90 仍为终态 null。C# 原 helper 反向“99 非 null”变换已删除。
- 原候选底层 `StrictContractDecoder` / `SurfaceStateCodec` 为本次未发布模块的固定 revision helper；公开版本装载路径已显式选择 R1，不保留会悄悄升级的 source-only loader。现有 first-slice 44-schema API 及业务 HTTP 接线未被此次修改。

## 静态来源与便携性

Main 新来源为固定提交 `ce38171868312588ce8f4b9e843ed6c39f4d0b0f`，PR192；COORD 报告远端 main 已包含于 `dbe59942c34a62a47f5e7b11112a3062a07d1263`。`source-delivery.json` 明确区别作者本地固定 Git blob 核对与 COORD 远端观察，本审查没有重新联网冒称独立远端验证。两份便携副本与我已独立审查的 Main 资产摘要完全一致：

| 内容 | SHA-256 |
| --- | --- |
| profile.json | `c354974c918dbc39d1ecf9352db79ab5b2b8a61e901f2db6ea328d6112993645` |
| overlay.json | `bce20c5a3bc93826993be6c2ba7c9a440fffd467f746245e5665707d8eedf549` |
| effective Schema | `fd9a9444eac23225ecbf8ab91856dc7df2c0e4da53d9b765cbfccdeb2223ba2d` |

Importer 固定 profile/overlay 原字节 pin，再检查 native locator、补丁身份、两项 replace/application count、完整 before 值及 effective hash，全部生成成功前不写输出；来源仅为本仓相对路径，无 Main 绝对路径运行依赖。即使输入指向外部便携副本，仍必须通过相同固定 pin。

独立递归比较原/effective JSON，变化**仅**为 `/components/schemas/PrivacyExportView/allOf/3/then/properties/expiresAtUtc` 的完整 string 节点到 `{type:null}`，以及同一 then 的 `x-semantic`。30/90 的整个条件分支及根 required 列表保持。38 definitions、257 refs 全部可解析、7 处 PrivacyExportView 引用（2 HTTP schema 加 5 UI）不变；55 仍表示冻结来源的引用修复数。C# effective base64 解码后与 effective 文件逐字节相同，TS effective 对象与该 JSON 相同。

22 份冻结输入（包括 package/profile/vectors/original/composed）与旧候选已核工作副本逐字节相同，30 case 的原 expected 未改。当前 generation manifest 的 24 source pins、16 generated output hashes 全部实算匹配；日志的 17 outputs 包含 manifest 本身。新 revision 是独立来源和身份，不把修订结果冒称原包 1.0.0 的 source bytes。

## 最终证据适用性

独立解析 `terminal-r1/executed-source.json`，**338 inputs、47 artifacts、22 output logs/TRX** 全部存在且实际 SHA 与记录相符；该清单为各检查消费范围的并集，并非声称每项检查消费全部 338 文件。执行 HEAD 如实仍为旧候选，清单绑定后续工作字节，不冒称未来提交已被执行。

| 检查 | 已核结果与适用范围 |
| --- | --- |
| C# | TRX 186/186 passed，0 failed/skipped；新增 18 项 root/5 UI 终态测试；49 个原 UI 行接受并真实 DTO Encode 往返；30 固定 ID 齐全。最终仅 Web metadata 生成变化，C# 输入/产物未变，复用本轮该次执行。 |
| Web | 最终日志 142/142，0 failed/skipped；82 本任务加 60 既有。相对旧 128 增加 14 项（12 root/5 UI 规则测试、1 revision identity、1 metadata immutable）；49 原 UI 输入均成功并保留 UTF-8 再 decode；30 固定 ID 齐全。 |
| Node/typecheck | Node 22.22.0、npm 10.9.4 满足现有 engines；最终 `next typegen && tsc --noEmit` exit0 由执行者工具回执记录，稀疏日志本身不被当作退出码推导依据。 |
| Importer | 最终生成/check 成功，6/6 CLI 完整性测试；包含 native/profile/overlay 篡改失败前零输出、便携生成、漂移不修复。 |

新增测试同时覆盖 90/99 null 合法、三字段缺失/非 null 拒绝；99 requested>completed 及 completed null/缺失拒绝；30时序错误及必要下载字段 null 拒绝。测试通过不再依赖把 UI `/46` 当作错误样例。冻结30项及原其他未受影响结论可复用准确历史范围。

关键最终证据 SHA-256：

| 文件（均在 docs/evidence/s01-08/terminal-r1） | SHA-256 |
| --- | --- |
| executed-source.json | `39354883919647ad75b872aa7f329f0b0657ea8965e14675e4b4bd0631347f8a` |
| source-delivery.json | `afd8a61d0e8ccb5facab7cd0ecc5c8d27450b9edf56c650d681aa3a492089d3e` |
| csharp-final.trx | `4bc4c30aef71b802982e9c9be5f353c89e743b80e0ad42e4374ebf7fad62f2dd` |
| web-final-142.txt | `ee948b24c8f2c1800cddb00a2c53a8d6658b26ee659580805e49e9fd52f64f04` |
| web-final-typecheck.txt | `48377bcab8ff42f75a4d7499205ba5fc6a57cbbaf1c05b06946e45500c4ca692` |
| importer-final-integrity.txt | `b631ae3814bb2b299464e3ddc963df1b30a49d3152e218627559cd7d1f5c3c60` |

## 历史记录与交付边界

原 RED、旧错误拒绝结论、Node20 观察、本轮 cp936 字面量错误、初次错误 cwd、metadata 可变 RED 均保留并说明真实结果；未把失败文件名带 green 当作通过。旧 `ts-verification.json` 原候选 Git blob 为 LF、12135 bytes、SHA `8664b9d3d8990d905d46bf05533679f31a906cf5c8b7c9d9f8e7f69b5c3fe400`，与当前文件逐字节一致。原工作树 CRLF 摘要 `91416d...` 只是 checkout 表示；未改历史 Git blob，其余 43 份旧执行记录工作字节也相同。

已读更新后的 DELIVERY、输入 README 与修补 README，其撤回错误、来源身份、186/142/6 计数及 NOT_RUN 范围准确。审查指出“capture-execution.py 可只读重算”不准确；作者已改为该脚本会写/覆盖清单，只能用于对应实际执行后且输入未变的捕获，独立摘要比较才是只读核对。我已读取更正文句，问题闭合，无需实现变更或重测。

后续仍须由 COORD 固定最终提交并确保被审代码/依赖/证据字节未再变化，正常完成串行保护合并和远端 main 包含核对。真实 HTTP 接线、服务器内部历史 expiry 保留、下载 proof/当前会话/ETag、终态页面、Owner 权限/事务、租户隔离、PostgreSQL 持久化、全业务/E2E、包发布/部署继续 NOT_RUN，分别归 S03/对应后续任务，不因本次合同库 ACCEPT 提前登记业务 PASS。
