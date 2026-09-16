# CP6 主仓库业务界面与客户端现状盘点

审计快照：CP6 `5f39a020a9c535fb0bd515cc419bc0e99b90fe07`。日期：2026-09-16。本分项只读源码与静态文档审查；未改业务代码、未启动服务、未构建或运行测试、未执行远程CI。下列 `path:line` 均为此快照的仓库相对位置。

## 1. 可以据此做升级决策的结论

CP6 主 Web 已是广覆盖的制造企业业务系统，包含销售/产品、采购、MRP、生产与质量、仓储、财务、OA、权限平台和空间设计/运行态。不能把它归为只有菜单的演示壳，也不能用页面数量推导“全部业务生产可用”。多数页面有真实 HTTP 数据入口和写操作，但局部作业链、外部系统接口和 UI 设计统一程度仍有明显差异。

最值得先处理的具体断点是：旧 Web 拣货页只在客户端记录实拣和缺货，点击完成不提交这些数据；包装页可在本地随机生成所谓运单号；ERP 的 mcframe7/POWER EGG/版型外部接口有正在注册使用的 NoOp 实现。它们都可能藏在可正常打开、能展示真实列表的页面中。应按“业务动作及其持久化结果”验收，不能按“已接 API 页面数”验收。

现有设计系统可继续扩展：Vue 3/Element Plus、全局语义 token、Cp 模板、多语言、移动断点和独立工作区均已有落点。主要升级工作应是统一交互、收敛新旧作业入口、整理权限与导航，并对真实业务链建立可重放验收；不宜先做全量重写。

原生端是 WMS 专用客户端：WPF 桌面负责任务管理、设备/条码和打印网关；Android MAUI 负责领取任务、扫码、执行与受控离线进度。`CP6.Space.Client` 是生成的 .NET API SDK，没有 UI。主 Web 没有 CRM 业务页面；旧 CRM 菜单只是默认禁用的目录，当前主仓库与外部 CRM 的可见前端关联是 OIDC 登录续接。

## 2. 数量口径与复现文件

所有数字来自目录枚举、路由源码和静态 import 追踪；不是生产菜单、授权角色、自动测试通过数或唯一业务能力数。

| 指标 | 数量 | 口径 |
|---|---:|---|
| `cp6.web/src` 下 Vue 文件 | 289 | 包含页面、模板、嵌套面板、设计演示组件 |
| `src/views` 下 Vue 文件 | 248 | 包含页面内子组件，不能称为 248 个独立页面 |
| `viewModules` 路由键 | 151 | `router/index.ts` 静态映射表；包括平台/内部子页、两个出库别名 |
| 151 个键对应不同组件 | 149 | `/wms/shipping-order[-list]` 复用出库页面 |
| `staticRoutes` 中有组件的顶层记录 | 23 | 包括 layout、登录、弹窗、Space 全屏、演示页；不含两个 redirect、一个 alias |
| 被 router 直接引用的不同 views 组件 | 164 | 包括三项根级登录/布局/独立布局和两个设计方案页 |
| `src/api` 非测试 TS 文件 | 122 | 排除 `*.spec.*`、`*.test.*` |
| 上述文件 `http.get/post/put/delete/patch` 调用行 | 925 | 调用点数；不是去重后的后端 endpoint 数 |
| `src` 单测文件 | 188 | `*.spec.[jt]s[x]` 或 `*.test.[jt]s[x]` 文件；未执行 |
| `e2e` Playwright spec 文件 | 10 | 不包含 `auth.setup.ts`；未执行 |
| `modules/space-design` 下 Vue 面板 | 20 | 在 views 数量之外 |

| 业务目录 | Vue 文件（含子组件） | `viewModules` 路由键 | 被 router 直接引用的不同组件 |
|---|---:|---:|---:|
| dashboard | 3 | 1 | 1 |
| pms | 23 | 17 | 22 |
| platform | 5 | 5 | 5 |
| ERP | 42 | 20 | 20 |
| MES | 18 | 15 | 15 |
| WMS | 41 | 40 | 38 |
| Pur | 8 | 8 | 8 |
| Plan | 2 | 2 | 2 |
| Fin | 22 | 22 | 22 |
| OA | 39 | 9 | 10 |
| WF | 6 | 2 | 2 |
| Space | 36 | 10 | 16 |
| 根级 Login/Layout/DetachedWorkspace | 3 | 0 | 3 |

审计附件在本报告的 [data目录](data/README.md)，复现脚本在本报告同目录：

- `inventory_ui.py`：可复现目录/路由/import/API 调用点统计；在附件目录执行 `python inventory_ui.py --core <CP6固定快照路径> --output <审计输出目录>`，两个参数均支持相对路径，只在指定输出目录写文件。
- `ui-page-inventory.csv`：248 行，逐文件列出模块、行数、路由、引用它的组件、静态传递 API 依赖、Cp/Vol 标签、权限指令和样式指标。
- `ui-route-inventory.csv`：151 个 viewModules 项及 23 个静态/布局组件记录，带 router 行号。
- `ui-api-call-sites.csv`：925 个 API 包装器 HTTP 调用行，带文件和行号。
- `ui-inventory-summary.json`：机器可读汇总。

复现脚本的 API 依赖追踪是静态辅助：跟踪可解析的 `@/`/相对 import、子 Vue、store 和 composable，不把“无直接 API import”的纯展示子组件判为空壳。它不是 Vue/TS 完整语义分析器，不证明所有运行分支会执行。逐页名单解决覆盖面，以下调用链解决代表性深度。

## 3. 路由、动态菜单与权限的真实规则

### 3.1 页面可达性由菜单和组件映射共同决定

`cp6.web/src/router/index.ts:7` 定义 151 个映射键；`addDynamicRoutes` 在 `:410` 把登录菜单过滤为既有 routePath、又在 viewModules 中存在的记录，并按路径去重（`:415`），然后重建 layout 子路由（`:425`）。因此数据库加一条菜单不等于自动获得页面；未匹配路径被忽略。反之，组件被放在 viewModules 中，也不等于某个租户、角色能看到。

`cp6.web/src/views/LoginView.vue:451` 写非敏感登录标志及菜单，`:458` 将菜单保存在 localStorage，`:459` 注册动态路由。刷新时 router `:519` 从缓存菜单重建。默认首页是菜单中第一条有效路由（`:421`），不是固定的 dashboard。菜单展示为递归树（`cp6.web/src/components/MenuTreeItem.vue:5`），名称优先 `nav.{id}` 翻译、再回退服务端 menuName（`:33`）。

平台区五页不依赖普通菜单，始终添加，前端用 `isPlatformAdmin` 做 UX 检查（router `:378`、`:491`）。内部页 `/oa/form-initiate`、`/space/location-publish` 始终挂载（`:394`）。旧 `/wf/todo` 与 `/wf/my-applications` 重定向到 `/oa/inbox`（`:194`）。

其余静态独立工作区包括销售五类录入窗口、OA/WF 三个设计器窗口、MES 大屏、Space Legacy 编辑器、Space Studio 启动/编辑器、3D 浏览器、层叠视图、控制塔，以及两个 UI 方案演示页。`cp6.web/src/utils/workspaceNavigation.ts:1` 明确把 OA/WF 设计器菜单转到独立窗口。不要把重复窗口当作新增业务能力。

### 3.2 权限与身份已有基础，但不能把前端隐藏当安全证明

Web 使用 `/api` Axios 实例、httpOnly cookie 和 `withCredentials`（`cp6.web/src/api/http.ts:25`）；非安全方法注入 CSRF 头（`:33`），401 共享一次 refresh 并重放（`:47`、`:65`），409 留给业务页处理（`:90`）。OA 代理操作额外送 `X-Acting-As`（`:40`）。

操作权限 store 从 `rolePermApi.myActions()` 取得 `menuKey:action`（`cp6.web/src/stores/permission.ts:14`）；`v-permission` 删除无权元素，但权限加载前保留元素，失败后 store 也不置 loaded（`cp6.web/src/directives/permission.ts:7`、`:29`；`cp6.web/src/stores/permission.ts:15`）。这是可见性的 fail-open，不证明后端授权缺失；升级时应统一“加载中/无权/失败”状态，避免按钮短暂出现或长期可点。

多数 standalone 路由只要求登录标志，绕过动态菜单依赖（router `:510`）；只有显式 meta.permission 才检查操作键，例如 Space 控制塔 `:346`。这意味着“菜单中没有入口”和“不能直接打开页面”不是同一件事。报告不据此声称越权读取后端数据，需与后端策略和真实角色验收联查。

router 没有 catch-all 404/无权页，缓存 JSON 用裸 `JSON.parse`（`:521`）；菜单失配与缓存坏值的 UX 值得在升级时收口。`cp6.web/src/views/LayoutView.vue:360` 登出执行 localStorage.clear，包含侧栏宽度/语言缓存等偏好；这是可核实的偏好丢失点。

## 4. 真实业务与页面覆盖

### 4.1 ERP 销售、产品与纸品行业主数据（20 个页面组件）

现有入口：估价计算录入/列表、报价录入/列表、产品五步录入/列表、订单三步录入/列表、价格更正、订单全程追踪、贷项通知单、欠交列表、OTD 报表、汇率、往来单位录入/列表、FSC 清单、纸板报价单价、版型模具录入/列表。完整路由在 `cp6.web/src/router/index.ts:86` 起，附表保留逐页路径。

代表链：

- 估价页面保存调用 `estimateCalcApi.create/update`（`cp6.web/src/views/erp/EstimateCalcView.vue:244`）；第三步 `cp6.web/src/views/erp/estimate/Step3Result.vue:123` 调 `/estimate-calcs/calculate`，controller `CP6.WebApi/Controllers/Erp/EstimateCalcController.cs:141` 委托 `EstimateCalcService.CalculateAsync`。服务实际读取纸张/段成率主数据和按客户、生效日查询的纸板价格（`CP6.Core/Services/Erp/EstimateCalcService.cs:253`、`:270`），并计算面积/单位成本。它不是仅返回固定整包演示数据，但仍包含固定兜底纸价 30、段成率 1、每工序成本 5、利润倍数 1.2（`:241`），所有数量档用同一计算单价（`:323`）。这是简化报价引擎，需要业务核价验收。
- 订单 `cp6.web/src/views/erp/OrderEntryView.vue:189` 先信用检查，再 buildDto/create/update；客户、订单类型和产品行校验在 `:178`。列表有真实 search/export（`cp6.web/src/views/erp/OrderListView.vue:180`、`:189`）；订单扩展为工单的 UI 在 `cp6.web/src/views/mes/WorkOrderEntryView.vue:126`。采购/财务之外，销售端保留日本制造业的手配 NO、mcframe 标识、纸品属性与多步状态编辑。
- 产品 `cp6.web/src/views/erp/ProductMasterView.vue:174` 调 copy，五步覆盖目标、基本信息、工序、材料、汇总；这类完整行业表单不能降为通用四字段 CRUD。

必须区分的外部集成边界：

| 边界 | 当前证据 | 对升级验收的影响 |
|---|---|---|
| 产品 WIP 检查 | `CP6.WebApi/Program.cs:559` 注入 `NoOpWipCheckService`；`CP6.Core/Services/Erp/IWipCheckService.cs:28` 固定无异常 | 页面检查按钮不代表查询真实 mcframe7 在制状态 |
| 订单 WIP | `CP6.Core/Services/Erp/OrderService.cs:684` 固定 Level=0 | 要保留“未接外部状态”的准确说明与能力开关 |
| 信用额度 | `CP6.Core/Services/Erp/OrderService.cs:695` 使用固定 10,000,000 与订单金额累计 | 不能称为完整信用/应收风险控制 |
| POWER EGG 工作流 | `CP6.WebApi/Program.cs:572` 注入 NoOp；`CP6.Core/Services/Erp/IPowerEggWorkflowService.cs:30` 为桩 | 界面成功不能当作外部系统创建成功 |
| 版型 PE API | `CP6.WebApi/Program.cs:582` 注入 NoOp；`IPlateMoldService.cs:70` 返回 Ok=true/stub 发送成功 | 业务可保存不等于对外下单已经实现 |
| 工期倒排 | `CP6.Core/Services/Erp/OrderService.cs:727` 简化为周末非工作日 | 与企业真实工厂日历的差距需单独关闭 |

### 4.2 采购与计划（8+2 个页面）

采购覆盖供应商价表、采购申请 PR、询比价 RFQ、PO、收货 GR、三单匹配、外协加工、采购对账；计划覆盖物料计划策略和 MRP 运算/建议单。这些是 API 实装页面，不是静态报表。

PO 页面具备新建、送审、取消和明细（`cp6.web/src/views/pur/PurchaseOrderView.vue:151`、`:168`、`:191`），API `/pur/po`（`cp6.web/src/api/pur/pur.ts:34`）。收货从 PO 拉行、按实际量确认并可申请质检（`cp6.web/src/views/pur/GoodsReceiptView.vue:149`、`:169`、`:190`）；三单匹配可提交匹配、放行和拒绝（`cp6.web/src/views/pur/ThreeWayMatchView.vue:182`、`:206`）。MRP 从开放订单运行，读取净需求与建议单，再确认/转单/忽略（`cp6.web/src/views/plan/MrpBoardView.vue:88`、`:113`、`:128`；`cp6.web/src/api/plan/plan.ts:11`）。

采购目录保留 Stub 类和旧接口注释，不应凭名字误判当前实现：实际 DI 使用真实 OA 审批、WMS 收货、WMS 发料、财务成本适配器（`CP6.WebApi/Program.cs:412`、`:414`、`:422`、`:423`）。收货适配器真实创建入库指示、确认，再调用 ConfirmReceiptAsync（`CP6.Core/Services/Pur/Contracts/WmsReceiveServiceAdapter.cs:29`、`:43`、`:46`）。真实审批绑定/配置和完整库存会计结果仍要以数据库场景验证。

升级重点：这些页面大量使用独立 Element 表格/弹窗，与已换 CpListPage 的 ERP/WMS 有明显实现模式差异；供应商/物料等字段应统一可检索选择器、错误定位、批量行编辑和单据关联跳转，而不只统一色彩。

### 4.3 MES、生产和质量（15 个页面组件）

工作中心、工序成本率、工单录入/列表、生产实绩录入/列表、质检录入/列表、不良管理、计划排程、MES 看板、设备、OEE、计划达成率和 Control Tower。

- 工单支持销售订单展开、保存和发行，发行后禁止基本/工序/材料编辑（`cp6.web/src/views/mes/WorkOrderEntryView.vue:126`、`:194`、`:214`），API `/mes/work-orders` 及 `/expand-from-order`（`cp6.web/src/api/mes/mes.ts:65`、`:87`）。
- 实绩页面按动作调用 start、suspend、resume、complete、report（`cp6.web/src/views/mes/ProductionResultEntryView.vue:226`、`:246`），不是只有一张产量列表。
- 质检页面先加载工单与检验模板，再建立/修改结果（`cp6.web/src/views/mes/QualityInspectionEntryView.vue:261`、`:272`、`:364`），API `/mes/inspections`；不良另有分类与记录 CRUD（`cp6.web/src/api/mes/mes.ts:179`）。
- 排程具备 reschedule/auto-arrange（`cp6.web/src/api/mes/mes.ts:223`），设备停机、状态、OEE 今日/趋势/重算均有后端包装器（`:262` 起）。

静态代码证明行为入口和 DTO 传递，不证明 OEE 算法口径、有限产能计划、设备在线数据质量或质量判定已完成真实工厂验收。MES 原始 el-table/el-form 样式为主，尚未像 WMS 大面积消费 Cp 页面模板。

### 4.4 WMS（40 个路由键、38 个页面组件）

| 功能组 | 现有页面/动作 | 代表证据 |
|---|---|---|
| 基础与查询 | 仓库、库位、库存查询/质量锁定、缺料、滞留库存、批次追溯、有效期 | `cp6.web/src/router/index.ts:128` 起；`CP6.WebApi/Controllers/Wms/StockController.cs:83` 与 `:104` 有库存 apply/move；`CP6.WebApi/Controllers/Wms/StockQcController.cs:23` 有质量状态操作 |
| 入库 | 入库指示录入/列表、实绩、生产入库 | `cp6.web/src/views/wms/ProductionInboundView.vue:177` 组 DTO，`:201` 调 confirm；`InboundReceipt` 页面与 API 为同族 |
| 出库 | 指示录入/列表、引当、开始拣货、包装发货；shipping-order 两路由复用 outbound 组件 | `CP6.WebApi/Controllers/Wms/OutboundOrderController.cs:97`、`:111`、`:124`；`cp6.web/src/views/wms/PackingShipView.vue:221` 真正调用 ship |
| 盘点/计划 | 盘点建快照、开始、保存数量、送审、审批/取消；货位优化、补货、越库 | `CP6.WebApi/Controllers/Wms/StockTakeController.cs:37` 起；`cp6.web/src/api/wms/logistics.ts:40`、`:43`、`:58`、`:61` |
| QC/售后 | 入库 QC、RMA 接收/检查/判定/关闭 | `CP6.WebApi/Controllers/Wms/QcInspectionController.cs:35`、`:74`；`CP6.WebApi/Controllers/Wms/RmaController.cs:46` 起 |
| 纸品行业 | 纸卷匹配/消耗/分切、余料、版模库存维护、油墨批次/混合、托盘、VMI、样品借还 | `cp6.web/src/api/wms/paperIndustry.ts:12` 起及 `paperIndustry2.ts` |
| 协同 | 移动作业/生产控制台、WCS 任务、承运商事件、IoT 传感器/读数/警报 | `cp6.web/src/views/wms/MobileTaskView.vue:134` 嵌入控制台；`cp6.web/src/api/wms/connectivity.ts:13` 起 |
| 管理/报表 | 仓库看板、桥健康、出库路由、报表中心（月存、ABC、呆滞、出入库历史及 CSV） | `CP6.WebApi/Controllers/Wms/ReportCenterController.cs:16` 起；`cp6.web/src/router/index.ts:147` 起 |

router `:147`、`:162`、`:170`、`:175` 的旧注释仍把后续阶段标成“占位”，但映射目标实际已是业务页面；真正的 `WmsPlaceholderView.vue` 无路由/无引用。应修正文档认知，不能把这些组整体报为未实现。

**存在实际 API，并不保证具体按钮闭环：**

1. `cp6.web/src/views/wms/PickingWorkView.vue:155` 明确行状态 client-side only，`confirmPick :239`、`confirmShort :253` 只改 reactive 数据，`loadTask :214` 重置状态；`onComplete :260` 只确认、提示成功、清选择、刷新列表，没有提交实拣量和缺货原因。因此旧 Web 拣货页只能证明“读取出库任务/开始拣货”，不能证明完成/差异过账。应收敛到 v2 任务执行模型或补完持久化链，并测试刷新/跨设备/部分完成/短拣。
2. `cp6.web/src/views/wms/PackingShipView.vue:209` 用日期和 `Math.random` 合成 trackingNo（`:212`），没有在此动作调用承运商注册。它是本地参考号生成，不是真实承运商运单获取；而随后的 ship 是真实后端请求。UI 应避免把两者混为一个能力。
3. IoT 页面同时支持真实读数接口与人工/模拟数据：`cp6.web/src/views/wms/IotMonitorView.vue:174` 调 simulate，`:226` 手工 postReading；API `cp6.web/src/api/wms/connectivity.ts:92` 为 `/wms/iot/simulate`。有传感器管理 UI 不能证明现场设备已集成。
4. WCS/承运商页面有状态推进 API，可做业务记录管理，但仅此不能证明 AGV、输送线或物流商 SDK 已连通。真实外部 connector 要另验。

**较新的生产任务链更完整：** `cp6.web/src/views/wms/MobileTaskView.vue:191` 查询 v2 任务，`:198` 联取任务与事件，支持分配、暂停、释放、接管、异常、解决异常；`cp6.web/src/views/wms/WmsProductionConsole.vue:401` 起有统计、设备激活、角色范围、功能变更申请、条码别名/规则、序列号、LPN、标签模板/任务。`cp6.web/src/api/wms/mobile.ts:12` 使用 `/v2/wms/tasks`；controller `CP6.WebApi/Controllers/Wms/MobileTasksV2Controller.cs:117` 真正委托 ScanAsync，`:129` 委托 CompleteAsync。这是升级旧作业页面可复用的主链。

### 4.5 财务（22 个页面）

覆盖科目、记账凭证、试算、期间关账、AP 发票/付款/账龄、AR 发票/收款/账龄、资产负债/损益、生产成本、资产类别/卡片/折旧/处置、银行流水/导入模板/对账、预算编制/执行比较。

代表行为：凭证新建→提交→过账/驳回→红冲（`cp6.web/src/views/fin/JournalEntryView.vue:188`、`:199`、`:208`、`:216`），关账先 preCloseCheck 后 close/reopen（`cp6.web/src/views/fin/PeriodCloseView.vue:70`、`:82`、`:94`）；成本按工单归集/结转（`cp6.web/src/views/fin/CostSheetView.vue:171`、`:193`）；银行对账含自动/手动撮合、挂账、生成凭证、锁定/解锁/撤销匹配（`cp6.web/src/views/fin/BankReconciliationView.vue:224`、`:240`、`:298`、`:350`）。

这些是真实业务操作界面；本报告不对财务正确性、税务适用性、科目规则、期间并发锁或会计政策做静态“通过”判断。高风险功能升级应保持原状态机和权限，验收以借贷平衡、重放幂等、关账保护和账簿结果为单位。

### 4.6 OA/WF、系统与平台

OA 主入口是信箱、流程管理、表单目录、查询、通知/代理设置、流程设计器、审批人映射和工作日历；起草是内部子路由。Inbox 包含 dashboard/pending/running/done/draft/detail 子组件（`cp6.web/src/views/oa/inbox/InboxView.vue:150`），加载代理授权、统计和流程目录（`:179`、`:232`、`:260`）。起草页真实加载表单定义、预演审批路线、存草稿并带幂等键提交（`cp6.web/src/views/oa/catalog/FormInitiate.vue:112`、`:133`、`:151`、`:172`）。

新流程设计器有加载、保存、发布、克隆（`cp6.web/src/views/oa/designer/DesignerView.vue:93`、`:246`、`:279`、`:310`），节点包含审批、网关、服务任务、子流程等。旧 WF 表单/流程设计器仍可达，与新 OA 设计器并存；旧待办/申请源码则已不被引用。应明确每种设计器的权威模型和迁移路线，不按目录名简单删除。

PMS 包括用户/角色/菜单、旧权限页、操作/安全日志、SSO、2FA、字段审计、语言/字典、部门、角色功能/数据/字段权限、序号与代码生成器；平台另有租户、平台超管、代入、跨租户审计、GDPR。登录、改密、SSO 落地、2FA challenge/enroll 是静态身份页面，不能漏在“菜单页面”盘点之外。

### 4.7 Space：设计、发布、运行态与规划并存

Space 应当按五种工作区评估，不能统称“3D 看板”。

| 工作区 | 实际实现与入口 | 边界 |
|---|---|---|
| 主数据/旧编辑器 | 站点、楼层、编码规则、库位发布、事件；`/space/editor/:floorId` 仍挂旧 `FloorEditor` | 旧编辑器消费 `sceneApi`（`cp6.web/src/views/space/editor/FloorEditor.vue:6`）；与 Design V1 是不同入口 |
| Space Studio / Design V1 | `/space/design/sites/:siteId/start` 建 draft/floor；`/space/design/:versionId/floors/:floorLogicalId/underlay` 别名 editor | `cp6.web/src/router/index.ts:313`；`DesignUnderlayView.vue` 4596 行，承载丰富专业交互 |
| 发布管理 | 校验、变更预览、发布尝试、轮询、失败重试、历史版本重发布 | `cp6.web/src/views/space/lifecycle/SpacePublishManagementView.vue:494`、`:522`、`:548`、`:602`、`:624` |
| 运行态 | Published 3D、全层叠、控制塔、库存定位、设备/人员、诊断与建议、任务路径 | `cp6.web/src/views/space/viewer/FloorViewer.vue:455` 起；只认可 Published 权威几何，运行数据单独覆盖 |
| 规划 | 规划分支、历史任务数据集、场景仿真和证据、GLB | `cp6.web/src/views/space/planning/SpacePlanningScenarioView.vue:60`；子 `PlanningScenarioPanel`/`PlanningHistoricalDatasetPanel`/`PlanningSimulationPanel` |

Studio 的实际行为包括底图上传/标定、CAD 上传/解析/问题复审、Excel-CAD 匹配、布局模板/企业仓库模板、元素绘制/复制/分割/合并/重绘、位置编码、WMS 采纳、AI 生成候选审查、2D/3D 联看、撤销/补偿与本地恢复命令。代表调用点：`cp6.web/src/views/space/editor/DesignUnderlayView.vue:893` 读 scene，`:1297` CAD 上传，`:2045` 底图上传，`:2308` 标定，`:2956` 编辑命令 envelope，`:3043` 编码预览，`:3224` CAD 复审应用，`:3482` 申请 lease，`:3530` 续租。其复杂度远超普通页面。

编辑可用性有明确限制：宽度 <1280 时只读，非 Draft、未拥有租约或 revisionConflict 同样只读（`:333`）。不能对其承诺“移动端可编辑”或把只读窄屏判断为响应式 bug；需先明确专业工具目标设备。

Published 浏览器入口通过 `cp6.web/src/api/space/designPublishedScene.ts:25` 验证 schemaVersion、authority=DesignRevision、runtimeOverlayIncluded=false、siteId/version/hash/revision，一层层检查 floor 属于同一 Published 快照。库存/设备/人员不能作为设计几何持久化来源。`cp6.web/src/views/space/viewer/FloorViewer.vue:1017` 定位库存、`:1109` 查任务路径、`:1270` 当前设备、`:1341` 当前人员，运行诊断、上架建议、派工建议/审批/执行/评价/重试/补偿在 `:536`、`:581`、`:632`、`:675`、`:771`、`:801`、`:846`。

演示与来源不能混算：`cp6.web/src/types/space/dataSource.ts:1` 明确 Real/Simulated/Unavailable；派工建议默认不包含模拟人员（`cp6.web/src/views/space/viewer/DispatchRecommendationPanel.vue:407`）；旧 AdvancedPanel 的优化顺序明确“仅演示，不回写 WMS”（`:68`），与新派工执行链不是同一能力。AI provider 支持 Mock/Local/External（`cp6.web/src/api/space/aiAdmin.ts:6`），有管理界面不表示当前环境配置了可用真实模型。规划仿真本来就是离线分析能力，不能拿仿真结果冒充真实仓库吞吐验收。

## 5. 空壳、演示、不可达与历史遗留的明确分类

| 对象 | 分类 | 依据 |
|---|---|---|
| `views/dashboard/HomeView.vue` | 空白遗留文件、当前无路由/无导入 | `:1` 只有空 main；实际 dashboard 是 DashboardView |
| `views/pms/AboutView.vue` | 模板遗留、当前无路由/无导入 | `:3` “This is an about page” |
| `views/wms/WmsPlaceholderView.vue` | 真实占位组件，但当前无路由/无导入 | `:9` comingSoon，`:13` inDev；不能外推为 WMS 大组未实现 |
| `views/wf/TodoCenter.vue`、`MyApplications.vue` | 有旧 API 行为的历史实现，当前无路由/无导入 | router 已 redirect 到新 Inbox；不是空文件，也不是当前主入口 |
| `/menu-designs`、`/flow-designs` | 可直接访问的 UI 演示页 | router `:349`、`:355`；`cp6.web/src/views/pms/MenuDesignVariantsView.vue:6`、`cp6.web/src/views/oa/designer/FlowDesignVariantsView.vue:221` 写“演示数据，不写入系统” |
| `/crm/*` 旧目录 | 没有主 Web 组件的旧菜单种子 | `CP6.WebApi/Seed/CrmMenuPermissionSeed.cs:16` 起，`Enable=false :55`；不在 viewModules |
| 旧 Web 拣货部分动作 | 半接通 | 有读取/开始 API，但实拣/短拣/完成只改本地状态 |
| 旧 Space 优化顺序 | 明示演示/不回写 | AdvancedPanel.vue:68 |
| IoT 模拟、Space 模拟人员/设备、Mock AI | 可配置或显式模拟数据分支 | 不能与真实来源合并统计为业务验收 |

“无路由/无导入”通过当前 `src` 可解析 import 与 router 映射静态检查得出，不包含仓库外宿主动态加载。其余嵌套组件不能因没有单独 route 就被判定不可达。

## 6. CRM 与外部应用关系

`CP6.WebApi/Seed/CrmMenuPermissionSeed.cs:13` 保留一个父组、五个子菜单和 22 个 action；新建菜单默认 Enable=false（`:55`），但已有菜单不会被强制重新禁用（`:61` 的注释和后续更新逻辑）。所以可以证明默认禁用，不能声称每个现有数据库里均禁用。主 Web 没有对应 `/crm/dashboard`、leads、accounts、opportunities、site 组件；即使旧菜单被人工启用，动态路由也因为 viewModules 缺失而不注册。

当前主 Web 的外部 CRM 接入在认证续接：`cp6.web/src/views/oidcReturn.ts:4` 只接受本地 `/connect/authorize?` 续接路径，sessionStorage 保存十分钟，成功后恢复跳转；`cp6.web/src/views/LoginView.vue:319` 识别 oidc_return，`:324` 做同源被动 profile 探测，`:469` 登录后恢复。`cp6.web/src/main.ts:52` 专门防止 CRM-wide logout 后遗留标志引起登录页加载受保护权限。主仓库是该身份路径的一部分，不是外部 CRM 的业务 UI 宿主。

仓库 `docs/crm` 中历史执行规范与当前菜单/模型退役存在时间差；全项目升级应由主审计结合外部 CRM 实际仓库另列能力，不能把 CRM 文档规划或 22 个权限 action 计作本主 Web 已完成 CRM 功能。

## 7. 桌面、移动与 SDK 的实际成熟度

| 客户端 | 当前范围与证据 | 静态成熟度判断 |
|---|---|---|
| CP6.Desktop | .NET 8 Windows WPF（csproj:4），一份 MainWindow 三类登录/升级/任务可见区；登录/SSO/2FA、设备激活、任务筛选分页/创建/分配/取消/暂停/释放/接管/异常、设备启停/条码、打印网关 | 有业务 ViewModel 和 HTTP 链的专用 WMS 控制客户端，不是占位；不是 ERP/财务/Space 全客户端 |
| CP6.Mobile | .NET 10 Android MAUI，Android 最低 API23（csproj:3、:14）；6 个业务/身份页路由（AppShell.xaml.cs:8-18） | 实现了面向现场人员的任务闭环；没有 iOS TargetFramework，不能称为已交付 iOS 版本 |
| CP6.Client.Core | 顶层19个 C# 文件，统一会话/升级/SSO/动态 endpoint/心跳/语言/扫码/任务/实时/打印能力 | 可复用客户端应用核心，非 UI 应用 |
| CP6.Client.Api | 两个顶层C#文件：typed client 与 models，native surface 漂移检查文件 | 仅选定 native API 面，不是全部 Web API SDK |
| CP6.Space.Client | net8.0 类库 + 1 个 NSwag 生成 C# 文件 | Design API v1 客户端/DTO，无窗口、页面或单独 Space 桌面产品 |
| sdk/typescript/space-design-v1 | NSwag Fetch 客户端/类型、tsconfig、README，生成/检查脚本 | 真实契约 SDK；Web 的 Space 包装器也引用其类型，但仍通过现有 Axios 统一身份通信 |

桌面证据：`CP6.Desktop/ShellViewModel.cs:304` CreateTask、`:321` Assign、`:341` Pause、`:361` Takeover、`:382` ActivateDevice、`:393` StartPrintGateway、`:482` 并行加载统计/设备/条码；`CP6.Desktop/MainWindow.xaml:149` 起是真正 DataGrid，表头仍有硬编码英文和数字状态。`CP6.Desktop/PlatformServices.cs:25` 使用 DPAPI CurrentUser 加解密 token，移动 `CP6.Mobile/PlatformServices.cs:7` 使用 SecureStorage。不能用这些静态实现替代实际 OS 打包、扫码硬件、打印和 SSO 回调验收。

移动证据：`CP6.Mobile/ViewModels.cs:232` 任务列表、`:304` 明细、`:365` 扫码状态机；扫码支持手工/HID/广播/摄像头适配（`:445`、`:499`、`:514`），离线保留扫描进度，完成时拒绝无网络提交（`:548`），部分完成要求原因（`:550`）。`CP6.Client.Core/WmsTaskService.cs:120` CompleteAsync 先上传数量扫描，再带 OperationId/RowVersion/ExecutionVersion 完成；网络结果未知时回查任务是否已完成同一 operation，避免把超时等同失败（`:153`）。这是比旧 Web Picking 页更可审计的执行链。

原生成熟度仍受范围限制：移动端核心是任务执行，桌面端核心是管理/网关；本审查没有设备实机、安装包签名、打印机回执和真实弱网证据。`CP6.Client.Tests` 有10个顶层测试源文件覆盖 token刷新、SSO、升级、语言、扫码、心跳、恢复等，存在测试不等于本快照本次通过。

## 8. UI 架构与升级优先级

### 8.1 已有可复用基础

`cp6.web/package.json:29` 起声明 Vue/Element Plus/Pinia/Axios/SignalR、Vue Flow、Konva、Three、pdfjs；版本以此快照文件为准，不做外部最新性判断。`cp6.web/src/main.ts:3` 起安装 UI 与全局 token/样式。`cp6.web/src/styles/tokens.css:1` 定义品牌、状态、字体、间距、圆角和运动；`components/templates` 有 CpPageShell、CpListPage、CpFilterBar、CpFormDialog、CpDetailPanel、CpStatusStrip、CpStatCard，另有 CpTag/CpEmpty/SectionHeader。

CpListPage 已处理统一查询/分页、请求序号避免旧响应覆盖、失败保留原数据（`cp6.web/src/components/templates/CpListPage.vue:5`、`:140`）；VolTable 也有手机卡片与操作权限（`cp6.web/src/components/VolTable.vue:66`、`:77`）。这些是升级资产。

### 8.2 统一程度尚不均衡

在164个 router 直接引用组件中，33个含 CpPageShell、34个含 CpListPage、17个含 CpFormDialog、43个含 CpTag、5个含 VolTable；92个直接含 el-table、106个直接含 el-form。数字可重叠，仅衡量源代码直接标签，不说明“模板之外都差”。ERP/WMS/OA/Space已吸收较多 Cp 组件，Fin/MES/Pur/Plan/PMS多为自己拼装 Element 页面。

暗色模式只有占位：`cp6.web/src/styles/tokens-dark.css:1` 明示 v1.0 占位，html.dark 无变量覆盖。不可在盘点中列成“支持暗色”。多语言已经有五语言与 fallback（`cp6.web/src/i18n/index.ts:15`、`:63`），但新 Studio 大量硬编码中文、WMS 控制台/原生表头存在英文，不能据已安装 i18n 就宣称全页面翻译完备。

复杂单文件组件增加跨领域回归风险：DesignUnderlay 4596行、Login 1827行、FloorViewer 1667行、MenuView1551行、Quotation1226行、BudgetEdit1004行。行数不是缺陷，但这些文件同时承担数据、状态、交互、视觉时，适合按领域状态机/数据加载/composable/面板拆分，而不拆成无意义的碎组件。

可访问性已有局部良好实践，例如侧栏调整使用 separator/键盘/ARIA（`cp6.web/src/views/LayoutView.vue:43`）和 dashboard reduced-motion（`cp6.web/src/views/dashboard/DashboardView.vue:1019` 附近）；也有普通 div 点击卡片、原始表单、图形画布等需浏览器和键盘实测。无法凭 CSS 源码证明对比度、焦点顺序、屏幕阅读器体验或所有屏宽无溢出。

### 8.3 建议的任务分组

1. **先补业务闭环并标明数据真伪。** 旧 Web Picking 收敛 v2；运单号改为真实承运商回执或准确标注本地参考号；NoOp外部接口在 UI/状态中可见；统一 Real/Simulated/Unavailable 和时间戳/来源，不把试算或演示当完成。
2. **统一工作区与权限导航。** 建立模块/角色入口、业务主链跨页链接、404/无权/身份失效/空数据/加载失败规范；清理旧重定向/演示页入口；保留新旧设计器迁移边界。权限异常 UX 与后端授权分开验证。
3. **批量迁移通用业务页面。** 采购/财务/MES 优先统一列表、筛选、列配置、批量操作、表单错误定位、行编辑、详情时间线、导入预览、导出范围和危险状态动作。复用 Cp 模板，保持真实业务规则不变。
4. **专业工作区独立设计。** Space Studio、OA流程/表单设计器、MES排程、WMS现场作业各有不同输入密度/设备/状态模型，不应强塞同一个 CRUD 模板；用一致导航和语义反馈连接它们。
5. **补基于角色、语言、设备和真实链路的验收矩阵。** Web 桌面/窄屏、原生 Android、WPF；不同角色和租户；中文/日文/英文长文本；库存/财务/审批/发布状态变换；实机扫码/打印；刷新/并发/弱网/错误恢复。验收结果应记录来源SHA和后端数据影响。

## 9. 验证边界及避免误读

已有 E2E 可以作为后续入口，但不能被静态报告复述为通过。`cp6.web/e2e/smoke-all-screens.spec.ts:44` 从登录菜单遍历，主要断言页面壳和内容存在（`:23`），并排除 MES Control Tower（`:62`）；它不是所有 standalone/内部页面或端到端业务验收。`cp6.web/e2e/wms-production-console.spec.ts:93` 和 `cp6.web/e2e/space-studio.spec.ts:1501` 拦截 API 返回测试数据，这些测试能证明交互契约，不能证明真实后端/设备/AI可用。

本次未证明：线上页面真实可达、任一角色的实际菜单、数据库已迁移和种子状态、业务存量数据质量、全部接口契约一致、会计/库存正确性、真实硬件联通、视觉还原、性能预算通过、包签名/安装可用或生产验收通过。任何这类结论都需要后续与风险相称的实际验证；本报告没有把静态可见实现或模拟数据改称生产验收。
