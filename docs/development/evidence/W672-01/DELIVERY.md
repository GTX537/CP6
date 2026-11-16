# W672-01 Main 登录锁定

任务从 Main `a16518e321c31e8cab3577d1a3b147d6c495721a` 登记，串行采用 W671 的 `3974694ccb47ef4e46e2abc24b661e82cf94070d`。产品候选 `317a56789720212ee8a8f80e193b4e1a7af811a7`。组合 `65ec830e82f382e9af6aa36f9b03d3bd29cf6f22` 与该产品候选的 Core、WebApi 和 PostgreSQL 文件逐字节一致，增加 SSO 与真实 CRM Owner 验证夹具。

## 实现

- 普通 Web 和 native 密码失败以及 Web/native 共享的登录二因子失败，在 PostgreSQL Main 认证根内重读所属用户的原计数、最后失败和锁定时刻。保留各来源原错误和审计，以及实际成功登录的会话证据和成功清零。
- 触及已登记 CRM 的锁定预先持久保存原账户、当前依赖、原绝对期限和该用户的精确 C02 计划，再打开记录中的物理 Store。二因子以原 `CompletionRef` 和 lease 绑定已接受失败。当前失败计数、原审计、该 lease 的释放、C02/Outbox 与不可变 Main 回执共同确认。
- 合法的同账户密码或经原 IdP 流程验证的 SSO 主证明可以恢复该原件。密码对外维持原通用错误，真实已锁随后按原错误处理。已接受且确定从未发远端封存的计划自然到期时，只保存原失败和期限回执，再让新的正常登录按当前状态执行。可能已远端提交的计划继续用原 C02/回执收敛。
- PostgreSQL 正向迁移新增两种原计划并接原 challenge 关联。在原 Close 入口保留已接受失败。未登记 CRM 的原账户计数走本库事务。

## 实际验证

全部业务数据为隔离模拟。Main 常规入口使用修复后的真实 Kestrel 与 PostgreSQL，SSO 用仓库签名 IdP 夹具经过实际 `SsoService` 和 `AuthenticationEntryServiceV1`，与真实 Main/CRM PostgreSQL 联验。候选 runner 和同次程序集 SHA-256 位于 `run-lockout.ps1` 和各目录 `tested-source.json`。

| 证据目录 | 实际观察 |
| --- | --- |
| `candidate-web-password`、`final-native-password` | 常规密码失败各一次跨阈值，单次计数和账户 C02，Web/native 原审计及 `E-SEC-001`，随后真实已锁 `E-SEC-002`。 |
| `candidate-success-first` | 正常登录持 Main 根等待实际用户行，后来失败也等待根。原会话和认证证据一次，成功清零后发生一次不足阈值的失败，无新 C02。 |
| `final-lockout-first` | CRM 原生封存 SQL 等待真实行锁期间，后来正确密码等待 Main 根。完成后锁定一次、原绝对期限和回执、后到 `E-SEC-002`。 |
| `final-native-verify`、`final-web-enroll`、`final-native-enroll` | 由正常密码主证明产生相应原 challenge。真实失败处理保持原 `E-SEC-011` 和结构化审计，原 lease 清理一次，无新会话或 TOTP 消费。 |
| `final-cold-web-verify` | 首次封存连接确实被拒。保留原完成记录与已接受 lease，未提交后段效果。重启后改变宿主锁定时长，合法同主体登录恢复原操作，最终 C02 hash 和绝对期限取初始接受值。 |
| `final-main-ack` | 实际 CRM SQL 等待期间精确安排丢弃一条 Main COMMIT ACK，随后按原回执读回。原账户、输出拒绝和最终 C02 同一次。 |
| `final-expired` | 原一分钟期限真实经过。恢复仍保留原期限的不可变回执，不再次尝试拒接的 CRM，正常登录、成功审计和会话证据一次，C02/Outbox 不增加。测试运行 73.54 秒。 |
| `combined-sso-recovery` | 首次真实 CRM 连接拒绝 28000。第二份实际验签的 SSO 主证明恢复同一原件，计数 5 与来源 hash 确认一次、原 lease 回到 pending。新证明得到原 `AUTHENTICATION_DENIED`，未产生新挑战或会话。 |
| `login-component` | 密码、滑动窗口、成功清零与二因子控制器组件回归 13/13，零跳过。 |

最早一次锁定先夹具在第二条 CRM 观察连接初始化报 SCRAM 无密码。Npgsql 打开连接后默认不再返回密码，修复使用原夹具连接配置，原日志保留在 `candidate-lockout-first/main.trx`。首次独立 SSO 夹具编译缺少 Wms 命名空间，保留 `final-sso-recovery/build.log`，组合后相关编译零错误和实证已通过。两个初始 Web/Main 结果使用前一相同密码路径，组合加入的是 SSO 独立入口和测试。

## CRM 受理的真实两序

组合 `65ec830e82f382e9af6aa36f9b03d3bd29cf6f22` 使用同一候选程序集和 W649 冻结的 CRM `intake:reject` Owner。测试以同一个合成账户的正常登录和真实 CRM Grant 开始。两个目录各有 Main 和 CRM TRX、独立写集与停止记录。

- `final-crm-owner-first` 的 CRM Owner 已被合法准入并持原生事务时，Main 封存 backend 6116 真实等待。CRM 先提交原决定及 Outbox，Main 随后保持该效果并提交一次原锁定。
- `final-crm-seal-first` 中 Main 先完成原封存，已经启动且持签名上下文的后到 CRM 请求返回 403，原 CRM 写集保持。
- 两序 Main 回 `400 / E-SEC-001`，账户计数从 1 到 2，user 来源从 4 到 5，受影响 Grant 撤销与 token 来源从 0 到 1。每次增两条 C02 Outbox 并确认唯一原回执。Main 2/2、CRM 2/2，零跳过，57254–57256 监听与任务进程均已停止。

## 本地制品与交接

`combined-web-verify` 的普通 Web 校验失败保持原 `E-SEC-011`、结构化审计和原 lease。累计 14 个相关候选场景通过，组件 13/13，实际各组零跳过。环境或夹具首轮失败单列在上文。

`publish/result.json` 绑定干净组合 `65ec830e82f382e9af6aa36f9b03d3bd29cf6f22`。本地制品 310 个文件，`publish/package-sha256.json` 的 SHA-256 为 `9b3e95cea801b2b86fcc495fc2ea60b0983da4cc7c4555cb741fa7bf2770606e`，三份产品程序集已另行复算一致。PostgreSQL 初始化退出 0 且核对 W671 QuickPIN 和 W672 lockout 约束已采用。真实发布进程的 `/health/live` 为 200，匿名 `/api/auth/profile` 为 401。构建有既有 `System.Security.Cryptography.Xml` 依赖的 NU1903 警告，本任务源码没有依赖变动。

最终复核 57240–57259 无监听，W672 的 PostgreSQL、测试、Python 与应用进程均已停止。仅保留本地 `codex/w672-01` 提交，DEV-1 工作树干净。席位仍由协调窗口登记，远端集成与释放由协调端执行。

[Main 受测源码](https://github.com/GTX537/CP6/tree/65ec830e82f382e9af6aa36f9b03d3bd29cf6f22) · [CRM 先提交](D:/CP6-test-artifacts/W672-01/final-crm-owner-first/result.json) · [封存先提交](D:/CP6-test-artifacts/W672-01/final-crm-seal-first/result.json) · [本地发布](D:/CP6-test-artifacts/W672-01/publish/result.json)
