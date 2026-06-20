# A5 预算 / 管理会计（Budget & Management Accounting）设计 spec

> ERP 完整性路线 A5（最后一项 A 类缺口）。把"预算"做真：多维（科目×成本中心×成本对象×期间）年度预算编制 → 按月分解 → 多版本 + 接 OA 审批 → 预算 vs 实际实时对比报表 → 按预算行可选的过账控制（柔性预警 / 刚性硬拦截）。
>
> 源对话：brainstorming 定稿（A5-D1~D7 + §8 四个小决策全采纳推荐值）。日期 2026-06-20。命名空间 **Fin**。

---

## 0. 目标与范围

**题眼**：系统已有 `GlAccount`/`JournalEntry`/`JournalLine`(含 `CostCenterId`/`CostObjectType`/`CostObjectId` 维度占位)/`FiscalPeriod`/`CostCenter`(树) + 三大报表(`TrialBalance`/`BalanceSheet`/`IncomeStatement`) 全套 GL 基建，但**没有预算层**：不能编预算、不能比预算 vs 实际、不能按预算控制开支。A5 补齐"管理会计/经营管控"的最后一块。

**核心定位（账外 memorandum）**：预算**不产生任何凭证、不参与借贷恒等、不改 GL 过账逻辑**。A5 只做三件事：
1. **编预算**——多维度行（科目×成本中心(可空)×成本对象(可空)）× 按月（12 期）分解，多版本，接 OA 审批，Active 版本作唯一基准。
2. **预算 vs 实际报表**——实时读已过账 GL 实际（仿 `TrialBalanceService` 但加成本中心/成本对象维度聚合），对比 Active 版本预算，出差异/差异率/执行率，月/季/年钻取。
3. **可选过账控制**——按预算行配 `None/Warn/Block`：`Block` 复用 A4 `BankReconGuard` 静态守卫范式，挂 `JournalEntryService.PostAsync` 拦截手工过账超预算。

**与既有模块的正交关系**：A5 **只读** GL 实际数与 `FiscalPeriod`，**调用** OA 审批引擎（`IApprovalService`/`IApprovalCallback`），**挂钩** `PostAsync` 守卫。不改动凭证/核销/银行对账/折旧任何既有逻辑。**不新增 `VoucherSource`**（无凭证产物，风险低于 A3/A4）。

### 0.1 纳入 MVP

| 能力 | 说明 |
|---|---|
| 预算方案/版本 | 每财年一个方案（控制口径唯一）；方案下多版本；版本状态机 Draft→PendingApproval→Approved/Rejected→Archived；一个 Active 版本 |
| 多维行网格 | 科目×成本中心(可空)×成本对象(可空) 唯一桶；维度可留空=更粗粒度（公司级/科目级） |
| 按月分解 | 年度额分解到 12 期；分解方式 均摊 / 季节比例 / 手工逐月 |
| 录入/来源 | 网格手工编辑 + Excel 导入(Preview/Confirm) + 一键复制（上年实际 / 上一版本）作起点 |
| OA 审批 | 提交起 OA 流程；通过→Approved 并**自动 Activate**（清旧 Active，旧版 Archived）；驳回→Rejected 可改回 Draft |
| 预算 vs 实际报表 | 实时聚合已过账实际 vs Active 预算；维度 + 月/季/年钻取；差异/差异率/执行率 |
| 过账控制 | 按行 `None/Warn/Block` × 口径 `Ytd/Period`；`Block` 守卫硬拦手工 `PostAsync` 超预算 |
| 工程护栏 | 操作级权限 + 审计日志 + RowVersion 乐观并发 + 五语 i18n + 分层测试(InMemory+SQLite) + gstack QA |

### 0.2 非范围（推迟）

- **滚动预算**（持续滚动 N 月）——本期按年度+按月分解，滚动留后续。
- **承诺占用 / encumbrance**（PO/PR 未达占预算）——控制口径仅算已过账实际；占用需接采购，推迟。
- **从 MRP / 销售预测自动引入预算**——跨模块耦合重，推迟。
- **资本支出预算 / 全科目预算**——范围限损益类（Type∈{Expense,Revenue}）；资产/负债/资本支出预算推迟。
- **多场景预算**（乐观/悲观/滚动并存）——每财年一个方案，多场景推迟。
- **`AutoPostAsync` 硬拦截**——自动凭证（AP/折旧/期末结转等）不被预算卡死，仅在报表反映超支；硬控制只作用手工 `PostAsync`（决策 §8-2）。
- **预算预警自动凭证 / 内部拨款**——预算账外，无凭证产物。
- **预算调整审批流的差异留痕版本 diff 可视化**——版本对比报表留后续。

---

## 1. 现状与依赖（落码前必读，均已存在）

| 资产 | 位置 | A5 用法 |
|---|---|---|
| `GlAccount`(Fin_GlAccount) | `CP6.Entity/DomainModels/Fin/GlAccount.cs` | `Type`(Asset=1/Liability=2/Equity=3/Revenue=4/**Expense=5**)、`NormalSide`(Debit=1/Credit=2)、`IsLeaf`(仅末级可记账)、`Role`。预算行科目限 **末级 + Type∈{Expense,Revenue}** |
| `JournalEntry` | `Fin/JournalEntry.cs` | `VoucherDate`/`PeriodId`/`Source`/**`Status`(Posted=2)**。报表实际侧 + 控制守卫只认 `Posted` |
| `JournalLine` | `Fin/JournalLine.cs` | `AccountId`/`Debit`/`Credit`/**`CostCenterId`**/**`CostObjectType`**/**`CostObjectId`**/`CurrencyCd`/`OrigAmount`。A5 维度聚合与控制匹配的来源 |
| `JournalEntryService` | `CP6.Core/Services/Fin/JournalEntryService.cs` | `PostAsync(entryId, checkerId)`(L72)、`AutoPostAsync(entry)`(L95)、`ReverseAsync`(L139)。**`BudgetGuard` 挂 `PostAsync`**（紧接 `BankReconGuard.CheckPostingAsync` 之后，§7） |
| `BankReconGuard` | `CP6.Core/Services/Fin/BankReconGuard.cs` | A4 静态守卫范式（同 `CP6Context` 直查、无 DI、无循环依赖、返 `FinResult`）——`BudgetGuard` 照此实现 |
| `IFiscalPeriodService` | `CP6.Core/Services/Fin/IFiscalPeriodService.cs` | `ComputeFiscal(year,month)→(FiscalYear,PeriodNo)`、`ResolveAsync(date)`、`IsOpenAsync(periodId)`、`ListAsync(year?)`。期间/财年解析 |
| `FiscalPeriod`(Fin_FiscalPeriod) | `Fin/FiscalPeriod.cs` | `FiscalYear`/`Year`/`Month`/`PeriodNo`(1..12)/`Status`(Open=0/Closed=1)。预算财年 + 控制落期对齐 |
| `CostCenter`(Fin_CostCenter) | `Fin/CostCenter.cs` | `Code`/`Name`/`Type`(Dept=1/Process=2/Machine=3)/`ParentId`/`LinkMachineId`/`IsActive`。维度下拉来源（无 Service，A5 直读或补只读查询） |
| `TrialBalanceService` | `CP6.Core/Services/Fin/TrialBalanceService.cs` | LINQ join `JournalLines`×`JournalEntries` where `Status=Posted` group by AccountId 的实际聚合范式（L29-42）——`BudgetVsActualService` 照此加维度 |
| `IncomeStatementService` | `CP6.Core/Services/Fin/IncomeStatementService.cs` | 按 `Type`(Revenue/Expense) + `Role`(COGS) 分类范式。预算范围筛科目同口径 |
| `IApprovalService` | `CP6.Core/Services/Wf/IApprovalService.cs` | `SubmitAsync(bizType, bizId, starterId, formSnapshot)→Guid`(L13)。预算版本提交审批 |
| `IApprovalCallback` | `CP6.Core/Services/Wf/IApprovalCallback.cs` | `BizType` + `OnApprovedAsync(ctx)`/`OnRejectedAsync(ctx)`；`ApprovalCallbackContext{BizType,BizId,InstanceId,StarterId,DecidedById,Reason}`。**新增 `BudgetApprovalCallback`** |
| `ApprovalDispatcher` | `CP6.Core/Services/Wf/ApprovalDispatcher.cs` | OA 终态回调分发（与引擎共享 DbContext，原子，回调抛异常则审批+业务一并不落库） |
| `Wf_FlowDef`/`Wf_ApprovalBinding` | `CP6.Entity/DomainModels/Wf/` | 流程模板 + 业务类型绑定。A5 seed `FlowKey="budget-approve"` + `BizType="A5_Budget"` |
| `Wf_FlowInstance` | `Wf/Wf_FlowInstance.cs` | `BizType`/`BizId`/`Status`(Running=0/Approved=1/Rejected=2…)。审批痕迹回查 |
| `JournalApprovalCallback` | `CP6.Core/Services/Fin/JournalApprovalCallback.cs` | 财务凭证接 OA 的**现成先例**——`BudgetApprovalCallback` 照此写 |
| `FinSequenceService` | `CP6.Core/Services/Fin/` | `NextAsync(key,date)` 采番。方案号 `BUD-{FY}-nnnn` |
| `FinResult` | `Core/Services/Fin/FinResult.cs` | `{Ok,Code,Args}`+`Pass()`/`Fail(code,args)`。A5 服务统一返回 |
| Fin 控制器范式 | `WebApi/Controllers/Fin/GlAccountController.cs` 等 | `[Authorize]`+`[RequirePermission(menu,action)]`，`Ok2(data)`/`Fin(FinResult)` |
| Fin 菜单 600 组 | `WebApi/Program.cs` | 600~620 已用（Fin 600-613、A4 614/619-620、A3 615-618），**621 起空位**给预算 |
| 多租户基类 | `BaseTenantEntity`(`CP6.Entity/BaseTenantEntity.cs`) | =`BaseEntity`(Id/审计)+`TenantId`；**不含 RowVersion/IsDeleted**（含二者的是 `BaseBizEntity`）。A5 实体继承 `BaseTenantEntity`，编辑核心实体显式加 `RowVersion`（§14.3）；唯一索引声明后自动补 `TenantId` 前缀（沿用 A2/A3/A4 机制） |
| i18n Seed 范式 | `WebApi/Seed/I18nA3ScreenSeed.cs` / `I18nBankReconScreenSeed.cs` | `I18n*ScreenSeed.Items` + `Program.cs .Concat` + 幂等插入。新增 `I18nA5BudgetScreenSeed` |
| 审计日志 | `Sys_OperLog` | A5 关键操作写审计（§14.4） |
| Excel 库 | `ClosedXML 0.105`（A4 已装） | 预算 Excel 导入复用 |

---

## 2. 术语

| 术语 | 含义 |
|---|---|
| **预算方案 Budget** | 一个财年的预算容器，每财年唯一。挂多个版本。 |
| **预算版本 BudgetVersion** | 方案下的一次编制/修订（初稿/年中调整…）。有独立状态机；至多一个 `IsActive`。 |
| **Active 版本** | 当前生效的版本，**控制与报表的唯一基准**。OA 通过即自动激活。 |
| **预算行 BudgetLine** | 一个维度桶 = 科目 × 成本中心(可空) × 成本对象(可空)；一版一桶唯一。 |
| **按月分解 BudgetLinePeriod** | 预算行在 12 个财年期号上的分摊额。 |
| **维度桶 / Dimension bucket** | (AccountId, CostCenterId?, CostObjectType?, CostObjectId?) 元组。NULL 维度 = 该维度通配（更粗粒度）。 |
| **控制模式 ControlMode** | `None`(不控)/`Warn`(预警不拦)/`Block`(硬拦)。版本级默认，行可覆盖。 |
| **控制口径 ControlBasis** | `Ytd`(期初至本期累计) / `Period`(仅本期)。版本级默认，行可覆盖。 |
| **最具体匹配 most-specific match** | 一条凭证行可能命中多个通配程度不同的预算桶，取非空维度最多者作控制依据。 |
| **执行率 / 差异率** | 执行率=实际/预算；差异=预算−实际；差异率=差异/预算。 |

---

## 3. 数据模型

4 个新实体，均继承 `BaseTenantEntity`，表前缀 `Fin_`。BudgetVersion / BudgetLine 显式加 `RowVersion`（编辑/并发核心）。

### 3.1 `Budget`（预算方案）`Fin_Budget`

| 字段 | 类型 | 说明 |
|---|---|---|
| `No` | string(30) | 方案号 `BUD-{FiscalYear}-nnnn`（`FinSequenceService.NextAsync`） |
| `Name` | string(100) | 如 "2027 年度预算" |
| `FiscalYear` | int | 财年（对齐 `FiscalPeriod.FiscalYear`） |
| `Scope` | enum `BudgetScope` | `PnL=1`(损益：费用+收入)。预留扩展，MVP 仅 PnL |
| `Description` | string(500)? | 备注 |
| `IsActive` | bool | 方案启用（停用≠删除，财务惯例） |

唯一索引：`UX_Fin_Budget_FiscalYear` = (FiscalYear)（自动补 TenantId 前缀）——**每财年一个预算方案**（控制口径唯一，MVP 不做多场景）。

### 3.2 `BudgetVersion`（预算版本）`Fin_BudgetVersion`

| 字段 | 类型 | 说明 |
|---|---|---|
| `BudgetId` | Guid | → Budget |
| `VersionNo` | int | 版本序号（方案内自增 1,2,3…） |
| `Name` | string(100) | 如 "初稿" / "年中调整" |
| `Status` | enum `BudgetVersionStatus` | Draft=0 / PendingApproval=1 / Approved=2 / Rejected=3 / Archived=4 |
| `IsActive` | bool | **唯一基准**：控制 + 报表只认 Active 版本 |
| `DefaultControlMode` | enum `BudgetControlMode` | None=0 / Warn=1 / Block=2（版本级默认，行可覆盖） |
| `DefaultControlBasis` | enum `BudgetControlBasis` | Ytd=0 / Period=1（版本级默认，行可覆盖） |
| `ApprovalInstanceId` | Guid? | → `Wf_FlowInstance`（审批痕迹） |
| `ApprovalRef` | string(50)? | 审批引用号（展示） |
| `SubmittedAt`/`SubmittedBy` | DateTime?/string? | 提交审批审计 |
| `ApprovedAt`/`ApprovedBy` | DateTime?/string? | 通过/激活审计 |
| `RejectReason` | string(500)? | 驳回原因（来自 OA 回调 ctx.Reason） |
| `RowVersion` | byte[]? | 乐观并发（§14.3） |

约束（服务层强校验）：
- 同一 Budget 下**至多一个 `IsActive=true`**（激活时清旧）。
- 仅 `Approved` 版本可 Activate；OA 通过即自动 Activate（决策 §8-1）。
- 仅 `Draft` 版本可编辑行 / 提交审批。

唯一索引：`UX_Fin_BudgetVersion_BudgetNo` = (BudgetId, VersionNo)。

### 3.3 `BudgetLine`（预算行 / 维度桶）`Fin_BudgetLine`

| 字段 | 类型 | 说明 |
|---|---|---|
| `VersionId` | Guid | → BudgetVersion |
| `AccountId` | Guid | → GlAccount（**末级 + Type∈{Expense,Revenue}**，服务层校验 §5.2） |
| `CostCenterId` | Guid? | **真实业务维度**，可空 = 公司级（不分成本中心）；可建 FK → Fin_CostCenter |
| `CostObjectType` | string(20)? | **真实业务字段**，可空（"WorkOrder"/"Order"… 对齐 JournalLine.CostObjectType） |
| `CostObjectId` | string(50)? | **真实业务字段**，可空（CostObjectType 非空时必填） |
| `CostCenterKey` | Guid (not null) | **唯一索引专用规范化键**：CostCenterId 为空→`Guid.Empty`，否则=CostCenterId。**不建 FK** |
| `CostObjectTypeKey` | string(20) (not null) | **唯一索引专用**：CostObjectType 为空→`""`，否则=CostObjectType |
| `CostObjectIdKey` | string(50) (not null) | **唯一索引专用**：CostObjectId 为空→`""`，否则=CostObjectId |
| `AnnualAmount` | decimal(18,2) | 年度额 = Σ 12 期（服务层维护与 BudgetLinePeriod 一致，§5.3） |
| `ControlMode` | enum `BudgetControlMode`? | null = 继承版本 `DefaultControlMode` |
| `ControlBasis` | enum `BudgetControlBasis`? | null = 继承版本 `DefaultControlBasis` |
| `Memo` | string(500)? | 备注 |
| `RowVersion` | byte[]? | 乐观并发（§14.3） |

唯一索引：`UX_Fin_BudgetLine_Dim` = (VersionId, AccountId, **CostCenterKey, CostObjectTypeKey, CostObjectIdKey**)——**一版一桶唯一**。

> **NULL 维度唯一性落码方案（保 FK，不写假记录）**：SQL Server 唯一索引视多个 NULL 为相异，会放过重复的 (Account, NULL, NULL, NULL)。**不能**直接把 `Guid.Empty` 写进 `CostCenterId`——若 `CostCenterId` 建 FK 到 `Fin_CostCenter`，`Guid.Empty` 会违反外键（除非建假 CostCenter，禁用此法）。落码采用**真实字段 + 规范化键分离**：`CostCenterId`/`CostObjectType`/`CostObjectId` 保持可空，承载真实业务关系与 FK；另加 `CostCenterKey`(Guid)/`CostObjectTypeKey`(string)/`CostObjectIdKey`(string) **三个 not-null 规范化键仅供唯一索引**，服务层在保存时由真实字段派生（空→`Guid.Empty`/`""`）。唯一索引只用 Key 字段，既解决 NULL 唯一性、又不破坏 FK。Key 字段对前端/业务不可见（纯持久化派生列）。

### 3.4 `BudgetLinePeriod`（预算行按月分解）`Fin_BudgetLinePeriod`

| 字段 | 类型 | 说明 |
|---|---|---|
| `BudgetLineId` | Guid | → BudgetLine |
| `PeriodNo` | int | 财年内期号 1..12 |
| `Amount` | decimal(18,2) | 该期预算额 |

唯一索引：`UX_Fin_BudgetLinePeriod_LinePeriod` = (BudgetLineId, PeriodNo)。

> 前端网格一行 = 一个维度桶 + 12 个月单元格 + 合计列；后端持久化 = 1 条 BudgetLine + 12 条 BudgetLinePeriod。`AnnualAmount` 为冗余合计（服务层保证 = Σ Amount）。

### 3.5 枚举汇总

```
BudgetScope          : PnL=1
BudgetVersionStatus  : Draft=0, PendingApproval=1, Approved=2, Rejected=3, Archived=4
BudgetControlMode    : None=0, Warn=1, Block=2
BudgetControlBasis   : Ytd=0, Period=1
```

> **不新增 `VoucherSource`**——A5 无凭证产物。

---

## 4. 状态机（BudgetVersion）

```
                    SubmitForApproval (仅 Draft)
   ┌──────────┐   ───────────────────────────►   ┌─────────────────┐
   │  Draft   │                                    │ PendingApproval │
   │ (可编辑) │   ◄───────────────────────────    │  (锁定/审批中)  │
   └──────────┘        OA 驳回回调 OnRejected       └────────┬────────┘
        ▲              → Rejected → 用户改回 Draft           │ OA 通过回调
        │                                                    │ OnApproved
        │ (Rejected 版本重新编辑)                            ▼
        │                                            ┌─────────────┐
   ┌──────────┐                                      │  Approved   │
   │ Rejected │ ◄────────────────────────────────── │             │
   └──────────┘                                      └──────┬──────┘
                                                            │ 自动 Activate
                                                            │ (清旧 Active → 旧版 Archived)
                                                            ▼
                                                     IsActive = true
                                                  (控制 + 报表 唯一基准)
```

**状态转移规则**：

| 当前 | 动作 | 目标 | 守卫 |
|---|---|---|---|
| Draft | 编辑行 / 删行 / 导入 / 复制 | Draft | 仅 Draft 可改（否则 E-A5-VERSION-005） |
| Draft | SubmitForApproval | PendingApproval | 仅 Draft（E-A5-VERSION-002）；起 OA 流程，同 (bizType,bizId) 进行中则拒（E-A5-VERSION-003） |
| PendingApproval | OA 通过 OnApprovedAsync | Approved → 自动 Activate | 幂等（已 Approved/Active 直接返回） |
| PendingApproval | OA 驳回 OnRejectedAsync | Rejected | 幂等（非 PendingApproval 直接返回） |
| Approved | (自动激活在 OnApproved 内一并完成) | IsActive=true | 至多一个 Active，清旧 Active 并 Archived |
| Rejected | 改回 Draft（用户编辑触发） | Draft | 仅 Rejected 可回退 |
| (任意 Active 版被新版替换) | 新版激活 | 旧版 Archived | — |

**编辑锁**：`PendingApproval` / `Approved` / `Archived` 版本**不可改行**；要调整已 Active 预算 → 在方案下**新建版本（复制 Active）** 再走审批。

---

## 5. 业务规则

### 5.1 方案与版本

- 方案唯一财年：建方案校验 `FiscalYear` 未被占用（E-A5-BUDGET-001）。
- 新建版本：方案下 `VersionNo` 自增；首版默认 `DefaultControlMode=None`/`DefaultControlBasis=Ytd`（纯分析起步）。
- 复制建版：`CopyFrom`（源=上年实际 / 同方案某版本 / 他年方案版本），把源维度桶 + 按月数复制为新 Draft 版本（§5.5）。

### 5.2 预算行维度与科目校验

- 科目必须 **`IsLeaf=true`** 且 **`Type∈{Expense,Revenue}`**（E-A5-LINE-002）。
- `CostObjectType` 非空时 `CostObjectId` 必填（反之亦然），成对（E-A5-LINE-004）。
- 维度桶唯一：同版本不可重复 (Account, CostCenter, CostObjectType, CostObjectId)（E-A5-LINE-001）。
- 维度可全留空 → 退化为"科目级公司预算"。混合粒度允许（同科目可同时有公司级桶与成本中心级桶；控制按"最具体匹配"§7.3 解析）。

### 5.3 按月分解

- 录入方式三选一（前端辅助，后端只收 12 期数）：
  - **均摊**：`AnnualAmount / 12`，余数进最后一期（分到分位）。
  - **季节比例**：给 12 个权重 → 按权重分摊。
  - **手工逐月**：直接填 12 格。
- 一致性：`AnnualAmount` 必须 = Σ BudgetLinePeriod.Amount（服务层保存时校验或自动回填年度额，E-A5-LINE-003）。落码取**自动回填**：保存按月数后服务端重算 `AnnualAmount = ΣAmount`，避免前端不一致。
- 期号范围 1..12（E-A5-LINE-004）。允许某期为 0 或负（冲减预算）。

### 5.4 Excel 导入（Preview / Confirm）

- 列映射：科目编码 / 成本中心编码(可空) / 成本对象类型(可空) / 成本对象号(可空) / M1..M12（或 年度额+分解方式）。
- 流程仿 A4：`PreviewAsync`(dryRun，校验科目存在/末级/损益类、成本中心存在、维度桶组内不重复、数值合法，返回逐行结果 + 致命错误标记) → `ConfirmAsync`(无致命错误才整批落库，否则整批拒绝 E-A5-IMPORT-001)。
- 仅 Draft 版本可导入。导入为**整版替换或追加**二选一（MVP 取**追加+按桶 upsert**：同桶覆盖，新桶新增）。

### 5.5 复制（建版起点）

- `CopyFromVersionAsync(targetVersionId, sourceVersionId)`：源版本维度桶 + 按月数 → 目标 Draft 版本（按桶 upsert）。
- `CopyFromActualAsync(targetVersionId, sourceFiscalYear)`：把 `sourceFiscalYear` 的**已过账实际**按维度桶聚合（仿 §9 实际侧聚合）→ 目标版本的按月数（实际发生额作下年预算起点）。源为空则 E-A5-COPY-001。
- 仅 Draft 目标可被复制写入。

### 5.6 控制配置

- 版本级 `DefaultControlMode`/`DefaultControlBasis` 设默认；行级 `ControlMode`/`ControlBasis` 为 null 时继承版本默认，非 null 时覆盖。
- 控制只在版本 **Active** 后对过账生效（Draft/审批中版本不影响过账）。
- `Block` 仅作用 `PostAsync`（手工过账）；`AutoPostAsync`（自动凭证）不受硬拦，仅报表反映（决策 §8-2）。

---

## 6. 核心流程

### 6.1 编制 → 审批 → 激活

```
1. 建方案 Budget(FiscalYear) ── 唯一财年校验
2. 建版本 BudgetVersion(Draft) ── 可空白起 / 复制上年实际 / 复制上版本
3. 编预算行 BudgetLine + 12×BudgetLinePeriod ── 网格/Excel导入；配 ControlMode/Basis
4. 提交审批 SubmitForApproval
      → IApprovalService.SubmitAsync("A5_Budget", versionId, userId, {fiscalYear,total})
      → Status=PendingApproval, 存 ApprovalInstanceId/ApprovalRef
5. OA 流转（待办人审批）
6a. 通过 → ApprovalDispatcher → BudgetApprovalCallback.OnApprovedAsync
      → Status=Approved → 自动 Activate（清旧 Active=false 且 Archived，本版 IsActive=true）
6b. 驳回 → BudgetApprovalCallback.OnRejectedAsync
      → Status=Rejected, RejectReason=ctx.Reason → 用户可改回 Draft 重编
```

### 6.2 过账控制（运行期）

```
手工过账 JournalEntryService.PostAsync(entryId, checkerId)
  → maker-checker 校验(E-FIN-111) / 锁期校验(E-FIN-112)
  → BankReconGuard.CheckPostingAsync(...)        [A4 既有]
  → BudgetGuard.CheckPostingAsync(_db, entry)    [A5 新增，§7]
        命中 Active 版本 Block 行 + 超 YTD/期预算 → Fail(E-A5-BUDGET-EXCEEDED)
  → ValidateAsync(借贷恒等) → 置 Posted
```

### 6.3 预算 vs 实际分析（报表）

```
BudgetVsActualService.BuildAsync(fiscalYear, versionId?=Active, periodFrom, periodTo, dim 筛选)
  预算侧：Active 版本 BudgetLine+Period 按桶/期聚合
  实际侧：已过账 JournalLine 按 (Account,CostCenter,CostObject) + 期 聚合（§9）
  → 行 = 维度 + 预算/实际/差异/差异率/执行率 + 12 期钻取列
```

---

## 7. BudgetGuard 过账控制逻辑（核心）

**范式**：静态类 `BudgetGuard`（无 DI、同 `CP6Context` 直查、无循环依赖、返 `FinResult`），完全照 `BankReconGuard`。挂 `JournalEntryService.PostAsync`（紧接 `BankReconGuard.CheckPostingAsync` 之后）。**不挂** `AutoPostAsync`（决策 §8-2）、**不挂** `ReverseAsync`（红冲释放预算）。

### 7.1 签名与短路

```csharp
public static async Task<FinResult> CheckPostingAsync(CP6Context db, JournalEntry entry)
```

短路顺序（任一不满足即 `Pass()`，对正常过账透明、爆炸半径=零）：
1. 解析 entry 落期（**与 §9 报表实际侧口径统一**）：**优先**用 `entry.PeriodId` 关联 `FiscalPeriod` 取 `FiscalYear` + `PeriodNo`；`PeriodId` 为空时 **fallback** 到 `IFiscalPeriodService.ResolveAsync(entry.VoucherDate)`。**不直接按 `VoucherDate.Year/Month` 算**——避免非自然财年 / 跨年财期 / 13 期 / 调整期出错。仍解析不到期间 → Pass。
2. 找该 `FiscalYear` 的 **Active** `BudgetVersion`（join Budget.FiscalYear + Version.IsActive）。无 Active → Pass（无控制）。
3. 取该版本所有 `ControlMode` 有效解析为 **Block** 的预算行（行覆盖否则版本默认）。无 Block 行 → Pass。
4. 否则进入逐行匹配校验。

### 7.2 逐行匹配与判定

对 entry 中每条 `JournalLine`：
1. 过滤：科目 `Type∈{Expense,Revenue}`（join GlAccount）。非损益类跳过。
2. 算本行发生额（带方向，统一为"预算消耗"口径）：
   - 费用类（Type=Expense）：消耗 = `Debit − Credit`（借增费用=正消耗）。
   - 收入类（Type=Revenue）：消耗 = `Credit − Debit`（贷增收入=正"完成"）；MVP 控制**仅对费用**做硬拦（收入只在报表比对，不拦过账）。即 §7 Block 仅作用 Expense 行。
3. **最具体匹配**（§7.3）找控制桶；无匹配 Block 桶 → 该行放行。
4. 命中 Block 桶 → 按 `ControlBasis` 取口径算"已用 + 本次"vs"预算额"：
   - **Ytd**：`已过账实际(桶, 期1..PeriodNo) + 本行消耗` vs `Σ预算(桶, 期1..PeriodNo)`。
   - **Period**：`已过账实际(桶, 本期) + 本行消耗` vs `预算(桶, 本期)`。
   - 已过账实际 = 仿 §9 聚合（同桶维度、同 FiscalYear、`Status=Posted`、期号∈区间）。
5. 若 `已用 + 本次 > 预算` → `Fail("E-A5-BUDGET-EXCEEDED", 科目Code, 预算额, 已用, 本次, 超出额)`。
6. 全部行通过 → `Pass()`。

> **同一 entry 多行命中同桶**：先把本 entry 内命中同桶的消耗合并再比（避免一张凭证内拆行绕过控制）。

### 7.3 最具体匹配（most-specific match）

一条凭证行的维度 = (Account, CostCenter?, CostObjectType?, CostObjectId?)。候选预算桶 = 同版本、同 Account、且每个**非空预算维度**都等于凭证行对应维度（预算维度为"未指定"哨兵 = 通配，匹配任意）。在候选中取**非空维度数最多**者（最具体）作控制依据；同具体度多桶并存属配置错误，取唯一或报 W-A5 警告并选其一（落码取：按 CostCenter→CostObject 顺序优先，确定性选择）。

> 例：版本里同时有
> - 桶A (科目 6602差旅, CC=null, CO=null) Block 年 100,000
> - 桶B (科目 6602差旅, CC=销售部, CO=null) Block 年 30,000
>
> 销售部一张差旅费凭证 → 命中桶B（更具体），按销售部 30,000 控制；非销售部的差旅 → 命中桶A，按公司 100,000 控制。

### 7.4 Warn 与 None

- **Warn**：不在 `PostAsync` 硬路径拦截（过账是事务性接受/拒绝，软警告不阻断）。Warn 超支在 §9 报表标红，且提供可选 `IBudgetCheckService.PreCheckAsync(entry)` 端点供凭证 UI **提交前**调用，返回 `List<预警>`（科目/桶/已用/预算/超出）。
- **None**：完全不参与控制。

### 7.5 边界与性能

- 守卫只读、`AsNoTracking`、按 entry 涉及的 AccountId 集合收窄查询（不全表扫描），仿 BankReconGuard。
- 外币：凭证行 `Debit/Credit` 已是本位币（`OrigAmount` 为原币展示），预算以本位币编制，直接本位币比对。
- 锁期/红冲/草稿过账无关预算（仅 `PostAsync` 成功路径）。

---

## 8. OA 审批接入逻辑（核心）

**耦合方式**：预算版本服务**直接依赖** `CP6.Core.Services.Wf.IApprovalService`（同程序集、OA 已落地，照 `JournalApprovalCallback` 先例，**不用桩**）。

### 8.1 提交审批

`BudgetVersionService.SubmitForApprovalAsync(versionId, userId)`：
1. 守卫：版本 `Status=Draft`（否则 E-A5-VERSION-002）；版本至少 1 行预算（否则 E-A5-VERSION-006）。
2. 调 `IApprovalService.SubmitAsync(bizType:"A5_Budget", bizId: versionId.ToString(), starterId: userId, formSnapshot: new { fiscalYear, versionNo, totalAmount })`。
   - OA 内部按 `bizType` 查 `Wf_ApprovalBinding` → `FlowKey` → `Wf_FlowDef` 起流程；同 (bizType,bizId) 已有 Running 实例 → OA 抛异常，A5 捕获包装 E-A5-VERSION-003。
3. 置 `Status=PendingApproval`、`ApprovalInstanceId=返回 Guid`、`SubmittedAt/By`。`formSnapshot` 存入 `Wf_FlowInstance.VarsJson`，供审批流条件取值（如金额分级）。

### 8.2 终态回调

新增 `BudgetApprovalCallback : IApprovalCallback`（`CP6.Core/Services/Fin/BudgetApprovalCallback.cs`），`BizType => "A5_Budget"`：

```csharp
// 与引擎共享 DbContext，在引擎最终 SaveChanges 前调用；抛异常则审批+业务一并回滚（原子）。
// 回调只标脏不自己 SaveChanges——由引擎统一持久化（同一事务）。
public async Task OnApprovedAsync(ApprovalCallbackContext ctx)
{
    var versionId = Guid.Parse(ctx.BizId);
    // 一次性完成 Status=Approved + 清旧 Active→Archived + 本版 IsActive=true（§8.3）
    await _budget.ActivateFromApprovalAsync(versionId, ctx.DecidedById?.ToString() ?? "OA");
}

public async Task OnRejectedAsync(ApprovalCallbackContext ctx)
{
    var versionId = Guid.Parse(ctx.BizId);
    var v = await _budget.GetVersionAsync(versionId);
    if (v?.Status != PendingApproval) return;                   // 幂等
    v.Status = Rejected; v.RejectReason = ctx.Reason ?? "审批驳回";
}
```

注册：`Program.cs` `builder.Services.AddScoped<IApprovalCallback, BudgetApprovalCallback>();`（与 `JournalApprovalCallback` 并列，多回调按 `BizType` 分发）。

> **事务边界（关键）**：`BudgetApprovalCallback` 与 OA 引擎**共享同一个 scoped `CP6Context`**，回调在引擎最终 `SaveChangesAsync` **之前**被调用。因此 `ActivateFromApprovalAsync` 必须在**同一 DbContext / 同一事务内**一次性完成「置 Approved + 清旧 Active→Archived + 本版 IsActive=true」，且**全程基于已加载的实体引用操作、不中途重查刚改未存盘的状态**（否则会读不到未 `SaveChanges` 的 `Approved`，导致状态判断不一致）。回调内**不自行 `SaveChanges`**——交引擎统一持久化，保证审批终态与预算激活的原子性。

### 8.3 自动激活（决策 §8-1）

两个入口，逻辑同源、事务边界不同：

**① `ActivateFromApprovalAsync(versionId, decidedBy)`（OA 回调专用，§8.2 内调）**——在与引擎共享的 DbContext 内一次性完成，不自行 SaveChanges：
1. 加载本版本实体；守卫 `Status=PendingApproval`（幂等：已 Approved/IsActive 直接返回）。
2. 置 `Status=Approved`、`ApprovedAt=now`、`ApprovedBy=decidedBy`。
3. 同 Budget 下其它 `IsActive=true` 版本（同一查询/同 DbContext 加载）→ 置 `IsActive=false`、`Status=Archived`。
4. 本版本 `IsActive=true`。
5. 全部基于已加载实体引用改值，**不重查刚改未存盘的状态**；引擎随后统一 `SaveChanges`（与审批终态同事务原子落库）。

**② `ActivateAsync(versionId)`（独立端点，手动补救/异常处置用）**——独立事务：
1. 守卫：版本 `Status=Approved`（否则 E-A5-VERSION-004）。
2. 同 Budget 下其它 `IsActive=true` 版本 → `IsActive=false`、`Status=Archived`。
3. 本版本 `IsActive=true`，自行 `SaveChanges`。

自此该 FiscalYear 的控制 + 报表基准切到本版本。两入口都保证「同 Budget 至多一个 Active」。

### 8.4 流程定义 Seed

启动 seed（幂等，仿 A3/A4 seed）：
- `Wf_FlowDef`：`FlowKey="budget-approve"`、`FlowName="预算审批"`、`FormKey="BudgetApproval"`、`SchemaJson`=单审批人(admin)默认流程、`Enable=true`。
- `Wf_ApprovalBinding`：`BizType="A5_Budget"`、`FlowKey="budget-approve"`、`Enable=true`。
- 真实多级/条件审批流由管理员在 OA 设计器改 `Wf_FlowDef` 配置（A5 不写死）。

### 8.5 审批中可见性/锁定

- `PendingApproval` 版本：业务侧锁定，**不可改行 / 不可重复提交**；可查、可撤回（若 OA 支持 Withdraw → 版本回 Draft，MVP 可不做撤回）。
- `Approved`/`Archived`：不可改。`Rejected`：可改回 Draft 重编。

---

## 9. 预算 vs 实际报表口径（核心）

`BudgetVsActualService.BuildAsync(fiscalYear, versionId?, periodFrom=1, periodTo=12, filter)`：

### 9.1 预算侧

- 版本 = 指定 `versionId`，缺省取该 `fiscalYear` 的 **Active** 版本（无 Active → 预算列为 0/空，仅出实际）。
- 聚合 `BudgetLine` join `BudgetLinePeriod`，where `PeriodNo∈[periodFrom,periodTo]`，按维度桶 (Account, CostCenter, CostObjectType, CostObjectId) 汇总 `Σ Amount` = 区间预算；保留 12 期明细供钻取。

### 9.2 实际侧（仿 TrialBalanceService + 维度）

```
from l in JournalLines
join e in JournalEntries on l.EntryId == e.Id
join a in GlAccounts on l.AccountId == a.Id
where e.Status == Posted
  && a.Type in (Revenue, Expense)
  && e 落 fiscalYear 的 PeriodNo ∈ [periodFrom, periodTo]   // 按 e.PeriodId→FiscalPeriod 的 FiscalYear/PeriodNo 过滤
group by (l.AccountId, l.CostCenterId, l.CostObjectType, l.CostObjectId, periodNo)
select 净额：
   Expense → Σ(Debit − Credit)
   Revenue → Σ(Credit − Debit)
```

> 这是现有三表**没有**做的成本中心/成本对象维度聚合——A5 首次按 `JournalLine` 的维度列分组。期间过滤通过 `JournalEntry.PeriodId` 关联 `FiscalPeriod` 取 `FiscalYear`+`PeriodNo`（与预算财年口径一致）。

### 9.3 对齐与差异

- 维度对齐：实际按 (Account,CC,CO) 桶 vs 预算同桶。**实际可能比预算细**（预算在公司级、实际带成本中心）——报表提供两种视图：
  - **按预算桶视图**：实际按预算桶的粒度上卷（实际更细的维度归并到最具体匹配的预算桶，仿 §7.3），逐桶比预算。
  - **按实际维度视图**：实际全维度展开，预算按通配下分（公司级预算不分摊到成本中心，预算列在明细行留空、仅在小计行显示）。MVP 先做**按预算桶视图**（与控制口径一致），实际维度视图作钻取下钻。
- **未编预算实际（Unbudgeted Actual）——不可静默丢弃**：实际发生（已过账损益类）若**找不到任何匹配预算桶**（含通配），**不得**因"没有预算行"而从报表消失。这类实际单列到 **"未编预算实际 / Unbudgeted Actual"** 分组：按 (Account,CC,CO) 维度展示，`预算=0`、`实际=发生额`、`差异=−实际`、执行率标记为 "∞/无预算"。用途：暴露**漏编预算**或**异常费用**。该分组计入报表合计（总实际含未编预算部分）。
- 每行：`预算` / `实际` / `差异=预算−实际` / `差异率=差异/预算` / `执行率=实际/预算`；费用超支（实际>预算）标红，收入未达标（实际<预算）标黄，未编预算实际标橙（提示漏编/异常）。
- 期间钻取：12 期列 + 季度(Q1-Q4)小计 + 全年合计。
- 实时、无快照（与三表一致，永远与总账同源）。

---

## 10. API

控制器 3 个，路由前缀 `api/fin/budget*`，`[Authorize]` + 操作级 `[RequirePermission("fin-budget", action)]`。返回 `Ok2(data)` / `Fin(FinResult)`。

### 10.1 `BudgetController`（`api/fin/budget`）— 方案 + 版本

| 方法 | 路由 | 权限 action | 说明 |
|---|---|---|---|
| GET | `/` | view | 方案列表（按财年） |
| POST | `/` | add | 建方案（唯一财年） |
| PUT | `/{id}` | edit | 改方案名/备注 |
| POST | `/{id}/deactivate` | edit | 停用方案 |
| GET | `/{id}/versions` | view | 方案下版本列表 |
| POST | `/{id}/versions` | add | 建版本（空白 / copyFromVersion / copyFromActual 参数） |
| PUT | `/versions/{vid}` | edit | 改版本名/控制默认值（仅 Draft） |
| POST | `/versions/{vid}/submit` | submit | 提交审批（§8.1） |
| POST | `/versions/{vid}/activate` | activate | 手动激活（补救，正常走自动） |
| POST | `/versions/{vid}/copy` | copy | 复制源版本/上年实际到本 Draft（§5.5） |
| DELETE | `/versions/{vid}` | delete | 删 Draft 版本（仅 Draft） |

### 10.2 `BudgetLineController`（`api/fin/budget/lines`）— 行网格

| 方法 | 路由 | 权限 action | 说明 |
|---|---|---|---|
| GET | `/?versionId=` | view | 版本预算行 + 12 期（网格数据） |
| POST | `/` | edit | 新增/改维度桶 + 12 期（upsert，仅 Draft） |
| DELETE | `/{lineId}` | edit | 删行（仅 Draft） |
| POST | `/import/preview` | import | Excel 预览（dryRun，§5.4） |
| POST | `/import/confirm` | import | Excel 确认落库 |

### 10.3 `BudgetReportController`（`api/fin/budget/report`）— 分析

| 方法 | 路由 | 权限 action | 说明 |
|---|---|---|---|
| GET | `/vs-actual?fiscalYear=&versionId=&from=&to=&...` | view | 预算 vs 实际（§9） |
| POST | `/pre-check` | view | 凭证提交前预算预检（§7.4，返回 Warn/Block 预警列表） |

> OA 审批通过/驳回**不经 A5 控制器**——由 OA 引擎 `ApprovalDispatcher` 回调 `BudgetApprovalCallback`（§8.2）。

---

## 11. 页面（前端 Fin house style）

house style：`page-header`(h2+subtitle) / `el-card shadow="never"` / `table-toolbar`(flex) / `el-table`(border stripe size=small max-height v-loading) / 操作列 `link` 文字按钮 / 状态 `el-tag` 配色 / `<style scoped>`。i18n 用中文自然语言 key（fin 模块惯例，避开 A3 点状 key 教训）。

### 11.1 `BudgetEditView`（菜单 622 预算编制）

- 左：方案树（按财年）→ 版本列表（状态 tag：草稿/审批中/已批/已驳/已归档 + Active 徽标）。
- 右：选中版本 → 维度行网格（科目/成本中心/成本对象 + M1..M12 + 合计列），工具条：新增行 / Excel 导入 / 复制(上年实际/上版本) / 分解(均摊/季节) / 提交审批 / 刷新。
- 行内配 `ControlMode`(None/Warn/Block) + `ControlBasis`(YTD/期) 下拉。
- 编辑门控：仅 Draft 可改（审批中/已批只读 + 提示"新建版本以调整"）。
- 提交审批弹确认；并发冲突（E-A5-CONCURRENCY-001）→ 刷新重试 toast。

### 11.2 `BudgetVsActualView`（菜单 623 执行分析）

- 筛选：财年 / 版本(默认 Active) / 期间区间(月/季/年) / 维度(成本中心/科目) 筛选。
- 主表：维度行 + 预算/实际/差异/差异率/执行率；费用超支红、收入未达黄；12 期钻取展开。
- 顶部卡片：总预算 / 总实际 / 总差异 / 整体执行率。

### 11.3 菜单与导航

- `621 预算管理`（父，挂 `600 财务管理`）；`622 预算编制`（→ `/fin/budget`）；`623 执行分析`（→ `/fin/budget/vs-actual`）。
- RoleMenu(admin 全开) + MenuActions(view/add/edit/delete/submit/activate/import/copy)；MenuKey 回填 ≤623。

---

## 12. 权限

资源键 `fin-budget`（由 RoutePath `/fin/budget` 自动派生 MenuKey）。操作 action：

| action | 含义 | 端点 |
|---|---|---|
| view | 查看方案/版本/行/报表/预检 | 所有 GET + pre-check |
| add | 建方案/版本 | POST 方案/版本 |
| edit | 改方案/版本/行/删行 | PUT、行 upsert/del |
| delete | 删 Draft 版本 | DELETE 版本 |
| submit | 提交审批 | submit |
| activate | 激活版本 | activate |
| import | Excel 导入 | import/* |
| copy | 复制建版起点 | copy |

> GET 端点须 seed `view` 权限（避开 A3 漏 view 致 403 的坑）。admin 默认全 action。

---

## 13. 错误码

前缀 `E-A5-*`（五语 seed 于 `I18nA5BudgetScreenSeed` + 全局 `I18nBackendMsgSeed` 视项目惯例）。

| 码 | 含义 |
|---|---|
| E-A5-BUDGET-001 | 该财年已存在预算方案（唯一财年） |
| E-A5-VERSION-002 | 仅草稿版本可提交审批 |
| E-A5-VERSION-003 | 该版本已有进行中审批（防重复提交） |
| E-A5-VERSION-004 | 仅已批准版本可激活 |
| E-A5-VERSION-005 | 版本非草稿，不可编辑 |
| E-A5-VERSION-006 | 版本无预算行，不可提交审批 |
| E-A5-LINE-001 | 维度桶重复（同版本 科目+成本中心+成本对象 唯一） |
| E-A5-LINE-002 | 科目须为末级且属损益类（费用/收入） |
| E-A5-LINE-003 | 按月分解合计与年度额不一致（落码取自动回填，一般不触发） |
| E-A5-LINE-004 | 成本对象类型/编号须成对，或期号超 1..12 |
| E-A5-LINE-005 | 成本中心不存在（Excel 导入引用的成本中心编码未找到） |
| E-A5-BUDGET-EXCEEDED | 过账超预算（Block 行）：科目 {0} 预算 {1} 已用 {2} 本次 {3} 超出 {4} |
| E-A5-IMPORT-001 | Excel 含致命错误，整批拒绝 |
| E-A5-COPY-001 | 复制源不存在或为空 |
| E-A5-CONCURRENCY-001 | 数据已被他人修改，请刷新重试（RowVersion 冲突） |
| W-A5-BUDGET-WARN | 预算预警（Warn 行超支，报表/预检提示，不拦过账） |

---

## 14. 工程护栏

### 14.1 迁移与索引

- 迁移 `A5Budget`：4 表（Budget/BudgetVersion/BudgetLine/BudgetLinePeriod）+ 唯一索引（含 TenantId 前缀自动补）+ RowVersion 列（BudgetVersion/BudgetLine）+ decimal(18,2)。
- BudgetLine 含 3 个 not-null 规范化派生列 `CostCenterKey`(Guid)/`CostObjectTypeKey`(string)/`CostObjectIdKey`(string)，与真实可空维度 `CostCenterId`/`CostObjectType`/`CostObjectId` 并存（§3.3）；真实维度可建 FK，Key 列不建 FK。
- 唯一索引：`UX_Fin_Budget_FiscalYear`、`UX_Fin_BudgetVersion_BudgetNo`、`UX_Fin_BudgetLine_Dim`(=VersionId+AccountId+**CostCenterKey+CostObjectTypeKey+CostObjectIdKey**，规避 NULL 不唯一)、`UX_Fin_BudgetLinePeriod_LinePeriod`。
- Active 唯一性由服务层保证（不靠 DB 过滤唯一索引，避免跨库差异）。

### 14.2 多租户

- 4 实体继承 `BaseTenantEntity`（带 TenantId 自动盖、行级隔离）。新实体无需另做多租户（已收口）。

### 14.3 RowVersion 乐观并发

- `BudgetVersion`/`BudgetLine` 显式加 `[Timestamp] byte[]? RowVersion`（`BaseTenantEntity` 不含，仅 `BaseBizEntity` 有；A5 不继承 BaseBizEntity 因预算停用≠逻辑删除）。
- 编辑/激活冲突 → `DbUpdateConcurrencyException` → E-A5-CONCURRENCY-001。
- BudgetLinePeriod 随 BudgetLine 整体保存，不单独加 RowVersion。

### 14.4 审计日志

- `Sys_OperLog` 记关键操作：建方案/建版本/提交审批/激活/导入/复制/删版本。OA 回调（通过/驳回）由 OA 侧留痕 + 版本 ApprovedBy/RejectReason。

### 14.5 i18n

- `I18nA5BudgetScreenSeed.Items`：nav.621-623 + 枚举(状态/控制模式/口径) + 字段/动作 label + 错误码 E-A5-*/W-A5-*；`Program.cs .Concat`；幂等插入。
- 五语（zh-CN/zh-TW/en/ja/ko），中文自然语言 key（fin 惯例）。零裸 key（A3/A4 教训）。

### 14.6 跨模块解耦

- 依赖 OA `IApprovalService`/`IApprovalCallback`（同程序集真实接口，OA 已落地，不用桩）。
- 只读 GL 实际 + FiscalPeriod；`BudgetGuard` 静态直查无循环依赖。
- 不改任何既有模块逻辑（仅 `PostAsync` 加一行守卫调用）。

---

## 15. 验收标准（AC）

| # | 场景 | 期望 |
|---|---|---|
| AC-001 | 年度额均摊到 12 期 | 每期=年度/12，余数进末期，Σ=年度额 |
| AC-002 | 同版本重复维度桶（含两行均"公司级/无成本对象"即维度全空） | 规范化 Key 命中唯一索引、拒绝 E-A5-LINE-001（验证 NULL 维度也唯一） |
| AC-003 | 科目非末级/非损益类入行 | 拒绝 E-A5-LINE-002 |
| AC-004 | Draft 提交审批 | 起 OA 流程、置 PendingApproval、存 ApprovalInstanceId、版本锁定不可改 |
| AC-005 | 非 Draft 提交 / 重复提交 | E-A5-VERSION-002 / E-A5-VERSION-003 |
| AC-006 | OA 通过回调 | 版本 Approved 且自动 Activate、旧 Active 置 Archived、本版 IsActive |
| AC-007 | OA 驳回回调 | 版本 Rejected + RejectReason；可改回 Draft 重编 |
| AC-008 | Block 行 + YTD 口径 + 手工过账超累计预算 | PostAsync 拒绝 E-A5-BUDGET-EXCEEDED（含科目/预算/已用/超出 args）；落期经 PeriodId（非自然财年也对） |
| AC-009 | Block 行 + 过账未超预算 | 正常过账通过 |
| AC-010 | Warn 行 + 过账超预算 | 过账**通过**（不拦），报表/预检标红预警 W-A5 |
| AC-011 | AutoPostAsync 超预算 | 不拦（自动凭证放行），报表反映超支 |
| AC-012 | 最具体匹配 | 成本中心级凭证命中成本中心桶、否则命中公司级桶（§7.3） |
| AC-013 | 同 entry 多行命中同桶 | 合并消耗再比，不可拆行绕过 |
| AC-014 | 预算 vs 实际维度聚合 | 实际按 (Account,CC,CO) 桶聚合数与逐凭证手算一致 |
| AC-015 | 复制上年实际为起点 | 新版本按月数 = 上年同桶已过账实际聚合 |
| AC-016 | Excel 导入致命错误 | 整批拒绝 E-A5-IMPORT-001，零持久化 |
| AC-017 | RowVersion 并发改行 | 后提交者 E-A5-CONCURRENCY-001 |
| AC-018 | 无 Active 版本时过账 | BudgetGuard 短路 Pass，过账不受影响 |
| AC-019 | 红冲已过账凭证 | 不被预算拦截（释放预算） |
| AC-020 | GET 端点权限 | seed view 后 admin 可读（无 403） |
| AC-021 | 实际发生但无任何匹配预算桶 | 报表单列"未编预算实际/Unbudgeted Actual"分组（预算=0、实际=发生额），不静默丢弃、计入总实际 |
| AC-022 | 非自然财年（FiscalYear≠日历年）下过账落期 | BudgetGuard 经 entry.PeriodId 取 FiscalYear/PeriodNo 正确归期；PeriodId 空时 fallback ResolveAsync |
| AC-023 | OA 通过回调激活 | OnApproved 内同事务一次性完成 Approved+清旧 Active+本版 IsActive，无重查未存盘状态导致的不一致 |

---

## 16. 测试建议

### 16.1 分层

- **InMemory**（逻辑层）：版本状态机迁移、按月分解（均摊/季节/手工）、AnnualAmount 回填、BudgetGuard 多维最具体匹配 + YTD/Period 判定 + 同 entry 合并、Warn/None 放行、复制(版本/实际)、Excel 解析、vs 实际维度聚合算术。
- **SQLite**（结构层）：唯一约束（含哨兵规范化 NULL 维度）、RowVersion 并发冲突、FK。
  - 注：A3/A4 因 `CP6Context` 含 `nvarchar(max)` 致 SQLite `EnsureCreated` 撞 `near "max"`——A5 同库，预期 SQLite 全 schema 建库受阻；唯一约束/并发以 InMemory + 真 SQL Server 兜底（与 A3/A4 一致处理，落码时核实是否已有 SQLite 测试基线绕法）。
- **OA 集成**：仿 `JournalApprovalIntegrationTests` seed `Wf_FlowDef`+`Wf_ApprovalBinding`，跑 提交→通过/驳回→回调改版本状态 全链。

### 16.2 gstack 端到端 QA

- 起后端 + 前端，admin 登录：建方案 → 建版本(复制上年实际) → 编行(配 Block/YTD) → 分解 → 提交审批 → (OA 通过) 自动激活 → 手工凭证超预算被拒 → 预算 vs 实际报表数对 → 五语切换零裸 key → 权限无 403。
- 重点逮"菜单驱动路由未注册致白屏"类（A4 H-3 教训）：seed 菜单 621-623 + RoleMenu 后核所有视图可达。

---

## 17. 落码顺序建议（subagent-driven 预排，详见 plan）

```
A 数据模型 + 迁移（4 实体 + 枚举 + 索引 + RowVersion）
B 方案/版本 Service + 状态机（建/复制/分解/CRUD）
C 预算行 Service + Excel 导入（Preview/Confirm）+ 复制(版本/实际)
D OA 接入（SubmitForApproval + BudgetApprovalCallback + Seed FlowDef/Binding + 自动激活）
E BudgetGuard 控制守卫（挂 PostAsync）+ PreCheck 预检
F 预算 vs 实际报表 Service（维度聚合）
G API 控制器 3 个 + 操作级权限 seed + 菜单 621-623
H 前端 2 视图 + types/api + i18n（I18nA5BudgetScreenSeed 五语）
I 分层测试 + OA 集成测试 + gstack QA
```

硬任务（评审上 opus）：E（守卫多维匹配 + YTD/Period 口径 + 同 entry 合并）、D（OA 回调原子性 + 自动激活清旧）、F（实际维度聚合口径与上卷）、C（复制实际的聚合 + Excel 维度解析）。

---

*生成于 2026-06-20。现状据财务 GL/凭证/成本中心/期间/三表 + OA 审批引擎(IApprovalService/IApprovalCallback/ApprovalDispatcher/Wf_FlowDef) + A4 BankReconGuard 真实代码探查（逆向不编造）。决策 A5-D1~D7 + §8 四小决策全采纳推荐值。下一步 writing-plans 出实施计划（对齐 A3/A4 plan 范式，subagent-driven）。*
