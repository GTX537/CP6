# 采购(Pur)写端点 × 権限键清单（M-PUR Task 1 真相源）

> 生成于 2026-07-12。本表是 **M-PUR 横切接线波的唯一真相源**：T2（`Sys_MenuAction`/`Sys_RoleAction` 逐租户种子 + 菜单 MenuKey 显式赋值/回填）、T3（逐端点贴 `[RequirePermission("menu-key","action")]`）、反射 fail-closed 测试 + 403 用例均以本表为准。
> 依据：`docs/00-横切接线规范.md` 第一章（功能级四粒度）+ 同型先例 `docs/seeds/oawf-permission-keys.md`（§一~§七 结构照抄）+ 现有 Pur 菜单种子 `CP6.WebApi/Program.cs` MenuId 700–708 + 逐 Service 实现读证的只读 POST 豁免判定。
> 扫描范围：`CP6.WebApi/Controllers/Pur/`（**8 控制器全量**：SupplierPrice / PurchaseOrder / GoodsReceipt / ThreeWayMatch / PurchaseRequest / Rfq / Subcontract / PurReconcile）。
> **本任务只产出本文档，不改任何控制器/种子/测试/前端代码。**

## 约定

- **资源键 = `{menu-key}:{action}`**，**menu-key 一律连字符小写、绝对禁止下划线**（全仓 100% RequirePermission 用连字符；已核 4 个既贴控制器键面均连字符：`pur-po`/`pur-gr`/`pur-supplier-price`/`pur-match`）。本波统一冠 `pur-` 业务域前缀。
- **键锚定「消费页菜单」的 MenuKey**：Pur 8 控制器与菜单 701–708 一一对应，RoutePath 与键天然对齐（`/pur/po`→`pur-po` …）。逐键给出锚定菜单 MenuId/RoutePath。
- **资源键必须能锚定到一个 `Sys_Menu` 行**（`PermissionAggregator = Sys_RoleActions join Sys_Menus on MenuId where MenuKey!=null → {MenuKey}:{ActionCode}`）。
- **`高危?` 列三值**（沿用 WMS/ERP/MES/OA 定义）：
  - `是` = 触及**不可逆写 / 财务负债或成本入账 / 库存实物移动 / 转单建采购承诺**。这些一次误授即他人可越权制造付款义务、发出实物或下达采购承诺，T3 贴点与审计**最高优先级**，绝不可与 view/edit 混授。
  - `状态` = 独立工作流状态流转（送审/取消/检收判定应用），单独成键、不塞 edit/view。
  - `否` = 四基粒度 `view/add/edit/delete`（+ 域内个性化写）之一。
- **只读 POST 豁免**：纯查询/计算类 POST 归 `view`，表内标 `只读POST→view`，§四逐条附**读 Service 实现证得的**无写副作用依据（文件:行）。GET 端点一律不列。

---

## 一、写端点映射表（POST/PUT/DELETE，共 24 行）

| # | 控制器 | HTTP方法 + 路由 | 方法名 | 建议 menu-key | action | 高危? | 备注 |
|---|---|---|---|---|---|---|---|
| 1 | SupplierPriceController | POST `/api/pur/supplier-price` | Save | `pur-supplier-price` | add | 否 | 价表 upsert（阶梯价）。**既有贴点**。菜单701 |
| 2 | SupplierPriceController | DELETE `/api/pur/supplier-price/{id}` | Delete | `pur-supplier-price` | delete | 否 | 删除价表行。**既有贴点**（Pur 域用 `delete` 非 `del`，全波沿用） |
| 3 | PurchaseOrderController | POST `/api/pur/po` | Create | `pur-po` | add | 否 | 建 PO（草稿，未生效）。**既有贴点**。菜单702 |
| 4 | PurchaseOrderController | POST `/api/pur/po/{poNo}/submit` | Submit | `pur-po` | submit | 状态 | 送审（草稿→审批；桩即时通过则直达 Confirmed，真实流程置 PendingApproval 待 OA 回调）。**既有贴点**。发注确定实际由 OA `oa-inbox:approve` 放行，故此处归 `状态`（PurchaseOrderService.cs:148-169） |
| 5 | PurchaseOrderController | POST `/api/pur/po/{poNo}/cancel` | Cancel | `pur-po` | cancel | 状态 | 取消（仅草稿/送审/确认且未收货可取消）。可逆业务状态。**既有贴点**（PurchaseOrderService.cs:193-204） |
| 6 | GoodsReceiptController | POST `/api/pur/gr` | Confirm | `pur-gr` | add | **是** | **确认收货=不可逆入库**：委托 WMS 收货、库存实物增加（详§三）。**既有贴点**。菜单703 |
| 7 | GoodsReceiptController | POST `/api/pur/gr/{grNo}/apply-qc` | ApplyQc | `pur-gr` | qc | 状态 | 应用检收判定（回写 PO 验收锚 AcceptedQty，驱动后续可开票量）。质量判定应用、不新建财务/库存移动，归 `状态`。**既有贴点**（GoodsReceiptService.cs:134-） |
| 8 | ThreeWayMatchController | POST `/api/pur/match` | Match | `pur-match` | add | **是** | **三单匹配→容差内自动建应付(AP)**：制造财务负债（详§三）。**既有贴点**。菜单704 |
| 9 | ThreeWayMatchController | POST `/api/pur/match/{matchNo}/release` | Release | `pur-match` | release | **是** | **人工放行挂起单→接受超容差差异建 AP**：越容差控制的财务负债生成（详§三）。**既有贴点** |
| 10 | ThreeWayMatchController | POST `/api/pur/match/{matchNo}/reject` | Reject | `pur-match` | reject | 状态 | 拒绝挂起单（仅置状态 Rejected，不建票、不动锚）。**既有贴点**（ThreeWayMatchService.cs:180-191） |
| 11 | PurchaseRequestController | POST `/api/pur/pr` | Create | `pur-pr` | add | 否 | 手工建 PR（草稿）。**裸控制器新键**。菜单706 |
| 12 | PurchaseRequestController | POST `/api/pur/pr/{prNo}/submit` | Submit | `pur-pr` | submit | 状态 | 送审（草稿→审批；PurchaseRequestService.cs:105-126）。**裸控制器新键** |
| 13 | PurchaseRequestController | POST `/api/pur/pr/{prNo}/convert` | Convert | `pur-pr` | convert | **是** | **PR→PO 转单**：按建议供应商分组建 PO=创建采购承诺（详§三，计划点名）。**裸控制器新键** |
| 14 | RfqController | POST `/api/pur/rfq/from-pr/{prNo}` | CreateFromPr | `pur-rfq` | add | 否 | 从 PR 发起询价（建 Rfq+行，RfqService.cs:68-116）。**裸控制器新键**。菜单705 |
| 15 | RfqController | POST `/api/pur/rfq/{rfqNo}/suppliers` | AddSuppliers | `pur-rfq` | invite | 否 | 邀请供应商（写 RfqSupplier，状态→邀请中，RfqService.cs:119-162） |
| 16 | RfqController | POST `/api/pur/rfq/{rfqNo}/quote` | RecordQuote | `pur-rfq` | quote | 否 | 收报价（按行 upsert RfqQuote 矩阵，RfqService.cs:165-229） |
| 17 | RfqController | POST `/api/pur/rfq/{rfqNo}/rank` | Rank | `pur-rfq` | rank | 否 | **比价排名=写**（持久化 `q.Rank`，RfqService.cs:246/257 + SaveChanges:263）。**看似只读实为写，不豁免**（§四复核） |
| 18 | RfqController | POST `/api/pur/rfq/{rfqNo}/select` | Select | `pur-rfq` | select | 否 | 选定（写 RfqQuote.IsSelected，RfqService.cs:268-311） |
| 19 | RfqController | POST `/api/pur/rfq/{rfqNo}/writeback` | WriteBack | `pur-rfq` | writeback | 否 | 回写价表（选中报价 Source=rfq upsert 进价表，RfqService.cs:314-348）。改主数据但可逆，归 `否` |
| 20 | RfqController | POST `/api/pur/rfq/{rfqNo}/convert` | Convert | `pur-rfq` | convert | **是** | **选中报价转 PO**：按供应商分组建 PO=创建采购承诺（详§三，成交价直落，RfqService.cs:351-404） |
| 21 | SubcontractController | POST `/api/pur/subcontract/{poNo}/{lineNo}/consign` | AddConsign | `pur-subcontract` | consign | 否 | 登记外注支給材（按 BOM 应发料 upsert PoConsignMaterial，SubcontractService.cs:25-69）。**裸控制器新键**。菜单707 |
| 22 | SubcontractController | POST `/api/pur/subcontract/{poNo}/{lineNo}/issue` | Issue | `pur-subcontract` | issue | **是** | **发支給材=委托 WMS 实物出库**：不可逆库存移动，累加 IssuedQty 防吞料锚（详§三，SubcontractService.cs:97-141） |
| 23 | SubcontractController | POST `/api/pur/subcontract/{poNo}/{lineNo}/finished-cost` | FinishedCost | `pur-subcontract` | cost | **是** | **外注成品成本入账**：`_finCost.PostSubcontractCostAsync` 生成成本凭证、接财务（详§三，SubcontractService.cs:144-182） |
| 24 | SubcontractController | POST `/api/pur/subcontract/{poNo}/{lineNo}/reconcile` | Reconcile | `pur-subcontract` | view | 只读POST→view | 防吞料对账：纯读+内存反推算差异返回 DTO，**全方法无 Add/Update/Remove/SaveChanges**（§四，SubcontractService.cs:185-235）。POST 仅为传 finishedQty/tolerancePct |

> **GET-only 控制器（无 POST/PUT/DELETE，不在上表）**：
> - `PurReconcileController`（1 GET `/api/pur/reconcile/{poNo}`，三方对账诊断表，纯读；无写端点 → 0 权限键；页面 `/pur/reconcile` 菜单708 靠菜单可见性控制，无 action 键）。
>
> **有 POST 端点但全豁免（真写=0，不占「含真写控制器」计数，见 §七）**：无（每个含 POST 的控制器都至少 1 个真写）。Subcontract 有 1 只读豁免但另 3 端点真写，仍计入含真写控制器。

---

## 二、menu-key 汇总清单（去重，共 7 个）

| # | menu-key | 锚定菜单（Program.cs MenuId / RoutePath） | 承载 action | 说明 |
|---|---|---|---|---|
| 1 | `pur-supplier-price` | 701 供应商价表 `/pur/supplier-price` | add, delete | ✅有菜单行。回填=`pur-supplier-price` ✅一致。**既有**（701–704 由 Program.cs:1513 Pur 块回填） |
| 2 | `pur-po` | 702 采购订单 `/pur/po` | add, submit, cancel | ✅。回填=`pur-po` ✅一致。**既有** |
| 3 | `pur-gr` | 703 采购收货 `/pur/gr` | add, qc | ✅。回填=`pur-gr` ✅一致。**既有** |
| 4 | `pur-match` | 704 三单匹配 `/pur/match` | add, release, reject | ✅。回填=`pur-match` ✅一致。**既有** |
| 5 | `pur-pr` | 706 采购申请 `/pur/pr` | add, submit, convert | ✅有菜单行 706。**但 706 不在 Pur 回填范围(701-704) → 洁净首启 MenuKey=null**（§六头号命门） |
| 6 | `pur-rfq` | 705 询价比价 `/pur/rfq` | add, invite, quote, rank, select, writeback, convert | ✅有菜单行 705。**同上：705 不在回填范围 → 首启失配**（§六头号命门） |
| 7 | `pur-subcontract` | 707 外注加工 `/pur/subcontract` | consign, issue, cost, view | ✅有菜单行 707。**同上：707 不在回填范围 → 首启失配**（§六头号命门） |

> **零孤儿 menu-key**：7 个键均对应实在菜单行（701–707），RoutePath 派生键与本表逐字一致，无 MES `machine-list` 那种错配。
> **第 8 个 Pur 菜单 708 `pur-reconcile`（`/pur/reconcile`）承载 0 个 action 键**（PurReconcile 控制器 GET-only），不在本清单。
> **命门：705/706/707 三菜单不在既有 Pur MenuKey 回填范围**（Program.cs:1513 只回填 `MenuId 701..704`），且全局回填块 Program.cs:922 在 Pur 菜单插入(:1385)**之前**执行 → 洁净库首启这三菜单 MenuKey 留 null → `pur-rfq/pur-pr/pur-subcontract` 全键 join 不出 → **首启即 fail-closed 403**。§六头号硬前置。

---

## 三、高危动作清单（`是`：财务负债/成本入账/库存实物移动/转单建承诺，共 7 个资源键）

> T3 贴 `[RequirePermission]` 与审计的**第一优先级**，**绝不可**与 view/edit 混授。Pur 域高危集中在**制造付款义务（AP/成本凭证）**、**发出实物（WMS 出库/入库）**、**下达采购承诺（转单建 PO）**——一次误授即他人可越权制造财务/实物后果。

| 资源键 | 端点# | 为何高危独立（读证 文件:行） |
|---|---|---|
| `pur-gr:add` | 6 | **确认收货=不可逆库存增加**：GoodsReceiptService.ConfirmReceiveAsync 建 GoodsReceipt 并委托 WMS 收货（对标 WMS `wms-inbound-receipt:post` 高危）。入库后驱动可开票量、成本，回退代价高。GoodsReceiptService.cs:51-132 |
| `pur-match:add` | 8 | **三单匹配容差内自动建应付**：ThreeWayMatchService.MatchInvoiceAsync→BuildApAsync→`_finAp.CreateApInvoiceAsync` 生成 AP 发票=供应商付款义务，并累加 PO InvoicedQty。ThreeWayMatchService.cs:118-150,196-211 |
| `pur-match:release` | 9 | **人工放行超容差挂起单→建 AP**：越过容差控制强行建应付（`match.Status=Released`+BuildApAsync），是绕开自动控制的最敏感写。ThreeWayMatchService.cs:153-177 |
| `pur-pr:convert` | 13 | **PR→PO 转单**：ConvertToPoAsync 按建议供应商分组 `_po.CreateAsync` 建 PO=创建采购承诺、回填 ConvertedPoNo。计划点名转单类。PurchaseRequestService.cs:150-204 |
| `pur-rfq:convert` | 20 | **RFQ 选中报价转 PO**：ConvertToPoAsync 按供应商分组 `_po.CreateAsync`，成交价直落 PO 单价=创建采购承诺。RfqService.cs:351-404 |
| `pur-subcontract:issue` | 22 | **发支給材=委托 WMS 实物出库**：IssueConsignAsync→`_wmsIssue.IssueAsync(Purpose=subcontract)` 出库实物，`c.IssuedQty += wms.IssuedQty` 记防吞料锚。不可逆库存移动。SubcontractService.cs:97-141 |
| `pur-subcontract:cost` | 23 | **外注成品成本入账**：CalcFinishedCostAsync→`_finCost.PostSubcontractCostAsync` 生成成本凭证(CostVoucherNo)、接财务 06（加工费+支給材成本并入）。SubcontractService.cs:144-182 |

### 3b. 独立状态流转动作键（`状态`，共 4 个，仍单独成键、不塞 edit/view）

`pur-po:submit`（PO 送审，#4）· `pur-po:cancel`（PO 取消，#5）· `pur-gr:qc`（检收判定应用回写验收锚，#7）· `pur-pr:submit`（PR 送审，#12）· `pur-match:reject`（拒绝挂起单，#10）。

> 更正计数：状态键 = **5** 个（`pur-po:submit`、`pur-po:cancel`、`pur-gr:qc`、`pur-pr:submit`、`pur-match:reject`）。其余端点走 `add/delete/view` 四基粒度 + Rfq 域个性化写键（invite/quote/rank/select/writeback）。

---

## 四、只读 POST 豁免清单（归 view，共 1 个 —— 逐条读 Service 实现证得无写）

| # | 端点（方法） | 豁免依据（读 Service 实现，文件:行） |
|---|---|---|
| 1 | POST `/api/pur/subcontract/{poNo}/{lineNo}/reconcile`（SubcontractService.ReconcileConsignAsync） | 仅 `_db.PurchaseOrders.FirstOrDefault` + `_db.PurchaseOrderLines.FirstOrDefault` + `_db.PoConsignMaterials.Where(...).ToListAsync()` 三处读 → 内存反推单耗/应耗/差异 → 返回 `ConsignReconcileResult` DTO，**全方法无 `Add/Update/Remove/SaveChanges`**（SubcontractService.cs:185-235）。POST 仅为传 finishedQty/tolerancePct 计算入参 |

> **复核结论（防望文生义）**：以下「看似查询/排名/对账」的 POST **确为写端点，不豁免**——
> - `POST /api/pur/rfq/{rfqNo}/rank`：**「比价排名」持久化 `RfqQuote.Rank`**（RfqService.cs:246/257 写 Rank + :263 SaveChanges）→ 归 `pur-rfq:rank` 写键。
> - `POST /api/pur/subcontract/.../finished-cost`：虽 SubcontractService 本体无 SaveChanges，但 `_finCost.PostSubcontractCostAsync` 下沉写成本凭证（SubcontractService.cs:163-173）→ 真写、且高危。
> - `POST /api/pur/match`（Match）：容差内自动建 AP（下沉 `_finAp`）→ 真写、高危，非「查询匹配结果」。
> - GET `/api/pur/reconcile/{poNo}`（PurReconcile）是纯 **GET**，本就不列（非 POST 豁免范畴）。

---

## 五、命名归并判断与疑点（供 T2/T3 复核）

1. **Pur 域删除动作用 `delete` 非 `del`**：既有 `pur-supplier-price:delete` 贴点(#2)与 Program.cs:1520 种子均用 `delete`；全 Pur 波沿用 `delete`（与 WMS 波 `del` 不同，各模块沿用自身既定风格，勿混改）。
2. **Rfq 七动作按业务语义各成键**（`add/invite/quote/rank/select/writeback/convert`）：价格发现全流程每步语义独立、审批面需分权（如可授「收报价」不授「转 PO」），照 ThreeWayMatch `add/release/reject` 分权先例逐操作成键，不聚合。若 T2 审计认为过细，可将 invite/quote/rank/select 归并为单一 `edit`——**当前按操作分权**。
3. **`convert` 跨控制器同名不同锚**：`pur-pr:convert`(#13) 与 `pur-rfq:convert`(#20) 是各自菜单(706/705)下的转单键，**不归并**（不同 menu-key，天然独立资源键；均高危）。
4. **`pur-po:submit` vs 发注确定**：submit 仅送审；真正「发注确定」(PO→Confirmed) 在真实流程由 OA 审批放行(`oa-inbox:approve`)触发回调 `ConfirmFromApprovalAsync`（PurchaseOrderService.cs:172-）。桩环境 submit 即时确认属测试便利，不改「submit=状态、approve=高危」的分层。故 submit 归 `状态`。
5. **`pur-gr:qc`（检收应用）判 `状态` 而非高危**：仅应用已定 QC 判定、回写验收锚，不新建财务/库存移动；下游开票受其影响但开票本身另有 `pur-match:add` 高危闸把关。若 T2 审计认为「验收即锁定可付款量」应提级，可改判——**当前 `状态`**。
6. **`pur-rfq:writeback`（回写价表）判 `否`**：写供应商价表(Source=rfq)属主数据沉淀、可逆可覆盖，不制造付款/实物后果，归基础写 `否`。与 `pur-supplier-price:add`(#1) 同风险层。

---

## 六、命门与遗留（T2 硬前置）

### 头号硬前置·MenuKey 回填范围漏 705/706/707（洁净首启 pur-rfq/pur-pr/pur-subcontract 全 403）

**证据链**：
- Pur 菜单 700–708 在 Program.cs :1385–1438 插入，**插入时均未设 MenuKey**（如 :1417 `new Sys_Menu{...RoutePath="/pur/rfq"...}` 无 MenuKey 字段）。
- 唯一的 Pur 局部回填块 Program.cs :1513 **只覆盖 `MenuId >= 701 && MenuId <= 704`** → 705(pur-rfq)/706(pur-pr)/707(pur-subcontract)/708(pur-reconcile) **不在范围**。
- 全局回填块 Program.cs :922（`menusNoKey = Sys_Menus.Where(MenuKey==null && RoutePath!=null)`）在 Pur 菜单插入(:1385)**之前**执行 → 洁净库首启时 705–708 尚不存在 → 跳过；随后插入的 705–708 MenuKey 留 **null**。
- 结果：`PermissionAggregator` 过滤 MenuKey==null → `pur-rfq/pur-pr/pur-subcontract` 全 action 键 join 不出 → **首启即 fail-closed 403，须二次重启由 :922 回填才生效**（对标 OA 头号命门、MES 命门#1、WMS「TenantAdmin 新租户重启前 403」平台票）。
- **注**：既有 701–704 因 :1513 回填在其插入(:1391)**之后**同一 pass 执行，故首启即拿到 MenuKey，不受此命门影响——命门专属新扩的 705–707。
- → **T2 必须**在 Pur 菜单插入块对 705/706/707 各行**显式赋 `MenuKey`**（`pur-rfq`/`pur-pr`/`pur-subcontract`），或把 :1513 回填范围从 `701..704` 扩为 `701..707`（708 GET-only 可含可不含）。**不做则洁净部署首启这三控制器全 403。**

### 头号硬前置·既有 Pur 种子仅默认租户（其余租户 admin 也 403）

**证据链**：
- 既有 Pur MenuAction/RoleAction 种子在 Program.cs :1518–1531，`purActions` 10 条只 `new Sys_RoleAction { RoleId = 1, MenuId, ActionCode }`——**无 `TenantId`、无租户循环**。
- `Sys_RoleAction : BaseTenantEntity`（CP6.Entity/DomainModels/Sys/Sys_RoleAction.cs:11）→ 未显式设 TenantId 时由 `CP6Context.StampTenant` 盖**当前(默认)租户** → 该种子实际只落**默认租户一份**。
- 结果：其余 3 个租户（共 4 租户）下 admin(RoleId=1) 无任何 Pur RoleAction → **Pur 既有 10 键在非默认租户 admin 也 403**（与会话记忆已知 Fin/Sys C# 种子仅默认租户缺口同型）。
- → **T2 必须**新建 `PurPermissionSeed.EnsureSeeded(db)`，照 `CP6.WebApi/Seed/WmsPermissionSeed.cs` 逐租户模式（枚举 `Sys_Tenants` 全 Id，对每租户显式 `TenantId=tid` 播 MenuAction+RoleAction，`IgnoreQueryFilters()` 幂等判存），**一次覆盖既有 10 键 + 新增 14 键（含 view）**，并在 Program.cs 于 Pur 菜单+MenuKey 之后调用。旧内联 :1518–1531 块应由新 Seed 取代或补齐逐租户（否则新键仍只默认租户）。

### 注·既有 10 贴点键面审计结论（格式/锚定/一致性）

- **键格式**：4 既贴控制器 10 键全部**连字符**（`pur-po`/`pur-gr`/`pur-supplier-price`/`pur-match`），零下划线，符合全仓约定。
- **锚定**：10 键锚定 701–704，均有菜单行、回填派生键与贴点键**逐字一致**（`/pur/po`→`pur-po` …），零错配。
- **内联种子键面吻合**：Program.cs:1520–1523 `purActions` 与 4 控制器 `[RequirePermission]` **1:1 吻合**（701 add/delete；702 add/submit/cancel；703 add/qc；704 add/release/reject）——无缺、无多。唯一缺陷是上文「仅默认租户」范围问题，非键面失配。
- **新键零种子**：705/706/707 三菜单的 action（14 键含 view）当前**无任何 MenuAction/RoleAction 种子**——T2 全新播。

---

## 七、计数收口

- **扫描控制器**：8（SupplierPrice / PurchaseOrder / GoodsReceipt / ThreeWayMatch / PurchaseRequest / Rfq / Subcontract / PurReconcile）。与计划「Pur 目录 8 控制器」精确吻合。
- **GET-only 控制器（0 非 GET）**：1（PurReconcile，仅 1 GET）。
- **有 POST 但全豁免（真写=0）**：0。
- **含真写端点控制器**：7（除 PurReconcile）。
- **POST/PUT/DELETE 端点行总数**：**24**（= §一表行数，精确吻合）。
  - 其中**既有贴点**：**10**（#1–#10）。
  - **裸控制器新端点**：**14**（#11–#24）。
  - 其中**只读 POST 豁免（→view）**：**1**（#24 subcontract reconcile）。
  - **真·写端点**：**23**（既有 10 全真写 + 新 13 真写）。
- **menu-key（去重，承载 action 键）**：**7**（701–707；708 pur-reconcile GET-only 承载 0 键，另计）。
- **资源键（去重，含 view）**：**24**（每端点唯一 `menu-key:action`，无跨控制器归并 → 端点 24 ↔ 资源键 24，1:1）。其中既有 10、新 14。
- **高危键（是）**：**7**：`pur-gr:add`、`pur-match:add`、`pur-match:release`、`pur-pr:convert`、`pur-rfq:convert`、`pur-subcontract:issue`、`pur-subcontract:cost`。
- **状态键**：**5**：`pur-po:submit`、`pur-po:cancel`、`pur-gr:qc`、`pur-pr:submit`、`pur-match:reject`。
- **只读豁免键（view）**：**1**：`pur-subcontract:view`。

### 逐控制器双向核对（控制器→表 / 表→控制器，零缺漏零 GET 误列）

| 控制器 | POST/PUT/DELETE 端点数 | 其中豁免 | 真写 | 既有贴点 | 表内 # |
|---|---|---|---|---|---|
| SupplierPriceController | 2（Save/Delete；GET List/Resolve 不列） | 0 | 2 | 2 | 1–2 |
| PurchaseOrderController | 3（Create/Submit/Cancel；GET List/Get 不列） | 0 | 3 | 3 | 3–5 |
| GoodsReceiptController | 2（Confirm/ApplyQc；GET List/Get 不列） | 0 | 2 | 2 | 6–7 |
| ThreeWayMatchController | 3（Match/Release/Reject；GET List/Get 不列） | 0 | 3 | 3 | 8–10 |
| PurchaseRequestController | 3（Create/Submit/Convert；GET List/Get 不列） | 0 | 3 | 0 | 11–13 |
| RfqController | 7（CreateFromPr/AddSuppliers/RecordQuote/Rank/Select/WriteBack/Convert；GET List/Get 不列） | 0 | 7 | 0 | 14–20 |
| SubcontractController | 4（AddConsign/Issue/FinishedCost/Reconcile；GET ListOrders/GetConsign 不列） | 1 | 3 | 0 | 21–24 |
| PurReconcileController | 0（仅 GET `/{poNo}`） | 0 | 0 | 0 | — |
| **合计** | **24** | **1** | **23** | **10** | **24 ✅** |

> 自洽核验：
> - 总非 GET 端点 24 = 只读豁免 1 + 真写 23 ✅；
> - 逐控制器真写累加 2+3+2+3+3+7+3+0 = 23 ✅；逐控制器非 GET 累加 2+3+2+3+3+7+4+0 = 24 ✅；
> - 既有贴点累加 2+3+2+3+0+0+0+0 = 10 ✅（= 计划「4 已部分贴点 10 处」）；新端点 24-10 = 14 ✅（= PR 3 + Rfq 7 + Subcontract 4，与计划口径精确吻合）；
> - 资源键 24 = 高危 7 + 状态 5 + view 1 + 基础/个性化写 11 = 24 ✅；其中基础/个性化 11 = add×4（supplier-price/po/pr/rfq）+ delete×1（supplier-price）+ consign×1（subcontract）+ Rfq 个性化 5（invite/quote/rank/select/writeback）；
> - 表行 #1–#24 连续无跳号 ✅。
