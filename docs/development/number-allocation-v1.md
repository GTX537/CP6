# Main 原命令编号分配 v1：限定提供者

本提供者实现 SYS-NUM-01 的独立持久分配，不代表 WP02-16 整卡或任何业务调用者已采用。没有公开领号端点、默认 DI 接线、既有域迁移或生产部署。调用者必须提供独立新 `CP6Context` 工厂、服务端固定 scope 和当前原会话解析器；解析器在本次分配 Context 上验证当前访问身份，业务对象/法人/仓库授权仍由该业务 Owner 在入口负责。不得将客户端 scope、主体或固定 Guid 函数作为生产授权实现。

## 域与版本

第一版只创建全新 Main `Pub_DocSequence(TenantId,BizKey)` 租户域。保留原 BizKey 的 PostgreSQL bpchar/排序规则、50个UTF-16单元容量，Prefix/DateFormat仍20；序列宽度是最小补零宽度，不能据此把合法五位号码截成四位。计数器保持正 int 上限，到达 int.MaxValue明确耗尽。`FirstValue`是新域服务器规则内固定起始值，默认1；不是领号请求参数，也不用于接续已有计数器。

Scope保存SourceOwner、完整业务scope、可选公司/仓库、原始创建依据；公司/仓库只是该已有唯一域的说明，不另造同BizKey的第二套计数。RuleVersion、时区、文化、reset策略和格式随登记不可变。业务Owner固定 `BusinessInstant`，按登记时区取年月日周期；每周期保存独立单调计数，回到旧周期继续原计数。不同周期格式造成相同显示号时明确拒绝历史碰撞，不把重号解释为新范围。其他reset值延续原“无周期”行为。

全局域、ERP/WMS/CRM和Common/DocNumber均KEEP_EXISTING。登记遇已有等价BizKey立即 `NUMBER_EXISTING_DOMAIN_KEEP_OWNER`，不接受“CurrentValue恰好为0”作为没有外露号/未决操作的证明。后续采用既有域必须另做完整外露历史与原Owner切换；本版不自动实施。

## 原命令与事务

`AllocateAsync`用原OperationRef、ExpectedRuleVersion、BusinessInstant和BusinessInputIdentity。Owner固定这些字符串；不将operation、版本、业务scope强制为GUID或截短。OperationRef用SHA-256作有限数据库索引键，原字符串/完整输入按JSON UTF-8原字节保留并精确比较；摘要碰撞/同key异义直接冲突，不复用错误结果。不是每层新增签名/证明回执。

独立READ COMMITTED事务持共同IAM边界与登记规则行锁：读取当前权限、查询原命令、选原周期计数、推进计数并写同一receipt，提交前再核当前身份/动作。原结果需要当前读取能力及原主体，同一原输入直接返回已保存结果。已分配号码与结果永久保留，后续业务保存失败、未保存或未知不回收；receipt为ALLOCATED/business UNKNOWN，业务Owner应另查自己的原业务操作，不能把编号提交等同业务提交。

提交响应不确定返回 `NUMBER_OUTCOME_UNKNOWN`，不自动重试。`ReadAsync`查原分配，或以同一原输入重入以恢复原结果。未找到返回明确的无原记录结果；不能在内部换key。计数存储不可用立即失败，历史receipt仍可读；不以业务表max(number)猜续号。第一周期仅在没有对应历史receipt时初始化，FK/不可删守卫防止正常路径丢失已有计数。

## 单写者与管理入口

新域仍占原Pub规则唯一键，但权威计数位于 `cp6_number_allocation_v1.periods`，不是原规则的遗留CurrentValue/CurrentPeriod字段。登记之后旧Next明确拒绝该域；数据库同时拒绝旧规则任何UPDATE/DELETE/TRUNCATE，receipt/scope不可改删，计数仅加一且提交时必须有对应receipt。未登记域保留旧分配与调用者事务/savepoint语义，未被全域切换。

旧SeqController修改/删除/preview对新域返回409，避免用遗留CurrentValue展示伪“下一号”。新Owner的Preview只读实际周期、返回 `isReservation=false`，不消费计数。原管理列表仍是遗留规则目录；其旧CurrentValue不代表新Owner的分配用量。完整规则CAS、历史影响分析、停用/变更、管理页面和实际消费者接线留待对应采用任务，不能通过旧页面修改新域规则。

## 存储与采用

前向迁移 `20261102021000_NumberAllocationV1`只新增PG schema与守卫，不改EF模型、不迁移旧号、不切租户schema。原Scope和规则、输入/结果用bytea保存无损JSON UTF-8；数据库索引限定结构键，长OperationRef不受PG索引页大小截断。号码历史使用有限摘要唯一键并保留实际号码；碰撞保守报错。运行数据库角色不得关闭守卫或直接重写历史。

实际证据和模拟范围见[W254交付](evidence/W254-01/DELIVERY.md)。测试采用真实PG迁移、权限数据、事务、两个以上独立连接和真实提交后异常；主体/企业资料、当前会话解析入口为明确组件模拟，未宣称生产宿主登录或任何实际业务Owner接线。
