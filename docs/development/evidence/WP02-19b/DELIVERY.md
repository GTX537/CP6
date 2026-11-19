# WP02-19b 客户端激活原结果与认证设备边界

**WP02-19b 七项原验收全部通过。** 本卡候选产品提交 `abb9df8885bdda22b001b849b936ee052425d48e`。按用户确认的组合证据核销，SYS-CLIENT-01 为 17/22，目标保持 PLANNED。

执行人 `EXEC-WP02-14B` 由远端 Main `e1759095d020c6685b6ca5e2a0da4fd5de99005d` 领取 `DEV-1`，工作区 `D:/CP6-worktrees/wave17-dispatch-20261018`，分支 `codex/wp02-19b`。派工原件 `000c396d6d0385710ca6288edf95fce6390be211`，登记采用 `70f416a67467c2e3852e61e3d792be2fea15e6e0`，原件及登记内容一致。父卡 #1266 已在该远端基线，#1268 已登记本余卡解阻。只在本卡席位写入，Windows 工作区使用 `git -c core.longpaths=true status` 核对。 [派工 #325](https://github.com/GTX537/CP6.AI-Handoff/pull/325) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/300e44a3372065e0d4fae4bd79c9c2d89c363c65/handoffs/CP6-DEV-20261012/tasks/WP02-19b/messages/20261119T050345Z-exec-wp02-14b-start/MESSAGE.md) · [父卡报告](../WP02-19/DELIVERY.md)

## 实现

- 发行时将持久票据主键作为激活操作标识写入二维码。旧码可沿原激活路径使用。新客户端在发起提交前保存原操作标识，确认未知时以原设备签名只读查询。Main 单快照核对启用租户、原票据消费、原设备持钥、当前状态与可披露的登记范围。只读响应包含安全状态和获准的原六字段。已有行和索引继续作为持久依据。
- Native `registered-device-v1` 是 Main 具名声明的设备绑定门。完成门复核原票据绑定、当前设备和租户、服务端版本与原策略。已采用的旧可选路径保留现行 IAM 及设备校验。原登记证据仅在成功完成当前完整认证后用于更新客户端设备就绪。
- 原 Native SSO 请求、一次性 grant 与后续挑战绑定同一适配能力。旧端遇到无法处理的 pending 时关闭原交互并返回明确拒绝。Windows、Android 的现行登录页继续原挑战。换码结果未知时先按原交互查询或关闭，再由用户重新认证。异步响应、刷新、安全重试及心跳绑定原交互或会话。
- Android 作业草稿及队列按 Main 来源、租户、主体、设备和作业分区。已有无归属的旧版草稿文件隔离保留。离线记录的扫描键和完成键持久化，当前原主体联网后先问原 Owner。未经核实的端点拒绝保持草稿与原键。已确认原完成仅按该键清理草稿。历史数据结构和 WMS Owner 采用范围保留。 [原卡](../../task-plans/WP02.md#wp02-19b) · [裁决 #322](https://github.com/GTX537/CP6.AI-Handoff/pull/322)

## 七项原验收

| 原 AC | 原入口与本轮实测 |
| --- | --- |
| AC-CL01-03 | 管理员实际发票，两个 Main Kestrel 共用隔离 PostgreSQL。原设备不使用首个响应，第二宿主按原操作持钥读回六字段，同一消费及绑定各一份。错误键、租户、持钥错误、时钟偏差及禁用统一安全状态。回查前后持久写集相同。客户端重新实例化，继续读原操作，实际激活提交调用一次。 |
| AC-CL02-03 | 实际禁用、启用、换仓转 Personal，原 access 和 refresh 均为 401。FullAuth 为空，完整新登录后才恢复当前权限。客户端恢复时按原主体打开原分区，既有排队扫描键保持。 |
| AC-CL02-04 | 原设备从 W1 转到 W2，W1 真实在途与完成作业保存原 Owner、仓、代次及完成键。现行 Main 可读原完成，关闭扫码配置、新扫描和完成，实际状态为 503，写集保持。获准的模拟 Owner 证明客户端在原归属及输入相符时只使用原键继续。 |
| AC-CL03-04 | 真库验证不合时钟的持钥请求不给结果，真实心跳拒绝保持前次观测。客户端切人后忽略迟到心跳及刷新，清空页面上的旧草稿。新主体无法读取或清除旧文件分区。原主体重新授权后读回原作业。 |
| AC-CL04-02 | 实际 Main 旧可选允许未登记的关联标识。发布具名 RequiredGate，当前注册、有效签名及版本通过。未知能力、缺证明、外租户、错误持钥、过低版本及禁用拒绝。支持旧可选的目标租户中无外部设备写入。 |
| AC-CL04-04 | Main 完成门、刷新与快切定向验证保留原 FullAuth 期限和原因子边界。客户端迟到旧用户刷新不写当前会话或安全存储。换人后不能读旧草稿，原主体恢复可取回相同作业、数量、扫描键及完成键。 |
| AC-CL04-05 | 实际 PostgreSQL 与受控 IdP 运行支持及不支持潜在挑战的 Native SSO。需要本地绑定或校验时，旧端收到 409 和 `AUTH_NATIVE_ADAPTER_REQUIRED`，原交互为 CLOSED，挑战、会话和 token 均为空。现行端可继续完成原挑战。客户端确认未知只查询及关闭原 grant。移动操作键独立于认证交互。 |

原卡中已有 10 项 PASS，原设计共 22 项，AC-CL05 的五项升级由 WP02-20 承接。目标没有另列必需运行修订。 本卡核销七项后原号与余卡完整。升级五项继续保留原状态，目标继续开放。 [原目录](../../assessments/20261011/catalogs/foundation.json) · [接受设计](../../../design-handbook/modules/SYS-CLIENT-01.md)

## 真实业务边界及模拟

实际 Main 的 WMS 在当前 PostgreSQL 采用范围内只提供原业务读取。授权换仓和重新完整认证后，原完成查询返回 200 和同一 CompletionOperationId。扫码配置、在途扫描和完成均返回 `MOBILE_OWNER_ADOPTION_UNPROVEN`，无新扫描、回执或库存事务。扫码意图及正式库存效果分别由 WP10-09 和 WP10-12 采用。模拟 Owner 用明确的同一 API 合同验证获准的原主体补交原扫描及完成命令，模拟确认丢失后只查询原回执，一次业务提交。现行 Main 标签 Owner 另有采用路径实际复验，两个真实客户端进程在隔离 PostgreSQL 中使用同一完成键，累计一次模拟打印、零 fail、一次完成。 用户在本次执行对话确认使用真实 Main 与明确标注的模拟 Owner 组合证据。该确认处理 QUESTION 的验收口径。 [已采用 WMS 读取](../W72-01/DELIVERY.md) · [实际标签恢复](../BUG-889-LABEL-RECOVERY-FENCE/DELIVERY.md) · [QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/e9c372cd52bceaa90d13083e8b65e1e7a08a4690/handoffs/CP6-DEV-20261012/tasks/WP02-19b/messages/20261119T053608Z-exec-wp02-14b-question/MESSAGE.md)

## 本地验证及限制

Windows，Main 使用 .NET SDK 8.0.424 和 PostgreSQL 18.6 本地隔离库，Android 用 .NET SDK 10.0.302。实际 Main HTTP、前端二维码工具与客户端生产程序集分别运行。业务输入、设备钥和 IdP 用隔离模拟数据或服务。现场硬件、企业 IdP、APK 安装及发布按其他责任范围。

| 输出 | 结果 |
| --- | --- |
| `main-recovery.trx` | 10 项通过，包含原六字段回查、Required 正负、旧 SSO 拒绝、支持端继续及真实 Owner 边界。 |
| `main-auth-and-device-final.trx` | 131 项现行 PostgreSQL、认证、设备及 WMS HTTP 回归通过。末轮 `main-reviewed.trx` 12 项通过，其中 2 项实际标签路径与广选互补，合计 133 项不同用例，零跳过。 |
| `label-cold-recovery.trx` | 2 项 PostgreSQL 和原客户端进程回归通过。原客户端传输依当前 bearer 与源，两个进程恢复原键。 |
| `client-reviewed.trx` | 105 项通过，覆盖原设备重启回查、切人隔离、迟到请求及明确模拟 Owner 的恢复。 |
| Windows／Android、Web 和 OpenAPI | Windows 与 Android 开发构建通过，Vue 类型检查和二维码 2 项测试通过。真实 Main Swagger 检查器 4 项通过，客户端契约摘要 `CDE008F249D18CC2CCCB33CC40EA3F1227BB9FDBC76D1551103F378344CAEC6C`。 |

广选的历史 SSO 迁移夹具 2 项在目录准备时报 PostgreSQL 42703，旧数据库模型尚未安装审计列 `AuditAtUtc`。这些场景单列，现行定向运行沿父卡相同过滤。原始 TRX、现行编译、真实 Main 状态码、隔离停止标记及模拟记录位于 `D:/CP6-test-artifacts/WP02-19b/`。证据不包含可复用凭据。

## 工时与远端处置

本卡有效预算 12,888.190 秒，二次拆分升级线 19,332.285 秒。 正式开工 `2026-11-19T00:03:15.150064-05:00`，候选核对 `2026-11-19T01:00:23.711390-05:00`，本轮有效 3428.561 秒。累计计划 18.749 小时。

按 #324 本 PR 单次结转 WP02-21 官方收尾 545.852 秒。父卡候选 1060.188 秒，合计 1606.040 秒，WP02-21b 余额 12793.960 秒，150% 升级线为 19190.940 秒。 [#324 签收](https://github.com/GTX537/CP6.AI-Handoff/blob/c542da67515daf3df6932351cfe240f72f4e729a/handoffs/CP6-DEV-20261012/tasks/WP02-21/replies/20261119T045141Z-coord-split-ack/MESSAGE.md)

同一 PR 交付源码、唯一台账及四份状态页。实际远端采用、合并后冒烟、分支清理及 DEV-1 释放以官方 `finish` 和原通信分支 DELIVERY 为准。 [CP6 #1269](https://github.com/GTX537/CP6/pull/1269) · [单卡计划](../../planning/20261117-card-execution.md)
