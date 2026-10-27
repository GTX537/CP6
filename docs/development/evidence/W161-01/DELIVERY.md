# W161-01 实物新建与旧记录站点/周期来源

COORD 本聊天单人，Main 基线 54be36cf2e05027eb2574106aa98b160cabe81e8，DEV-1/DEV-2 固定目录复用。[原计划](../../dispatch/20261027-wave161/README.md)。预计50–80分钟；Main 官方 START 至本登记 21.11 分钟，最终耗时见 finish/proof，Handoff 不重复加时。七天持续目标 ACTIVE。

## 实际交付

真实 ToolRegistrationOwnerV1 接入原 /api/wms/specials/v1/tools 与原实物页面。POST 新建未经使用、未关联客户/产品/库位的实物，实际 T_PlateMoldStock UUID 和 PLT2 编号；复用 Main 同一 DbContext 採番，检查重复/耗尽，不创建 Stock/StockTransaction 或工程对象。类型、色数、日期、decimal(18,2)成本字符串、尺寸、次数上限和备注按原合法边界。规范金额及 Unicode NFC 进入独立 tool.registration.v1 原输入摘要，null/空串保留；W160 profile 原输入编码和恢复协议不变。

新建要求当前 add 和 query，可信 creation authority version 与本人/部门/子部门/自定义/全部范围；实际新对象部门映射能力不可用于旧对象重绑。成本/备注的当前写策略和实际仓区范围在同一事务内核对。未定位新实物只在实际通配仓区范围内可见，不自动赋予权限。角色/字段/组织/当前身份最终检查，失败回滚编号、原业务行、来源版本、映射、登记、原结果及审计。

Sites 从真实 Space_Site 原件选择，持有 space-site 当前 query，引用 Main.Space.PhysicalSiteIdentity.v1 的准确版本/摘要；停用、跨租户、伪造或改变的 Site 不被采用，不从 Warehouse.BaseCd 推导站点。旧记录 PUT registration 另须当前 initialize 动作、原 If-Match 和原 native SourceRef；登记一次真实站点和周期观察起点，原 UsedShots=500 继续500，历史周期序号与终身次数缺证仍 UNKNOWN。没有自动改旧仓库/库位/维护/工程状态。

新 cp6_tool_instance_v1.registrations 是独立不可变原件（tenant/object/native revision 外键、SHA256、防改删/truncate），包含原 actor、准确 Site/native 引用、核实依据、稳定周期起点 UUID。真正新建可证明初始周期1、当前和终身次数0；旧数据只证明本次观察起点，不伪造历史维护轮次。每次读取扫描起点后的真实不可变原生观察；UsedShots、LastUsedAt、维护日期或状态曾发生未核实变化，即降为 RECONCILIATION_REQUIRED，当前次数/周期编号/终身次数不再显示旧确定值，即使随后改回也不恢复。单纯 profile 变动不破坏初始计数证据。

原操作同键同规范输入回原结果，异义409；恢复只需原 actor 当前 query/对象和原历史仓区，不以已经撤销的 add/initialize 再授权写入。UNKNOWN 查询/close，CLOSED 拒绝迟到提交。内部 HoldExact 联合当前 native、独立登记和 Site 来源，在实际 Main UoW 内保护至提交；校验明确 query 能力和同 DbContext，最后重读 Site 可捕捉本事务内修改。实际另一个连接更新 Site 必须等待读者提交。此参与者不签发资格，也未冒充 ENG 双 Role 已采用。

原页面提供新建、旧记录站点登记和计数来源查询。选择准确实际 Site、明确所属部门；成本保字符串，未知计数有独立显示。冲突保原输入及版本，原登记恢复只保存 tenant/actor 命名空间 operationId/objectId/kind；Web Locks、提交前当前 scope、身份更换/权限失效清私有状态及迟到响应隔离。无自动重发或 latest 回填。当前使用资格始终 UNKNOWN。

## 验证与集中审查

28 项真实隔离 PostgreSQL18/HTTP：14 新登记相关（含新租户权限定义），14 既有实物来源/资料回归。覆盖零值新建/不建库存、旧500和未知寿命、独立 initialize、原 ETag/来源、三种部门授权与撤销、Site版本/当前 query/停用/跨租户、变化又改回、规范金额与原键恢复/close、成本与仓区回滚、并发一次登记、最终权限失败回滚、同 UoW/错上下文/错误动作/另一连接 Site writer、真实HTTP的CSRF/严格请求/当前创建版本。45项 UI/router/auth（8新登记页、10原资料页、27路由/认证）通过；vue-tsc及最终Vite构建通过，无跳过。

首次22/26通过，4个真实失败保留：测试在同租户创建重复 SiteCode 导致三项失败；故障注入仅拦截 reader，却用 INSERT nonquery 作触发，另一项没有命中。修正真实测试设置后27行为项全过。之后仅补新租户 query/initialize 定义及定向种子验证，复用未变27项行为结果，不把复用写作新执行。UI仅最后文字明确“登记起点/待核对”，45项及type结果复用，最终Vite重建。证据 D:/CP6-local-test/wave161/foundation/final-validation.json，绑定最终文件 blobs。

执行者一次 SPEC/QUALITY 集中审查完整任务 diff：真正新/旧语义、权限/部门/仓区/字段、Site 与 native 事务保护、原结果和最终回滚、不可变历史/精度、实际页面恢复/隐私/身份与原完整范围。检查输入、差异、链接和台账约束，无未解决必需问题。所有工作流/依赖不变，push/PR前复核既有手动触发结论；未触发 Actions。

## 剩余范围与收尾

**这是限定的实际新建/旧登记交付，不是完整 WP10-48 或七天完成。** 非空客户/产品/库位的可信选择及关联仍需原 Owner 来源；本轮版本只交付明确未关联的新实物，不改变完整原合同的合法输入。纠正 Site 重绑、工程技术候选与正式双 Role、实际使用/维护/重置/报废及业务资格都未交付。迁移只用于隔离 PG18 验证，未运行既有环境迁移、浏览器端到端、真实企业验收或生产部署。Continue WP10-48 actual trusted optional customer/product/location links and technical candidates, then real WP05-06 dual-Role adoption; formal lifecycle/use/maintenance/retirement, approval/freeze/DOC and all original D2/D3/D5/D6/D7 exits remain.

W160 完整归档一次，只关闭 WP10-48.wave160_physical_provider。本轮只加 WP10-48.wave161_registration 限定记录；375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态保持。D1/D4整阶段完成，2/7=28.6%仅阶段比例，不是工作量百分比。

Handoff 正常 PR https://github.com/GTX537/CP6.AI-Handoff/pull/137，source 9019c444ec7ee270a4190cb1187bfe04e071d248，目标 fb443d80a111982c403d4432bce1a56f7d4fc5c6，任务分支已删除、DEV-2 已官方 finish 释放。Main 本报告是正常 PR/远端采用/分支删除/finish 前的真实快照；最终 proof 补齐完整生命周期。固定目录、Main 依赖连接和 CRM 缓存保留。
