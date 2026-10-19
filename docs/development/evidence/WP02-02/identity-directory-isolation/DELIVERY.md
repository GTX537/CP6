# W19-02：身份目录普通账号只读隔离组件

交付实际三张目录表的显式首次安装RLS/最小权限组件，真实普通A/B登录无tenant谓词查询仍只读本租户，写入/旁路对象/角色升级被拒绝。相关原生PG最终去重 **5项通过、0剩余失败/跳过**；夹具还逐项验证异常安装前置和整体回滚，不将前置断言数加成测试用例数。候选待正常PR和远端核对，完整WP02-02及实际宿主采用未完成。

## 基线、接口和对象

基线 `85f4776e36f19db16f5c0077f72906d8d3d31441`，分支 `codex/wave19-directory-isolation-20261018`。W19-01已通过PR285合入，#284 Closed。Handoff `a2a66b333bf48a9b519bad03461a9a9fd6cc0036` 的 `tasks/WP02-02/messages/20261019T000959Z-wave19-isolation-start/MESSAGE.md` 为一次真实START。同一总控复用原目录，无新代理或worktree。

`IdentityDirectoryRowSecurityV1.InstallAsync(connection, callerTransaction, logins)` 仅限明确的control首次安装，model/migration标记为 `identity-directory-read-v1` / `identity-directory-read-rls-v1`。数据库布局保留现有固定schema，没有另造目录副本或按请求拼schema。

| 原表 | 原主键 | 保留的事实 | runtime授权 |
|---|---|---|---|
| crm_identity.Bootstrap | TenantId | CompletedAtUtc、bundle SHA、RowVersion | 租户内SELECT |
| crm_identity.Snapshot | TenantId + AggregateId | payload原文/hash、事件/版本、墓碑、RowVersion | 租户内SELECT |
| public.CrmIdentityTenantGenerations | TenantId | bigint Generation、非负约束 | 租户内SELECT |

列类型/空值/主键和三条触发器身份先校验；不改变数据、列、索引、键、文本排序规则或历史迁移。Snapshot的代际触发器、两个RowVersion触发器保持原实现。已有RLS/policy/登录目录、角色继承/提升、表和列级异常授权、PUBLIC系统权限增加、未知应用函数及不符合安装预期的对象拒绝，不自动接管旧profile。

安装复用唯一TenantLoginDirectoryV1（session_user的OID+名称映射和事务目录锁），三表ENABLE/FORCE RLS；普通角色只授三表SELECT、必要schema USAGE、CONNECT和受保护tenant helper EXECUTE。没有任何表写入、sequence、映射目录读取或其他应用函数执行权。安装检查初始及最终有效权限，复用原ServiceTokenProfileV1的原生PUBLIC初始ACL校验，无旧profile/seal修改。

控制Owner有显式ALL policy，保持权威写入；普通runtime不得拥有/继承控制角色。安装在新隔离布局撤销PUBLIC的CREATE/TEMP、public schema及应用函数默认执行，保留控制身份权利。新安装事务锁为数据库局部 `(1129330260,3)`，与目录映射1/service provisioning2分开。调用者持有事务并负责任何失败后的rollback；组件不commit、不retry，不支持重复安装/既有profile追加/生产迁移或持续ACL漂移监控。

## 实际本地证据

PostgreSQL18.6、.NET8及原锁定依赖；夹具执行实际Core/IdentityPriority迁移并通过实际Bootstrap生成A/B目录（含相同permission:role:777聚合）。证据在 `D:/CP6-local-test/wave19/w19-02/`，原始log/JSON/results-TRX和失败数据库均保留。

| 运行前缀 | 真实结果及修正 |
|---|---|
| `isolation-red-20261019T001200Z` | API scaffold尚不安装权限，1失败/0跳过，普通账号被实际连接IdentityRejected安全门拒绝；这是缺失功能RED |
| `isolation-green-20261019T001411Z` | 1失败：新安装SQL主键校验的JOIN范围错误，42P01；未完成安装，不冒充隔离业务失败 |
| `isolation-query-fixed-20261019T001537Z` | 1失败：PG优化表达式顺序，sequence权限函数被用于非sequence对象，42809；以按对象类型CASE保护权限函数修正 |
| `isolation-acl-fixed-20261019T001751Z` | 首个完整A/B普通账号读取场景1通过 |
| `isolation-boundaries-20261019T002129Z` | 3通过、2测试假设失败：无grant-option的GRANT可warning/no-op；PG18禁止取消bootstrap superuser属性。安装前置/回滚和读取/映射/重装拒绝均完成 |
| `isolation-control-followup-20261019T002329Z` | 两项受影响测试2通过：验证实际无UPDATE权；使用事务内临时普通control成员，SUPERUSER/BYPASSRLS均false且row_security_active=true，真实控制写入和触发器有效 |

最后修改仅测试，产品从isolation-acl-fixed后未变。有效5项是最后两次按用例去重组合，不重跑未变成功范围。关于上述PG行为核对了[ALTER ROLE](https://www.postgresql.org/docs/18/sql-alterrole.html)和[GRANT](https://www.postgresql.org/docs/18/sql-grant.html)；没有为适配测试给runtime扩大授权。

真实矩阵覆盖：

- 每个普通连接实际通过TenantConnectionGuard；三表不带tenant WHERE仍仅本租户，外租户WHERE返回0，同聚合ID互不泄漏。缺失/重命名登录映射使已有连接拒绝，恢复映射后可读。
- INSERT/UPDATE/DELETE/TRUNCATE、旁路业务/ServiceToken/两类Outbox、sequence、MES函数、控制/另一租户SET ROLE、持久/临时DDL均被42501拒绝。自行GRANT UPDATE无效果；row_security=off不能绕过，伪造cp6.tenant_id设置也不改变session映射。
- 每个坏前置在独立control事务内施加、断言固定类型拒绝并rollback：runtime额外SELECT/TEMP、PUBLIC列权限/系统表UPDATE、pg_read_all_data继承/BYPASSRLS、额外policy/列、禁用代际trigger、SECURITY DEFINER/未知应用函数、既有目录。正确安装后rollback不留schema/helper角色/RLS，再由正确安装commit。原生SQL错误不被当成预期拒绝。
- 普通control继承角色在事务内验证FORCE RLS实际生效，仍能控制读取全目录及更新Snapshot，原RowVersion改变和generation递增；rollback恢复原数据并移除探针角色。运行账号从未继承此角色。

六个自有PG已停止，均有stopped.txt且无postmaster.pid，55435无监听；无HTTP资源。保留原有依赖与分析告警，无升级/抑制。当前普通角色验证为本地合成目录数据，**不是已有DEV、全Main普通账号切换或生产验收**。

复现：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave19/permission-consumption-w19-02-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~IdentityDirectoryPostgresTests' -v:q
```

## 审查、复用与后续

唯一总控完成一次任务级SPEC/QUALITY作者自审：完整产品/测试diff、实际模型/迁移对象、SQL参数与标识符、普通有效权限/Owner policy、失败后control rollback、对原目录helper的复用和未知profile拒绝。两项PG SQL实现问题及两项测试假设均按上述证据闭环，无剩余阻断；作者自审不称独立审查。

复用既有[连接组件](../tenant-connections/DELIVERY.md)的连接预算/轮换/取消/生命周期，以及[目录一致性](../../WP02-05/directory-consistency/DELIVERY.md)的bootstrap/分页合同；本轮没有重跑这些未修改行为。W19-03将增加真实可信连接读取适配，W19-04串联HTTP。当前内部installer没有Program/CLI注册，版本字符串本身不证明宿主采用。

全WP02、完整不可变目录协议、CRM消费/健康对账、普通用户/部门管理写路径、Owner最终撤权门、生产安装升级/恢复/容量以及真实业务验收仍未完成。没有Actions触发/取消、依赖/工作流/分支保护变化、既有库操作或部署；正常push/PR前复用未变工作流内容并检查本次路径。共享台账和四状态同窗口同步，远端核对后才关闭本窗口。
