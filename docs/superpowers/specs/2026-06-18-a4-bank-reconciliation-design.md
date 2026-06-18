# A4 银行对账（Bank Reconciliation）设计 spec

> ERP 完整性路线 A4。把"银行流水"与"账面银行 GL 科目分录"对账做真：导入银行流水 → 自动/人工撮合命中银行 GL 科目的 `Fin_JournalLine` → 单边项一键入账或标记未达 → 出双向余额调节表 → 期末锁定（并守卫锁后过账）。
>
> 源对话：brainstorming 定稿（A4-D1~D5 + 用户两轮 review 全采纳）。日期 2026-06-18。命名空间 **Fin**。

---

## 0. 目标与范围

**题眼**：系统已有 `BankAccount`/`Payment`/`Receipt`/`ApSettlement`/`ArSettlement`（收付款↔发票核销）+ `AutoVoucherEngine`/`JournalEntryService`/`FiscalPeriodService`/`GlAccount.Role` 全套 GL 基建，但**缺银行流水侧**：没有 `BankStatement`/流水导入/对账撮合/余额调节表。A4 补齐"银行流水 ↔ 银行 GL 科目分录"对账。

**核心区分**：本系统已有的 **核销(Settlement)** = "收付款单 ↔ 发票"；A4 的 **银行对账(Reconciliation)** = "银行流水行 ↔ 命中银行 GL 科目的凭证行"。两者正交，A4 不改动核销。

**账面侧策略（D-A）**：流水匹配目标 = 银行 GL 科目（`BankAccount.GlAccountId`）上的所有 `Fin_JournalLine`（含 Payment/Receipt 产生的、也含手工凭证），**直查不投影**（不建 BankBookEntry 台账，避免与不可变凭证重复/同步漂移）。

**纳入 MVP**：CSV+Excel 导入(Preview/Confirm) + 手工录入 + 导入模板；自动撮合(1:1 / 1:N / N:1，唯一解) + 人工撮合(N:M)；银行单边项一键生成凭证(幂等+反冲) 或标记未达；双向余额调节表；Lock/Unlock + 锁后过账守卫；外币按原币匹配；操作级权限 + 审计日志；五语 i18n；分层测试(InMemory + SQLite) + gstack QA。

**推迟（非本期）**：银行 API / 开放银行自动取流水；ML/模糊匹配（仅规则 + 文本相似度排序）；银行余额期末汇兑重估（沿用现有 `FxRevaluationService`/`FxReval`）；调节表 PDF 导出；跨账户转账自动识别；对账单 OCR；**部分匹配/部分核销（MVP 明确不做——见 §8.5）**。

---

## 1. 现状与依赖（落码前必读，均已存在）

| 资产 | 位置 | A4 用法 |
|---|---|---|
| `BankAccount`(Fin_BankAccount) | `CP6.Entity/DomainModels/Fin/BankAccount.cs` | `Code`/`Name`/`BankName`/`AccountNo`/`CurrencyCd`/**`GlAccountId`**/`IsActive`。A4 以 `GlAccountId` 定位账面侧凭证行 |
| `JournalEntry`/`JournalLine` | `Fin/JournalEntry.cs`/`JournalLine.cs` | `JournalLine.{AccountId,Debit,Credit,CurrencyCd,FxRate,OrigAmount}`；`JournalEntry.{VoucherDate,PeriodId,Source,Status}`。账面侧候选来源 |
| `JournalEntryService` | `Core/Services/Fin/` | `AutoPostAsync`(一步建+校验+过账)/`ReverseAsync`(红冲)。单边项凭证 + 锁后守卫挂载点 |
| `AutoVoucherEngine` | `Core/Services/Fin/AutoVoucherEngine.cs` | `FinBizEvent`→`PostingRule`→分录；`GlAccount.Role` 锚定科目 |
| `FiscalPeriodService` | `Core/Services/Fin/` | `ResolveAsync(date)`/`EnsureOpenAsync(date)`/`IsOpenAsync`。会话期间 + 单边项凭证落期 |
| `GlAccount`(Fin_GlAccount) | `Fin/GlAccount.cs` | `Role`("BANK"/"FX_GAIN"…)、`IsLeaf`/`IsActive`/`RequirePartner`。单边项对方科目解析 |
| `FinSequenceService` | `Core/Services/Fin/` | `NextAsync(key,date)` 采番。会话号/对账单号 |
| `FinResult` | `Core/Services/Fin/FinResult.cs` | `{Ok,Code,Args}`+`Pass()`/`Fail(code,args)`。A4 服务统一返回 |
| `VoucherSource` enum | `Fin/JournalEntry.cs` | 现 Manual/AP/AR/Cost/Carryover/Reversal/FxReval=6 → **A4 新增 `BankRecon=7`** |
| Fin 控制器范式 | `WebApi/Controllers/Fin/` | `[Authorize]`+`[RequirePermission]`，`Ok2(data)`/`Fin(FinResult)`/`CurrentUser` |
| Fin 菜单 600 组 | `WebApi/Program.cs` | 601~613 已用，**614 空位**给银行对账 |
| 多租户基类 | `BaseTenantEntity`(`CP6.Entity/BaseTenantEntity.cs`) | =`BaseEntity`(Id/审计)+`TenantId`；**不含 RowVersion/IsDeleted**（已核实；含二者的是 `BaseBizEntity`）。A4 实体继承 `BaseTenantEntity` 并**显式加 `RowVersion`**（§2/§8.3）；唯一索引声明后自动补 `TenantId` 前缀（沿用 A2 机制） |
| 审计日志 | `Sys_OperLog` | A4 关键操作写审计（§12） |
| Pub 导入工具 | （Pub 模块，落码时核实） | Excel 解析优先复用；无则加轻量库（§3.6） |

---

## 2. 数据模型

5 个新实体，均继承 `BaseTenantEntity`，表前缀 `Fin_`。

### 2.1 `BankStatement`（对账会话头）`Fin_BankStatement`

| 字段 | 类型 | 说明 |
|---|---|---|
| `No` | string(30) | 会话号，`BKR-yyyyMM-nnnn`（FinSequenceService） |
| `BankAccountId` | Guid | → BankAccount |
| **`FiscalPeriodId`** | Guid | → FiscalPeriod（**财务期间主键**，对齐 EnsureOpenAsync/结账） |
| `PeriodStart`/`PeriodEnd` | DateTime | 冗余展示（由 FiscalPeriod 派生） |
| `StatementDate` | DateTime? | 对账单日期（展示） |
| `CurrencyCd` | string(3) | 取自 BankAccount（null=本位币） |
| `OpeningBalance` | decimal(18,2) | 对账单期初余额 |
| `ClosingBalance` | decimal(18,2) | 对账单期末余额 |
| `Status` | enum `BankStatementStatus` | Open=0 / Locked=1 |
| `ImportFileName` | string(255)? | 末次导入文件名 |
| `LockedStatementInternalDiff` | decimal(18,2)? | **锁定快照**：Opening+ΣDeposit−ΣWithdrawal−Closing |
| `LockedReconciledDiff` | decimal(18,2)? | **锁定快照**：BankAdjusted−BookAdjusted |
| `LockedBankAdjustedBalance` | decimal(18,2)? | **锁定快照**：银行侧调整后余额 |
| `LockedBookAdjustedBalance` | decimal(18,2)? | **锁定快照**：账面侧调整后余额 |
| `LockSnapshotJson` | string(max)? | **锁定快照**：完整调节表 JSON（审计追溯） |
| `LockedAt`/`LockedBy` | DateTime?/string? | 锁定审计 |
| `RowVersion` | byte[]? | 乐观并发（§8.3） |

> **重要（实时 vs 快照）**：`StatementInternalDiff`/`ReconciledDiff`/`BankAdjustedBalance`/`BookAdjustedBalance` **不作 BankStatement 长期存储**——Open 态由 `GetReconciliationStatementAsync` / `LockAsync` **实时重算**（不依赖旧值）；上面 `Locked*` 字段仅在 Lock 成功时写入，作锁定时点快照与审计追溯，**非 Open 态实时真相来源**（点 2.2）。

唯一索引：`UX_Fin_BankStatement_AcctPeriod` = (BankAccountId, FiscalPeriodId)（自动补 TenantId 前缀）——**每账户每期一个会话**。

### 2.2 `BankStatementLine`（银行流水行）`Fin_BankStatementLine`

| 字段 | 类型 | 说明 |
|---|---|---|
| `StatementId` | Guid | → BankStatement |
| `LineNo` | int | 行序 |
| `TxnDate` | DateTime | 交易/起息日 |
| `Direction` | enum `BankLineDirection` | Deposit=1(入,↔银行GL借) / Withdrawal=2(出,↔银行GL贷) |
| `Amount` | decimal(18,2) | 正数（方向由 Direction） |
| `SignedAmount` | decimal(18,2) | **只读计算属性 / DB computed column**：Deposit=+Amount，Withdrawal=−Amount（统一求和口径 §4.1）。**禁止前端传入**；若项目习惯物化，则仅后端在 Amount/Direction 变化时统一重算（点 2.1） |
| `CurrencyCd` | string(3)? | 原币（外币账户），null=本位币 |
| `Description` | string(500)? | 摘要 |
| `CounterpartyName` | string(200)? | 对方 |
| `RefNo` | string(100)? | 参考号/传票号 |
| `BalanceAfter` | decimal(18,2)? | 流水余额（若文件有） |
| `Source` | enum `BankLineSource` | Imported=1 / Manual=2 |
| `MatchStatus` | enum `BankLineMatchStatus` | Unmatched=0 / Matched=1 / MarkedPending=2 |
| `Category` | enum `BankLineCategory` | None=0 / BankCharge=1 / InterestIncome=2 / Transfer=3 / Pending=4 / Other=5 |
| `MatchGroupId` | Guid? | → BankReconMatch（null=未匹配） |
| `GeneratedJournalEntryId` | Guid? | 单边项一键生成的凭证（幂等键，§5.1） |
| `GeneratedAt`/`GeneratedBy` | DateTime?/string? | 生成审计 |
| `ImportBatchNo` | string(30)? | 导入批次（追溯） |
| `RawRowJson` | string(max)? | 原始行 JSON（追溯） |
| `RawRowHash` | string(64)? | 原始行哈希（强重复判定） |
| `Fingerprint` | string(128)? | 去重指纹（§3.4） |
| `RowVersion` | byte[]? | 乐观并发（撮合/改行核心实体，§8.3） |

> **语义**：生成凭证并自动配平后该行 `MatchStatus=Matched`（**不再叫 BankOnly**）；`Category` 仅作差异来源分类。`MarkedPending` = 仅标记未达/待定（不入账）。

索引：`IX_Fin_BankStatementLine_Stmt`(StatementId)；`IX_..._Fingerprint`(StatementId,Fingerprint)。

### 2.3 `BankReconMatch`（匹配组，统一承载 1:1 / 1:N / N:1 / N:M）`Fin_BankReconMatch`

| 字段 | 类型 | 说明 |
|---|---|---|
| `StatementId` | Guid | → BankStatement |
| `MatchType` | enum `BankReconMatchType` | Auto=1 / Manual=2 |
| `StmtSignedSum` | decimal(18,2) | 组内流水行 ΣSignedAmount（=组内凭证行银行侧带方向合计，必相等） |
| `MatchedAt`/`MatchedBy` | DateTime/string | 审计 |
| `Note` | string(500)? | 备注 |
| `RowVersion` | byte[]? | 乐观并发（撮合台核心实体，§8.3） |

约束（服务层强校验）：组内 Σ(流水行 SignedAmount) == Σ(凭证行 银行侧 SignedAmount)，**完全相等**（MVP 无部分匹配/容差，§8.5）。

### 2.4 `BankReconJournalLink`（匹配组 ↔ 凭证行；不动不可变凭证）`Fin_BankReconJournalLink`

| 字段 | 类型 | 说明 |
|---|---|---|
| `MatchGroupId` | Guid | → BankReconMatch |
| `JournalLineId` | Guid | → Fin_JournalLine.Id（账面侧） |
| `JournalEntryId` | Guid | 冗余（便于按凭证查/守卫） |
| `BankSignedAmount` | decimal(18,2) | 该凭证行银行侧带方向金额（Debit=+,Credit=−） |

唯一索引：`UX_Fin_BankReconJournalLink_JL` = (JournalLineId)（自动补 TenantId 前缀）——**一条凭证行只能对账一次**（并发守卫，§8.4）。索引 `IX_..._Group`(MatchGroupId)。**并发保护靠该唯一约束 + 事务，本表不需 RowVersion。**

### 2.5 `BankImportProfile`（导入列映射模板）`Fin_BankImportProfile`

| 字段 | 类型 | 说明 |
|---|---|---|
| `Name` | string(100) | 模板名 |
| `BankAccountId` | Guid? | 绑定账户（null=通用） |
| `FileFormat` | enum | Csv=1 / Excel=2 |
| `Encoding` | string(20) | UTF-8 / Shift_JIS / GBK …（CSV） |
| `Delimiter` | string(4) | CSV 分隔符（默认 `,`） |
| `SkipHeaderRows` | int | 跳过表头行数 |
| `DateField` | string(40) | 日期列（列号或表头名） |
| `DateFormat` | string(40) | 如 `yyyy/MM/dd` |
| `AmountMode` | enum | SignedSingle=1（单列带符号） / **DepositWithdrawalColumns=2**（入款列/出款列双列） |
| `AmountField` | string(40)? | SignedSingle 模式金额列 |
| `DepositAmountField`/`WithdrawalAmountField` | string(40)? | DepositWithdrawalColumns 模式：入款列/出款列。**业务语义命名，不采用银行 Debit/Credit 记账视角**（避免与企业银行 GL 借贷方向混淆，点 9） |
| `SignRule` | enum | SignedSingle 时：PositiveIsDeposit=1 / PositiveIsWithdrawal=2 |
| `DescriptionField`/`CounterpartyField`/`RefNoField`/`BalanceField` | string(40)? | 可选映射 |
| `DecimalSeparator`/`ThousandsSeparator` | string(2) | 金额解析（默认 `.` / `,`） |
| `IsActive` | bool | 启用 |
| `RowVersion` | byte[]? | 乐观并发（§8.3） |

> **方向解析规则（必须显式，§3.6）**：DepositWithdrawalColumns 模式——入款列(`DepositAmountField`)有值=Deposit、出款列(`WithdrawalAmountField`)有值=Withdrawal；SignedSingle 模式——按 `SignRule` 判定正负对应 Deposit/Withdrawal。

枚举 + 迁移：新增上述 enum；`VoucherSource` 追加 `BankRecon=7`；迁移 `A4BankReconciliation`。

---

## 3. 导入流程

### 3.1 模式
CSV/Excel 导入 **与** 手工录入并存。导入分两步：**Preview（dryRun，解析不落库）→ Confirm（确认落库）**（护栏①）。

### 3.2 Preview（dryRun）
上传文件 + 选 `BankImportProfile` → 按映射逐行解析为内存中的候选行 → 返回 **导入报告**，不写库：
- 成功行数、失败行数、强重复数、疑似重复数；
- 每个失败/重复行的行号 + 原因 + 原始内容；
- 解析出的明细预览（供前端展示确认）。

### 3.3 Confirm
Confirm **以 Preview 返回的"可导入行"为基础**落库为 `BankStatementLine`（`Source=Imported`，带 `ImportBatchNo`/`RawRowJson`/`RawRowHash`/`Fingerprint`）。强重复默认跳过，疑似重复默认导入（前端可逐行取舍）。
**失败行处理（点 8，MVP 简单规则）**：若 Confirm 重新解析时仍存在日期/金额/方向等**致命解析失败**，则**整批 Confirm 拒绝落库**（返回 `E-A4-IMPORT-001`），不落部分行——即"要么全部可导入行落库，要么整批退回"。因此 **A4 不持久化异常导入行、不新增 ImportBatch 表**；`LockAsync` 也无需检查"未处理异常行"（见 §7.1 改写）。

### 3.4 去重（护栏⑤）
- `Fingerprint` = hash(TxnDate + Direction + Amount + RefNo + CounterpartyName + Description + BalanceAfter)；`RawRowHash` = hash(原始行文本)。
- **强重复**：`RawRowHash` 或 `Fingerprint` 与会话内已有行完全一致 → 默认跳过。
- **疑似重复**：(TxnDate + Direction + Amount + RefNo) 相同但 摘要/对方/余额 不同 → 仅 warning，**不自动跳过**（避免误杀同日同额多笔）。

### 3.5 容错
空行跳过；单行解析失败（日期/金额/方向）→ 收集进报告，**不中断整批**。

### 3.6 解析器与 Excel（护栏⑧）
`IBankStatementImporter` 按 `BankImportProfile` 解析。**显式规则**：编码、分隔符、跳过表头行数、日期格式、金额小数/千分位、`AmountMode`(单列带符号 vs 借贷双列)、`SignRule`(正号=入/出)、方向解析。CSV 一等公民（轻量解析）；Excel 优先复用 Pub 现有导入工具（落码时核实），无则引轻量库（NPOI/ClosedXML 任一，单点封装）。

### 3.7 手工增删改
仅 `Status=Open` 时允许；`Source=Manual`；已匹配行须先 `Unmatch` 才能改。

---

## 4. 撮合算法

### 4.1 SignedAmount 统一口径
- 银行流水：Deposit=+Amount，Withdrawal=−Amount。
- 银行 GL 凭证行：Debit=+金额，Credit=−金额（银行为资产科目，借增贷减）。
- 匹配/求和一律用 SignedAmount，方向天然内含。

### 4.2 账面侧候选来源（护栏②/⑤/⑨）
候选 = `Fin_JournalLine` 满足：
- `TenantId` 当前租户；
- `AccountId == BankAccount.GlAccountId`；
- 所属 `JournalEntry.Status == Posted` 且 **未反转**（`Source != Reversal` 且未被红冲）；
- 未被任何 `BankReconJournalLink` 占用；
- `JournalEntry.VoucherDate <= BankStatement.PeriodEnd`（**含历史未达项**，不限本期）；
- 默认搜索窗 `PeriodStart − 90 天 .. PeriodEnd`，人工高级搜索可放宽（窗口仅为性能默认，非业务硬边界）；
- **外币（护栏⑨）**：会话 `CurrencyCd` 非本位币时，凭证行须 `CurrencyCd == 会话币种` 且 `OrigAmount` 非空，按 `OrigAmount` 的 SignedAmount 匹配；原币缺失或币种不一致的凭证行**不进入自动候选**（可人工处理）。

`|流水.TxnDate − 凭证.VoucherDate|` **仅作排序优先级，非硬过滤**。

### 4.3 Phase 1 — 1:1 精确
对每条未匹配流水行，在候选中找 SignedAmount 完全相等者：
- 恰一个候选 → 自动建 1:1 `BankReconMatch`(Auto)；
- 多候选 → 按 (日期接近度 + RefNo/对方/摘要 文本相似度) 排序，**留人工**；
- 无候选 → 留人工。

### 4.4 Phase 2 — 有界归并（1:N / N:1）
对剩余未匹配行，在 **同 RefNo / 同对方 / 同日期窗 / 同金额桶** 内做**有界子集求和**：
- 1 流水 ↔ M 凭证（批量代发：一笔银行出账 ↔ 多张付款凭证行）；
- N 流水 ↔ 1 凭证（合并收款）；
- 子集大小上限 **K ≤ 8**，窗口有界；
- **仅唯一解自动确认**；多解 → 不自动，转人工。
- **绝不做无界子集和、不跨账户、不跨币种。**

### 4.5 人工撮合 N:M（护栏⑦/⑩）
`ManualMatchAsync(流水行[], 凭证行[])`：
- 全部流水行属同一 `BankStatement`；全部凭证行 `AccountId==BankAccount.GlAccountId`；
- 均未被其他匹配组占用；
- **Σ流水 SignedAmount == Σ凭证 SignedAmount（完全相等，无部分匹配）**；
- 通过 → 建 `BankReconMatch`(Manual)；金额不平/方向不符/已占用 → 拒（错误码见 §16）。
- 典型场景：客户付 1000、银行实收 990、手续费 10 → 一条 +990 流水 ↔ 凭证行(借银行 +1000) + (贷银行 −10) 净 +990。

`UnmatchAsync(groupId)`：仅 Open 会话允许拆组并释放流水行/凭证行；若组关联了 `BankRecon` 自动凭证，**不自动删凭证**，须走反冲（§5.1）。

### 4.6 候选推荐
`GetCandidatesAsync(statementLineId, widen?)`：返回排序后的未对账候选凭证行（金额接近/日期/文本相似），供人工撮合台用。

---

## 5. 单边项处理

### 5.1 银行单边项（流水有、账面无）
**方式一 — 生成凭证并自动匹配** `GenerateBankOnlyVoucherAsync(流水行[], 对方科目Id或Role, 可选往来)`
- 凭证方向：Withdrawal（手续费）借 费用/财务费用、贷 银行GL；Deposit（利息）借 银行GL、贷 利息收入。银行侧 = `BankAccount.GlAccountId`；对方科目由用户选或 `PostingRule`/`Role` 默认解析。

- **批量语义（点 5，MVP）**：**一条银行流水生成一张 BankRecon 凭证**，**不做多行合并凭证**。前端可批量选多条，**后端按行逐条执行，返回逐行结果**。批量入口约束：所有行属同一 `BankStatement`、会话 `Open`、每行 `Unmatched` 或 `MarkedPending`、每行未生成过 BankRecon 凭证。**采用一行一事务逐条执行**：某行失败不回滚已成功行（非 all-or-nothing），逐行返回成功/失败原因。

- **单条事务（点 6，强制）**：对单条流水，下列步骤必须在**同一数据库事务**内完成，任一步失败整体回滚（杜绝"凭证已过账但未匹配"的孤儿态）：
  1. `FiscalPeriodService.EnsureOpenAsync(TxnDate)` 落期 → `JournalEntryService.AutoPostAsync` 过账，`VoucherSource=BankRecon`；
  2. 写回 `BankStatementLine.GeneratedJournalEntryId`/`GeneratedAt`/`GeneratedBy`；
  3. 建 `BankReconMatch`；
  4. 建 `BankReconJournalLink`（关联新银行GL凭证行）；
  5. 更新 `BankStatementLine.MatchStatus=Matched`；
  6. 写 `Sys_OperLog`。

- **幂等（点 8/原护栏）**：流水行 `GeneratedJournalEntryId` 非空时再次调用 → 拒 `E-A4-BANKONLY-DUP`。

- **改错走反冲（点 7）**：科目错时 Unmatch → `JournalEntryService.ReverseAsync(原 BankRecon 凭证)` → 重生成 → 重匹配；**不物理删已过账凭证**（遵守不可变凭证原则）。`GeneratedJournalEntryId` 表示**当前有效**的 BankRecon 凭证：原凭证 Reverse 成功后，先 `Unmatch` 该行 → **清空旧 `GeneratedJournalEntryId`** → 重生成时写入**新 `GeneratedJournalEntryId`**（不被幂等规则挡住）。原凭证↔反冲凭证的追溯走 `JournalEntry.Reversal` 链 + `Sys_OperLog`；**不新增 `ReversedGeneratedJournalEntryId` 字段**（保持模型轻量）。

**方式二 — 仅标记** `MarkBankOnlyAsync(流水行, Category=Pending/…)`：科目未定/暂不入账 → `MatchStatus=MarkedPending`，列入调节表，不生成凭证。

### 5.2 账面单边项（账面有、流水无）
银行GL上未对账的凭证行（`VoucherDate <= PeriodEnd` 且未占用）= **在途存款/未取付支票**，自动作为调节项进调节表，无需操作（下期流水到账再匹配）。

---

## 6. 余额调节表（调整后余额法，护栏⑦）

> **方向修正（核心）**：在途存款/未取付支票是"账面已记、银行未动"→ 调**银行侧**；银行单边项是"银行已动、账面未记"→ 调**账面侧**。

```
银行侧调整余额  BankAdjustedBalance =
      StatementClosingBalance                  对账单期末余额
    + BookOnlyDepositInTransit                 账面单边·借方未达（在途存款）
    − BookOnlyOutstandingPayment               账面单边·贷方未达（未取付支票）

账面侧调整余额  BookAdjustedBalance =
      GlBankEndingBalance                       GL银行科目期末余额(Σ借−贷 至 PeriodEnd)
    + BankOnlyDepositNotBooked                  银行单边·已收未入账
    − BankOnlyWithdrawalNotBooked               银行单边·已扣未入账

ReconciledDiff = BankAdjustedBalance − BookAdjustedBalance
ReconciledDiff == 0  ⇒  允许锁定
```

`GetReconciliationStatementAsync(statementId)` 返回展示项：对账单期初/期末余额、本期流水收入/支出合计、GL银行科目期末余额、已匹配流水/账面金额、账面单边项(在途存款/未取付支票明细)、银行单边项(已收未入账/已扣未入账明细)、`StatementInternalDiff`、`ReconciledDiff`、`BankAdjustedBalance`、`BookAdjustedBalance`。

**实时重算（点 2.2）**：`GetReconciliationStatementAsync` 与 `LockAsync` **必须实时重算**上述四量，**不读 BankStatement 上的旧值**；`BankStatement.Locked*` 仅在 Lock 时写快照（§2.1/§7.1）。

**外币 GL 银行余额（点 3，关键）**：`GlBankEndingBalance` 的计算口径随账户币种分流——
- `BankAccount.CurrencyCd` 为本位币或 null：用 `JournalLine.Debit/Credit`（本位币）计算 Σ借−贷 至 `PeriodEnd`。
- `BankAccount.CurrencyCd` 非本位币：**必须用 `JournalLine.OrigAmount` 按 `BankAccount.CurrencyCd` 计算**；本位币折算额仅作展示，**不参与 `ReconciledDiff`**。若银行 GL 凭证行缺 `OrigAmount` 或 `CurrencyCd` 与账户币种不一致 → 该行不进自动候选(§4.2)，并在人工候选中提示"原币信息异常"。
- **目的**：避免 USD 对账单余额与 CNY 折算 GL 余额直接相减。

---

## 7. 锁定工作流

### 7.1 `LockAsync(statementId)` 校验（**实时重算**后全满足才锁）
`LockAsync` 先**实时重算** InternalDiff/ReconciledDiff/BankAdjusted/BookAdjusted（不读旧值），再校验：
1. `StatementInternalDiff == 0`（Opening+ΣDeposit−ΣWithdrawal==Closing，护栏⑨）；
2. `ReconciledDiff == 0`；
3. 当前会话不存在未确认的导入批次（Confirm 阶段不会落库解析失败行，见 §3.3，故无需检查"异常导入行"，点 8）；
4. 所有 `BankReconMatch` SignedAmount 合计一致；
5. 所属 `FiscalPeriod` 仍为 Open。
失败 → `E-A4-RECON-001`（含差额明细）。
**锁成功时写快照**：`LockedStatementInternalDiff`/`LockedReconciledDiff`/`LockedBankAdjustedBalance`/`LockedBookAdjustedBalance`/`LockSnapshotJson`/`LockedAt`/`LockedBy`（§2.1）。
前端锁定前必弹**调节表确认对话框**展示 4 个余额量（护栏⑦/§10）。

### 7.2 锁后冻结 + 过账守卫（护栏③，关键）
锁后：`Status=Locked`；`BankStatementLine`/`BankReconMatch`/`Category` 禁增删改。
**过账守卫**（避免循环依赖，§1）：`JournalEntryService` 的 `AutoPostAsync`/`PostAsync`/`ReverseAsync` 路径，对每条命中"某 `BankAccount.GlAccountId`"的凭证行，**直查同 `CP6Context` 的 `BankStatements`**：若存在覆盖该 (账户所属 FiscalPeriod) 且 `Status=Locked` 的会话 → 拒 `E-A4-RECON-LOCKED-POSTING`。
- 一个 GL 科目可能被多个 BankAccount 共用 → **守卫按"该科目对应的任一已锁会话"保守阻断**（护栏⑤）。
- 需先 `Unlock` 才能再过账。

**反冲守卫（点 4，关键）**：`ReverseAsync` 除上面"新反冲凭证落期"的过账守卫外，**还须检查被反冲的原 `JournalLine` 是否已存在 `BankReconJournalLink`**：若原凭证行已被对账、且其 `BankReconMatch` 所属 `BankStatement.Status=Locked` → **禁止反冲**，拒 `E-A4-RECON-LOCKED-REVERSAL`，须先 `Unlock` 原银行对账会话。（防止"锁定对账已成立，却把被对账的原凭证抽掉"。）

### 7.3 `UnlockAsync(statementId, reason)`
必填原因；仅当所属 `FiscalPeriod` 仍 Open 时允许（已结账禁，`E-A4-RECON-002`）；写审计（操作人/时间/原因）。

---

## 8. 边界与异常

1. **多币种**：以 `BankAccount.CurrencyCd` 为准；外币按原币金额匹配（§4.2 护栏⑨）；银行余额期末汇兑重估沿用现有 `FxRevaluationService`，不在 A4。
2. **反转凭证**：已反转凭证行不进自动候选；已对账凭证行若需反转 → 先 Unlock 会话再走反冲。
3. **并发（护栏⑥，点 1）**：双层保护——(a) `BankReconJournalLink.JournalLineId` 唯一约束 + 事务内完成 AutoMatch/ManualMatch/Generate，防同一凭证行被多人重复占用；(b) `BankStatement`/`BankStatementLine`/`BankReconMatch`/`BankImportProfile` 的 **`RowVersion` 乐观并发**——前端提交 `Match`/`Unmatch`/`EditLine`/`MarkPending`/`GenerateVoucher` 时**带 RowVersion**；后端版本冲突 → `E-A4-CONCURRENCY-001`，前端提示"当前流水/凭证状态已变化，请刷新候选列表后重试"。`BankStatementLine` 与 `BankReconMatch` 是撮合台并发冲突核心实体。
4. **金额精度（点 10，统一）**：A4 金额字段**沿用现有 Fin 模块精度 `decimal(18,2)`**（与 `JournalLine.Debit/Credit` 一致）；**匹配比较按系统实际存储精度完全相等**；`CurrencyCd` 小数位**仅用于前端展示格式化**，不改变数据库精度与匹配精度。无业务容差（任何容差须 `BankReconTolerance` 配置且仅唯一解自动执行——本期不实现）。
5. **MVP 不做部分匹配（护栏⑩）**：所有 `BankReconMatch` 必须 SignedAmount **完全配平**；不支持一条流水部分核销多笔的"部分分配"。

---

## 9. API

Fin 范式：`[Authorize]` + `[RequirePermission(资源键, 动作)]`，`Ok2(data)` / `Fin(FinResult)` / `CurrentUser`。

**`BankStatementController` `/api/fin/bank-statement`**
- `GET ?bankAccountId=&fiscalPeriodId=&status=`（列表）/ `GET {id}`（含行 + 报告）
- `POST`（建会话：账户 + 期间 + 期初/期末余额）
- `POST {id}/import?dryRun=true|false`（multipart 文件 + profileId；dryRun=Preview 报告，false=Confirm 落库）
- `POST {id}/line` / `PUT {id}/line/{lineId}` / `DELETE {id}/line/{lineId}`（手工增改删，仅 Open）

**`BankReconciliationController` `/api/fin/bank-recon`**
- `POST {statementId}/auto-match` / `POST {statementId}/manual-match`(流水行[]+凭证行[]) / `POST unmatch/{groupId}`
- `GET {statementId}/candidates?lineId=&widen=` / `POST {statementId}/generate-voucher` / `POST {statementId}/mark-pending`
- `GET {statementId}/reconciliation-statement`（调节表 4 量 + 明细）
- `POST {statementId}/lock` / `POST {statementId}/unlock`(reason)

**`BankImportProfileController` `/api/fin/bank-import-profile`** — CRUD。

---

## 10. 前端（Vue3 + element-plus）

- **`BankReconciliationView.vue`（撮合台）**：左=流水行(过滤 未匹配/已匹配/待处理)，右=候选凭证行；勾选 N+M → 人工匹配；自动撮合按钮；生成凭证对话框(选科目/Role/往来)；标记未达；匹配组列表 + 拆组；**调节表面板**(4 余额量 + 调节项 + ReconciledDiff)。**并发冲突提示 + 刷新重试**（护栏⑥）。**Lock 前弹调节表确认对话框**展示 `StatementInternalDiff`/`ReconciledDiff`/`BankAdjustedBalance`/`BookAdjustedBalance`（护栏⑦）。
- **`BankStatementView.vue`**：会话列表(账户×期间)、建会话、导入对话框(选 Profile + 上传 → **Preview 报告**(成功/失败/重复逐行) → Confirm)。
- **`BankImportProfileView.vue`**：模板 CRUD（列映射可视化编辑：方向解析/编码/金额符号/借贷双列/跳过表头）。
- 类型/api 在 `cp6.web/src/{types,api}/fin/bankRecon.ts`。

---

## 11. 权限（操作级，护栏②）

资源键 = 派生 MenuKey（参 Fin D-2 权限做法）。**操作级拆分**，至少：
`view` / `import` / `match`(auto+manual+unmatch) / **`generate-voucher`** / `mark-pending` / **`lock`** / **`unlock`** / **`profile-manage`**。
Seed 默认授 admin；`[RequirePermission]` 贴各端点（`HasActionAsync` 无 admin 旁路，属性与 seed 同 commit）。

---

## 12. 审计日志（护栏③）

下列操作写 `Sys_OperLog`（操作人/时间/对象/前后摘要）：`ManualMatch`、`Unmatch`、`GenerateVoucher`、`MarkPending`、`Lock`、`Unlock`（Unlock 额外记原因）。导入 Confirm 记批次摘要。

---

## 13. 菜单 + i18n

- 菜单 **614 银行对账**(`/fin/bank-reconciliation`) 挂 Fin 组 600，授 RoleId=1，幂等。导入模板作撮合台内 tab/对话框（不另占菜单）。
- `CP6.WebApi/Seed/I18nBankReconScreenSeed.cs` 五语(ZhCN/ZhTW/En/Ja/Ko)：菜单/视图标题/全字段标签/按钮 + 全部 E-A4-*/W-A4-* 文案；接 `Program.cs` i18n 链 `.Concat(...)`；`npm run i18n:pull`+`i18n:check` 绿。

---

## 14. 多租户 + 迁移

- 5 实体继承 `BaseTenantEntity`；唯一索引声明后自动补 `TenantId` 前缀（A2 机制）：`UX_Fin_BankStatement_AcctPeriod`(BankAccountId,FiscalPeriodId)、`UX_Fin_BankReconJournalLink_JL`(JournalLineId)。
- 迁移 `A4BankReconciliation`：建 5 表 + 索引 + `VoucherSource.BankRecon=7`。命令 `dotnet ef migrations add A4BankReconciliation --project CP6.Core --startup-project CP6.WebApi`（会先构建，勿带 `--no-build`）。

---

## 15. 测试分层（护栏④）+ 验收标准

**分层**：
- **EF InMemory** —— 覆盖服务业务逻辑（撮合算法、调节表公式、单边项、状态机）。`TestHelper.CreateInMemoryContext()`。
- **SQLite in-memory** —— 覆盖 **唯一索引(JournalLineId/会话唯一)、事务、外键、并发(同凭证行重复占用)、锁后过账守卫**（EF InMemory 不校验这些）。测试项目按需加 `Microsoft.Data.Sqlite`+EF Sqlite provider（落码时若未引则加）。

**验收标准（A4-AC-001~010，plan 阶段落成测试项）**：

| 编号 | 验收 |
|---|---|
| AC-001 | 导入后 `Opening+ΣDeposit−ΣWithdrawal == Closing`（InternalDiff=0），否则不能锁定 |
| AC-002 | 流水与凭证 SignedAmount 相等，且在默认候选范围内**唯一命中**时自动 1:1 匹配；**日期接近度仅作候选排序优先级，不作硬性排除**（点 12，与 §4.2 一致） |
| AC-003 | 一流水 ↔ 多凭证：仅有界求和**唯一解**时自动确认 |
| AC-004 | 多流水 ↔ 一凭证：仅有界求和**唯一解**时自动确认 |
| AC-005 | 人工撮合支持 N:M，但 Σ SignedAmount 必须完全相等 |
| AC-006 | 手续费流水可一键生成 `BankRecon` 凭证并自动与该流水匹配（行变 Matched） |
| AC-007 | 同一流水不能重复生成 BankRecon 凭证（`E-A4-BANKONLY-DUP`） |
| AC-008 | `ReconciledDiff != 0` 时禁止锁定 |
| AC-009 | 锁定后，禁止向该银行账户对应银行 GL 科目过账本期凭证（`E-A4-RECON-LOCKED-POSTING`，SQLite 层测） |
| AC-010 | 会计期间已结账后，禁止 Unlock（`E-A4-RECON-002`） |

**补充测试（点 13）**：
1. **RowVersion 并发冲突**：两用户同时匹配同一流水/凭证，后提交者收到并发/占用错误（`E-A4-CONCURRENCY-001`/`E-A4-MATCH-002`/`E-A4-MATCH-005`）。
2. **Locked 快照**：`LockAsync` 实时重算并写入 `LockedStatementInternalDiff`/`LockedReconciledDiff`/`LockedBankAdjustedBalance`/`LockedBookAdjustedBalance`/`LockSnapshotJson`。
3. **外币调节表**：外币账户下 `GlBankEndingBalance` 用 `OrigAmount` 计算；本位币折算额不参与 `ReconciledDiff`；缺原币/币种不符的凭证行排除自动候选。
4. **Locked reversal**：已锁定对账的原 `JournalLine` 被 `ReverseAsync` 时拒绝，返回 `E-A4-RECON-LOCKED-REVERSAL`（SQLite 层）。
5. **GenerateBankOnlyVoucher 事务**：模拟过账成功后匹配步骤失败 → 整体回滚，不出现"已过账未匹配"孤儿凭证（SQLite 层）。
6. **反冲后重生成**：反冲旧 BankRecon 凭证后，允许重新生成新 BankRecon 凭证并写入**新** `GeneratedJournalEntryId`（不被幂等挡）。
7. **Confirm 导入失败**：Confirm 阶段出现致命解析失败 → 整批拒绝落库（`E-A4-IMPORT-001`），无部分落库。
8. **ImportProfile 字段方向**：`DepositAmountField`/`WithdrawalAmountField` 解析方向正确，不受银行 Debit/Credit 视角影响。

其他：历史未达项(上期凭证行)能进本期候选；已反转凭证行被排除；导入指纹去重(强重复跳过/疑似仅警告)；并发同凭证行重复匹配被 `UX_..._JL` 唯一约束/事务挡住（SQLite 层）。末尾 **gstack 端到端 QA**（建会话→导入 Preview/Confirm→自动撮合→人工 N:M→生成手续费凭证→调节表平→锁定→验证锁后过账/反冲被拒）。

---

## 16. 错误 / 警告码

| 码 | 含义 |
|---|---|
| E-A4-IMPORT-001 | 导入文件/模板解析失败（行级原因见报告） |
| E-A4-IMPORT-002 | 会话非 Open，禁止导入/改行 |
| E-A4-MATCH-001 | 匹配组 SignedAmount 不平 |
| E-A4-MATCH-002 | 凭证行已被其他匹配组占用 |
| E-A4-MATCH-003 | 跨账户/跨币种/方向不符，禁止匹配 |
| E-A4-MATCH-004 | 流水行不属同一会话 / 凭证行非本银行GL科目 |
| E-A4-MATCH-005 | 流水行已被其他匹配组占用 |
| E-A4-BANKONLY-DUP | 该流水行已生成 BankRecon 凭证（幂等拒绝） |
| E-A4-STATEMENT-LOCKED | 会话已锁定，禁止导入/改行/撮合/生成凭证/标记未达 |
| E-A4-RECON-001 | InternalDiff 或 ReconciledDiff ≠ 0，禁止锁定 |
| E-A4-RECON-002 | 会计期间已结账，禁止 Unlock |
| E-A4-RECON-LOCKED-POSTING | 该银行账户本期对账已锁定，禁止过账影响银行GL科目的凭证 |
| E-A4-RECON-LOCKED-REVERSAL | 被反冲凭证已完成锁定银行对账，必须先 Unlock 对账会话 |
| E-A4-CONCURRENCY-001 | 当前流水/凭证状态已变化，请刷新后重试（RowVersion 冲突） |
| W-A4-IMPORT-DUP | 疑似重复行（仅警告，不自动跳过） |
| W-A4-IMPORT-SKIP | 强重复行已跳过 |
| W-A4-CAND-NONE | 流水行无自动候选，转人工 |

---

## 17. 落地顺序建议（供 writing-plans）

- **A 数据模型 + 迁移**：5 实体 + `VoucherSource.BankRecon=7` + **`RowVersion`(4 实体)** + **Lock 快照字段** + **`DepositAmountField`/`WithdrawalAmountField` 命名** + **金额精度统一 decimal(18,2)** + 错误码枚举 / i18n key 预留。
- **B 导入**：Profile + 解析器 + Preview/Confirm（失败行不落库）+ 指纹去重。
- **C 撮合引擎**：SignedAmount(计算属性) + 候选(含历史未达/外币原币/反转排除) + Phase1/2(唯一解) + 人工 N:M + Unmatch。
- **D 单边项 + 调节表**：`GenerateBankOnlyVoucher` **单条事务 + 批量逐行执行 + 反冲后重生成(清空再写新 GeneratedJournalEntryId)** + 标记未达；调节表**双向公式 + 实时重算 + 外币原币口径**。
- **E 锁定 + 过账守卫**：Post/AutoPost 守卫 + **`ReverseAsync` 对已锁定原凭证的守卫** + Lock **实时重算并写快照** + Unlock(期间未结账)。
- **F**：操作级权限 + 审计日志 + API/控制器。
- **G 前端**：撮合台 + 会话 + 模板 + **并发冲突 UX(带 RowVersion+刷新重试)** + **锁定前调节表确认对话框** + 菜单/五语 i18n。
- **H 分层测试**：SQLite 覆盖唯一约束/并发/锁后过账/**锁定后反冲拒绝**/单边项事务回滚；InMemory 覆盖公式/撮合/状态机；AC-001~010 + 补充 8 用例；gstack 端到端。

> 关联：[[project_finance_module]]（GL/AP/AR/核销/AutoVoucherEngine 现状）、[[project_a2_process_routing]]（同 subagent-driven 落地范式）、[[project_module_taxonomy]]。源 brainstorming 决策 A4-D1~D5 + 用户两轮 review 全采纳。
