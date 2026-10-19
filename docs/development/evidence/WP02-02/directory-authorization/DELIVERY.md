# W20-01：目录当前授权的普通PostgreSQL身份

新增独立v2只读目录profile和当前授权reader。普通租户LOGIN只读取本租户资格、服务令牌必要列及原目录；不再要求这个内部读取组件依赖Owner连接。实际HTTP采用留W20-03，不能把组件验证当成当前Program已切换。相关原生PG去重7项通过、0剩余失败/跳过；候选待正常PR/远端核对。

基线 `ac8a85273489218339bdcff4307d2b465191d7bf`，分支 `codex/wave20-directory-authorization-20261018`。规划PR290已合入；一次真实START为Handoff `48a70e4f842ebe192c7eb86fdaac366e35412b2f` 的 `tasks/WP02-02/messages/20261019T023639Z-wave20-start/MESSAGE.md`。当前总控单独实施与作者审查，复用原工作目录。

## 行为与边界

`IdentityDirectoryAuthorizationV2` 的model/migration为 `identity-directory-authorized-read-v2` / `identity-directory-authorized-read-rls-v2`，在调用者事务内先执行未改动的v1 fresh-layout验证和三表隔离，再校验并锁住两个实际授权对象：

| 对象 | 租户键 | 新增SELECT列 |
|---|---|---|
| public.Sys_Tenants | Id | Id、Enable、ExpireDate |
| crm_identity.ServiceToken | TenantId | Issuer、Jti、ClientId、TenantId、ExpiresAtUtc、RevokedAtUtc |

额外对象必须由当前control拥有、无既有RLS/policy/rewrite，所消费列类型/空值/主键符合实际PG模型。两表ENABLE/FORCE RLS，runtime policy使用受保护session_user OID/名称到TenantId的唯一映射；control保留显式ALL policy。仅列级SELECT，不授整表SELECT、写入、DDL、grant option或其他表/函数权利。TenantCode、备注、RowVersion等额外列不开放；不改变已有数据、文本/时间表示、键、触发器或历史迁移。

v2仅首次安装，不是v1升级入口；已经安装v1、v2或其他目录均拒绝自动采用。调用者必须整体rollback失败，组件不commit或retry。正式安装manifest、身份绑定、inspect和漂移检查留W20-02；profile字符串本身不证明实际安装。

`TenantIdentityDirectoryAuthorizationReader`只在当前可信scope和v2普通连接中工作，先验证实际LOGIN映射并用事务共享锁固定它，再以单条SQL检查租户Enable/ExpireDate及指定issuer/client/jti/tenant的当前未撤销令牌。到期沿既有本机wall-clock语义，令牌沿既有60秒偏移且严格大于边界；没有缓存或Owner回退。未知scope、错tenant/profile、权限缺失统一存储不可用；取消保留取消语义，归还lease。它是内部host组件，调用者仍须先认证并绑定issuer/client/jti；不替代JWT/服务注册校验，不承诺检查之后瞬时撤权或Owner最终写入fence。

现有`TenantIdentityDirectoryReader`仅新增明确v2 model/migration配对，v1继续有效；不是任意版本通配。测试夹具增加受控安装hook以复用真实迁移、bootstrap和同一隔离数据库构造，不新增数据库副本目录或生产配置。

## 实际验证与复用

PostgreSQL18.6 / .NET8，实际Core与IdentityPriority迁移、Bootstrap、令牌登记及撤销服务；A/B为合成测试租户。证据根 `D:/CP6-local-test/wave20/w20-01/`，原log/JSON/TRX和失败根保留：

| 运行 | 结果 |
|---|---|
| red-minimal-auth-20261019T023725Z | 1失败/0跳过：v1普通账号读Sys_Tenants得到真实42501，证明缺失能力；这是新增功能RED，不将正确v1拒绝登记为BUG |
| green-auth-20261019T024635Z | 5通过/0失败/跳过：最小列和RLS、当前资格/撤销/到期、A/B/A目录消费、scope/profile/权限失败、阻塞请求取消与lease恢复 |
| control-and-columns-20261019T025326Z | 2通过：补普通control身份下FORCE RLS真实写入/RowVersion/rollback；只复测修正为DISTINCT、避免依赖测试顺序的列读取场景 |
| v1-compatibility-20261019T025546Z | 1通过，no-build：受影响旧v1夹具和A/B/A目录读取入口仍有效 |

上述按用例去重为7项，不把重复结果或夹具断言另计为覆盖数。夹具还在各独立事务中证明空值类型、额外policy/列授权及已安装v1拒绝，并验证失败/成功安装后rollback无残留，再安装commit。普通控制角色测试通过继承Owner的普通NOLOGIN角色执行，实际superuser/BYPASSRLS=false且两表row_security_active=true；runtime从未加入控制角色。

四个自有PG均已停止，有stopped.txt、无postmaster.pid；没有HTTP资源。只保留已有依赖/分析告警，未升级或抑制。复用W19[三表隔离](../identity-directory-isolation/DELIVERY.md)、[可信reader](../../WP02-05/trusted-directory-reader/DELIVERY.md)与[HTTP合同](../../WP02-05/directory-http-integration/DELIVERY.md)未变范围；没有重复全仓测试。

复现：设置`CP6_PERMISSION_PG_BIN=C:/Program Files/PostgreSQL/18/bin`及全新自有`CP6_PERMISSION_PG_ROOT=D:/CP6-local-test/wave20/permission-consumption-w20-01-<unique>`，使用现有NuGet缓存，执行`dotnet test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter FullyQualifiedName~IdentityDirectoryAuthorizationPostgresTests`。v1兼容用例须单独新根运行，避免两个fixture争用同一根/端口。

## 集中审查与剩余范围

一次作者SPEC/QUALITY审查覆盖本任务完整diff、实际PG表/列/键、最小ACL和RLS、角色身份、参数绑定、tenant/source边界、当前资格时点、事务回滚、取消和版本兼容。补充普通control专项验证并去掉测试顺序依赖后，无剩余阻断；不是独立审查。每次push/PR核对未变workflow引用和本次路径过滤；相关输入不变不重跑相同业务验证。

W20-02安装/inspect、W20-03实际宿主、W20-04串联尚未完成。完整WP02、既有库升级/恢复和全Main切换、冻结全目录/CRM、Owner最终门、真实DEV/业务/生产仍未完成；无Actions触发/取消、分支保护变化、既有数据库操作、生产部署、新工作树或代理。共享台账和四状态同窗口同步，远端核对后才关闭本有限窗口。
