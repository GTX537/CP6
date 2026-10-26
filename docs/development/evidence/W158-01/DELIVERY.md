# W158-01 旧版型写者、共同事务与原订单请求

COORD单人；Main基线200b99977a553852b5b1e17578cbcba363554308；固定DEV-1/DEV-2。预计90–150分钟，Main官方START至本登记35.43分钟；实际结束以finish/proof计时，Handoff不叠加。[原计划](../../dispatch/20261026-wave158/README.md)。七天目标ACTIVE。

## 实际交付

PostgreSQL原PlateMoldService的create/revise/update/delete与对应旧HTTP写路由，统一进入PlateMoldWriteOwnerV1。缺Owner、原操作UUID/理由、ExpectedNativeRowVersion或ExpectedTechnicalControl直接拒绝；旧仅rowVersion删除不能绕过。CREATE只接受明确absent/new draft；已有对象必须匹配当前原生令牌和准确观察Ref。此技术知识为TECHNICAL_CONTROL_UNPROVEN，来源观察不是专业批准/冻结；原Rev、传输0/9与物理资格独立。不得读取latest替调用者补写缺失前置。

当前工具action/query/对象范围、原Order对应action/字段/可信部门/完整图范围及最终授权在同Main PostgreSQL事务核对。创建分配原MLD编号；修订只使用准确最大前驱，新增Rev+1并关闭前驱适用期，保留原历史观察；未来版本、非法区间和陈旧前驱拒绝，并发仅一个成功，不用max+1重试替换用户意图。原字段合法域、完整40位工具编号/20位BaseCd和decimal(15,4)保持；拒绝数据库隐式舍入。修改/删除保留原身份及传输事实。未新增数据库迁移或自动回填。

CREATE/REVISE通过W157原OrderWriteOwner/ReadOwner在同事务创建真实原订单、行及唯一关联，商业状态OWNER_APPLIED仅指原订单创建。UPDATE/DELETE由原OrderWriteOwner耐久受理CHANGE_REQUEST/CANCEL_REQUEST，绑定真实工具观察、原Order/Line UUID和原令牌，保存商业提案与理由、不可变Main操作原件和审计，状态REQUESTED。原Order默认CONFIRMED，不能用传输0或无履约猜成可删草稿；本轮不改不删该原订单及下游事实。缺失/多义/共享关联不自动重建。没有调用PE stub或外部桥，PE状态NOT_EXECUTED；仍是NATIVE_ORDER_LINK_ONLY，不是正式Sales发布。

工具变化、来源观察、原订单创建或耐久商业请求、两侧原结果和审计一起提交或回滚。相同原键和完整输入回放原结果，异体拒绝；提交ACK丢失查询COMMITTED，UNKNOWN可继续查询或close，CLOSED拒绝晚到写入。恢复只需当前原对象和原订单query范围及原actor，撤销写权限后仍可核对；恢复历史原件不伪装当前版本。原编辑读取、列表和历史按当前工具对象范围；关联读取仍核对原Order权限。旧HTTP安全错误保留403/412/409/503，不被原InvalidOperationException catch压成400，实际CSRF/no-store保留。

旧编辑页和列表实际采用：列表准确编号+rev进入原编辑；加载显式原生/观察/原订单版本；保存冻结原输入，创建使用明确新草稿前置，删除发送完整body版本，不再只有query rowVersion。当前add/edit/del控制相应操作，去除假定admin=true。原精度2的小数控件恢复4位。页面显示技术批准未证实、实物资格未知、PE未执行及原订单请求已受理未应用。响应丢失只存租户/actor命名空间的原键和动作种类，不存表单/金额，Web Locks排他；原键查询/结束、版本冲突保输入和原版本、不自动重新提交。当前身份变化/401/403清理私有表单/列表、旧响应丢弃。成功后可明确重新载入准确原记录。

## 验证与集中审查

13项新PG18/实际HTTP行为检查，加15项W157原订单关联回归，共28项通过：实际CRUD、精度和原actor、缺版本和缺Owner绕过拒绝、原键重复/异体、确认订单不变与耐久CHANGE/CANCEL请求、准确前驱/适用期/历史、native与观察冲突、缺失关联、原订单版本/动作/字段/完整范围、丢失ACK及撤写后恢复/close晚写、最终授权失败全回滚、非法精度/未来版本、两个实际DbContext并发修订、四旧HTTP路由/CSRF/错误码/no-store/当前撤权、实际列表历史对象过滤。W157原UUID链接/同键并发/原件不可变/字段和租户范围全部回归通过。作用域仅本次改动，无无关全仓回归。

47项UI/router/auth行为通过，其中15项新编辑/列表行为、9项原链接面板和23项原路由/认证。vue-tsc和Vite通过。初始不存在路由405、测试source.owner类型和三项列表按钮文案夹具失败保留；修复夹具后只复查三项列表，另外44项输入不变并复用；type/Vite的产品输入未变，只改测试按钮字面量。后端三次成功运行只补并发/HTTP和范围测试，产品无后续变化。证据D:/CP6-local-test/wave158/foundation/final-validation.json，所有失败日志保留，无跳过。

一次执行者SPEC/QUALITY集中审查完整任务差异，核对实际Owner/同事务/最终授权、不可变原结果、请求与应用区别、版本/精度、原页面和既定范围；无待修必需项。不声称浏览器E2E或真实企业验收。无Actions、部署、既有环境迁移或保护更改。

## 剩余范围与收尾

专业工具技术控制Owner、批准冻结、实物资格与DOC仍未交付；当前显式UNKNOWN与观察Ref只防止以旧原件写入。原Order请求处理/批准/实际应用仍待独立原Owner生命周期任务，不能将REQUESTED称为业务已变更/已取消。旧估算/产品导入、标签和序号读取的全部独立Owner收敛未作完整验收；本轮写者和列表/历史范围之外不扩张完成声明。完整WP05-04/05/04-32、正式Sales稳定来源以及ROU/BOM/WO/SCHED仍未完成。Continue original WP05-04 professional technical control and physical/DOC qualifications, WP05-05 original Order request processing and WP04-32 formal Sales source gaps; then original ROU/BOM/WO/SCHED dependencies. All D2/D5/D6/D7 exits retained; no fabricated qualification or approval.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/134，source 8e997d2c68ece002b7bdd3c1e9b425711fd8c4c9，目标6f3d7853d3e9348128db54fb191ed8fca9e432b9，准确分支已删除、DEV-2官方finish释放。Main本记录保留正常PR/远端采用/分支删除/finish之前的真实快照；最终proof确认生命周期。固定目录、Main依赖连接和CRM缓存保留。

W157只归档一次并关闭WP05-05.wave157_order_link，本轮仅WP05-04/05新增wave158_legacy_writers限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。只有D1/D4整阶段完成，2/7=28.6%是阶段比例，不是工作量完成百分比。
