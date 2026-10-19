# W24-01 / BUG315 当前平台调用人最终权限门

> 已集成：[PR316](https://github.com/GTX537/CP6/pull/316)合入远端main `5a44d92eeffad18ee209a8c6a28c5707295ce6ac`，源 `0c4c6ecc5990a27a47db7ae5cfc3bdd431084743`按范围核对一致；#315Closed，Handoff `50e20a5ddf07f43b630090a3d5a4d3b8071133da`已回读。下文候选措辞为原时点记录。

## 结论

基线 `d9abec3d8e3797f3ffeb9c42adb29af947c4591e`，分支 `codex/bug-315-platform-current-actor-20261019`。[P0 #315](https://github.com/GTX537/CP6/issues/315)已完成本地修正；**Ready to merge: Yes**，一次作者集中SPEC/QUALITY无阻断，非独立审查。66项有效相关验证通过（38原生PG/HTTP、28单测），0失败/跳过。待正常PR合并及远端核对后关闭Issue。

## 原问题与修正

原生复现5项RED：实际请求通过平台filter后暂停在服务根获取之前；另一连接撤权、停用或删除调用人并提交，原请求仍HTTP200并授予target。正确actor配其他tenant的JWT，以及无认证主体的直接PG服务调用也未被拒绝。不是把静态推测当复现。

新增共用PlatformAdminAuthorization：模拟标识优先E-SEC-034；仅一个已认证身份，subject/sub和tenant必须有效且一致，不拼接另一身份的权威claims；必须平台claim=true，拒绝token_use半登录/未知用途及受限改密claim。支持无Name及一致GUID别名。DB读取当前actor必须仍在同一可信tenant且Enable/IsPlatformAdmin有效；角色1不替代平台权。

入口RequirePlatformAdmin复用此检查。**PostgreSQL Grant/Revoke取得W23管理员root后再次检查**，AsNoTracking读取当前actor并FOR SHARE锁定其行，持有到原效果/必需审计事务提交或回滚；actor行锁也防直接TenantId修改改变提交时绑定。先撤权则后请求拒绝；合法操作先持有根则允许先提交，撤权在之后生效。目标仍可跨租户；必要安全审计使用已核实的持久actor Id/Name，HTTP鉴权拒绝按403映射，末名E-SEC-037仍保留400。

W23根、迁移、保存点/回滚/UNKNOWN处理均复用，没有新增迁移或SQL Server实现。PG服务调用必须传真实认证HttpContext；不能把无主体调用隐式当系统授权。源树产品消费者只有已注册PlatformAdminController；受影响旧PG服务测试现在明确提供合法fixture主体，未加绕过开关。两项新行锁测试覆盖直接绑定更新顺序和取消后释放根锁。

## 实际验证与复用

原日志、TRX及结构化结果统一在 `D:/CP6-local-test/wave24/platform-current-actor`；四次结果保留，不生成重复报告。

| 运行前缀 | 实际结果/范围 |
|---|---|
| red-current-actor-20261019T082155Z | 5失败/0跳过，五项真实缺陷复现 |
| green-current-actor-20261019T082426Z | 原步骤5通过/0跳过 |
| actor-boundaries-20261019T082723Z | 63通过/0跳过：新平台actor16原生、W23受影响19原生、过滤器20及既有服务8单测 |
| actor-row-and-shared-filter-20261019T082940Z | 3通过/0跳过：新actor行锁2原生、共享过滤器实际TenantController1原生 |

后两份构成66个去重有效结果，原步骤不重复累加。覆盖授予/撤销分别遭遇调用人撤权/停用/删除，双方先获得根的顺序与commit/rollback，当前actor旧EF跟踪、调用方保存点保留既有工作、RR/Serializable40001、取消真实PG行锁等待、无认证主体、合法跨租户与真实审计。W23末名/审计失败/取消/前向迁移等受影响19项在新服务输入下重新执行；不是无条件全仓回归。数据库锁等待检查本实例pg_stat_activity，不用固定sleep推断成功。

真实HTTP采用产品JWT验证/平台过滤器/Tenant/Csrf/业务异常与实际控制器，PG18自有实例；不是完整Program或真实浏览器/DEV验收。Fixture的refresh/2FA依赖仍strict mock且本范围不调用。合法跨tenant授撤和共用filter的租户创建/拒绝/审计失败均验证。其他平台业务控制器只采用共用主体过滤器，未逐个做业务验收。新helper最终验证后只有注释和额外测试变动，既有成功结果按相同产品输入复用。

## 一次作者集中SPEC/QUALITY

检查完整产品/测试diff及当前main：三门顺序、主体单身份/tenant与持久actor绑定、根→actor行锁→目标更新顺序、无旧跟踪授权、持锁到同一事务完成、审计actor真实性、保存点及未知结果沿原规则、无新自动重试、真实HTTP403与E-SEC-037兼容。只接受固定有效证据，RED保持失败事实。没有敏感值、机器连接串、调试产品钩子、依赖/项目/工作流改动；测试barrier仅在fixture注入。

行锁及旧事务语义参考[PostgreSQL18 explicit locking](https://www.postgresql.org/docs/18/explicit-locking.html)与[transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html)，并以本机原生测试核实。长调用方事务会持锁；外部写者相反锁序可能被PG判为死锁，失败不自动重放，未做吞吐压测。测试构建仍打印已有依赖漏洞和既有空引用警告，未把它们称为本任务修复或新增扫描通过。

## 范围、资源与后续

只在自有迁移fixture执行，既有DEV/生产未操作；部署前仍需W23既有管理员保护迁移及适用数据库权限。普通生产写LOGIN全采用未验证，Owner/超级用户禁用trigger等管理绕过不在应用边界中。

本次是当前平台权限和主体绑定门，**不是完整认证最终门**：AuthSessionVersion、refresh/session谱系、当前2FA/SSO/restricted政策、其他平台写入口事务门、全部IAM普通权限/撤权轴、CRM投影/Owner业务最终门仍需后续。旧有效token遇凭据或策略改变的完整拒绝规则未在本次实现；247运行NOT_RUN及完整WP02完成状态不提升。

4个自有PG根stopped且无postmaster.pid，HTTP/testhost释放。D余约44.01GiB；原目录/build/NuGet缓存复用，零新worktree/代理/Actions。原7项用户资料与全部失败证据保留，无删除、既有库操作或部署。
