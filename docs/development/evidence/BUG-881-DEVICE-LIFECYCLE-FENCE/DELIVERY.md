# W440 · BUG-881-DEVICE-LIFECYCLE-FENCE

2026-11-10 UTC。[Issue #881](https://github.com/GTX537/CP6/issues/881) 对应原 WP02-03 / PGT-017 的设备生命周期现存写者。COORD 单人复用 DEV-2，分支 `codex/bug-881-device-lifecycle-fence`，基线 `f7c19d3e268ac3652ef999b422bb9b16259c1c47`。本窗不核销完整 SYS-CLIENT-01、新版 operation DTO、前端或发布策略。

## 根因和实际改动

管理票据、设备更新/禁用和快捷 PIN 原依赖早期 HTTP 授权；匿名票据激活与签名心跳也没有在最终事务决定时核对当前身份。真实暂停先提交后，五类请求原版均返回 200 并改变对应原行。心跳 nonce 使用宿主缓存先查后写，两个实际独立内存缓存宿主对同一签名请求均返回 200。

三类管理入口复用现有 IAM 管理 Owner 的 PostgreSQL 共同认证根和当前权限，传递同一次构建的实际 UserPermissionContext，核对 device-manage 与源/目的库区范围。新增的内部回调保留旧 IAM 公开回调的 actor、事务及错误语义；未给已有 IAM 操作增加新资格层。当前用户名用于实际审计字段。授权动作可以合法使自己的设备或 PIN 凭据版本失效，收尾复用既有 TenantManagementSessionV1 的外部 logout/JWT 时钟协议，避免再次要求刚被合法撤销的旧版本有效。

匿名激活以实际票据身份在独立共同根事务中读取当前租户、一次消费与注册；完整解析现有允许的 RSA/ECDSA 公钥，在最后负责边界再次核对票据及租户寿命。租户 ExpireDate 为 null 的既有无限期合同保留。普通心跳核对当前真实会话、当前设备及签名，不新增管理员资格；nonce SHA-256 在 `cp6_authentication_v1.device_heartbeat_nonces` 的租户/设备唯一键裁决，与成功观察同事务。清理和十分钟保留使用 PostgreSQL clock_timestamp，失败不会消耗 nonce 或刷新 LastSeenAt。遥测 CurrentTaskNo 仍只作声明，不产生任务或业务完成回执。

更新保留原 CAS、字段白名单、原 400/404/409 错误，禁用与所有实际 refresh 撤销同事务，Shared→Personal 清除共享 full-auth 资格。Owner 缺失、数据库不可用和提交未知分别按明确 503 协议返回，不静默降级。历史非 PostgreSQL 路径保留其范围，未新增 SQL Server 实现或验收。

集中检查又实际发现设备 WarehouseCd/AreaCd 未纳入既有原生 track_device 版本比较：合法换仓后旧客户端仍返回 200，registration revision 不变。前向迁移保留原触发器对身份、状态、公钥、模式、TRUNCATE、共享窗口等行为，补充两个范围字段。安装时更新已有 device_versions，使无法证明是否经历旧范围变更的原生会话一次重新登录；设备业务行、租户和用户身份均不改写。该迁移未应用到现有开发或生产环境。

## 实际验证

明确模拟租户、普通用户、权限、设备、公钥、库区和票据，使用实际 Main Kestrel HTTP 与 PostgreSQL 18.6 隔离集群。最终本地 10 项执行、10 通过、0 跳过，启动 11 个真实宿主；其中设备场景 8 项，另有既有 IAM 权限等待和 role CAS/CSRF/恢复兼容回归 2 项。

| 实际场景 | 原故障 | 最终结果 |
| --- | --- | --- |
| 票据、更新、PIN、匿名激活、心跳的暂停先提交 | 五类均 200 并写入 | 四类认证请求 401，匿名激活 400；实际等待共同根且七类原行保持 |
| 两宿主顺序重放相同签名请求 | 第二宿主也 200 | 首次 200、重放 409 WM-DEVICE-REPLAY，无新增写入 |
| 两宿主同时进入原生根等待后释放 | 原版仅顺序跨宿主已复现，未冒充并发结果 | 原生等待链证明两请求均到达同一控制根；结果恰为 200/409，仅一条新 nonce |
| 合法范围变更后的旧原生会话 | 200、版本不变 | 401、设备 registration revision 改变 |
| 含既有设备/原生会话的前向升级 | 升级前实际 profile 200 | 设备 JSONB 原行保持，新增 nonce 表；旧 profile 401，新登录 profile 200 |

五类暂停场景均先完成正常业务，再由原生目标租户锁控制真实暂停提交与在途写入顺序。记录实际 backend/blocker、共同根等待和提交时间，七类 JSONB 原行逐字节比较包括设备、激活票据、用户、refresh、device_versions、shared_windows 和 nonce。暂停后再次请求也拒绝，合法 reactivate 后重新登录继续业务。

正常路径同时验证 Windows/Shared 规范化与票据有效期夹取、仅保存 token hash 和实际创建者；自禁用成功、refresh 撤销、旧客户端失效，重新启用不恢复旧窗口；自改 PIN 成功、实际 hash 可验证、旧会话失效并可重新认证。覆盖原 CAS 409、源/目的范围拒绝和不存在用户 404。匿名激活覆盖一次消费、重复注册无孤儿、错租户、错平台、过期票据、过期租户、无效公钥、有限/无限期合法租户和 RSA/ECDSA 实际签名心跳。普通角色移除 device-manage 后仍能合法心跳；错误签名、旧时间戳和重放不改变成功观察，声明任务不产生任务/回执。

设备禁用故障在实际 Sys_RefreshTokens 撤销更新触发器抛出，返回 503 DEVICE_AUTHORITY_UNAVAILABLE，七类原行保持。PostgreSQL 日志确认故障确实执行，测试 finally 准确移除该触发器/函数。已有数据升级使用本次隔离集群独有的随机数据库，从 `20261110020400_IamMembershipRoleTenantReferenceV1` 迁移到 `20261110080000_DeviceHeartbeatNonceV1`，宿主在升级前后保持真实运行；私有 PGDATA 保留测试数据库证据，集群和所有宿主均已停止。

## 证据与集中审查

原始证据 `D:/CP6-local-test/CP6-72H-20261109/W440-01`。最终原版为 `red-hosted-02/actual.json`（6 项预期失败真实复现），范围版本另有 `scope-red-01/actual.json`（1 项预期失败）。最终绿色 `green-hosted-03/actual.json`，SHA-256 `f80e37bcafe8e3c3ee0fe4ec7c5cbf5e49ae82a53cabe60c11b0199e82f335d2`；TRX、真实宿主日志、原生等待链、升级前后比较及 PostgreSQL 日志在同目录。最终测试源码 SHA-256 `92618b8e9b2d7552c3d02c69910479b3de4f5bb2bdf760af28e3e77696b9f92c`；初次红色测试仅覆盖当时六个故障，后续边界、范围版本和升级断言按各次实际源码分别留存。

最初红色匿名请求缺少既有 CSRF 双提交，返回 403，补齐测试运输后才记为有效业务复现。最初绿色编译使用了错误异常构造器，随后改为既有可传递 404 的异常。绿色第一轮误把 null 租户寿命视为失效，五类设备及 nonce 场景失败，两个既有 IAM 场景通过；修正 null 无限期后五类生命周期通过。第二轮并发屏障只统计直接 blocker，忽略真实 PostgreSQL 排队等待链而超时；改为递归原生阻塞路径，保持原八秒期限，没有以加时或跳过伪装成功。最终全部十项按最终输入新执行，前述失败均保留。

正常 SDK 串行编译相关 Core、PostgreSQL、Web API、Tests，锁定依赖与无关产品复用已有成功构建。发现原生 scope 问题后只重编 PostgreSQL 与 Tests；最后补充票据边界仅重编 Tests。Core 完整分析器延续前窗已记录的内存限制，使用 RunAnalyzersDuringBuild=false；完整分析器未运行。没有全仓回归、SQL Server 新验收、GitHub Actions 或部署。

COORD 一次任务集中审查完整 diff：共同根与实际作用域 Context、旧 IAM 回调兼容、当前权限和范围、合法自失效、anonymous/普通用户证明、nonce 数据库时钟与唯一约束、公钥输入边界、设备身份版本和既有数据升级、原错误/CAS、故障原子性、提交未知、全部实际失败/通过来源、工作流和项目状态。最终 diff 与实际构建输入匹配。未验证混合版本宿主；后续部署采用需先应用迁移并统一升级调用这些入口的实例。

## 工时、采用与真实余项

start 为 2026-11-10 07:53:09 UTC，开工估计 60–100 分钟，依据前窗实际 26分36秒，加上五类身份边界、原生迁移、自失效及双宿主。新增发现的范围版本问题及已有数据升级保留实际执行时间，完整生命周期按正式 start/finish 校准，不将席位墙钟或子进程时间叠加到原生目标时钟。用户确认的总预算仍为 48 小时/172800 秒，下个四小时校准为原生 115200 秒。

原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。当前仅修复现有设备写者，PGT-017 全体相关写者采用及完整业务验收仍 NOT_RUN，序列号等入口需继续。新版 SYS-CLIENT-01 operation DTO、UI、发布策略、bootstrap/in-flight task 采用及 PGT-021/022 同源恢复验收不由本修复冒充完成。

本窗同步 W439 / #880 的远端采用、准确任务分支清理、DEV-2 释放及 1596.34824 秒工时校准。当前任务正常 PR 合入后核对远端 main，最终分支处置、固定工作区状态和席位释放以正式 finish.json 为准；下一窗口核销此采用事实。
