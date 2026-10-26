# Main Transfer 本地需求预算持久组件 v1

W142，2026-10-26。依据 [WMS-TRANSFER-01](../modules/WMS-TRANSFER-01.md) 已接受 R1 的 SourceDemand / DemandSegment、T04、§8 与 §11。本增量是正式建单的内部存储前置；没有注册 T01–T04 HTTP、运行时 Owner、权限目录或生产主数据适配。不能把它当作 REP 派发、Transfer 库存执行或完整模块验收。

## 调用与信任边界

`TransferDemandBudgetV1` 为 Core 内部组件。调用方先开启 Main 的实际 PostgreSQL READ COMMITTED 或 SERIALIZABLE 事务，以 `Open` 一次声明完整预算键集合，之后所有方法使用同一连接、事务和固定 TenantId。组件不提交、不执行外部调用、不写 Stock、不建立另一份实物账。事务、租户改变或同事务重新扩展锁计划会被拒绝。事务失败由调用方整体回滚；不在未知提交后生成新绑定重试。

环境参数必须由未来上层部署注册、认证通道及 Tenant 授权解析。这里仅校验环境/沙箱形状和持久隔离，不提供该可信解析器。Product、UOM、Purpose、地点和 AuthorityRef 仅做结构与不可变身份检查；实际当前原文、适用性、权限和撤权检查仍由后续 Owner 完成。测试使用明确构造的 Owner 元数据，不能证明生产登记或主数据采用。

持久空间首键为 EnvironmentId / SandboxId / TenantId，生产使用内部零 UUID 沙箱哨兵，wire 仍是 null；模拟必须有非零沙箱。schema `cp6_transfer_local_v1` 是共享技术表，保留 TenantId 全部约束，不是已采用一租户一 schema，也不以 schema 代替权限。PUBLIC 权限撤销不等于运行角色已经获得隔离验收。未改变既有环境或数据库。

## 行为

- `RegisterLocal` 只建立 WMS_TRANSFER / LOCAL_TRANSACTION 原文。初始 OPEN、版本1；来源原文逐版本不可变，相同版本同文返回原结果，异文冲突。后继必须比较当前版本并连续增加；相同 BudgetKey 的身份不变，已占需求坐标必须仍被覆盖，CLOSED 不能复活。旧版本重试不会回退当前头。Planning 委托和 MAT 引用不在此组件范围。
- `Bind` 校验每个显式 RequestRange ↔ DemandRange 等长，完整覆盖请求 `[0,boundQuantity)`；两个轴各自无重叠，需求坐标可从1000等非零位置开始。来源授权区间可以分段，连续相邻区间的并集可以覆盖同一绑定。来源原 Ref 必须精确匹配当前原文、版本和 OPEN 状态。
- 同空间、同预算的来源版本、不同 Request/Line/Binding 共用同一事务锁和持久排他判定。相同绑定 ID 同文返回不可变原结果，异文冲突。每个 Request/Line 最多一个 ACTIVE 绑定。SourceDemand 和 Binding 共用 Owner evidence ID 唯一域，不能发出同 Owner/id/version 的不同原件。
- `Release` 必须匹配原 RequestId / LineId，增加预算修订并关闭准确绑定；重复释放不再写。旧 Bind 重试返回原 ACTIVE 回执，但当前状态仍是 CLOSED，绝不重新占量。原结果和当前状态必须分别使用；它不是 T36 撤回权限或请求状态检查的替代品。
- 原文和绑定结果使用固定 camelCase 字段及属性序排序的 UTF-8 JSON，保存完整字节和 digest；输入记录先冻结再等待。此内部序列化尚未通过正式注册的 TF 跨 Owner canonical 互认，后续公开适配必须完成该验证，不能仅凭 hash 格式宣称源证明有效。

数量使用严格非负固定8位十进制字符串，命令量为 numeric(21,8)，正数量必须大于零。拒绝科学计数、超精度、负零、前导零与隐式舍入。区间参数明确绑定为 PostgreSQL `numrange[]`，不是驱动默认推断的 `nummultirange`。数据库检查 `[from,to)`、有限边界、精度、完整双轴映射及实际重叠；源码没有把 TotalDecimal(38,8) 聚合缩成 decimal，本组件未提供该聚合 API。

锁顺序为完整预算键按 Ordinal 排序的事务 advisory 锁，再预算行锁。64位资源哈希碰撞仅增加互斥，不允许效果串错；所有读写继续带完整首键。数据库触发器补充重叠、版本和原文/终态约束；死锁、超时、SQL 约束失败必须整个事务回滚。未来上层须将其纳入完整 CANONICAL/SCOPE/DETAIL 顺序，不能在已有相反锁顺序之后插入本组件。

## 后续必须完成

正式 T04 的 context/catalog、可信部署与原主数据解析、`transfer.*` 当前权限/范围、draftKey/commandKey 原操作及 RequestVersion/审批状态，需要接到本组件同一事务。编辑与撤回必须检查真实 Request/Stage/ApprovalHead 后精确释放或重绑。再落实 Approval、REP 的完整 cut/目标预算、Transfer→Stock SourceAllowance、Root/Occurrence 与共同事务。现有 REP/RMA 写入围栏不变。

单份实测与失败记录见 [W142 交付报告](../../development/evidence/W142-01/DELIVERY.md)。
