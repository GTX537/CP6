# WP02-10 卡核销：Main Owner 共同事务和原结果

结论：**卡 DONE**。M 卡上限 4h，强制拆卡点 6h。固定 DEV-1、`codex/wp02-10`；基线 Main `17aa9250d46a0859fcb2ce8ddd4e5bec4b69c42b`（[前卡 PR #1237](https://github.com/GTX537/CP6/pull/1237)）。WP02-02/03/07 已 DONE，核实原依赖后登记开工。本次只核销原定义，不改产品代码、合同、原 AC 或依赖。

## 本卡剩余出口与原 AC

[原卡定义](../../../task-definitions.json)：提供 Main Owner 的 PostgreSQL 同事务、固定命令结果、最终授权 fence、可测故障点；盘点现有 writer 接入方式和原结果只读边界。正向要求真实代表 Owner 的业务/审计/回执一起提交并能原键恢复；反向要求同 key 异 payload、撤权竞争及故障回滚不留半组记录。具体业务消费者逐项采用仍由对应业务卡证明。

| 出口与映射原 AC | 已采用证据及实际范围 | 结论 |
|---|---|---|
| 业务、原结果、审计同事务；PLT-DEL-01 DEL01-A1/A2、DEL07-A2/A3 的 Main 提供方部分 | [W43 原日志](../main-original-operation/DELIVERY.md) PA090 实际 Npgsql 事务保存价格、兄弟传播、审计、原请求与原结果；[W44 库存原操作](../stock-original-operation/DELIVERY.md) 双腿、流水、采番、审计和原结果一起回滚；[W50 Outbox](../stock-outbox/DELIVERY.md) 在同一物理连接和事务保存通知/source/binding，队列不独立提交 | 满足 |
| 原键、同体回放/异体冲突、UNKNOWN；DEL01-A3/A8、DEL07-A6、SYS-IAM-01 AC-IA01-06 的共同恢复边界 | W43 的实际同键竞争、确认丢失、UNKNOWN/close 双序及当前读权；W44 的同键不重复扣增、双腿/序号全回滚及精确原对象身份；[W98 实际 MOVE 页面](../../W98-01/DELIVERY.md) 冻结请求、刷新原键、撤写后当前 query、9007199254740.12345678 精确保留 | 满足 |
| 最终撤权门、无半组提交；DEL07-A4、AC-IA03-09/10 的共同门 | [W435 普通库存双序](../../W435-01/DELIVERY.md) 真实 PG 阻塞链，业务先保留原写集、暂停先则旧会话拒绝且库存/审计/原操作/Outbox 不变；[W645/PGT-017](../../W645-01/DELIVERY.md) 将所有登记 writer、实际后台、财务及 CRM 两库封存的原先缺口核销到 27/27 | 满足 |
| 事务内无外部不可回滚效果、原结果只读、writer 盘点；DEL01-A5/A7、DEL07-A1/A5/A9 的本卡责任 | W50 网络调用在提交之外，Published 不称 OwnerApplied；W43/44 的 GET 只按当前读取授权披露且不改业务/回执/权限版本。W645 实际 writer 表与[当前资源/路由目录](../../../authorization/current-main/README.md)保留已采用入口和各消费者边界 | 满足 |

原编号与断言来自 [foundation 原 AC](../../../assessments/20261011/catalogs/foundation.json)。这是本卡提供方及代表 Owner 出口核销，未将 PLT-DEL-01 的 broker、全部路由切换、retention、replay 和完整业务目标逐项改为 PASS；SYS-IAM-01 跨 CRM/全链部分同样继续由原卡负责。原目标状态不变。

## 当前源码、复用及审查

核对现行 `MainOwnerOperationJournalV1` 的原生 Npgsql 事务约束、`StockMovementService.OriginalOperations` 的最终提交权限和原结果路径，以及 `OperationsOwnerV1` 的实际参与。W43/W44 是原行为证据；W50 补齐原 best-effort 通知缺口；W435 和 W645 证明其后普通身份/共同门的实际采用，旧报告“完整卡未完成”保留当时事实，不覆盖新的原卡定义核销决定。

W43 源 `1ed6753ee3e946e6c6eb44ae58c8c0ccb598f7e0`、W44 源 `acdfcafccb942da4eedef059d31973feafee1902`、W50 源 `68a0ecaad7c900ddb1beea591738b6a14457c9be` 均已进入当前 main；W645 报告保存各后续真实执行的 SHA/环境/日志。历史 PostgreSQL、实际 Program/HTTP、Vue 和精度/竞争回归按其真实输入范围复用，本次不称重新执行。模拟业务输入、真实 PostgreSQL 和宿主范围按各报告注明。

本 PR 只有文档：实际检查原证据/当前入口、严格前置、源提交采用、原 AC 映射、台账生成及完整差异。未改产品，受影响的产品行为为空，不启动全仓编译或重复业务回归。唯一报告即本文件，无其他未验证缺口。所有其他业务卡原出口继续保留。

## 交付和工作区

本卡一个分支、一个 PR、一个报告；正常合并后核对远端 main 包含准确任务 head，再由官方 finish 定向删除本地/远端任务分支，固定工作区干净 detached、停止本任务进程并释放 DEV-1。合并和实际释放结果以本卡 PR 与 `D:/CP6-test-artifacts/WP02-10-card/finish.json` 为准，不提前声称完成清理。

工作流树与前卡完全相同；分支/PR/main 不触发远程编译测试。Actions 启动/取消/触发修改、数据库迁移及部署均为零。下一卡 WP02-04，只在本卡远端采用和席位释放后开工。

本卡有效区间 2026-11-17T12:03:05.756979+00:00 至 2026-11-17T12:05:27.589202+00:00：0.039h；另接续前卡合并/席位收尾 0.111h，计划累计 0.359h，关闭 2 卡、0 目标、0 拆卡。当前卡无剩余出口；阶段 0 和总预算均未到限，未到 24h 检查点。
