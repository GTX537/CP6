# C04A 来源冻结证明与整组目标启用

从已核对远端 main 的 Core `648b691525de22ab8f262404f366db1defd582bb`、CRM `a22116ad029aea5219e19a93f47df26b64534cec` 创建同任务独立 worktree。此前多目标恢复与启动入口控制已交付，保留原通过证据，不重开无关套件。

## 实际范围与事务

实际执行范围是三份来源（本机原生 CP6DB、C02 私有 Core 库、Docker CP6DB）和同一本机 SQL 实例上的两个 CRM 组织目标。新增入口支持 2–16 个同一本机原生 SQL 实例目标，拒绝跨实例或不同连接安全设置；不把顺序提交多个事务描述为整组原子启用。使用一个连接、一个 Serializable 本地事务，通过准确数据库名称切换上下文，保留各数据库的应用锁和所有业务表锁，全部启用/审计变更只提交一次。

顺序：验证原配置/批准请求/工具包及来源文件 → 原有组织 Ready 检查 → 按组织 ID 顺序取得全部目标锁并核对完整集合 Anchor/代数 → 逐份运行受固定工具包约束的来源证明 → 验证来源唯一性、目标集合绑定、随机挑战及当前 Frozen 状态 → 按既有 Enable 规则写全部目标 → 再核对全部目标 → 单次 COMMIT。失败或取消回滚整组；提交结果不明时只允许同请求精确重放/状态检查，不能自动解除来源冻结。

现有合法来源恢复必须先取得所有目标的永久终止证明及锁。整组目标锁持有期间，新的来源 Frozen 观察与随后目标启用之间不会被该恢复协议穿插；实际来源不接受旧演练 Reopen。该论证不替代 SQL/Docker 管理员及未知写入者隔离，因此完整围栏、审批和路由验证标志仍为 false。

## Core 证明

新增按原始文件摘要绑定的 `prove-target-enable-actual` 命令；输入仅为私有来源连接环境变量、既有冻结请求路径/文件摘要、预期完整目标集合 Anchor 摘要和调用方新生成的 GUID 挑战。数据库/服务器/BrokerGuid/本机容器身份从严格解析的既有冻结请求取得；不允许覆盖成另一份来源。沿用 `ActualSourceFreezer.StatusAsync` 的真实身份、Frozen Scope、20 表保护及 Frozen/代数 1 验证，再生成规范 PascalCase JSON。

共享 `contracts/c04a/SourceFrozenProof.cs` 在两仓逐字节一致。记录字段：`Challenge`、`FreezeRunId`、`SourceFreezeRequestSha256`、`TargetSetAnchorSha256`、`SourceIdentitySha256`、`ServerName`、`DatabaseName`、`DatabaseGuid`、`FrozenScopeSha256`。Format 为 `CP6.C04A.SourceFrozenProof.v1`，State=Frozen、Generation=1；仅 `SourceFrozenAtObservation` 为 true，完整围栏和审批为 false。严格规范解析拒绝未知/重复/getter 篡改字段。它是当前观察证明，不冒充永久 ForwardOnly Seal。

## CRM 操作协调

新增严格、原文件摘要绑定的 `enable-targets-verified-local` 请求。包括原配置摘要、RunId/批准引用、完整目标 Anchor、各目标预期代数、1–16 份排序且唯一的来源冻结文件/文件摘要/请求摘要/私有连接环境变量引用，以及 Core 发布目录清单文件及摘要。许可的 SourceSealEvidenceSha256 绑定稳定启用请求摘要；实时随机挑战证明只进入本次报告，不改变精确重放许可。

具体来源验证器检查本机规范目录、精确文件集合/摘要、无重解析路径，持有发布文件句柄直至全部目标提交后释放。只启动固定的 Core DLL 与允许的 dotnet 宿主，明确设置子进程环境，连接不进入参数或公共输出。限制输出大小和执行期限，处理取消并等待自己创建的进程退出；禁止任意可执行文件/委托自报通过。证明必须匹配新挑战、请求摘要、Anchor，且所有来源物理身份唯一并与任何目标不同。

现有单目标 Enable/Close 的状态、许可、审计和首次实际业务写入规则继续复用。精确重放要求整个集合均为同请求的 Enabled；Written、终止、代数混合或其他运行均拒绝。新增成功报告明确来源在启用时已验证，完整围栏/批准/路由及实际执行验收继续开放。

## 验证与交付

Core 限定子任务负责共享证明、只读命令及纯输入/契约覆盖；主任务负责 CRM 验证器、整组事务、CLI 和真实发布跨仓 SQL 场景。所有构建、发布及 SQL 夹具由主任务串行执行。新覆盖至少验证三份来源/两目标成功、未冻结或错误 Anchor/挑战/包拒绝、任一目标失败的整组回滚、锁保留、精确重放与 Written 拒绝。只补直接受影响的原 Enable 行为，不重跑其他通过套件。

归档准确源码/发布绑定、失败原件、清理及范围证据，完成边界、完整差异审查、四份状态、正常 PR 合并和远端 main 包含性验证。每次远端写入前重新核对已暂停 Actions；不触发 Actions、Tag 或实际部署。

本轮不操作实际来源冻结或目标启用。宿主恢复依赖兼容、特权身份与实际入口完整性、最终具体执行/恢复包及 0.2 变化字节签收仍需完成；CRM11/C04B 正式验收和观察期保留。总体 60%（3/5）。
