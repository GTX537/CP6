# W677-01 · WP02-02 结项与 WP02-03 整卡复核

2026-11-16 UTC。COORD，固定 DEV-1、`codex/w677-01`，已确认基线 `3436248133a64b448b400605b35fbfddaf06896a`。本轮只作原卡范围、当前源码和已采用证据复核；不新增业务运行、不改原设计合同。

**WP02-02 登记 DONE；WP02-03 进入 IN_PROGRESS。** 正式 PostgreSQL 27/27、原询盘 60/60 保持。375 张实施卡变为 39 DONE / 1 IN_PROGRESS / 335 PLANNED，125 个完整目标仍为 0 DONE。

## WP02-02 远端采用与整卡出口

直接前置 S02-00 和 WP02-01 已在原台账登记 DONE，其准确采用分别为 Main PR178 和 Platform PR59。原卡要求可信映射、模型缓存/连接池上下文、运行与迁移权限、逐租户初始化及正反隔离；现有数据切换另行处理。沿 [W674](../W674-01/DELIVERY.md) 的原卡复核，本卡 17 项 PGT 均 PASS，后续两个明确余项现已交付。

| 交付 | 已核对采用 | 结论 |
| --- | --- | --- |
| [W674 原卡与 PGT](../W674-01/DELIVERY.md) | [PR1196](https://github.com/GTX537/CP6/pull/1196)，Main `8dfe0ef0b53852fa6797e986639133d3708f022c` | 复用各项实际场景及其原版本，不把文档复核作为新业务验收。 |
| [W675 当前对象归属](../W675-01/DELIVERY.md) | [PR1197](https://github.com/GTX537/CP6/pull/1197)，Main `2ad52836b4e48ae669aba4046f5daad206fc956a` | 实际 140 schema、891 表、全部 9 种 DbContext 及原生/封闭配置/包写者索引；保持共享键和已采用租户布局。 |
| [W676 存储 v2 恢复](../W676-01/DELIVERY.md) | [PR1199](https://github.com/GTX537/CP6/pull/1199)，候选 `40c5850a03940a6a0e85d068ab3e8e7531482eee`，Main `3436248133a64b448b400605b35fbfddaf06896a` | v12 同快照、五项关闭候选、同源恢复、失败回滚、当前撤权与 B 租户保护已有实际 PostgreSQL 结果；合并后完整树与已审候选一致。 |

W676 核对远端后已关闭 [Issue1198](https://github.com/GTX537/CP6/issues/1198)，准确任务分支本地/远端删除，固定目录干净 detached、DEV-1 已释放。本轮从该主线重新领取同一席位。实际回执为 `D:/CP6-test-artifacts/W676-01/main-remote-main.json` 和 `main-finish.json`。

本卡完成不等于全库一租户一 schema、全数据库毁损恢复、其他业务卡或生产切换完成。v2 恢复沿与 v1 相同的原物理源库缺失行单位，完整支持边界见 [操作说明](../../storage/rma-storage-model-v2-recovery.md)。

## WP02-03 复核结论

对照 [原卡](../../task-plans/WP02.md#wp02-03)、[SYS-IAM-01](../../../design-handbook/modules/SYS-IAM-01.md) 和当前 Main。原卡包括用户/部门/角色/委派的 CAS 与权威版本、认证和业务最终门，以及完整注册字段在 query/write/export/download 各用途的当前决定。共同写入门通过，不自动证明所有字段与输出路径均已核销。

| 原待办 | 当前事实与证据 | 本轮处理 |
| --- | --- | --- |
| 暂停/撤权与业务提交竞争、登记写者共同门 | [W645](../W645-01/DELIVERY.md) 已集中核对 Main 权威/业务/后台、Space 和 CRM 原事务；[PGT-017](../../acceptance/postgresql-tenancy.json) 为 PASS。保留各模拟拓扑、运行身份和真实提交顺序范围。 | 从当前待办移除旧的“PGT-017 未运行”和重复写者补齐要求，历史报告不改写。 |
| 当前身份、有效角色及一致权限来源 | `CurrentPermissionContext` 每次调用读取持久权限；`PermissionAggregator` 的 PostgreSQL 分支使用单语句 `PostgreSqlPermissionReader`。旧 `perm-ctx` 仅保留删除兼容，未读作允许。见 [W13](../WP02-03/current-permission-consumption/DELIVERY.md)、[W18](../WP02-03/consistent-permission-read/DELIVERY.md)。 | 不新增旧 30 分钟授权缓存整改或凭空要求新的缓存协议；既有 RR 快照仍不代替最终门。容量按实际改动风险验证，不另设整卡通用负载门禁。 |
| 字段 V1 真实来源与局部消费者 | [W31](../WP02-03/trusted-field-source/DELIVERY.md) 的持久版本、完整域及先 MIN 后默认已采用。当前 `OrderReadOwnerV1` 明确映射 Read/Export/Download；订单写入、Operations、Flow/Form、ApprovalBinding 和询盘来源均有实际调用。订单的实际 HTTP/字段/导出/下载证据见 [W34](../WP02-03/order-read-owner/DELIVERY.md)。 | 不再写“字段 V1 完全没有真实消费者”；逐资源复用已有交付。`IsAvailable` 不能替代逐用途 Allowed，通用源不替 Owner 当前决定。 |
| 完整注册与四用途覆盖核销 | `Program.cs` 注册后封存 `AuthorizationCatalogV1`；封存只保证该快照，不证明所有入口/DTO/派生字段已完成 Owner 映射。`OperationsOwnerV1` 的通用决策只直接列 Read/Write，其他用途须沿具体消费者核对。当前没有一份完成核销的全 Main 资源—字段—用途—实际入口清单。 | 保留为本卡真实剩余范围；不能从注册调用数、PGT 写者数或纯计算测试推导全部输出已覆盖，也不能据此判定所有未核对入口都有产品缺陷。 |

本轮直接阅读了上述目录、来源、订单和 Operations 的当前实现，并检索实际来源调用。静态检索用于定位，不冒充完整调用图或运行覆盖。空字段资源可能是合法的无可控字段域，不能自动当缺陷，也不为目录封存追加第二套批准登记。

当前源码入口：[目录与封存](../../../../CP6.Core/Services/Sys/AuthorizationCatalogV1.cs)、[当前权限](../../../../CP6.Core/Services/Sys/CurrentPermissionContext.cs)、[字段持久来源](../../../../CP6.Core/Services/Sys/CurrentFieldPermissionSourceReaderV1.cs)、[订单读取用途](../../../../CP6.Core/Services/Erp/OrderReadOwnerV1.cs)、[Operations](../../../../CP6.Core/Services/Integration/OperationsOwnerV1.cs)、[询盘服务字段](../../../../CP6.WebApi/Services/InquiryServicePermissionSourceV1.cs)。

Space 的 `SPACE-S5-AUTHORIZATION/1`、61 字段/78 DTO 与完整 grant-clause 组合属于 [WP13-04](../../task-plans/WP13.md#wp13-04) 的消费者实现。本仓当前未检索到该新组合的运行实现；本轮不把其合同改写为 WP02-03 全量 Space 实施任务。WP02 提供当前 IAM/字段来源，WP13 固定适用版本并在对应采用阶段验收，避免倒置前置关系。

## 下一执行窗口

1. 在当前 Main 建立一份资源—字段—用途核销清单：从封存目录、实际路由/Owner、字段来源调用和既有报告对齐；逐项记 query/write/export/download 的真实入口、映射版本、派生依赖及当前拒绝边界。已批准 legacy、明确关闭、已通过和未覆盖分别按真实事实记录，零字段域保留原义。
2. 只对清单确认的实际缺口继续原 Owner 实现；查询、导出、下载和历史结果披露分别使用当前许可，不把普通读取当导出许可，不改变旧结果身份或合法精度。原有权限、租户、字段、用途和事务边界继续保留。
3. 对有改动的真实路径定向验证隐藏/只读、未知字段、撤权、失败及当前回读，再集中审查并按整卡出口核销。使用模拟数据即可，复用未变证据；不重跑全部 PGT、不等待企业实数、不提前要求后续消费者完整上线。

WP02-03 仍有上述核销与可能的实现余项，本轮不登记 DONE，不提升 SYS-IAM-01 或其他目标。过去 W310/W531–534 等报告保留历史范围；当前入口由本报告与台账取代过时待办。

## 本轮验证与交付

只检查完整文档差异、必要源码/证据引用、台账结构和生成页一致性；没有运行产品测试、构建、数据库或浏览器。业务结果均为明确引用的既有实际执行。台账、生成页、协作入口和四份项目状态文件统一同步。

台账初检因新增复用证据条目缺少来源 SHA、环境或观察时间而失败；补齐准确来源及复用范围后通过，未放宽校验。原始 `roadmap-check.json` 与最终 `roadmap-final.json` 保存在本轮私有工件目录。

工作流及依赖与已分析基线一致，普通分支/PR 不命中版本 Tag；本轮 Actions 启动、取消、触发修改和部署为零。正常 PR 采用后，按准确 HEAD 执行工作区 `finish`。本任务分支处置、干净固定工作区与席位释放的最终结果保存在 `D:/CP6-test-artifacts/W677-01/main-finish.json` 及 PR；本报告不预报自身合并。根目录用户内容保持原状。
