# W29-353：完整整数角色身份兼容

2026-10-20，COORD当前聊天单独执行；`codex/w29-353`，基线`cf186bdb0fa77402a64626513b8c16a1f85fc3a5`。本地可合并：Yes。关联[#353](https://github.com/GTX537/CP6/issues/353)；消费者已先由[CRM PR95](https://github.com/GTX537/CP6.CRM/pull/95)合入`c420cf9e0711bc0c5529d2f709708a57533b341a`，源`725f3fa22aaa15d21c244e0bde004ae6acdd9791`，远端改动blob已核对，任务分支本地/远端删除、DEV-2释放。Main待本PR正常采用后关闭Issue及finish。

## 改动与版本边界

既有Sys_Role完整Int32域不变。新增user.changed.v2、permission.changed.v2、token.revoked.v2（schemaVersion2.0.0），只接受规范十进制完整整数role subject，包括0、int.MinValue和int.MaxValue；不重编号、不丢角色、不更换Owner。旧v1的30份schema/例子和原5条bundle entries保持原字节/内容。新组合bundle1.1.0固定hash `d06c2918d3aeee236a9ecb6b254911ad3a337f249fbac26c5237399644783e6a`，Main/CRM合同资产逐字节相同；现有Platform通用实现支持，无包升级/发包。

原有正整数aggregate继续v1；首次需要0/负数时升v2，用户以后移除这些角色或删除仍保留v2，aggregate ID/分区/Owner/单调版本不变。角色捕获、bootstrap、权限快照、IAM及SSO角色revision使用Invariant十进制。v2 token撤销进入原优先Outbox，事件/业务事实仍同事务；不跳过捕获、不绕过旧消费者拒绝。旧v1-only validator测试证明新域写入拒绝时无部分角色、回执或事务提交。

用户新增/角色分配和字段决策去除错误的正数限制，保留实际存在、当前有效角色、版本、租户及配置域校验。SSO首次身份仍需已有批准政策，0/负数合法默认角色可精确采用且不获得平台管理员身份。前端未新增功能代码：现有用户/授权页以null区分无选择，新定向组件测试核实0的实际勾选和请求。

CRM消费所有相关类型分支，保持严格合同/hash和未知major拒绝。旧撤销Origin保留其原固定v1 bundle hash与事件字节证明；新v2不能伪装旧捕获。新reconciliation Ready仍需当前bundle hash，未来环境采用必须先更新消费者并正常对账成功，再允许新生产域；此处只集成源码，没有部署或启用真实订阅。

## 实际验证与复用

证据根`D:/CP6-local-test/wave29/foundation`，原失败、JSON/TRX/log保留；无整仓/依赖/构建复制。

- `integer-role-red-20261020T065627Z`：2个真实失败，分别为字段决策EFFECTIVE_ROLE_INVALID、PG角色创建IAM_IDENTITY_CONTRACT_UNAVAILABLE。
- `integer-role-green-20261020T071118Z`：83/83通过；完整整数域创建→授权配置→用户分配→原始Outbox/快照、跨仓传递文件、旧消费者全事务回滚、v1/v2合法/非法/敏感数据合同及字段决策。
- `integer-role-writers-20261020T072029Z`：62/62通过；原生PG角色/IAM/identity writer及事务并发回归、0/负数SSO首次身份、v2粘性/删除、token优先Outbox。不同租户、旧正数及原CAS/原子性断言保留。
- `integer-role-baseline-20261020T072310Z`：28/28通过；新增三个v2类型可经原始快照baseline验证，完整目录/漂移/限额/原字节/来源边界保留。
- `integer-role-sso-final-20261020T072527Z`：5/5通过；最后SSO revision改为Invariant后的局部复查，首次SSO原LINK/JIT及新域、文化负号。以上Main集合去重**174项**，0失败/跳过；不与W29旧窗口重复累计。
- CRM `crm-identity-green-20261020T071637Z`56项、`crm-contract-recheck-20261020T072145Z`15项，去重61项全部通过。实际消费Main PG导出的原始事件/快照，证明5个整数身份保持、published compiler依赖完整、0角色停用后权限403、篡改/未知major拒绝。详细范围见[CRM DELIVERY](https://github.com/GTX537/CP6.CRM/blob/725f3fa22aaa15d21c244e0bde004ae6acdd9791/docs/development/evidence/W29-353/DELIVERY.md)。
- Vitest4.1.11：RolePolicyEditor8项通过，UserView修正夹具后6项通过，共14项；`integer-role-typecheck`通过。首次UserView夹具误勾“启用”而非角色，断言真实失败，改为fieldset内实际角色输入后复查6/6；没有修改产品来迎合测试。原日志`integer-role-components`保留。
- W29原先4个授权页面、6个角色页面真实Chrome及W27跨Tab恢复/SSO批准验收按原范围复用；前端产品代码、依赖与环境未变，不重复浏览器运行。现有NuGet/其他项目告警未作无关升级。

## 集中作者SPEC/QUALITY与收尾

完整任务审查覆盖原v1字节和合法域、v2精确边界、生产者选择/版本粘性、文化格式、原子快照/优先撤销、租户及实际有效角色、SSO批准政策、baseline和CRM所有精确类型分支、历史撤销凭据、前端0选择。最后发现SSO revision仍有文化格式拼接，已修正并通过局部回归。当前范围无合并阻断；这是作者审查，不冒称独立评审。

所有本任务PG18 55435隔离实例由夹具停止，没有新的常驻API/Web。CRM无数据库/HTTP后台进程；其工作树已释放，DEV-2后续按manage bind回Handoff并resume原W29-353-HANDOFF通信分支。Main正常PR/远端采用后finish删除准确本地/远端任务分支、释放DEV-1；最终管理登记与协调收尾补记实际结果。四仓合计2开发+1集成，固定目录复用；跨仓替换前正常释放旧席位，没有新增目录/完整clone/副本。

未运行CRM数据库Inbox/session实际持久化、真实DEV或完整Owner采用；其SQL/模型/迁移未改，纯消费及分派验证不能冒称完整DB验收。本次未运行SQL Server集成、Actions、真实租户审批、既有环境迁移或生产部署。完整IAM/CRM/Owner、125目标及247真实业务验收状态不提升。
