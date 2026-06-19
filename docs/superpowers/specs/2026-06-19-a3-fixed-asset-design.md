# A3 固定资产（Fixed Asset）设计 spec

> ERP 完整性路线 A3。补齐制造业 ERP 的固定资产管理：资产分类与卡片 → 四法月末折旧（汇总凭证 + 资产级明细追溯）→ 全套处置（出售/报废/转让/盘亏，经清理科目结转）。**全程复用已成熟的 GL 自动凭证基建（`JournalEntryService.AutoPostAsync` + `GlAccount.Role` + `PeriodCloseService` 钩子 + `TenantScopeRunner`），零引擎改造**。
>
> 源对话：brainstorming 定稿（A3-D1~D9，用户两批决策全采纳）。日期 2026-06-19。命名空间 **Fin**。

---

## 0. 目标与范围

**题眼**：系统已有 `GlAccount`(含 `Role` 锚点)/`JournalEntryService`(`AutoPostAsync`/`ReverseAsync`)/`FiscalPeriodService`/`PeriodCloseService`(`CloseAsync` 钩子)/`FinSequenceService`/`CostCenter`(树·`LinkMachineId`)/`AutoVoucherEngine` 全套 GL 与自动凭证基建，科目 `1601 固定资产`/`1602 累计折旧` 已留；但**缺固定资产业务侧**：没有资产分类/卡片/折旧计提/处置。A3 补齐「资产卡片 → 月末折旧凭证 → 处置结转」闭环。

**账务策略（核心）**：折旧与处置凭证**不走 `AutoVoucherEngine` 的 `FinBizEvent→PostingRule` 路径**（折旧是多资产汇总分行、处置是变结构多行，PostingRule 单据模型不契合），改为**仿 `FxRevaluationService`**：服务层直接拼 `JournalEntry` → `JournalEntryService.AutoPostAsync`，科目由 `GlAccount.Role`（单例锚点）+ `AssetCategory`（费用科目路由，非单例）解析。**无需新增 PostingRule，不改引擎**。

**取得入账边界（防双写）**：资产「取得」分录（借 固定资产 / 贷 银行·在建工程·应付）由**采购/AP 或手工凭证**完成；A3 资产卡片**仅登记主数据、不重复生成取得凭证**。期初建卡（存量资产导入）亦**不生成凭证**（原值/累计折旧已在 GL 期初数）。A3 只**自动生成两类凭证**：① 月末折旧；② 处置结转。

**纳入 MVP**：
- 资产分类（`AssetCategory`，树·默认年限/残值/方法 + 三科目路由）；资产卡片（含期初建卡、草稿→在用、折旧计划预览、轻量变更）。
- 四法折旧（直线/双倍余额递减/年数总和/工作量法）；次月起折/次月停折；当期处置照提。
- 折旧计提三路：手动 Run→复核→Post；月末 Worker 备草稿（不自动过账）；结账钩子 `AccrueAsync` 兜底。批次幂等 + 反冲回滚 + 资产级明细追溯。
- 全套处置（出售/报废/转让/盘亏）经清理科目（`1606 固定资产清理` / `1901 待处理财产损溢`）单次确认结转，幂等 + 反冲。
- 科目/分类 Seed；操作级权限 + 审计日志；五语 i18n；分层测试（InMemory + SQLite）+ gstack QA。

**推迟（非本期，§15）**：资产减值准备/重估；后续资本化支出；租赁资产；卡片大变更（仅做成本中心/部门/责任人轻量调整）；工作量法的 MES 产量自动回写（本期工作量手工录入）；折旧计划表 PDF 导出；资产标签/二维码盘点。

---

## 1. 现状与依赖（落码前必读，均已存在）

| 资产 | 位置 | A3 用法 |
|---|---|---|
| `JournalEntry`/`JournalLine` | `CP6.Entity/DomainModels/Fin/JournalEntry.cs`·`JournalLine.cs` | `JournalLine.{AccountId,Debit,Credit,CostCenterId,CurrencyCd}`(L42-43 有 `CostCenterId`)；`JournalEntry.{VoucherDate,PeriodId,Source,Status,SourceDocNo}`。折旧/处置凭证载体 |
| `JournalEntryService` | `CP6.Core/Services/Fin/IJournalEntryService.cs` | `AutoPostAsync(entry)`(一步建+校验配平+过账)/`ReverseAsync(id,maker,reason,autoPost)`(红冲)。折旧/处置过账与反冲挂载点 |
| `AutoVoucherEngine` | `CP6.Core/Services/Fin/AutoVoucherEngine.cs` | **A3 不直接调用**（见 §0 账务策略）；仅参考其 `ResolveRoleAsync`(L98-103) 的 Role→科目解析逻辑 |
| `GlAccount`(Fin_GlAccount) | `CP6.Entity/DomainModels/Fin/GlAccount.cs` L15-89 | `Code/Name/Type/NormalSide/ParentId/Level/IsLeaf/IsControl/`**`Role`**`(L51-53)/IsActive`。单例科目（清理/损益/累计折旧）按 Role 解析 |
| `FiscalPeriodService` | `CP6.Core/Services/Fin/` | `ResolveAsync(date)`/`EnsureOpenAsync`/`IsOpenAsync`。折旧/处置凭证落期、起折期推算 |
| `PeriodCloseService` | `CP6.Core/Services/Fin/PeriodCloseService.cs` L53-77 | `CloseAsync(periodId,userId)`：L60-64 `if(_reval!=null)` 钩子模式 → A3 在其前插 `if(_depreciation!=null) AccrueAsync` 兜底 |
| `FxRevaluationService` | `CP6.Core/Services/Fin/FxRevaluationService.cs` L32-141 | **折旧/处置服务的实现范本**：L32-40 幂等检查、L44-47 Role 解析科目、L111-140 拼 `JournalEntry`→`AutoPostAsync` |
| `FinSequenceService` | `CP6.Core/Services/Fin/IFinSequenceService.cs`·`FinSequenceService.cs` L15-39 | `NextAsync(seqKey,date)`→`KEY-yyyy-MM-nnnnn`。采番 `FA`(卡片)/`DEP`(折旧批)/`FAD`(处置) |
| `CostCenter`(Fin_CostCenter) | `CP6.Entity/DomainModels/Fin/CostCenter.cs` L14-48 | `Type{Department=1,Process=2,Machine=3}`/`ParentId`/`LinkMachineId`。卡片成本中心由机台派生 |
| `Machine`(M_Machine) | `CP6.Entity/DomainModels/Mes/Machine.cs` L14-70 | `Id`/`MachineCd`/`MachineName`。资产卡片可选 `MachineId` 关联（派生成本中心） |
| `FinResult` | `CP6.Core/Services/Fin/FinResult.cs` | `{Ok,Code,Args}`+`Pass()`/`Fail(code,args)`。A3 服务统一返回 |
| `VoucherSource` enum | `CP6.Entity/DomainModels/Fin/JournalEntry.cs` L71-88 | 现 `Manual=0/AP=1/AR=2/Cost=3/Carryover=4/Reversal=5/FxReval=6` → **A3 新增 `Depreciation=7`/`AssetDisposal=8`**（A4 spec 的 `BankRecon=7` 未开工，届时顺延为 `9`，A3 落地时补注 A4 spec） |
| `TenantScopeRunner` | `CP6.WebApi/BackgroundServices/TenantScopeRunner.cs` L15-51 | `ForEachTenantAsync(scopeFactory,body,logger,ct)`。折旧 Worker 必经此租户循环 |
| 后台 Worker 范本 | `CP6.WebApi/BackgroundServices/FinReconciliationWorker.cs` L46-57 | `IHostedService` + `TenantScopeRunner` 范式。`AssetDepreciationWorker` 仿写 |
| Worker 注册 | `CP6.WebApi/Program.cs` L117 | `AddHostedService<...>()`；服务 DI L120-143 `AddScoped<IService,Impl>()` |
| Fin 控制器范式 | `CP6.WebApi/Controllers/Fin/` | `[Authorize]`+`[RequirePermission("module","action")]`(JournalEntryController L36-59)，`Ok2(data)`/`Fin(FinResult)`/`CurrentUser` |
| 结账权限范式 | `CP6.WebApi/Controllers/Fin/PeriodController.cs` L34-42 | `[RequirePermission("fin-period","close")]` 高权限模式 |
| Fin 菜单 600 组 | `CP6.WebApi/Program.cs` + i18n | 601~613 已用、**614 留 A4**；A3 用 **615~618** |
| i18n 种子 | `CP6.WebApi/Seed/I18nFinScreenSeed.cs` | `nav.6xx`/枚举/字段/错误码五语。A3 追加资产词条 |
| 多租户基类 | `CP6.Entity/BaseTenantEntity.cs`(=`BaseEntity`Id/审计+`TenantId`)·`BaseBizEntity.cs`(+`IsDeleted/RowVersion`) | A3 实体继承 `BaseTenantEntity` 并**显式加 `RowVersion`**（§8.3，沿 A4）；唯一索引声明后自动补 `TenantId` 前缀 |
| 审计日志 | `Sys_OperLog` | A3 关键操作写审计（§12） |

---

## 2. 数据模型

5 个新实体，均继承 `BaseTenantEntity` + 显式 `RowVersion`，表前缀 `Fin_`。

### 2.1 `AssetCategory`（资产分类·主数据）`Fin_AssetCategory`

| 字段 | 类型 | 说明 |
|---|---|---|
| `Code` | string(30) | 分类编码，唯一 |
| `Name` | string(100) | 分类名（房屋建筑物/机器设备/运输设备/电子设备/办公设备…） |
| `ParentId` | Guid? | 树形父分类 |
| `Level` | int | 层级（1 起） |
| `DefaultMethod` | enum `DepreciationMethod` | 默认折旧方法（D1 四法之一） |
| `DefaultUsefulLifeMonths` | int | 默认可使用月数 |
| `DefaultSalvageRate` | decimal(7,4) | 默认残值率（0.05=5%） |
| **`AssetAccountId`** | Guid | → GlAccount，固定资产科目（默认 1601） |
| **`AccumDeprecAccountId`** | Guid | → GlAccount，累计折旧科目（默认 1602） |
| **`DeprecExpenseAccountId`** | Guid | → GlAccount，折旧费用科目（机器设备→制造费用 5101 / 办公→管理费用 6602 / 销售用→销售费用 6601）。**D8 账务路由核心** |
| `IsActive` | bool | 启用 |
| `RowVersion` | byte[]? | 乐观并发 |

唯一索引：`UX_Fin_AssetCategory_Code`=(Code)（自动补 TenantId 前缀）。
> 三科目均存**直接 GlAccountId**（非 Role）：固定资产/累计折旧通常跨分类同为 1601/1602，但**折旧费用因分类而异**（制造/管理/销售费用），Role 单例机制无法承载多目标路由，故落到分类。

### 2.2 `AssetCard`（资产卡片·核心）`Fin_AssetCard`

| 字段 | 类型 | 说明 |
|---|---|---|
| `AssetNo` | string(30) | 资产编号，`FA-yyyy-MM-nnnnn`（FinSequenceService key=`FA`） |
| `Name` | string(100) | 资产名称 |
| `SpecModel` | string(100)? | 规格型号 |
| `CategoryId` | Guid | → AssetCategory（建卡时拉默认值/科目路由） |
| `OriginalValue` | decimal(18,2) | 原值/入账价值 |
| `SalvageRate` | decimal(7,4) | 残值率（默认取分类，可覆盖） |
| `SalvageValue` | decimal(18,2) | 残值=OriginalValue×SalvageRate（建卡时算，可手工改） |
| `Method` | enum `DepreciationMethod` | 折旧方法（默认取分类，可覆盖） |
| `UsefulLifeMonths` | int | 可使用月数（默认取分类，可覆盖） |
| `TotalWorkload` | decimal(18,4)? | 预计总工作量（**工作量法必填**，否则 FA008） |
| `WorkloadUnit` | string(20)? | 工作量单位（小时/件/公里…） |
| `AcquisitionDate` | DateTime | 购置/取得日 |
| **`DepreciationStartPeriod`** | string(7)? | 起折期间 `yyyy-MM`（= 购置日**次月**，D2；启用时定格） |
| `AccumulatedDepreciation` | decimal(18,2) | 已提累计折旧（运行态，含期初导入数） |
| `DepreciatedPeriods` | int | 已提期数（运行态） |
| `NetBookValue` | decimal(18,2) | **只读计算 / 物化**：OriginalValue − AccumulatedDepreciation（净值；本期无减值） |
| `DeprecExpenseAccountId` | Guid? | 折旧费用科目**卡片覆盖**（null=取分类默认） |
| `CostCenterId` | Guid? | 成本中心（分析维度；机台派生或手工，D8） |
| `MachineId` | Guid? | → MES Machine（可选；派生成本中心用） |
| `DeptId` | Guid? | 使用部门（成本中心回退） |
| `Status` | enum `AssetStatus` | Draft=0 / InUse=1 / FullyDepreciated=2 / Disposed=3 |
| `Location` | string(200)? | 存放地点 |
| `Custodian` | string(100)? | 责任人 |
| `IsOpeningImport` | bool | 期初建卡标记（true=不生成取得凭证、允许录入初始累计） |
| `Remarks` | string(500)? | 备注 |
| `RowVersion` | byte[]? | 乐观并发 |

唯一索引：`UX_Fin_AssetCard_AssetNo`=(AssetNo)。索引 `IX_..._Category`(CategoryId)、`IX_..._Status`(Status)、`IX_..._Machine`(MachineId)。
> **期初建卡**：`IsOpeningImport=true` 时允许直接录入 `AccumulatedDepreciation/DepreciatedPeriods` 作期初；不生成凭证。普通建卡二者初始为 0。

### 2.3 `DepreciationRun`（折旧批次头·每期一批）`Fin_DepreciationRun`

| 字段 | 类型 | 说明 |
|---|---|---|
| `No` | string(30) | 批次号，`DEP-yyyy-MM-nnnnn` |
| `FiscalPeriodId` | Guid | → FiscalPeriod（计提期间） |
| `PeriodYearMonth` | string(7) | `yyyy-MM`（冗余展示） |
| `Status` | enum `DepreciationRunStatus` | Draft=0 / Posted=1 / Reversed=2 |
| `RunMode` | enum `DepreciationRunMode` | Manual=1 / Worker=2 / CloseHook=3 |
| `TotalAmount` | decimal(18,2) | 本批折旧合计 |
| `AssetCount` | int | 计提资产数 |
| `JournalEntryId` | Guid? | 过账生成的汇总凭证（**幂等键**） |
| `RunAt`/`RunBy` | DateTime/string | 生成审计 |
| `PostedAt`/`PostedBy` | DateTime?/string? | 过账审计 |
| `ReversedAt`/`ReversedBy` | DateTime?/string? | 反冲审计 |
| `RowVersion` | byte[]? | 乐观并发 |

约束（服务层守卫）：**每期仅一个非 Reversed 批次**（FA003）。EF 无法对 enum 做过滤唯一索引，故落服务层校验（沿 A4 幂等模式）。

### 2.4 `DepreciationEntry`（资产级折旧明细·每资产每批）`Fin_DepreciationEntry`

| 字段 | 类型 | 说明 |
|---|---|---|
| `RunId` | Guid | → DepreciationRun |
| `AssetCardId` | Guid | → AssetCard |
| `FiscalPeriodId` | Guid | 冗余（追溯） |
| `Method` | enum `DepreciationMethod` | 当期采用方法（快照） |
| `DepreciationAmount` | decimal(18,2) | 本期折旧额 |
| `OpeningAccumulated` | decimal(18,2) | 期初累计 |
| `ClosingAccumulated` | decimal(18,2) | 期末累计 |
| `OpeningNetValue` | decimal(18,2) | 期初净值 |
| `ClosingNetValue` | decimal(18,2) | 期末净值 |
| `DeprecExpenseAccountId` | Guid | 借方折旧费用科目（快照） |
| `AccumDeprecAccountId` | Guid | 贷方累计折旧科目（快照） |
| `CostCenterId` | Guid? | 成本中心（快照，汇总凭证分行键） |
| `WorkloadThisPeriod` | decimal(18,4)? | 工作量法本期工作量（Run 后手工录、Post 前必填） |
| `RowVersion` | byte[]? | 乐观并发 |

唯一索引：`UX_Fin_DepreciationEntry_RunAsset`=(RunId, AssetCardId)。索引 `IX_..._Asset`(AssetCardId)。

### 2.5 `AssetDisposal`（资产处置单）`Fin_AssetDisposal`

| 字段 | 类型 | 说明 |
|---|---|---|
| `No` | string(30) | 处置单号，`FAD-yyyy-MM-nnnnn` |
| `AssetCardId` | Guid | → AssetCard |
| `DisposalType` | enum `AssetDisposalType` | Sale=1 / Scrap=2 / Transfer=3 / InventoryLoss=4 |
| `DisposalDate` | DateTime | 处置日 |
| `FiscalPeriodId` | Guid | → FiscalPeriod（落期） |
| `OriginalValue` | decimal(18,2) | 原值（快照） |
| `AccumulatedDepreciation` | decimal(18,2) | 累计折旧（快照，**含处置月补提后**） |
| `NetBookValue` | decimal(18,2) | 净值=Original−Accum（快照） |
| `Proceeds` | decimal(18,2) | 处置价款不含税（出售/转让；报废残料收入） |
| `TaxAmount` | decimal(18,2) | 销项税（出售） |
| `DisposalExpense` | decimal(18,2) | 清理费用 |
| `NetGainLoss` | decimal(18,2) | 净损益=Proceeds−DisposalExpense−NetBookValue（计算） |
| `ClearingAccountId` | Guid | 清理科目（Sale/Scrap/Transfer→1606；InventoryLoss→1901；按类型解析） |
| `GainLossAccountId` | Guid | 损益科目（Sale/Transfer→6115；Scrap/InventoryLoss→6711/6301） |
| `ReceiptBankAccountId` | Guid? | 收款银行账户（出售/转让有价款时） |
| `Status` | enum `AssetDisposalStatus` | Draft=0 / Confirmed=1 / Reversed=2 |
| `JournalEntryId` | Guid? | 结转凭证（**幂等键**） |
| `FinalDeprecEntryId` | Guid? | 处置月补提折旧明细（若触发，§4.3） |
| `ConfirmedAt`/`ConfirmedBy` | DateTime?/string? | 确认审计 |
| `ReversedAt`/`ReversedBy` | DateTime?/string? | 反冲审计 |
| `Reason` | string(500)? | 处置原因 |
| `RowVersion` | byte[]? | 乐观并发 |

唯一索引：`UX_Fin_AssetDisposal_No`=(No)。索引 `IX_..._Asset`(AssetCardId)、`IX_..._Status`(Status)。约束：一资产仅一张非 Reversed 处置单（服务层守卫，FA002）。

### 2.6 枚举

```csharp
public enum DepreciationMethod { StraightLine = 1, DoubleDeclining = 2, SumOfYears = 3, UnitsOfProduction = 4 }
public enum AssetStatus { Draft = 0, InUse = 1, FullyDepreciated = 2, Disposed = 3 }
public enum DepreciationRunStatus { Draft = 0, Posted = 1, Reversed = 2 }
public enum DepreciationRunMode { Manual = 1, Worker = 2, CloseHook = 3 }
public enum AssetDisposalType { Sale = 1, Scrap = 2, Transfer = 3, InventoryLoss = 4 }
public enum AssetDisposalStatus { Draft = 0, Confirmed = 1, Reversed = 2 }
// VoucherSource 追加：Depreciation = 7, AssetDisposal = 8
```

---

## 3. 折旧引擎

### 3.1 `IDepreciationCalculator`（纯函数计算·无 DB 依赖，单元测试主战场）

```csharp
public interface IDepreciationCalculator
{
    // 给定资产折旧参数 + 已提期数 + 本期工作量，返回本期折旧额（已封顶残值、末期取整兜底）
    decimal PeriodAmount(DepreciationCalcInput input);
}
public sealed class DepreciationCalcInput
{
    public DepreciationMethod Method;
    public decimal OriginalValue;        // 原值
    public decimal SalvageValue;         // 残值
    public int UsefulLifeMonths;         // 可使用月数
    public int DepreciatedPeriods;       // 本期之前已提期数
    public decimal AccumulatedBefore;    // 本期之前累计折旧
    public decimal? TotalWorkload;       // 工作量法预计总量
    public decimal? WorkloadThisPeriod;  // 工作量法本期量
}
```

**四法公式**（`Depreciable = OriginalValue − SalvageValue`；`RemainMonths = UsefulLifeMonths − DepreciatedPeriods`）：

| 方法 | 月折旧额 |
|---|---|
| **StraightLine 直线** | `Depreciable / UsefulLifeMonths` |
| **DoubleDeclining 双倍余额递减** | `max( NetBookBefore × (2 / UsefulLifeYears) / 12 , (NetBookBefore − SalvageValue) / RemainMonths )`，其中 `NetBookBefore = OriginalValue − AccumulatedBefore`（**不扣残值**）；取 max 等价于末两年自动切直线 |
| **SumOfYears 年数总和** | 按所处年序 `y`（`y = ⌈(DepreciatedPeriods+1)/12⌉`，`Y=⌈UsefulLifeMonths/12⌉`）：年额 `= Depreciable × (Y−y+1) / (Y(Y+1)/2)`；月额 `= 年额 / 12` |
| **UnitsOfProduction 工作量** | `Depreciable × WorkloadThisPeriod / TotalWorkload`（`TotalWorkload` 缺→FA008） |

**统一兜底**：① 封顶 `本期额 = min(本期额, Depreciable − AccumulatedBefore)`（累计不破可折上限）；② 末期（`RemainMonths==1` 或封顶触发）一次性补足残差，消除累计取整误差；③ 结果 `< 0` 取 0。

### 3.2 `IAssetDepreciationService`

```csharp
public interface IAssetDepreciationService
{
    Task<List<DepreciationEntryDto>> PreviewAsync(Guid periodId);                 // 试算不落库
    Task<FinResult> RunAsync(Guid periodId, string userId, DepreciationRunMode mode); // 生成 Draft 批次+明细（幂等）
    Task<FinResult> SetWorkloadAsync(Guid entryId, decimal workload);             // 工作量法补录本期量
    Task<FinResult> PostAsync(Guid runId, string userId);                         // 拼汇总凭证→AutoPost→回写卡片
    Task<FinResult> ReverseAsync(Guid runId, string userId, string reason);       // 红冲+回滚卡片
    Task<FinResult> AccrueAsync(Guid periodId, string userId);                    // Run(若需)+Post，幂等；结账钩子/兜底
    Task<List<DepreciationScheduleRow>> GetScheduleAsync(Guid assetCardId);       // 单卡前瞻折旧计划
}
```

- **计提资格（期 P，D2 次月起停）**：`card.Status==InUse` 且 `DepreciationStartPeriod ≤ P` 且 `AccumulatedDepreciation < OriginalValue − SalvageValue` 且 **该资产本期 P 无 Posted 折旧明细**（去重键，防与处置补提重复）。即**当期增加不提（起折=次月）**。
- **当期处置照提（次月停）· 顺序无关**：资产在 P 处置 → 恰好计提一次 P 折旧。若**批量先于处置确认**：批量纳入（仍 InUse）→ 处置确认见已有 P Posted 明细则跳过补提；若**处置先于批量**：确认时补提 P（§4.3）并置 `Disposed`→ 批量按 `Status==InUse` 过滤自动排除。两序殊途同归、不重不漏。
- **RunAsync**：守卫期间开启（FA007）+ 无非 Reversed 批次（FA003）→ 取资格资产 → 逐资产 `IDepreciationCalculator.PeriodAmount` → 写 `DepreciationRun(Draft)` + `DepreciationEntry[]`（费用/累计科目、成本中心快照）。工作量法资产 `WorkloadThisPeriod=null` 占位。
- **PostAsync**：守卫工作量法明细已补录（FA008）→ 拼**汇总凭证**（§5.1）→ `AutoPostAsync` → 回写每卡 `AccumulatedDepreciation += 额`、`DepreciatedPeriods += 1`、累计达上限置 `FullyDepreciated` → `Run.Status=Posted`+`JournalEntryId`。
- **ReverseAsync**：守卫 `Status==Posted`（FA009）→ `JournalEntryService.ReverseAsync(JournalEntryId,…)` 红冲 → 逐卡回滚累计/期数/状态 → `Run.Status=Reversed`。
- **AccrueAsync**：幂等（本期已 Posted→Pass）；无批次则 `RunAsync(CloseHook)`→`PostAsync`。供结账钩子（§6.1）与 Worker 兜底调用。

### 3.3 成本中心派生（D8）

`DepreciationEntry.CostCenterId` 解析序：① 卡片 `CostCenterId` 显式值；② 否则若卡片有 `MachineId` → 查 `CostCenter.LinkMachineId == MachineId` 命中者；③ 否则按 `DeptId` 找部门型成本中心；④ 仍无→ null（凭证行无成本中心）。借方折旧费用科目解析序：卡片 `DeprecExpenseAccountId` 覆盖 > 分类 `DeprecExpenseAccountId`。

---

## 4. 处置（`IAssetDisposalService`，D3 全套 + D7 过清理科目）

```csharp
public interface IAssetDisposalService
{
    Task<FinResult> CreateAsync(AssetDisposal d, string userId);   // 快照+算损益+解析科目（Draft）
    Task<FinResult> ConfirmAsync(Guid id, string userId);          // 补提+拼结转凭证→AutoPost→卡片 Disposed
    Task<FinResult> ReverseAsync(Guid id, string userId, string reason); // 红冲+恢复卡片 InUse
    Task<AssetDisposal?> GetAsync(Guid id);
    Task<List<AssetDisposal>> ListAsync(AssetDisposalStatus? status, Guid? assetCardId);
}
```

### 4.1 科目解析（按 DisposalType，用 `GlAccount.Role` 单例锚点）

| 类型 | 清理科目 ClearingAccount | 损益科目 GainLossAccount |
|---|---|---|
| Sale 出售 | `ASSET_CLEARING`(1606) | `ASSET_DISPOSAL_PL`(6115 资产处置损益) |
| Transfer 转让 | `ASSET_CLEARING`(1606) | `ASSET_DISPOSAL_PL`(6115) |
| Scrap 报废 | `ASSET_CLEARING`(1606) | 净损→`NON_OP_EXPENSE`(6711)；净收→`NON_OP_INCOME`(6301) |
| InventoryLoss 盘亏 | `PENDING_PROPERTY_LOSS`(1901) | `NON_OP_EXPENSE`(6711) |

### 4.2 CreateAsync

守卫资产在用、无非 Reversed 处置单（FA002）、期间开启（FA007）→ 快照 `OriginalValue/AccumulatedDepreciation`（取**当前卡片值**，处置月补提在 Confirm 完成）→ 解析清理/损益科目 → 暂存（Confirm 时再以补提后值重算净值/损益）。出售/转让但 `Proceeds>0` 而 `ReceiptBankAccountId` 空→FA010。

### 4.3 ConfirmAsync（当期处置补提 + 单次结转，D2/D7）

1. **处置月补提（D2 当期处置照提）**：若资产本处置期 `P` 尚无该资产的 Posted 折旧明细，则按 §3.1 计算其**处置月折旧额**，单独生成一条 `DepreciationEntry`（挂当期 Run；若无 Run 则建一个 CloseHook 单资产 Run）并入 `AccumulatedDepreciation`，记 `FinalDeprecEntryId`。**确保后续期 P 的批量折旧跳过该资产**（已处置/已提）。
2. **重算净值/损益**：以补提后 `AccumulatedDepreciation` 定 `NetBookValue` 与 `NetGainLoss`。
3. **拼单张结转凭证**（§5.2）→ `AutoPostAsync` → `Disposal.Status=Confirmed`+`JournalEntryId` → 卡片 `Status=Disposed`。

> **口径声明（用户采纳）**：当期处置**照提**处置月折旧（GAAP 严格、与批量折旧执行顺序无关）。补提折旧额走折旧费用科目（与月末折旧同口径），处置结转凭证仅含转销/价款/损益部分，二者解耦不重复。

### 4.4 ReverseAsync

守卫 `Status==Confirmed`→ 红冲结转凭证（与补提折旧凭证若独立则一并红冲）→ 卡片恢复 `Status=InUse`、回滚 `AccumulatedDepreciation`（扣补提）→ `Disposal.Status=Reversed`。

---

## 5. 自动凭证模板

凭证 `Source/VoucherDate(=业务日)/PeriodId(=FiscalPeriodId)/SourceDocNo(=单号，幂等)`，经 `AutoPostAsync` 校验配平后过账。

### 5.1 月末折旧汇总凭证（`Depreciation`，DEP-yyyy-MM-nnnnn）

借方按 `(DeprecExpenseAccountId, CostCenterId)` 分组分行，贷方按 `AccumDeprecAccountId` 分组：

```
借  折旧费用（制造/管理/销售费用）[按 费用科目×成本中心 分行]   Σ本期折旧
    贷  累计折旧(1602) [按累计科目分组]                          Σ本期折旧
```

### 5.2 处置结转凭证（`AssetDisposal`，FAD-yyyy-MM-nnnnn，单张轧平）

**出售/转让**（经 1606，损益→6115）：
```
借  累计折旧(1602)              AccumulatedDepreciation
借  固定资产清理(1606)          NetBookValue
    贷  固定资产(1601)                         OriginalValue
借  银行存款(收款行)            Proceeds + TaxAmount      [Proceeds>0]
    贷  固定资产清理(1606)                     Proceeds
    贷  应交税费—销项税额                       TaxAmount
借  固定资产清理(1606)          DisposalExpense           [Expense>0]
    贷  银行存款                               DisposalExpense
— 结转 1606 余额(=NetGainLoss) —
  NetGainLoss>0: 借 固定资产清理(1606) | 贷 资产处置损益(6115)   NetGainLoss
  NetGainLoss<0: 借 资产处置损益(6115) | 贷 固定资产清理(1606)   |NetGainLoss|
```
（1606 行内轧平为 0，凭证借贷平）

**报废**：同上，但损益结转入 `营业外支出(6711)`（损）/`营业外收入(6301)`（残料收益）；通常 `Proceeds=0/TaxAmount=0`。

**盘亏**（经 1901，损→6711）：
```
借  累计折旧(1602)              AccumulatedDepreciation
借  待处理财产损溢(1901)        NetBookValue
    贷  固定资产(1601)                         OriginalValue
借  营业外支出(6711)            NetBookValue
    贷  待处理财产损溢(1901)                   NetBookValue
```
（1901 行内轧平为 0）

### 5.3 处置月补提折旧凭证（§4.3，可选独立）

结构同 §5.1（单资产一行借折旧费用/一行贷累计折旧），`Source=Depreciation`，与处置凭证独立过账、独立可红冲。

---

## 6. 期间集成与后台 Worker（D4 手动 + Worker）

### 6.1 结账钩子（兜底，保证「结账即已计提」）

`PeriodCloseService.CloseAsync`（L60-64 `_reval` 钩子前）插入：
```csharp
if (_depreciation != null)
{
    var dr = await _depreciation.AccrueAsync(periodId, userId);  // 幂等：本期已 Posted 则 Pass
    if (!dr.Ok) return dr;                                       // 计提失败阻断结账
}
// …随后既有汇兑重估 _reval…
```
顺序：**折旧在汇兑重估之前**。`PreCloseCheckAsync` 增检「有在用资产但本期无 Posted 折旧批次」→ 由 `AccrueAsync` 自动补，不硬阻断。

### 6.2 `AssetDepreciationWorker`（月末备草稿，不自动过账）

仿 `FinReconciliationWorker` + `TenantScopeRunner.ForEachTenantAsync`：每日检查，若当前开启期为月末且无本期折旧批次，则 `RunAsync(…, Worker)` **生成 Draft 草稿**（不过账），日志/审计提示待复核。**Worker 不自动 Post**——过账权交人（手动复核）或结账钩子兜底。注册：`Program.cs` `AddHostedService<AssetDepreciationWorker>()`。

> 三路统一：**手动**(Run→复核→Post 任意时点) + **Worker**(月末备草稿) + **结账钩子**(兜底 Run+Post)，既自动又留复核权与审计链。

---

## 7. API / 控制器（`CP6.WebApi/Controllers/Fin/`）

| 控制器 | 端点 | 权限点 |
|---|---|---|
| `AssetCategoryController` | GET list/{id}、POST、PUT/{id}、DELETE/{id} | `fin-asset-category`(view/add/edit/delete) |
| `AssetCardController` | GET list/{id}、POST(建卡)、PUT/{id}、POST/{id}/activate(草稿→在用)、GET/{id}/schedule(折旧计划) | `fin-asset-card`(view/add/edit/activate) |
| `AssetDepreciationController` | GET preview?periodId、POST run、PUT entry/{id}/workload、POST/{id}/post、POST/{id}/reverse、GET list/{id} | `fin-asset-deprec`(view/run/post/reverse) |
| `AssetDisposalController` | GET list/{id}、POST(建单)、POST/{id}/confirm、POST/{id}/reverse | `fin-asset-disposal`(view/add/confirm/reverse) |

范式：`[Authorize]`+`[RequirePermission("module","action")]`，返回 `Ok2(data)`/`Fin(FinResult)`，`CurrentUser` 取操作人（对齐 `JournalEntryController` L36-59）。

---

## 8. 并发 / 事务 / 守卫

- **8.1 过账事务**：折旧 Post / 处置 Confirm 在单事务内「拼凭证 + AutoPost + 回写卡片/批次状态」，失败整体回滚（不留半过账）。
- **8.2 幂等**：折旧批次 `JournalEntryId`、处置单 `JournalEntryId` 为幂等键；`AccrueAsync` 本期已 Posted 直接 Pass。重复 Run 受「每期一非 Reversed 批次」守卫（FA003）。
- **8.3 乐观并发**：5 实体均显式 `RowVersion`（`[Timestamp]`）；卡片回写累计折旧、批次/处置状态流转校验 RowVersion，冲突 → 并发错误。
- **8.4 锁后守卫**：折旧/处置凭证落期受 `FiscalPeriodService` 约束——期间已结账（Closed）则 `AutoPostAsync` 拒绝（FA007），不可对已结账期补提/处置。
- **8.5 反冲守卫**：仅 `Posted`/`Confirmed` 可反冲（FA009）；反冲后批次/处置置 Reversed，卡片状态/累计原子回滚。

---

## 9. 科目与 Seed

### 9.1 GlAccount Seed（确保存在 + 设 Role 锚点）

| Code | Name | Type | Role | 备注 |
|---|---|---|---|---|
| 1601 | 固定资产 | Asset | `FIXED_ASSET` | 已留，补 Role |
| 1602 | 累计折旧 | Asset(备抵) | `ACCUM_DEPRECIATION` | 已留，补 Role |
| 1606 | 固定资产清理 | Asset | `ASSET_CLEARING` | 新增 |
| 1901 | 待处理财产损溢 | Asset | `PENDING_PROPERTY_LOSS` | 新增 |
| 5101 | 制造费用 | Expense | —（分类路由） | 机器设备折旧费用 |
| 6602 | 管理费用 | Expense | —（分类路由） | 办公/管理资产折旧费用 |
| 6601 | 销售费用 | Expense | —（分类路由） | 销售用资产折旧费用 |
| 6115 | 资产处置损益 | P/L | `ASSET_DISPOSAL_PL` | 出售/转让损益 |
| 6711 | 营业外支出 | Expense | `NON_OP_EXPENSE` | 报废损失/盘亏 |
| 6301 | 营业外收入 | Revenue | `NON_OP_INCOME` | 报废残料收益 |

> 折旧费用科目（5101/6602/6601）**非单例 Role**——由 `AssetCategory.DeprecExpenseAccountId` 路由，故不设 Role，仅确保 Code 存在供分类引用。

### 9.2 AssetCategory Seed（默认分类，可选 demo）

房屋建筑物（直线/240月/3%）、机器设备（直线/120月/5%，费用→5101）、运输设备（直线/60月/5%，费用→6602）、电子设备（直线/36月/3%，费用→6602）、办公设备（直线/60月/5%，费用→6602）。

---

## 10. 菜单 / 权限 / i18n

- **菜单**（Fin 600 组，614 留 A4）：`nav.615 资产分类` / `nav.616 资产卡片` / `nav.617 折旧计提` / `nav.618 资产处置`（确切号落码核实）。
- **权限点**：`fin-asset-category` / `fin-asset-card` / `fin-asset-deprec` / `fin-asset-disposal`（各含 view/add/edit/post/reverse 等动作）；`[RequirePermission]` 贴端点 + Seed 授权 admin。资源键派生 MenuKey（沿财务模块约定）。
- **i18n**（追加 `I18nFinScreenSeed` 或新建 `I18nAssetScreenSeed`，五语 zh-CN/zh-TW/en/ja/vi）：菜单/枚举（折旧方法·资产状态·处置类型·批次状态）/字段标签/错误码 FA001~FA010。代码只放 key，语义点分 key（沿 i18n 三铁律）。

---

## 11. 错误码（FinResult Code）

| 码 | 含义 |
|---|---|
| FA001 | 资产分类账务科目未配置（建卡/计提解析失败） |
| FA002 | 资产已处置或已有进行中处置单，不可重复操作 |
| FA003 | 本期已存在折旧批次（幂等/重复计提） |
| FA004 | 资产尚未起折（起折期间晚于当前期间） |
| FA005 | 累计折旧已达可折旧上限（无可提额） |
| FA006 | 处置净值/损益计算异常（数据不一致） |
| FA007 | 会计期间未开启或已结账 |
| FA008 | 工作量法缺预计总工作量或本期工作量未录 |
| FA009 | 批次/处置需先反冲（状态非 Posted/Confirmed 不可再过账） |
| FA010 | 出售/转让有价款但未指定收款银行账户 |

---

## 12. 审计

关键操作写 `Sys_OperLog`：建卡/启用/变更、折旧 Run/Post/Reverse、处置 Create/Confirm/Reverse（记单号、资产号、金额、操作人、时点）。凭证经 `AutoPostAsync` 自带凭证审计链。

---

## 13. 测试（分层：InMemory + SQLite，末加 gstack QA）

**单元（`IDepreciationCalculator`，纯函数）**
1. 直线法均摊 + 末期取整补足残差至「原值−残值」。
2. 双倍余额递减：前期按双倍率、末两年自动切直线、不破残值。
3. 年数总和：年序加权、跨年月额、封顶。
4. 工作量法：按本期/总量比例；缺总量→FA008。
5. 残值封顶：累计永不超「原值−残值」。

**服务（InMemory）**
6. 次月起折：当期增加不提、起折期=购置次月。
7. 当期处置照提：处置月补提、后续期跳过。
8. RunAsync 幂等：重复 Run→FA003。
9. PostAsync 汇总凭证：借方按费用科目×成本中心分行、贷累计折旧、借贷平、回写卡片累计/期数/状态。
10. ReverseAsync：红冲 + 卡片累计/期数原子回滚。
11. 四类处置凭证逐类轧平（1606/1901 行内净零、借贷平）+ 损益方向（收益/损失）。
12. AccrueAsync 结账钩子兜底：未计提→自动 Run+Post；已计提→幂等 Pass。
13. 成本中心派生序（卡片>机台>部门>null）；费用科目覆盖序（卡片>分类）。

**集成（SQLite）**
14. RowVersion 乐观并发冲突（并发回写累计折旧）。
15. 唯一索引（AssetNo/分类 Code/批次每期单批/处置每资产单单）。
16. 已结账期拒过账（FA007）。

**gstack 真浏览器 QA**：分类建档 → 建卡（含工作量法/期初建卡）→ 启用 → 折旧 Preview/Run/Post → 查资产级明细 → 反冲 → 处置（四类各一）→ 反冲 → 结账钩子兜底验证。

---

## 14. 决策记录（brainstorming 定稿）

| 决策 | 选择 | 理由 |
|---|---|---|
| **A3-D1 折旧方法** | 四法（直线/双倍余额递减/年数总和/工作量法） | 覆盖常规 + 机器加速折旧（税务激励）+ 产量型，计算可插拔成本低 |
| **A3-D2 起折/停折** | 次月起折、次月停折（当期增加不提、当期处置照提） | 中国会计准则标准口径 |
| **A3-D3 处置范围** | 全套（出售/报废/转让/盘亏） | 可售完整 ERP |
| **A3-D4 计提模式** | 手动 + Worker（备草稿）+ 结账钩子（兜底） | 既自动又留会计复核权 |
| **A3-D5 资产分类** | 独立 `AssetCategory`，驱动默认值 + 三科目路由 | 默认值统一 + 折旧费用按类路由（制造/管理/销售） |
| **A3-D6 凭证粒度** | 每期一张汇总凭证（费用科目×成本中心分行）+ `DepreciationEntry` 资产级明细追溯 | 凭证干净 + 单资产可查 |
| **A3-D7 处置入账** | 过清理科目（1606/1901）· 单次确认结转 | 账务正确且不繁，价款/费用确认时一并录 |
| **A3-D8 费用归属** | 分类默认 + 卡片覆盖 + 机台派生成本中心 | 灵活路由，复用 `CostCenter.LinkMachineId` |
| **A3-D9 凭证生成** | 直接拼 `JournalEntry`→`AutoPostAsync`（仿 FxReval），**不走 PostingRule** | 汇总/变结构凭证不契合 FinBizEvent，直建更清晰、零引擎改造 |

---

## 15. 范围外（YAGNI，本期不做）

资产减值准备（1603）/重估；后续资本化支出（改良/大修资本化）；租赁资产（使用权资产）；卡片大变更工作流（仅做成本中心/部门/责任人轻量调整）；工作量法 MES 产量自动回写（本期工作量手工录入）；折旧计划表 PDF 导出；资产标签/二维码实物盘点；多准则（IFRS 组件折旧）；月中购置按天折旧（统一月口径）。

---

*生成于 2026-06-19。现状据财务 GL/自动凭证/期间结账/成本中心/MES Machine 真实代码探查（Explore 逐行锚定，见 §1）。下一步：writing-plans 出实施计划。*
