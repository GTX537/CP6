# W175-01 审批业务绑定与不可变生效时间线

COORD单人，CP6-7D-20261022持续目标。Main基线`13a007fc1b81abb1332b2537c2fb7233dc8babf2`；DEV1固定路径`D:/CP6-worktrees/wave17-dispatch-20261018`。W174已完整收尾，仅归档一次并关闭WP12-02.wave174_flow_activation；不重复执行。

## 实际交付

- 新增ApprovalBindingOwnerV2和`/api/oa/app/v2`绑定配置、修订、停用、历史时间线、能力/选项和原命令查询。采用正式接受记录UA-20261001-B-OA-APP-MD01对应R2原件，历史DRAFT标签由接受记录覆盖。
- PostgreSQL隔离schema保存受控部署登记的Owner/type/subject/schema目录、BindingHead、不可变Revision/Timeline/Intervals、同事务命令原结果及实际权限审计。目录不会自动登记任何真实业务Owner，权限种子只登记read/configure操作名、不授角色。
- 配置体保持oa.app/2.0封闭字段，Owner、Tenant、回调不能由浏览器传入。独立bigint计数以准确8字节大端Base64表示CAS，不复用draft/activation UUID。时刻输出UTC毫秒，配置切点使用UTC秒。
- §16.1排程保留旧时间线字节，有限结束后产生DISABLED区间；既有未来切换发生冲突时拒绝静默覆盖。停用取锁内服务器时刻、取消未来排程并保留历史；重新启用须发布新修订。历史修订指向其首次发布的时间线。
- 实际session/租户、oa.app.binding.read/configure、本人/部门/自定义范围和Configuration字段权限同事务核验；Authentication root→WF_AUTH→命令/绑定。最终权威重验、登记ABA/撤权、独立连接CAS与不可变数据库保护均有本地行为验证。
- EXACT_PINNED校验本Scope准确不可变发布来源、所属flowKey和当前head启用/依赖；动态模式所有pin必须null，配置检查不会把一次校验误记成未来Submission的pin。各规则在登记快照字段白名单内编译校验，不用合成业务值求真假，不调用旧吞错fallback。
- 原命令同body返回已保存结果；异body拒绝。已完成命令在write撤销/Scope停用后仍要求当前read，不能造成重新生效。未知commit返回OUTCOME_UNKNOWN；公共原命令查询返回准确CommandQuery。未观察404不是未提交证明。
- 现有流程管理页增加审批业务绑定tab，实际表单、版本/规则/时间选择、停用排程提示、历史和原命令恢复。身份绑定sessionStorage保存待定配置意图；401/403、登出、切身份清理；晚到结果丢弃；历史成功不冒充当前配置。

## 验证及集中审查

证据目录：`D:/CP6-local-test/wave175/foundation`。

- 23项不同后端检查通过：8项隔离PG18/实际HTTP行为、1项绑定规则编译检查、14项既有表达式回归。`binding-local-20261027T113617Z.json`23项通过；集中审查补齐malformed JSON的400映射后，`binding-final-*.json`仅重跑9项受影响绑定检查。14项未改表达式回归按原来源复用。
- `binding-ui.json`15项mounted Vue/身份恢复检查通过；6项本轮绑定、5项已有启停、4项权限恢复；0skip。`build-reviewed-execution.json`类型检查及构建通过，输出位于隔离证据目录。
- 三次中间构建发现SQL插值括号、测试断言括号和StrictJsonDocument类型问题，业务测试未开始；已修复，原失败日志保留，不改写为通过。
- 一次COORD完整SPEC/QUALITY审查覆盖完整任务diff：合同/当前权限/Scope与原子提交、原结果、未来排程冲突、历史准确版本、独立连接竞争、跨租户/字段/保护缺失、实际HTTP登录和CSRF、UI恢复。审查修正历史时间线、最终读取身份/对象范围、数据库区间保护及错误映射；相应局部验证通过。无需重复全仓回归。

## 实际范围与剩余出口

本轮是WP12-06的绑定配置子交付，**不是完整WP12-06完成**。尚未实现TxS/TxE、Owner真实冻结Subject/Policy与提交Outbox、实例实际启动、TxD真实审批证据、DecisionOutbox、Owner Inbox/实际应用与ApplicationReceipt。旧同步callback/latest/backfill没有被冒充新合同采用。注册表存在不证明服务传输鉴权或实际Owner采用。

规则求值/匹配示例预览、真实提交时动态解析及固定版本、实际服务注册/撤销接收和消费者资格随真实提交接入继续完成；本轮只实现配置时完整语法/白名单与所有候选版本准入。FORM/subflow精确来源原缺口保留。无浏览器连接PG端到端、247真实验收、企业验收、Actions、既有环境迁移或部署。

七天D1/D4完成，其余阶段按原真实范围保留；125完整目标、247验收、任务总状态未抬高。此配置子交付不作为新的业务完成百分比。

## 合并与工作区

Handoff正常合并：https://github.com/GTX537/CP6.AI-Handoff/pull/151；source `7e36893c89b99e0d4f65e1828215e9a769f4998c`，目标`f485dedb409d5223130dfe67a05b28de7fa4bc75`，准确树采用已核对；任务分支已定向删除，DEV2已finish释放。Handoff基准为codex/t02c-entry-handoff-20260921。

本报告提交时Main仍待正常PR/远端核对/finish，不能预先宣称已释放。Main最终结果、准确分支处置和席位状态写入本轮外部final-verification与checkpoint；下轮台账归档读取该实际证据。

预计实际90–150分钟；本报告登记实际26.26分钟，最终以官方Main START→finish为准，重叠Handoff不累计。固定目录与依赖缓存保留，根工作区原件不触碰。

下一步接入原七天所需真实Owner冻结提交和原键启动，继续遵守APP完整事务/证据合同，不用配置或本地夹具代替审批成功。
