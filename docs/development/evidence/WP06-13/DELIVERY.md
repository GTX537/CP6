# WP06-13 交付记录

## R10 本机关卡候选

仅本机候选。台账及生成页的 DONE 和计数为拟采用状态，需 COORD 独立复核和原 #1326 受保护合入后才生效。远端草稿仍为 PLANNED。verified_at 记录本机技术核对时间。独立签收与实际 Main 采用待记录。OA 暂停，next_card=null。

原远端 Draft 为 `28c7f6114f02578e3f1c4cbe33e0227541e41b40`，Main 基线为 `ada8cd1f0a9a42fd61777e4c0bc9f455863274ed`。本机关卡候选只更新台账、本报告和对应生成页，受测产品仍为 `e34acbdbc911556b84cc714e42d784994470186e`。

使用三轮可复算工作窗上界 **4409.716 秒**预填 M 卡记录，上限 14,400 秒，拆分线 21,600 秒。R9／R10 使用转达复核标识的时间戳作为工作窗上界的起点。各轮间等待剔除，未来独立验收、合并和席位收尾按实际结算。拟累计从 226420.710 秒到 230830.426 秒。详情见本机 local-closure-time-basis.json。

## EXEC5 R10 先授权再查询原操作

依据转达的 COORD R14，在 LAPTOP-3QQ44FJS 原 DEV-1 更新同一 Draft #1326。Main 台账 WP06-13 保持 `PLANNED`，OA 暂停且 `next_card=null`。 [Draft #1326](https://github.com/GTX537/CP6/pull/1326)

| 对象 | 精确提交 | Git 树 |
| --- | --- | --- |
| Main 基线 | `ada8cd1f0a9a42fd61777e4c0bc9f455863274ed` | `16a79bd5b9b12ab71e7b9577ad42ddd879062468` |
| R9 Draft 锚点 | `0716b8ad616d3825c941292a86f9096c51c06dbc` | `39fc0a2aa903e9af0230b22b5e73e4562a77abc0` |
| R10 固定受测产品 | `e34acbdbc911556b84cc714e42d784994470186e` | `78f1737ee02f20450d0b0ba5a3cdabd2f477806c` |

R10 固定受测产品到本轮 Draft 的唯一差异为本报告。Draft 的精确 SHA 和树见 PR 描述及本机 `draft-readback.json`。

- `ReceiveAsync` 在同一事务内先核对当前可信主体、适用能力和持久 Sales 原件的对象披露范围。原操作查询只有在独立的结果读取能力与对象授权成立后才执行。仅有接收能力时可以完成首次接收，该路径检查自然键占用以保持事务内幂等。 [正式授权与恢复合同](https://github.com/GTX537/CP6/blob/ada8cd1f0a9a42fd61777e4c0bc9f455863274ed/docs/design-handbook/remediation/02-commercial-physical-contracts.md#L28)
- 接收入口将缺失和不可见来源统一为 `ACCESS_DENIED`，回查在同一可见范围规则下统一返回 `NOT_OBSERVED`。两者保留中性响应。合法原键恢复返回同一原回执，发出结果或提交业务前复核当前授权。
- 固定产品使用 .NET SDK 10.0.302 和隔离回环 PostgreSQL 18.6。**41 通过、0 失败、0 跳过**，含 18 个授权单元用例、6 个 Planning PG、15 个 Sales PG 和 2 个订单披露 PG。新增 7 个单元用例用替身核对能力和对象检查先于历史查询。正常合成业务验证仅接收能力可首次接收，仅结果读取能力可恢复原回执。
- 原事务语义保持。隔离回归中的事务 `1640` 回滚，事务 `1642` 实际提交，从新连接按原键取回同一回执。H32 优先、同源同版去重、历史与当前效力分列均已复验。新增授权用例和业务回归均在本轮固定产品执行。

本机 `D:/CP6-test-artifacts/WP06-13/exec5-r10/` 保留原始 `results/wp06-13-r10-candidate.trx`，SHA-256 为 `f804f322ab24b7ea01586ba0cd468a5bd06a5440f8d93f0e06ac1d19129d18ed`。同根 `candidate-proof/index.json` 索引 31 份原件和状态，`candidate-run.json`、`candidate-assemblies.json`、`strict-prerequisites.json` 和 `candidate-cleanup.json` 保存受测身份、17 项前置以及本机清理记录。回环 PostgreSQL 和旧 HTTP 回归监听均已停止。

整卡待 COORD 独立复核。生产服务身份及授权接入、真实 Sales 调用、H32 生产解除授权、历史迁移及下游采用保持 `UNPROVEN`。外部投递、新增 HTTP／UI、生产迁移和部署为 `NOT_RUN`。

## EXEC5 R9 当前调用授权

依据转达的 COORD5 R13 复核，在 LAPTOP-3QQ44FJS 的原 DEV-1 修正同一 [Draft #1326](https://github.com/GTX537/CP6/pull/1326)。Main 台账的 WP06-13 为 `PLANNED`，OA 暂停且 `next_card=null`。本轮结果交 COORD 独立复核。

| 对象 | 精确提交 | Git 树 |
| --- | --- | --- |
| Main 基线 | `ada8cd1f0a9a42fd61777e4c0bc9f455863274ed` | `16a79bd5b9b12ab71e7b9577ad42ddd879062468` |
| R8 Draft 锚点 | `d0f17ff4d772bab196e56a8f5b61fe02c0e7805d` | `d94659fc8b08eab01e38ac82dcaadd08db2146f1` |
| R9 固定受测产品 | `b6e8ce06f8441e05d57b888304e5859c1cc79992` | `4ab8be79e7cef5b652964142b8dadc9fbe035065` |

R9 固定受测产品到更新后的 Draft 仅差本报告。Draft 当前 SHA 与 Git 树见 PR 描述及本机 `draft-readback.json`。

### 当前权威与披露

- 进程内 Planning 接收依赖同一 `CP6Context` 的 Main 当前授权。通过现有会话原件验证、可信租户、PostgreSQL 权限和订单所有者的披露参加端口验证。首次接收需要明确登记并已授权的 `service:planning.source.receive`。原键重试与结果回查需要独立的 `service:planning.source.result.read`。能力缺失时拒绝。 [正式合同](https://github.com/GTX537/CP6/blob/ada8cd1f0a9a42fd61777e4c0bc9f455863274ed/docs/design-handbook/remediation/02-commercial-physical-contracts.md#L28)
- 读取原回执前，先核对持久 `Main.Sales` 来源与原操作的主体、原单和原行的当前可见范围、完整披露权限以及原谱系／源版／摘要。授权不足的对象和未观察对象在回查中都只返回未知，无原件披露，也无未执行证明。返回回执前和首次接收提交前复核当前授权。新增审计字段记录本次能力与当前披露依据，原结果字节及既有事务语义保留。
- 合成隔离夹具提供本机主体与两项角色能力，消费真实 Main 权限表和对象披露查询。夹具的主体验证回调仅在测试中使用。生产启动沿用既有 Main 当前会话重核，未登记生产角色、服务身份或权限。实际服务 Producer／环境／epoch 和生产外部授权绑定仍为 `UNPROVEN`。

### 固定验证

在 .NET SDK 10.0.302 和隔离回环 PostgreSQL 18.6 构建及测试。**34 通过、0 失败、0 跳过**，包含 11 个新授权单元用例、6 个 Planning PostgreSQL 场景、15 个 WP04-32 Sales 回归和 2 个既有订单范围／字段披露回归。构建与测试退出码均为 0。测试使用新实现和合成数据。

原本机业务场景继续通过，包括同源同版去重、H32 优先、Draft／Confirm 无 Demand、实际提交确认丢失后回查。事务 `1637` 回滚，事务 `1639` 实际提交并按原键恢复。夹具撤销接收授权后，保留读取能力的正常回查和原键重试仍返回相同原回执，最终 Demand／Version／Binding 各一份。

本机证据根为 `D:/CP6-test-artifacts/WP06-13/exec5-r9/`。原始 `results/wp06-13-r9-candidate.trx` 的 SHA-256 为 `e6407000fc2f783233a7528e25d745dac30787ad9db08d7af03e17a9446fab4c`。`candidate-proof/index.json` 索引 31 份原件及前后状态。`candidate-run.json`、`candidate-assemblies.json`、`candidate-scope.json` 和 `candidate-cleanup.json` 记录运行、构建身份、覆盖及回环实例清理。工作树干净，任务 PostgreSQL／HTTP 监听及进程均已停止，DEV-1 保留登记供 COORD 复核。

真实 Sales 调用、生产服务绑定、生产 H32 解除授权、真实历史迁移和完整下游采用保持 `UNPROVEN`。外部投递、新增 HTTP／UI、生产迁移与部署为 `NOT_RUN`。

## EXEC5 R8 原草稿交付记录

CP6-EXEC-5 R8 在 LAPTOP-3QQ44FJS 的 DEV-1 实施 Planning 原生来源接收。Main 与 Platform 的 17 项递归前置已核对为 DONE，采用提交在各自固定远端 Main 的祖先链上。DEV-1 于 2026-11-24 15:56:52 UTC 单独登记。本轮交付至 Draft，卡级采用待 COORD 独立复核。 [固定派工](https://github.com/GTX537/CP6.AI-Handoff/blob/77b103601cbfaa94fc14e3b06164b54b1197f46a/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261124T154435Z-coord5-wp06-13-decision/MESSAGE.md) · [原卡](https://github.com/GTX537/CP6/blob/ada8cd1f0a9a42fd61777e4c0bc9f455863274ed/docs/development/task-plans/WP06.md#wp06-13)

| 对象 | 精确提交 | Git 树 |
| --- | --- | --- |
| Main / base | `ada8cd1f0a9a42fd61777e4c0bc9f455863274ed` | `16a79bd5b9b12ab71e7b9577ad42ddd879062468` |
| 本机固定受测产品 | `6bd87bb064cbdc3e8b81390bf5d788a58d21ca92` | `3b5df4aab9c02c29f0afa822af0732663b465c9a` |

分支为 `codex/wp06-13`。固定受测提交至 Draft 的唯一差异为本报告。Draft 的精确 SHA、树和 PR 编号记录在 PR 描述及本机 `draft-readback.json`。

## 实际改动

- `DemandSourceIntake` 是 Main 进程内的 `ORD-DEM-SOURCE/1` 接收与原键回查入口。它在 Main 现有 IAM 根下核对当前租户，并在该租户解析已提交的 `Main.Sales` Owner 原操作，逐字节核对完整来源输入及摘要。接收和源控制共用操作键及谱系锁，并在相同 PostgreSQL 事务内核对 Sales 的原生当前发布门。
- PostgreSQL 独立保存 Demand、版本、原单行／交期／释放绑定、控制事实及 Inbox。接收回执和审计使用现有 Main Owner 日志，原 Sales body、Envelope 及回执保留。相同原键、原版及原字节重投读回第一次提交的结果。
- H32 的控制记录保留动作、UTC 时点和确切范围。H32 可以先保存而无 Demand。本机规则按原订单商业版、订单行、Schedule、谱系及 PLAN 动作检查迟到接收。已接受版、已见源版和历史回执分开，历史成功可以和当前阻断同时返回。缺发布或解除证据维持未知。

## 原卡验收

| 场景 | 本机合成验证 |
| --- | --- |
| 初次释放、顺序及并发幂等 | **PASS**。既有 Sales 参加端口持久化真实 H10 形状，独立进入 Planning 真实服务。正量 2 的交易量和标准量、订单／行／版本／交期及释放原绑定一致。顺序和运输别名重投、两笔重叠的 PostgreSQL 事务各只留一份 Demand、版本、映射、Inbox 和原回执。 |
| H32 先于迟到 H10（W1-DEM-03 接收端） | **PASS**。独立 H32 v2 在 Planning 尚无 Demand 时保存两个带准确作用域的控制。测试时钟在生效前一个 tick 回查 H10 为 `NOT_OBSERVED`。PLAN 控制到点后，迟到 v1 留下 `BUSINESS_REJECTED / SOURCE_CONTROL_BLOCKED` 原回执。两笔 Inbox、两条控制，Demand／Version／Binding 均为零。另验 H10 已接受后生效的 PLAN 控制，历史接受回执逐字节保留，当前效力另列。 |
| 提交后确认丢失与提交前回滚（W1-DEM-08、W1-DEM-RECOVER-01 接收端） | **PASS**。回滚事务 `1584` 内六类写入各一行，第二连接和回滚后均为零。事务 `1586` 实际提交后由夹具丢弃确认，另建连接用原键、谱系、源版和摘要恢复原回执、Demand ID／v1 及当前效力。Inbox 和原结果行的 PostgreSQL 事务号一致，重投仍为一份 Demand。未见、未提交的结果维持 `NOT_OBSERVED`、`noEffectProven=false`，无 finality。 |
| Draft／Confirm | **PASS**。夹具里的普通 Draft 和实际 Sales Confirm 参加端口各保留正常事实，正式 H10 尚未提交时 Planning 保持未知。缺原 H10 的接收返回 `PLANNING_SOURCE_UNPROVEN`，全写集为零。 |

## 运行与证据

在本机以 .NET SDK **10.0.302** 全新构建固定产品提交，并用隔离回环 PostgreSQL **18.6** 和完全合成的数据执行 `CP6.Tests/CP6.Tests.csproj`。产品构建和测试均返回 0。**21 通过、0 失败、0 跳过**，含本卡 6 个场景与 15 个 Sales 回归。执行窗口为 2026-11-24 16:11:00.993Z 至 16:16:25.414Z。首轮增量构建混用了旧列名与新迁移，原始失败 TRX 保留。固定提交在新产物目录全量构建并重跑验证。

本机证据根目录为 `D:/CP6-test-artifacts/WP06-13/exec5-r8/`。

- `results/wp06-13-r8-candidate.trx` 是原始结果。SHA-256 为 `5a89d0a454c5b5aa642408ab576410314965ea6d77e98f965b3c4d90dae1fb20`。
- `candidate-proof/index.json` 索引 6 个场景、31 份按 TRX 导出的原件和校验摘要，包括 H10／H32 原字节、原回执、持久前后状态及回滚与提交事务号。`candidate-run.json` 和 `candidate-assemblies.json` 记录精确构建／测试选项、过滤、四个程序集的固定修订和哈希。
- `strict-prerequisites.json`、`dev-1-start.json` 与 `candidate-cleanup.json` 记录基线、单席登记和清理。两次本机 PostgreSQL 实例已停，回归用的回环 HTTP 和任务进程、监听均已清理。
- 普通分支推送和创建 Draft 前核对 Main 与候选两侧的 GitHub 和 Azure 工作流及变更路径。GitHub 工作流只接受手动入口或候选标签，Azure 四份配置关停 push／PR 触发，DEV 的流水线完成触发限定 Main。仓库可见触发数据及 Draft 回读存于同根。

真实 Sales Confirm／Release／Control 调用、外部服务身份与 HTTP 采用、H32 生产解除授权、真实历史迁移、Planning 后续完整生产链和不可拆组、WMS 采用为 `UNPROVEN`。真实投递、生产或 SQL Server 验证及新增 HTTP／UI 为 `NOT_RUN`。OA 维持暂停。 [合同](https://github.com/GTX537/CP6/blob/ada8cd1f0a9a42fd61777e4c0bc9f455863274ed/docs/design-handbook/remediation/02-commercial-physical-contracts.md#L139) · [验收目录](https://github.com/GTX537/CP6/blob/ada8cd1f0a9a42fd61777e4c0bc9f455863274ed/docs/design-handbook/remediation/08-acceptance-catalog.json#L793)