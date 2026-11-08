# W319 原个人FORM两卡整卡核销

W319按原WP12-03/04实施范围及FORM R1直接25条AC完成整卡核销；旧端点统一适配、合法数据限额、准确重基前恢复与历史内容当前授权采用均已有真实交付，两张原卡正式DONE。

基线 `a95fb085f906eda5840060d1ed2d002425ec26db`，COORD本对话单人，固定DEV1/Main与DEV2/Handoff。已完整读取准确FORM R1原件189行（SHA1099d259…），核[WP12-03](../../task-plans/WP12.md#wp12-03)与[WP12-04](../../task-plans/WP12.md#wp12-04)三项实施范围及各自正反验收；直接25条为F02六条/F03八条/F04六条/F05五条。F01/F06属于原目录/检索卡，不借本窗关闭；原35条设计验收/全目标状态仍单列。

## 原直接AC与当前交付

| AC | 对应真实行为和适用性 |
|---|---|
| F02-01 | W275/W278本人Active、准确pin、原命令仅一草稿且无实例任务；W316旧Create共用该Owner。 |
| F02-02 | W275 partial类型/形状/未知字段；W285真实FILE、W287选项；BUG756满额UTF8/深度/精确数值补齐，W316旧HTTP完整限额回归。 |
| F02-03 | W275 Owned/tenant/currentActor；W316旧入口不接受替人；W318实际HTTP另一用户404、不披露旧内容。TASK代理不授草稿编辑。 |
| F02-04 | W275双连接保存CAS和W278页面；W316失败保行/原意图，当前Save按原pin合并并保隐藏值。 |
| F02-05 | 当前DraftsAsync仅本人Active、updatedUTC/id稳定序和摘要；W316旧List不返回DataJson；W285详情/原件下载当前授权。W318旧目录内容另按准确当前映射授权。 |
| F02-06 | W275停用/上下文变化；当前Save直接采用原Publication和当前mask，不用最新Schema；Submit需当前activation。W282退回实例独立，未复活Submitted草稿。 |
| F03-01 | W275/W278准确bound FormData/instance/pin；W304真实两连接不同命令争同draft仅一提交，W316旧提交同Owner。 |
| F03-02 | W275原槽同输入重放/异输入拒绝；W316实际旧SubmissionKey固定映射、原hash/原bytes和所有ID恢复。 |
| F03-03 | W304 DIRECT/DRAFT五项expected分别不等均拒绝、保Active/原data/token；W316旧请求缺准确前驱必须新GET，封闭DTO不静默接替换Data。 |
| F03-04 | W304首真实人工任务CHECK失败整组回滚、同意图恢复；W275通知/最终授权失败及确定延迟COMMIT拒绝。当前FormData、runtime、draft、history、receipt/Outbox仍同事务。 |
| F03-05 | W275/W316合法unbound只有FormData且instance=null；当前policy显式allowsUnbound，无绑定本身不授许可，页面区分未启动审批。 |
| F03-06 | W275原槽actual/tenant/environment/operation/resource；W316另一actual相同旧SubmissionKey不认领历史或他人原结果，当前read/字段/FILE再次投影。 |
| F03-07 | W275真实commit后故障与W278页面丢答；W316旧同原键恢复和W304异新键不假报胜者成功，W318原键恢复未破坏旧链。 |
| F03-08 | W275 native runtime guard、W282 APP/SFS分流；普通FORM不写Owner冻结Subject，APP应用仍由真实业务Owner负责。 |
| F04-01 | W293精确source/target/digest、可见path分类/default；W303补隐藏处置消费，PersonalFormRebaseV2内核相对该交付未变。 |
| F04-02 | W293/W275准确preview与明确可见损失确认；W303隐藏有值损失必须独立真实处置Ref，盲confirm拒绝。 |
| F04-03 | W293 table逐列/逐行诊断，MaxRows缩小保所有原行并阻Submit；W302/303实际HTTP/Chrome无临时越权、不泄路径或值。 |
| F04-04 | W275预览过期，W293准确source/target；W303原源/当前政策/后发目标/处置期限改变拒绝；当前RebasePlan门内重算摘要。 |
| F04-05 | W293 requiredAfterRebase/动态规则及逐行诊断；重基后可留Active，Submit按准确新schema完整复算/验证。 |
| F04-06 | W275/W303原重基命令保同token/receipt且处置只消费一次；原§6独立恢复要求由W317精确before快照→新draft和两次动作链补齐，当前原生恢复/FILE/未知回归在W318再验证。 |
| F05-01 | W275仅本人Active可删、Submitted不可删；W316旧DELETE共用ChangeDeleted墓碑，不再由HTTP调用物理Remove。 |
| F05-02 | W275真实submit/delete竞争仅一合法终态；W304不同submit竞争是补证，未冒称替代原竞争。共同IAM门和同draft控制仍适用。 |
| F05-03 | W275删除原键/CAS；W316旧If-Match无损映射和原结果，404不伪装已删。当前restore有准确留存/政策Ref及最终到期检查。 |
| F05-04 | W285真实FILE原件/hold/已删draft资格及不连带删字节；W317/318恢复只绑定真实合法原件；purge保持独立授权，不把保留当擦除。 |
| F05-05 | W316实际Chrome取消零请求、失败保行、成功删除后刷新不复活；W318重新执行该旧适配Chrome，结果通过。 |

原[W304](../W304-01/DELIVERY.md)发现的三处缺口已分别补齐：[BUG756](https://github.com/GTX537/CP6/issues/756)合法data域、[W316](../W316-01/DELIVERY.md)旧入口/固定原命令/取消删除组合、[W317](../W317-01/DELIVERY.md)准确重基前恢复。[W318](../W318-01/DELIVERY.md)补历史内容当前授权映射及向前复制；它不把旧数据自动改成可信历史，也不作为旧库迁移完成。原汇总仍称W305未完属于过时摘要，W305已按原TASK迁移设计边界核销WP12-10。

## 当前源码与有效证据

W316的Legacy.cs、LegacyPersonalFormHttpV1、DraftController和FormSubmissionController与交付原件未变。W317 Recovery.cs及来源/延迟提交迁移未变；重基内核相对W293的差异已核对为W303实际隐藏处置观察/准确消费采用，相对W303交付未变。共享Drafts/Closure在W317后扩展W318新操作登记；当前读取/创建/保存/删除/恢复与SubmitForm/RebasePlan实际调用链已检查：准确原pin、本人/当前mask、源和目标CAS、同IAM门、最终会话、同事务原结果及既有Legacy映射继续适用。五expected中Form activation来自FORM当前行；绑定和Flow activation由同一真实FLOW activation_current行同时拥有并随其修订，不能误把字段别名当漏掉Flow权威；Form与Flow两条实际激活权威分别核对。

准确旧数据只读目录与新原生草稿分开；W318当前映射不证实原发布/历史实际人，不接客户端Data替换、不覆写旧行。无法准确采用的旧值/附件在边界明确拒绝并保留，不作为已迁移。未采用的普通FORM Service业务效果明确拒绝FORM_SERVICE_OWNER_REQUIRED，实际业务Owner实施仍属对应运行/Owner卡；不以无效果桩冒成功，也不把该外域能力排除包装成全OA目标完成。APP Subject始终独立。

逐来源证据/采用祖先及未变输入见 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave319-reconciliation.json`。W275/W278/W285/W287/W293/W302/303/W304/W316/317/318和BUG756各按原报告实际环境/时间/模拟范围复用，不把旧运行改称本轮新执行；W318最新真实HTTP/两条Chrome/FILE/原生恢复回归适用。相关公开报告：[W275](../W275-01/DELIVERY.md)、[W278](../W278-01/DELIVERY.md)、[W285](../W285-01/DELIVERY.md)、[W287](../W287-01/DELIVERY.md)、[W293](../W293-01/DELIVERY.md)、[W302](../W302-01/DELIVERY.md)、[W303](../W303-01/DELIVERY.md)。保留全部历史失败，未新增重复产品测试/哈希清单或逐文件回执。

## 直接前置及真实剩余

S01-02、S02-00、原定义WP12-01/02和历史职责WP12-10已DONE，准确源已在当前Main。WP02-10复用CLOSED W171可信上下文/共同最终fence；WP02-13登记已采用个人FORM原事务命令/审计的原W275来源；WP02-14直接采用W285真实FORM内容/FILE绑定下载；WP02-15采用W302处置和W303当前隐藏损失消费/留存，现草稿墓碑/显式restore政策由原个人FORM Owner负责。不删除原前置，不要求无关全域审计/所有Owner清理再次成为本调用链开工条件。已交付记录按真实远端采用核销状态，没有新增资格协议或业务效果。

原375现35DONE/2IN_PROGRESS/338PLANNED；原59局部11闭环/48剩余。125完整目标0关闭、247正式验收NOT_RUN保持。W306所需WP12-04/10整卡前置已满足，原WP12-08仍待本卡范围核对；历史批量转换、物理purge、普通FORM服务节点业务Owner及正式部署保持各自范围。

两原卡当前范围无剩余。完整OA-FORM/OA-TASK目标、全PRIV擦除/全FILE引用图、历史批量转换/部署分别记录，不能连带关闭。下一按原WP12-08范围推进W306。

一次集中原范围/源码/证据及文档审查。原FORM25条直接AC、当前调用链/精确源祖先/有效证据适用性及限定前置核对，文档diff/链接/台账和生成页通过；本窗未运行产品测试或重新生成旧运行证据。 当前累计有效55441秒，开工55149秒；24h继续，下一57600秒统一校准。无Actions运行/取消、工作流/依赖/保护更改、生产部署或既有数据库迁移。

本提交时正常远端采用、准确分支处置、工作区干净及官方finish尚待执行；最终以 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave319-final-verification.json` 的PASS核销。
