# W93-01：出库路由独立Owner与原出库事务内参与

Issue [494](https://github.com/GTX537/CP6/issues/494)，COORD本聊天单人。Main基线`5f983ff53a542f9bc67521fdc048a44cf56cb214`；任务`BUG-494-ROUTING-OWNER`，固定DEV-1 Main、DEV-2 Handoff原仓复用，没有bind/新增目录。Main实际start2026-10-24T01:56:50.559476Z，原估45–70min＋独立前置约4min，最终按finish计；Handoff不重复累加。[范围与前置](../../dispatch/20261024-wave93/README.md)。W92已完整收尾并归档，不重复。

## 实际行为与边界

ROUTING-OWNER/1新增分页原记录与单条观察。当前原会话/租户、路由query与实际目标仓库query分别校验；租户、原UUID routing-rule与warehouse各自部门2–4/本人范围、实际仓库授权、软删除在计数分页前交集。冻结查询，单页1–200，SortOrder/Id稳定次序；前后权限/组织版本重核和原生表锁防止并发源变更穿透。RuleName/CustomerCd/ProductCdPrefix/Remarks字段MIN，隐藏条件明确HIDDEN，客户空字符串仍旧EXACT，产品空前缀仍旧ANY；这不是正式策略ANY。真实8byte ETag、原OutType/启用标志/目标原UUID保留；未记录Domain/Site、正式策略版本与Selector均不推断。没有全局快照声明。

旧公开list先当前query再409版本要求；旧preview先独立preview再503 Source/Policy采用缺口；三个旧写先当前add/edit/del及适用原对象门，再503正式策略写采用缺口。没有业务或库存效果；NoOp/feature-disabled不再以空列表/用户给定fallback绕过Owner。当前查询独立于legacy resolver开关，因此关闭解算功能仍可读取有权限的真实记录。HTTP no-store并保持403/404/409/503，不吞为400。新增query/preview仅动作定义，种子与前向迁移不授角色；新原对象映射由原生触发器校验同租户实际规则和有效目标仓库，保留既有kind与不可变身份。没有EF模型或既有环境变更。

唯一生产内部消费Outbound Allocate使用程序集内部参与入口，接收原Owner access及实际header/detail UUID，精确校验同CP6Context、同原生transaction实例、同tenant与Outbound.allocate；在当前Outbounds范围中重新读取Confirmed原单及归属明细，由存储字段定义customer/product/type/fallback。过期access、新ambient事务、错context/动作/明细拒绝，不嵌套独立路由Owner或依赖管理query权限。关闭功能使用原单仓库；启用功能沿用现有旧规则/优先级/兜底，最终库存选择仍经原Out Owner仓区/库存范围与同事务，最终权限故障完整回滚。该历史行为不声称正式RSV、RouteDecision、RouteSelector、Source/Stock共同UoW或KERNEL_ACTIVE采用；正式合同的NEVER/显式FallbackMode、不可变版本和审批/CAS不得由此旧算法替代。非PG历史代码与回归保留，不增加SQL Server实现/验收。

路由页改为真实当前分页/目标仓库筛选/明细。明确旧启用标志、feature状态及未知正式状态；不提供虚构的创建/审批/激活/预览。身份、查询条件、权限撤销/目录刷新、乱序响应与卸载清空旧显示。仓库候选继续W92当前Owner最多200项码搜索；本窗补上权限目录刷新而query布尔值不变时的候选失效。IAM可搜索真实routing-rule UUID并作明确部门绑定。

## 验证与一次集中审查

25独立.NET（19原生PG、5既有InMemory路由/分配回归、1两租户权限种子）及42Vue通过，无跳过；相关构建和vue-tsc通过，实际去重67项。作者集中SPEC/QUALITY审查全部产品diff、新DTO/Owner/迁移、现有内部消费、权限/字段/范围/原生锁/故障回滚、旧五入口及NoOp、UI迟返/身份/权限刷新与正式合同边界；补一项仓库候选目录刷新迟返行为验证，无剩余阻断。

- `owner-red-20261024T015839Z.json`：3真实PG红灯，缺Owner时list/preview/create未拒绝，失败保留且转绿。
- `owner-green-20261024T020424Z.json`：新增断言括号错误导致编译失败（TaskStatus没有GetAwaiter），未执行测试/启动PG；修正后通过，保留失败。
- `owner-green-fix-20261024T020630Z.json`：25执行，13通过、12失败；12均复用SeedMaster(h,0)触发辅助方法rows[0]越界，尚未到业务断言。修正为真实一个库位，未修改产品实现来迁就测试。13通过包括3原红灯、内部实际消费/故障回滚/能力边界、完整confirm/allocate/pick/ship、5既有历史回归及权限种子。
- `owner-fixture-fix-20261024T020750Z.json`：实际18/18原生PG通过。过滤表达式未排除已通过的6项，按真实结果去重，不将重复计为新覆盖；实际包含12项修正夹具后的新验证，以及两种功能配置的真实Program/login/HTTP/状态码/序列化、203行分页、字段/范围/部门2–4、原生SQL锁等待、最终权限故障与原生映射/目录重放。前轮其余7项成功结果复用，产品行为/相关依赖未变化。
- `owner-ui.json`27通过（9路由/18IAM）；权限刷新补丁后`owner-ui-final.json`23通过（9路由/14仓库候选相关回归）；最终`owner-ui-review.json`10通过（加1实际候选迟返补验），去重42。`typecheck-result.json`记录最终产品类型检查exit0。未声称现场浏览器或正式业务验收。
- 3自有PG串行55435，ownership/stopped齐，无postmaster.pid，数据/log/TRX全部保留。无Actions、全仓回归、镜像、既有数据库迁移或部署；既有构建警告保持真实。

## 采用与剩余

待Main产品正常PR采用→Handoff DELIVERY/合并/准确分支删除/finish→同Main分支状态收尾PR/准确分支删除/finish→Issue494关闭。完成前不声称席位已释放。固定路径留复用。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN不变。完整RT01–07、WMS-RSV-01/WP10-07/08、真实Domain/Source/Approval/Selector/Stock事务采用及正式O-RSV仍UNPROVEN/NOT_RUN，不能以当前观察与入口拒绝缩小接受合同。七天目标ACTIVE，D2–D7继续，D5/D6真实前置仍需推进。

W89官方跨仓bind自动审批返回`blocked by policy`，CRM未修改；两个原分支可靠保存、deferred席位已释放，明确确认仍待答复，不重试或绕过。历史W51指定停止data删除拒绝边界保留。
