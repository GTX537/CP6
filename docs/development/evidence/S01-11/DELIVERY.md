# S01-11 候选交付

状态：**本地实现、验证和任务审查通过，交付 COORD 串行集成；尚未声明 DONE。**

独立任务分支为 `codex/s01-11-erp-credit-20261012`，确认主线基线为 `e20740e37e20bc0210e0b32c5670c2bce962002a`。实现、测试、来源、原始失败/成功记录和审查的提交为 **`b1a93ec932af89fa5de7db03f35eadb0eb00f9fe`**；随后只增加本报告及[源码绑定回执](source-receipt.json)，未改受测输入。最终候选 SHA、PR、每次发布前工作流检查和远端回读由本任务专属 Handoff DELIVERY 固定提交记录。

## 交付行为

- C# / TypeScript 分离 current-v1 与 frozen-R8 的准确身份，完整采用原生 WMS/FIN-AR schema 和对应 registry。14 个具名原设计发布根、2 个金额标量辅助入口、2 个 C01/C02 专用重放根都有显式边界；[来源/API 映射](../../../../eng/contracts/s01-11/surface.json)记录完整三文件闭包及与归档生成 DTO 的差异。
- WMS Money 使用 `value/currency`、30 位整数及最多 8 位小数；FIN-AR 使用自己的 20/8 D8 域。保留合法 `-0`、`1.0` 和原始金额词法，运算使用 BigInteger/bigint。严格 JSON、重复/转义等价键、required/null-only、封闭字段、版本和嵌套语义均受检。
- 冻结命令和回执保留实际原始 UTF-8、摘要及版本身份。C01/C02 guard 以相同 key/请求字节返回 HTTP200 与首次 HTTP201 的完整原始 COMMITTED 正文，不重新序列化。
- 适配缺少可信 Owner/current AR/GL/member/finite、Store/issuer 证明时保持 `READ_ONLY_UNPROVEN` / `MANUAL_RECONCILIATION` / UNKNOWN，禁止 FOUND、释放或派发。代码没有自报布尔授权入口。

## 验证与审查

| 门禁 | 最终实际结果 | 原始运行目录（`implementation/`） |
| --- | --- | --- |
| 指定 net8 叶子 C# 测试 | 417/417，零失败、零跳过；实际 .NET 8.0.29 | `20261013T060354Z-dotnet-final-407086e6` |
| 指定 TypeScript 测试 | 416/416，零失败、零跳过 | `20261013T060526Z-ts-final-5254b7ee` |
| 前端 type-check | exit 0 | `20261013T060619Z-typecheck-83ec4cb2` |
| 可移植设计子断言 | 5/5；343 投影、60 篡改拒绝、18 Money、29 路由、10 扩展块 | `20261013T060706Z-design-f299e68e` |

两语言逐 ID 执行全部 10 条固定向量，各加 362 条构造用例、38 条选定设计投影及 6 个 guard；C# 另含运行时断言。BYTES 原向量不符合完整 PostInput RV 域，测试明确拒绝其 native decode，仅验证 `NativeSchemaValidated=false` 的字节保留。

[独立证据回读](validation.json)和[提交绑定](source-receipt.json)核对原始结果、完整输入对象、当前源码/依赖、原始日志以及编译产物哈希；工作树执行字节与 Git blob 分别记录。提交、SHA 或说明文档变化没有触发重复业务测试。原有效 RED、早期较窄通过记录、首次 type-check 失败均原样保留。

[SPEC](review/20261013-spec-review.md) 后接 [QUALITY](review/20261013-quality-review.md)，同一完整任务集中审查，均 PASS、零阻断。所有改动只在派工允许的五类路径；完整差异已检查，源码/资产/证据暂存字节与工作树一致，`git diff --check` 通过。来源清单 CRLF 用局部 `-text whitespace=cr-at-eol` 保存，未改写实测字节。

质量审查保留 Q-NB-01：大量区间重叠检查为 O(n²)。TS 的 1k/2k/4k 合法区间小范围探针已记录；最大规模和 C# 容量未测。真实接线前需在保留合法域和精度的前提下改为排序扫描并完成容量验收。

## 协调交付和未验收范围

COORD 负责审阅最终候选、统一同步 roadmap/生成进度/四份状态文件、正常保护合并和远端 main 包含核对。执行对话未改共享状态、主线或其他任务，未删除分支/worktree。START 固定消息见 [回读回执](start-receipt.json)；上传及 ACK 均不替代合并或业务验收。

每次候选 push / PR 前检查候选及刷新后的 protected main 工作流事件、过滤与本地依赖；使用普通分支推送且不推 tag。没有运行或重跑 Actions，没有取消任务、修改触发器或分支保护，也没有发布、迁移或部署。发布检查和对应操作后的远端证据随 Handoff DELIVERY 附送。

本任务没有真实 Owner、Store/issuer、授权/租户、PostgreSQL、事务恢复、HTTP 宿主或业务写入验收。现有 CreditNote Service / Entity / DTO / Controller 保持原样，其历史存储不因此取得精确合同采用证明。三文件包缺少 F09，未开放 C06 三个通用根。247 完整运行场景仍 NOT_RUN，41 历史静态仍 REFERENCE_ONLY，ITER/REL 缺失原件、WP05 PROPOSED 不变；没有把设计 fixture 或局部测试升级为真实业务 PASS。
