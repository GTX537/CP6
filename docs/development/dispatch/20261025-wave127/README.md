# W127 原生人工撤回同意 Owner 与页面入口

COORD本聊天单人，七天目标ACTIVE。W126 Main539/CRM151/Handoff102已正常合并，准确任务分支删除，全部固定席位释放；登记29.11分钟。累计登记63.76小时包含等待/暂缓，不是纯编码时间或完成比例，已超初估35–55小时。最新AGENTS/POLICY、台账、冻结R3V15/FX26权限合同和当前原生数据图已核对。

WP03-04限定采用窗口：使用W126真实人工用途privacy:consent-withdraw；当前query与privacy consent-withdraw动作、consent可写字段、主管且管理部门、精确Submission/Purpose/Site/Form。独立稳定privacy.consent.withdraw幂等槽，当前事务持有身份/信任证明；原生完整图锁内原子写入首次撤回时间、撤销关联回执凭据，保留原同意历史事实、原处置和保留deadline。原始结果、审计与明确标记目标本地版本的生命周期事件同事务保存。现有冻结release/reject动作数组不扩充，使用独立服务器计算的撤回上下文。

|任务|预计实际执行时间|
|---|---|
|原生撤回Owner、独立命令记录与原子一致性|35–55分钟|
|默认关闭的宿主/BFF/UI采用|20–35分钟|
|相关PostgreSQL并发/失败/权限/重试、实际路由页面及集中审查|25–40分钟|
|三仓合并、分支清理与官方finish|10–20分钟|

合计90–150分钟，从Main START 2026-10-25T03:14:43.202765+00:00 计一次。DEV-1/Main、DEV-2/Handoff与CRM官方bind串行复用；PG55453隔离测试，显式安装新本地schema，不迁移已有环境。共享写入、数据图及生成资源由本聊天串行处理。

完整WP03-04前置与业务验收仍NOT_CHECKED/NOT_RUN；不关闭完整隐私导出、保留规则推广、Outbox正式发布器或生产验收。无Actions/生产部署/权限自动预授予；PostgreSQL唯一新增目标。正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY保持。

定向风险补充：撤回新增历史后，后续匿名化事件版本须从实际历史数量递增。增加独立PG55454执行原60分钟deadline的真实撤回→匿名化链路；等待期间继续页面与交付准备，仍在90–150分钟预估内。未变更持久deadline、数据库时钟或冻结保留合同。

## 交付准备

79项唯一相关本地检查通过：27项PostgreSQL/HTTP/实际浏览器、52项Web及相关页面类型/浏览器包构建；必需0跳过。包括真实原60分钟保留期，不改业务期限或时钟。 [报告](../../evidence/W127-01/DELIVERY.md)。CRM/Handoff已采用并finish，Main正常收尾待。下一窗口落实privacy:export/download的独立当前真人来源与明确字段用途映射，再实施原生导出任务/加密制品/单次下载及页面。原生Outbox发布、真实授权配置/既有环境迁移/Lead月跨度到期和正式业务验收仍未完成。
