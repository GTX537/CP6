# WP12-15 第 9 回合裁决后的最小拆分候选

同一 [Draft #1319](https://github.com/GTX537/CP6/pull/1319) 供总控独立复核。基线 `a52330b4ef243146e51560ae40d664c9b99d376e`，受验源码 `9ad452dbdfe33fb8af967f432704660c6050aa33`，源码 Git 树 `2d94b28a0c92e2113e894aacf0bac223af936f3b`。本轮交付范围依据总控第 9 回合裁决和原派工边界。 [Handoff #423 采用决定](https://github.com/GTX537/CP6.AI-Handoff/blob/f438891212b38396b3b90d58faea8a954bc09078/handoffs/CP6-DEV-20261012/tasks/COORD/replies/20261123T165911Z-coord3-wp12-15-dispatch/MESSAGE.md)

## 候选内容

版本化 TIMER 配置页在输入框旁提示“标准五段 Cron，1–200 字符。”，用 `aria-describedby` 关联提示，并保留 `required` 和 `maxlength="200"`。新建和修订复用 Main 的 `CheckConfig`，按原有五段解析和 1–200 字符合同校验。唯一产品差异是同页提示。其余文件为定向验证和本报告。 [配置页](../../../../cp6.web/src/views/oa/admin/ConnectorControlPanel.vue) · [现有 TIMER 校验](../../../../CP6.Core/Services/Oa/ConnectorVersionControlService.Versions.cs#L145-L160)

预演、导出、Cron 时点解释和第二版对旧路由的改造已移出本候选。对基线和候选逐树核对了 `CP6.Core`、`CP6.WebApi`、实体、PG 持久层、前端 OA API、路由和身份代码，全部 Git 对象相同。状态台账、任务定义、计划和生成视图与 Main 一致。

## 本机验证

在 `LAPTOP-3QQ44FJS` 的原 DEV-1，使用隔离 PostgreSQL 18.6、合成租户、真实 HTTP 和 Chrome。定向 .NET 共 12 项通过，0 失败、0 跳过，包括两项本轮新增验证、原生 `/simulate` 的 Main 回归和九项既有 Cron 单测。Vue 类型检查、浏览器脚本语法检查和只读台账结构检查通过。SQL Server 为 `NOT_RUN`，本次版本化控制面走现有 Npgsql 路径。

| 场景 | 实测 |
|---|---|
| 版本化草稿新建与修订 | 两条路径均接受 200 字符的合法五段表达式，逐个回读为未发布 DRAFT，原字符串完整保留。两条路径均以 `422 TIMER_CONFIG_INVALID` 拒绝合法语法的 201 字符、空输入、四段、六段及超范围分钟。拒绝请求的命令回执和运行记录增量为零。 |
| 同一配置页 | Chrome 确认提示可见、输入框具备对应无障碍描述和 200 上限，实际 POST 原样提交 200 字符，HTTP 200 后由数据库回读为未发布 DRAFT。 |
| 旧入口 | Main 的 `/api/oa/flow-triggers/cron-preview` 对合法的 201 字符表达式仍返回 200 及含 5 个时点的 `data.next` 数组，非法分钟返回 `400 E-WF-022`。旧 `/api/oa/forecast/preview` 对合成缺失流程仍返回 `400 E-WF-006`。原生预演回归仅核对 Main 既有行为。 |
| IAM、排程和写集 | 成功新建和修订各产生一条配置命令回执，Chrome 另有一条草稿回执。角色菜单和动作绑定快照一致，实例、令牌、任务、服务作业、触发流水、运行成员及结果和启动 Outbox 增量为零。旧停用 TIMER 的配置、版本、`NextDueUtc` 和 `LastFiredUtc` 逐项不变。API 测试宿主关闭后台服务，Chrome 仅访问本机环回，流程定义的绑定前置使用合成资料。 |

原始证据根目录为 `C:/Users/tt/CP6-test-artifacts/WP12-15/20261123-wp1215-exec3-r3`。从原 DEV-1 运行根目录的 `run-wp1215-pg.ps1 -Run <新名称> -Filter "FullyQualifiedName~OaConn_TimerDraft_|FullyQualifiedName~Simulation_actual_HTTP_exact_draft_precision_read_authority_and_no_business_effects|FullyQualifiedName~WfCronHelperTests" -Browser` 可复验。依赖以 `dotnet restore CP6.Tests/CP6.Tests.csproj --locked-mode --artifacts-path <证据根目录>/dotnet --disable-build-servers` 还原，脚本创建新的隔离 PG 目录并选用环回端口。

| 原件（相对于证据根目录） | SHA-256 |
|---|---|
| `wp1215-split-candidate.trx` | `9b121ae64755ba36570a42b7a2653e9f3384603395c3f4a8cd396d70cbc9420a` |
| `permission-consumption-wp1215-split-candidate/timer-draft-browser/result.json` | `e0305ea5dc5963a4d35b96a44b1418752ab29029d7d3313990e29f3aab28bef1` |
| `permission-consumption-wp1215-split-candidate/timer-draft-browser/draft-input.png` | `f8f6362fc77c9f9b9a4f41e24fe7054de83b1891faf97c3f212f8ee8d9095434` |
| `baseline-parity.json` | `3566331d52c88c492f0c96e138e873d1a40d6311d690ab196c1155da5c032435` |
| `roadmap-structure.json` | `7bd383621ef00bc1a95ab59ce676a2a223fb2ff9473985b76ade092ec04cf9ce` |

`source-bind.json` 绑定受验源码与三个文件的 Git blob。`wp1215-split-candidate.log`、`vue-tsc-exit.txt`、PG 版本及停止记录保存在同目录。原始轨迹中的身份和状态全属隔离夹具。

## 余项与恢复

- WP12-15 现行台账为 `PLANNED`，`CONN-15/16/17/30` 均为 `NOT_RUN`，OA-CONN-01 保持 16/32。拆分结论和台账由总控复核后处理。
- 拟议 WP12-15b 保留预演、导出及 Cron 时点解释。权限采用受阻，所需凭证为 SYS-IAM 和 OA Connector Owner 采用的 `conn.forecast` 正式菜单、当前租户范围、资源与字段绑定或严格等价映射。时间出口另需正式时区规则与工作日历修订采用凭证。
- WP12-14b 为独立 `BLOCKED` 项，四类真实启动仍依赖 `RP-CONN-01/02`。本轮只保存版本化 TIMER 草稿。
- 第二版 `2c13573d06190d57d5dd8d14b7fccaea78dc0467` 作为同分支祖先完整保留。原报告、TRX、Chrome 证据在旧根目录 `C:/Users/tt/CP6-test-artifacts/WP12-15/20261123-wp1215-exec3-r2`，其 SHA-256 已分别复核为 `4171b5eff92b812eebc1f2ada86ea9f467d93a3790c4f9e134c6708df183138b`、`63afb3bc9437e670a50bdbd87ac5678f28939ed9d767640bf8961933091d117d`、`f1c37370342fe46ddc9f21f864d00b856eda5234b2469cca530cfba56716629a`。第二版为后续恢复凭据，验收结论沿用第 9 回合 REJECT。 [第二版报告](https://github.com/GTX537/CP6/blob/2c13573d06190d57d5dd8d14b7fccaea78dc0467/docs/development/evidence/WP12-15/DELIVERY.md)

## 工时与席位

本轮从 `2026-11-23T18:54:34.5505900+00:00` 原席位恢复至 `2026-11-23T19:07:07.6871522+00:00` 候选报告快照，实际增加 753.137 秒，累计 5014.545 秒。此前有效 4261.408 秒，领用前核对本轮 92.379 秒及此前 171.438 秒单列，等待裁决时间从有效账排除。快照后的实际收尾尾差以远端回读记录为准。DEV-1 继续由 CP6-EXEC-3 持有本卡等待总控。DEV-2 空闲。

WP12-14 的 754.861 秒继续待合资格 Main 台账 PR 按原 DELIVERY 单次结转。 [WP12-14 原 DELIVERY](https://github.com/GTX537/CP6.AI-Handoff/blob/951c930a7e05ecd544a3e5ab03770798b7a52793/handoffs/CP6-DEV-20261012/tasks/WP12-14/messages/20261123T163520Z-cp6-exec-3-delivery-closeout/MESSAGE.md)
