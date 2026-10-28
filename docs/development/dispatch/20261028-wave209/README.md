# W209 — 库位类型化规则候选 Owner

COORD单人，持续目标 CP6-7D-20261022；PostgreSQL；复用 DEV-1/DEV-2。W208完整交付及释放核实后只归档一次。

预计实际执行50–85分钟：Owner与原件25–40；独立页面、历史及原操作恢复10–20；定向PG/HTTP/Vue、一次集中审查及正常合并和精确收尾15–25。

落实接受规范WMS-MASTER-01 v1.1.1 §4.1.2/4.3/7.1及08D/08W：合法LIMITED（含有限0且decimal21,8无损）、UNLIMITED和UNKNOWN候选；LEGACY_UNLIMITED仅准确旧来源读取，不可伪造。明确selectorFamily/metric/domain稳定域，政策版本不用于绕开同一Selector竞争。保存空Selector0/null及独立候选原件，不更改当前规则、Location数量、预占、封锁、初始化或生命周期。

DRAFT可保存尚未核定字典引用和null有效期，保留输入身份及UNRESOLVED状态；没有真实参考Owner绑定/批准/时钟/占用保护时不得送审或应用。候选不是单位/操作字典来源或批准。后续来源解析必须新增候选版本，不追改原件。

新增独立规则资源和当前动作/字段权；作用域1–5复用真实Location Creator/部门关联、独立Location和Warehouse query与WMS区域。支持原生旧Location和W208隔离草稿，未知旧生命周期只按当前未删除对象读取，不冒称ACTIVE。整条typed patch、ETag/CAS、规范同值NO_CHANGE不增版，撤回与编辑争同一前驱。版本/审计/Journal/结果同事务；原UUID回查与关闭、当前只读恢复、旧结果不覆盖当前状态。

独立规则页可从原生库位入口进入，展示来源、候选版本、空Selector及准确未批准/未应用状态。字段缺权隐藏整个相关值，身份/路由/失权清理、WebLocks与仅UUID持久恢复，UNKNOWN不重发。

本地真实PG18+实际Program HTTP检查数值边界、原子回滚、同域多候选、更新/撤回竞争、当前权限/租户/部门/字段、不可改原件和恢复；Vue及类型/构建。保持W206/W208隔离和所有原七天出口/125目标/247正式验收；无Actions、既有环境迁移、生产部署。正常PR、远端采用核实、准确任务分支删除、官方finish释放。

## 实际交付

[报告](../../evidence/W209-01/DELIVERY.md)，登记17.52分钟；Handoff已释放，Main待正常PR/finish。完整目标不关闭。
