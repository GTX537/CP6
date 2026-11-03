# W289-01 SFS待办编辑与三版本原子办理

W289：SFS待办已采用字段补丁与任务/实例/表单数据三版本，固定节点与当前IAM/委派权限相交，表单/运行变量/决定/历史/文件绑定/原结果同事务；真实编辑页面、选项与附件及未知结果恢复闭环。

Main基线`72a459e9718ce523276a2589db0a91415d213cd8`，COORD单人复用DEV1/Main、DEV2/Handoff；[24小时持续计划](../../planning/20261102-codex-24h-oa.md)继续。原OA-TASK R2 §3、S3 Common §4/5.1和WP12-08卡分别核对；APP五字段决定协议不变。

## 实际采用

新`POST /api/oa/v2/personal-forms/tasks/{task}/decisions`只接收封闭SFS WFC输入：服务器准确原生任务来源、同一taskId、APPROVE/REJECT、可选1000字符意见、dataPatch、真实task/instance/formData三版本。envelope expectedVersion与body任务版本必须逐字相同，异值400 PRECONDITION_MISMATCH；三版本任一变旧409，不取服务器新值替换客户端期望。APP kind、辅助Actor、未知字段和错误来源不能进入此写链。旧SFS双版本入口拒新写410，仅保留准确旧原结果重放/查询/关闭；旧浏览器恢复标识继续查询旧槽，不能作为新SFS意图重发。

当前任务context补齐SFS类型、准确表单数据版本与流程/表单固定引用、node/token/stage/round。字段权限取当前实际人、委派主体与固定任务节点三者交集；readonly/hidden/未知patch拒绝。patch省略保留、null明确清空、数组整值替换；使用原数字词元，服务端重算及最终必填/引用/字典/文件复核。主记录、所有活跃共享表单实例Vars的真实字段变化、任务票决、不可变前后快照、文件绑定、通知与原命令同事务。子流程未变化的映射变量保留，集合子实例item/itemIndex维持冻结行坐标；合法同名根表单字段仍可编辑，不覆盖子实例坐标。

实际TASK选项查询与文件上传/读取/下载/状态操作采用当前准确任务、固定表单版本与字段权限。只读委派不能上传或查询编辑候选；文件扫描在事务外，落库前重新取得当前任务版本、表单版本和所用写授权，撤权即拒。TASK附件只可绑定本准确SUBMISSION表单数据，不能跨对象挪用；最终决定检查并绑定准确新表单版本。文件原操作按当前实际操作者和读取权恢复，旧委派失效不抹掉其已完成事实。

新页面只发送改变的可编辑字段及本次GET的三版本，精确金额/明细不经过浮点转换。409保留输入且停止再次提交，明确重读后才形成新意图；未知提交保原命令，不把NOT_OBSERVED当失败。切换身份/代理人立即清私有字段并丢弃迟到响应；sessionStorage只留恢复身份，不存表单内容。真实文件未知结果阻止决定；清空数字在补丁模式发送null，不误成省略保留。

## 验证与审查

实际隔离PostgreSQL18/HTTP验证SFS三版本、精确补丁/重算、节点/IAM/委派权限、当前字典、文件绑定、两连接竞争及故障原子回滚；15项既有任务动作回归通过。3条实际Chrome链通过（新编辑/代理办理/退回重送），文件链使用真实Defender。46项相关Vue行为通过，新增清空补丁行为通过，显式应用vue-tsc通过。各次执行及复用范围见final-validation.json，未跑全仓。

实际行为来源索引`D:/CP6-local-test/wave289/foundation/final-validation.json`保留全部原日志、TRX及失败结果；按方法索引不冒充理论参数用例总数。`adopted-core`中已成功24项直接复用，失败夹具修复后定向通过；新边界覆盖三种独立旧版本、并行表单变化、精确大数、只读/隐藏字段、字典停用、委派撤权/过期、两连接竞争、文件绑定缺失、投票前失败和commit响应未知。新浏览器两标签页真实竞争，409保留输入，选择字符串1.0、真实Defender扫描/下载附件，最后实际commit响应被丢弃后重载查询原件；数据库确认只有正确票与绑定。另两条原页面链确认代理和退回重送通过新入口，APP既有HTTP五字段/CSRF/双版本/恢复继续通过。

一次集中SPEC/QUALITY审查覆盖整个生产/迁移/UI/测试diff。相关修正包括expectedVersion异值的准确错误、子实例运行坐标保护、显式清空补丁和浏览器成功后返回列表的实际断言。初始红灯、夹具Owner遗漏、浏览器context/完成导航断言及类型/编译失败均保留；只修相应原因，未将失败运行整体称通过。迁移仅在隔离测试库实际执行，PostgreSQL deferred约束检查决定/表单历史/文件绑定完整性；现有环境未迁移。

模拟业务数据、用户、政策、字典与流程；内部Owner、PG18、登录HTTP、Chrome真实运行。普通Owner级文件夹具显式模拟检查器，新编辑浏览器使用实际Microsoft Defender。没有真实企业数据前置、Actions、生产部署或额外席位。旧成功输入未变则复用，不按提交SHA重跑。

## 剩余及生命周期

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-08仍需原卡逐项核证及统一action-context/APP WFC适配等真实余项，不能由本项自动关整卡。

APP当前原生决定已有同事务Recorder/Outbox，仍沿accepted入口。通用WFC commandId允许任意1..100字母数字-_.，现APP accepted端口为Guid；下一适配必须先核原accepted身份合同，不能隐式缩小合法输入、派生另一个命令身份或建第二个独立成功槽。统一action-context尚未宣称本窗提供；当前新增context字段仅对实际SFS消费闭环负责。

远端Main/Handoff完整树采用、准确任务分支删除、工作区干净及官方finish/席位释放最后以`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave289-final-verification.json`核销。固定缓存与用户资料保留。本窗完成不等于24小时总目标完成，下一22小时校准继续按实际吞吐修正。
