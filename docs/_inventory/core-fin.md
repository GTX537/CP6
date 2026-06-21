### 二、CP6.Core/Services/Fin + Common — 财务与通用服务

#### Services/Fin

##### 总账 / 科目 / 期间（GL 内核）

- `CP6.Core/Services/Fin/IGlAccountService.cs` — 会计科目服务接口：科目 CRUD（不删只停用）、按 Role 角色锚点取科目、多国别科目表模板导入。
- `CP6.Core/Services/Fin/GlAccountService.cs` — 会计科目服务实现：科目列表/取值/新建/改/停用 + 按方案导入模板包，Code 唯一校验。
- `CP6.Core/Services/Fin/IJournalEntryService.cs` — 记账凭证服务接口：借贷恒等校验、maker-checker 过账、自动凭证直过、驳回、红冲（只 Post/Reverse 无 Update/Delete）。
- `CP6.Core/Services/Fin/JournalEntryService.cs` — 记账凭证服务实现：草稿/提交复核/过账/自动过账/驳回/红冲全状态机 + 借贷不平等错误码（101~130）。
- `CP6.Core/Services/Fin/IFinSequenceService.cs` — 财务采番服务接口：凭证号 `{key}-{yyyy-MM}-{NNNNN}` 按月归零。
- `CP6.Core/Services/Fin/FinSequenceService.cs` — 财务采番服务实现：按 SeqKey+月作用域 gapless 累计取下一凭证号。
- `CP6.Core/Services/Fin/IFiscalPeriodService.cs` — 会计期间服务接口：日历→财年期次换算、按记账日归期/解析、期间 Open 判定、上一期间、期间列表。
- `CP6.Core/Services/Fin/FiscalPeriodService.cs` — 会计期间服务实现：财年起始月可配（appsettings），期间生成/归期/锁期判定。
- `CP6.Core/Services/Fin/IPeriodCloseService.cs` — 月结/锁期工作流接口：结账前检查、结账（置 Closed）、反结账（限高权限留痕）。
- `CP6.Core/Services/Fin/PeriodCloseService.cs` — 月结/锁期工作流实现：组合期间/试算/汇兑重估/折旧，前检查（未过账/试算不平/上期未结）后置 Closed。
- `CP6.Core/Services/Fin/ITrialBalanceService.cs` — 试算平衡表服务接口：按期间构建三栏试算表 + 两层平衡校验。
- `CP6.Core/Services/Fin/TrialBalanceService.cs` — 试算平衡表服务实现：期初（含历史）+本期+期末实时滚算，不平则记错误日志告警。
- `CP6.Core/Services/Fin/TrialBalance.cs` — 试算平衡表 DTO：三栏行结构 + 本期发生平衡/期末余额平衡两层标记。

##### 自动凭证引擎 / 事件 / 模板种子

- `CP6.Core/Services/Fin/IAutoVoucherEngine.cs` — 自动凭证引擎接口：把财务业务事件按 PostingRule 拼成凭证并直过（四步：幂等/找规则/拼凭证/AutoPost）。
- `CP6.Core/Services/Fin/AutoVoucherEngine.cs` — 自动凭证引擎实现：幂等判重、按 EventType 找启用规则、按角色锚点取科目+炸开单据行+外币换算、调 AutoPostAsync 直过。
- `CP6.Core/Services/Fin/FinBizEvent.cs` — 财务业务事件载荷（非持久化）：AP/AR 各生命周期段构造的引擎入参，含来源/幂等键/外币汇率/按字段名查表取值。
- `CP6.Core/Services/Fin/FinCoaTemplate.cs` — 默认科目表模板包：CN-GAAP/INTL 两套结构一致、角色锚点恒定的科目种子定义，部署时一键导入。
- `CP6.Core/Services/Fin/PostingRuleSeed.cs` — 记账规则种子：幂等播种 AP/AR 发票/收付款/核销/红字等标准记账规则，只引用 Role 锚点与模板包解耦。

##### 应付（AP）

- `CP6.Core/Services/Fin/IFinAp.cs` — 采购→应付对外契约：据三单匹配结果创建并过账 AP 发票（含外币），幂等 (供应商,发票号)，低耦合入口。
- `CP6.Core/Services/Fin/IApInvoiceService.cs` — 应付发票服务接口：录入草稿（防重+行级算税+采番）+ 过账（发 AP.InvoicePosted 事件生成凭证）。
- `CP6.Core/Services/Fin/ApInvoiceService.cs` — 应付发票服务实现（兼 IFinAp）：发票录入/过账，借费用+进项税/贷应付，不可抵扣税并入成本行。
- `CP6.Core/Services/Fin/IPaymentService.cs` — 付款服务接口：付款过账（借应付/贷银行，预付走预付账款）+ 撤销（先解核销再红冲）。
- `CP6.Core/Services/Fin/PaymentService.cs` — 付款服务实现：发 AP.Payment/Prepayment 事件生成凭证、撤销按顺序还原核销+红冲付款凭证。
- `CP6.Core/Services/Fin/IApSettlementService.cs` — 应付核销服务接口：一笔付款核销多张发票，尾差/折扣与已实现汇兑损益写差额冲销凭证。
- `CP6.Core/Services/Fin/ApSettlementService.cs` — 应付核销服务实现：逐发票冲减欠款、算已实现汇兑损益+折扣写冲、更新发票/付款核销额。
- `CP6.Core/Services/Fin/IApReconcileService.cs` — 应付子账↔GL 勾稽接口：AP 子账未付合计须等于 GL AP_CONTROL 控制科目余额。
- `CP6.Core/Services/Fin/ApReconcileService.cs` — 应付子账↔GL 勾稽实现：子账未付按记账汇率折本位币 vs AP_CONTROL 已过账分录（贷−借）。
- `CP6.Core/Services/Fin/IApAgingService.cs` — 应付账龄接口：按到期日相对基准日分桶（未到期/逾期1-30/31-60/60+），按供应商汇总折本位币。
- `CP6.Core/Services/Fin/ApAgingService.cs` — 应付账龄实现：未付余额=(Gross−Settled)×记账汇率，按逾期天数落桶（红字反向）。
- `CP6.Core/Services/Fin/IApMasterService.cs` — 应付主数据接口：银行账户+税码的列表/新建（供 AP 界面下拉）。
- `CP6.Core/Services/Fin/ApMasterService.cs` — 应付主数据实现：银行账户/税码列表+新建，编码唯一校验。

##### 应收（AR）

- `CP6.Core/Services/Fin/IArInvoiceService.cs` — 应收发票服务接口：录入/过账（收入确认+成本结转双凭证）、出货自动开票（幂等）、销售退货红字、红冲。
- `CP6.Core/Services/Fin/ArInvoiceService.cs` — 应收发票服务实现：发 AR.Revenue（借应收/贷收入+销项税）+ AR.Cogs（借COGS/贷FG）凭证，收入与成本分开幂等。
- `CP6.Core/Services/Fin/IReceiptService.cs` — 收款服务接口（镜像付款）：收款过账（借银行/贷应收，预收走预收账款）+ 撤销。
- `CP6.Core/Services/Fin/ReceiptService.cs` — 收款服务实现：发 AR.Receipt/Advance 事件生成凭证、撤销按顺序还原核销+红冲收款凭证。
- `CP6.Core/Services/Fin/IArSettlementService.cs` — 应收核销服务接口（镜像应付核销）：一笔收款核销多张发票，销售折扣/汇差写差额冲销凭证。
- `CP6.Core/Services/Fin/ArSettlementService.cs` — 应收核销服务实现：逐发票冲减欠款、算已实现汇兑损益+销售折扣写冲、更新发票/收款核销额。
- `CP6.Core/Services/Fin/IArReconcileService.cs` — 应收子账↔GL 勾稽接口：AR 子账未收合计须等于 GL AR_CONTROL 控制科目余额。
- `CP6.Core/Services/Fin/ArReconcileService.cs` — 应收子账↔GL 勾稽实现：子账未收按记账汇率折本位币 vs AR_CONTROL 已过账分录（借−贷）。
- `CP6.Core/Services/Fin/IArAgingService.cs` — 应收账龄接口（镜像应付）：按到期日分桶、按客户汇总折本位币，逾期桶驱动催收预警。
- `CP6.Core/Services/Fin/ArAgingService.cs` — 应收账龄实现：未收余额=(Gross−Settled)×记账汇率，按逾期天数落桶（红字反向）。
- `CP6.Core/Services/Fin/ICreditControlService.cs` — 信用控制接口（AR 独有风控）：出货/受注前校验客户应收余额+本单是否超信用额度（财务反向钩子）。
- `CP6.Core/Services/Fin/CreditControlService.cs` — 信用控制实现：当前应收余额按未结发票折本位币，复用取引先 CreditLimit，超额返 Exceeded。

##### 外币 / 财务报表

- `CP6.Core/Services/Fin/IFxRevaluationService.cs` — 期末未实现汇兑重估接口：月结时按期末汇率重估未结外币 AP/AR 余额，差额计未实现汇兑损益，下期初冲回。
- `CP6.Core/Services/Fin/FxRevaluationService.cs` — 期末未实现汇兑重估实现：reversing 法生成本期重估+下期初冲回凭证，仅落本期损益，子账↔GL 勾稽不受影响。
- `CP6.Core/Services/Fin/FinancialStatements.cs` — 财务报表 DTO：资产负债表（期末余额时点数+本年利润并入权益）与损益表（本期发生区间数+毛利/净利逐层）数据结构。
- `CP6.Core/Services/Fin/IBalanceSheetService.cs` — 资产负债表服务接口：从科目期末余额重组（复用试算表，必平校验）。
- `CP6.Core/Services/Fin/BalanceSheetService.cs` — 资产负债表服务实现：按 AccountType 分资产/负债/权益，本年利润并入权益侧，资产=负债+权益+本年利润恒平。
- `CP6.Core/Services/Fin/IIncomeStatementService.cs` — 损益表服务接口：从收入/成本/费用科目本期发生额算（复用试算表）。
- `CP6.Core/Services/Fin/IncomeStatementService.cs` — 损益表服务实现：收入贷−借、费用借−贷，按 Role 分主营成本(COGS)与营业费用，逐层算毛利/净利。

##### 成本 / 桥接 / 审批回调

- `CP6.Core/Services/Fin/ICostCollectService.cs` — 成本归集接口：吃 MES 真实消耗×BOM 供给单价（料）+ 工费标准估算，按工单一单一成本单，差异化卖点。
- `CP6.Core/Services/Fin/CostCollectService.cs` — 成本归集实现：料按实际用量×受给単価、工费逐工序工时×费率做真（缺实绩回退标准），双模式严格/迁移。
- `CP6.Core/Services/Fin/ICostSettleService.cs` — 成本完工结转接口：料工费→WIP 凭证 + WIP→FG 凭证，FG 单位成本喂 AR 成本结转切真实。
- `CP6.Core/Services/Fin/CostSettleService.cs` — 成本完工结转实现：借WIP/贷原材料+人工+制费（吸收法）、借FG/贷WIP，全按 Role 锚点取科目。
- `CP6.Core/Services/Fin/FinBridgeHook.cs` — 财务跨模块桥接钩子：出货确认→AR 自动开票、出货取消→红冲，Best-Effort 落 IntegrationEvent 不阻断主操作。
- `CP6.Core/Services/Fin/JournalApprovalCallback.cs` — 记账凭证 OA 审批回调（BizType=FinJournalPost）：审批通过调 PostAsync 过账、驳回调 RejectAsync，复核人取 OA 决策人天然满足过账人≠制单人。

##### 固定资产（折旧 / 处置）

- `CP6.Core/Services/Fin/IDepreciationCalculator.cs` — 折旧引擎接口（纯函数无 DB）：给折旧参数+已提期数+工作量返回本期折旧额。
- `CP6.Core/Services/Fin/DepreciationCalculator.cs` — 四法折旧纯函数实现：直线/工作量等方法，封顶残值+末期补足残差+负数归零兜底。
- `CP6.Core/Services/Fin/AssetDtos.cs` — 资产折旧/处置 DTO：折旧入参/明细行/前瞻计划行/处置月补提结果等数据结构。
- `CP6.Core/Services/Fin/IAssetDepreciationService.cs` — 资产折旧服务接口：试算预览/计提 Run/录工作量/过账/红冲/结账钩子计提/处置月补提/单卡前瞻计划。
- `CP6.Core/Services/Fin/AssetDepreciationService.cs` — 资产折旧服务实现：手动 Run/Post、Worker 备草稿、结账钩子 Accrue 三路，仿 FxReval 直建折旧凭证。
- `CP6.Core/Services/Fin/IAssetDisposalService.cs` — 资产处置服务接口：处置单创建/确认/红冲/取值/列表（出售/报废/转让/盘亏）。
- `CP6.Core/Services/Fin/AssetDisposalService.cs` — 资产处置服务实现：经清理科目结转处置损益（含处置月补提折旧），仿 FxReval 直建凭证。

##### 银行对账（A4）

- `CP6.Core/Services/Fin/IBankStatementImporter.cs` — 银行流水导入解析接口：按 Profile 解析 CSV/Excel 文件流为候选行（不落库，单行失败收集不中断）。
- `CP6.Core/Services/Fin/BankStatementImporter.cs` — 银行流水导入解析实现：按格式分发 ParseCsv/ParseExcel，生成含指纹/去重哈希的候选行。
- `CP6.Core/Services/Fin/IBankStatementService.cs` — 银行对账单服务接口：导入 Profile 管理、对账会话 CRUD、文件导入预览/确认、手工流水行增改删。
- `CP6.Core/Services/Fin/BankStatementService.cs` — 银行对账单服务实现：组合期间/采番/导入器，管理对账会话与流水行落库。
- `CP6.Core/Services/Fin/IBankReconService.cs` — 银行撮合服务接口：候选行查询、自动/手工撮合、解撮合、银行单方建凭证、挂起、调节表、锁/解锁。
- `CP6.Core/Services/Fin/BankReconService.cs` — 银行撮合服务实现：流水↔账面 GL 撮合（自动/手工）、银行单方凭证生成、双向调节表与锁后守卫支撑。
- `CP6.Core/Services/Fin/BankReconDtos.cs` — 银行对账 DTO：导入预览报告、内存候选行、撮合候选/手工撮合请求/调节表等数据结构。
- `CP6.Core/Services/Fin/BankReconGuard.cs` — 银行对账锁后守卫（静态）：凭证命中已锁定银行对账期的银行科目则拒绝过账，供 JournalEntryService 同库直查。

##### 预算 / 管理会计（A5）

- `CP6.Core/Services/Fin/IBudgetService.cs` — 预算主体/版本服务接口：预算与版本 CRUD、按年/版本复制、提交审批、激活（含 OA 回调激活）。
- `CP6.Core/Services/Fin/BudgetService.cs` — 预算主体/版本服务实现：组合采番+OA 审批服务，管理预算与多版本生命周期。
- `CP6.Core/Services/Fin/IBudgetLineService.cs` — 预算明细行服务接口：行 CRUD（按科目×成本中心×成本对象）+ Excel 导入预览/确认。
- `CP6.Core/Services/Fin/BudgetLineService.cs` — 预算明细行服务实现：行 Upsert/删除（年额按 even/seasonal/manual 分解12月）+ ClosedXML 导入。
- `CP6.Core/Services/Fin/IBudgetReportService.cs` — 预算报告服务接口：预算 vs 实际报告、过账前预算预检、按桶聚合实际发生额。
- `CP6.Core/Services/Fin/BudgetReportService.cs` — 预算报告服务实现：按科目×成本中心×成本对象匹配最具体预算桶，无匹配归未编预算组不丢弃，实际侧仿试算表取 Posted。
- `CP6.Core/Services/Fin/BudgetGuard.cs` — 预算过账守卫（静态）：手工凭证过账时拦截 Block 模式费用科目超支，Warn/None 透传，仿 BankReconGuard 同库直查。
- `CP6.Core/Services/Fin/BudgetDtos.cs` — 预算 DTO：带数据的泛型 FinResult&lt;T&gt;、预算行 DTO、导入预览结果等数据结构。
- `CP6.Core/Services/Fin/BudgetApprovalCallback.cs` — 预算版本 OA 审批回调（BizType=A5_Budget）：通过则激活本版+归档旧版、驳回则置 Rejected，用 IServiceProvider 延迟解析打破 DI 循环依赖。

##### 通用结果类型 / 每日对账

- `CP6.Core/Services/Fin/FinResult.cs` — 财务操作结果类型：携带 i18n 错误码(E-FIN-xxx)+格式化参数，不带语言文字（Pass/Fail 工厂）。
- `CP6.Core/Services/Fin/IFinReconciliationService.cs` — 财务每日对账接口：跑多项一致性勾稽，返回不一致清单（供 Worker 调度）。
- `CP6.Core/Services/Fin/FinReconciliationService.cs` — 财务每日对账实现：三项一致性（AP/AR 子账↔GL 控制科目 + 各开启期间试算平衡），复用 TrialBalance/ReconcileAp/Ar。

#### Services/Common

- `CP6.Core/Services/Common/DocNumber.cs` — 全社统一采番辅助（静态）：机能码(3)+年月(6)+自增(4)=13位单号，自增按机能码全局累计、与调用方 SaveChanges 同事务确定。
- `CP6.Core/Services/Common/QuerySort.cs` — 一览画面服务端动态排序辅助（静态）：按白名单列名+升降序生成可翻译为 SQL 的 OrderBy 表达式，防注入，缺失回退默认排序。
- `CP6.Core/Services/Common/ITenantContext.cs` — 当前租户上下文接口+默认实现：请求级 scoped 从 JWT 解析 tenant_id，供全局查询过滤与写入盖章，后台默认租户兜底。
- `CP6.Core/Services/Common/ITenantEnumerator.cs` — 活跃租户枚举接口+默认实现：列出 Sys_Tenant 启用租户（空表回退默认租户），供后台 Worker 按租户循环。
- `CP6.Core/Services/Common/TenantSeed.cs` — 默认租户种子（静态）：幂等把默认租户登记进 Sys_Tenant 注册表，使枚举/登录/管理 UI 可见。
- `CP6.Core/Services/Common/IMaterialUsageCalculator.cs` — 共享用量内核接口+实现：出料用量（非成本），尺寸驱动（面积×段成率×数量）与静态定额（单耗×数量），见積与 MRP 共用同一公式。
