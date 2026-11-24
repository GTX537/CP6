# WP06-13 交付记录

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