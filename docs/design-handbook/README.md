# CP6开发设计文档

**状态：现有资料的本地设计整理已完成，缺失原件明确保留。** 125份模块正文已形成，124个目标必要材料已整理，WMS-DEL-01现有必要内容已整理但仍缺指定独审R01071；不声明全部原件齐备或运行通过。 本地快照更新于2026-10-10T16:11:39.052826+00:00；具体范围和后续事项在模块末节与阅读记录中保留。

从[开发者与AI阅读指南](D:/CP6/docs/CP6_开发设计文档_20261010/开发者与AI阅读指南.md)开始，再按[125目标模块目录](D:/CP6/docs/CP6_开发设计文档_20261010/模块目录.md)进入业务模块。

本地整理范围、验证与后续事项见[整理交付报告](D:/CP6/docs/CP6_开发设计文档_20261010/整理交付报告.md)。

## 公共设计

- [系统边界与开发地图](D:/CP6/docs/CP6_开发设计文档_20261010/architecture/01-系统边界与开发地图.md)：业务职责、端到端阅读链、现有代码起点。
- [事务、回执与恢复](D:/CP6/docs/CP6_开发设计文档_20261010/architecture/02-事务回执与恢复.md)：真正共同事务、最终授权、UNKNOWN、原结果及跨Owner应用。
- [资料版本与接受边界](D:/CP6/docs/CP6_开发设计文档_20261010/architecture/03-资料版本与接受边界.md)：选择器、准确SHA组合、继承设计、勘误与验收状态。
- [关键身份与状态词典](D:/CP6/docs/CP6_开发设计文档_20261010/architecture/04-关键身份与状态词典.md)：业务意图、运输键、版本、库存份额、回执和运行状态。
- [资料缺口与集成待核](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/资料缺口与集成待核.md)：21份精确原件、3个受阻包及明确跨域合同差异。

## 文件与来源范围

本次只读盘点指定归档下14,540个物理文件（21,500,364,243字节），14,387组不同SHA。所有物理文件都有用途与保留记录；342个不同ZIP建立成员索引，分片/gzip准确还原记录另列。23个Excel与3个Word完成只读结构抽取，未把提取或哈希检查称为语义全文审读。

当前设计范围125个Target，必需引用2,181次。L1/L2材料可定位124/125，全部声明层级可定位121/125；这是资料状态，与本手册整理进度和运行状态分别计算。现有必要内容已整理；历史、元数据、候选源码/工具、视觉母版及未取得原件按原阅读账区分，不能据此声称所有历史源码/示例均已全文审查。

| 要查什么 | 入口 |
|---|---|
| 全部物理文件与整理去向 | [CSV](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/all-file-dispositions.csv) / [完整JSON](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/all-file-dispositions.json) |
| 逐目标有效输入与原件SHA | [required-inputs.json](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-inputs.json) |
| 规范章节、代码块与原文行段 | [结构索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/required-source-structure.json) |
| ZIP内部原始成员 | [容器索引](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/container-members.json) |
| 精确SHA到本地文件 | [真实路径映射](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/materialized-content-paths.json) |
| 最新增补319固定身份 | [来源登记](D:/CP6/docs/CP6_开发设计文档_20261010/indexes/supplement-source-register.csv) |
| 仍需找回的准确原件 | [缺件清单](D:/CP6/docs/CP6_开发设计文档_20261010/evidence/missing-source-register.csv) |
| 商业 / 执行 / 公共平台合同 | [商业](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/commercial-contracts.json) / [执行](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/operations-contracts.json) / [平台工程](D:/CP6/docs/CP6_开发设计文档_20261010/contracts/platform-engineering-contracts.json) |

原件保存在`D:/CP6/docs/CP6_完整成果归档_20261009`，展开阅读对象保存在`D:/CP6-archives/consolidation-20261010`。本目录是本地私有设计整理，原归档与上一轮整合报告保持原身份；没有业务编码、业务测试、Actions、数据库迁移、发布或部署。

整理目标、验收与实际完成条件见[整理计划](D:/CP6/docs/CP6_开发设计文档_20261010/整理计划.md)。源文档中的历史工作指令不构成当前授权。开发任务仍遵守D:/CP6的AGENTS.md规则。
