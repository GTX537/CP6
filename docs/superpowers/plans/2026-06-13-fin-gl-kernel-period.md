# 财务 总账内核 + 期间结账（章01+02）Implementation Plan（初稿）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **工作流（丛书模式）**：我出初稿 → 你修订 → 我评审合并定稿后再编码。**财务第一份计划**（共三份）。地基章，无前置依赖（AP/AR/成本都从这里长出来）。

**Goal:** 浇出财务模块的地基——章01 总账内核（会计科目表 `GlAccount` + 多国别模板包 + 凭证 `JournalEntry/Line` + **借贷恒等校验** + **maker-checker 双人复核状态机** + **红冲**）+ 章02 会计期间（`FiscalPeriod` + 财年起始月可配 + **试算平衡表三栏** + 月结锁期）。完成后：手工录一张平衡凭证存得进查得出、不平衡被拒、过账后改不动只能红冲、一个月凭证能结平能锁、锁后记不进去。

**Architecture:** 落 `Fin` 命名空间（`CP6.Entity/DomainModels/Fin`、`CP6.Core/Services/Fin`、`CP6.WebApi/Controllers/Fin`、`cp6.web/src/views/fin`）。两条会计铁律是硬约束：①借贷恒等（每张凭证 Σ借=Σ贷，`ValidateBalance` 不可绕过）②凭证不可改不可删，只能红冲（`JournalEntry` 无 Update/Delete，只有 `Post`/`Reverse`）。手工凭证强制 maker-checker（过账人≠制单人）、自动凭证可信直过（`AutoPosted`，留给 Plan 2 自动凭证引擎用）。科目按 `Role` 角色锚点跨模板恒定（换国别模板包零改动）。金额全 `decimal`。试算平衡表三栏（期初+本期+期末），不平=数据完整性 bug→告警。

**Tech Stack:** .NET 8 + EF Core 8 + SQL Server / xUnit + EF Core InMemory / Vue 3.5 + element-plus。源文档：`docs/finance/01·02`（引用总纲两条铁律）。

---

## 关键前置决策（待你修订时确认）

| # | 议题 | 文档原意 | 现状/对账 | **本稿建议值** |
|---|---|---|---|---|
| **F-D1** | **TenantId** | 全表 TenantId（doc 写 `int TenantId`） | 零多租户 | 本阶段不引入 TenantId（同 Space/PUB/OA）；**统一在 OA Plan 3 章10 系统级多租户做**（届时 Fin 表也接 `BaseTenantEntity`）。doc 的 `int TenantId` 字段本阶段不加 |
| **F-D2** | **审计字段** | doc 写 BaseEntity 含 Id/CreateTime/CreateBy | 真实 `Creator/CreateDate/Modifier/ModifyDate` | 以代码为准；Fin 实体继承 `BaseEntity` |
| **F-D3** | **凭证采番** | doc "仿 MesSequence 建 FinSequence"（GL-年-月-流水） | 有 `IMesSequenceService.NextAsync(seqKey,date)` + `DocNumber` | **新建 `IFinSequenceService`**（仿 MesSequence，按"GL-yyyy-MM-流水"采番，并发安全）；或复用 DocNumber 改格式。建议独立 FinSequence（财务号格式特殊） |
| **F-D4** | **模板包 seed 范围** | CN-GAAP 全量(~70) + INTL 映射；JP/US 路线图 | — | 本计划落 **CN-GAAP + INTL 两套 seed**（按 `StandardScheme` 选一套导入），JP/US 留路线图。科目 `Role` 锚点必填（自动凭证 Plan 2 靠它） |
| **F-D5** | **余额结转** | 实时滚算 vs 快照表 | — | **MVP 实时滚算**（不存期初，查开账至今已过账凭证累计），百万行级再加 `PeriodBalance` 快照（YAGNI） |
| **F-D6** | **权限点** | 制单/复核拆两权限点（maker-checker） | PUB B1 权限引擎 | 制单/过账/结账/反结账独立权限点；接 PUB B1 `[RequirePermission]`（PUB 落地后），本阶段先 `[Authorize]` + 服务层校验 maker≠checker |

> **测试基建**：xUnit + InMemory。借贷恒等/maker-checker/红冲/试算三栏/锁期可纯单测（核心价值，doc 已给代码）。`decimal` 金额相等校验务必单测（防 double 误差）。

---

## File Structure

### 章01 总账内核（`CP6.Entity/DomainModels/Fin` + `Services/Fin`）
- `GlAccount.cs`(+ `AccountType`/`AccountSide` 枚举)、`CostCenter.cs`(+ `CostCenterType`)、`JournalEntry.cs`(+ `VoucherSource`/`JournalStatus`)、`JournalLine.cs`
- `IGlAccountService.cs`/`GlAccountService.cs`（科目 CRUD + 模板导入）
- `IJournalEntryService.cs`/`JournalEntryService.cs`（ValidateBalance/Submit/Post/AutoPost/Reverse/Reject）
- `IFinSequenceService.cs`/`FinSequenceService.cs`（凭证采番）
- seed：`fin-coa-cn-gaap-seed.sql`、`fin-coa-intl-seed.sql`（或 C# seeder）

### 章02 期间结账
- `FiscalPeriod.cs`(+ `PeriodStatus`)；`IFiscalPeriodService.cs`/`FiscalPeriodService.cs`（期间生成/IsOpen/Close/Reopen/EnsureOpen/Previous）
- `ITrialBalanceService.cs`/`TrialBalanceService.cs`（三栏试算 + 平衡校验）
- 公司设置 `FiscalYearStartMonth`（系统配置）

### 控制器 + DI + 迁移 + 前端 + 测试
- `Controllers/Fin/{GlAccountController,JournalEntryController,PeriodController,TrialBalanceController}.cs`
- 迁移 `*_FinGlKernel`（GlAccount/CostCenter/JournalEntry/JournalLine/FiscalPeriod 5 表）
- `cp6.web/src/views/fin/{GlAccountView,JournalEntryView,PeriodCloseView,TrialBalanceView}.vue`
- 测试：`JournalEntryServiceTests`（★恒等/maker-checker/红冲）、`TrialBalanceServiceTests`（★三栏/平衡）、`FiscalPeriodServiceTests`（锁期/不跳月）

---

## 实施分三阶段

- **Phase A**（A-1..A-2）：科目表 + 模板包 seed（章01 §1-3）
- **Phase B**（B-1..B-3）：凭证 + 借贷恒等 + maker-checker + 红冲 + 采番（章01 §4-6）★
- **Phase C**（C-1..C-3）：会计期间 + 试算平衡三栏 + 月结锁期（章02）

---

# Phase A — 科目表 + 模板包

## Task A-1: GlAccount + CostCenter 实体 + 迁移（章01 §2/§4.1.1）

**Files:** Create `GlAccount.cs`, `CostCenter.cs`; Modify `CP6Context.cs`; migration; Test `GlAccountServiceTests.cs`（落库往返 + 末级/停用校验）

- [ ] **Step 1: 失败测试**（科目落库；只有 IsLeaf 能记账[B 阶段用]；Code 唯一）`[InMemory]`
- [ ] **Step 2: 跑红 → Step 3: 写实体**（照 01 §2/§4.1.1：GlAccount[Code/Name/Type/NormalSide/ParentId/Level/IsLeaf/IsControl/SubLedgerType/RequirePartner/**Role**/StandardScheme/IsActive/CurrencyCd]，**去掉 doc 的 int TenantId**[F-D1]，继承 BaseEntity；CostCenter[Code/Name/Type/ParentId/LinkMachineId→MES Machine/IsActive]；枚举 AccountType/AccountSide/CostCenterType）+ DbSet + 索引（Code 唯一、Role、ParentId）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(fin): GlAccount + CostCenter entities + migration (ch01 §2)"`

## Task A-2: GlAccountService + 多国别模板 seed（章01 §3）

**Files:** Create `IGlAccountService.cs`/`GlAccountService.cs`, seed `fin-coa-cn-gaap-seed`/`fin-coa-intl-seed`; Test

- [ ] **Step 1: 失败测试**（导入 CN-GAAP 模板→~70 科目入库且 Role 锚点正确[AP_CONTROL=2202/AR_CONTROL=1122/REVENUE=4001/COGS=5001...]；INTL 同 Role 不同 Code[AP_CONTROL=2100]）
- [ ] **Step 2: 跑红 → Step 3: 实现**（GlAccountService：CRUD[科目不删只停用]、ImportTemplate(scheme)；seed 按 01 §3.2 CN-GAAP 全量 + §3.3 INTL 映射，**Role 锚点必填**——控制科目 AP_CONTROL/AR_CONTROL/INVENTORY/WIP/FG/TAX_INPUT/TAX_OUTPUT/REVENUE/COGS/DIRECT_MATERIAL/DIRECT_LABOR/MFG_OVERHEAD/FX_GAIN/FX_LOSS/EQUITY_CAPITAL/RETAINED_EARNINGS）
- [ ] **Step 4: 跑绿 → Step 5: GlAccountController + 科目表维护 UI + 提交** → `git commit -m "feat(fin): GL account service + CN-GAAP/INTL templates (role-anchored) (ch01 §3)"`

---

# Phase B — 凭证 + 借贷恒等 + maker-checker + 红冲（章01 §4-6）★

## Task B-1: JournalEntry/Line 实体 + FinSequence + 迁移（章01 §4）

**Files:** Create `JournalEntry.cs`, `JournalLine.cs`, `IFinSequenceService.cs`/`FinSequenceService.cs`; Modify `CP6Context.cs`; migration

- [ ] **Step 1-3: 写实体**（照 01 §4：JournalEntry[No/VoucherDate/PeriodId/Source/SourceDocNo/Status/Description/MakerId/MakerAt/CheckerId/CheckerAt/RejectReason/AutoPosted/ReversedById/ReverseOfId/Lines]，去 int TenantId；JournalLine[EntryId/LineNo/AccountId/**Debit/Credit decimal**/PartnerId/CostObjectType/CostObjectId/CostCenterId/CurrencyCd/FxRate/OrigAmount/Memo]；VoucherSource/JournalStatus 枚举）+ FinSequenceService（仿 MesSequence，"GL-yyyy-MM-{seq:D5}"，并发安全）+ DbSet + 索引（No/PeriodId/Status）
- [ ] **Step 4-5: 迁移 + 提交** → `git commit -m "feat(fin): JournalEntry/Line entities + FinSequence (ch01 §4)"`

## Task B-2: ValidateBalance 借贷恒等 + Submit/Post/AutoPost maker-checker（章01 §5/§6）★★

**Files:** Create `IJournalEntryService.cs`/`JournalEntryService.cs`; Test `JournalEntryServiceTests.cs`

- [ ] **Step 1: 失败测试（★铁律 1+2）**

```csharp
public class JournalEntryServiceTests
{
    [Fact] public void Validate_Unbalanced_Fails()
    {   // 借100/贷90 → 借贷不平拒绝
        var e = Entry(Line(acc1, debit:100), Line(acc2, credit:90));
        Assert.False(Svc().ValidateBalance(e).Ok);
    }
    [Fact] public void Validate_NonLeafAccount_Fails() { /* 记非末级科目→拒 */ }
    [Fact] public void Validate_RequirePartner_Missing_Fails() { /* 应收应付科目无往来单位→拒 */ }
    [Fact] public void Validate_DecimalPrecision_NoFloatError()
    {   // 借 0.1+0.2 / 贷 0.3 → decimal 相等通过（double 会失败）
        var e = Entry(Line(a,debit:0.1m), Line(a,debit:0.2m), Line(b,credit:0.3m));
        Assert.True(Svc().ValidateBalance(e).Ok);
    }
    [Fact] public async Task Post_MakerEqualsChecker_Fails()
    {   // 制单人==过账人 → maker-checker 拒
        var id = await SubmitDraft(maker:"u1");
        Assert.False((await Svc().PostAsync(id, checkerId:"u1")).Ok);
    }
    [Fact] public async Task Post_DifferentChecker_Succeeds() { /* checker≠maker → Posted */ }
    [Fact] public async Task AutoPost_ManualSource_Rejected()
    {   // 手工来源不能直过
        Assert.False((await Svc().AutoPostAsync(Entry(Source.Manual))).Ok);
    }
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 01 §5/§6 逐字：ValidateBalance[至少两行/借贷二选一/非负/末级/启用/RequirePartner/**Σ借=Σ贷 decimal**]；SubmitForReview[Draft→PendingReview 过校验]；PostAsync[PendingReview + checker≠maker + 期间 Open + 再校恒等 → Posted]；AutoPostAsync[非 Manual + 校验 + Open → Posted/AutoPosted，给 Plan 2 用]；RejectAsync[→ Draft/Rejected + 原因]；**无 Update/Delete 接口**）
- [ ] **Step 4: 跑绿 + 提交** → `git commit -m "feat(fin): balance validation + maker-checker post/autopost (ch01 §5/§6)"`

## Task B-3: 红冲 ReverseAsync（章01 §6）★

**Files:** Modify `JournalEntryService.cs`; Test

- [ ] **Step 1: 失败测试**（红冲已过账凭证→生成借贷对调反向凭证、原凭证→Reversed、互指 ReversedById/ReverseOfId；autoPost=true 直过/false 待复核；非 Posted 不能红冲）

```csharp
[Fact] public async Task Reverse_CreatesSwappedEntry_AndFreezesOrigin()
{
    var id = await PostEntry(debit:acc1=100, credit:acc2=100);
    var r = await Svc().ReverseAsync(id, "u1", "记错", autoPost:false);
    var origin = await Get(id); var rev = await GetByReverseOf(id);
    Assert.Equal(JournalStatus.Reversed, origin.Status);
    Assert.Equal(100, rev.Lines.Single(l=>l.AccountId==acc1).Credit);  // 借贷对调
    Assert.Equal(JournalStatus.PendingReview, rev.Status);             // 手工红冲走复核
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 01 §6 ReverseAsync：只 Posted 可红冲；新建 Reversal 凭证[Debit↔Credit 对调，保留 Partner/CostObject/CostCenter]；autoPost 决定直过/待复核[红冲跟随触发源]；原凭证 Status=Reversed + 互指）
- [ ] **Step 4: 跑绿 → Step 5: JournalEntryController（录入/提交/过账/驳回/红冲，无改删）+ 凭证录入 UI + 提交** → `git commit -m "feat(fin): voucher reversal (red-ink, no update/delete) (ch01 §6)"`

---

# Phase C — 会计期间 + 试算平衡 + 月结锁期（章02）

## Task C-1: FiscalPeriod + 财年起始月 + 期间服务（章02 §1）

**Files:** Create `FiscalPeriod.cs`, `IFiscalPeriodService.cs`/`FiscalPeriodService.cs`; Modify `CP6Context.cs`; migration; Test `FiscalPeriodServiceTests.cs`

- [ ] **Step 1: 失败测试**（凭证 VoucherDate 归期[Year/Month]；FiscalYearStartMonth=4→2026-04 是 FiscalYear2026/PeriodNo1、2027-03 是 FiscalYear2026/PeriodNo12；IsOpenAsync）
- [ ] **Step 2: 跑红 → Step 3: 实现**（实体 FiscalPeriod[FiscalYear/Year/Month/PeriodNo/PeriodStart/PeriodEnd/Status/ClosedAt/ClosedBy]，去 int TenantId；FiscalPeriodService：按 FiscalYearStartMonth[公司配置] 算 FiscalYear/PeriodNo；ResolvePeriod(date)、IsOpenAsync、EnsureOpenAsync、PreviousAsync）
- [ ] **Step 4: 跑绿 → Step 5: 迁移 + 提交** → `git commit -m "feat(fin): FiscalPeriod + configurable fiscal year start (ch02 §1)"`

## Task C-2: TrialBalanceService 三栏试算（章02 §2）★

**Files:** Create `ITrialBalanceService.cs`/`TrialBalanceService.cs`; Test `TrialBalanceServiceTests.cs`

- [ ] **Step 1: 失败测试（★三栏 + 平衡）**

```csharp
[Fact] public async Task TrialBalance_ThreeColumns_OpeningIncludesHistory()
{
    // 5月过账：应收账款 借20000；6月过账：应收 借5000
    // 6月试算：应收 期初=20000(含5月)、本期借=5000、期末=25000  ★期初含历史
    var tb = await Svc().BuildAsync(june);
    var ar = tb.Rows.Single(r => r.Code == "1122");
    Assert.Equal(20000, ar.OpenBal); Assert.Equal(5000, ar.PeriodDebit); Assert.Equal(25000, ar.CloseBal);
}
[Fact] public async Task TrialBalance_Balanced_BothLevels()
{
    var tb = await Svc().BuildAsync(period);
    Assert.True(tb.MovementBalanced);   // Σ本期借 == Σ本期贷
    Assert.True(tb.ClosingBalanced);    // Σ借方余额 == Σ贷方余额
}
```

- [ ] **Step 2: 跑红 → Step 3: 实现**（照 02 §2.3：期初=VoucherDate<PeriodStart 的已过账累计[★含历史]、本期=本期间已过账、期末=期初+本期；按 NormalSide 带号；MovementBalanced + ClosingBalanced 两层校验；不平→`DeadLetterNotifier`/SignalR 告警[复用现有]）
- [ ] **Step 4: 跑绿 → Step 5: TrialBalanceController + 试算表 UI（三栏）+ 提交** → `git commit -m "feat(fin): three-column trial balance + imbalance alert (ch02 §2)"`

## Task C-3: 月结锁期 PreCloseCheck/Close/Reopen（章02 §3）

**Files:** Modify `FiscalPeriodService.cs`; Test

- [ ] **Step 1: 失败测试**（结账前检查：有未过账凭证→拒/试算不平→拒/上期未结→拒[不跳月]；Close→Status=Closed + 下期 EnsureOpen；锁后凭证落该期被拒[B-2 PostAsync 已挡]；Reopen 限高权限+留痕）
- [ ] **Step 2: 跑红 → Step 3: 实现**（照 02 §3：PreCloseCheckAsync[未过账数/试算平/上期已结]；CloseAsync[Status=Closed+ClosedBy/At+EnsureOpen 下期]；ReopenAsync[高权限+Sys_OperLog 留痕]）
- [ ] **Step 4: 跑绿 → Step 5: PeriodController（结账/反结账/期间列表）+ 月结 UI + DI 全装配 + 提交** → `git commit -m "feat(fin): period close/reopen with checklist + lock (ch02 §3)"`

---

## Self-Review（对照章01/02 覆盖）

- **章01**：科目表五大类+NormalSide+控制科目(A-1) ✅ / 多国别模板 Role 锚点(A-2) ✅ / JournalEntry/Line decimal(B-1) ✅ / 借贷恒等校验(B-2) ✅ / maker-checker 状态机(B-2) ✅ / 自动凭证直过(B-2，给 Plan 2) ✅ / 红冲不改不删(B-3) ✅ / 末级/RequirePartner 校验(B-2) ✅ / 凭证采番(B-1) ✅ / 成本中心维度(A-1) ✅
- **章02**：会计期间+财年起始月(C-1) ✅ / 试算三栏期初含历史(C-2) ✅ / 两层平衡校验(C-2) ✅ / 月结锁期(C-3) ✅ / 不跳月+未过账拦截(C-3) ✅ / 反结账留痕(C-3) ✅ / 实时滚算余额(C-2，F-D5) ✅

**已知缺口/推迟（已标注）：**
1. **TenantId**（F-D1）—— OA Plan 3 章10 系统级多租户统一。
2. **JP/US 模板包**（F-D4）—— 路线图，CN-GAAP/INTL 先落。
3. **年结清损益**（章02 §4）—— 阶段5/年结，科目 3103/3104 已预留。
4. **PeriodBalance 快照**（F-D5）—— 百万行级再做。
5. **自动凭证引擎**（章05）—— Finance Plan 2（AutoPostAsync 接口本计划已备）。
6. **PUB 权限点接入**（F-D6）—— PUB B1 落地后 `[RequirePermission]`，本阶段服务层校验 maker≠checker。

**Type 一致性：** `GlAccount.Role`(A-1/A-2) 给 Plan 2 自动凭证按角色找科目；`JournalEntryService.AutoPostAsync`(B-2) 给 Plan 2 自动凭证；`ValidateBalance`(B-2) 被 Post/AutoPost/Reverse 共用；`IFiscalPeriodService.IsOpenAsync`(C-1) 被 PostAsync(B-2) 锁期；`TrialBalance` 三栏(C-2) 被 CloseAsync 预检(C-3) 用。

---

## 执行交接

计划存 `docs/superpowers/plans/2026-06-13-fin-gl-kernel-period.md`。**财务第一份（总账地基）**。后续：
- Finance Plan 2 = `2026-06-13-fin-ap-ar-voucher.md`（章03 AP★MVP + 章04 AR + 章05 自动凭证引擎，接 BridgeHook）
- Finance Plan 3 = `2026-06-13-fin-cost-statements.md`（章06 成本 + 07 多币种 + 08 报表 + 09 集成 + 10 完整性审计）

**下一步按工作流是你修订**（拍板 F-D1~D6）。定稿后执行：**总账地基** → AP/AR/自动凭证 → 成本/报表；AP（Plan 2 阶段2）是 MVP 价值点，但必须先有本计划的总账地基（凭证落 GL）。

---

*初稿生成于 2026-06-13。源：docs/finance/01·02（文档已含可落码代码）。已勘察：零多租户(TenantId 延 OA章10)、BaseEntity 审计字段、MesSequence 采番范式、MES Machine(成本中心 LinkMachineId)、DeadLetterNotifier/SignalR/Sys_OperLog 现成、EF Migrations、folder=namespace Fin。*
