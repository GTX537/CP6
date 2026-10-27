# W162-01 / BUG582：登记输入原码点修复

COORD 本聊天单人；Main 基线 6a9554571c510aadfb541fedc85719c5617daec5，官方工作区任务 BUG-582-TOOL-REGISTRATION-UNICODE，固定DEV-1/DEV-2复用。[计划](../../dispatch/20261027-wave162/README.md)，[活动BUG](https://github.com/GTX537/CP6/issues/582)。预计25–45分钟，Main START至本登记26.02分钟；最终耗时以finish/proof为准，Handoff不重复加时。原CP6-7D-20261022持续目标ACTIVE。

## 问题与修复

WMS-TOOL-01 R2 第66行要求摘要保留字符串原码点。W161对核实依据、尺寸说明、备注执行NFC，导致输入被改写，组合形式é与分解形式e+组合重音被当成同一原操作输入。W161报告中的“NFC”和当时无已知问题是历史快照；本修复纠正该不符合原合同的行为。

新登记（CREATE及BIND_LEGACY）使用tool.registration.v2，三类字符串保留原码点/空白，金额仍按原十进制规范串。原输入经严格Unicode检查后才做JSON冻结，非法孤立代理项返回400且不产生业务行、编号或原操作，避免编码时替换字符。合法补充平面字符继续支持。新操作同键相同原文返回原结果；不同码点即使显示相近也409，不重复创建实物或编号。

Main.Wms.ToolRegistration.v1 Owner身份、对象原UUID、登记原件版本1及摘要机制不变。新版原件及结果明确InputContract=v2；读取旧JSON缺字段时识别为v1。已保存v1的input/result/original字节和SHA不变，重放使用该记录原codec，包括其历史NFC及金额规范化语义；不能声称能区分已经丢失的旧原码点。返回v1来源标记，实际页面显示“旧版规范化文本，无法还原输入时的原始字符序列”。新键不能选择旧codec。旧CLOSED键仍拒绝迟到提交。

全部写入继续在原Main PostgreSQL权限/对象/字段/部门/仓区/准确Site/native及最终授权事务内。原actor在撤销add/initialize后可按当前query和对象/历史范围恢复；撤销query则拒绝。只读恢复不重新执行业务，不更新历史登记。W160 profile编码未更改，无数据库迁移或既有环境改写。

## 实际验证与审查

首次回归文件缺少测试命名空间导致编译失败，修复后在未改产品代码的真实隔离PG18上5项行为回归全部失败：新建三字段原码点丢失、旧初始化依据改写、同键异码点未冲突。两次失败日志保留，未把编译失败当作业务复现。

最终37项真实PG18/HTTP通过：9项新增修复相关测试，28项既有实物来源/登记/新租户权限种子回归。覆盖三字段原文与重放、旧初始化原500及未知终身次数、新旧codec、实际历史v1 JSON形状的CREATE/BIND原记录及CLOSED键、原字节不变、撤写权限恢复/撤query拒绝、非法Unicode零副作用，实际HTTP新建/重放/冲突/读取。兼容测试以隔离数据库构造准确旧结构原件，是兼容夹具，不是企业生产数据。既有Site并发保护、权限失败回滚、历史变化检测等回归通过。9项真实组件UI测试通过，含原文提交、旧版本来源提示及原有恢复/身份边界；vue-tsc与Vite通过。无跳过。

证据 D:/CP6-local-test/wave162/foundation/final-validation.json 绑定最终源码/test blobs。未受影响的路由/认证行为沿用W161记录，不冒充本轮新执行。只做一次任务级SPEC/QUALITY集中审查：编码版本隔离、旧原件不可改、合法字符串与金额边界、实际恢复/权限/事务、页面隐私、完整diff/链接/状态约束。工作流及其依赖保持已核验内容，push/PR前复核；无Actions、生产部署、既有环境迁移、浏览器E2E或真实企业验收。

## 剩余与收尾

原七天全部出口保留。此修复不完成可信非空客户/产品/库位关联、技术候选、实际工程双Role、使用/维护/报废或完整WP10-48。已定位真实Space_Site.WarehouseCd仓库映射；后续必须结合当前来源/授权和版本，不能直接以代码字符串绑定。Continue trusted optional customer/product/location sources and technical candidates, actual engineering dual Role and physical lifecycle; all original D2/D3/D5/D6/D7 exits remain.

W161归档CLOSED一次，仅关闭WP10-48.wave161_registration；本轮只记录wave162_unicode限定修复。375正式卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。D1/D4完成，2/7=28.6%仅阶段比例，不代表工作量。

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/138，source bf64335d2bb5c35a26e4cffc6885fb81287b2358，目标 fdc0758b4da6424a59a1f0879239d98fa74e6b71；准确任务分支已删除、DEV-2已官方finish释放。Main在本报告时待正常PR合并、远端采用、Issue关闭、准确分支清理和finish；最终proof记录实际结果。固定目录、依赖连接/缓存和用户原件保留。
