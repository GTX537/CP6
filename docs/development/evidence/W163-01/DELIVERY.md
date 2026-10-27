# W163-01：客户原身份来源 Owner

COORD 本聊天单人；Main基线 e07d4c733243956d3fa6afa2596a413bce92fa59，固定DEV-1/DEV-2复用。[计划](../../dispatch/20261027-wave163/README.md)。预计60–90分钟，Main官方START至本登记17.4分钟；最终耗时以finish/proof为准，Handoff不重复加时，前置只读调查单列。原CP6-7D-20261022持续目标ACTIVE。

## 实际交付

为WMS-TOOL-01 R2的可信customerRef提供真实来源前置。Main.Erp.BusinessPartnerIdentity.v1沿用T_WebBusinessPartner租户和原UUID，不从客户代码/同名/CRM账号推导身份。最小来源含原BpCd/BpName/BaseCd、Status、CustomerFlg、IsFrozen、IsDeleted，不复制财务、联系人等170字段。IsFrozen是原冻结事实；TradingEligibility固定NOT_ATTESTED，不把客户角色、注册状态或可读取等同交易/信用资格。

PostgreSQL新增cp6_business_partner_identity_v1：原表INSERT/UPDATE/DELETE触发器，单调版本、不可变原JSON/SHA-256、观测时间及sourcePresent。所有原写入（含既有业务服务、EF批量和原生SQL）由同事务采集；回滚同时撤销业务与来源变化。代码点原样保留，ABA即使摘要恢复相同也产生新版本；软删可在当前对象范围读取历史、硬删无当前对象则拒绝披露，重建不复用旧版本。迁移只记录旧行首次实际观察，不虚构先前历史、不改原表已有行。历史/头禁止改写或TRUNCATE，旧迁移不修改，仅前向迁移；本轮只在隔离测试库执行。

新增真实HTTP GET /api/business-partners/identity-owner-v1及/{id}?version=准确版本。独立当前erp-business-partner:query、启用菜单/动作目录、原会话/角色、租户和1–5数据范围；默认self，部门及子部门/自定义必须显式business-partner原UUID归属。分页/计数先过滤对象范围，代码检索不将名字字段变成泄露通道；CustomersOnly仅检查原客户角色，不宣称有效客户。历史读取同样需要当前权限。名称不可读时Source.bpName及OriginalJson同时隐藏，摘要仍指向不可变原件，Projection明确FULL/NAME_REDACTED；不输出170字段原记录。响应no-store。

HoldExact为内部同Main上下文/原事务消费入口，接受Owner/原UUID/准确版本/摘要，锁住原客户来源至提交，最终重新核对当前来源、对象及权限。拒绝跨上下文、旧版本、同事务来源变化和撤权；同事务失败整体回滚。返回与公开读取相同的字段投影，不能借内部入口拿到被隐藏名称。此为已测试的基础能力，后续WMS/ENG仍须真实调用并执行自己的权限、字段和业务合同，不把夹具当作正向业务采用。

原行政归属页已加入“客户原主档”，按真实搜索UUID保存/撤销。读取目录仅登记query，无新增角色自动授权；旧客户CRUD和既有写权限维持原历史范围，本轮没有宣称其完整独立Owner改造，也不改CRM。

## 验证与审查

实现前真实HTTP入口返回404的失败基线保留。实现后12项新增PG18/HTTP验证：原UUID/原Unicode及敏感字段排除、实际分页/名称隐藏/撤权；ABA与原件、源与历史共同回滚和不可变约束；旧客户业务服务建改删；硬删/重建；部门2/3/4显式归属和撤销；当前/历史/内部Hold字段投影；self/跨租户/跟踪对象/跨上下文拒绝；真实第二连接原SQL写入被锁等待，提交后更新，旧Ref和同事务变更/撤权使事务回滚；缺失捕获或原表漂移关闭读取；实际前向迁移首次观察且不自动授角色。5项既有共享回归覆盖产品实际HTTP、实物HoldExact和库存归属2/3/4；5项权限种子单元验证包括多租户幂等和不增加query授权。共22项后端，其中17项真实PG/HTTP、5项InMemory种子，不混称22项数据库验收。全部无跳过。

归属组件26项通过（包含新客户UUID选择和原有保存、恢复、身份边界），vue-tsc及Vite通过。证据D:/CP6-local-test/wave163/foundation/final-validation.json绑定最终源码/测试blobs。未变更的路由认证和旧登记codec沿用W162/W161相应证据，不作为本轮新跑结果。

一次任务级SPEC/QUALITY集中审查覆盖最小合同、当前权限/字段/租户、来源及原交易共同事务、锁与最终版本、不可变历史、原迁移兼容、实际HTTP和行政页面、完整diff和台账。工作流及依赖沿用已核验的相同内容并在push/PR前复核。无Actions、浏览器E2E、真实企业验收、既有环境迁移或生产部署。

## 剩余与收尾

下一项真实接入登记的客户/产品/库位（预计90–150分钟，开工时再核对）：新版输入必须保留旧tool.registration.v1/v2字节编码与恢复；禁止直接向旧序列化DTO加可空字段。客户当前权限与WMS字段/对象范围相交，隐藏名称不得进入可披露原件。库位仍需真实来源版本及Space_Site.WarehouseCd映射，不用Warehouse.BaseCd推断。工程候选、使用/维护/报废、实际工程双Role采用、审批/DOC及ROU/BOM/WO/SCHED均未因此完成。Continue trusted optional customer/product/location sources in actual registration, technical candidates, physical lifecycle and actual engineering dual Role; all original D2/D3/D5/D6/D7 exits remain.

W162归档CLOSED一次，仅关闭WP10-48.wave162_unicode；本轮只登记wave163_customer_source。375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。D1/D4完成，2/7=28.6%仅阶段比例，不代表工作量。原D2/D3/D5/D6/D7和完整WP10-48保持未完成；WP05-05订单请求处理、WP04-32正式Sales仍开放。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/139，source 54536865241dfe9aa9330a5e1df8d8c272784d80，目标 6e9fcda411a246714948e9fbf47b75529d0fc554；准确任务分支已删除、DEV-2已官方finish释放。Main在本报告时待正常PR、远端采用、准确分支删除及官方finish；最终proof记录实际结果。固定目录、依赖连接/缓存和用户未跟踪原件保留。
