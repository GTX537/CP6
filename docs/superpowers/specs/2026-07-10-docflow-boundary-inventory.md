# 在途单据受配置变更影响的逐 DocType 边界盘点

> 只读盘点 · 2026-07-10 · 对应 07-09 配置基建 spec §7.6（§1④/§3/§5）
> 三维度：**环节被关 × 审批解绑/换绑 × 校验器变化**，逐 DocType 给可执行处置。
> 状态：待用户评审；§3 争议点拍板后回写本文件。

## 0. 先说两个影响全表的现状事实（代码实读）

**0.1 审批单源现状：已存在，但有三处差距**

规划中的 `IApprovalService` + `Wf_ApprovalBinding` + `IApprovalCallback` **不是待建，已落地**：

- `CP6.Core\Services\Wf\IApprovalService.cs` / `ApprovalService.cs` — SubmitAsync 防重（(bizType,bizId) 已有 Running 实例即抛）→ 按 Binding 选 FlowKey → FlowEngine 起流程
- `CP6.Entity\DomainModels\Wf\Wf_ApprovalBinding.cs` — BizType→FlowKey 映射，`ConditionJson` 字段已建但 **v1 未实现**（注释明言"预留"）
- `CP6.Core\Services\Wf\IApprovalCallback.cs` — 终态回调，与引擎同事务原子落库（OA2-D5 铁律），现有实现：`PUR_PR`/`PUR_PO`（`Pur\PurApprovalCallback.cs`）、`FinJournalPost`（`Fin\JournalApprovalCallback.cs`）、`A5_Budget`（`Fin\BudgetApprovalCallback.cs`）

与 spec §1④ 的差距：
1. **DocFlowConfig.ApprovalPoints（迁移点→BizType 映射）不存在**——现状迁移点硬编码在各业务服务里（如 `PurchaseRequestService.cs:111` 提交即送审），"哪个迁移点要审批"不可配。
2. **无绑定时的语义分叉（全表最大的现状雷）**：`ApprovalService.SubmitAsync`（`ApprovalService.cs:32-33`）无启用绑定 → **抛异常（fail-closed）**；而采购适配器 `Pur\Contracts\ApprovalServiceAdapter.cs:29-32` 无绑定 → **自动放行 AutoApproved（fail-open，单据直通 Approved）**。同一"解绑"动作在两条路径上结果相反。
3. ConditionJson 条件选流程未实现（信用超限走高额流程依赖它）。

**0.2 WFS 没有版本 pin——口径3"在途审批自然走完"的前提有缺口**

`Wf_FlowDef.cs:8` 注释自认："**阶段1 简化：实例不存 schema 快照，按 FlowKey 取最新**"。`Wf_FlowInstance` 只存 FlowKey 无版本号；`FlowEngine.LoadSchemaAsync`（`FlowEngine.cs:433-437`）对**在途实例**每次按 FlowKey 取当前 SchemaJson，且不过滤 Enable。由此：

| 变更动作 | 在途审批实例实际行为 |
|---|---|
| Binding 换绑（改 FlowKey） | ✅ 安全——实例已记旧 FlowKey，继续走旧流程定义 |
| FlowDef 停用（Enable=false） | ✅ 安全——只拦新提交（`FlowEngine.cs:47` 过滤 Enable），在途 Load 不查 Enable，可走完 |
| **同 FlowKey 原地改 SchemaJson** | ⚠️ **在途实例即刻漂移到新图**；若 CurrentNode 在新图中被删，实例卡死 |
| **删除 FlowDef 行** | 🔴 在途实例 LoadSchemaAsync 抛异常，永久卡死 |

WFS 版本治理在二期计划里但**未开工**。口径3 写"与 WFS 版本 pin 同款思想"，但 pin 本身还不存在——见争议点 #2。

**0.3 口径3 原文与转述不是一个模型**（影响每一行的"环节被关"列）

spec §1④口径3 原文是"**新配置从下一次迁移动作起约束**"（per-action：在途单的**下一步**就按新图走，只是不动已停留状态和已发起审批）；而"在途单据按创建时的配置走完"是 per-doc pin（需要给每张单存配置快照/版本号）。两者行为不同：关掉 Confirmed 后，停在 Draft 的在途单——per-action 下一步直接走备选边 `Draft→InFulfillment`；per-doc pin 则仍要走 `Draft→Confirmed`。**本表按 spec 原文（per-action）写处置**，差异列入争议点 #1。

## 1. DocType 总清单

### 1.1 v1 纳入 DocFlowConfig 管辖（5 个，全部 Sales v2 链）

| DocType | 来源 | 主干状态机（v1） | 可裁白名单 OptionalSteps（建议） |
|---|---|---|---|
| **Quotation(v2)** | 新建 | Draft→Submitted→Confirmed→Converted（+Expired/Cancelled） | **整环节可关**（spec §3 明文）；环节内 Submitted（见积承認步）可关 |
| **SalesOrder(v2)** | 新建 | Draft→Confirmed→InFulfillment→Shipped→Invoiced→Closed（+Cancelled）（spec §1④ 明文） | Confirmed（备选边 Draft→InFulfillment 已预声明）；Invoiced 建议可裁（备选边 Shipped→Closed）——**争议点 #4** |
| **SalesOrderChange（订单变更单,v2）** | 新建（前篇推荐值 #1：进 v1） | Draft→Submitted→Approved→Applied（+Rejected/Cancelled） | Submitted/Approved（变更审批步）可关（小额直改）；**Applied 不可裁**（变更落账原子点） |
| **出货指示/出库（WMS 接缝）** | 现存 `Wms\OutboundOrder.cs`（0下書き→1確定済→2引当済→3ピッキング中→4出庫完了/9取消） | 只管 SalesOrder 的 **Shipped 迁入点**；WMS 内部子状态不进 DocFlowConfig | **无**——Shipped 是主干必经（库存已实扣），白名单为空，配置层无法表达"关出货" |
| **发票（F1 接缝）** | 现存 `Fin\ArInvoice.cs`（Draft→Posted→PartiallySettled→Settled/Reversed） | 只管 SalesOrder 的 **Invoiced 迁入点**；ArInvoice 自身状态机不可裁（账务完整性，Posted 后只能红冲） | 见 SalesOrder 行的 Invoiced |

### 1.2 已在审批单源上、但 v1 不进 DocFlowConfig（4 个——"审批解绑"边界今天已真实存在，一并出表）

| DocType | BizType | 状态机 | 不进 v1 的理由 |
|---|---|---|---|
| PurchaseRequest | PUR_PR | Draft→Submitted→Approved→Converted→Closed（+Rejected） | 审批已可配（Binding），但迁移点硬编码、无环节裁剪需求；待 ④ 机制在 Sales v2 验收后回头迁入 |
| PurchaseOrder | PUR_PO | Draft→PendingApproval→Confirmed→PartiallyReceived→Received→PartiallyInvoiced→Closed（+Cancelled） | 同上 |
| JournalEntry | FinJournalPost | Draft→PendingReview→Posted（+Rejected/Reversed） | 财务过账链法定完整性，环节永不可裁；只有审批维度 |
| BudgetVersion | A5_Budget | Draft→PendingApproval→Approved→Archived（+Rejected） | 同上 |

### 1.3 不纳入 DocFlowConfig（逐类给理由）

| 类别 | 实体（均在 `CP6.Entity\DomainModels\`） | 理由 |
|---|---|---|
| **老 /erp 单据** | `Erp\Order.cs`（双轨：legacy `Status` int + `OrderStatus` 生命周期 CONFIRMED→IN_PRODUCTION→SHIPPED/CANCELLED/PARTIALLY_CANCELLED，**无 Draft 态，出生即 Confirmed**）、`Erp\Quotation.cs`（EstimateCheckFlg 0/9 手工承認+MasterConfirmFlg，非 WFS）、`Erp\OrderDetail.cs:205`（PA090 単価訂正 ApprovalStatus，走 `IPowerEggWorkflowService` NoOp 桩，`OrderService.cs:975`） | 拍板 #6：老模块**冻结只修 bug**，迁移切新后退役；给冻结模块接配置基建=返工。PowerEgg 桩按前篇勘误迁 IApprovalService 备选实现位，不在 ④ 范围 |
| **WMS 作业单据** | InboundOrder、KitOrder、ReplenishOrder、CrossDockOrder、CarrierShipment、MobileTask、WcsTask、SlottingPlan、PaperRoll、Pallet、QcInspection、RmaHeader、SampleStock、RemnantMaterial | 作业执行流，状态由物理动作（引当/拣货/上架）驱动，裁环节=物理断链；非商务审批流 |
| **WMS StockTake（盘点）** | `Wms\StockTake.cs`（0計画→1カウント中→2差異確認中→3承認待ち→4完了/9取消）+`StockTakeDetail.ApprovalStatus` | **自带审批**（`StockTakeService.ApproveAndApplyAsync` 手工承認+差异0自动承認，未走 WFS）。v1 不动；后续可作为"迁 IApprovalService"候选记票，与 ④ 无关 |
| **MES** | WorkOrder（0下書き→…→4完了/9取消）、DefectRecord、Machine | 生产执行状态由报工/设备驱动；完工反冲已拍板走既有链 |
| **FIN 过账类** | Receipt/Payment（出生即 Posted，只有红冲）、ApInvoice、CostSheet、AssetCard/AssetDisposal/DepreciationRun、BankStatement、FiscalPeriod | 账务不可裁不可配，错账唯一出路=红冲，天然与配置变更绝缘 |
| **计划/自动单据** | Plan_PlannedOrder、Plan_MrpRun、Pur\ThreeWayMatch、GoodsReceipt、Rfq、IntegrationEvent | 系统运算/物理收货生命周期；MRP 只消费主干状态（spec §1④ 明文"裁剪对下游不可见"） |
| **OA 单据（稟議書等）** | Wf_FormData/Wf_FlowInstance 本身 | 它们**就是** WFS 实例；其"配置变更边界"=WFS 版本治理（二期波），不归 DocFlowConfig |

## 2. 逐 DocType 边界表（v1 管辖 5 个）

| DocType | 状态机 | 环节被关：在途单处置 | 审批解绑/换绑：在途审批处置 | 校验器变化：存量数据处置 | 特殊边界/风险 |
|---|---|---|---|---|---|
| **Quotation(v2)** | Draft→Submitted→Confirmed→Converted（+Expired/Cancelled）；整环节可关 | **整环节关闭**：只藏"新建"入口；在途报价单不冻结不作废，保留详情/转单入口直至终态（转单产生的 SalesOrder 照常挂 QuotationRef 血缘，只读）。环节内 Submitted 步关闭：停在 Draft 的单下一动作直走 Draft→Confirmed（备选边预声明）；已停在 Submitted 的单可正常走出（不拦离开） | 见积承認点解绑：Running 实例自然走完，终态回调照常推 Confirmed/退 Draft；解绑后新提交的确认动作直通。换绑：在途实例 pin 旧 FlowKey 继续 ✅；**同 FlowKey 原地改版不安全**（见 0.2） | 报价校验（定价/最低毛利等 GuardConfig）只在迁移动作时刻评估：存量 Draft 不回溯，下次提交/确认按新配置拦；改松即过。悬空键（包停用）→迁移拦 E-CONF（spec fail-closed 明文） | ① 整环节关闭后在途报价"能否继续转单"需定死——推荐**能**（在途走完含转单），仅藏新建；可选配套"批量作废在途报价"运维动作（争议点 #5）。② 报价改严后停在 Draft 的旧单可能永远确认不了→必须保证 Draft 可编辑修数，不做"强制过期"自动动作 |
| **SalesOrder(v2)** | Draft→Confirmed→InFulfillment→Shipped→Invoiced→Closed（+Cancelled）；OptionalSteps=Confirmed（、Invoiced 待拍板） | **关 Confirmed**：停在 Draft 的在途单下一动作直走备选边 Draft→InFulfillment（per-action 口径，见 0.3）；已停在 Confirmed 的可正常走出；已过 Confirmed 的无感。**开回 Confirmed（反向）**：停在 Draft 的在途单下一步需过审批——在途单遇新增环节，可接受但要写进运维须知。**Shipped/Closed/Cancelled 永不可裁**（库存实扣/终态/逃生口），白名单不含即配置层无法表达 | ApprovalPoint（Confirmed 迁入，如信用审批）移除：已发起 Running 实例自然走完，回调照常推 Confirmed（回调需幂等+状态守卫，已是 IApprovalCallback 契约）；想让 PendingApproval 单立即放行=管理员撤回实例（实例→Withdrawn）后重执行迁移，此刻无审批点即直通——**前提是状态机有 PendingApproval→Draft 显式回退边+撤回工具**（争议点 #6）。**ApprovalPoint 在而 Binding 停用/缺失=E-CONF 拦迁移（fail-closed）**，保存 Binding 停用时 dry-run 预警"以下 DocType 的迁移点将被卡死"；不想审批的正确操作=删 ApprovalPoint，不是停 Binding（争议点 #3） | 信用校验等 GuardConfig 变化：只约束下一次迁移动作；已 Confirmed/已 Shipped 的单不回溯（库存/账务已发生，回溯=灾难）。改严：停在 Draft 的旧单确认时被新校验拦→出路只有修数或（若配）审批 override，不许静默放行。悬空键 fail-closed E-CONF+停包前 dry-run（spec 明文） | ① **中间环节悬空问题被口径2 结构性消解**：缝合边必须预声明，`DisabledSteps` 保存时校验"被关集合的备选边覆盖完整"，不完整拒存 E-CONF——运行时永不悬空，这条要落成保存校验的 DoD。② 信用审批依赖 ConditionJson（金额>10万走高额流程）——**该字段今天未实现**（0.1 差距 3），④ 开工前置项。③ Cancel 级联（在途 WO/Outbound 半路态 PARTIALLY_CANCELLED，老模块已有先例 `OrderCancelFullCascadeE2ETests`）与配置无关但必须原样保留 |
| **SalesOrderChange（变更单）** | Draft→Submitted→Approved→Applied（+Rejected/Cancelled）；可裁=审批步；Applied 不可裁 | 关变更审批步：停在 Draft 的在途变更单下一步直走 Draft→Applied（小额直改语义）；已在 Submitted 的走完当前审批再 Apply。**变更单整环节不可关**（关=订单确认后不可改，那是权限问题不是流程裁剪） | 同 SalesOrder。额外：审批期间**目标订单可能已前进**（如已 Shipped）——Apply 回调必须先做前置状态复核，不满足→变更单置 Failed/退回重提，绝不硬改；这是回调与单据状态的竞态，配置变更会放大它（审批走得越慢越容易撞） | **Apply 时刻按"当时的"配置全量重校验**——变更单录入时校验通过≠应用时仍有效（校验器可能已改严）；重校验失败→拒 Apply 报 E-CONF/E-SALES，变更单退 Draft | 🔴 **跨配置应用是全表最尖的角**：变更单创建于旧配置，Apply 时订单主干已被裁（例：变更要求退回 Confirmed，而 Confirmed 已被关）。推荐：**Apply 只允许作用于当前有效状态图内的迁移，目标态被关→E-CONF 拒绝+人工改单**，不做自动备选边映射（映射=语义猜测）（争议点 #7） |
| **出货指示/出库（WMS 接缝）** | OutboundOrder 0→1→2→3→4/9（作业流，不进 ④）；④ 只管 SalesOrder.Shipped 迁入点 | **不可关，无处置**——Shipped 不在白名单，"关出货"在配置层不可表达（spec 口径1）。租户"不走 WMS 直发"是 Shipped 迁移的**触发器差异**（手工 Ship 动作 vs WMS 出庫完了回写），v1 不做，勿混入 DisabledSteps | v1 不在出货点挂审批（信用审批在 Confirmed）。若后续挂"出貨承認"：解绑时**已引当/拣货中的作业不回滚**，审批只拦"出庫確定"这一迁移动作本身 | 出货校验（ATP/批次/效期）变化只影响**后续引当与出库动作**；已引当库存不因改严而自动释放——敞口写运维须知（同跳号口径，是正确行为不是 bug） | ① SalesOrder 若在 WMS 作业中途被取消→沿用既有 Cancel 级联半路态先例。② WMS 回写推 Shipped 走的是主干必经边，**任何裁剪配置都不得影响回写路径**——集成测试点：关掉全部 OptionalSteps 后 WMS 回写仍然通 |
| **发票（F1 接缝）** | ArInvoice Draft→Posted→PartiallySettled→Settled/Reversed（自身不可裁）；④ 只管 SalesOrder.Invoiced 迁入点 | **若 Invoiced 进白名单**（争议点 #4）：关闭后新单 Shipped→Closed 直达；在途"已 Shipped 待开票"的单 per-action 下一步也直接 Close——**但可能已挂 Draft ArInvoice→悬空发票草稿**。推荐：关闭 Invoiced 的保存动作强制 dry-run，列出 Shipped 未 Invoiced 在途单+Draft 发票清单，顾问二选一（先开完再关/确认弃置草稿） | 开票/过账审批（FinJournalPost 先例）解绑：PendingReview 实例走完照常 Posted（回调与引擎同事务原子，已实现）；解绑后**不得**让过账 fail-open 直通——财务线一律 fail-closed | 税码/金额校验变化：只影响未过账 Draft 发票（下次 Post 按新规则拦）；**Posted 永不回溯**，错了走红冲重开（Reversed 边永在） | ① Invoiced 关闭 → 收入确认口径旁落系统外，F1 油路对该租户断流——这不只是流程裁剪，是**财务能力开关**，建议同时要求 `module.f1` FeatureGate 联动（①与④职责边界：①管模块开没开，④管环节裁没裁，两处必须一致，否则出现"F1 开着但 Invoiced 关着"的半吊子） |

### 2.1 附表：已在审批单源上的 4 个（审批维度边界今天已存在）

| DocType | 审批解绑/换绑：在途处置 | 特殊边界/风险 |
|---|---|---|
| **PurchaseRequest（PUR_PR）** | 在途 Submitted 的 Running 实例走完照常回调（Approved/Rejected）；换新 FlowKey 只对新提交生效。**解绑后新提交：走 `ApprovalServiceAdapter` → 自动放行直通 Approved（fail-open）** | 🔴 fail-open 是现状真实行为（`ApprovalServiceAdapter.cs:29-32` 注释明言"向后兼容"）——顾问误停 Binding=采购申请全直通，**无任何警示**。④ 上线时必须统一语义（争议点 #3）；另：实例被撤回后 PR 停在 Submitted 无退路（PrStatus 无 Withdrawn，Rejected 语义不符）——需补回退边 |
| **PurchaseOrder（PUR_PO）** | 同 PR；已 Confirmed 之后的解绑无影响（审批点已过） | 同 PR 的 fail-open+PendingApproval 退路问题 |
| **JournalEntry（FinJournalPost）** | PendingReview 实例走完，回调过账与引擎同事务（原子铁律已实现，不存在"OA 过了账没落"的窗口）；解绑走 `ApprovalService` 路径=抛异常 fail-closed ✅ 财务线语义正确 | 复核人≠制单人靠 `DecidedById` 回传——换绑到"自动通过"型流程会让 checker=starter，绑定保存时应校验流程含人工审批节点（记票） |
| **BudgetVersion（A5_Budget）** | 同 Journal | BudgetGuard（`Fin\BudgetGuard.cs`）= **现存"校验器配置变化"活先例**：Block/Warn 模式由 BudgetVersion 配置驱动，变化即时作用于下一次过账、已过账不回溯——与口径3 per-action 完全一致，④ 的 GuardConfig 语义可直接对齐此先例 |

## 3. 需要用户拍板的争议点

1. **口径3 的精确语义：per-action 还是 per-doc pin？** spec 原文="新配置从下一次迁移动作起约束"（per-action：在途单下一步就按新图走）；记忆/任务转述="在途单按创建时配置走完"（per-doc：需给每张单存配置快照）。行为差异见 0.3。**推荐 per-action（spec 原文）**：零快照存储、旧配置引用失效校验器时不会把在途单困死；代价=在途单行为随配置变、需写进顾问操作须知。若拍 per-doc pin，则 DocFlowConfig 需加版本表+单据存 ConfigVersion，施工量显著上升。
2. **④ 开工是否前置 WFS 版本治理？** 现状无版本 pin（0.2），"在途审批自然走完"仅在换绑/停用场景成立，同 FlowKey 原地改版会击穿。**推荐最低限**：不等版本治理波，先立运维硬规约+代码闸："改流程=建新 FlowKey+换绑，禁止原地改已投产 FlowDef 的 SchemaJson（有在途实例时保存拒绝 E-WF）"——这个保存闸是小改动，建议随 ④ 一起做。
3. **解绑语义统一**：ApprovalService fail-closed（抛异常）vs Pur Adapter fail-open（自动放行）并存。**推荐**：④ 体系下"不想审批"的唯一正规动作=删 ApprovalPoint（配置层）；"ApprovalPoint 在而 Binding 停用/缺失"=保存时预警+运行时 E-CONF 拦迁移（fail-closed）。Pur 的 fail-open 兼容语义是否收敛（收敛=存量未配审批的租户 PR/PO 提交会开始报错，需配套种子"直通绑定"或迁移公告）。
4. **Invoiced 是否进 OptionalSteps**（租户不在系统内开票，备选边 Shipped→Closed）？若进，是否强制与 F1 模块开关（①FeatureGate）联动一致性校验？
5. **关闭 Quotation 整环节时在途报价的默认处置**：推荐"在途走完可转单，仅藏新建"+可选批量作废运维动作；还是一刀切冻结？
6. **管理员撤回在途审批的配套是否进 v1**：各状态机的 PendingApproval/Submitted→Draft 显式回退边+撤回工具，是"审批解绑后放行在途单"的唯一正规通道；不做则解绑后 PendingApproval 单只能等实例走完。
7. **变更单跨配置 Apply**：目标态被裁时 E-CONF 拒绝+人工改单（推荐，语义安全）vs 自动映射到备选边（省人工，但等于替用户猜业务语义）。

**关键文件索引**：`docs/superpowers/specs/2026-07-09-tenant-config-platform-design.md`（§1④/§3/§5/§7）、`CP6.Core\Services\Wf\ApprovalService.cs`（防重+fail-closed）、`CP6.Core\Services\Pur\Contracts\ApprovalServiceAdapter.cs`（fail-open）、`CP6.Core\Services\Wf\FlowEngine.cs:433`（无版本 pin 的 LoadSchemaAsync）、`CP6.Entity\DomainModels\Wf\Wf_FlowDef.cs:8`（阶段1 简化自认）、`CP6.Entity\DomainModels\Erp\Order.cs:121`（老单生命周期）、`CP6.Core\Services\Fin\BudgetGuard.cs`（校验器配置化活先例）。
