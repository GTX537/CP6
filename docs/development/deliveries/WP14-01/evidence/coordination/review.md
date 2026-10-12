# WP14-01 独立集中审查

审查时间：2026-10-12 07:23 UTC。结论：**未发现阻塞候选内容合入的问题；可进入协调串行交付窗口**。本结论不代替 assignment/entry_gate、共享状态同步、受保护合并或远端 main 包含性核对，不表示业务验收完成。

- 候选：`bd8b7528580567f4b66cbbf065fcebfc472c3a14`
- 固定准备来源：`df5553fd04ec4fc516602a4e2843650c38ae5006`
- 本次对照主线：`8570384f1e28065afee4ce416c9f492dde734131`
- 工作树：`D:/CP6-worktrees/wp14-01-20261012`
- 本次只读仓库；唯一写入为本报告。未启动 Actions、数据库、业务编译/测试或部署。

## 任务与范围

已读 `D:/CP6/AGENTS.md`、requesting-code-review 技能及 reviewer 模板、WP14-01 精确任务定义/任务卡和并行入口。检查候选相对当前 main 的完整任务差异（25 个新增文件，均在 `docs/development/deliveries/WP14-01/`），逐项阅读 README、PLAN、REPORT、ENVIRONMENT、EVIDENCE、requirements、prepare.py、verify-docs.py 及已保存验证/失败/交付记录；程序化读取全部 CSV 和 138 项输入清单，并独立解析全部 13,404 条原定义引用和 247 修订定义引用。原外部归档字节没有重新审计，未把指针解析当成真实步骤已执行。

## 已核实的关键事实

1. **责任覆盖准确。** 125 目标和台账目标逐一相等；13,404 个 `(target_id, assignment_id)` 与台账原关联集合完全相等，全部唯一。分类为 8,456 ORIGINAL_AC、4,948 REQUIRED_SUPPLEMENTAL_SCENARIO。247 个完整修订 ID 和 599 个 `(target_id, case_id)` 消费者对完全匹配原目录/台账；每条 scenario/input/expected/expected_writes 与原定义相等。生成逻辑见 `prepare.py:116`、`:137`、`:166`、`:217`。
2. **共享身份边界保留。** 12,635 个 source_key 只作调度分组，不是新业务分母。逐组检查没有不同顶层 source 字段被合并；相同原 SHA/原编号/非空源行没有被拆成多个 source_key。没有原生编号的 4,596 条仍保留空原编号，并均有源行或准确 JSON pointer，没有发明原 AC。`README.md:31`、`:35` 和 `EVIDENCE.md:51`–`:54` 要求每个目标附加断言/消费者分别举证。
3. **完整定义没有被摘要替代。** CSV 指向完整原对象并保留目标与目录级解释入口；抽查 OA refinements、SPACE body_assertion、ERP-CREDIT ordered_operation_bindings、WMS canonical_identity_mapping/supplemental_source_assertions、QA required_source_lines 均可从对应引用取得。当前生成器不复制断言正文，符合准备索引定位；未来运行必须取回只有外部指针的原件，不能只看标题。
4. **NOT_RUN 与缺口准确。** 全部业务行 NOT_RUN；41 历史静态条目 REFERENCE_ONLY、runtime_count=0。123/125 原清单 COMPLETE；ITER 保留 99 个已恢复关联与旧30AC缺口，REL 保留122个已恢复关联与旧48AC缺口，均 NOT_INVENTORIED。`README.md:33`、`requirements.json:10`、`:36` 和 `EVIDENCE.md:54` 没有把后续 WP05 补订或247修订通过冒充全量闭环。
5. **独占数据和失败证据要求足以支撑本 DESIGN 任务。** ENVIRONMENT 第9–20行要求具体数据库/schema/角色/业务资源/端口/队列、故障控制、备份恢复和清理 allowlist；第26–38行要求可信租户、普通角色、池复用、实际原生事务来源、原 profile 和连续步骤。EVIDENCE 第24–28行要求每原条目展开精确操作/barrier/允许结果/禁止写集，并给订单断响应、GR叶40/60示例。requirements 提供12域资源、8类故障、9条链；其公共分类不替代原验收，也不以准备文本声称运行就绪。
6. **原任务依赖没有被篡改。** chains.csv 保留当前 task-definitions 的准确 dependencies/card_targets；chain_candidates 仅作按责任域的编排提示，README 第27行明确不得据此增加/删除前置或宣称全目标覆盖。业务Owner写权不因WP14协调转移。

## S01-01 DONE 后的来源漂移与复用

`input-manifest.json` 确实 pin 了旧 roadmap **整个文件**，不是只 pin 派生子集；不能说整个旧 roadmap 字节仍等于当前 main。

本次逐项比较138份来源，只有 `AGENTS.md`、`docs/development/09-parallel-development.md`、`docs/development/roadmap.json` 相对当前main改变。原验收/修订目录、任务定义、125模块及固定评估输入未改变。roadmap 只有 `change_log`、`implementation_tasks` 两个顶层字段改变；生成器实际消费的三个子集逐对象完全相同：

| 子集 | 条目数 | 两版本共同的规范JSON SHA-256 |
|---|---:|---|
| targets | 125 | c9c7de784b75171add08b422fef5e50ff8fc381368b7fb1822482253192ccae6 |
| revision_acceptances | 288 | 803b0e30e1e24744ee6789c06d04c494ed33b52d815ceea9f5fe42bbfd8f04b1 |
| target_task_coverage | 125 | a4b1faf41c2ab18323d0321d1f59cdb6f8210e00042e8ebebd7c13008d88c5df |

规范化为 Python `json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')`。这些派生摘要是本次复用依据，不改写原清单的 Git 字节 hash。

因此可保留 PREP/1.0 的 df5553fd 历史输入与既有映射验证；无须为S01状态变化重建13,404行或改变原验收分母。候选目录 verify-docs 的 source_drift 为空只证明候选工作树与其固定基线相符，**不能当成已比较8570384f主线**。本次独立比较补足了该采用审查。

协调交付说明应引用当前S01已交付的 `CP6-S01-CONTRACT-INPUTS/1.0.0`、实现4258f7b、PR176及远端d589af回执；区分33来源/16 profiles/122向量的输入包与未实施的S01-02..11消费者。旧REPORT第30–32行/主线刷新记录仍是当时事实，不应覆盖成新执行。旧ledger-check不证明当前S01状态或WP14领取；协调者同步共享状态时应运行其当前台账结构门。以上属于既定交付窗口的采用与状态记录，不是候选身份/覆盖缺陷。

## 本次实际验证

- CPython 3.13、`-B`，只读运行 `prepare.py`（未加 `--write`）：PASS；125/123、13,404、12,635分组、247/599、41、9链全部对账。
- 同解释器只读运行 `evidence/verify-docs.py`：PASS；30本地链接、138固定输入、12,635分组、599消费者；元数据反例保持有效。
- 自写只读探针独立完成全原关联集合、全修订消费者对/输入输出字段、源身份分组、当前主线派生子集和138源文件差异比较。
- `git diff --check 8570384f...bd8b7528` 无输出、退出0。`git -c core.longpaths=true status --short` 无输出，候选工作树保持干净。没有凭默认Windows长路径警告误报五份历史文件删除。
- 任务目录常见凭据特征扫描未命中；没有业务/公共合同/EF/迁移/工作流改动。
- 两次临时审查探针曾因错误假设字段名 `tasks`、`case_id` 报 KeyError；实际键为 `implementation_tasks`、修订对象 `id/original_acceptance_id`，调整后独立核验通过。这些是只读探针错误，不是候选产品失败。候选原有初次文档FAIL及其他准备诊断亦保留，未覆写。

## 未验证范围与结论

没有复核原始外部归档全部字节、CRM/Platform当前包、真实PG布局/租户隔离/事务/故障/迁移恢复、真实Owner执行与签收、发布候选或生产部署。没有新执行当前协调台账门，没有进行push/PR/merge或查询/取消Actions；这些不在本只读审查授权范围。

**Critical/Important阻塞问题：无。** 候选满足WP14-01准备任务卡，适合与协调者准确的准入、来源采用、当前台账和四份状态页同步一并交付。不得单凭本报告将业务场景置PASS或把WP14-01提前标DONE。
