### 九、cp6.web/src — 前端基础设施（API封装/状态/路由/组件/工具/类型/多语言）

> 范围：cp6.web/src 下除 `views/` 外的全部 `.js/.ts/.vue`。每文件一句话功能；`types/*` 为 TS 类型/枚举/接口定义，按文件列出但描述合并风格。

#### (src 根)

- `cp6.web/src/main.ts` — 应用入口：先 `initI18n()` 拉翻译再挂载 Vue，注册 Pinia/Router/ElementPlus/全部 Element 图标/`v-permission` 指令，并安装全局 errorHandler 吞掉路由切换时的瞬态 patch 错误。
- `cp6.web/src/App.vue` — 根组件，仅渲染 `<RouterView/>` 并设全局 body 字体。

#### api（公共）

- `cp6.web/src/api/http.ts` — axios 实例工厂：baseURL `/api`、请求拦截器自动带 Bearer Token，响应拦截器统一拆信封/401 跳登录/409 留给业务/其余业务错误码走 i18n toast。

#### api/erp

- `cp6.web/src/api/erp/backorder.ts` — 受注残（缺货队列）API：查询缺货队列、关闭剩余量、拆分为新订单。
- `cp6.web/src/api/erp/businessPartner.ts` — MSBBPA110/120 取引先マスタ API：单取/重复校验/新建(可事前登录)/更新/删除/列表检索/CSV 导出。
- `cp6.web/src/api/erp/creditNote.ts` — 红字/赤伝（CreditNote）API：分页检索红冲单。
- `cp6.web/src/api/erp/estimateCalc.ts` — MSBBPA010 見積計算書 API：分页/按No取明细/增删改/流用复制/计算引擎调用。
- `cp6.web/src/api/erp/fsc.ts` — MSBBPA100 FSC 製品化チェックシート API：检索/取格式/发行/Excel 下载 URL。
- `cp6.web/src/api/erp/fxRate.ts` — 汇率（FxRate）API：列表/按币种解析当日汇率/增删改。
- `cp6.web/src/api/erp/master.ts` — 主数据 API：拠点/担当者/通用码下拉，以及得意先、製品マスタ的共通 Master Popup 查找。
- `cp6.web/src/api/erp/order.ts` — MSBBPA070/080/090 受注 API：CRUD/采番/取消级联/未出荷检索/5 套引入仕様/业务规则校验(可编辑·仕掛·与信)/一覧 CSV/单价订正批量。
- `cp6.web/src/api/erp/orderTrace.ts` — 受注追溯 API：按 webOrderNo 取订单跨模块流转轨迹。
- `cp6.web/src/api/erp/otdReport.ts` — OTD（准时交付）报表 API：汇总/CSV 导出。
- `cp6.web/src/api/erp/plateMold.ts` — MSBBPA140/150 版型（刃版·木型）API：采番/按見積取/历史/增删改与改定/一覧 CSV/标签发行。
- `cp6.web/src/api/erp/product.ts` — MSBBPA050/060 製品マスタ API：5 表 CRUD/采番/复制/仕掛检查/按見積書取部材与基本信息/CSV 导出。
- `cp6.web/src/api/erp/quotation.ts` — MSBBPA030/040 御見積書 API：CRUD/复制/确定与取消确定/发行(PDF)/关联見積計算書候选。
- `cp6.web/src/api/erp/sheetUnitPrice.ts` — シート単価 API：检索/Excel 取込/批量更新。

#### api/mes

- `cp6.web/src/api/mes/mes.ts` — MES 全套 API：製造指図(ME020/030)、製造実績(ME040/050)、品質検査(ME060/070)、不良品(ME080)、生産計画ボード(ME010)、ダッシュボード(ME090)、Phase4 設備/停止记录/OEE。
- `cp6.web/src/api/mes/planAchievement.ts` — 计划达成率（大屏）API：汇总/CSV 导出。
- `cp6.web/src/api/mes/processCost.ts` — A2 工艺主数据 API：工作中心 CRUD、工序费率(生效区间)CRUD。

#### api/sys

- `cp6.web/src/api/sys/dashboard.ts` — 首页仪表盘汇总 API。
- `cp6.web/src/api/sys/dict.ts` — 字典 API：字典类型/字典数据 CRUD 及下拉选项获取。
- `cp6.web/src/api/sys/menu.ts` — 菜单 API：全量取/增改删。
- `cp6.web/src/api/sys/operlog.ts` — 操作日志 API：只读分页查询（add/update/del 为满足 VolTable 接口的占位 reject）。
- `cp6.web/src/api/sys/role.ts` — 角色 API：分页/全量/增改删/取角色菜单/保存角色菜单。
- `cp6.web/src/api/sys/user.ts` — 用户 API：分页列表/增改删。
- `cp6.web/src/api/sys/dept.ts` — PUB 章00 部门 API：树/增改删/移动/设负责人。
- `cp6.web/src/api/sys/rolePerm.ts` — PUB 章02/03/04 权限 API：菜单动作/角色功能权/数据权限/字段权限的读写，及我的动作键·我的只读字段。
- `cp6.web/src/api/sys/userRole.ts` — PUB 用户-角色 API：取/保存用户多角色及主角色、历史迁移。
- `cp6.web/src/api/sys/lang.ts` — 多语言词条 API：CRUD/审校/发布·回滚/manifest（i18n P4/P5）。

#### api/pub

- `cp6.web/src/api/pub/attachment.ts` — 附件 API：列表/上传(草稿token)/删除/重绑/带 token 的 blob 下载与预览对象 URL。
- `cp6.web/src/api/pub/codegen.ts` — PUB 章08 代码生成器 API：取表/保存配置/内联预览生成代码。
- `cp6.web/src/api/pub/seq.ts` — PUB 章05 采番规则 API：CRUD + 预览样本号。

#### api/wf

- `cp6.web/src/api/wf/flow.ts` — OA 流程 API：流程定义存取、起流程提交、任务办理、实例详情(含痕迹)、待办/我的申请/撤回。
- `cp6.web/src/api/wf/form.ts` — OA 表单 API：表单定义存取、表单数据提交。

#### api/plan

- `cp6.web/src/api/plan/plan.ts` — 计划中台 API：MRP 运算(运行/计划订单·净需求钻取/确认·转单·忽略)、品目计划策略 CRUD。

#### api/pur

- `cp6.web/src/api/pur/pur.ts` — 采购全套 API：供应商价表(阶梯价·带价解析)、采购订单、收货、三单匹配、采购申请(PR→PO)、询价比价(RFQ)、外注加工(支給材·成品成本·防吞料对账)、采购对账。

#### api/fin

- `cp6.web/src/api/fin/fin.ts` — 财务总账内核 API：会计科目、记账凭证(状态机)、试算平衡、应付发票/付款、AP 主数据(银行账户·税码)、应收发票/收款、信用控制、报表(BS/PL)、成本核算、会计期间月结。
- `cp6.web/src/api/fin/asset.ts` — A3 固定资产 API：资产分类、资产卡片(启用·折旧排程)、折旧计提(预览·运行·过账·冲回)、资产处置。
- `cp6.web/src/api/fin/bankRecon.ts` — A4 银行对账 API：银行对账单(导入预览·确认·行维护)、撮合(候选·自动·手动·解绑·生成凭证·标记挂起·调节表·锁定解锁)、导入模板维护。
- `cp6.web/src/api/fin/budget.ts` — A5 预算 API：预算方案、预算版本(复制·提交·激活)、预算行(grid·Excel 导入)、成本中心查找、预算vs实际报告与过账前预检。

#### api/wms

- `cp6.web/src/api/wms/bridgeHealth.ts` — 跨模块桥健康 API：取桥事件指标、按事件 ID 补偿重放。
- `cp6.web/src/api/wms/connectivity.ts` — WMS 連携 API：WCS 任务、配送业者(Carrier)发货事件流转、IoT 传感器/读数/告警/模拟。
- `cp6.web/src/api/wms/expiry.ts` — 有効期限 API：临期库存查询、批量废弃处置。
- `cp6.web/src/api/wms/inboundOrder.ts` — 入庫予定（入库单）API：检索/详情/CRUD/确定/取消。
- `cp6.web/src/api/wms/inboundReceipt.ts` — 入庫受入 API：检索/详情/受入登录(同时确定并反映库存)。
- `cp6.web/src/api/wms/kitting.ts` — キット（套件）API：套件主数据 CRUD、套件作业单建/执行/取消。
- `cp6.web/src/api/wms/logistics.ts` — 物流优化 API：越库(CrossDock)、补货(Replenish 含批量生成)、库位优化(Slotting 分析·批准)。
- `cp6.web/src/api/wms/lotTrace.ts` — 批次追溯 API：正向/反向追溯、批次库存汇总、召回标记。
- `cp6.web/src/api/wms/materialShortage.ts` — 材料缺料告警 API：分页检索、解决、忽略。
- `cp6.web/src/api/wms/mobile.ts` — 移动作业指示(WM300)API：任务列表/详情/开始/扫描/完成/取消。
- `cp6.web/src/api/wms/outboundOrder.ts` — 出庫指示 API：CRUD/确定/取消/引当(FIFO+期限)/拣货/出庫(梱包采番)/从 MES 指图·PA 受注自动展开。
- `cp6.web/src/api/wms/outboundRouting.ts` — 出庫ルーティング规则 API：列表/预览命中仓库/CRUD。
- `cp6.web/src/api/wms/paperIndustry.ts` — 纸业特化 API（卷1）：纸卷(消耗·匹配·分切·废弃)、墨水批次(开封·调墨·配色历史)、托盘(组建·移至发货·标记出货)、VMI(客户库存汇总·计费)。
- `cp6.web/src/api/wms/paperIndustry2.ts` — 纸业特化 API（卷2）：余材(匹配·预留·使用·废弃)、版型库存(使用记录·维护·寿命预警)、样品库存(借出·归还·过期·超期)。
- `cp6.web/src/api/wms/qcInspection.ts` — WMS 来料检验 API：检索/详情/从入库或直接建检/保存项目/判定/取消。
- `cp6.web/src/api/wms/reportCenter.ts` — WMS 报表中心 API：月度库存/ABC 分析/呆滞库存/出入库历史，及带 Bearer 的 CSV 下载工具。
- `cp6.web/src/api/wms/rma.ts` — 退货(RMA)API：建单/收货/开始检验/判定处置/关闭/取消。
- `cp6.web/src/api/wms/stock.ts` — 在庫照会 API：库存检索/变动历史/单笔变动应用/棚移动/流水列表，及单件·按工单的 QC 状态设置。
- `cp6.web/src/api/wms/stockDwell.ts` — 库存滞留(Dwell)分析 API：汇总。
- `cp6.web/src/api/wms/stockTake.ts` — 棚卸（盘点）API：检索/详情/建计划/开始盘点/录差/提交/批准/取消。
- `cp6.web/src/api/wms/warehouse.ts` — 倉庫マスタ API：仓库 CRUD + 库位树读写。
- `cp6.web/src/api/wms/wmsDashboard.ts` — WMS 仪表盘 API：KPI/趋势/各仓货值/告警。

#### stores（Pinia）

- `cp6.web/src/stores/counter.ts` — Vue 脚手架自带的计数器示例 store（未在业务中使用）。
- `cp6.web/src/stores/estimate.ts` — 見積計算書向导 store：管操作种别(5×4矩阵)、当前 Step、基本信息/工程明细/计算结果/リサイクル法回写、dirty 标记。
- `cp6.web/src/stores/order.ts` — 受注入力 3 页向导 store：操作模式/步骤、订单头与多明细(details)、当前编辑明细、可编辑·仕掛状态，及明细增删移复·工程/材料/注记更新。
- `cp6.web/src/stores/plateMold.ts` — 版型向导 store：操作种别(登录/改定/编辑/删除/查看)、DTO 与历史、字段编辑权派生、工程→版型分类自动设置。
- `cp6.web/src/stores/productMaster.ts` — 製品マスタ 5 页向导 store：5 页状态(部材/基本/工程/材料/ロット単価)、PK 与服务端状态、buildDto/loadFromDto、部材行增删移。
- `cp6.web/src/stores/permission.ts` — PUB 前端权限 store：缓存当前用户操作键集合(loadMyActions)，供 v-permission 判定 has()。
- `cp6.web/src/stores/businessPartner.ts` — 取引先マスタ store：操作种别、BP DTO 与订正前快照、9 属性 FLG 显隐与发注先/メーカ/有償支給連動规则、FLG 变更不可校验。

#### router

- `cp6.web/src/router/index.ts` — 路由表与守卫：viewModules 路径→懒加载视图映射、静态路由(登录/独立 popup 窗口/Layout 壳)、按后端菜单动态注册子路由(addDynamicRoutes/reset)、beforeEach 鉴权(token/动态菜单加载)并按路径预载 i18n 命名空间。

#### components

- `cp6.web/src/components/VolTable.vue` — 通用 CRUD 列表组件：内置搜索/新增/批量删除/分页，桌面表格与移动卡片自适应，列配置驱动(开关/选项渲染)，依赖 api 对象的 getList/add/update/del。
- `cp6.web/src/components/VolForm.vue` — 通用弹窗表单组件：列配置驱动渲染 input/textarea/switch/select，移动端全屏 sheet 适配，必填规则。
- `cp6.web/src/components/MenuTreeItem.vue` — 递归侧边菜单项组件：支持任意层级，标签优先取 i18n `nav.{id}` 否则用 menuName。
- `cp6.web/src/components/PubImportDialog.vue` — PUB 章07 数据导入向导弹窗：三步(下载模板→上传 Excel→校验结果)。
- `cp6.web/src/components/PubUpload.vue` — PUB 附件上传组件：拖拽上传 + 文件列表(下载/预览/删除)，调用 attachmentApi。
- `cp6.web/src/components/estimate/RecycleLawDialog.vue` — 見積 リサイクル法 A/B/C 弹窗：回收责任/识别表示/单价等输入并回写 store。
- `cp6.web/src/components/master/MasterReferenceDialog.vue` — 共通 Master Popup 参照弹窗(MSBBPACOM)：关键词检索得意先/製品マスタ并选行回填。
- `cp6.web/src/components/HelloWorld.vue` — Vue 脚手架自带欢迎组件（未用于业务）。
- `cp6.web/src/components/TheWelcome.vue` — Vue 脚手架自带欢迎面板（未用于业务）。
- `cp6.web/src/components/WelcomeItem.vue` — Vue 脚手架自带欢迎项布局组件（未用于业务）。
- `cp6.web/src/components/icons/IconCommunity.vue` — Vue 脚手架自带 SVG 图标（Community，未用于业务）。
- `cp6.web/src/components/icons/IconDocumentation.vue` — Vue 脚手架自带 SVG 图标（Documentation，未用于业务）。
- `cp6.web/src/components/icons/IconEcosystem.vue` — Vue 脚手架自带 SVG 图标（Ecosystem，未用于业务）。
- `cp6.web/src/components/icons/IconSupport.vue` — Vue 脚手架自带 SVG 图标（Support，未用于业务）。
- `cp6.web/src/components/icons/IconTooling.vue` — Vue 脚手架自带 SVG 图标（Tooling，未用于业务）。

#### composables

- `cp6.web/src/composables/useBreakpoint.ts` — 响应式断点组合式：监听 window resize，导出 isMobile/isTablet/isDesktop。
- `cp6.web/src/composables/useFieldControl.ts` — 見積字段控制矩阵组合式：按操作种别返回字段状态(E/RO/D/R)、是否禁用/必填/页只读、按钮显隐。
- `cp6.web/src/composables/useLinkage.ts` — 見積 Step1 联动组合式：受注拠点→担当者重载、刃渡り blur→シート寸法、見積数 blur→パレット数自动算。
- `cp6.web/src/composables/useConflictHandler.ts` — 見積計算書乐观锁 409 冲突处理：弹框提示并可一键拉最新版覆盖 store 后回到编辑态。
- `cp6.web/src/composables/useProductConflictHandler.ts` — 製品マスタ乐观锁 409 冲突处理（同上模式，作用于 productMaster store）。
- `cp6.web/src/composables/usePubExcel.ts` — PUB 章07 导入导出组合式：带 token 的 blob 下载，封装 exportExcel/downloadTemplate。
- `cp6.web/src/composables/useValidation.ts` — 見積 Step1 校验组合式：MSG-111~129 element-plus 必填规则 + 业务级手动校验。

#### directives

- `cp6.web/src/directives/permission.ts` — `v-permission` 指令：无该操作权时移除元素(store 未加载完成时 fail-open)，仅 UX 层、强校验在后端。

#### utils

- `cp6.web/src/utils/signalr.ts` — 通用 SignalR 连接单例(/hubs/notify)：构建/启动/停止，含自动重连。
- `cp6.web/src/utils/mesHub.ts` — MES SignalR Hub 客户端单例(/hubs/mes)：构建/启动/停止。
- `cp6.web/src/utils/wmsHub.ts` — WMS SignalR Hub 客户端单例(/hubs/wms)：构建/启停 + 按仓库/产品订阅，并定义 StockChanged 等推送 payload 类型。
- `cp6.web/src/utils/format.ts` — locale 感知格式化工具(i18n P2)：日期/数字/百分比/数量/多币种货币，提供全局函数与 useFormat() 组合式两套。

#### i18n

- `cp6.web/src/i18n/index.ts` — i18n 核心：createI18n(flatJson/回退链/日期·数字格式)，按模式(live _core / publish 版本包)与路由命名空间懒加载词条，含切换语言、ensureNamespacesForPath、dev 伪本地化 QA 语言。
- `cp6.web/src/i18n/keys.generated.ts` — 自动生成的词条 key 类型(MessageKey 联合 3837 条)与类型安全 tt() 触发器，请勿手改。
- `cp6.web/src/i18n/keys.generated.json` — 自动生成的源词条 key 数组（ja 源文案全集），是 `keys.generated.ts` MessageKey 联合类型的数据来源，由抽取脚本产出，请勿手改。

#### types/erp（TS 类型/枚举/接口定义）

- `cp6.web/src/types/erp/backorder.ts` — 受注残队列项/操作请求·结果/查询及 ApiResult 信封类型。
- `cp6.web/src/types/erp/businessPartner.ts` — 取引先 DTO/列表项/查询/操作种别枚举(BpOperationType)。
- `cp6.web/src/types/erp/creditNote.ts` — 红字单列表项/分页/查询类型。
- `cp6.web/src/types/erp/estimateCalc.ts` — 見積計算書 DTO 及工程明细、操作种别枚举、字段状态、主数据(拠点/担当者/通用码)、ApiResult 等类型。
- `cp6.web/src/types/erp/fsc.ts` — FSC チェックシート查询/项目/发行请求·结果/格式类型。
- `cp6.web/src/types/erp/fxRate.ts` — 汇率实体与 WmsApi 信封类型。
- `cp6.web/src/types/erp/order.ts` — 受注 DTO(头/明细/工程/材料/注记)、查询/列表项、单价订正、取消结果、未出荷、操作种别枚举、分页类型。
- `cp6.web/src/types/erp/orderTrace.ts` — 受注追溯结果类型。
- `cp6.web/src/types/erp/otdReport.ts` — OTD 报表查询/汇总类型。
- `cp6.web/src/types/erp/plateMold.ts` — 版型 DTO/历史/查询/列表项/订单关联·PE 连携结果、操作种别枚举类型。
- `cp6.web/src/types/erp/productMaster.ts` — 製品マスタ DTO 及部材/工程/材料/連産品/ロット単価、基本信息、仕掛检查、操作种别枚举类型。
- `cp6.web/src/types/erp/quotation.ts` — 御見積書 DTO/查询/列表项/計算书候选/确定·发行请求类型。
- `cp6.web/src/types/erp/sheetUnitPrice.ts` — シート単価 DTO/导入结果/批量更新/查询类型。

#### types/mes

- `cp6.web/src/types/mes/mes.ts` — MES 全套类型：製造指図/実績、品質検査/不良、计划ボード、ダッシュボード、设备/停止/OEE 的 DTO·查询·分页。
- `cp6.web/src/types/mes/planAchievement.ts` — 计划达成查询/汇总类型。
- `cp6.web/src/types/mes/processCost.ts` — A2 工作中心/工序费率实体与 ApiResp 信封类型。

#### types/sys

- `cp6.web/src/types/sys/dept.ts` — PUB 部门树节点/表单类型。
- `cp6.web/src/types/sys/rolePerm.ts` — 菜单动作/角色权限/数据权限/字段权限相关 DTO 类型。

#### types/pub · wf · plan · pur · fin

- `cp6.web/src/types/wf/wf.ts` — OA 流程/表单/任务/实例相关类型。
- `cp6.web/src/types/pur/pur.ts` — 采购全套类型：供应商价/PO/GR/三单匹配/PR/RFQ/外注/对账的 DTO·表单·结果及 ApiResp。
- `cp6.web/src/types/plan/plan.ts` — 计划中台类型：品目策略/MRP 运行/计划订单/净需求/运行请求及 ApiResp。
- `cp6.web/src/types/fin/fin.ts` — 财务总账类型：科目/凭证/期间/试算/AP·AR(发票·付款·收款·账龄·对账)/银行账户·税码/报表/成本/信用 DTO 及 ApiResp。
- `cp6.web/src/types/fin/asset.ts` — A3 固定资产类型：分类/卡片/折旧分录·排程·运行/处置。
- `cp6.web/src/types/fin/bankRecon.ts` — A4 银行对账类型：对账单/行/候选行/调节表/导入模板·预览结果。
- `cp6.web/src/types/fin/budget.ts` — A5 预算类型：方案/版本/预算行 grid·DTO/vs实际报告/告警/导入预览/成本中心。

#### types/wms

- `cp6.web/src/types/wms/wms.ts` — WMS 主类型库：库存/流水、入出庫、棚卸、仪表盘、WCS/Carrier/IoT、纸卷/墨水/托盘/VMI、余材/版型/样品、QC/RMA/有効期限/Kit/物流优化、报表，及 WmsApi/WmsPaged 信封。
- `cp6.web/src/types/wms/materialShortage.ts` — WMS 材料缺料告警/操作请求/分页/查询类型。
- `cp6.web/src/types/wms/outboundRouting.ts` — 出庫ルーティング规则类型。
- `cp6.web/src/types/wms/stockDwell.ts` — 库存滞留分析查询/汇总类型。
