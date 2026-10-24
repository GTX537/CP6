# W93 出库路由管理独立Owner

COORD本聊天单人，Main `5f983ff53a542f9bc67521fdc048a44cf56cb214`，Handoff `6a74a70759f9d5e1fc18a5ce662e72a15b692b20`；Issue[494](https://github.com/GTX537/CP6/issues/494)。W92 Main492/493、Handoff67、Issue491已完成，25.62min＋独立前置4min，准确分支删除/固定席位释放。W89跨仓确认待答复，不重试bind。

## 实际前置与范围

最新AGENTS/POLICY哈希未变，Main fetch核对，官方status三席位空闲干净无进程，无重复开放Issue/PR。已完整读当前Routing Controller/Service/interface/entity及页面、WMS-RSV-01手册、WP10-07/08适用跨度；实际Outbound内部消费与原Spec§8、RoutePolicyDraft/RT01–07已核，不声称全文重读4070行或正式验收。原代码没有实际PolicyDomain/Site/不可变版本/ApprovalOwner/Selector，不能以旧CRUD代替已接受正式RT操作。

## 顺序与估计

- [x] PG复现原list/preview/CRUD和NoOp无独立Owner，保留失败。
- [x] 新增路由query/preview动作定义但不授角色，原UUID routing-rule部门绑定、当前独立目标仓库权限/范围、字段MIN与计数分页前过滤，真实8byte ETag/原类型/启用/条件语义保留。观察不是RouteDecision，未知Site/版本/选择器不可猜。
- [x] 旧列表要求版本化观察，旧预览无实际Source/Policy/Selector则零效果拒绝；三旧写按动作/对象门后拒绝未采用正式策略写。启用开关关闭不能绕过当前权限。
- [x] 唯一Outbound内部resolver经原生Owner私有参与入口；精确context/transaction/tenant/parent动作与原单/明细，冻结原输入、最终Out权限范围由原Owner负责。拒绝服务直调/任意ambient事务/过期access；不嵌套独立Owner，不把旧算法称为正式RSV策略。原非PG历史保留。
- [x] 页面原规则分页/筛选/明细，敏感字段隐去，身份/撤权/乱序/卸载清空；IAM部门绑定页面加入实际类型。相关PG/真实HTTP/内部消费/既有非PG/Vue/类型检查，一次集中SPEC/QUALITY。
- [ ] 正常Main产品PR、Handoff采用/finish、同Main分支状态PR/finish、Issue494关闭、精准分支清理/席位释放。每次push/PR核对工作流与过滤，无Actions。

预计45–70min：读取/作用域15–25，服务边界/内部消费15–20，页面/验证/审查/交付15–25；独立前置约4min，W92内重叠预读不重复计。Main实际start `2026-10-24T01:56:50.559476+00:00`，按finish记录，不重复Handoff。固定DEV-1/DEV-2原仓复用、PG55435隔离，保留失败/数据、自有进程停止。

完整RT01–07/RSV/Stock/Source同UoW/RouteSelector/O-RSV仍UNPROVEN/NOT_RUN。24/375整卡、125目标0完整、247运行NOT_RUN不变。七天全目标ACTIVE，不能以此窗口关闭D2/D4/D5/D6；继续真实前置。无既有环境迁移/生产部署。

## 验证完成

25独立.NET（19原生PG、5既有InMemory路由/分配回归、1两租户权限种子）及42Vue通过，无跳过；相关构建和vue-tsc通过，实际去重67项。一次集中审查及权限目录刷新迟返补验完成；PG停止，待正常采用/收尾。
