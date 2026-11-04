# W299-01 — 版模处置审批撤回与原结果恢复

W299：版模处置实际Owner审批撤回、独立业务委派/BLOCKED显式接续、共同控制版本、原操作与运输恢复及真实页面完成本地验收；保原处置请求、物理停用和全部未清义务。

Main基线`1a4e6edac635000951eb7b4ec5d12edecd850691`，`codex/w299-01`、DEV1，COORD单人。继续`CP6-24H-20261104`有效24小时目标。

## 实际行为

- 实际`Main.Wms.ToolDisposition.v1`拥有准确请求/原送审控制头；独立dispose-withdraw与dispose-delegate按认证实际人、当前原对象/Remarks可读及准确grant版本授权。TASK委派不代用，正文不接受伪Actor；撤回无需实物资料可写。
- 业务撤回追加不可变事实、Outbox和原命令，同PG事务改变控制版本。审批应用读取相同控制并覆盖ETag；撤回后迟到批准只记STALE，原终态回执保留。当前实际应用不能完成报废，不制造APPLIED竞争赢家；两连接验证合法BLOCKED与撤回顺序，物理停用、原请求和未清义务完整保留。
- 支持原送审受理前撤回。Workflow未启动时明确BLOCKED；新明确授权追加连续事实/命令，原BLOCKED保留，已被接续的旧事实被当前控制拒绝。工具现合同只有单一原提交，没有伪造替代轮次。
- 可信注册/服务主体/真实已接受EvidenceMode控制运输；网络在事务外，先查原键，412只换Workflow上下文，body/实际Actor保持。丢答恢复查询原结果；合法RETURNED输入可采用，未知状态拒绝。没有为工具虚构需求集EvidenceMode配置。
- 现“实物版模→查看资料→读取审批条件”显示准确原提交的撤回/委派/再授权与原结果恢复。缓存只存环境、租户、实际人分区的操作标识，不保存原因明文；存储失败不发送，身份改变清理私有数据。审批撤回与物理停用、Workflow运输分别显示。
- 三项前向PG迁移只在隔离PG18执行；真实旧提交显式采用EXISTING_SUBMISSION_ADOPTION，原BLOCKED回执与待受理提交保留。GET不造历史，缺控制头明确失败。

## 实际验证与集中审查

11项独立实际PostgreSQL18场景通过（含Main认证HTTP、Chrome与三项原版模应用回归）；11项Vue行为单测、vue-tsc通过。相同未变输入复用已通过结果；审查修改的当前读取上下文复用与合法RETURNED输入追加3项相关回归通过。

单一证据索引`D:/CP6-local-test/wave299/foundation/final-validation.json`保存每项实际运行JSON/TRX及失败记录来源。覆盖独立动作拒绝、隐藏Remarks拒绝/只读允许、跨租户/错提交、伪Actor/CSRF、当前grant撤销与新明确接续、两连接竞争、缺Outbox回滚、提交丢答原键恢复、受理前撤回与前向采用。首轮404失败、测试夹具编译/权限时点/Owner命名及UI联合类型失败均保留，修复后实际复测。

浏览器证据`D:/CP6-local-test/wave299/permission-consumption-tool-withdraw-browser-20261104T021938Z/tool-withdrawal-browser/result.json`：真实Chrome、Vue、Main Program登录HTTP和PG18，页面新建授权、受托再授权、丢弃真实提交成功响应、刷新原结果查询；数据库核对两条连续事实与原BLOCKED字节。已查看恢复截图。内部Owner未模拟；模拟输入为用户、版模、站点、政策、授权、Workflow及服务注册。业务验收不等于生产部署。

COORD单人一次任务级集中SPEC/QUALITY审查：准确Scope/工具/原申请/原提交、独立权限与当前grant、共同control/最终fence、原事实/Outbox/原键原子性、单一原送审、PG前向采用、页面恢复及真实库存/停用边界。复用已授权当前调用方上下文，保留受托主体与最终观察的必要当前读取。补齐新版模运输对合法RETURNED的处理，未知状态仍拒绝；旧三个消费者的同类问题已登记BUG747，独立分支处理。无未处置的本窗阻断项。

## 剩余范围、估时与交付

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收不变。WP12-19仍待库位规则等实际采用与整卡核销。版模现应用仅BLOCKED/STALE/CONFLICT，缺少真实终结参与方；本窗没有APPLIED或实际报废/库存/财务效果。

累计7213秒完成2小时校准：W2973412秒、W2981377秒、BUG744473秒；同类采用估计下调，主队列剩余约13.15–27.2h，原WP02-15导出/擦除/UNKNOWN/墓碑恢复余项补为9–18h滚动储备。按有效0–8/8–16/16–24h推进，下一次14400秒校准；不等待凑时、不重复已完成范围。

本次交付采样累计7468秒，W299起始5776秒；最终实际耗时以finish后的`wave299-final-verification.json`为准。本报告提交时Main/Handoff远端采用与准确分支删除、官方finish仍待完成，不能据本报告提前声明席位释放。

下一项独立修复[BUG747](https://github.com/GTX537/CP6/issues/747)，再W300库位规则；不是三天目标已完成。无Actions触发/取消、workflow或依赖配置改动，无部署、既有数据库迁移或实物终结。分支/工作区/席位最终证明：`C:/Users/tt/CP6-planning-reports/20261017-foundation/wave299-final-verification.json`。
