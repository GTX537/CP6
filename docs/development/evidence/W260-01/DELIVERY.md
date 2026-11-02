# W260-01 / 第三期D3新编号域管理HTTP

第三期D3已实现新登记Main编号域的受权规则管理HTTP：当前/历史版本、格式CAS、原修改恢复及无副作用预览；实际Main登录、CSRF、当前会话与租户生效。D2真实60分钟保留期验收仍在运行，不能称三天全部完成。

## 结果与边界

新增NumberRuleControllerV1五个受权入口，见[接口合同](../../number-allocation-v1.md#格式规则管理httpw260)。RuleId只定位当前可信租户的原登记；客户端不能提交Tenant/SourceOwner/固定域属性。管理动作使用pub-seq:query/edit，原修改恢复保留原主体与当前query要求。JSON体保留长版本/原命令和NUL；缺必需值、未知字段在HTTP边界拒绝，格式合法性复用Owner。没有新领号/登记/删除/重置接口或旧域切换。前端UI、业务消费者、全局域、CodeGen及完整WP02-16未完成。

每个管理请求创建独立DI scope，明确复制中间件已验证租户。新scope的CP6Context同时用于业务Owner与AccessTokenAuthentication/Policy/Directory；IAM公共辅助方法增加显式服务scope重载，既有两参数调用仍使用原RequestServices。Owner在独立READ COMMITTED事务内持共同IAM边界与规则锁，并于提交前检查当前会话与实际权限；不使用固定Actor函数或重复签名包装。登记读取只解析可信数据库原件，不作为授权。

## 实际验证及审查

4 actual PostgreSQL18/Program HTTP cases PASS,0 skipped:format history/CAS/3000-character NUL original recovery/preview;current query-edit grants/cross-tenant/logout;two in-flight revoke cases after waiting for the shared authentication gate. Original missing-entry RED and MVC record metadata failure retained;annotation placement corrected and all4 rerun PASS. W257 unchanged Owner/store behavior evidence referenced,not rerun. No Actions/deployment/new migration.

证据D:/CP6-local-test/wave260/foundation/final-validation.json。使用隔离自有PG、真实Main Program/登录cookie/CSRF/注销，企业与权限行数据为模拟输入；登记/初始领号仅作为组件夹具，HTTP管理路径使用真实原会话。首个当前规则读取在入口缺失时失败；首批实现暴露MVC record属性校验元数据500，改为构造参数注解后4项全通过。所有失败保留，不算通过。

已验证当前和历史规则、3000字加NUL的版本/原键、同原输入恢复、同版本竞争一胜一412、后续版本之后恢复原修改、未知字段/空必需值400、未找到404、预览不预留且旧号码与计数不变；跨租户404、匿名401、撤edit后只允许恢复、撤query后禁止读取、真实注销401；两项等待锁后更改AuthenticationEpoch/撤edit均无新修改结果。W257 26项组件证据的输入/算法未变，引用原报告而不改称本轮新执行。

一次作者完整SPEC/QUALITY审查包括完整产品/测试diff、真实独立会话绑定、租户查询参数、权限最终点、DTO边界、原件无损及文档/台账。没有依赖、数据库迁移或工作流改动。

## 接续与生命周期

D1已按wave258-final-verification.json核销。D2/Main准备分支040695a906c396dd9dd017d355449e4448404b3f已可靠保存并官方deferred，CRM仍在DEV-2等待真实UTC05:44:02截止；本分支引用其准确准备快照，不声明D2通过。Main D3验证后正常合并/远端核对/finish；Handoff在CRM释放后串行发布本START与交付引用。待D2回来，Main W259-01正常合并最新主线保留本次产品并完成整期核销。

原375卡27DONE/2IN_PROGRESS/346PLANNED、原59局部余56保持。本报告提交前快照；最终分支清理、固定工作区及席位释放见wave260-final-verification.json。Main自有PG停止；CRM的独立验收PG仍运行；无Actions或生产部署。
