# Planning / WO 后继设计夹具 R1

本包解决 PLAN-IF-007/008/014/015、OPS-IF-006/007/008 的设计数据一致性。原归档、旧 sealed Graph/native identity、已接受 wire 和实际采用记录不变。`runtime=NOT_RUN`、`actual_adoption_status=UNPROVEN`。新对象包是静态 DEMO；不是执行过的 SQL UOW、Core 授权或 Owner 接纳凭据。

## 可复现交付

在仓库根运行 `python tools/build_planning_fixture_patches.py`。它只读固定 SHA 的原 JSON/Markdown，在内存建立完整后继对象包并验证；不 import/execute 原作者程序、SQL 或业务应用。无第三方依赖。禁止 `python -O`，因为校验必须启用。

需要完整本地文件时，显式传 `--materialize <新建的本地输出目录>`。三个文件是 `exc-successor.json`、`sc06-successor.json`、`cp19-successor.json`；已存在且字节不同的输出拒绝覆盖。完整原包和约 57 MB 的派生 EXC 对象包均不复制进本仓库。

| 文件 | 用途 |
|---|---|
| `generation-recipe.json` | 生成器 SHA、所有输入完整 SHA、逐域预映像、输出完整 SHA/bytes/序列化 |
| `planning-closure-manifest.json` | 四世界身份/摘要、全部 graph members、原生版本后继、原生/私有 hash 依赖图、Case 依赖及 PLAN-PO 变更叶路径 |
| `patches.json` | SC06、CP19、WO 三个源文件的 7 条精确 `replace_text`；完整原 SHA、匹配次数和派生 SHA/bytes |
| `validation.json` | 本次静态校验明细与反例；业务执行、数据库事务和真实采用仍未运行 |
| `../../workparts/planning-fixtures.json` | 七条 finding 的逐条决定、原文证据和正负验收要求 |

`patches.json` 对 SC06/CP19 保留原文件的外层排版，修正必要字节。`--materialize` 输出的是完整 canonical JSON；因此其 SHA/bytes 单独列在 recipe 中，不能把两种序列化的文件 hash 混用。原文件从不写入。

## PLAN-IF-007/008：不可变 EXC 闭包

PLAN-IF-007 采用原生 F014 的 `availableNode`（与 F016 `actualNode` 同为 `2026-10-04T18:10:02Z`）。PREPARED/PUBLISHED 的 SUP 投影 body 及私有 graph 同步引用该原事实节点。PLAN-IF-008 采用已经存在的 `MAT_ALGORITHM_V2`、`algorithmVersion=2.0-proposed`，使 MATERIAL_OLD/NEW 的 graph 与 MAT_INPUT/RUN/ATTEMPT 使用完全相同的八字段 Ref。原 F025 1.0 / I1 仍是历史链，八类支持输出并不意味着每个结果必须有八行。

四个新 Graph、Assessment、Impact 使用 UUIDv5 新身份；Graph 的声明 version 不当作 Case revision 序号。变动的 native body 使用原完整 Ref 的 `versionKey + /R1`，保持 Owner、namespace、objectKey、Scope 和 Ref 其他域；不把新摘要填回旧 native version。新 artifactId、storageLocator、provenanceId 和完整 nativeUtf8 同时生成。历史对应关系放在旁路 manifest，不加入或改写原 business body。

所有 required Owner read 的 query requestKey 用原 opaque text 加 `/R1`，同时重算其 UTF-8 byteLength/rawDigest。查询正文、capture、其依赖 source facts、Graph memberRefs、read plan、source request/page 全部按完整 Ref 解析。新的 readPlanId、captureWorkId、页 artifact/trust 与 query 一致；不能复用旧不可变 query 响应槽。

四个 Graph 的精确传递成员数是 **84 / 116 / 188 / 188**。最终对象包包括 **395** 个实际依赖的原生文档、**15** 个 CurrentManifest、四组新私有对象、四份完整来源页、公共授权/掩码视图及 **104** 个 Case 支持行。公共视图中同名 F018 的 Core 事实有不同完整 Ref，按实际完整 Ref 收录，不能按 semantic label 串包。

原件的 761 个已登记摘要预映像均复现；依赖变更共 61 个节点（49 native、4 Graph、4 Assessment、4 Impact）。没有递归清掉所有叫 `digest` 的字段：

| 域 | 精确预映像 |
|---|---|
| native | 完整 closed body；包含嵌套 Ref/body/closure 摘要 |
| Graph | 私有 business body 仅去顶层 `digest`；memberRefs/CurrentManifest/嵌套摘要保留 |
| Assessment | 完整私有 business body；它没有自身 digest 成员 |
| Impact closure | closure 仅去自身顶层 `digest` |
| Impact | 先放入已算 closure.digest，再仅去 Impact 顶层 `digest` |
| CurrentManifest | 仅去顶层 `bodyDigest` |
| source page | 完整 `preimage`；artifact/trust 是外层 envelope，不进入该页预映像 |
| Stored row | 明确提供的 `canonicalBody`；列上的 BodyCanonical/BodyDigest 不再加入自身预映像 |

以上 JSON 使用已从原件复现的 EXC-JCS-1：UTF-8、Ordinal key order、紧凑 JSON、无 BOM、无末尾换行，保留数组顺序、null 和字符串形数字。`BUSINESS_ROOT_EXCLUDES_OWN_DIGEST_AND_PUBLIC_DISCLOSURE` 指私有业务根不包含公共披露包装；不授权删除私有根中的任意嵌套字段。

新 CaseRevision 固定新 Assessment/Impact/HeadObservation 的完整身份/摘要，CaseHead 固定新 revision/digest/writer/epoch/commit tuple。支持行使用原实际行型与列集合，所有 canonicalBody 摘要重新计算。新 DEMO 的 writer 为 `EXC_R1_DEMO_WRITER`，commit 141–144；RowVersion 仅是显式合成的 provider readback 数据，绝不能成为 SQL assignment。

旧 publicViews 指向的历史 `CP03_LOCAL_UOW_LEDGER_INDEX.json` 全仓快照保留为 `historicalLocalProvenance`。新 view 的 Case 引用指向新对象包；不继续宣称旧完整 SQL 快照承载新字节。本次完整性范围是 **对象/原生事实/来源捕获/Case 行的引用闭包**。它不重放所有历史 SQL conditionalWrites、Store gate 或 rowversion；那些运行门在每条 runtime AC 中仍为 NOT_RUN。

## PLAN-IF-014/015：原 native receipt 与全部副本

SC06 同一事件/leg/command 的原 `OwnerReceipt` 是唯一证据。两份 revision4 BodyJson 的 requestDigest、ownerResultDigest、完整 ownerResult 对齐该 receipt，两份 revision 和两份 head 的 BodyDigest 均为 `c35ad4f9195c1958b59989ca76d97b67112ee0921b0b126b1dda3541ff05a6a0`，恰等于已有 receipt PayloadDigest。原 snapshot 不重算成别的 Owner 版本；ExactTupleHash 仍是原 length-prefixed pipe 字符串的 UTF-16LE SHA-256，不能换为 JCS。

CP19 step1 的执行键采用已冻结请求及前一步事实：`CP18-MAKE-579b4a48-c253-5aa9-834d-1847c3733973`。完整结果 canonical 为 399 UTF-8 bytes，摘要为 `07c674c823ef9ec67fc1954b7a389e520899c61995064a94706346c2fde401a5`。23 处旧键、44 处旧结果摘要的所有 native/resultCanonical、参数、OwnerObservation、OwnerWorkflowStep、OwnerCommand terminal、RecoveryCheckpoint、后续 pre/post/claim、rootFirstClaim 副本都修正；manifest 列出每个改变的叶路径。原本正确的请求、局部 CommandOutcome 和无关 supporting capture 不改。

## OPS-IF-006/007/008：准确新示例

- **006**：旧 WO Markdown 的 a/b/c 重复摘要原已声明是占位值。R1 文本采用 REL/AUTH-PROD-001 v2 gate，真正可复现的完整后继采用 SC06/CP19 的已有 v2 SourceDescriptor、EngineeringCheck、candidate 和 snapshot 预映像。不能把改完字面量的旧占位摘要宣称已复现，更不能编造一个 v1→v2 审批事件。
- **007**：UNSTARTED60 的 scope 为 `[40,100)`，root 100 和已实际执行 40 分别保留。FinalizeCancel 必须闭合整个请求 scope；新说明示例不证明运行取消成功。
- **008**：正式 409 literal 是既有 `SUCCESSOR_SCOPE_HELD`；`SUCCESSOR_SCOPE_ALREADY_HELD` 是旧说明字样，未新增兼容 wire alias。TransferredNoExecution 区间仍完整转移，不制造 free-pool 间隙。

主要来源是 SHA `7fe69593…` / `9c659111…` 两份 EXC JSON、SC06 `255e32f6…`、CP19 `46156a22…`、WO v1.1 `a2acf9ec…`，以及真实 physical/local record/ledger 三文件。完整路径、64 位 SHA 和逐条已读范围在 manifest/workpart。原 EXC materializer 脚本仅按文本读取过其摘要公式，未执行。
