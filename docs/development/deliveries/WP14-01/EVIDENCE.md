# WP14-01 候选输入与证据索引规则

本约定组织验收记录，不改变各业务合同的字段、错误码、Owner 或原子性。唯一任务/目标/场景进度仍在 [roadmap](../../roadmap.json)，实际运行报告由执行任务独占保存；此目录只保存准备快照。

## 候选输入矩阵

| 输入 | 责任 | 就绪证据 | 本版范围 |
|---|---|---|---|
| 125 目标原验收 | 各目标 Owner，WP01 核对清单 | 完整定义、来源 hash、refinements/连续步骤、接受版本 | 123 完整；ITER/REL 历史缺口保留 |
| 247 修订运行场景 | 单包 Owner 或 WP14 + 全部消费者 | [逐场景矩阵](revision-runtime.csv) 的原 input/expected/write set，准确采用实现 | 247 NOT_RUN，599 消费者责任 |
| S01 精确合同 | S01 + 每个真实消费者 Owner | 已交付 profile/版本/组合 hash、真实存储与 wire 采用 | 当前卡/评估只是来源，其他任务候选分支不作交付 |
| S02 多租户布局 | S02 + 各仓持久化 Owner | 已交付共享/租户边界、可信映射、权限、池/事务、迁移/恢复约定 | 本任务不选择/创建 schema 布局 |
| WP05 历史补订 | WP05-01、WP01-01 | 已采纳版本、旧→新覆盖、不可覆盖部分与完成门变更 | 不新增旧 30/48 AC，不修改当前不完整状态 |
| 真实实现/包 | Main/CRM/Platform 各仓负责人 | 完整源码 SHA、远端 main 包含性、正常 PR、包/合同版本与 artifact hash | Main 文档基线固定；CRM/Platform 评估 SHA 仅历史观察，未新验收 |
| 运行环境/数据 | 各 Owner + WP14 预约 | [环境预约单](ENVIRONMENT.md) 全字段、隔离资源与权限核对 | 全部未预约/未创建 |
| 发布候选交接 | WP14-10 + 独立发布责任人 | R2 权威候选、签名、SBOM/扫描、不可变清单和环境审批 | 本地文档/业务验证均不替代该门；未触发 Actions/部署 |

归档中旧 SQL Server 原文/输出仍保留其历史身份；新运行只针对 PostgreSQL。迁移、真实数据切换及发布按独立任务授权办理，不因本输入清单而启动。

<a id="fault-selection"></a>

## 故障注入必须落实到原条目

`requirements.json` 的 F-SOURCE 对每条原验收/修订场景生效：执行前写出原前置、具体操作、断言及故障触发点。F-REPLAY/AUTH/UOW/TRANSPORT/UNKNOWN/PRECISION/MIGRATION 是按链分配的准备要求，按原合同选择适用路径；不能只完成这几类公共检查便称全部原验收通过，也不把它们扩成未经批准的新业务合同。

每个适用故障步骤必须记录：Owner/入口、发生于 read/最终权限门/业务写/receipt 写/commit/响应/OwnerApply 的哪一步、触发方式、并发双方及 barrier、允许结果集合、禁止写集/泄露、查询恢复命令与原键、最终见证。如果认定某公共模式对当前只读场景不适用，记录原因；原条目明确要求的故障不能以不适用跳过。

例如订单提交断响应：先按原 Intent 发起，确认是否到达 native commit barrier，再断响应；只用原键查询，分别记录 Order/Fact/Allocation 的计数与身份及原回复字节。查询空页/ACK 不能证明 no-effect，不能换新 Intent 再创建。采购叶40/60的质量场景则由 PUR/QA/WMS 分别记录原叶、观测义务、决定与入库，不用母100结果代两叶。

<a id="run-record"></a>

## 每次实际运行的记录

| 部分 | 必填内容 |
|---|---|
| identity | 原完整 case ID/source_key、全部目标关联、run ID/attempt、执行人、Owner 签收人和 UTC 时间 |
| scope | 原完整定义和接受版本/组合 hash、全部补充断言和顺序步骤；原件/解包成员 SHA、JSON Pointer/行/单元格；明确此 run 证明哪些消费者 |
| source | 实际参与各仓完整 SHA、远端 main/PR/包版本；准确运行 artifact 与逐文件 SHA-256；不能只记录准备分支 SHA |
| environment | 预约单引用、PG/驱动版本、脱敏数据库/角色、TenantId/schema/迁移版本、端口/队列/worker 身份 |
| input | 精确 command key/Intent/source tuple、原 request/response bytes 的摘要和安全存放位置、初始事实、资格/版本/时间、seed 来源与局限 |
| execution | 可复制命令/入口和参数、安全 Secret 引用、实际退出码、逐步骤时间、故障 barrier/触发日志、原始 stdout/stderr/HTTP 或测试日志 |
| witnesses | 每个 Owner 的前后事实/receipt/原键/版本、完整允许/禁止写集、事务/锁/CAS、权限/租户和数量/金额不变量；运输/业务/物理/现金状态各列 |
| result | 每个必需断言和消费者的实际观察；PASS/FAIL/BLOCKED/NOT_RUN 与理由；失败日志保持不变，复测新 attempt 关联前次 |
| recovery | 原结果查询/重放证据、原义务/claim 保护、迟到/取消后终态、资源恢复/清理结果；UNKNOWN 不作无效果证明 |
| integrity | 每个 artifact 路径/URI、SHA-256、生成来源、权限/保留期；报告含索引，原始日志不被摘要取代，不提交 PII/凭据 |

登记至台账的实际执行证据至少包含 `path/source_sha/scope/command/environment/observed_at/result`；涉及多仓时报告分别列出真实源码与产物，不能让一个 Main SHA 代表全部实现。交付证据另需 `remote_main_sha/pr_url` 和所属仓库远端包含性。模板空字段不是证据，fixture 里的预期也不是实际观察。

## 去重、复用和拒收

- 一个修订完整 ID 的一次运行只有一个 run record。各消费者引用该记录的 witness，全部到齐才允许一次 PASS；另一次实际运行用新 run ID，不覆盖先前失败。
- 原验收先按 `source_key` 集中编排；每个 `assignment_id` 的 target/catalog 补充要求仍必须逐个勾对。只通过某 Owner 的分支不能把其他关联自动标 PASS。对仅指向相同标题或不同原版本的记录禁止合并。
- 已有成功证据只在代码、依赖、合同输入、环境与适用断言未改变时复用，记录原 run/SHA/hash、适用消费者和未覆盖范围。提交或合并 SHA 改变本身不要求重跑；来源内容变化不能悄悄复用旧准备快照。
- 247 全通过也不关闭 125 目标：还必须满足主体任务、全部原验收/补充、版本化历史补订、实际实现与各仓远端交付。41 历史静态、目录工具 PASS、运输 ACK、候选分支、合成 fixture 和缺失的签收均不能补足该门。
- 完成门仍有未运行/失败/缺真实提供者/不完整清单时，保留事实并列明解除条件；不伪造 no-effect/闭环、不删除失败、不把 skipped 算通过、不用本地报告冒充 R2/GHCR 或 Azure 成功制品。
