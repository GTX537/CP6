# W20-03：实际目录 HTTP 宿主采用

现有身份目录 GET 端点增加严格、显式 opt-in 宿主，使用真实 C02.Services JWT 认证及产品 source verifier/authority。当前租户/令牌授权和目录读取均使用已安装 v2 的普通租户 LOGIN；启用后失败无 Owner 查询回退。18项配置边界及1条原生 PostgreSQL/实际HTTP采用链路通过，0失败/跳过。更广的负向/恢复矩阵留W20-04。

基线 `73ddf0b9374e5f09140be4280895d150749f87c5`，分支 `codex/wave20-directory-host-20261018`；W20-01/02经PR291/292合入。真实START为Handoff `c155fa0a87ff9e139ebacf8504b33e4c5f427f20` 的 `tasks/WP02-05/messages/20261019T033503Z-wave20-host-start/MESSAGE.md`。总控单独实施及作者集中审查，复用原工作目录/构建输出。

## 实现与边界

配置字段及采用合同见[Tenancy README](../../../../../CP6.Core/Persistence/Tenancy/README.md)。CrmIdentity:IdentityDirectoryTenancy不存在或只有Enabled=false时保留旧路径；未知字段、带附加设置的禁用段、无效数据库/回执/凭据/注册/时效以及与同目标其他fresh安全profile混用均拒绝。配置错误只返回固定错误，不打印密码或连接串。必须显式指定实际安装身份和结构指纹，普通LOGIN不能等于控制连接LOGIN。

注册时冻结目标、密码、catalog、reader注册/版本和region；专用singleton持有受保护可信scope、产品verifier/authority及有界连接池。真实JWT先验证签名/issuer/audience/时效，再绑定唯一规范tenant/jti、client/sub/scope和已注册读者；读取前使用普通连接核对当前租户资格、记录令牌及撤销/有效期。授权与目录均绑定安装期望、数据库身份和实时结构指纹，不调用旧Owner查询。

source时效为配置TTL（1–300秒）、注册有效期、JWT原有60秒偏移边界的最小值，返回结果前再检查上下文、时间和取消。注册失效/存储故障采用固定503；访问拒绝401/403；原游标、generation、no-store响应合同保留。控制变更必须停流、排空所有实例并审查重启，不宣称热重载、所有Owner写最终fence或既有环境切换。

只接入目录GET端点；Main其他入口仍可能使用控制连接。构造旧依赖不等于查询数据库，测试对请求DI中的Owner DbContext全部SQL命令设失败拦截器，以实际HTTP成功且零调用验证该边界。fixture仅提供隔离库、签名key、测试令牌登记和未使用浏览器依赖；没有替换产品reader、source verifier或authority，也没有声称真实OIDC发令牌端点验收。

## 实际验证及复用

证据根 `D:/CP6-local-test/wave20/w20-03/`，原始JSON/TRX/日志保留。PostgreSQL18.6/.NET8及原锁定依赖，无依赖或workflow变化。

| 运行 | 结果与范围 |
|---|---|
| red-config-20261019T034208Z | 1预期失败：旧注册忽略显式未知配置，无PG，新能力RED |
| initial-config-20261019T034718Z | 产品实现编译，原RED配置检查1通过，无PG |
| configured-host-20261019T035048Z | 19通过，0失败/跳过：18配置边界及1原生PG/实际HTTP链路 |

最终19项去重结果覆盖配置fail-closed、singleton/禁用兼容、不兼容profile，真实HTTP A→B→A读取、本租户响应、跨租户快照404、no-store、Owner查询计数0、连接租借归零；pg_stat_activity实际观测directory_a/directory_b普通角色。1个自有PG已停止，stopped.txt存在且无postmaster.pid。

复用[W20-01当前授权](../../WP02-02/directory-authorization/DELIVERY.md)、[W20-02安装与恢复](../../WP02-02/directory-installation/DELIVERY.md)及W19未变reader合同；不因提交变化重复执行。选择器 `FullyQualifiedName~IdentityDirectoryConfiguredHttpPostgresTests|FullyQualifiedName~IdentityDirectoryConfigurationTests`，新隔离CP6_PERMISSION_PG_ROOT、原PG_BIN、复用artifacts/NuGet缓存。HTTP撤销/漂移/时效/取消和原合同矩阵尚留W20-04，当前报告不提前声称通过。

## 一次作者 SPEC/QUALITY 审查

核对完整产品diff、配置解析/复制边界、认证来源与authority绑定、旧60秒偏移、普通账号当前授权及安装检查、Owner回退、错误和取消/租借释放；配置测试和真实HTTP采用证明上述基本路径。无本窗口剩余阻断；这是单作者集中审查，不冒称独立审查。共享台账/四状态同任务同步，正常PR及远端main核对后才关闭窗口。

持续目标active，W20-04继续矩阵/集成收尾。完整WP02、全Main普通账号切换、冻结完整目录/CRM、Owner最终授权门、真实DEV/业务/生产均未完成。无Actions触发/取消、新工作树/代理、既有数据库操作或部署。
