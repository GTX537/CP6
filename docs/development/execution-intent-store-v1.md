# 执行意图与保护动作持久底座 v1

`ExecutionIntentStoreV1` 是 WMS-MASTER-01 v1.1.1 §12.3 / §13.3 的内部 PostgreSQL 状态存储前置。没有 HTTP/DI 注册，不授权库存、规则或主数据写入。当前业务 Owner 尚未采用；`APPLIED` 与 `SEALED_NO_EFFECT` 的覆盖仅为 `PARTICIPATING_WRITERS_ONLY`，原决策固定 `externalClosureAuthority=false`。该原件不能作为跨 Owner 解除保护的生产证明。

## 事务与不可变身份

`Open` 绑定实际 Main Npgsql 事务、当前租户、环境和 PRODUCTION/SIMULATION 空间。生产空间没有 Sandbox，模拟空间必须有非空 Sandbox。仅支持 ReadCommitted/Serializable，不支持环境事务或更换 DbContext 事务/租户。每个实际事务只能开一个此类会话；授权摘要的 PostgreSQL 事务咨询锁在首次读取前取得，持续到调用方提交/回滚。需要多意图原子操作时，须另行定义完整确定顺序，不能循环打开此组件规避限制。

`Authority(kind,id)` 在该空间唯一，类型原件须由采用者核实；`Plan` 固定意图类型、候选摘要、总范围摘要及全部必需 Owner/范围/保护种类/前驱原件。必需 Owner 集合按 Owner/范围排序；不能在重试中删减 Owner 或替换前驱。原授权同义重试返回原意图，变义冲突。命令别名在空间内唯一，不能转向其他授权；终态之后的新别名仍指向同一终态。该组件不解析其他系统的 Canonical/Adoption 别名，采用者必须先找到真实唯一授权。

`Admit` 一次写入 OPEN/Revision1/ExecutionEpoch1、所有不可变 Binding 和 PREPARE Action。每个 Binding 的 PrepareRequestKey 在此生成一次；请求原文及摘要均不可变。准备键按空间/Owner唯一；绑定按意图/Owner/范围/保护种类唯一。调用方必须先提交短事务，之后才发送远程请求；存储方法返回不代表已经提交，也不授权在锁内调用远端。

## 执行与动作的两类代次

初次 `TakeOver` 将 OPEN 转为 PREPARING；后续业务接管增加 ExecutionEpoch 并推进 Revision。`Ready` 和 `Apply` 校验当前 Revision/Epoch。所有必需 PREPARE 都须有准确绑定的 PROTECTED/COMPLETE 确认、已知零 pending，方可 READY。未知或尚有 pending 不等于零。

每个动作的 ActionKey 和载荷摘要永久固定。`ClaimAction` 仅推进其 AttemptEpoch/AttemptRevision；旧动作代次不能覆盖新代次。PREPARE 再查询会将 READY 回到 PREPARING，同时保留最后确认的保护事实。确认/解除重试不改变终态的 ExecutionEpoch/Revision。成功终态动作返回原完成状态，不新建动作。

`OwnerOutcome` 是**已经由未来采用者核实**的观察封装，必须绑定 ActionKey/完整载荷摘要、准备键/准备摘要、执行意图、Owner、范围及候选。持久化层检查这些绑定和状态转换；它不认证网络响应、签名、Owner原件或覆盖完整性。确认载荷包含原 WMS 决策，因此载荷摘要也绑定原业务结果。每代观察保存不可变原文，重复观察同义可恢复，变义拒绝。

LastConfirmed 与当前 ObservationState 分开保存。UNKNOWN/NOT_OBSERVED/CONFLICT 不清除已安装保护，不自动解除，也不产生新准备键。新确认不能替换原 Guard 标识；已确认解除后又收到迟到的 PROTECTED 是 Owner 协议冲突，不能把解除事实当成仍然可靠并默默安装新保护。原成功观察在后续查询之后回放，只读既有动作状态。

## 提交、封闭与恢复

`Apply` 在同一意图决定锁内检查 READY 和全部准备，调用本域效果回调，写原决策/APPLIED及每个 Binding 唯一 CONFIRM_COMMITTED 动作，均使用调用方实际事务。回调必须在此事务中核实时点权限、实际类型授权、当前原件/控制前驱/保护有效性，写真实业务原件、CommandResult、Audit 和 Outbox 所需内容。测试中的 SysTenant 同库写入只证明事务联动，不证明 Stock、Selector 或任何真实 Owner 已采用。

返回原 APPLIED 不再调用回调；后续只能查回/重试原确认动作。STOP_NEW_USE 只接受 CONFIRMED_STOP_CONTROL，普通主数据/规则/生命周期保护接受 CONFIRMED_TRANSITIONED，不能把停止当临时保护解除。

`Seal` 与 Apply 竞争同一决定锁。APPLIED 返回真实原决策，绝不转成无效果；未应用意图要求准确 Revision，写 SEALED_NO_EFFECT、Revision+1、Epoch+1、封闭前最后可执行 Epoch、全部未来同授权别名覆盖声明及每个 Binding 的 RELEASE_SEALED 动作。Prepare 尚未发送或回应未知也保留原请求键。SEALED 原结果恢复不重新执行。终态不可重开。

封闭当前仅覆盖此组件参与者，尚未原子拒绝真实 Command/Authority，也未证明所有原生副作用路径采用。后续接入必须把这些原件、所有最终写入口和可验证的权威封闭接口一起完成，再按新版本明确发布生产解除资格，不能删除 `externalClosureAuthority=false` 当作接入完成。

RELEASE_SEALED/CONFIRM_COMMITTED 行是持久待投递动作；本轮没有网络发送器、后台扫描调度、真实 Owner 的原准备键查询/墓碑或跨 Owner 控制 CAS。记录测试 Owner 的 CLOSED_NO_EFFECT 回应只测试本地保存，不证明真实 Owner 已阻止迟到 Prepare。RESTORE_CONTROL 不支持；原停止后的恢复必须另行采用新意图/子控制/原控制前驱 CAS，不能复用 Seal 原停止来解除。

## 数据库与故障边界

迁移 `20261030020000_ExecutionIntentV1` 新建 `cp6_execution_v1`，不修改既有 Stock/Selector。原准备、别名、动作载荷、观察记录不可变。数据库禁止终态重开；延期约束确保绑定/PREPARE集合完整，APPLIED 必有全部确认动作且无解除动作，SEALED 必有全部解除动作且无确认动作。PUBLIC 权限撤销；只支持前向恢复。

每次存储操作使用保存点，后续动作写失败会撤销此前业务回调、终态和已写动作；外层回滚同时撤销整个效果。保存点不重置 EF ChangeTracker 内存状态，调用方失败后须丢弃或正确重载上下文，不能凭已接受的内存实体继续写入。回调不得提交/切换事务或执行事务外副作用。采用者须在其完整锁顺序中集成意图锁和真实业务锁；本轮没有证明跨组件统一锁顺序、全局 Canonical 身份或大规模调度性能。

相关证据见 [W214 交付](evidence/W214-01/DELIVERY.md)。仅隔离本地 PostgreSQL 验证，无既有环境迁移、Actions 或生产部署；真实源覆盖/独占委托、Owner保护采用、08E、Transfer/Stock和原七天剩余出口保留。
