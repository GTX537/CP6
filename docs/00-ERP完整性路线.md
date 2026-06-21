# CP6 ERP 完整性路线（✅ 五项全收官）

> **生成于 2026-06-17；2026-06-21 全部完成。** 核心闭环（销售→生产→库存→采购→财务 + OA 审批 + PUB 权限组织 + 多租户 + i18n）+ 本文所列 **5 项 ERP 完整性高价值缺口（A1~A5）现已全部全栈落地上 main**（115 控制器 / 919 测试 +1skip / 经 gstack 真浏览器 QA）。**单租户完整制造业 ERP 完整性达成。**
>
> 本文原为"待打标记的缺口路线"，现转为**收官记录**：下表与第三章已标 ✅ 并附落地数据；第二章"推荐顺序"保留为实际执行回顾；第四章改为**下一阶段方向**。
>
> 配套：`docs/00-功能盘点.md`（全量功能/缺口）、`docs/00-执行计划总盘.md`（既有 16 份计划与波次）。各缺口落地详情见各子文件：A1 计划/spec、A2 工艺路线、A3 固定资产、A4 银行对账、A5 预算（项目记忆均有对应条目）。

---

## 一、五项缺口 ✅ 全部已完成一览

| 项 | 性质 | 价值 | 已实现内容 | 落地数据 | 状态 |
|---|---|---|---|---|---|
| **A1 MRP 净需求** | 新建（独立 `Plan` 模块） | ★★★★★ 制造业"大脑" | 受注/预测 × BOM 展开 − 库存 − 在途 − 在制 → 建议采购/生产；低层码净需求引擎 + 计划看板 + 人确认转 PR(桩)/工单 | 790 测试 | ✅ 上 main |
| **A2 工艺路线完善** | 扩展现有 + 少量新建 | ★★★★ 成本"做真" + CRP 前置 | 工序级标准工时 × 工序费率 → 成本做真；工时采集(`WorkOrderProcess`)/工时差异 + CRP 地基 | 807 测试 | ✅ 上 main |
| **A3 固定资产** | 新建 | ★★★★ 机器多的纸箱厂必备 | `FixedAsset`/`DepreciationSchedule`/`AssetDisposal` + 四法折旧 + 全套处置 + 月末折旧 Worker → 自动凭证（复用引擎） | gstack QA 过 | ✅ 上 main |
| **A4 银行对账** | 扩展 | ★★★★ 月结最后一块 | `BankStatement`/`BankTransaction` 流水导入 + 流水↔银行 GL 自动撮合 + 双向调节表 + 锁后守卫 | 879 测试 | ✅ 上 main |
| **A5 预算/管理会计** | 新建 | ★★★ 老板经营管控 | `Budget`/`BudgetVersion`/`BudgetLine`(科目×成本中心×期间) + 按月分解 + OA 审批 + 预算 vs 实际 + 可选过账控制(BudgetGuard) | 919 测试 +1skip | ✅ 上 main |

> **收官（2026-06-20~21）**：A1 先做（最高价值 + 唯一码就绪），A2–A5 各自 `brainstorming → spec → writing-plans → 编码` 全栈落地，均经 gstack 真浏览器 QA。**A 类五缺口全收官 = 单租户 ERP 完整性达成。**

---

## 二、执行顺序（实际回顾·✅ 已全部完成）

**实际即按此推进：主线先做 A1（最高价值 + 唯一码就绪），财务线 A4→A3→A5 顺序收口，A2 制造精度线穿插完成。**

```
Tier 1（先做，计划现成）
  └─ A1 MRP P1 净需求地基  ──► 后续 MRP P2 预测 / P3 MPS / P4 CRP（CRP 需 A2 工时）

Tier 2 · 两条独立支线（A1 之后，可并行；各项互不依赖）
  ├─ 制造精度线：A2 工艺路线完善（标准工时/费率/工时采集）──► 支撑 MRP P4 CRP + 标准成本做真
  └─ 财务完整线：A4 银行对账（最小，月结收尾） → A3 固定资产（合规必备） → A5 预算（管理增值）
```

**为什么这样排**
1. **A1 第一**：价值最高（被动缺料 → 前瞻净需求，采购 PR 从"反流"升级为"算出来"），且是唯一计划/数据现成的——开工成本最低、回报最大。
2. **A2 紧随制造线**：让已实现的成本会计从"工费估算"变"工序真实成本"，并铺好 CRP（MRP 套件 P4）的工时地基。
3. **财务线 A4→A3→A5 独立并行**：A4 最小（扩展、复用核销框架），先收尾月结；A3 资产折旧合规必备；A5 预算是管理增值、依赖 GL+CostCenter（均成熟）。三者与 A1/A2 无依赖，可按你客户/资源拉动重排。

---

## 三、各项落地详情（✅ 全部已完成，附实际完成序）

### A1 · MRP 净需求地基　【实际完成序：① ✅】
- [x] ✅ **已完成（全栈上 main，gstack QA 过）**
- **产出**：`Plan` 新模块（用量内核下沉 `IMaterialUsageCalculator` + `ProductMaterial` 补单耗 + `Plan_ItemPlanningPolicy` + 低层码净需求引擎 + 计划看板 + 人确认转 PR/工单）
- **就绪**：✅ 计划 `docs/superpowers/plans/2026-06-13-plan-p1-mrp-foundation.md` + spec `docs/superpowers/specs/2026-06-13-mrp-planning-suite-design.md`（决策 MP-D1~D6，MP-D2/D6 已确认无前置缺口）
- **开工动作**：直接修订定稿该 plan → `subagent-driven-development` 逐 Task 落地
- **后续**：P2 预测 / P3 MPS / P4 CRP（待出计划；P4 CRP 依赖 A2 工序工时）

### A2 · 工艺路线完善（标准工时 + 工序费率 + 工时采集）　【实际完成序：② ✅】
- [x] ✅ **已完成（全栈上 main，gstack QA 过）**
- **产出**：`ProductProcess` 加 `StandardHour`/`CycleTime`/工序依赖；新建工序费率（`ProcessCostRate` 或字段）；`WorkOrderProcess` 加 `ActualWorkingHour`(MES 采集)；`CostCollectService` 加工时差异行
- **现状**：工序序列/工作中心/号机/外注价已有；缺标准工时/费率/实绩工时/差异
- **开工动作**：先 `brainstorming` 定需求边界（多级 BOM 展开是否独立建 `BomStructure` 表 vs 复用 ProductMaterial 递归）→ `spec` → plan
- **注**：多级 BOM 的"展开"逻辑 MRP P1 已含；A2 聚焦"工艺路线+成本精度+产能地基"

### A3 · 固定资产（资产卡片 + 自动折旧）　【实际完成序：④ ✅】
- [x] ✅ **已完成（全栈上 main，gstack QA 过）**
- **产出**：`FixedAsset`(资产卡片)/`DepreciationSchedule`(折旧计划)/`AssetDisposal`(处置) + 月末折旧 Worker → 自动凭证（借 制造费用/管理费用，贷 累计折旧）
- **复用**：自动凭证引擎（加 `VoucherSource.FixedAsset` + Role `FIXED_ASSET`/`ACCUM_DEPR`/`DEPR_EXPENSE`）；MES `Machine` 关联
- **开工动作**：`brainstorming`（折旧方法：直线/年数总和/双倍余额；残值；起折规则）→ `spec` → plan

### A4 · 银行对账（流水导入 + 自动对账）　【实际完成序：③ ✅】
- [x] ✅ **已完成（全栈上 main，gstack QA 过）**
- **产出**：`BankStatement`/`BankTransaction`(流水，支持 Excel/CSV 导入) + 自动匹配（金额/日期/摘要 vs Payment/Receipt）+ 未达账项 + 对账差异处理
- **复用**：BankAccount/Payment/Receipt/核销框架现成、HeaderAccount 凭证模式；差异凭证复用 Settlement 冲销
- **开工动作**：`brainstorming`（导入格式、匹配规则、单边/多边匹配、容差）→ `spec` → plan　（**工作量最小，适合先收口月结**）

### A5 · 预算 / 管理会计　【实际完成序：⑤ ✅】
- [x] ✅ **已完成（全栈上 main，gstack QA 过）**
- **产出**：`Budget`/`BudgetVersion`/`BudgetLine`(按科目×成本中心×期间) + 预算 vs 实际报表 + 可选过账预算拦截
- **复用**：CostCenter 树 + `JournalLine.CostCenterId` 维度已占位；GL 实际数现成
- **开工动作**：`brainstorming`（预算粒度、刚性/柔性控制、滚动预算）→ `spec` → plan

---

## 四、下一阶段方向（A 类收官后）

A 类已全收官，制造业 ERP "完整性"达成。往下走有四条候选线（详见 `docs/00-功能盘点.md` 第三/四章）：

1. ~~采购外注委托接真实~~ **✅ 已完成（2026-06-21）**——外注成本(`FinCostServiceAdapter`借FG贷INVENTORY)、支給材出库(`WmsIssueServiceAdapter`真扣 WMS 库存)两委托桩已换真实适配器，TDD 8 测 + gstack 真实 QA 过；采购章01~09 至此全部"做真"。
2. **MRP 套件 P2/P3/P4** —— 预测 / MPS / CRP（P4 CRP 前置 A2 工时已就绪）。
3. **ERP 行业纵深（B 类）** —— QMS 进阶(SPC/CAPA) / EAM 设备维护 / 轻 CRM 合同 / BI 自定义报表 / 现金流量表，按客户拉动。
4. **产品化为可售 SaaS（第四章）** —— 安全合规(SSO/2FA) → Onboarding/租户定制 → 订阅计费 → 运营可观测 → 开放 API/EDI。
5. **商业化新模块** —— Space 3D 空间数字底座（丛书定稿，待写 P1 计划）/ OA 低代码引擎（丛书写完，待落地）。

> 起任一项均遵循 [[feedback_coding_skills]]：`brainstorming → spec`（双格式 .md+.docx，见 [[reference_spec_dual_format]]）`→ writing-plans → subagent-driven-development`，TDD（superpowers）+ gstack 真浏览器 QA；跨模块桩起步、低耦合；新实体直接继承带 `TenantId` 的基类。

---

*生成于 2026-06-17；2026-06-21 五项 A1~A5 全部完成，本文转为收官记录。现状据真实代码盘点（115 控制器 / 919 测试 +1skip）+ git 提交核对。下一阶段方向见第四章与 docs/00-功能盘点.md。*
