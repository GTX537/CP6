# W674-01 · WP02-02 整卡复核

2026-11-16 UTC。复核基线为已核对的 Main `b829ff69893f4c59d12335b27b26bdf88f19f7d7`。本窗口是原 WP02-02 的接续审查，使用固定 DEV-1 和 `codex/w674-01`，不新增实现任务卡。原 `wp02-02-*` 历史分支仍保留；管理器因这些历史引用拒绝直接建立同名前缀任务后，登记本次具名复核窗口。

**结论：WP02-02 保持 IN_PROGRESS。** 原卡所属 17 项正式 PostgreSQL 场景全部 PASS，整个矩阵为 27/27；这证明各场景记录的实际采用范围。整卡仍缺当前 Main 对象及写者的完整存储归属核对，以及已支持的 RMA 存储模型 v2 的兼容归档恢复。下述两项属于已有 S02-00 与 W528 的明确余项，不重新要求已通过的场景执行，也不要求将全库搬入租户 schema。

## 原卡与已采用证据

直接前置 S02-00、WP02-01 在台账均为 DONE。Main 当前采用正式 Platform 0.10.4，包发布和验签身份沿用 W526；本次没有重新发布或重新验签。范围以 [原卡](../../task-plans/WP02.md#wp02-02)、[S02-00 设计](../../../design-handbook/architecture/02-postgresql-tenancy.md)及[正式矩阵](../../acceptance/postgresql-tenancy.json)为准。

| 原卡要求 | 当前实现和实际证据 | 本次判断 |
| --- | --- | --- |
| 可信租户到存储映射，缺 scope 或碰撞拒绝 | `TenantStorageBindingV1` 从 GUID 派生 schema；目录限定 host/database/model/LOGIN，`TenantConnectionManager` 只接受当前可信上下文。PGT-001/003/005 见 [W426](../W426-01/DELIVERY.md)、[W425](../W425-01/DELIVERY.md)、[W421](../W421-01/DELIVERY.md)；缺失/伪造输入另由 PGT-004 [W514](../W514-01/DELIVERY.md)覆盖。 | 已采用路径满足。 |
| A/B 连接复用、取消和回滚后准确读写 | PGT-002 在实际宿主完成顺序和并发各 100 轮；PGT-010 保存点、超时、HTTP 取消及坏连接替换；PGT-023 普通池预算、凭据轮换和排空。见 [W421](../W421-01/DELIVERY.md)、[W432](../W432-01/DELIVERY.md)、[W344](../W344-01/DELIVERY.md)、[W424](../W424-01/DELIVERY.md)。 | 原实测输入复用，不将 N1000 或生产容量记为通过。 |
| EF 模型缓存与明确映射 | PGT-012 核对 schema/provider/layout/model/designTime；PGT-024 实测同库 A v2 与 B v1、共享最低版本、旧写者拒绝和应用回退。见 [W421](../W421-01/DELIVERY.md)、[W528](../W528-01/DELIVERY.md)。当前 `RmaTenantReadContext` 保留相同模型键与两套映射。 | 已采用模型满足；恢复兼容范围见下文。 |
| 运行/迁移分权与显式跨 schema 拒绝 | PGT-006/007/009 的普通 LOGIN 直接 SQL，PGT-008 共享 RLS 与密钥列拒绝，PGT-027 原租户列与复合引用。见 [W417](../W417-01/DELIVERY.md)、[W427](../W427-01/DELIVERY.md)、[W434](../W434-01/DELIVERY.md)。 | 已登记对象/profile 的实证满足；不能外推到未分类对象。 |
| 逐租户初始化、版本和原结果 | PGT-018/019 覆盖两进程迁移互斥、事务中断、非事务索引和共享升级失败；[W350](../W350-01/DELIVERY.md)、[W353](../W353-01/DELIVERY.md)、[W433](../W433-01/DELIVERY.md)保存初始化和同键原件，W433 还覆盖实际 Main 创建到 Core/RMA 首装。 | 已采用初始化路径满足。既有业务数据迁移另行处理。 |
| 同快照恢复与旧消息/当前安全状态 | PGT-021/022 在原 RMA 存储 v1、归档格式 v11、Stock/ERP/身份/文件共享切片的登记范围通过。见 [W526](../W526-01/DELIVERY.md)、[W527](../W527-01/DELIVERY.md)。 | v1 的正式通过保持；不推导 v2 已可恢复。 |

以上归本卡的正式场景为 PGT-001、002、003、005、006、007、008、009、010、012、018、019、021、022、023、024、027，共 **17 项**。其余 10 项由各自原卡负责；本次逐条按矩阵的 `implementation_task` 核数，不把其他卡的场景改归本卡。

## 真正剩余范围与下一步

### 1. 当前 Main 存储对象及写者的完整归属

S02-00 §3 明确要求对实际 EF 模型、raw SQL、视图、触发器、函数及 sequence 登记 Owner、物理位置、租户列、全部唯一域/FK、写者、事务域、保留和恢复依赖；未分类对象保持原位置，新 schema 路由不开放。这个要求不等于一租户一份全库，也不要求提前实现其他业务卡。

现有 [328 表 EF 准备清单](../WP02-02/tenant-connections/postgresql-ef-relational-inventory-7837009a.json)固定在 `7837009a`，其 `limitations` 明确没有完整列出 raw SQL、迁移对象、写者、Owner、保留与恢复依赖。[ERP 八表清单](../WP02-02/erp-tenant-isolation/OBJECTS.md)覆盖了该共享域；Stock、RMA、身份及 PGT-017 各报告提供了进一步的实际采用证据，但尚未汇合成当前 Main 的完整对象归属。PGT-017 的写者/授权核销不替代对象唯一域和恢复依赖分类。

**下一实施窗口先完成此项。** 以当前已采用模型和迁移为输入，复用上述清单与各 Owner 报告，补齐实际 raw/bulk/后台入口及非表对象；对每个对象明确“继续共享”“已采用租户布局”或“尚未开放新布局”，保留全局唯一键与原 Owner。输出当前对象清单和有源码依据的剩余采用边界。只对发现的真实权限、事务或路由缺口做对应实现与定向验证，不能从未分类直接推导全库迁移。

### 2. RMA 存储模型 v2 的归档恢复

当前 [兼容矩阵](../../../../CP6.WebApi/rma-tenant-model-compatibility.json)允许 `rma-application-core-tenant-v2` 读写，但将其恢复标为 `requires-compatible-archive-and-restore`。[archive.py](../../../../eng/wp02-02/rma-tenant-archive/archive.py)的 `require_v1_schema` 在发现 `rma_model_v2_writer` 时明确拒绝，导出与 [live_recovery.py](../../../../eng/wp02-02/rma-tenant-archive/live_recovery.py)均调用它。[W528](../W528-01/DELIVERY.md)已如实记录这项余量。

这不是归档格式 v2/v11 与租户存储模型 v2 的同一版本轴。不能删掉拒绝门冒充支持，也不能用 W526 的存储 v1 结果关闭它。后继需给已启用的存储 v2 定义兼容归档/恢复版本，在隔离 PostgreSQL 用模拟数据验证真实 v2 写入、完整共享切片、普通角色读回、当前安全状态、同键恢复、失败回滚及 B 租户保护，并保留已有 v1 路径。单独沿用本卡窗口实施，不涉及现有环境切换。

## 清除旧待办，保留各自验收边界

- `remaining_scope` 中的 PGT-022、024 分别由 W527、W528 通过；PGT-011、025 分别由 [W529](../W529-01/DELIVERY.md)、[W530](../W530-01/DELIVERY.md)通过；PGT-017 由 [W645](../W645-01/DELIVERY.md)正式核销。它们不再是本卡未运行场景。
- 旧 `scope_reconciliation.remaining` 中的正式包、Stock 候选、现行安全状态、原恢复批准链和合法 epoch 路径由 W526/W527 及所引已采用证据取代；不存在的非 C03 原图不补造为前置。历史报告和原失败记录保持原样。
- WP02-03 保持 PLANNED，当前授权最终门、字段级来源和具体消费者仍按原整卡复核。PGT-017 PASS 直接作为既有证据，不重新列为 NOT_RUN，也不据此把 WP02-03 或整个 SYS-IAM-01 标为完成。
- 原询盘业务验收保持 60/60；375 张任务卡保持 38 DONE / 1 IN_PROGRESS / 336 PLANNED，125 个完整目标的状态不变。生产发布、部署和既有数据库切换不在本窗口范围。

## 本次验证及交付

本次只更新复核报告、台账、生成进度页、协作入口和四份项目状态文件，未改业务代码、数据库模型、依赖或工作流。核对当前源码与上述报告，并检查原 W526/W528 运行记录和 W645 远端采用回执。绑定、池与租约实现自 W331 后未变；RMA 模型和归档拒绝门的最近采用为 W528。实际业务证据复用原隔离 PostgreSQL 18.6、普通运行角色及明确模拟数据的执行结果，原 SHA、TRX、失败记录和未测范围不变；本次不宣称新跑业务测试。

台账结构/生成一致性、必要链接和一次完整 diff 审查已通过，`D:/CP6-test-artifacts/W674-01/roadmap-check.json` 保存本地检查结果。核对仅本卡和 WP02-03 的过时余项/接续记录变化，旧领取与窗口历史保留，所有任务、目标及业务场景状态未提升。该结构检查只证明登记一致，不是业务验收。工作流预检沿现行配置：普通工作流为手动，R2 candidate 只匹配版本 Tag；本窗口分支推送和 PR 不命中。Actions 启动、取消与生产部署为零。

本次复核窗口经正常 PR 合入并核对远端后，使用工作区管理器按准确源 HEAD 收尾，定向清理 `codex/w674-01` 并释放 DEV-1。固定目录保留复用，根目录用户资料及历史 `wp02-02-*` 引用不动。实际采用 SHA、分支处置与席位释放见本窗口 PR 和 `D:/CP6-test-artifacts/W674-01/main-finish.json`。WP02-02 的未完成实现范围保留在台账，下一步从当时最新远端 main 领取对象归属核对窗口。
