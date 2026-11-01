# 完整 Root 工单操作改期 v1

W240 承接 MES-SCHED-01 RECOVERY-R2 的 TIME_ONLY 子集。上游工程快照、Occurrence、资源 Assignment/Claim、来源窗口、执行状态和日历约束可明确模拟；实际 Root/操作版本、计划、共同事务和原结果由 Main/PostgreSQL 执行。模拟资料可用于生产业务验收；环境、部署状态另外记录，不等待企业真实数据。

## 边界与初始化

`/api/mes/scheduling` 采用当前身份、租户、环境及 ScopeAuthorizationFence；Root 必须同时在计划、工单和报工的当前可读范围内。新能力独立登记，不自动授权角色：`mes-planning-board:plan.prepare/change.apply/command.read/history.read/simulate-source`、`mes-work-order:change.schedule`。

模拟入口仅在 `SchedulingSimulation:Enabled=true`、当前 `simulate-source` 操作及全范围权限下开放。`POST simulation/roots` 接收 `dataKind=SIMULATED`、准确原生工单/全部操作 ID、Site/Zone、Snapshot、五域前驱、每操作 Occurrence/Assignment/Resource/数量单位与依据、完整观察集合。原生工单必须尚未执行；不猜测 ProcessCd/MachineCd 对应关系，不自动迁移既有在制数据。已采用的租户/工单号由 PostgreSQL 触发器关闭原生工单和操作写入，旧 reschedule/arrange 返回 `MIGRATED_SCOPE_NEW_FLOW_REQUIRED`；未采用旧计划仍为参考只读。

新采用 Root 的队列成员为空，服务端明确返回 `queueHeads:[]`。TIME_ONLY 冻结并比较该队列前驱，不产生队列效果；完整资源队列、排队和自动排列待后续任务实现。

`POST simulation/observations` 仅发布同一观察身份的下一版本或重放当前相同内容，模拟当前来源窗口、资源使用区间、Claim 有效性、用途、执行状态、技术前驱和不可用区间。旧版本不能重置当前观察。模拟输入不会被标为真实 PLM/RES/EXEC 服务完成。

## 实际业务链

1. `GET context?siteId=` 返回当前身份可见的已采用 Root；`GET roots/{id}/capture` 保存完整 Root/操作/来源观察，标识读取时刻、30 分钟到期时间和完整性。
2. `POST change-plans` 接受 capture、操作 ID、操作排程版本、WO 五域、`TIME_ONLY`、含 UTC/offset 的起止时间、可选优先级及理由。`resourceChoiceRef` 必须为空；MachineCd、租户、任意附加字段不属于输入合同。
3. 服务端保存不可变计划及整个 RootApplyGroup，包括所有未改操作。根时间只保留或向外扩展，不能缩短；原根时间为空时，所有成员时间必须已知才能计算。理由/优先级和旧新根计划一同保存。
4. `POST change-plans/{id}/apply` 只接收原计划身份和命令 ID。最终决定点核对当前权限、WO 五域、完整操作集合/排程版本、队列前驱、当前来源/Claim/执行状态、资源窗口/冲突、技术前驱。执行已开始或未知时不得产生新改期。
5. WO 参与者与 SCHED 共用一个实际 PostgreSQL 事务，原子写入根计划、操作版本、application、audit_outbox 和原命令。根 revision 一次加一；仅改期操作 scheduleRevision 加一，其余 WO 域及未改操作保持。时间改期不伪装释放或开始执行。
6. 相同命令重放原结果；同命令不同输入拒绝。相同计划的另一命令返回相同 application，不重复改期。写权限撤回后，原键可按当前 `command.read` 与对象读权限恢复。

`GET commands/{id}` 的 `NOT_OBSERVED` 明确 `canInferNoEffect=false`，不能推断未提交。`GET change-plans/{id}` 供准确重试；`GET roots/{id}/history` 返回当前可读根的原结果。所有 HTTP 响应 no-store。

版本为最多 30 位正整数文本；数量保留 13 位整数、8 位小数及 Unit/Basis Ref。实际未知数量保持 null。时间输入必须含 offset/Z，最多微秒，内部按 UTC 微秒序列化；不猜测 DST 重叠时间。SCHED-JCS-1 对键排序和文本 NFC 化，只有类型为时间的字段转换 UTC，普通理由文本不被时间解释。外部边界校验一次，内部采用已确定合同，不做序列化往返验资格。

## 页面与范围

计划看板新增完整 Root 改期面板：读取→准备→展示整组前后差异及未改成员→确认→原结果。浏览器按环境/租户/身份保存原命令身份，不持久存储理由；使用 Web Locks 协调多页面提交。未知响应、查询失败及未知后的重试拒绝均保留原命令，身份变化清除当前页面的私有资料并忽略旧响应。

本版本不宣称完成自动排列的整 Root 批次、资源变更六阶段、完整资源队列、标准 BoardCapture/筛选/KPI/分页、原生真实工程资源执行宿主衔接或整个 D3。既有 PLM W202 和资源 W203 交付继续复用；剩余输入按用户授权模拟，后续直接实现实际业务行为，不新增资格证明链。

实际验证、失败修复与环境见[W240 单份交付报告](evidence/W240-01/DELIVERY.md)。
