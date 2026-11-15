# W641-01 PostgreSQL 旧子流程父续行

`codex/w641-01` 从 Main `4df0a3a4253eb05a22af259e14ca9817ef24857d` 开工。交付源码 `9e0618898a822ab9036a5ffa94c9f4b4563beb7d` 对应 PGT-017 #1012 本批旧子流程续行。五个源码和测试文件通过集中审查及 `git diff --check`。核对本地 `origin/main` 跟踪引用 `db6c58fd89006524201d2578b6ffc890525e0712`，W642-01 的文件与本批独立。

## 事务合同

- PostgreSQL 提交后的 inline 续行按当前租户和原 job 标识锁定已提交的 Pending `SubFlowResume`。新事务先取 Main 根，再读租户有效性和父子原关系，提交前复核已读取的到期时间。原 `Wf_ServiceJob.TokenId` 表示子实例，`ActionRefJson.ParentTokenId` 才是父 token。父 token、任务、历史、`Wf_Notification` Outbox 及同一 job 的结束状态共同提交。暂停先时，原 Pending 凭据可由现有 lease worker 在恢复租户后接续。[FlowEngine.SubFlow.cs](D:/CP6-worktrees/dev-2/CP6.Core/Services/Wf/FlowEngine.SubFlow.cs:183) [WfServiceJobService.cs](D:/CP6-worktrees/dev-2/CP6.Core/Services/Wf/WfServiceJobService.cs:203)
- 已有 Main 原生事务的调用保留该事务及其他追踪状态，只刷新本次已保存的 job。祖先 job 由终态父实例正常入队，暂停可在两个原 job 之间生效。角色撤销影响新的撤回，已获准且提交的撤回仍由其持久化 job 授权续行。续行进入同步 WebApi 节点时采用原持久化延期执行路径。
- `POST /api/wf/flow/{id}/withdraw` 在明确收到原撤回提交后保留原 200 响应。其后续行 COMMIT 回执未知时记录续行异常，恢复由原行状态决定。原撤回事务的错误响应沿用既有入口。[TaskController.cs](D:/CP6-worktrees/dev-2/CP6.WebApi/Controllers/Wf/TaskController.cs:38)

## 实测

隔离 PostgreSQL 18.6 和 Main Kestrel 使用模拟租户、已发布父子流程、实际登录和签名请求。独立 PostgreSQL 连接读取完整业务行与 `pg_blocking_pids` 等待链。基线 0/2、候选新用例 4/4、同候选相关回归 24/24，零跳过。

| 场景 | PostgreSQL 与 HTTP 观察 |
| --- | --- |
| 固定基线，暂停先 | 原撤回 COMMIT 已由数据库确认。独立回读看到子已撤回、父保持原状态、原 job 为 Pending。挡住暂停的租户表后交付撤回应答，旧实现未出现续行的显式 Main 根申请，夹具等待超时。基线此序在断言点结束，暂停后的最终业务写集没有采到。 |
| 固定基线，续行先 | 观察到父行上的实际等待。旧实现分开保存父效果和 job 完成状态，夹具放开父行后，持有租户表的暂停请求在第二次保存等待阶段实际返回 `55P03` 和 HTTP 500。 |
| 候选，暂停先 | 在原撤回已落库且尚未向应用转发 COMMIT 应答时启动签名暂停。暂停先持根，inline 在真实 `SELECT cp6_authentication_v1.acquire()` 等待，`transactionid`。原 job `91867931-e95e-49da-b628-c5ac4e621ec2` 保持 Pending，暂停前后业务快照散列均为 `e54946b443691185af2c235c294a10c00cd0badacd605fdd8631ba6fc94e2f1f`。签名恢复后，由实际 W625 lease worker 消费同一行并推进一条父待办和一条 Outbox，再扫零效果。 |
| 候选，续行先 | inline 持根等待父实例原行，签名暂停在 Main 根等待，均为 `transactionid`。放开父行后且暂停租户更新尚未提交时，独立回读已有原 job `3a796daf-091b-4ed3-91ff-5f6733bb9150` 成功、父错误分支、历史、待办和 Outbox。暂停前后的完成态一致，散列为 `fd9f32436483f2be4975367389446542a0469a7c7eeaa708f63ee5274d389844`。 |
| 候选，续行 COMMIT 回执丢失 | loopback 在第二段 PostgreSQL 确认 COMMIT 后切断回执，真实撤回 HTTP 返回 200。原 job `738eb9ee-46dd-4f74-b0ea-6181b1f2dd8e` 和原父效果完整可见，worker 再扫零效果。 |
| 候选，三代祖先链 | 实际撤回沿原三代链生成并完成两个 job，各自 `Succeeded`，原父实例 `Rejected`，祖父进入原错误分支。有中间层终态通知和祖父待办通知各一条。 |
| 同候选相关回归 | W577 原角色撤销两序 2，W606 `POST /api/fin/budget/versions/{vid}/submit` 提交后续行 1，W625 原 journal timer 和暂停两序 2，原子流程复核和 lease worker 组件 19。 |

TRX 带独立读回的 JSON、业务快照 SHA-256 和等待链。基线 PostgreSQL 57151，候选 57152，相关回归 57153。候选双宿主为 57156、57157，COMMIT 中继为 57155，相关用例复用 57154。相关用例运行时设 `Kestrel__Endpoints__W641Regression__Url=http://127.0.0.1:57154` 和 `CP6_JOURNAL_APPROVAL_HTTP_PORT=57154`。回放参数含筛选和被测源码指纹。[基线 TRX](D:/CP6-test-artifacts/W641-01/baseline-inline/results.trx) [候选 TRX](D:/CP6-test-artifacts/W641-01/candidate/results.trx) [相关回归 TRX](D:/CP6-test-artifacts/W641-01/related/results.trx) [候选命令](D:/CP6-test-artifacts/W641-01/candidate/invocation.json) [相关回归命令](D:/CP6-test-artifacts/W641-01/related/invocation.json)

本批实测覆盖旧撤回、预算提交、原 lease 接续及已有 journal timer。延期 WebApi 采用既有处理程序，本批未单独执行网络 connector 或全部 PGT-017 写者验收。正式 PGT-017 仍为 `NOT_RUN`。

## 本地发布和工作区

发布的 Main WebApi、Core、PostgreSQL persistence 程序集与上述实测副本 SHA-256 一致。310 个文件，发布清单 SHA-256 `b96ffccc9a9483e64bf1984caf3987fea82b07acd655e34d48a189aaea771af3`。从已发布文件在端口 57158 启动，`GET /health/live` 为 200，匿名 `POST /api/wf/flow/{id}/withdraw` 为 401。结果用于本地发布检查，生产制品及部署另行验收。[发布清单](D:/CP6-test-artifacts/W641-01/publish-sha256.json) [源码与冒烟记录](D:/CP6-test-artifacts/W641-01/publish-smoke.json)

本任务 PostgreSQL、Kestrel 和 loopback 中继已停止，57150–57159 无监听，证据目录无运行中数据库的 `postmaster.pid`。分支保留在本地提交，DEV-2 工作树干净，席位等待协调方集成、处置分支及 `finish`。Actions 触发配置及生产环境无操作。
