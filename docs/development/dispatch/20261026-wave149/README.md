# W149 原生币种政策 Owner 与准确来源读取

COORD本对话单人，使用brainstorming/writing-plans设计核对及executing-plans/TDD实际执行，项目授权的精简验证与一次集中SPEC/QUALITY优先。W148已完成，原七天CP6-7D-20261022保持ACTIVE。

## 合同与设计

WP04-33要求原产品主账经营身份/UOM/币种/工程准确来源，不造平行产品表。当前只有FxRate与旧代码，不能证明独立币种政策；ERP-EST-01原主册L80/320/443要求code、policyVersion、minorDigits0–4、priceDigits0–6、HALF_EVEN及实际Owner引用。故新增明确Main.Erp.CurrencyPolicy.v1原生Owner，采用已有Main当前授权/事务/原结果基础；不将JPY、旧标签或FX自动升格为政策。

政策稳定Tenant+UUID与原样Code、不可变creator、追加版本/原始UTF8/digest，CAS修订与停用；同码不能覆盖身份，停用仍留历史。查询/新增/修订独立erp-currency-policy动作，scope1创建者、2/3/4真实部门、5租户；创建时显式可信部门及授权版本，映射/政策/审计/原结果同事务。只有当前准确Owner/id/version/digest且enabled可供新采用，历史仍当前授权读，旧版本/ABA不因同码恢复有效。

金额计算只提供按准确政策的HALF_EVEN量化观察，使用BigInteger保持输入字符串精度，不经JS Number/CLR decimal；amount/price明确选择，正确处理正负中点、零和大数。不汇兑、不把量化当财务审批或实际业务采用。原ProductMaster并无币种字段，不补造产品币种绑定；EST最终同根Binding/工程双Owner/制造与SCHED消费仍由后续实际消费者完成。当前三层方案中：复用FX缺政策身份，使用GenericCode缺独立版本/权限，原生政策Owner可保留原接受合同，因此采用第三项。

新增存储只PostgreSQL，表锁与实际同DbContext事务绑定；精确读取参与者核当前权限、准确引用与原文。原操作同键同体返回原结果，异体拒绝；UNKNOWN只查/close，close挡迟到。页面采用既有公开恢复引用/Web Locks、身份时代、CAS和当前动作；输入明确选择，无默认JPY或自动授权。

## 任务与预计实际耗时

|任务|预计|
|---|---|
|原生版本/准确读、独立权限与创建部门、事务/原操作|50–75分钟|
|独立页面、精确量化及PG/HTTP/UI定向验证|45–65分钟|
|一次集中审查、单份报告/台账、两仓正常PR与finish|25–40分钟|

合计120–180分钟，最终只计Main START至finish，不叠加Handoff。固定DEV-1/2复用；前置核对wave149-preflight.json PASS，四仓无开放PR/活动BUG、无本任务Actions、无重复任务。

## 实施步骤与文件

- [x] CP6.Tests/Erp/CurrencyPolicyOwnerPostgreSqlTests.cs先以真实Program/Kestrel冷入口404红灯，证明原生Owner缺失；后续扩展身份、历史/停用、同码不可复用、CAS、回滚、权限/部门/租户、原键关闭和精确量化。
- [x] CP6.Core/Services/Erp/CurrencyPolicyContractV1.cs、ValuesV1.cs、StoreV1.cs、OwnerV1.cs定义原始版本/验证/存储/事务Owner；OperationsAccessV1.Currency.cs实现独立范围与一次创建绑定；OperationsOwnerV1及IamStandaloneDepartmentMappingV1登记准确资源。
- [x] CP6.Persistence.PostgreSql/Migrations/Core/20261026180000_CurrencyPolicyOwnerV1.cs创建原生表/不可变约束和菜单能力，不授予角色；CP6.WebApi/Controllers/Erp/CurrencyPolicyController.cs与Program.cs提供无缓存API/真实DI。
- [x] cp6.web/src/api/erp/currencyPolicies.ts、auth/currencyOperationRecovery.ts与views/erp/CurrencyPoliciesView.vue，router及IAM原部门界面接线；相关页面/恢复测试验证UNKNOWN不重发、准确字符串/引用、身份变化清输入和拒绝迟到。
- [x] 本地真实PG/HTTP定向测试、相关Vitest、vue-tsc/Vite；失败分析后只重跑受影响项，成功输入不变直接引用。数据库只任务隔离fixture；新行为红绿保留。一次完整diff审查与必要链接/台账检查。
- [ ] 单份docs/development/evidence/W149-01/DELIVERY.md；台账只新增WP04-33有限记录并按final proof归档W148一次；正常PR、远端采用、精确分支清理与官方finish，报告分支/工作区/席位。

本轮不运行Actions、迁移/替换既有环境、部署或新增目录。不关闭完整WP04-33/125目标/247验收。后继双Owner工程映射→工程冻结→原D3正式WO与SCHED；D2/D5/D6/D7全部原出口保留。

## 实际交付

见[单份报告](../../evidence/W149-01/DELIVERY.md)。当前登记26.08分钟；前五步完成，集中审查通过，最后生命周期步骤待Main正常PR/finish后由最终proof确认。Handoff已采用并释放，原七天ACTIVE。
