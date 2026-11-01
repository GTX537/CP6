# 自动排列及完整 Root 批次 v1

W241 在[完整 Root 改期](scheduling-time-v1.md)实际 WO/SCHED 共同事务上实现 MES-SCHED-01 RECOVERY-R2 §6 的稳定启发式预览及逐 Root 应用。上游时长、交期、日历、维护和提交依赖可明确模拟，实际排程建议、原子提交、回滚和原结果由 Main/PostgreSQL 执行，可用于用户批准的生产业务验收。部署状态单列。

## 输入和完整捕获

`POST /api/mes/scheduling/simulation/planning-inputs` 仍使用现有模拟配置、当前 `simulate-source` 权限及全范围作用域。提供准确 Root、递增来源 Ref、可空的带 offset 交期、完整操作集合、可选技术时长（正整数微秒及 Duration Ref）、明确的日历/维护/技术前驱已知状态及跨 Root 提交前驱。旧原生 LeadTime 只作为明确标记的兼容估计，已经采用的原生行有单写围栏。

这是一份业务输入，不是新资格或证明层。输入改变后当前 Ref 前进，原捕获仍保留当时版本；同 Ref 异内容拒绝。缺输入或必需约束未知时，预览明确 `UNKNOWN_CONSTRAINT`，不把空集合当作无约束。

`GET /api/mes/scheduling/arrangement-captures?siteId=` 必须显式选择 Site，在同一实际事务中冻结当前有权完整 Root、五域、所有操作、当前观察、规划输入和原生 LeadTime，最长30分钟。每次预览所选候选最多500项，整个Site可以超过500项；未选Root仍参与占用检查，不混进所选Root的候选manifest。该读取不会创建生产事实。有权 Root 不因无关隐藏工单而整场拒绝；隐藏工单只对当前可见资源产生不带工单/操作身份的占用区间，私有资源引用不返回，整体 coverage 标 PARTIAL。每个可申请写入的 Root 仍必须完整可读。

操作可明确无 Assignment/Resource，显示 UNASSIGNED 并排除，不拼造 unspecified 机器。当前 Root 集合/观察/规划输入改变，旧 capture 不被静默更新。

## 可解释预览

`POST /arrangement-previews` 接收 `{siteId,captureId,operationIds,baseTime,defaultDurationMinutes,algorithm,resourceScopeRefs,keepLocked:true,reason}`。起点必须含 offset/Z，没有隐含8点；默认时长1..10080分钟。候选 ID 与资源 Ref 必须来自原 capture，不允许任意 MachineCd 或换资源。

- 固定算法 `LEGACY_PRIORITY_DUE_CONTIGUOUS_V1`：Priority降、DeliveryDate升且null最后、RouteSortOrder升、RootId升、operationId升。
- 时长依次取技术微秒、正原生LeadTime的 `truncate(max(1,days*8))` 小时、显式默认值。1.3天是10小时，绝不按数量乘工时或改成10.4小时。
- 保留已开始/完成及未选操作；在当前资源沿基准游标避开准确占用和不可用区间，不移动锁定项。未知占用、日历、维护、执行或技术前驱不能宣称已满足。
- 所有候选与未选/排除/阻断原因保存在完整 manifest；未改变时间的操作标 `UNCHANGED_INTERVAL`，保留其排程版本。每个有实际改期的 Root 只有一个组，组内任一必要候选失败则整组不可申请。
- 使用 W240 保留/扩展根包络和完整未改成员。根包络无法解释时明确 BLOCKED，不造默认成功组。
- 旧占用需要另一个原 Root 组先搬离时，冻结该组为提交前驱；显式 Root 依赖同样进入 DAG。循环或无法腾让的占用阻断，不能靠覆盖先写的计划解决。

`GET /arrangement-previews/{id}?cursor=&pageSize=20|50|100` 始终分页同一不可变 preview，并返回完整 manifest 数量/摘要及所有原子组。预览是启发式建议，不宣称最优APS、有限产能或物料齐套，不建立或释放 Claim。

## 持久批次与原结果

`POST /arrangement-previews/{id}/apply` 只接原 preview身份、原 manifest digest、明确选择的完整 groupId 集合、原理由和 commandId，不接受候选行。需要当前 `arrange.apply`；每个实际 Root 提交还需当前 WO `change.schedule` 和对象权限。`arrange.preview`、`preview.read`、`arrange.apply`、`work.read` 单独登记，不自动授予角色。

批次先持久保存原输入与每组稳定 key，然后逐组执行：

|组状态|含义及允许动作|
|---|---|
|WAITING|尚未发送，前驱可能未成；允许停止|
|SEND_INTENT / 对外UNKNOWN|原发送意图已持久化；查询或继续原组，不能推断无效果或另造key|
|COMMITTED|实际 WO/SCHED/application/audit 与组结果同一事务提交；保存不可变原结果|
|REJECTED|确定拒绝且该尝试无新效果；原组终态，不刷新前驱再提交|
|CANCELLED|仅尚未发送组已停止；同preview/Root不能经另一command重新执行|

每 Root 使用原五域、完整操作和队列前驱、原观察/规划输入一次应用，实际改变操作各加一次版本，根只加一次，其余域和未改成员不变。跨 Root 不宣称全局原子：失败/未知前驱的后继等待，独立组可继续。最终还在共享数据库围栏内重读其他 Root 的当前计划/执行占用；资源 Ref 版本不同不会把同一物理资源当作两台机器。

同 preview/Root 被不同命令或并发批次选择时共用唯一组 work/application。命令 ID 在原单次改期与批次之间也不能复用为异意图。同键异正文409；同键回读不会悄悄继续新业务。`GET /commands/{id}` 返回原批次当前逐组知识；NOT_OBSERVED 明确不能推断无效果。

`GET /arrangement-batches/{id}` 查看；`POST /arrangement-batches/{id}/continue` 在当前权限下继续原意图和原组 key；`POST /arrangement-batches/{id}/stop` 仅将 WAITING 转 CANCELLED，已发送未知/已成均保留。当前撤权不删除原结果；原命令按当前 `command.read` 和对象读权限恢复。五类结果计数（已提交/拒绝/未知/等待/取消）之和等于 submittedCount；只有全部实际提交才显示全部成功。

## 页面和范围

计划看板自动排列面板要求显式 Site、起点、默认时长、理由；展示全部清单、排除理由、逐 Root 前后根时间和依赖。分页20项不缩小提交范围，确认框明确包含当前页以外的完整成员。浏览器仅保存原命令身份与所选组，不保存理由；重试从不可变预览读取原理由。网络失败或批次执行后读权限失败仍保留原命令，身份切换清理私有页面，未知不显示失败无效果。

本轮没有实现资源更换六阶段、资源队列维护/EXEC事件、统一 BoardCapture 的筛选/KPI和完整详情；已有 W202/W203 产品成果继续复用，后续直接实现这些业务，缺输入仍模拟。尚未在既有环境执行迁移或生产部署。实际范围、验证来源及失败记录见[W241单份交付报告](evidence/W241-01/DELIVERY.md)。
