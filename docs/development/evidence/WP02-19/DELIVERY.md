# WP02-19 客户端激活、nonce 与身份完成门交付

**拆卡交付。** WP02-19 原号核销已验证范围。AC-CL01 至 AC-CL04 的 17 项均已逐项检查，10 项通过，7 项仅有部分证据，由 WP02-19b 承接。SYS-CLIENT-01 保持 PLANNED。升级渠道、包签名、安装与回滚的 AC-CL05-01 至 AC-CL05-05 归 WP02-20。 [原卡](../../task-plans/WP02.md#wp02-19) · [余卡](../../task-plans/WP02.md#wp02-19b) · [原 AC 目录](../../assessments/20261011/catalogs/foundation.json)

执行人 `EXEC-WP02-14B` 在 Main `DEV-1`（`D:/CP6-worktrees/wave17-dispatch-20261018`）领取 `codex/wp02-19`，受测及已采用基线为远端 `d989ec140359e9f78b6274c4b0072d6ca704d402`。用 `git -c core.longpaths=true status` 核实工作区。WP02-03、WP02-04 均完整 DONE，无拆分余卡；正式核销的合并提交 `17aa9250d46a0859fcb2ce8ddd4e5bec4b69c42b` 与 `66b92018889889ce76438c3280283bcad008e7ab` 均为基线祖先。派工原件与登记消息字节相同。 [WP02-03 #1237](https://github.com/GTX537/CP6/pull/1237) · [WP02-04 #1239](https://github.com/GTX537/CP6/pull/1239) · [DECISION #321](https://github.com/GTX537/CP6.AI-Handoff/pull/321) · [START](https://github.com/GTX537/CP6.AI-Handoff/blob/fd396fdae5ad1fa4e813d9c599aa978d2a97004e/handoffs/CP6-DEV-20261012/tasks/WP02-19/messages/20261119T031616Z-exec-wp02-14b-start/MESSAGE.md)

## 实际设备与会话结果

- 两个独立 Main Kestrel 进程共用同一隔离 PostgreSQL。并发使用同一有效激活票据及同一设备申请，实际响应为 200 和 400，只有一行注册，激活行与票据的消费时间、租户和绑定设备一致。再次使用原票返回 400。装置本身无认证 Cookie、会话记录或 FullAuth。本地模拟调用方未收到首次结果，存量数据足以确认原持久关系，正式调用方回查出口仍缺。 [设备服务](https://github.com/GTX537/CP6/blob/d989ec140359e9f78b6274c4b0072d6ca704d402/CP6.Core/Services/Wms/ClientDeviceService.cs) · [设备 HTTP](https://github.com/GTX537/CP6/blob/d989ec140359e9f78b6274c4b0072d6ca704d402/CP6.WebApi/Controllers/ClientDevicesController.cs)
- 两个实际实例并发更新同一 RowVersion，200／409 唯一胜出。按源和目的库区复核管理权限，迁移仓库导致旧 Device revision 失效。禁用和 refresh 撤销同事务，实际撤销保存故障使整组保持。用原活跃会话先成功刷新，禁用提交后迟到刷新返回 401，重新启用后重复旧刷新也返回 401。三个数据库计数（refresh 行、认证证据、access 会话映射）均无新增，重新完整登录只新增一组。另一合规账号登录已启用设备必需策略的目标租户时，外租户设备返回 403，两边会话计数保持。
- 真实双实例验证顺序及并发 nonce 重复。共用唯一索引按 `tenant_id/device_id/nonce_hash` 占位，原生等待链证明两个请求实际在 PostgreSQL 同一共同根等待。两次交互各只有一个成功占位，重复得到 409。失败未刷新成功观察和持久会话，CurrentTaskNo 仅是遥测。两种有效签名、非法签名、陈旧时钟窗口和当前租户停用均有回归。 [原采用 #882](https://github.com/GTX537/CP6/pull/882) · [设备运行证据](../BUG-881-DEVICE-LIFECYCLE-FENCE/DELIVERY.md)

### 认证入口与当前决定点

| 现行入口 | PostgreSQL 的实际出口和验证 |
| --- | --- |
| Web 密码、Web SSO callback | 共同完成门确认当前租户、账号、认证/配置版本及原 Web 交互。实际密码登录、过期来源和输出决策回归。SSO 验签及原浏览器绑定用受控提供方，待本地因子仅返回 pending/CSRF，认证 Cookie 与业务会话为空。 |
| Native 密码、Native SSO start/callback/exchange | Native 与原 client/device 绑定。合法密码登录及受限拒绝经实际 Main HTTP。SSO 回调仅交付一次性原 grant，换码后仍走本地因子及当前认证完成门。覆盖真实签名、当前原策略与因子启用变化，IdP 和部分设备端请求为模拟。 |
| Web/Native 2FA setup、email、enroll、verify | 准备或发送步骤保有原挑战，完整因子流程核当前主体、策略、设备及原 challenge。成功发行发生在最后同一事务，重用、设备不符、旧 epoch、非法政策和必要审计失败均按现行门处理。 |
| Web/Native refresh、quick-switch | 原证据在轮转和输出阶段各复核。family、无 family 和原生拒绝分别验证，拒绝仅封闭相应代次。quick-switch 服从原共享窗口及当前目标因子，refresh／quick-switch 均不延长 FullAuth。 |
| Web/Native logout、完成状态与关闭 | 实测原浏览器／设备证明限定结果和撤销对象。提交不明可核对并关闭原交互，安全状态没有历史 token 或人资料。原业务操作的客户端完整恢复仍在余卡。 |

相关主线来源包含设备 `33ec0281a897ba95c8ff152803afddb23fb7576b`、设备 Owner `46a64f3f67633b54416bf49a2816642fe1266975`、身份交互 `9b6ca06bbc40390024bbc8919c5a6dfc05289bc3` 和会话完成 `4d75c61d824d934d0fe665307b229c0e8df107c6`，均已逐一核实为远端基线祖先。合法前置身份与租户分别沿已验收的 Owner 协议。 [设备当前授权](https://github.com/GTX537/CP6/blob/d989ec140359e9f78b6274c4b0072d6ca704d402/CP6.Core/Services/Wms/ClientDeviceService.Authority.cs) · [原生认证](https://github.com/GTX537/CP6/blob/d989ec140359e9f78b6274c4b0072d6ca704d402/CP6.WebApi/Controllers/ClientAuthController.cs) · [身份交互证据](../WP02-03/authentication-entry-completion/DELIVERY.md) · [会话连续性证据](../WP02-03/authentication-session-continuity/DELIVERY.md)

## 17 项原验收

| 原 AC | 结论 | 实测或保留出口 |
| --- | --- | --- |
| AC-CL01-01 | PASS | 票据核租户、平台和目标范围，实测两个宿主同时提交只有一台设备，消费与注册原子。`device-baseline.trx`、`device-supplement.trx`。 |
| AC-CL01-02 | PASS | 过期、重复、无效公钥及错平台拒绝；确认无残缺 Active 注册。`device-baseline.trx`、`device-supplement.trx`。 |
| AC-CL01-03 | 部分 | 原绑定已核实且无重建、无 raw 票据输出。正式调用方的原操作读取资格、原结果及未决收敛归 WP02-19b。`wp0219-activation-once.json`。 |
| AC-CL01-04 | PASS | 真实激活保留独立的设备证据，不生成会话或 FullAuth；匿名 profile 拒绝，业务 Owner 仍按当前认证及权限。`device-supplement.trx`、`auth-focused.trx`。 |
| AC-CL02-01 | PASS | 双实例同 RowVersion 唯一胜出，源／目的仓权限、白名单和租户不自动移动。`device-baseline.trx`、`device-supplement.trx`。 |
| AC-CL02-02 | PASS | 禁用与 refresh 撤销同一事务。模拟真实写失败整组保持。真实 profile／迟到 refresh 的效果门分别观测。`device-baseline.trx`、`device-supplement.trx`。 |
| AC-CL02-03 | 部分 | 重新启用后旧 token 仍拒，实际完整新登录成功。客户端离线意图和队列在恢复后的完整隔离归 WP02-19b。`device-supplement.trx`。 |
| AC-CL02-04 | 部分 | 实测授权换仓使原会话失效，模式变更保持设备归属。前序客户端恢复保留原业务键。缺真实切仓、切模式与同一在途业务 Owner 的组合验收，归 WP02-19b。 |
| AC-CL03-01 | PASS | RSA／ECDSA 心跳，错误签名、旧时间和值绑定失败保留成功观察。只签现行 DeviceId、时间、nonce 与 AppVersion，任务等字段仍为遥测。`device-baseline.trx`。 |
| AC-CL03-02 | PASS | 两个独立宿主共用 PostgreSQL，顺序与并发分别出现一个成功和一个 409。独立数据库回读和原生等待证据。`device-baseline.trx`。 |
| AC-CL03-03 | PASS | 心跳声明 CurrentTaskNo 后任务和原操作账保持，无完成或授权派生。`device-baseline.trx`。 |
| AC-CL03-04 | 部分 | 服务端时钟偏差拒绝，客户端心跳循环、状态唤醒和失败清理组件已测。离线期间切人和旧请求迟到的组合及当前上下文隔离归 WP02-19b。 |
| AC-CL04-01 | PASS | 分列 bootstrap/设备/认证/业务四轴。Web、Native 密码、SSO、因子、刷新、快速切换及控制出口按上表核对。Pending 无 session，SSO 第一因子仍需本地因子，未知政策关闭。`auth-focused.trx`。 |
| AC-CL04-02 | 部分 | 设备必需策略下外租户 403、当前策略/注册版本变化失效均实测。旧可选行为继续由现行 flag 决定，显式 RequiredGate 能力和可选模式下外租户归属的原义务待裁决及验证，归 WP02-19b。 |
| AC-CL04-03 | PASS | family／无 family／Native 路径、输出前后拒绝、UNKNOWN 隔离原 replacement 和健康 family 保持均实测。`auth-focused.trx`、`device-supplement.trx`。 |
| AC-CL04-04 | 部分 | 新交互开启窗口，refresh／quick-switch 不续长，共享切换禁止借用他人因子。含旧离线草稿的端到端切换仍归 WP02-19b。`auth-focused.trx`、`client-baseline.trx`。 |
| AC-CL04-05 | 部分 | SSO 初绑/JIT 和会话分开，已消费 grant/challenge、enroll 和原状态/关闭有服务端实证。旧 Native 客户端的明确 pending 能力出口及带同一业务键的完整恢复，归 WP02-19b。`auth-focused.trx`。 |

七项部分证据保留在报告，正式台账仍为 NOT_RUN。新余卡按原范围收口，原 WP02-20 前置须等待 WP02-19b 完整交付。原激活回查的信任关系、可选和 Required 设备归属，以及旧 Native pending 和当前离线草稿采用已请求 COORD 裁决。 [QUESTION](https://github.com/GTX537/CP6.AI-Handoff/blob/ff248bd3db0e5026c389428297fc2fa034a10791/handoffs/CP6-DEV-20261012/tasks/WP02-19/messages/20261119T032845Z-exec-wp02-14b-question/MESSAGE.md) · [唯一台账](../../roadmap.json)

## 复验记录与边界

Windows、.NET SDK 8.0.424、PostgreSQL 18.6、实际本地 Kestrel。使用隔离数据库、模拟租户和设备、测试签名及受控 IdP。原生客户端组件使用模拟传输。没有使用实际生产账户、真实现场设备或外部企业 IdP。所有原始验证位于 `D:/CP6-test-artifacts/WP02-19/`，外部夹具、运行设置及数据不进入 Git。

| 输出 | 本轮结果和复验入口 |
| --- | --- |
| `device-baseline.trx` | 7 项当代产品验证通过，包括暂停先提交五类写者、双实例 nonce、范围版本。另一次旧数据库迁移夹具失败见下。过滤位于 `device-filter.txt`。 |
| `device-supplement.trx` | 3 项通过。用 `acceptance.targets` 将 `WP0219DeviceAcceptance.cs` 临时加入现行 `CP6.Tests` 新构建。`WP0219_` 覆盖一次绑定、并发 CAS、跨租户和吊销后迟到刷新。原始计数只保存状态与行数。 |
| `auth-focused.trx` | 118 项通过，0 失败、0 跳过。测试类覆盖 `AuthenticationState/Policy/Entry/Continuity/SsoPostgreSqlTests`，精确表达式见 `auth-focused-filter.txt`。旧集成编译从 `d409b993c8885113903aff9fbf808e10269ac6c5` 至当前基线只有文档差异。 |
| `client-baseline.trx` | 19 项通过，0 失败、0 跳过。从现行客户端源码构建并执行 `ClientDeviceHeartbeatLoopTests`、`BearerRefreshTests`、`NativeSsoServiceTests`、`WmsTaskRecoveryTests`。 |

独立的 128 项真 PostgreSQL／认证测试及 19 项客户端组件共 147 项通过，早期 `auth-baseline.trx` 的重复成功未再次计数。旧库迁移附带探测有 3 项在准备阶段报 PostgreSQL 42703，现行审计模型向刻意降到旧迁移的 `Sys_FieldAuditLogs` 写入尚未安装的 `AuditAtUtc`。这 3 项保持失败证据。`auth-baseline.trx` 内另 1 项 PGT-017 联仓用例因本轮无对应桥资源跳过，沿其历史范围单列，未计入 118 项。执行的定向编译保留基线警告，现行产品差异为空；未执行全仓分析器、Actions、生产部署或现场升级。 [开发规则](../../../../AGENTS.md)

## 工时与收尾

本卡从 `2026-11-18T22:15:34.570710-05:00` 至候选 `2026-11-18T22:34:27.922225-05:00`，计 1133.352 秒。余卡使用原 M 卡扣减后的 13266.648 秒，累计计入 WP03-04 的候选后正式收尾 421.682 秒一次。本卡原预算 4 小时，6 小时强制拆卡点。按未确定的原契约提前拆分，缓冲无增量。主仓合并后核对远端采用与冒烟。精确任务引用、本地工作区及 DEV-1 释放以官方 `finish` 与原通信分支 DELIVERY 回读。 [单卡计划](../../planning/20261117-card-execution.md)
