# W20-02：目录显式安装、只读检查与漂移拒绝

v2目录组件现在有显式控制入口及真实CLI：绑定目标数据库身份，首次安装生成受保护回执；只读inspect核对实际结构/权限/映射，runtime可使用固定回执检查后再读。真实PG/CLI两条完整生命周期流程通过，另有四个旧CLI入口无数据库识别冒烟；候选待正常PR及远端核对。实际HTTP采用仍留W20-03。

基线 `1b03afcb8ba9b86ea6a9ccc8d45ad5392ba71364`，分支 `codex/wave20-directory-installation-20261018`；W20-01已经PR291合入。一次真实START为Handoff `65961b0f8bbf1685fbac07fc93817863d240ad44` 的 `tasks/WP02-02/messages/20261019T030942Z-wave20-installation-start/MESSAGE.md`。总控单独实施和作者审查，复用原目录/构建输出。

## 合同与实现

完整调用/恢复说明见[Tenancy README](../../../../../CP6.Core/Persistence/Tenancy/README.md)。新增`install-identity-directory-v2 --stdin`、`inspect-identity-directory-v2 --stdin`，严格单个有界JSON、未知/重复字段拒绝；密码只来自输入，不输出连接串/提供者DETAIL或内部异常。

安装只针对已迁移、预先建立普通租户LOGIN的明确fresh安全布局，不创建Core库或凭据，不改历史迁移，不采用v1/未知profile、不自动重试升级。实际endpoint/database/OID/cluster system_identifier与superuser数据库Owner先校验；控制事务固定pg_catalog search_path，复用目录advisory锁和W20-01 v2安装。SourceRevision是40位源码追踪标签，由操作者提供，不是全Core源码等价证明。

新增独立control-owned `cp6_identity_directory_v2.installation`，存规范请求、身份、版本及结构指纹。复用原catalog/有效系统ACL指纹，并加入完整受保护login映射和回执非指纹字段；正常业务行、sequence计数和统计排除，旧service-token seal完全保留。runtime只增加必要非敏感回执列及两个固定SECURITY DEFINER只读函数（cluster identifier/fingerprint）的执行权；函数固定pg_catalog，无用户SQL/标识符输入。不开放canonical请求、控制映射、Owner连接或写权限。

inspect采用READ ONLY / Repeatable Read事务，先确认receipt为control-owned普通无RLS/rule/trigger且列类型匹配的表，再读规范请求、映射和真实指纹。ABSENT仅指已确认目标中回执schema不存在，不证明剩余布局可安装。VERIFIED才可采用；错误是REJECTED/UNAVAILABLE，不自动修复或reseal。

安装确认commit返回INSTALLED；提交前确认rollback返回NOT_APPLIED；commit/rollback/输出不确定返回UNKNOWN（CLI exit4），必须保留原输入并inspect，不重放安装。重复安装即使输入相同也拒绝，不能把历史成功当成幂等恢复。原其他CLI合同保留。

`IdentityDirectoryDatabaseIdentity`由显式安装/检查返回、由宿主保护冻结；授权reader和目录reader在固定LOGIN映射之后核对期望endpoint/database/cluster/installation和stored/live指纹，失败保持固定503且无Owner回退。可选内部接缝保留原v1/v2组件兼容，正式宿主必须提供期望回执，当前尚未自动注册。

这不是签名或防御可替换全部control对象/宿主配置的Owner，也不是对任意并发特权DDL的瞬时fence。可信控制变更须停流/排空并按独立升级流程处理；全Core实际负载下的catalog扫描成本、既有环境迁移/自动恢复/生产推广未验收。

## 实际验证

PostgreSQL18.6 / .NET8及原锁定依赖。证据根 `D:/CP6-local-test/wave20/w20-02/`；五个自有PG均停止，stopped.txt存在、无postmaster.pid，原日志/JSON/TRX及失败根保留。

| 运行 | 实际结果 |
|---|---|
| red-cli-inspect-20261019T031434Z | 1失败/0跳过：旧CLI对新命令返回exit2，新增能力RED |
| install-lifecycle-20261019T032040Z | 1完整生命周期通过：真实CLI安装/检查、坏输入/目标身份、取消回滚、重复拒绝、普通runtime身份与业务行不影响seal、RLS/角色/回执/映射/ACL漂移拒绝 |
| lost-commit-recovery-20261019T032246Z | 1完整恢复流程通过：真实commit后故障seam丢失回执，UNKNOWN，随后只读VERIFIED，重复安装拒绝 |
| final-install-lifecycle-20261019T032441Z | 固定控制search_path后，受影响生命周期复查1通过 |
| final-lost-commit-20261019T032559Z | 同项补强后，受影响恢复流程复查1通过，no-build |

最终为两条独立生命周期流程去重通过、0剩余失败/跳过，不把每个断言、CLI调用或重复执行计成独立用例。原生测试分别使用新fixture根/串行端口55435；禁止两个collection同根启动。

CLI首次构建缺少该工具在复用输出中的assets（NETSDK1004），随后仅对该工具执行locked/no-dependencies restore并沿用原NuGet缓存；未修改依赖。实现构建出现CS0136局部result重名，重命名后构建成功；这些准备失败保留在 `C:/Users/tt/CP6-planning-reports/20261017-foundation/wave20-install-cli-*.log`，没有冒充PG失败或成功。最终CLI编译通过，已有依赖/分析告警保持。`wave20-install-legacy-cli.json`记录provision、旧revocation、旧service-token、旧tenant-control四个入口对空JSON正确拒绝，未连接数据库；这不是四条旧业务验收。

生命周期中使用实际Core/IdentityPriority迁移及Bootstrap，不是手工仿制表。普通runtime验证当前授权和目录读取、错误fingerprint拒绝、不可读canonical/映射及不可改回执；业务token登记不使指纹失效。逐项施加并复原RLS禁用、BYPASSRLS、回执修改、login映射变化，检查及两类读取均拒绝；末项额外列授权保留在最终停止的测试库，inspect确实没有修复或重封。恢复流程使用内部确定性AfterCommit故障seam，不声称已完成真实网络断线演练。

复用[W20-01](../directory-authorization/DELIVERY.md)七项PG的权限/取消/资格/版本语义及W19目录合同；未变输入不重跑。新增两个生命周期选择器分别为`FullyQualifiedName~IdentityDirectoryInstallationPostgresTests`、`FullyQualifiedName~IdentityDirectoryInstallationRecoveryPostgresTests`；设置CP6_PERMISSION_PG_BIN及全新permission-consumption-*根，CP6_DIRECTORY_CLI指向已构建工具apphost，沿原artifacts与NuGet路径。

## 一次作者集中审查

检查完整diff和CLI共用错误路径、SQL参数/标识符、控制身份、receipt对象可信性、指纹覆盖与原seal兼容、固定SECURITY DEFINER执行面、回滚/UNKNOWN、取消、普通连接检查与剩余威胁边界。补固定事务search_path后两项相关复查通过，无剩余阻断；不是独立审查。共享状态同窗口同步，正常PR/远端核对后才关闭窗口。

持续目标active，后续为W20-03实际HTTP可信配置及W20-04串联收尾。完整WP02/全Main切换、冻结完整目录/CRM、Owner最终门、真实DEV/业务/生产仍未完成。无Actions触发/取消、分支保护或旧workflow变化、新工作树/代理、既有数据库操作或生产部署。
