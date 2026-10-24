# W117 真实服务签名上下文与公开提交/H01共同事务

COORD本聊天单人，七天持续目标ACTIVE，PostgreSQL唯一新增数据库。已核对Main `653138213a5e672b6c16e620ee36dd005bf40d3a`、CRM `b406807437bdb7d29f3b39415175237457d9429b`、Handoff `d43a26d8f43a463bad58d60d72dc4bb92a59fcf7`。W116三仓合并、准确分支清理和官方finish均完成，登记51.99分钟（不含前期只读调查及最后核对），证据 `C:\Users\tt\CP6-planning-reports\20261017-foundation/wave116-final-verification.json`；不重复。

## 范围、前置和实现约束

本轮目标为真正可提交的公开询盘/H01主链路：Main同一次真实Basic服务认证及当前注册/权限根锁生成版本化签名上下文，绑定实际客户端、稳定UUID、认证epoch、注册修订、原始C02来源和调用随机数。内层冻结R3 PermissionProjection原样保留，不扩充旧合同、不从公开请求接受身份/密钥/权限。CRM真实服务HTTP调用在业务锁之前完成；持久签名信任、当前服务来源及角色/字段/站点表单/部门/资源在最终原生Serializable事务中复核。

同一个实际Owner组织原始IssuedAttempt消费、稳定请求重放、完整Submission/PublicAttempt及历史、Consent/ReceiptProof、按真实策略NeedsReview或H01转换的完整Lead与历史、编号和Audit/Outbox；只在外层提交成功后返回原合同成功。相同请求重放先于可变前置、限流和消费；不同内容冲突、提交结果未知、撤权/过期/事务回滚保留真实语义。沿用原图v4、七个身份锁、实际xid及first-lock-before-snapshot；不得用备用最小表、假用户、固定NeedsReview或ClientId哈希UUID替代。

新增公开提交HTTP，保持真实传输/Origin/CSRF/browser绑定；公开页面交互随后按同一D6目标接续，完整WP03-02及125目标/247验收未因本窗口自动完成。基于原始R3V15归档核对合同与H01 intake:release门禁，不拿夹具SYSTEM身份作为产品来源。

## 任务与实际执行时间估计

|任务|预计实际执行|
|---|---|
|W117-01 Main真实服务认证、来源绑定与签名上下文|40–65分钟|
|W117-02 CRM在线获取、最终授权、重放/消费、H01原图共同事务与提交HTTP|120–190分钟|
|W117-HANDOFF与总控：定向PG/HTTP验证、集中审查、台账/四状态、三仓合并清理finish|45–70分钟|

合计205–325分钟；登记起点 `2026-10-24T17:06:40.592946+00:00`，各仓不重复相加，前期只读调查另记。使用DEV-1/DEV-2固定目录；Handoff START保存释放后官方bind CRM，交付后再bind回Handoff恢复同分支。无新物理席位，无Actions、保护修改、既有环境迁移或部署。

- [x] 真实服务签名来源绑定与实际Main→CRM在线采用。
- [x] 完整公开提交/H01原图事务、重放和实际HTTP行为。
- [x] 定向验证及一次任务级SPEC/QUALITY审查。
- [ ] 三仓远端采用、准确任务分支清理和全部席位释放。

正式台账24/375 DONE、2IN_PROGRESS、349PLANNED；0/125总目标；247NOT_RUN+41REFERENCE_ONLY保持。局部本地行为验证不冒充正式业务验收。

## 源码核对后的必要范围补充

发现真实crm-intake字段策略变化可能不改变按动作资源生成的C02权限内容。为使已签权限可被当前来源撤销，本轮在既有字段变化捕获中推进原权限来源版本，并在服务签名上下文绑定全部实际角色来源的类型/版本/字节哈希；CRM持锁时比较相同来源。保留原事件合同与原有表，未伪造crm-intake动作或授权默认值。相关字段变化/回滚/C02与签名旧来源拒绝纳入定向验证。

## 实际交付

Main真实Basic服务认证、认证根锁及当前已提交C02来源生成域分离签名上下文，绑定nonce、client/tenant/subject/epoch/注册修订、全部角色来源与站点/表单；intake:release内嵌未扩充的R3权限，public:receive仅身份。字段策略变化同事务推进角色来源与Outbox；两用途均拒绝关闭密钥。

CRM [PR143](https://github.com/GTX537/CP6.CRM/pull/143)已采用 `24f5931b51d7c1f8fbc42cc8b05e24fd7575c974`，来源 `4d1db6563bf6064214ab033d7ff67fbdf69f6a70`；[单份报告](https://github.com/GTX537/CP6.CRM/blob/24f5931b51d7c1f8fbc42cc8b05e24fd7575c974/docs/development/evidence/W117-02/DELIVERY.md)。在线调用在业务锁前，最终实际PG事务复核服务来源/权限/资源，写完整原Submission/PublicAttempt/Consent/ReceiptProof/H01 Lead与历史、稳定消费/原结果、审计和Platform原生Outbox。重放返回原结果不扣额、不重复H01。Unicode保护明确新格式，旧codec不冒充采用；LeadNo按原合同可NULL，未另造编号规则。

122项唯一相关本地成功检查：Main4、CRM118。真实Main PostgreSQL/TLS控制器到独立CRM完整提交；正常/待审/最大Unicode与精确版本、稳定重放/竞争/响应丢失/限额、权限来源、到期/取消/晚期失败回滚均验证。相关读取和Node BFF回归通过；无必需跳过。 联合证据D:/CP6-local-test/wave117/foundation/final-validation.json；早期真实失败和定向修复、未变输入证据复用均保留。自有PG已按原拥有清单及零活动会话停止，失败数据/日志/缓存保留。未运行Actions、修改保护、迁移既有环境或部署。

CRM准确分支删除、DEV-2已官方finish。默认关闭；公开POST BFF/页面、后续管理/隐私Owner新格式采用、Outbox真实发布者和正式验收继续。正式24/375、0/125、247NOT_RUN不变，七天ACTIVE。
Handoff [PR93](https://github.com/GTX537/CP6.AI-Handoff/pull/93)已采用 `eb641466f17e818b009f37e1fbd18b0ab83d9fb1`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟97.50分钟，原估205–325分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。

本提交时Main最终合并/席位收尾仍待，官方finish和final_proof为最终证明；下轮真实开工统一核销，不单独建记账分支。
