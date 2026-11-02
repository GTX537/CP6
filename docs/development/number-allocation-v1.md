# Main 原命令编号分配 v1：限定提供者

本提供者实现 SYS-NUM-01 的独立持久分配，不代表 WP02-16 整卡或任何业务调用者已采用。没有公开领号端点、默认分配 DI 接线、既有域迁移或生产部署；W260提供下述格式规则管理HTTP入口。分配调用者必须提供独立新 `CP6Context` 工厂、服务端固定 scope 和当前原会话解析器；解析器在本次分配 Context 上验证当前访问身份，业务对象/法人/仓库授权仍由该业务 Owner 在入口负责。不得将客户端 scope、主体或固定 Guid 函数作为生产授权实现。

## 域与版本

第一版只创建全新 Main `Pub_DocSequence(TenantId,BizKey)` 租户域。保留原 BizKey 的 PostgreSQL bpchar/排序规则、50个UTF-16单元容量，Prefix/DateFormat仍20；序列宽度是最小补零宽度，不能据此把合法五位号码截成四位。计数器保持正 int 上限，到达 int.MaxValue明确耗尽。`FirstValue`是新域服务器规则内固定起始值，默认1；不是领号请求参数，也不用于接续已有计数器。

Scope保存SourceOwner、完整业务scope、可选公司/仓库、原始创建依据；公司/仓库只是该已有唯一域的说明，不另造同BizKey的第二套计数。每个RuleVersion的内容不可变；W257允许通过原修改命令追加格式版本，时区、文化、reset策略和起始值保持登记值。业务Owner固定 `BusinessInstant`，按登记时区取年月日周期；每周期保存独立单调计数，回到旧周期继续原计数。不同周期格式造成相同显示号时明确拒绝历史碰撞，不把重号解释为新范围。其他reset值延续原“无周期”行为。

全局域、ERP/WMS/CRM和Common/DocNumber均KEEP_EXISTING。登记遇已有等价BizKey立即 `NUMBER_EXISTING_DOMAIN_KEEP_OWNER`，不接受“CurrentValue恰好为0”作为没有外露号/未决操作的证明。后续采用既有域必须另做完整外露历史与原Owner切换；本版不自动实施。

## 原命令与事务

`AllocateAsync`用原OperationRef、ExpectedRuleVersion、BusinessInstant和BusinessInputIdentity。Owner固定这些字符串；不将operation、版本、业务scope强制为GUID或截短。OperationRef用SHA-256作有限数据库索引键，原字符串/完整输入按JSON UTF-8原字节保留并精确比较；摘要碰撞/同key异义直接冲突，不复用错误结果。不是每层新增签名/证明回执。

独立READ COMMITTED事务持共同IAM边界与登记规则行锁：读取当前权限、查询原命令、选原周期计数、推进计数并写同一receipt，提交前再核当前身份/动作。原结果需要当前读取能力及原主体，同一原输入直接返回已保存结果。已分配号码与结果永久保留，后续业务保存失败、未保存或未知不回收；receipt为ALLOCATED/business UNKNOWN，业务Owner应另查自己的原业务操作，不能把编号提交等同业务提交。

提交响应不确定返回 `NUMBER_OUTCOME_UNKNOWN`，不自动重试。`ReadAsync`查原分配，或以同一原输入重入以恢复原结果。未找到返回明确的无原记录结果；不能在内部换key。计数存储不可用立即失败，历史receipt仍可读；不以业务表max(number)猜续号。第一周期仅在没有对应历史receipt时初始化，FK/不可删守卫防止正常路径丢失已有计数。

## 单写者与管理入口

新域仍占原Pub规则唯一键，但权威计数位于 `cp6_number_allocation_v1.periods`，不是原规则的遗留CurrentValue/CurrentPeriod字段。登记之后旧Next明确拒绝该域；数据库同时拒绝旧规则任何UPDATE/DELETE/TRUNCATE，receipt/scope不可改删，计数仅加一且提交时必须有对应receipt。未登记域保留旧分配与调用者事务/savepoint语义，未被全域切换。

旧SeqController修改/删除/preview对新域返回409，避免用遗留CurrentValue展示伪“下一号”。新Owner的Preview只读实际周期与当前规则、返回 `isReservation=false`，不消费计数。原管理列表仍是遗留规则目录；其旧格式和CurrentValue不代表新Owner的当前配置或用量。W257已提供限定格式CAS；历史影响分析、停用、周期/域身份变更、管理页面和实际消费者接线仍是余项，不能通过旧页面修改新域规则。

## 格式规则修改与原结果恢复（W257）

`ChangeRuleAsync(NumberRuleChangeCommandV1)`采用原OperationRef、ExpectedRuleVersion、新版本名和前缀/日期格式/最小宽度。只新增这三项格式的修改能力，不改变旧登记/分配合法输入；域、Owner、租户、公司/仓库、周期、时区、文化和FirstValue没有修改入口。完整字符串仍以UTF-8 bytea保存，长版本/命令名不截断，摘要只作有限索引。

与分配共用原登记规则行锁。新修改需要配置的当前读取动作和实际 `pub-seq:edit`，提交前再次核当前编辑身份；原修改结果仅向仍有读取能力的原主体恢复。首先查询原命令：相同key和原输入返回原结果，即使之后又有新版本；同key异义409。新命令的expectedVersion过期返回412，任何已使用版本名不可再绑定，格式不合法422。新版本只影响随后成功的新分配；旧分配重入不要求升级expectedVersion，直接恢复原号码。

版本记录只追加；`rule_versions`中最大连续revision就是唯一当前指针，不另造可失配的head副本。首次修改把原登记规则保存为revision0，随后规则版本和不可变原修改结果同事务追加。原scope原件不改写，周期计数不清零，已有号码不重编、不回收。版本行/原修改记录不可改删或truncate；数据库要求新版本同组有修改结果，并在新receipt写入时核当前版本。格式仍可能在某个业务时间/周期产生历史重号，实际分配明确失败且回滚计数；本版没有宣称预测所有未来格式冲突。

`ReadRuleAsync()`读当前，传版本名读原版本或返回明确的无记录；`ReadRuleChangeAsync(operation)`查原修改。提交响应不确定仍为 `NUMBER_OUTCOME_UNKNOWN`，只用原key查询/重入恢复，不能另造命令或猜测失败。恢复修改不分配号码，也不复活被覆盖版本。

## 格式规则管理HTTP（W260）

基路径 `/api/pub/seq/owner-v1/{ruleId}`，仅适用于已登记的新租户域。实际Main认证/租户中间件确定当前租户；服务器按租户和RuleId读取不可变登记scope，不接受客户端租户、Owner或域说明。每次调用创建独立DI scope，其CP6Context同时供业务Owner和真实当前会话解析器使用；共同IAM锁内及提交前检查原会话、用户/租户、当前菜单动作，拒绝受限改密身份和模拟登录身份。

| 方法/后缀 | 请求体 | 结果 |
| --- | --- | --- |
| GET（基路径） | 无 | 当前权威规则 |
| POST `/versions/read` | `version` | 指定不可变版本 |
| POST `/changes` | `operationRef, expectedRuleVersion, newVersion, prefix, dateFormat, minimumWidth` | 格式CAS修改或同原输入恢复 |
| POST `/changes/read` | `operationRef` | 原主体的已保存修改结果 |
| GET `/preview` | 无 | 当前服务器时间对应周期的样例，`isReservation=false` |

读取/预览/原修改恢复需要当前`pub-seq:query`；新修改额外需要`pub-seq:edit`。撤销edit后仍可按query恢复自己的原修改，撤销query后不得读取。请求体拒绝未知属性、缺失必需值；实际格式与原键合法性由已有Owner负责。版本名和原键通过JSON传递，保留长字符串、Unicode与NUL，不收窄为GUID。POST读取也遵守现有CSRF规则。响应禁止缓存；未登记域或无原记录404、当前动作不足403、原会话失效401、CAS失配412，其他Owner错误保持原状态码。未知提交仍按原操作查询/重入恢复。

这些入口不登记域、不分配号码、不重置计数、不删除原件、不改变周期/时区/文化/起始值。旧Seq管理列表保持遗留目录语义；新域的当前规则与预览应通过本入口读取。前端页面、实际业务领号接线与旧域切换仍为后续任务。实际验证范围见[W260交付](evidence/W260-01/DELIVERY.md)。

## 存储与采用

前向迁移 `20261102021000_NumberAllocationV1`只新增PG schema与守卫，不改EF模型、不迁移旧号、不切租户schema。原Scope和规则、输入/结果用bytea保存无损JSON UTF-8；数据库索引限定结构键，长OperationRef不受PG索引页大小截断。号码历史使用有限摘要唯一键并保留实际号码；碰撞保守报错。运行数据库角色不得关闭守卫或直接重写历史。

实际证据和模拟范围见[W254交付](evidence/W254-01/DELIVERY.md)。测试采用真实PG迁移、权限数据、事务、两个以上独立连接和真实提交后异常；主体/企业资料、当前会话解析入口为明确组件模拟，未宣称生产宿主登录或任何实际业务Owner接线。

W257前向迁移`20261102040000_NumberRuleChangesV1`仅增加版本/修改记录、新receipt版本键及守卫。迁移不读取/重写旧登记或原receipt正文，旧receipt版本键保留NULL；首次显式修改再采用原登记规则。新二进制需要先应用该迁移；同库Owner须一起升级，旧二进制可能展示旧预览，且在规则已修改后新写入被数据库拒绝，不能宣称滚动兼容。旧Pub规则字段继续作为原登记目录，不作为新格式权威。迁移仅在自有本地测试数据库实际执行，未迁移既有环境。W257实际验证与边界见[交付](evidence/W257-01/DELIVERY.md)。
