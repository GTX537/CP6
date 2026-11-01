# 补货建议、人工草稿、登记与直接移动 v1

W237 按 [REP 设计](../design-handbook/modules/WMS-REP-01.md) 的 D01/D02 推进建议、AllocationPlan、订单和 ActiveIntent。接口为 `/api/wms/replenish/v1`，页面通过现有 `/wms/replenish` 的“补货建议与登记”进入。旧根响应和历史分页保留，旧 PostgreSQL 生成／执行链仍拒绝未采用写入，不调用 MobileTaskV2 代替 Transfer。

## 已实现行为

- 选择原规则和目标引用，服务端冻结上游完整观察与当前 ActiveIntent 集合。同一事务保存可重读的 cut、建议和分配计划；上游原 cut 以完整 Ref 身份保存，允许同 id 的不同版本，禁止同一原件被覆盖。
- 需求维度包含目标、产品、技术、货主和基本单位。现量、未到量与其和使用最多 30 位整数／8 位小数的精确计算；单行命令仍限制 13 位整数／8 位小数。空目标须有明确完整初始化观察，未知量返回 null。
- 未到量按准确根范围求并集；本域已登记义务只计一次，运输关联不叠加。现 3、未到 2、目标 10 的建议为 5。当前有效位置未低于 min 时不补货。
- 来源按正式 sourceRuleRef、完整维度和当前可移动条件选择，依到期、收货时间、来源地点和 Root 稳定排序。只对总量向下取一次 lotMultiple，再截取准确半开范围，按来源地点分成最多 200 个 split。
- 容量使用具名 Domain／Metric／UOM 和准确有理数变化量；同域先合计净变化，LIMITED(0) 不等于无限。未知或不足时保留需求解释但不产生可登记计划；无法准确表示的换算直接失败。
- 批量只接受原建议中的完整 planRef 和 selectionDigest。一次事务内检查所有行的当前规则、需求预算、源范围和原观察版本，再保存全部订单、唯一目标义务、split 关联、原命令结果及待发布事件。任一行或中途写入失败整组回滚。
- REGISTERED 提交即计入 openIncoming，Order/Task/Transfer 关联不重复增加义务。同计划换 HTTP 键或与另一批次的选择重叠，复用原 split/order；不同计划争用同一缺额或来源范围则冲突，不自动缩量。
- 原命令按环境、租户、真人和 commandKey 持久保存；同键异输入拒绝。成功丢响应后按原键查询或同体重试。当前只读权限允许找回历史成功，生成权限撤销不抹除原结果。库位禁用阻止新登记，历史读取仍核当前权限。

## 输入与原件

本域不配置正式 MASTER 规则，也不维护第二套 Stock 账。`ReplenishPlanningStoreV1.Publish` 是可信上游完整读投影的事务入口；原观察包括正式引用、原生地点权限映射、完整目标现量、根范围、条件、容量变化和外部未到范围。登记与观察 head 使用同一数据库事务锁，不能拼接两次独立读取冒充完整 cut。上游投影本身不产生 Stock 或 Claim 效果。

为执行用户已授权的模拟数据生产验收，提供明确的模拟输入入口 `POST simulation/observations`。它必须由服务器配置 `ReplenishSimulation:Enabled=true`，当前真人具备 `wms-replenish:simulate-source` 和全域对象权限，输入 `dataKind` 必须为 `SIMULATED`，租户及环境来自服务器。入口只导入模拟规则／观察，不能伪装为真实 Stock 回执；默认不开启，部署者可在隔离验收环境显式启用。正常业务页面只选择规则，不编辑这些原件。

本轮生产业务验收使用上述模拟上游输入，真实执行 PostgreSQL、Owner、API 和页面行为。真实 MASTER/Stock 的完整 cut 发布者接线仍待采用；这不是等待企业数据，模拟入口可直接运行验收。

## 端点

| 方法与路径 | 返回或作用 |
| --- | --- |
| GET `context` | 当前有权范围、规则、输入类别和依赖状态 |
| POST `suggestions` | `command,scope,ruleRefs,targetLocationRefs`，首次 201，原命令重放 200 |
| GET `suggestions/{id}` / `cuts/{id}` | 原建议、上游原件及该时点本域义务引用 |
| GET `allocation-plans/{id}` | 不可变的完整来源范围和稳定 split |
| POST `batches` | `command,suggestionRef,selectedPlans,selectionDigest`，整组登记 |
| GET `batches/{id}` / `orders/{id}` / `active-intents/{id}` | 当前交付范围的原登记结果 |
| GET `operations/{commandKey}` | COMMITTED 原结果或 UNKNOWN；空查询不表示无效果 |
| POST `orders` | `command,input:OrderInput`，人工草稿首次 201；不计义务、不占库存 |
| PATCH `orders/{id}` | 完整替换 DRAFT，必须带刚读取的 expectedRevision，旧版本 412、冻结状态 409 |
| GET `orders/{id}/history?afterRevision=0&limit=50` | 不可变原版本，nextRevision 表示还有后页，每页最多 200 |
| POST `orders/{id}/prepare` | 必须带当前 expectedRevision；原子登记后等 Transfer 准备时返回 202 |

创建的 `command={commandKey,expectedRevision:null,reason}`；编辑和 prepare 的 expectedRevision 必须是当前订单原版本。选择摘要是按 ordinal lineKey 排序后的 `[{lineKey,allocationPlanRef}]` 紧凑 camelCase JSON 的 SHA-256；分配引用由本服务产生，固定字段顺序为 owner/id/version/digestDomain/codecVersion/digest。输入未知字段拒绝，最多 200 行、2MiB。建议／批量使用 `wms-replenish:generate`；人工创建 add、编辑 edit、准备 prepare、读 query，优先级 1 另需 expedite 和理由。备注沿当前字段读写权限。源与目标地点都须有权，部门范围使用当前健康组织映射。响应 no-store，保留既有认证及 CSRF。

## W238 人工草稿与 prepare 登记

context 中的 draftOptions 提供完整维度和来源原件，页面允许明确选择单一来源地点的 Root 范围和字符串数量，不在浏览器浮点求和。人工可选择正式规则允许的非 FEFO 来源；prepare 仍按准确目标预算、所选范围与容量核算，不能借人工入口绕过其他已登记义务。未保存的编辑必须保存后才能 prepare。

草稿以稳定订单 id 和递增 revision 保存不可变原版本，当前 head 仅指向已保存版本。原创建／修改命令重放返回该命令原结果，不把它替换为当前版本；当前读与分页历史分别提供。完整替换改变维度或来源时清除未冻结选择，所有历史原件保留。迁移为既有批量订单回填版本 1，后续批量创建也登记历史。

人工 prepare 在同一元数据事务内检查当前权限、规则、完整 cut、目标缺额、准确来源与 typed 容量；保存 selection、REGISTERED 版本、唯一 ActiveIntent、prepare 结果、稳定 dispatchKey／operationId／writerEpoch、NOT_SENT 发送知识和事件／原命令。任何写入失败整组回滚，响应丢失按原键恢复。同一选择换键也回原 prepare；批量 prepare 复用既有 ActiveIntent，不再增加义务。来源预算按 Root Owner/id 保留占用，换 Root 观察版本不能重新登记相同物理范围；单个上游 cut 不能同时宣称同根的不同版本。

W238 的元数据 prepare 原结果为 mode=ACCEPTED_TRANSFER、adoptionRef=null、canDispatch=false、blockers=[WAITING_TRANSFER_ADOPTION]；原结果保持不可变。W239 通过 `orders/{id}/transfer-prepare` 消费该原登记，创建实际 Transfer SourceDemand、Request 和完整 Stage；独立批准后推进 PREPARED，后续派发和移动见[直接移动合同](replenish-direct-move-v1.md)。不能把旧 prepare 回执改写为后来已执行的结果；当前状态由订单和 execution-link 查询。

## 剩余范围

W237/W238 的原批量响应仍为 REGISTERED、dispatchSummary=NOT_REQUESTED；它们是原登记时点的结果。W239 当前订单可以推进 PREPARED、TASK_ISSUED、MOVED 或 CANCELLED_NO_EFFECT；Stock 原回执与订单完成同事务保存，实际不确定时保留 RECONCILIATION/UNKNOWN 和 null 数量。

后续继续完整 Transfer ActualFact/Case 与有权后继、通用 RSV Use/Claim 工作流、MASTER/质量专业 Owner 接线、新订单列表分页／导出和历史任务族衔接。W239 已实际执行模拟期初下的整单直接移动、永久无效果停止、同 cut 到货及原结果恢复；此范围不替代整个 REP/Stock/Transfer 模块或 D2 阶段完成。待发布事件已持久保存，外部消费者未在本轮采用。

验证与交付见 [W237 报告](evidence/W237-01/DELIVERY.md)、[W238 报告](evidence/W238-01/DELIVERY.md)和 [W239 报告](evidence/W239-01/DELIVERY.md)。
