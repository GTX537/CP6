# CP6 财务会计模块 · 完整设计与实现丛书

> **定位**：CP6 现在有完整的"业务单据层"（受注 / 出货 / 工单 / 入出库 / 信用单），但**没有"会计账层"**。没有这一层，CP6 是一套很强的"进销存 + MES"，但**还不是 ERP**。这套丛书的目标，是把财务会计内核——总账 / 应收 / 应付 / 成本——在 CP6 里从零亲手实现，让纸箱版从"能管业务"升级到"能管钱、能出三大报表、能签合同卖"。
>
> 风格沿用 [`docs/oa/`](../oa/README.md) 与 [`docs/learning/`](../learning/README.md)：拿真实代码当教材，每章讲**为什么这么设计、不这么写会出什么会计事故、业界 ERP（SAP / Odoo / 用友）怎么做**。
>
> 读者视角：你已经会 .NET + Vue（CP6 就是你的项目），现在要补的是"一个 ERP 的财务内核是怎么做出来的"这层认知——尤其是**会计的两条铁律**会反过来约束你所有的表结构。

---

## 一、先记住这一句话（整套书的题眼）

> **每一笔业务动作，最终都要落成一组"借贷相等"的会计分录（凭证）。财务模块 = 一台"业务事件 → 凭证"的翻译机 + 一本永远恒等平衡的总账。**

你出一次货、付一次款、领一卷原纸去生产——在业务上是动作，在财务上**必须**变成一张凭证：

```
出货给客户 100 元的纸箱：
  借  应收账款 (Asset+)   100
  贷  主营业务收入 (Rev+)      100        ← 借方合计 = 贷方合计，恒等

同时结转成本（这卷纸用了 60 元）：
  借  主营业务成本 (Exp+)  60
  贷  库存商品 (Asset-)        60
```

SAP、Odoo、用友、金蝶——**全是这一个套路，没有魔法**。区别只在"翻译规则"多复杂、报表多漂亮。所以"做一个 ERP 的财务" = **做三件事**：①一本恒等的总账（科目 + 凭证）②一台把业务事件翻译成凭证的引擎 ③在总账之上长出子账（应收应付）、成本、报表。

**总账是地基，不是最后做的。** 这是本书最重要的方法论：哪怕你的 MVP 业务价值是"应付 AP"，**也必须先把总账内核（科目表 + 凭证 + 借贷恒等）做出来**，因为 AP 的每张发票、每笔付款都要往总账里写凭证。先做子账、后补总账的项目，最后都要推倒重来。

---

## 二、会计的两条铁律（它们会约束你所有表结构）

做业务系统你可以"先跑起来再说"。做财务**不行**——下面两条是硬约束，违反了就是账做错了：

### 铁律 1 · 借贷恒等（每张凭证 Σ借方 = Σ贷方）

每张凭证（`JournalEntry`）下挂多条分录行（`JournalLine`），**保存前必须校验借方合计 = 贷方合计**，不等就拒绝落库。这不是业务规则，是会计公理。

### 铁律 2 · 凭证不可改、不可删，只能"红冲"

业务单据可以改可以删，**会计凭证一旦过账就冻结**。错了怎么办？做一张金额相反的"红冲凭证"把它冲掉，再做一张正确的。这叫**审计轨迹不可篡改**，是财务能被审计、能打官司的前提。

> 这两条铁律决定了：`JournalEntry` 没有 `Update`/`Delete` 接口，只有 `Post`（过账）和 `Reverse`（红冲）。你现成的 `Sys_OperLog` 审计 + `IntegrationEvent` 不可变事件流，正好契合这个心智。

### 推论 · 子账必须与总账勾稽

应付子账（每个供应商欠多少）的总和，**必须永远等于**总账里"应付账款"控制科目的余额。两边对不上 = 账坏了。这叫**子账-总账勾稽（reconciliation）**，是月结时第一个要对的数。

---

## 三、财务模块 = 五本账

| 账 | 干什么 | 核心表 | 本书章节 |
|---|---|---|---|
| **总账 GL** | 一切凭证的归宿，恒等平衡 | `GlAccount` 科目表 · `JournalEntry/Line` 凭证 | [01](./01-gl-kernel.md) · [02](./02-period-close.md) |
| **应付 AP** | 我欠供应商多少（原纸/油墨/外包） | `ApInvoice` · `Payment` · `ApSettlement` | [03](./03-accounts-payable.md) ← **MVP 先做** |
| **应收 AR** | 客户欠我多少 | `ArInvoice` · `Receipt` · `ArSettlement` | [04](./04-accounts-receivable.md) |
| **成本会计** | 每张订单真实花了多少料工费 | `CostSheet` 归集单 · 标准/实际/差异 | [06](./06-cost-accounting.md) |
| **报表** | 试算表 / 资产负债表 / 损益表 | （查询，无新表） | [08](./08-financial-statements.md) |

把这五本账缝起来的，是一台**自动凭证引擎**（[05](./05-auto-voucher.md)）——它监听业务事件（出货、付款、领料），按"入账规则"自动生成凭证。**它复用你现成的 Bridge Hook + IntegrationEvent，不用重造。**

---

## 四、章节目录

### Part 0 · 总览
- **00. 心智模型 + 两条铁律**（本页一、二节）

### Part 1 · 总账内核（地基，最先做）
- [01. 总账内核：科目表 + 凭证 + 借贷恒等](./01-gl-kernel.md) — **阶段 0**，从这里入门
- [02. 会计期间与期末结账：试算平衡 / 结转 / 锁期](./02-period-close.md) — **阶段 1**

### Part 2 · 子账（MVP 主战场）
- [03. 应付 AP：供应商发票 → 付款 → 核销](./03-accounts-payable.md) — **阶段 2，★MVP 第一个落地**
- [04. 应收 AR：出货 → 发票 → 收款 → 核销](./04-accounts-receivable.md) — **阶段 3**
- [05. 自动凭证引擎：业务事件 → 凭证（复用 BridgeHook）](./05-auto-voucher.md) — 贯穿全程

### Part 3 · 成本与多币种
- [06. 成本会计：实际原价（工单归集）+ 标准成本差异](./06-cost-accounting.md) — **阶段 4**，复用 PaperRoll/InkLot
- [07. 多币种与汇兑损益：复用 FxRate，结算算汇差](./07-multi-currency.md)

### Part 4 · 报表与产品化
- [08. 财务报表：试算表 / 资产负债表 / 损益表](./08-financial-statements.md)
- [09. 与 CP6 集成：实体落点 / BridgeHook 接法 / 数据一致性](./09-cp6-integration.md)
- [10. 数据完整性与审计：红冲 / 锁期 / maker-checker / 权限](./10-integrity-audit.md)

> ✅ **全 11 个文件（总纲 + 01~10）已写完**。本页（总纲）是全局地图、铁律、数据模型、动手路线与需求基线；各章是详细设计 + 代码骨架，可直接照着编码。

---

## 五、分阶段动手路线（关键：地基优先，子账其次）

| 阶段 | 目标 | 做什么 | 不做什么 | 完成标志 |
|---|---|---|---|---|
| **阶段 0** | 总账能记一笔 | 科目表 + 手工凭证录入 + 借贷恒等校验 | 不碰自动凭证 | 手工录一张平衡凭证，存得进、查得出，不平衡被拒 |
| **阶段 1** | 期间能结账 | 会计期间 + 试算平衡表 + 锁期 | 不做合并报表 | 一个月的凭证能结平、能锁、锁后不能再记 |
| **阶段 2** | **AP 跑通（MVP）** | 供应商发票（手工录）→ 付款 → 核销 → **自动生成凭证入 GL** | 不做采购三单匹配 | 录一张原纸采购发票→付款→AP 余额 = GL 应付控制科目余额 |
| **阶段 3** | AR 跑通 | 出货**自动**生成 AR 发票 → 收款 → 核销 | — | 出货后发票自动出现，收款核销，AR 与 GL 勾稽 |
| **阶段 4** | 成本落地 | 工单归集实际料工费（吃 PaperRoll 残米/InkLot 消耗）+ 标准成本差异 | 不做分步法 | 一张订单能看到真实材料成本 vs 标准成本差异 |
| **阶段 5** | 出报表 | 试算表 / 资产负债表 / 损益表 | — | 三大报表能从凭证一键生成 |

**每个阶段都是能演示的里程碑。** 阶段 2 结束时，你已经能向纸箱厂演示"采购欠款 → 付款 → 账自动平"——那一刻 CP6 就真的有"财务"了。

> ⚠️ **一个跨阶段依赖要心里有数**：阶段 3（AR）出货时要生成"成本结转"凭证，而准确成本来自阶段 4（成本会计）。所以**阶段 3 先用估算成本**（标准成本或上次实际），等阶段 4 落地后自动切换到真实成本。按顺序实施时别在阶段 3 卡等阶段 4——估算成本足以让 AR 闭环演示。详见 [04 章](./04-accounts-receivable.md#二杀手锏出货自动开票吃-cp6-现成数据)。

> **为什么 MVP 是 AP 却要先做阶段 0/1？** 因为 AP 付款必须落成凭证、凭证必须进总账。没有总账，AP 就是个孤立的"欠款记事本"，不是会计。地基两阶段是 AP 的前置，但很薄（科目表 + 凭证 + 期间），约 2 人周即可垫好。

---

## 六、最小数据模型（贯穿全书）

先建立全局表结构直觉，细节各章展开。所有表都带 `TenantId`（即使现在单租户也预留，避免日后重构——这是 [docs/oa/10](../oa/10-multi-tenant.md) 的教训）。

```
■ 自动凭证规则（阶段 2 起 —— 引擎的"翻译表"）
  PostingRule     入账规则（EventType 业务事件类型 → 多条 PostingRuleLine)
  PostingRuleLine 规则行（Side 借/贷, Source=固定角色/单据行透传,
                          FixedRole: AccountRole + AmountField；DocumentLines: LineAccountField + LineAmountField)
                          ← 透传支持混行发票各进各科目

■ 总账 GL（阶段 0/1 —— 一切的地基）
  GlAccount       科目表（Code 科目编码, Name, Type=资产/负债/权益/收入/费用,
                          ParentId 树形, NormalSide=借/贷, IsControl 是否控制科目,
                          Role 角色锚点(跨模板恒定), StandardScheme 模板包=CN-GAAP/INTL/JP/US)
  CostCenter      成本中心（Code, Name, Type=部门/工序/机台, ParentId, LinkMachineId→MES）
  JournalEntry    凭证头（No 凭证号, Date, PeriodId, Source 来源,
                          Status=草稿/待复核/已过账/已驳回/已红冲,    ← maker-checker 状态机
                          MakerId 制单人, CheckerId 过账人(≠制单人), Description, ReversedById 红冲指向)
  JournalLine     凭证行（EntryId, AccountId, Debit 借方, Credit 贷方,
                          CurrencyCd, FxRate, PartnerId 往来单位, CostObject 成本对象,
                          CostCenterId 成本中心(机台/工序/部门，分析维度))
  FiscalPeriod    会计期间（Year, Month, Status=Open/Closed, ClosedAt)

■ 应付 AP（阶段 2 —— MVP）
  ApInvoice       采购发票头（No, SupplierInvoiceNo 供应商原始票号(防重), SupplierId→BusinessPartner,
                          Date, DueDate, CurrencyCd, FxRate, NetAmount, TaxAmount, GrossAmount,
                          Status=待付/部分/已付, PurchaseOrderId 可空 ← 预留采购三单匹配,
                          IsCreditMemo 供应商红字(采购退货), OriginInvoiceId, RmaId→WMS RMA)
  ApInvoiceLine   发票行（ItemId, Qty, UnitPrice, TaxCodeId, Amount, ExpenseAccountId, CostCenterId)
  BankAccount     银行账户主数据（Code, BankName, AccountNo, CurrencyCd, GlAccountId 映射GL银行科目)
  Payment         付款单（No, SupplierId, Date, Amount, CurrencyCd, FxRate, Method, BankAccountId,
                          IsPrepayment 预付款, Status=正常/已撤销)
  ApSettlement    核销（PaymentId, ApInvoiceId, SettledAmount, DiffAmount 尾差/折扣, DiffType, DiffAccountId)
                          ← 一笔付款可核销多张发票，多对多；支持尾差核销+现金折扣

■ 应收 AR（阶段 3 —— 与 AP 对称）
  ArInvoice / ArInvoiceLine / Receipt / ArSettlement   （结构镜像 AP）

■ 税（通用，不绑国别）
  TaxCode         税码（Code, Name, Rate %, Direction=进项/销项, Recoverable 可抵扣(不可抵扣则税额并入成本), IsActive)

■ 成本（阶段 4）
  CostSheet       成本归集单（WorkOrderId/OrderId, MaterialCost 料, LaborCost 工,
                          OverheadCost 费, StandardCost 标准, Variance 差异, Status)
  CostSheetLine   归集明细（来源=PaperRoll消耗/InkLot消耗/工时/费率)
```

> 三个最关键的勾稽点，写代码时刻刻盯着：
> 1. **每张 `JournalEntry`**：`Σ JournalLine.Debit = Σ JournalLine.Credit`
> 2. **AP 子账 ↔ GL**：`Σ 未核销 ApInvoice = GL 应付账款控制科目余额`
> 3. **凭证不可逆**：`JournalEntry` 过账后无 `Update/Delete`，错了走 `Reverse`（红冲）

---

## 七、它怎么嵌进 CP6（你已有的便宜可占）

| 财务需要的能力 | CP6 现成的东西 | 怎么用 |
|---|---|---|
| 业务事件 → 凭证（异步/幂等/补偿） | `IntegrationEvent` + 重试/死信（Phase 6） | 出货/付款事件复用这套驱动自动凭证，天然幂等可补偿 |
| 出货后自动开 AR 发票 | `IErpBridgeHook`（出库→订单回写已有） | 加一个 Hook：出货 → 生成 ArInvoice → 生成收入凭证 |
| 供应商 / 客户主数据 | `BusinessPartner`（取引先已有） | AP 供应商、AR 客户直接复用，加"往来科目"配置 |
| 多币种 + 汇率冻结 | `FxRate` + `Order.CurrencyCd/FxRate`（Gap 4.3 已做） | AP/AR 发票按交易日冻结汇率，结算时算汇兑损益 |
| **实际成本的数据源** | `PaperRoll.残米長` + `InkLot` 消耗记录 | ★工单归集真实材料成本，做出通用 ERP 给不了的差异化 |
| 审计轨迹（凭证不可篡改） | `Sys_OperLog` + Kafka 审计流 | 凭证过账/红冲全程留痕 |
| 实时提醒（到期应付/超期应收） | SignalR Hub（已有） | 账龄预警推到看板 |

**两个硬缺口（本模块要新建）：**
1. **科目表（Chart of Accounts）全新** —— CP6 现在没有任何会计科目概念。提供一套"通用制造业默认科目表"做模板，客户可调整。
2. **采购模块未建** —— AP 的"三单匹配（PO/收货/发票）"依赖它。本模块 AP **先以手工录发票起步**，`ApInvoice.PurchaseOrderId` 预留，采购模块落地后再开启匹配。

---

## 八、与业界 ERP 对照（确认"原来就这么回事"）

| 你想理解 | 去看 | 学什么 |
|---|---|---|
| 完整开源 ERP 的会计内核 | **Odoo `account` 模块** | 科目表 / 凭证(move+move_line) / 自动入账规则 / 多币种，结构和本书一一对应 |
| 双辕记账的极简实现 | **Ledger / beancount（纯文本记账）** | 借贷恒等的最小内核，剥掉一切 UI 看本质 |
| 制造业成本会计 | **ERPNext `Manufacturing + Stock Ledger`** | 工单归集料工费、WIP/成品结转 |
| 凭证不可变 + 事件溯源 | **你自己的 `IntegrationEvent`（Phase 6）** | 不可变事件流，正是凭证账的天然底座 |

读它们不是抄，而是确认：**Odoo 的 `account.move` 就是本书的 `JournalEntry`，`account.move.line` 就是 `JournalLine`**——核心模型全世界一样。

---

## 九、里程碑自检（学到没学到，问自己）

- [ ] 阶段0：我能说清"为什么凭证不能改只能红冲"，并在代码里挡住 Update 吗？
- [ ] 阶段1：试算平衡表为什么一定平？它和"借贷恒等"是什么关系？
- [ ] 阶段2：一笔付款核销两张发票，数据上怎么记？AP 子账余额怎么和 GL 控制科目对上？
- [ ] 阶段3：出货自动开发票，如果出货被取消（你已有 OrderCancel 级联），AR 发票和凭证该怎么红冲？
- [ ] 阶段4：同一张订单，实际材料成本（PaperRoll 残米）和标准成本差异是怎么算出来的？
- [ ] 阶段5：资产负债表的"应付账款"那一行，数字是从哪来的？（应该 = GL 控制科目余额 = AP 子账合计）

全部能答 → CP6 就从"进销存+MES"真正变成了"ERP"。

---

## 十、需求基线（已拍板 / 待定）

### ✅ 已拍板（2026-06-10）

| # | 需求 | 决策 | 影响 |
|---|---|---|---|
| 1 | 默认科目表 | **多国别模板包**（CN-GAAP / INTL / JP / US，按国别选配） | 见 [01 章 §3](./01-gl-kernel.md)，结构一致只换码；自动凭证按 `Role` 锚点，换包零改动 |
| 2 | 凭证复核 | **手工强制 maker-checker，自动凭证可信直过** | 手工：草稿→待复核→已过账(过账人≠制单人)；自动凭证 `AutoPosted` 直过，留批量复核开关 |
| 8 | 成本中心维度 | **现在就加 `CostCenterId`**（机台/工序/部门），MVP 可不填 | 分析性会计维度，回填历史极贵故先占位；机台可挂 MES `Machine` |
| 3 | 会计期间精度 | **月结起步** | `FiscalPeriod` 按年月，月度结账 + 锁期 |
| 4 | 目标税制 | **通用/国际**，不绑国别 | 税做成可配置 `TaxCode` 税率表 |
| 5 | 总账深度 | **自建完整双辕 GL** | 科目+凭证+试算+资产负债/损益表 |
| 6 | 成本方法 | **实际为主 + 标准参考** | 工单归集实际料工费，标准成本作差异分析 |
| 7 | MVP | **应付 AP**（发票手工录起步，预留 PO 三单匹配） | 阶段 2 第一个落地 |

### ✅ 02/03/05 两轮评审追加决策（2026-06-10）

- 财年起始月可配 `FiscalYearStartMonth`（日本4月起）；FiscalPeriod 加 FiscalYear/PeriodNo/PeriodStart/PeriodEnd
- 试算平衡表三栏（期初+本期发生+期末），B/S 科目期初含开账至今累计（修正初版只算本期的 bug）
- AP 防重录（SupplierInvoiceNo 唯一约束）、独立 BankAccount 主数据、核销尾差+现金折扣、预付款纳入 MVP
- AP 更正：发票红冲 + **付款撤销**（先解核销再红冲）+ **采购退货/供应商红字**（IsCreditMemo，联动 WMS RMA）
- 自动凭证 PostingRuleLine 两类：FixedRole + DocumentLines 透传（混行发票各进各科目）
- **多币种 AP 延后到 07 章**：MVP 本位币为主，外币双金额+期末重估放 07

### ⏳ 仍待定（不阻塞阶段 0–2，到对应阶段再拍）

- **多账簿 / 多法人合并**：本路线（单企业纸箱厂）建议**不做**，留到日后多租户阶段。
- **成本的"工"和"费"数据源**（阶段 4 关键前置）：料可吃 PaperRoll/InkLot，但**工时与制造费用 CP6 目前没采集**。两个选项：(a) 先用"标准费率 × 工单标准工时"估算，(b) 新增工时采集。到阶段 4 前必须定。

---

*生成于 2026-06-10。配套实现将落于 `CP6.Entity/DomainModels/Fin`、`CP6.Core/Services/Fin`、`cp6.web/src/views/fin`（随章节推进创建）。需求基线：通用税制 / 自建完整双辕 GL / 实际为主+标准参考成本 / MVP=应付 AP。*
