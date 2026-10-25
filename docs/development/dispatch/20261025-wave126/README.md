# W126 人工撤回同意的当前权限来源

COORD本聊天单人；七天目标ACTIVE。W125 Main538/Handoff101已合并清理finish，Issue537关闭，登记31.22分钟。累计登记63.28小时含等待/暂缓非纯编码。Main80af679、CRM4295604、Handoffdec1862；最新AGENTS/POLICY、台账、冻结R3V15与当前源码已核对。

WP03-04限定前置窗口：Main增加准确privacy:consent-withdraw人工用途，要求crm-lead:query和crm-privacy:consent-withdraw；独立版本MainInquiryConsentFieldsV1映射crm-privacy.consent，沿用实际stored-MIN后注册默认语义但撤回须Access1。主管来源仍是当前crm-lead:assign和完整健康部门树，范围交集包括实际crm-lead与crm-privacy数据范围；不借用PII查看权限或服务身份。CRM增加具体受保护会话、CSRF/在线捕获及相同原事务的当前human证明，所有角色和零授权角色来源保持完整。签名证明仍不能单独授权业务。

|任务|预计实际执行时间|
|---|---|
|Main当前动作/字段/部门来源与定向验证|20–30分钟|
|CRM明确人工用途、当前证明与撤权验证|15–25分钟|
|实际Main HTTP签名字节到CRM PostgreSQL及集中审查|10–20分钟|
|三仓合并、分支清理和官方finish|10–15分钟|

合计55–90分钟，从Main START 2026-10-25T02:41:59.224652+00:00 计一次。DEV-1/Main、DEV-2/Handoff与CRM官方bind串行复用；PG55452隔离测试。共享配置/来源由本聊天串行编辑，冻结合同不改，无新工作树/Actions/生产部署/既有环境迁移/权限预授予。

原生撤回Owner、严格ETag/幂等槽、ConsentFacts及完整数据图同事务更新、目标本地版本事件和页面入口留给紧随其后的采用任务。本窗口只是必要的实际前置，不关闭完整WP03-04或业务验收。撤回应立即记录事实但不改已有保留deadline。正式24/375DONE、2IN_PROGRESS、349PLANNED；0/125；247NOT_RUN+41REFERENCE_ONLY保持。

## 交付准备

48项唯一相关本地检查通过：Main17项PostgreSQL/真实登录OIDC/HTTP，CRM29项具体保护会话/HTTP/当前来源，跨仓实际签名/原始快照到CRM PostgreSQL2项；必需0跳过。 [单份报告](../../evidence/W126-01/DELIVERY.md)。CRM151/Handoff102已采用并finish，Main正常收尾待。原生撤回Owner、精确Submission/Consent资源、ETag/幂等槽、原事务即时事实和目标本地版本事件、页面入口紧随后继；撤回不改已有保留deadline。隐私导出/FILE当前下载/嵌套分类与原生Outbox发布仍另行采用。
