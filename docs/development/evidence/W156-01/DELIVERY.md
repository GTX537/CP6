# W156-01 版型工具准确原来源与当前Owner读取

本聊天COORD单人，Main基线8d678d4803657e0866bc825589065d29d3d89085，固定DEV-1/DEV-2。[计划与范围](../../dispatch/20261026-wave156/README.md)。预计60–100分钟，Main START至本登记22.01分钟；最终finish/proof计时，不叠加Handoff。原七天目标ACTIVE。

## 实际交付

Main.Erp.PlateMoldSource.v1读取真实T_PlateMold的完整BaseCd20/WdPtnNo40/WdRev及原UUID/租户。原表仍为旧主账，No+Rev全局唯一约束与历史订单15位映射未改。新增PG前向迁移，原INSERT/UPDATE/DELETE在同一事务记录不可变技术来源JSON/SHA256、观察令牌、观察时间和单调head；禁止原件更新/删除/截断及硬删UUID复用，捕获回滚随原业务事务回滚。原记录无变化不升观察版；商业金额/客户供应商/修改时间字段不进入技术原件，商业修改仅改变当前原生令牌。迁移只记录当前实际观察，不改旧行、不造此前历史、不授角色。

完整身份、原技术参数/纸构成/有效期、八附件标志和必要物保留；decimal(15,4)按字符串传输，null保持null。旧业务修订、观察版本、传输状态、原生令牌分别返回，技术批准与实物资格固定UNKNOWN。旧代表产品编码、日期、传输9和附件勾选均不作为准确产品版本、执行许可或受控文件证明；旧实物样字段仍原表保留，未伪装WMS实例账。

当前erp-plate-mold:query和当前租户/创建人/可信部门原UUID控制列表、当前、指定观察历史。IAM登记plate-mold原对象，数据库映射检查租户与原对象，query只入目录不自动授权。读取同Main事务锁住原表/原件，确认捕获触发器和当前原件与原行一致，检查摘要；缺捕获/漂移拒绝，历史读取也须当前原对象可见。内部CurrentExact要求相同DbContext/事务栅栏、真实原query授权和精确Owner/UUID/version/digest，过期引用拒绝；只证明本次原来源观察，尚无ROU/BOM业务采用。

API /api/plate-molds/source-owner-v1提供scope、分页检索和UUID/准确观察版本读取，no-store。/erp/plate-mold-sources为受query保护的独立只读页面，从旧版型登记/列表两页进入，展示完整身份、原值、观察历史和未知资格；401/403与身份切换清理来源，过时响应不重新披露。

## 验证与审查

本地隔离PG18/实际HTTP17项、权限目录单元5项、UI/IAM/router/auth52项不同检查通过，无跳过，类型检查和Vite构建通过。覆盖完整40位同前15位不同身份/业务修订、20位基地、极值与null、真实商业更新令牌区分、技术变更历史、回滚/并发单调捕获、硬删UUID禁止复用、不可变原件、触发器缺失/原行漂移、当前query/创建人/租户/部门2/3/4及禁用映射、内部同事务/错误Owner/旧Ref、真实HTTP与query目录无新角色授权、旧库存部门/设备Owner兼容、UI历史/403/身份切换和真实路由直接访问。

证据D:/CP6-local-test/wave156/foundation/final-validation.json。13项新PG/HTTP及5项目录取source-isolated；4项旧共享行为取shared-compat（未改编译输入）。ui-first50，ui-route19替代此前17项路由结果，去重52；type/Vite应用输入未改变，之后仅新增路由测试，不重建产品。初始入口405及夹具21位长度/共享全局No+Rev冲突失败保留；只修夹具与目录预期，未放宽业务约束。一次执行者SPEC/QUALITY集中审查覆盖全差异/当前授权/原件/事务/精度/UI与原范围，无待修必需项。未做浏览器E2E/真实企业验收，未运行Actions、部署或既有环境迁移。

## 剩余范围与收尾

旧writer的expectedNativeRowVersion+expectedTechnicalControl、CAS/改定/有效期与原命令共同事务、原Order Owner联动和PE仍未由本来源窗口完成；正式工具技术批准、实例/DOC、工艺冻结及WO/SCHED保持后续真实采用。Continue WP05-04 legacy writer native-token/technical-control and original Order Owner transaction compatibility; retain routing/BOM/WO/SCHED and every original D2/D5/D6/D7 exit. No fabricated external prerequisites.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/132，source 35076a7cdb7a871588bff26d43b9f0b860505db1，目标 916669c45ffad7e2be486ba5341eb7241e8a1ea3；准确分支删除、DEV-2官方finish释放。Main保留真实收尾前快照，正常PR/精确远端采用/准确分支删除/finish由最终proof确认。固定目录和缓存保留。

W155仅归档一次并关闭WP08-16/WP10-47.wave155_time_basis；本轮仅新增WP05-04.wave156_tool_sources限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。仅D1/D4整阶段完成，2/7=28.6%为阶段比例，不能作为工作量完成百分比。
