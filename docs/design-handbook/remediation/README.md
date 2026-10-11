# CP6 设计修订 R1

本补充包版本为 **CP6-DESIGN-REVISION-20261011/R1**。它把六份台账的115个原编号逐项处置，MRP/MAT两条合并后为114个问题族；复杂问题保留子项。125个目标逐一登记覆盖，未受影响的材料复用原阅读记录。准确完成状态、分类计数和验证结果以本目录JSON及本次验证报告为准。

本轮处置结果为：**10 个设计缺口已补齐、45 个资产矛盾已修订、32 个已有设计待实际采用、13 个有意差异保留、11 个已有后继、3 个入口陈旧已更新**。114 个问题族均有证据和明确去向；实际影响 80 个目标，其余 45 个复用原阅读。验收目录包含 288 条记录，其中 247 条运行场景保持 `NOT_RUN`，41 条为静态设计或资产检查记录。不能把 115/115 处置率换算为业务上线通过率。

先读[版本与效力](01-version-and-authority.md)，再按下面六类设计阅读，最后使用[编码交接清单](09-implementation-handoff.md)。原归档中的历史结论和本次新设计分别保存；新设计不会自动升级某个Owner的实际采用状态。

| 设计 | 开发人员需要掌握的决定 |
|---|---|
| [商业接口](02-commercial-physical-contracts.md) | ACC完整Scope与资格、订单140字段的物理映射、QTN原H07同事务、Demand生命周期/整组切换/原键恢复 |
| [事务与恢复](03-transactions-and-recovery.md) | 完整资源计划与共同锁序、最终授权、全部写者、未知提交和无效果终结 |
| [财务纠正](04-finance-correction-contracts.md) | MATCH/AP取消close、原账事实及金额/来源映射；财务当前性与原结果分开 |
| [计划与材料](05-planning-material-mapping.md) | MAT→MRP责任、已抵/新增抵扣、NET/GROSS、库存/生产/计划准确后继 |
| [类型与样例](06-wire-types-and-fixtures.md) | 金额精度、单位、null-only、生成类型、有界修订及连续样例摘要闭包 |
| [共享与工程](07-shared-consumer-mappings.md) | ITER/OA完整候选集合和持久应用、SPACE/S5字段与撤权、ENG多输出绑定 |

查某个问题时，使用[统一处置表](00-disposition-register.json)中的原编号；每项可追踪原件SHA、决定、消费者、修订输出和[验收场景](08-acceptance-catalog.json)。查模块时看[125目标覆盖](10-target-coverage.json)，不要把“未登记新问题”解释为本轮重新通读全部原件。

修订资产采用原SHA＋准确前置值＋有版本的派生结果。完整派生文件在本地独立缓存 `D:/CP6-archives/design-revision-20261011-checks/`，原归档不覆盖。小型补丁由 `../tools/materialize_design_revision.py` 组合；规划、质检等大样例按各目录recipe生成。资产清单列出准确新SHA与来源，不能按文件名猜采用版本，也不能只改叶节点不更新父摘要。

**设计完成与运行验收分开。** 静态Schema/引用/摘要检查以及独立类型编译，只证明对应文档资产。实际提供者、消费者、全写者和部署注册仍为 `UNPROVEN`；业务合同、并发、撤权、数据库及端到端验收保持 `NOT_RUN`。接入必须按交接清单完成真实验证后才能放开 RequiredFenced。

WMS-DEL指定历史独审R01071已确认无法找回，保留缺件登记并关闭补找行动；本补充包不重建历史原件或伪造过去的接受。原六份发现台账作为历史输入保持字节不变，当前编码以本补充包的准确处置和原有效合同组合为入口。

本任务[集中审查与定向修正记录](../evidence/design-completion-review.json)和[全局静态验证](../evidence/design-completion-validation.json)保存检查范围与限制。Git 为本次修订资产及已记录哈希的两份 recipe 生成器保留原始字节，避免换行转换改变 SHA。公开仓库交付状态以对应 PR 为准；本地快照与 Git 核对结果保存在交付目录的 `LOCAL-SNAPSHOT.json`、`LOCAL-GIT-RECEIPT.json`。
