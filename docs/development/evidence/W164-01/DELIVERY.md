# W164-01：可信来源关联实物登记

COORD 本聊天单人；[计划](../../dispatch/20261027-wave164/README.md)。Main基线 25e5ca250f51ce40f042671cecfdae6b37cabd39，固定DEV-1/DEV-2复用。预计90–150分钟；Main官方START至本登记25.46分钟，最终耗时以finish/proof为准，Handoff重叠不相加，前置只读核对单列。原CP6-7D-20261022持续目标ACTIVE。

## 实际交付

实际WMS实物登记已有可选客户、产品和库位来源：客户沿用W163 Main.Erp.BusinessPartnerIdentity.v1 的租户/原UUID与不可变版本，要求原客户角色且非删除状态；这不是信用/交易资格。产品沿用Main.Erp.ProductIdentity.v1商用主档原UUID/版本/摘要，不宣称工程资格。产品名称是该版本下原生SetProductName的显示观察，明确不伪称旧身份payload已有此字段；原表每次UPDATE都会使版本前进，名称单独变化同样令旧Ref失效。

库位/仓库采用实际原UUID、原生PostgreSQL8字节单调RowVersion（Base64）及最小身份摘要，名义来源Main.Wms.LocationIdentity.v1 / Main.Wms.WarehouseIdentity.v1。校验原生行版本触发器启用，原库位和仓库代码唯一、未删除且库位未阻塞；当前独立query/对象/仓库区域权限均须满足。使用Space_Site.WarehouseCd显式映射、为空时SiteCode，绝不按Warehouse.BaseCd或名称推断。站点原版本和本次映射观察一并保留；不改变旧站点JSON，不宣称已有独立库位历史API。

POST /api/wms/specials/v1/tools/linked-registration 及 PUT /api/wms/specials/v1/tools/{id}/linked-registration 使用独立wrapper（registration + links），输入合同tool.registration.v3，Owner仍为Main.Wms.ToolRegistration.v1。旧ToolRegistrationCommandV1字段、v1/v2编码及保存原件不修改；v1继续按历史NFC规则比较，v2/v3保留原Unicode文本。相同原操作换合同或改变来源均拒绝，原CLOSED不重开；丢失响应仍按原operationId查询或关闭，不重复消耗PLT2编号或产生实物。

新CREATE在同Main事务读取和锁住准确来源，填写原生CustomerCd/Name、ProductCd/Name、WarehouseCd/LocationCd，再保存实物、来源记录、登记、编号与原操作结果。当前来源权限与WMS写字段权限相交；来源客户名被隐藏时新实物名称保持null。BIND_LEGACY必须给出原ETag/实物Ref，所选来源逐项精确匹配原代码；不修改原名称、位置、计数、维护状态或原实物版本。留空不会按代码自动补绑，旧有未对应代码以LEGACY_UNMAPPED表示；新未关联字段UNLINKED。

PostgreSQL前向迁移20261028000000_ToolRegistrationLinksV1创建cp6_tool_instance_v1.registration_links。租户/对象键、原登记FK、冗余UUID/码值与JSON的一致性检查、SHA256、UPDATE/DELETE/TRUNCATE禁用及PUBLIC权限撤销；不回填旧v1/v2。关联原文含明确原来源Ref及本次显示/位置观察，属于内部原件；其LinksDigest写入并受原登记摘要保护。公开登记只返回经字段投影的关联Ref/状态，不公开原关联JSON或来源隐藏名称。新增库只在隔离PostgreSQL18测试环境迁移。

关联后的实物列表/总数/分页、当前/历史单读、普通资料写入和原操作恢复都再次使用各来源当前query及原UUID对象范围。过滤发生在Count/Skip之前，撤权不影响无关联历史；原生关联码漂移时关闭披露，避免把新代码冒充旧UUID。来源客户BpName权限与WMS客户名称字段权限共同控制快照读取。当前来源名称或代码改名不改写历史关联原件，也不因版本前进重复执行已提交登记。普通库存/StockTransaction、工程候选或执行许可均无本轮副作用。

实际页面可搜索并选择当前允许的客户/产品/站点库位，发送原来源Ref；切换站点清除库位，来源刷新不自动替换旧选择，412保留输入和旧来源供用户核对，身份变更或403清除私密状态。未知结果只在本地保存operationId/objectId/kind，不保存来源、客户名称或依据文本。

## 验证与集中审查

实际HTTP新入口405失败基线保留。开发中的插值编译错误及组合夹具重复授权冲突均保留并修复，未伪造成功。最终12项新增PostgreSQL/真实HTTP覆盖三类真实来源正向选择和登记、不可变关联/摘要/原操作重放、未知关闭、当前字段与各来源query撤销、部门原UUID归属与撤销、分页前过滤及历史/恢复、源版本ABA/名称变化、错误站点、区域范围和跨租户、旧实物正向匹配保留整个原快照/ETag、隐藏客户名不落盘、同事务产品变化导致实物/编号/关联/原操作整体回滚、真实第二连接写入等待及提交响应丢失后唯一恢复。

36项既有实物Owner及登记回归通过，覆盖原v1/v2字节和Unicode兼容、ETag、字段、部门/区域、历史、并发及恢复；未变更的全租户种子沿用W161/W162证据，不重跑昂贵的累积全租户扫描。合计48项PG18/真实HTTP，无跳过。9项新增与36项回归在相同生产代码运行，随后只新增3项边界测试并通过；此前测试方法和生产代码未变，不因测试文件新增无关方法重复全跑。

12项Vue组件检查通过：原登记行为/恢复、准确来源Ref、站点切换、412保留、来源403清除及身份切换后的迟到响应。vue-tsc/Vite通过。证据D:/CP6-local-test/wave164/foundation/final-validation.json绑定最终生产源码和所审查测试，明确各结果真实来源及复用范围。一次完整diff的SPEC/QUALITY覆盖以上合同、权限、原子性、来源锁/提交前复核、不可变历史、页面及台账。无浏览器E2E、Actions、真实企业验收、既有环境迁移或生产部署。

## 剩余和生命周期

Continue technical-tool candidates, physical lifecycle and actual engineering dual Role; preserve all original D2/D3/D5/D6/D7 exits, Order request processing and formal Sales. 工程技术候选、实物使用/维护/报废、双Owner双Role采用、审批/DOC及ROU/BOM/WO/SCHED不因本次关联完成而关闭。客户完整170字段CRUD、交易资格、独立库位历史、旧记录回填不在本范围。原WP05-05订单CHANGE/CANCEL仍为REQUESTED、WP04-32正式Sales仍开放。

W163归档CLOSED一次，仅关闭WP10-48.wave163_customer_source；本轮登记wave164_registration_links。375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。D1/D4完成；2/7=28.6%只表示阶段数，不能充当实际工作量完成率。完整WP10-48和D2/D3/D5/D6/D7仍未完成。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/140，source 3b99dcb9ce298c351bb2a886e36c5623a3a894f6，目标 b80f0efaf4a29aa6f69565ae83ef7eb1c36b59d2；准确本轮分支已删除、DEV-2已官方finish释放。Main在本报告时待正常PR、远端采用、准确分支删除及官方finish；最终proof记录实际结果。固定目录、依赖连接/缓存与用户未跟踪原件保留。
