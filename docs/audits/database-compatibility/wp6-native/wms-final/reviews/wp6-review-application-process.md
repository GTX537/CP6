# WP6 应用、恢复探针与进程边界：集中审查分区

- 日期：2026-10-03；工作区：`D:\CP6\tmp\worktrees\db-compat-wp6-20261003`。
- 审查者：`/root/wp6_entry_inventory`；本报告是同一次任务级集中审查的独立分区。审查时未修改实现、未执行 .NET、数据库、原生工具或外部操作。
- 范围：`tools/CP6.DatabaseCompatibility.ApplicationProbe/**` 的源文件和依赖锁；`DatabaseCompatibilityApplication.psm1`、`DatabaseCompatibilityProcess.psm1`、`DatabaseCompatibilityHttp.psm1`、`DatabaseCompatibilityBaseline.psm1`。
- 排除：本审查者先前编写的 Lifecycle、Entries、共享 helper、fixture、文档；主 runner、Results、生产 gate 由其他审查分区负责。仅为确认 batch 语义，只读核对生产 `WfNotificationDispatchWorker.cs` 的候选列表与最后一次 SaveChanges。
- 初始结论：发现 2 项 P2 代码缺陷，均已由 root 确认需要定向修正。其余列出的场景存在实际断言，不能以静态阅读替代待完成的 native 验收。

## 必须修正的发现

### A1 / P2：SignalR 登录及协商未固定为无代理直连

- 文件：`tools/CP6.DatabaseCompatibility.ApplicationProbe/SignalRVerification.cs:133`、`:145`–`:150`。
- 触发：运行账户或继承的代理配置处理 loopback 地址时，登录 `HttpClientHandler` 使用默认 `UseProxy=true`，SignalR 的协商 handler 也未禁止代理。虽然 BaseUri 是 literal loopback，主进程也核对了监听 PID，这不能保证这些客户端请求实际直接发往该 PID。代理还可能接收登录请求或认证 cookie。
- 影响：真实 WS 两用户证明的本机直连边界依赖机器代理配置；与已经明确 `UseProxy=false` 的 readiness / HTTP fixture 不一致。
- 修正要求：登录 handler、SignalR 协商 HTTP handler 及 WebSocket transport 均显式使用直连配置；保留 cookie、实际 WebSocket、两个用户及批次断言。依赖是本地锁定的 SignalR / Http.Connections.Client 8.0.12。
- 校验：必要的本地编译由 root 串行执行；实际 WS native entry 随本次必需验收运行。此报告没有声称已出现真实代理泄漏。

### A2 / P2：启动只回传旧产物清单哈希，未重新检查运行文件

- 文件：`scripts/database-compatibility/DatabaseCompatibilityApplication.psm1:21`–`:31`、`:61`–`:71`、`:76`–`:85`。
- 触发：调用 `New-Cp6ApplicationSettings` 后，同一发布目录有文件新增、删除或覆盖，再执行重复 initializer、源 API 或恢复 API。清单只在 NewSettings 时计算；两个公开启动入口直接使用目录，却继续回传旧 `ArtifactManifestSha256`。readiness 的 Git SHA 来自同一 Settings 注入的 release 环境变量，不能补足文件身份检查。
- 影响：同一 run 可运行不同字节的应用而继续报告相同 artifact 身份，破坏本地 build once / 同产物恢复证明。
- 修正要求：每次 initializer / host 启动前，将整个实际发布文件集合（相对路径、大小、SHA-256，含新增、缺失）与最初记录清单比较；同时绑定 Settings、manifest 与私有配置的 RunId / SourceSha / 目录。失败不得启动子进程，不能只核对 manifest 文件自身哈希。
- 校验：纯离线使用临时假发布文件、禁止真实进程/DB 的桩，覆盖未改动以及新增/缺失/等长内容变更；验证两种启动入口均在调用进程之前失败。最终实际启动仍由 root 执行。

## 核对结果及明确边界

| 检查 | 源码依据 | 结论 |
| --- | --- | --- |
| 四个迁移上下文 | `ProbeContexts.cs:41`–`:65` | Core / Identity / ERP / Space 逐一读取实际已应用及待应用历史，要求 known 非空、applied 与 known 精确顺序一致、pending 为 0；SQL 的迁移 owner 复用 Core 是明确 profile 规则。 |
| 全表覆盖与行内容 | `NativeSnapshot.cs:14`–`:149`、`SnapshotCommands.cs:29`–`:71` | 原生枚举所有用户普通/分区表；独立表总数检查避免静默漏表；PG `ONLY` 避免继承重复。每列采用原生二进制并区分 null、长度，行哈希排序后聚合；恢复比较表集合/列定义/行数/行哈希。 |
| 序列与 identity | `NativeSnapshot.cs:152`–`:218` | PG 包括 definition、last_value / is_called；SQL 包括 identity seed / increment / last 和 sequence definition / current。严格恢复比较；序列不是事务快照，调用方必须在无 writer 的边界 capture / verify。 |
| 快照与完整 catalog 的分工 | `SnapshotCommands.cs:87`–`:92` | 本探针覆盖表列与行、序列、四 history；它本身不单独枚举全部索引/FK/grants。恢复 catalog / DB 权限依赖其他必需 entry 与 Lifecycle，不能把本探针单项称为全 catalog 验收。 |
| 真实 HTTP 权限 / DP cursor | `DatabaseCompatibilityHttp.psm1:151`–`:271` | 分离 cookie jar；admin、匿名 401、普通用户实际创建/改密/重登及权限 403；恢复重用先前保护 cursor，要求同用户/租户且精确第二个 asset；通知 digest 必须由正式调用方提供，公开 `NotificationIdentityChecked` 不允许略过后仍宣称完成。 |
| 实际两 WS 用户 / 后续两批 | `SignalRVerification.cs:23`–`:119` | 数据库验证两物理用户；两个客户端强制 WebSockets；目标与两个逐次 marker 均要求真实事件及持久 dispatched / attempts=1。marker 在上批完成后才写入，生产 worker 候选列表在 batch 开始捕获并在末尾 SaveChanges，故两个 marker 代表后续两批而非定时 sleep。 |
| 幂等恢复 replay | `NotificationReplay.cs:13`–`:56` | 使用生产 NotificationService 同 EventKey 且故意不同内容，要求 SaveChanges=0、精确一行、完整前后对象哈希和 worker 状态相同；不会以重写标题/重置派发状态掩盖重复。pending → dispatched 恢复领取仍须正式运行验证。 |
| 进程 / listener 所有权 | `DatabaseCompatibilityProcess.psm1:38`–`:135` | 模块私有字典保留实际 Process；公开 token/PID 无环境；超时只终止自己持有的进程树；listener 必须属于真实 PID 且只在所查端口 loopback。非 Windows listener 验证明确失败，不假装可移植已验收。 |
| 私密数据 | `ProbeHost.cs`、`DatabaseCompatibilityProcess.psm1:45`–`:93`、`DatabaseCompatibilityHttp.psm1:1`–`:2` / `:127`–`:130` | Probe 公共输出是安全断言/计数/hash/固定异常分类；进程 stderr/stdout 私有；HTTP 明确 Public/Private 分离。正式 caller 必须只序列化 Public；原始 body/cookie/密码/cursor 仅保存在 private。 |
| 真正旧 Core136 | `DatabaseCompatibilityBaseline.psm1:6`–`:7`、`:66`–`:70`、`:195`–`:260` | 固定 Git commit `993e844df180f9a110eb4a3bea2904a0ba70ba6c` 的 archive，新私有目录，源码 migration 数=136 且末项固定，锁定 restore / 当前 SDK 编译历史源码，真实旧 initializer 只对空 owned SQL 库运行；归档/锁/发布 hash 均记录并复核，无手写 seed / DOWN。 |
| 旧基线与当前升级区别 | `DatabaseCompatibilityBaseline.psm1:189`、`:283` | 报告仅证明旧初始化准备；严格 live Core136 + seed、当前 136→140 是独立 RequiredNextGate。必须由正式 matrix 实际完成，不能把旧源码编译当当前升级通过。 |

## 执行证据的适用范围

- root 已告知双库原型 full application / recovery proof 实际通过；本分区没有重新执行，不能改称新结果，也不改写旧 WP2–5 证据。
- 新实际 WebSocket 两用户、两批 marker、恢复 pending worker 及最后 replay 尚待 root 完成编译和两库 native entry；这是必需门禁的执行缺口，区别于 A1/A2 代码缺陷。
- 正式 matrix / SQL 历史升级 / 最终 cleanup、主 runner source binding、生命周期权限与原生恢复均由其他分区和 root 汇总。该报告不是 WP6 整体完成声明。

## 初始审查源码 SHA-256

以下为修改 A1/A2 之前的源码快照；行号以上述快照为准。

| 文件（工作区相对路径） | SHA-256 |
| --- | --- |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/CP6.DatabaseCompatibility.ApplicationProbe.csproj | C79D1137C3F70932066DEE8C26B7DC4570AAADDE94845CBB93521D4B1323703D |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/NativeSnapshot.cs | C8C059358643918316D68F3D24DCCE4749F05FE00B9544D2752DF5A24939B8C5 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/NotificationCommands.cs | 6BBE46AD88B7B596D97231BBEDF300F414C9E8EC18F86BC9E63C53E8E4FADAA6 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/NotificationReplay.cs | 1D5A526E86A07438992AE40985165F42E2294BF47892341F89193EF6AFE9A1D6 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/packages.lock.json | 6155B97413ED665582D8E906C5284C35BBCF75C01668821042855E95D95275B4 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/ProbeArguments.cs | 600E78E69231608DD42DCF6F502B7AAD0B61E772F35E12BD61EE93C6CC6593C2 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/ProbeContexts.cs | 549B354499659238015C5C189896A67C56B6FEC9B86B4219083D50153944B18E |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/ProbeHost.cs | A6DC5224DF83C37EED419E534366820EECAD22F2416E578CDC894635AD7843C8 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/Program.cs | 114AA7A3D8F939313B5AB6DAE6C0E67C0A69E0427B9AC3FBBA0F94AD7BD2CD37 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/README.md | ED8623A52D2DB047738AF830F9481C85C4A3059694B93A1B8BC691B45BACD279 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/SignalRVerification.cs | 0F6AAD899C779C8A4DF869FBB8B8388928E7B982698CD4F80A95A89076CF0934 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/SnapshotCommands.cs | AFB9833EFEE780CEA6C99AC6DFD145F676B41963DD371F8FA53201DD1A51D52B |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/SnapshotState.cs | FA6E4DE0D8D1801D610644C8D6F6A4BCAA2D278098CF0CAA07B22A98308257C0 |
| scripts/database-compatibility/DatabaseCompatibilityApplication.psm1 | D0A7A5D24F96F43EFB2D4FC73A9F84CB1A851D4912C4867F04FFA83E2720A8EC |
| scripts/database-compatibility/DatabaseCompatibilityProcess.psm1 | B95CDE488E14B7D4AE986458FBAE589934912DBDDA1EC08413D945C6DAF56B25 |
| scripts/database-compatibility/DatabaseCompatibilityHttp.psm1 | FE9D5FD101ABC122F221C954B25A404392FE945DCE6D27FA009364F97E78FA7E |
| scripts/database-compatibility/DatabaseCompatibilityBaseline.psm1 | 69D6033531FBF7E47E33789741128E37D3B7E90B2724E1380F1E2069800B8E8D |

## Root 授权后的定向修复记录

初始报告已先落盘；随后 root 授权审查者仅修 A1/A2 及相应纯离线检查。以下为同次审查问题的局部修复，不另起全仓审查，不替换上方原始发现和源码快照。

- A1：登录 `HttpClientHandler.UseProxy=false`；Hub 协商 `HttpMessageHandlerFactory` 必须得到实际 HttpClientHandler 并禁止代理/重定向；`HttpConnectionOptions.Proxy` 和 `WebSocketConfiguration` 使用空 `WebProxy`，不继承宿主代理。API 与本地锁定 8.0.12 XML 文档核对；纯内存检查空 WebProxy 对 loopback / remote URI 的 `IsBypassed` 均为 true，没有网络请求。
- A2：两个公开启动入口在 owner/native/listener/process 之前执行 `Assert-Cp6ApplicationArtifact`。绑定 Context、私有 Settings、manifest 的 RunId / SourceSha / 目录，并将实际所有文件（包括 hidden，相对路径、长度、SHA-256）与原始清单比较；manifest 文件自身哈希同时保留。异常统一固定安全码。
- 新 `scripts/database-compatibility/Test-DatabaseCompatibilityApplication.ps1` 仅构造临时假发布文件，并在模块内部替换 owner / connection / process 函数。对 init 和 Host 各覆盖未改动、新文件、缺文件、等长内容变化、目录变化、manifest 变化、manifest identity 变化、私有目录变化；负例必须在 owner 桩之前失败，正例只到桩就停止，不开 socket。
- 实際离线 RED：16 项，2 项对照通过、14 项变更拦截失败，process exit 1。文件：`D:\CP6\tmp\wp6-application-artifact-offline-red.json`。
- 实际离线 GREEN：16 / 16 通过，process exit 0。文件：`D:\CP6\tmp\wp6-application-artifact-offline-green.json`。
- 修改后的两个 PowerShell 文件 AST 错误为 0；定向 `git diff --check` 通过。
- Application / SignalR 实现已冻结并通知 root。SignalR 的 .NET 编译及新的双库实际 WS / pending-worker 恢复 entry 仍由 root 串行完成；本审查者没有执行它们，也不把离线桩当作这些场景通过。

修复冻结源码：

| 文件 | SHA-256 |
| --- | --- |
| scripts/database-compatibility/DatabaseCompatibilityApplication.psm1 | EE12DD7410221E66A3FAE553631E8603A80323B43630D849384DCF6B51817350 |
| scripts/database-compatibility/Test-DatabaseCompatibilityApplication.ps1 | 5A6E73416A5535DA6F143701E0B1E8BC70457F0F6A4BF00ABA48AECF46344A25 |
| tools/CP6.DatabaseCompatibility.ApplicationProbe/SignalRVerification.cs | 0E0A16B900C48CD751E0B368248ECD202648D48A8576C58D4E05FDCCFA2964B4 |

离线 RED JSON SHA-256：`4536059771D2F7119F79FEC59A9898B0E548BD6B15CCC52CD0CFEF6179C7B55B`；GREEN JSON SHA-256：`FE3984DB2E2C6DD663E9F3001A6F069B23623DB28B97823DDC1C4BABD0137144`。
