# W255 / 第二期D1：准确采用与配置原卡核销

第二期D1核清Main共享连接尚未采用WP02-02新租户连接profile；WP03-01按原完整配置范围核销DONE，WP03-02冻结公开路径NUM/FILE不适用但整卡未来共同服务前置保留。375卡27DONE，原59局部卡余56。继续W256审核Expired持久终态。

单人执行，Main基线`b807ae9da839a8334b49592cf24b3bbbf4b15e1e`；CRM当前`6741882cfa4b500536fb3dd7749291e7a24f782a`。四仓AGENTS与第一期一致、无开放PR，固定席位status/start通过。第二期计划见[三天执行](../../planning/20261102-three-day-cycle2.md)。第一期W252–254最终PASS及全部7任务合并/删除/释放已核对，归档一次；七天/W249不重复。

## WP02-10

当前`Program.cs`的CP6Context工厂使用宿主配置的单一`databaseConnection`，请求级`ITenantContext`由认证后的`TenantMiddleware`从tenant_id写入，CP6Context读取其CurrentTenantId执行过滤/写入盖章；Stock/OperationsOwner在该Context及实际PG事务中执行，原结果/当前授权及库存效果证据来自W44/W50/W249。默认租户兼容路径仍存在，受保护Owner另核当前合法主体/tenant，不能把兼容字段单独当作授权。

WP02-02的`TenantConnectionManager`、受保护目录及普通登录RLS组件有真实验证，但现有适配仅ERP integration和Identity messaging等明确profile。实际`Program`的Main业务Context没有采用该连接管理器，也没有Stock/主业务表的同事务适配。`tenant-connections`、`identity-priority-tenant-isolation`、`provisioning`原报告均明确不接Main业务宿主；不能借它们的合并记录证明Stock采用。

因此将“尚需定位采用关系”改为精确缺口：宿主共享Main连接与新目录/profile之间尚无Main业务EF采用。下一实施必须限定真实主业务表/共享对象/原唯一域、角色权限和事务上下文，再引入明确profile及代表Owner；不把所有表复制到租户schema、不删除TenantId，也不把此关系核对解释为已发现库存泄露。原卡PLANNED保持；代表Owner共同事务成果继续有效。

## WP03-01

按原完整配置scope核销DONE，不要求重做下游询盘/全部隐私业务。映射如下。

|原范围/验收|准确采用与有效执行来源|
|---|---|
|不可变Bundle、政策/日历绑定、配置原命令|W102实际PG配置Owner，原会话/用途/Site/Form、共同事务内容/日历/Bundle/审计/outbox及原结果；同key异义、撤权、部分失败回滚。|
|Draft预览、激活/定时生效、替代退休、陈旧反馈|W105实际Save/Preview/Activate/GET与Node/浏览器：精确[from,to)相邻区间、重叠拒绝、原actor/内容/版本/五分钟proof、禁用后继和原回执保留；预览不签发公开Attempt。|
|完整合法日期/时区/同day/holiday及原Attempt冻结|W102保公元0000假期与精确时间；W247实际HTTP上海/纽约日历和两种DST、合法0000假期，发Attempt后换UTC版本，新GET用新Bundle、原Attempt保留原Bundle/期限。H01仅Asia/Shanghai是冻结合同，不是本配置卡缺少任意时区。|
|可信前置和当前授权|WP02-09既有wave102_delivery已被实际配置Owner采用；W130/131/246补实际源/撤权先后竞争。台账工具直接消费原crm_*字段/CRM采用记录，不借Main协调合并当产品采用。|

引用：[W102产品报告](https://github.com/GTX537/CP6.CRM/blob/d3615db81675ca6cdad2d9c777505d451a552931/docs/development/evidence/W102-02/DELIVERY.md)、[W105产品报告](https://github.com/GTX537/CP6.CRM/blob/14ca04883e5aae7b856b13674d00b6777d1567d5/docs/development/evidence/W105-02/DELIVERY.md)、[W247](../W247-01/DELIVERY.md)。原日志/PG/HTTP/页面成功输入复用，W253仅审核/登录相关变化不影响配置/日历/冻结合同；没有把复用称本轮新运行。企业资料/外部身份明确模拟，产品和数据库实际执行，符合用户验收口径。

## WP03-02

[W135](../W135-01/DELIVERY.md)已核冻结原文LeadNo可NULL，公开/H01四根闭合且无附件字段；W247实际公开提交、旧Attempt/NFC原字节恢复、真实后台到模拟持久消费者补齐限定公开流程。不能为了凑NUM/FILE采用而引入不存在的请求字段或把显示回执当业务号码。

但原卡第四条及WP02-16直接前置仍含未来共同服务消费；W135明确保留整卡前置。本轮不改变原scope/依赖、不把提供者测试写作CRM采用，因此完整卡保持PLANNED。冻结公开路径适用已明确，后续真实Lead编号/附件须正式版本入口、当前授权与原键恢复，完整隐私导出属其原卡，不倒灌当前公开入口。

## 验证与交付

82 related local roadmap tests PASS,including4 cross-repository adoption regressions;2 positive cases failed before implementation. CRM pinned product receipts now resolve their own repository,never Main coordination adoption;mutable/wrong/unrelated refs rejected. Unchanged W102/W105/W247 product PG/HTTP/UI evidence reused;no product tests rerun. One author SPEC/QUALITY review;no Actions/deployment.

核对相关已采用源提交均是所属仓最新远端祖先；来源清单在wave255-audit.json。引用原W105 85项与W247 6项等报告的真实范围，不相加成新的本轮产品测试数。本次发现台账原校验只接受本仓文件，拒绝CRM原固定SHA报告；最小扩展既有采用解析，按实际所属仓取原字段，固定URL仅允许该仓source/remote SHA及docs路径。报告语义及远端事实仍由本次审查核对，工具不假称已联网或证明内容。先运行4项回归见2项预期失败，再运行82项相关测试全部通过；日志wave255-roadmap-tests.log。另做台账生成/一致性、链接及一次完整SPEC/QUALITY审查。未改产品、依赖、数据库或工作流。

375原卡27DONE/2IN_PROGRESS/346PLANNED；125完整目标/247正式验收不批量提升。原59局部卡已核销3张、余56；剩余与部署分别记账。D2/W256继续审核Expired持久终态，D3/W257版本化编号规则CAS；旧编号域不自动切换。

提交前快照：Main/Handoff待正常采用、精确分支删除和官方finish。最终状态由同轮PR及wave255-final-verification.json核销，未提前声称席位释放。

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/231 已采用、准确分支已删除、DEV-2已释放；Main待本PR最终核销。
