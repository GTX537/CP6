# WP14-01 全链验收责任与证据准备

准备版本：`WP14-01-PREP/1.0`。依据 Main `df5553fd04ec4fc516602a4e2843650c38ae5006`；当前任务交付状态由协调对话在 [roadmap](../../roadmap.json) 登记。本目录是任务私有准备材料，**全部业务结果为 NOT_RUN**，不新增业务合同或改写原验收身份。

## 交付入口

| 材料 | 用途 |
|---|---|
| [执行计划](PLAN.md) | 基线、允许写入范围、协调窗口、准备过程与已知限制 |
| [125 目标责任表](targets.csv) | 每目标业务 Owner、所属仓库/主体实现任务、完整原定义、修订 ID、数据责任、未覆盖范围 |
| [逐条原验收责任索引](original-assignments.csv) | 13,404 条目标—来源关联；包含 8,456 条 ORIGINAL_AC 和 4,948 条 REQUIRED_SUPPLEMENTAL_SCENARIO |
| [修订运行矩阵](revision-runtime.csv) | 247 个唯一修订 ID，599 条消费者责任；原输入、预期、写集和每个真实消费者 |
| [历史静态参考](historical-reference.csv) | 41 条历史检查，仅 REFERENCE_ONLY，新运行计数为零 |
| [跨域链矩阵](chains.csv) | WP14-02..10 的来源事实、Owner 见证、独占数据、故障要求与原任务前置 |
| [数据与故障需求](requirements.json) | WP02..13 共 12 个数据准备责任域、8 类故障准备要求和 9 条后续链 |
| [环境与数据预约](ENVIRONMENT.md) | 资源独占、可信租户、普通/迁移身份、连接复用、清理/恢复与就绪门 |
| [证据约定](EVIDENCE.md) | 候选输入、实际运行记录、失败保留、共享结果、拒收条件 |
| [输入清单](input-manifest.json) | 固定来源提交和逐文件 SHA-256；不是实现/发布候选清单 |
| [交付报告](REPORT.md) | 实际本地验证、审查、范围限制及协调交接 |

## 责任如何落实

`WPxx/目标ID:Owner` 是业务执行和结果签收责任角色。该角色准备本 Owner 数据，使用真实入口执行、核对本 Owner 持久效果并提供证据；不会因为跨域协调而转移数据写权。`executor_person=UNASSIGNED` 如实表示未来执行人的姓名/对话尚未安排，不阻塞本准备任务，也不表示运行就绪。

单工作包场景由该包协调；跨包场景由 WP14 协调。数据准备责任见每目标 `data_profile`，跨域链参与者分别提供其数据。共享原条目在索引中列出 `coordinator_package` 与全部 `witness_targets`；修订条目列出全部 `execution_owners`。协调者负责预约独占数据、收齐版本/证据、关联一次执行，不写其他 Owner 的业务表。

每目标的主体实现任务来自既有 `target_task_coverage`。`chain_candidates` 按责任域提示可能参加的后续任务；它不增删任务卡的依赖，不意味着这些任务获准同时运行，也不保证该链覆盖该目标全部原验收。未被特定业务链覆盖的原条目仍由本目标 Owner 执行，最终由 WP14-10 核对。

## 原验收不能被简化

`complete_definition_ref` 使用仓库相对路径和 JSON Pointer，定位原条目的完整对象；同时读取 `target_expectations_ref`、`catalog_overlays_ref` 和目标模块。必须包括所有 refinements、supplemental_expectations、current_interpretation_overlays、源行/区间、工作簿单元格、连续步骤、原生字节和前置。某些目录只保存原件指针；未来执行前还必须取得所指原件并核对 SHA，不能把标题或指针解析成功当作完整步骤已读。

原清单保持 **123/125 COMPLETE**；COMPLETE 只表示来源盘点完整。PLM-ITER-01、PLM-REL-01 保持 NOT_INVENTORIED，已恢复的条目照常映射；无法找回的旧 30/48 AC 不由本任务补造，也不再次索要。WP05-01 的版本化补订经真实交付及 WP01-01 采纳后，由协调者明确变更版本、替代关系、范围和新分母，再更新后续验收输入。

13,404 是关联数，不是全域唯一用例数。`source_key` 仅对固定原文件 SHA、原编号及精确位置建立内部共享分组，不是新增原 AC。相同位置的目标级解释仍分别保留；只有全部参与 Owner 的完整断言满足，才能把同一次运行引用给这些关联。不同版本、不同原编号、同名不同位置不能凭标题合并；别名/跨来源等价关系须保留原依据并经任务级审查确认。本次生成的源分组数不是另一个获准业务分母。

247 修订场景始终按完整 `issue_id::case_id` 唯一计数。比如多个问题族都出现 W1-ORD-01，完整 ID 不同，不能按尾号或相同预期合并。一个完整 ID 下的多个消费者共同举证，只计算一次；缺任一真实消费者就不能标记该场景通过。41 历史静态结果永不加入运行通过数。

## 本地复现

从仓库根使用 Python 3.10+：

```powershell
python docs/development/deliveries/WP14-01/prepare.py
python docs/development/tools/manage_roadmap.py
```

前一命令只读固定 Git 基线及本目录需求，核对五份 CSV 和输入清单的完整生成内容、全部指针/责任覆盖与 NOT_RUN 边界。`--write` 只重建本目录这些准备文档，不改共享台账。后一命令只检查当前台账结构和生成视图。两者的 PASS 都不是业务 PASS。

本机裸 python 是旧版；实际使用的 CPython 3.13 路径与执行记录见 [交付报告](REPORT.md)。固定快照不会随协调者更新状态而偷偷重算；采用不同验收来源/任务定义时须明确新版准备材料。执行前由协调者重新核对当前源码和依赖交付。
