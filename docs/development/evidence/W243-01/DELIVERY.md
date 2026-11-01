# W243-01 资源换机与准确绑定队列迁移交付

W243资源换机：完整上下文/原计划与六阶段固定键、持久Owner请求/回执、自身五域、停止与未知恢复、WO/RES/SCHED共同绑定采用、准确assignment一次队列迁移及页面。

**生产业务验收采用模拟数据，不等待企业数据。** 实际运行环境是本地隔离 PostgreSQL18、Main Owner/API 和 Vue 组件；工单起始/工程快照/资源资格、EXEC登记及 Owner 运输明确模拟。阶段请求、执行围栏、Claim状态、绑定采用、队列迁移、并发竞争和故障恢复实际执行。本范围验收通过；部署状态：未部署生产。

## 实际交付

- 完整换机上下文及整个Root计划冻结原五域、原绑定/全部旧Claim/EXEC集合、新资源choice、预分配绑定和六阶段固定键；不能提交任意新前驱。相同计划只有一个work。
- 请求先持久登记后发送，Owner实际效果与原回执共同提交。未知阶段只查询原键；模拟Owner处理原inbox，迟到回执不创建第二个业务阶段。明确Owner拒绝保存失败结果，数据库/运输未知保留未知。
- BEGIN围栏、准确旧EXEC闭合、PREPARED_NO_EXECUTION新Claim、WO/RES/SCHED共同绑定提交、旧Claim释放、永久停止证明后的未采用Claim关闭。停止和提交竞争只有一个决定，迟到预留保留不可执行状态再关闭；已采用Claim不能被停止清理。
- 原五域仅由本工作已接受转换回执推进，无关改动暂停。根/操作版本各一次、快照/Occurrence/实际记录/未改成员保留。新资源Claim的实际使用区间进入后继观察。
- 绑定提交同时登记唯一迁移；另一个共同事务操作准确旧assignment与目标队列当前尾部，保存稳定新Item。当前无关顺序保留、两队列与迁移回执共同回滚；缺旧成员不冒充成功。普通队列CAS与TIME_ONLY规则不变。
- 页面支持选择/计划/继续/停止/原结果/队列迁移；未知继续保留原命令，身份变化不显示旧数据。成功后仍NO_NEW_EXECUTION。新租户登记work.reconcile，不自动授予角色。

[详细合同](../../scheduling-rebind-v1.md)。沿用现有内部契约，当前权限/来源/可变业务条件在负责边界核对，不新增资格包装或逐层重复门禁。

## 实际验证与审查

39 distinct relevant backend scenarios PASS:12 resource-rebind PG18/actual HTTP,9 queues,11 affected scheduling,1 arrangement original-result behavior,6 permission seeds;23 Vue scenarios PASS (8 rebind,7 time-only,8 planning);app vue-tsc PASS. Explicit simulated production business inputs and Owner transport; actual PostgreSQL transactions and API. Initial broad Rebind_ filter also ran8 unrelated existing checks, not counted.

12项换机场景覆盖完整共同提交和原结果；未知原阶段不重发及停止后迟到预留清理；无关Root变化不得替换五域；EXEC未知与实际冲突；绑定回执失败整体回滚；停止先赢/提交先赢及真实并发竞争；撤权后禁止新业务、允许原范围保全；真实HTTP输入/原命令读取；迁移保留当前无关顺序且两队列共同回滚；来源改变明确拒绝且旧成员缺失不视为成功；原EXEC结果收敛及RES区间占用冲突。权限种子验证包含新增能力不自动授予。

记录：`D:/CP6-local-test/wave243/foundation/pg-*.json`及TRX；每个场景取最后实际结果。未受后续换机修正影响的9队列、11改期和1排列结果复用同轮pg-final记录，不因提交或SHA变化重跑。页面`D:/CP6-local-test/wave243-web-contract.log`23项、`wave243-web-contract-types.json`exit0；组件响应为模拟，HTTP运行实际宿主和数据库。首次宽过滤另运行8项既有检查，未计入本范围。新增双工单资源冲突测试首次误重复初始化权限种子，唯一键拒绝；改为复用同一已登记夹具并定向复测，失败日志保留。已有不相关编译警告保留，不假称全仓零警告。

One author task SPEC/QUALITY review of full diff: original six keys and immutable payload/receipts, exact own five-domain successor, old EXEC closure, Claim reservation/adoption/non-adoption, current authority and original command namespace, stop/commit same-lock race, common Root/Claim/application/audit/migration rollback, precise current-head membership migration, tenant/object isolation, simulated-service recovery after write revocation and UI identity/pending-command handling. Found and corrected Claim use interval projection and new-tenant reconcile capability; deterministic Owner rejection is persisted after phase rollback, unknown results remain original-key pending. Final contract alignment keeps operation-only context lookup and independent work.read; focused HTTP and UI regressions verified. No extra qualification wrappers.

未执行全仓回归、浏览器布局或正式生产候选构建；没有把它们写为通过。Actions未触发，工作流及依赖不变，推送前复用同内容事件审查并核对当前引用。

## 剩余与生命周期

本轮不关闭完整D3/WP08-10/WP08-11。统一看板及原跨阶段剩余范围继续。输入和同进程EXEC/RES运输明确模拟，实际持久Claim/执行围栏/绑定共同事务/队列效果；没有外部EXEC/RES宿主部署、生产部署或既有环境迁移。

原七天目标ACTIVE，D1/D4闭环，D2/D3/D5/D6/D7部分完成；2/7不是工程完成比例，正式375任务/125目标/247验收不由此窗口关闭。下一步：继续原D3统一BoardCapture/筛选/KPI/分页；保留原D2/D5/D6/D7剩余范围。生产业务验收缺少输入直接模拟，不等待真实企业数据。

本聊天COORD单人，基线`7287e338cdbbb864e4ac1c5f23687e3e874a96e2`，复用DEV-1/DEV-2，预计实际90–160分钟。W242按最终proof归档一次，不重复。此为合并前快照，准确PR、分支删除、工作区状态、官方finish及实际用时以同轮最终proof核销；固定目录和缓存保留。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/219)已完整采用并finish；Main待最终核对。本快照距开工30.13分钟。
