# 资源换机与准确绑定队列迁移 v1

W243 实现 MES-SCHED-01 RECOVERY-R2 的 RESOURCE_CHANGE。用户允许模拟数据用于生产业务验收：本版本显式构造 WO 初始资料、EXEC 工作登记、资源选择/资格和 Owner 运输；实际阶段日志、执行围栏、Claim 状态、绑定共同事务、队列迁移和恢复使用 Main/PostgreSQL。环境与部署单独记录，不等待企业数据。

## 外部输入与内部合同

沿用 W240 准确原生未执行工单/全部操作的采用、旧写围栏、租户/环境/对象权限，以及 W242 当前绑定队列。已有资源目录和工程基线仍是其原有能力；普通资源声明不冒充 Claim 或资格回执。

`SchedulingSimulation:Enabled=true` 下，具有 `simulate-source` 和完整 Scope 5 的当前主体通过 `POST /api/mes/scheduling/simulation/rebind-sources` 登记明确 `dataKind=SIMULATED` 的准确 Root/Operation、全部旧 Claim 和 EXEC 工作集合、资源 choice/requirements/technical/useInterval/资格/到期/协议及可选延迟阶段。登记关联当前绑定，身份不复用到另一绑定；旧 Claim 必须含当前观察采用的 Claim。EXEC 状态可模拟 REGISTERED、CLOSED_NO_EXECUTION、UNKNOWN、MAY_EXECUTE、ACTUAL_CONFLICT。新 Claim 和后续状态由实际 Owner 参与者执行，不接收客户端“已采用”标记。

这些是明确模拟的业务输入和同进程 Owner 运输，不声称存在外部 EXEC/RES 部署。关闭模拟运输选项后，新的资源换机申请/新阶段失败于 `REBIND_OWNER_TRANSPORT_UNAVAILABLE`；现有原键读取仍可用。

`GET operations/{id}/rebind-context` 由准确操作定位可见工单（可选 rootId 查询不改变范围），保存完整 Root 捕获、原五域、准确操作/原绑定/完整旧 Claim/EXEC 集合、资源 choices、原队列成员及读取切面。`POST change-plans` 的 RESOURCE_CHANGE 使用这个 contextId，只能选择其中的 choice。服务端冻结完整 RootApplyGroup、原五域、预分配新 binding/claim/item ID、REMOVE_OLD_APPEND_NEW_WAITING 以及全部六阶段原键。TIME_ONLY 原合同不变。

## 工作与六阶段

`POST change-plans/{id}/apply` 接受既有计划身份/摘要/原命令，返回 rebind work。同一计划只有一个 work；不同命令不能产生第二个。改期、排列、队列和换机共用命令冲突域。

`POST rebind-works/{id}/continue` 和 `/stop` 只接受 commandId、expectedWorkRevision、reason，不能替换五域、资源或 Claim。继续先在一个真实事务保存完整阶段请求、原键、摘要、原计划、原五域/准确自身后继/前驱回执/自身转换引用，再调用模拟 Owner。Owner 效果及阶段回执在另一个共同事务提交，SCHED 从原回执推进 work。命令回执不可变，`GET rebind-works/{id}` 是当前工作进度。

| 阶段 | 实际效果与决定条件 |
|---|---|
| BEGIN | 原绑定、完整 Root/旧 Claim/队列前驱匹配后，登记 NO_NEW_EXECUTION 围栏；ControlEpoch 加一并产生自身转换回执。 |
| CLOSE_OLD | 关闭全部准确登记的旧 EXEC 工作。UNKNOWN/MAY_EXECUTE/实际冲突没有闭合回执，不能预留新资源；实际时间不被清除。 |
| RESERVE_NEW | 采用闭合回执及仍适用的选择/资格/区间，在 RES Owner 登记 PREPARED_NO_EXECUTION Claim；检查当前其他有效 Claim 的物理资源和区间占用。 |
| COMMIT_BINDING | WO/RES/SCHED 在同一个 Main PostgreSQL 事务内采用 Claim、更新绑定和完整 Root 计划、登记 application/audit/自身回执及唯一队列迁移工作。根 revision 一次加一、改动操作 scheduleRevision 一次加一、BindingRegistryRevision 加一；Snapshot/Occurrence/证据域/未改操作/实际记录保留。 |
| RELEASE_OLD | 只有准确绑定提交回执后才释放全部旧 Claim，保留 RES Owner 责任记录；不关闭已采用的新 Claim。 |
| CLOSE_UNUSED | 必须有永久 WO Stop 围栏、无绑定采用且原 RESERVE 无未知结果，才关闭未采用新 Claim。未发送预留与已登记但未知的预留分别判断。 |

每个阶段只由原请求决定。同工作已有转换回执确定有效五域，不用 GET 最新 Root 覆盖原计划。无关 Root 变化进入 PAUSED_STALE；可确定的 Owner 业务拒绝通过 savepoint 回滚该阶段效果后保存原键失败回执，进入 PAUSED_REJECTED。数据库/运输未知和未闭合 EXEC 不伪装成无效果。

Q18 在已发送阶段只查询原回执，不重发新业务。正常模拟运输自动处理首次登记的阶段；`deferredPhases` 可用于模拟迟到/失联。`POST simulation/rebind-works/{id}/deliver` 是模拟 Owner 消费原持久 inbox，绝不产生新阶段或新键；重复原回执直接返回。`/execution-resolution` 可提供原 EXEC 的明确模拟结果；不能换工作、不能撤销已闭合结果，也不能以“未执行”覆盖实际记录。

Stop 与 COMMIT_BINDING 使用同一决定围栏。提交先赢时返回实际绑定结果，只继续旧 Claim 清理；停止先赢时永久禁止该 work 的绑定提交。已登记 RESERVE 迟到后只产生不可执行 Claim，然后按原键清理。成功、停止和清理都不解除 NO_NEW_EXECUTION，不授予 Start。

## 回查、撤权与队列

`GET commands/{id}` 返回原 rebindReply，NOT_OBSERVED 明确不能推断无效果。`POST rebind-works/{id}/reconcile` 只收敛已登记原阶段回执；新的独立 `work.reconcile` 能力由迁移及新租户种子登记，不自动授予角色。原键回查需要当前 command.read，当前工作读取使用既有 work.read，业务继续/停止需要 change.apply 与 WO change.schedule。

具有原模拟主体、完整对象范围和 simulate-source 的明确模拟保全服务可调用 `/simulation/rebind-works/{id}/conserve`：停止未提交工作，只登记 CLOSE_UNUSED 或已提交后的 RELEASE_OLD。它允许在业务写权撤回后保全原已登记范围；不得新建 BEGIN/RESERVE/COMMIT。随后 deliver 处理原清理 inbox，reconcile 收敛。此接口不是任意主体越权查询或业务写入口。

绑定提交同事务登记唯一 `(Tenant,rebindKey,newAssignment)` 迁移。`POST rebind-works/{id}/migrate-queue` 在已登记范围内、另一个共同事务读当前两队列，只删除准确旧 assignment 成员，追加稳定新 Item 到当前目标尾部，并保存原迁移回执。现行实现沿用共享 Scheduling 数据库围栏，并以稳定 QueueId 顺序写两队列。无关当前顺序保留，两个队列/历史/Root 引用/迁移回执一起提交或回滚；旧成员缺失不是成功证明。

迁移前旧成员显示 ASSIGNMENT_SUPERSEDED，已有目标队列显示 PENDING_QUEUE_MIGRATION 且该捕获不可重排；没有目标队列时，工作状态明确显示 PENDING。迁移成功不重复提交绑定。普通队列 CAS、TIME_ONLY 冻结的队列前驱仍保持。

## 页面及实际范围

计划入口新增资源换机面板：选择工单/工序、读取资源 choices、准备完整计划、申请、继续/停止、原键回查、原绑定队列迁移。浏览器按环境/租户/主体保存 work 和原命令身份，不保存理由；身份变化丢弃旧响应。继续请求可能已保存 inbox 后才遇到运输错误，页面保持原命令；明确失败的首次原子申请才允许重新准备。

本次不完成统一 BoardCapture/筛选/KPI或完整 D3，也不执行生产部署或既有环境迁移。当前历史事实接收继续沿用 W242；执行围栏限制新增执行许可，不能拒收历史实际事实。实际验收范围、结果及生命周期见[W243 单份报告](evidence/W243-01/DELIVERY.md)。
