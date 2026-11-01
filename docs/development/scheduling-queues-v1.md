# 资源执行队列 v1

W242 实现 MES-SCHED-01 RECOVERY-R2 §7 的同资源执行建议顺序、完整捕获/CAS及当前 EXEC 投影，直接复用 W240/W241 的准确工单/操作/绑定、作用域和共同事务。上游绑定、开工及完成输入可按用户决定明确模拟，用于生产业务验收；队列、回滚、原结果及历史实际运行 PostgreSQL。业务验收和部署分别记录。

## 当前队列与完整捕获

`POST /api/mes/scheduling/simulation/queues/{resource}?siteId=` 在既有模拟开关、独立 simulate-source 权限及全范围作用域下，从该 Site 已采用的准确资源绑定建立队列。客户端不能提交成员或顺序。ItemId 使用不变操作 ID，Occurrence/Assignment和只读 RouteSortOrder 随成员保存。重复初始化不增版本，新采用成员可追加；已有成员资源/绑定改变要求准确迁移，不能借初始化删除旧成员或跨机搬移。

`GET /queue-context` 返回当前可见的 Site/资源选择。`GET /resources/{id}/queue?siteId=&captureId=&cursor=&pageSize=20|50|100&windowFrom=&windowTo=` 捕获完整队列和当前执行观察；首次读取保存不可变 capture，30分钟内分页使用同一 capture。日期窗口两端必须同时存在且明确 offset/Z。过期返回409，页面保留旧页并标 STALE，不拼接新旧页。

有隐藏成员或日期窗口不完整时，`completeForWrite=false`、总数null、仅返回可见成员并重新连续编号，不泄漏隐藏项位置。服务仍保留完整内部清单，禁止部分读取覆盖整队列。页面只在完整当前范围中准备重排。后续翻页、准备及原结果读取重新核对当前对象权限。新绑定成员缺失时明确 QUEUE_MEMBERSHIP_CHANGED，不能把旧队列声称完整。

全部完成后的空队列仍在 capture 中保留其原对象范围，后续分页重新核权，不能用零成员绕过撤权。

## 服务端重排与原结果

`POST /resources/{id}/queue-plans` 接受原 resourceRef、队列版本、capture、移动 itemId、明确 BEFORE/AFTER/END、对应唯一相邻项、manifest摘要及理由。BEFORE必须只有beforeItemId；AFTER只有afterItemId；END两者null。客户端不提交整队列。服务重建完整顺序，保持稳定身份，并展示全部前后顺序和受影响成员。

- 移动项必须 WAITING；实际执行中、完成或未知状态不能移动。
- 锁定项保持原位置，不能借移动其他项越过锁定锚点。
- 同队列技术前序必须仍排在后序之前；跨资源前序保留引用。队列顺序不构成执行许可，也不代替实际执行门。
- 资源归属、绑定、来源排程和观察版本在最终决定点核对；不刷新旧计划的前驱来拼成功。

`POST /queue-plans/{id}/apply` 使用既有 SchedApply 原键形状。当前 queue.apply 和对象范围通过后，在既有 Main 权限/业务围栏及 Scheduling 围栏持有期间检查队列 CAS、当前 EXEC 锁和依赖。队列新版本、不可变前后历史/审计、关联 Root 的 QueueHead 引用、唯一计划 application、原命令结果同一 PostgreSQL 事务提交。Root 五域、操作排程版本、RouteSortOrder、WO.Priority、机器状态和 EXEC Token 不变。

同计划不同命令共用唯一原 application，同命令异内容409；队列、改期、排列共享命令冲突域。`GET /commands/{id}` 返回 queueReply；撤销写权后可按当前 command.read 和对象读范围恢复。NOT_OBSERVED不能证明无效果，网络未知保留原键。`GET /queue-plans/{id}` 读取原计划用于原键重试；`GET /resources/{id}/queue-history?siteId=` 保留历史版本，需要历史完整对象读权。独立 queue.read/prepare/apply 只登记，不自动授予角色。

## 执行事实与迟到处理

`POST /simulation/queue-execution-events` 在现有模拟边界接受明确 SIMULATED 的准确 Root/Operation、Owner/FactId/Version/Digest、STARTED或COMPLETED、可空实际区间和带时区发生时间。它是模拟外部 EXEC 输入，实际事实登记、当前观察投影和队列效果运行真实数据库；不调用原生开工、不制造执行许可。

Owner+FactId绑定原操作，不可换绑；Owner/FactId/Version同正文重复无新效果，同身份异正文409。迟到旧版保存但不回退当前版，完成状态不被新开始反转。开始事实无论排在队列何处均接收；当前投影转 LOCKED_IN_EXECUTION，前面仍有等待项时显示 QUEUE_REASSESS_REQUIRED。原历史不改写。完成事实从当前待用队列移出一次并产生新队列版本；重复/旧事实不重复移除。其他模拟观察不能覆盖已经登记的 EXEC 当前事实。

队列成员尚未同步时，已授权的准确 EXEC 事实仍先登记并更新当前执行观察，同时返回重新评估提醒。队列读取会明确报告成员需同步；这种显示状态不是拒收执行事实的条件。

## 页面、范围与后续

页面显式选择 Site/资源，显示执行锁、迟到重新评估和只读路线序号，确认完整前后顺序后才提交。分页不会把提交范围缩为当前页。原命令保存在当前租户/环境/身份下，不保存理由；原结果查询/重试保留原身份和计划，身份改变清除旧私有页面。确定首次业务拒绝要求重新捕获，未知或读权失败保留原命令。

本轮完成同资源队列重排及上述 EXEC 投影。跨资源唯一 QueueMigration 必须消费准确已提交 assignmentReceipt，随原换机六阶段继续实现，不以普通重排或模拟任意资源修改替代。统一 BoardCapture/筛选/KPI及其他原D3范围继续；不把队列首位视为自动Start。现有新Scope旧写围栏沿用W240。实际验证和剩余范围见[本轮报告](evidence/W242-01/DELIVERY.md)。
