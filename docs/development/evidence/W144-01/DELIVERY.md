# W144-01 原产品主账身份与持久源版本

原ProductMaster头身份来源已具有原生持久版本、不可变历史、当前授权读取及显式部门绑定；正式UOM/币种/双Owner工程映射和最终业务准入仍未证。

## 依据与实际改变

原七天 D3 的 MES-SCHED R2 要求真实 Snapshot/Occurrence/Root 五域及工单同事务；当前工单尚无对应状态，旧工序Status不能替代。WP08-17 / WP05-12/11/20/03/02的前置链落到WP04-33既有ERP产品主账提供者。本轮是该提供者的实际原产品头来源限定窗口，原卡所有硬前置及完整D1–D7出口不变，绝不算作排程写入完成。亦支持D2后续原主数据采用。

- 唯一可编辑来源仍是 `T_ProductMaster`。新 PostgreSQL 迁移保存原UUID、Tenant、持久递增版本与产品头身份/旧资格字段JSON，原始字节SHA256可复核。任意产品头INSERT/UPDATE/DELETE均在原数据库事务记录来源见证；旧ProductService创建/修改/复制及原生SQL都会推进版本。来源改回旧值不复用版本，硬删/重建也不重置。
- versions禁止覆盖/删除；heads禁止删除/重置并仅递增1；源TRUNCATE拒绝，避免无版本清空。两表以Tenant+原UUID作为复合身份，版本表另含版本键；跨租户UUID不混，未新增数据库物理分区或租户schema方案。仅覆盖头字段，不包含工艺/材料/价格子图，不是平行产品主数据、完整产品快照或制造Snapshot。
- 新服务 `CommercialProductIdentityReader` 与 `GET /api/products/identity-owner-v1/{id}?version=N` 使用实际原会话、product原数据范围及独立erp-product:query，在同实际Main授权/原表锁事务读取和最终重核。历史版本也要求当前对象权限；硬删除对象不在API披露，原件仍保留。当前触发器关闭/来源不一致/缺版本即拒绝，不拿旧缓存当当前事实。`IsCurrent`只指该次事务观察。
- 范围1本人、5租户与原默认5不变；2/3/4须现有IAM中的显式product-master对象/部门绑定，服务和配置页均可选真实UUID，缺绑定不推断。迁移仅新增query能力定义，无自动角色授权。旧产品写API未整体改成Owner，本轮只使其原生源变更参与frontier。
- 原QtyUnit只返回代码，旧Status/WfApproval不升格制造许可；所有结果 `REQUIRED_FENCED`。正式版本化UOM/profile、币种、双Owner工程映射、源完整性以及最终业务HoldExact/消费者同事务参加者仍待。不得用此读取替代最终业务准入。原产品代码全库唯一的历史约束保持，本轮未进行多租户主键迁移。

## 实际验证与一次审查

31项当前本地PostgreSQL18/真实Kestrel检查通过，0跳过：11项新行为（含3个部门范围），20项受影响计划/部门/撤权回归。另19项部门绑定UI行为检查通过，包括从搜索结果选择产品UUID并保存原对象映射。只复用固定DEV-1既有缓存和测试宿主；没有把旧测试改称本轮执行。证据：`D:/CP6-local-test/wave144/foundation/final-validation.json`，详细源为final-20261026T130837Z.json及ui.json。

覆盖真实旧ProductService写入/复制、原生SQL、原状态与frontier共同回滚、不可改原件、ABA与硬删重建、原版本恢复、当前登录撤权、显式IAM绑定/撤回、租户和本人范围、源触发器停用拒绝、真实SQL写者等待读取事务、从上一迁移向前迁移已有原记录；既有计划写入503保护仍通过。迁移既有行只记录迁移时实际观察，不虚构此前历史。

保留失败：red路由404；first本地net8不支持ToHexStringLower（已改用现有API）；scoped的旧全库ProductCd唯一约束与测试固定代码冲突（改为独立真实代码）；scoped2实际HTTP撤权500（补上现有OrderOwnerResult，最终403/no-store通过）。未经授权不联网/运行Actions替代本地失败。所有失败日志、数据库证据保留。隔离测试数据库由原fixture确认身份后停止；未迁移既有数据库。

任务级完整diff集中SPEC/QUALITY在提交前核对源字段边界、Tenant/UUID/版本、原生锁/事务、历史/当前授权、SQL原件/正向迁移、API失败映射及台账范围；结果记录于wave144-main-review.json。没有全仓回归、正式业务验收、UOM/工程最终准入、R2/容器发布或部署验证。来源依赖已采用数据库触发器/权限和可信DB运维，不宣称防御特权管理员改库/禁用门禁。

## 总控与收尾

W143按wave143-final-verification.json一次归档CLOSED，WP02-29唯一wave143_managed_payload记录核销；本轮WP04-33全卡仍原状态。Handoff [https://github.com/GTX537/CP6.AI-Handoff/pull/120](https://github.com/GTX537/CP6.AI-Handoff/pull/120) 已正常合并、精确分支删除且DEV-2释放；Main任务分支codex/w144-01待本报告同轮正常PR/远端采用/清理/finish，以最终wave144-final-verification.json为准，不另建文档收尾分支。

原估100–150分钟，截至此Main登记25.39分钟；最终实际耗时以START到finish非叠加计算，含本地验证与运输，前置只读审计不包含在该登记时间，非纯编码工时。固定2+1配额，无新物理工作区；原缓存/用户文件保留。无Actions运行、取消或触发变更；无生产和既有环境迁移。

原七天ACTIVE，仍只有D1/D4完成（2/7只表示阶段关闭率，不是工作量百分比）；375卡/125目标/247运行验收不提升。历史65%和42–70小时不当作本次重新测算。下一步：Continue WP04-33 actual versioned UOM/profile and dual-Owner engineering identity providers, then WP05 engineering source/frozen baseline and original D3 Snapshot/Occurrence/Root same-UoW planning adoption; retain D2 formal Transfer/REP/RMA, D5 qualification/enterprise transport, D6 real policies/registration and D7 integration.
