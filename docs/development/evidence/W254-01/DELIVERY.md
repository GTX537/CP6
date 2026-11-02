# W254 / D3：编号按原命令持久分配与恢复

D3编号原命令提供者已实现并通过真实PG验证：新登记Main租户域独立持久分配/原结果恢复、不回收、当前权限和同域唯一写者。旧域保留原Owner；完整规则CAS、业务调用者切换、全局域和CodeGen仍为WP02-16余项。三天必做限定交付进入最终采用/释放核销。

## 实现及准确边界

新增NumberAllocationOwner/Contract/Store/Authority v1，限定为全新明确登记的Main Pub_DocSequence租户/BizKey域。[版本化采用合同](../../number-allocation-v1.md)说明全部入口、索引、权限与未接线范围。Scope记录原Owner、业务scope、公司/仓库及NEW_DOMAIN_NO_LEGACY_INTENTS创建依据；维度不暗分裂原tenant/BizKey唯一域。原PG bpchar/排序及合法长度保持，operationRef/规则版本不强制GUID或截短，3000汉字原operation已运行。

规则固定时区/文化/版本与原reset语义；每周期保留独立单调计数，旧周期返回不会清零。最小补零宽度不作为号码容量上限，仍可产生合法五位或int.MaxValue号码；到上限失败，不截断或回绕。新规则FirstValue是可信登记内固定的正int起点，默认1，不能通过请求指定或用于迁移旧计数。

每次调用由新CP6Context独立事务持有共同IAM边界及实际登记规则锁。相同原key/完整输入恢复原receipt；同key异义409。计数推进与原receipt同提交，号码历史唯一、原件不可改删；提交前当前身份/动作再判定。业务事务回滚不回收已分配号，receipt准确为ALLOCATED/business UNKNOWN。真正提交成功但响应丢失返回NUMBER_OUTCOME_UNKNOWN，后续查原结果，不自动重领。缺计数存储快速失败，已保存原结果仍可读，不凭业务表max(number)恢复。

新域仅首次创建，不接管任何已有等价BizKey，即使它当前为0。旧Next对登记域拒绝，原规则UPDATE/DELETE/TRUNCATE由DB守卫拒绝；SeqController改/删/旧preview均409。新Owner Preview读取实际周期且isReservation=false。未登记域原分配/调用者savepoint保持；8条原Seq PG回归通过。原管理列表是遗留规则目录，其旧CurrentValue不是新周期用量；完整管理视图/规则CAS仍后续，不冒称旧页面已采用。

前向PG迁移20261102021000_NumberAllocationV1新增scope/period/receipt及不可变、单调、同提交守卫；不改EF模型、不迁移旧计数或租户schema。无公开任意领号端点、无默认生产DI接线。全局域、ERP/WMS/CRM/Common/DocNumber均KEEP_EXISTING，所有真实消费者需在其后续采用任务绑定真实原会话解析器、权限动作、对象scope和原业务操作；本轮组件模拟Owner不能充当这些消费者完成。

## 实际验证与审查

17 unique relevant PostgreSQL cases PASS/0skip:9 allocation/current-authority scenarios and8 unchanged legacy Seq regressions. Sources allocation-related-20261102T020936Z(16),final-authority-20261102T021202Z(1). Actual fresh PG migrations/transactions/connections/permission data;simulated business actor and host-session resolver,no actual consumer adoption claimed. One author SPEC/QUALITY review;no Actions/deployment.

证据`D:/CP6-local-test/wave254/foundation/final-validation.json`。新服务覆盖原长key/同key异义、独立连接并发不同key和同key、后续业务回滚、实际commit后响应丢失、当前角色撤权/他租户/错误Owner、旧规则与控制器旁路、只读preview、登记旧域拒绝、旧规则版本、实际时区日界与返回旧周期、非法格式、int上限、receipt插入失败整组回滚、跨周期历史显示号冲突、计数表不可用且原receipt可读，以及写后最终当前权限失败回滚。

主体、企业资料及宿主当前会话解析入口为明确组件模拟；PG迁移、持久权限、业务输入、事务、独立连接、约束和提交后异常真实执行。本轮没有Main Program登录/生产HTTP或实际业务消费者集成、前端变更、正式制品/发布环境验证。Ctrl管理拒绝在真实PG控制器方法上执行，不称完整HTTP。七天/W249有效旧证据不重跑。

初始RED实际返回N0001/N0002，准确证明旧接口没有原命令恢复；最终测试改用新版本入口，旧接口仍保留旧语义。首次green的两项失败来自测试AsyncLocal HttpContext在helper返回后不可用，改用隔离宿主上下文后通过，未放宽产品身份校验。失败日志完整保留。自有PG均已由夹具停止，原日志/数据保留。

一次作者完整SPEC/QUALITY集中审查覆盖独立提交/UNKNOWN、当前授权最终点、同域单writer、历史不回收、原合法编号域、版本/时区/周期、DB原子性和精简验证。没有新代理、全仓重构、无关依赖更新、Actions触发/取消、保护变更或生产部署。

## 三天目标结论与余项

D1依赖准确采用规则已交付，WP09-11/WP12-07整卡核销；WP02-10留下当前Main租户/连接采用关系待核对。D2实际切组织/七天审核拒绝与越界回滚交付，CRM Issue164已修；Expired持久终态/独立保留策略仍在WP03-03。D3本期限定提供者交付，WP02-16仍有规则治理、全域实际矩阵、旧域切换、业务消费者和CodeGen。原375卡保持26DONE/2IN_PROGRESS/347PLANNED，125完整目标/247正式验收未越级；原59局部卡当前余57，历史审计保留。候补B1/B2未开工，不属于三天必做出口。

本提交为交付前快照，Main/Handoff正常采用、准确分支清理及官方finish待同轮核销。最终三天目标完成状态、分支处置、工作区与席位释放见同轮PR及`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave254-final-verification.json`，不将预期写成已完成。

Handoff https://github.com/GTX537/CP6.AI-Handoff/pull/230 已准确采用，任务分支已清理、DEV-2已释放。Main最终采用/finish待当前PR。
