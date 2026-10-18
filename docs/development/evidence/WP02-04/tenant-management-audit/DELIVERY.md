# W18-02 / BUG-277：租户管理与必需审计同事务

本窗口修复 [P1 #277](https://github.com/GTX537/CP6/issues/277)：创建租户后，旧实现另行调用吞异常的审计服务；日志被数据库拒绝时仍返回成功并留下租户、管理员与角色。现在 create/update/suspend/reactivate 将平台管理日志加入业务的同一 SaveChanges 工作单元，失败不再被吞掉。原生 PostgreSQL 与相关回归去重 **39 项通过、0 失败、0 跳过**。一次集中 SPEC/QUALITY 作者自审无剩余阻断，候选可正常合并；本报告时点尚未合入，Issue 仍 Open，远端结果由总控 Handoff/台账接续记录。

## 实现与边界

基线 `f58080a06df806b00e4f7da0ba2a64404253ee6b`（W18-01 / PR276），分支 `codex/bug-277-tenant-management-audit`，复用目录 `D:/CP6-worktrees/wave17-dispatch-20261018`。实际 START 为 Handoff `3fb4e89a2ec059e4562ea231d6d4e81a7e89fc75` 的 `tasks/WP02-04/messages/20261018T154215Z-wave18-start/MESSAGE.md`；P1 红绿进展 `6c51afba89447216e1a6c7f81c4fed3cc66a4ba6` 已发布回读。唯一总控实施/审查，无子代理或新增工作树。

- TenantAdminService 直接暂存 Sys_SecurityLog，显式 `TenantContext.DefaultTenant`，保存真实可用 actor GUID/name、原目标 code 与不可变目标 GUID。临时密码不写日志，只沿原成功返回合同返回；角色、默认菜单复制、强制改密、时区与错误码逻辑保留。
- 管理日志和业务同一次 CP6Context.SaveChanges，沿现有物理事务涵盖字段审计与启用后的身份快照/普通或优先 Outbox。调用者已有事务时由服务加局部 savepoint，晚阶段失败回滚到此处，保留调用者此前工作；服务不替调用者 commit 或整体 rollback。
- HTTP CancellationToken 传到四种管理操作的查询与保存。真实数据库锁等待取消后回滚并释放自有事务/连接。普通 SecurityAuditService 的 best-effort 认证审计合同没有改变。
- 构造函数删除不再使用的 audit/tenant 依赖；仓内两处原直构测试已同步，现有 DI 注册不变并经真实 HTTP 解析。四个接口方法新增可选取消参数，原调用源码保持兼容；HTTP 路由与请求/返回 DTO 不变。没有发布 Core 包或声称外部二进制兼容。
- 没有持久请求槽或幂等恢复协议。重复 code 沿原 E-SEC-033/数据库唯一性处理；超时或响应未知不意味着未提交，不能自动重试创建或重发旧密码。失败后结束该 DbContext 工作单元，不重试其残留跟踪状态；caller savepoint 证明数据库先前工作可保留，不承诺失败 EF 跟踪状态可直接复用。

新租户不会由此自动加入 CrmIdentityOptions.Tenants；已有显式启用的租户才沿现有捕获链发布。未更改公共 CP6Context、捕获器、模型、迁移历史、依赖或正式包。本窗口关联 WP02-10 的必需管理审计切片，不扩展普通登录审计或全部审计治理。

## 真实验证与复用来源

本机 .NET 8、PostgreSQL 18.6；沿 W18-01 已成功锁定恢复，NuGet 缓存 `D:/CP6-local-test/WP02-02/adopt-a1/n`。编译产物复用 `D:/CP6-local-test/wave18/w18-01/artifacts`，日志/运行元数据在 `D:/CP6-local-test/wave18/w18-02/`，TRX 在 `results/`。同一最终产品代码的未变成功证据直接复用，没有全仓重跑。

| 原始运行文件前缀 | 实际结果与用途 |
|---|---|
| `audit-red-20261018T154409Z` | 1失败：原生约束拒绝管理日志，旧服务 error=none，tenants/users/roles各1，logs0；有效原步骤RED |
| `audit-green-20261018T154649Z` | 1通过：初步修复在原最小模型夹具上通过 |
| `migrated-fixture-20261018T154914Z` | 1通过：切换正式迁移的原生夹具，确认同一原步骤 |
| `management-boundaries-20261018T155100Z` | 5通过、1失败：新测试误写事件编号，实际19/20/21/22；按真实枚举修正，不改产品 |
| `management-followup-20261018T155204Z` | 6通过：四操作审计拒绝回滚、正确事件/actor/target、caller先前工作保留；校验实际 PostgreSQL 23514 与准确约束名 |
| `identity-boundaries-20261018T155338Z` | 4通过：快照/普通Outbox/优先Outbox后阶段拒绝整体回滚，caller rollback同时撤销业务/审计/身份效果 |
| `http-cancel-20261018T155836Z` | 3通过：真实锁等待取消；Kestrel HTTP平台闸及审计失败500；补强caller事务内确有待提交优先消息后整体rollback |
| `menu-followup-20261018T160014Z` | 2通过：集中审查后补非空默认菜单输入，验证创建失败不留菜单映射、成功实际复制；只局部重验受影响场景 |
| `related-green-20261018T155240Z` | 21通过：TenantAdminServiceTests、TenantControllerTests、SecurityAuditServiceTests；直构控制器不称HTTP |
| `platform-gates-20261018T155252Z` | 6通过：RequirePlatformAdminFilterTests（原三道闸与TenantScope）。上一运行的误名过滤器未选中本类，此处实际执行 |

成功结果去重为12项原生PG/HTTP + 27项相关测试。最终仅补测试输入/断言与说明，产品逻辑未变；最后2项不与原用例重复相加。失败与早期夹具范围保持历史事实，不覆盖旧日志。既有 NU1902/NU1903、CS1998、xUnit2017/xUnit2029 提示保留，未抑制或改依赖。

原生矩阵检查 Tenant、admin、role/menu、字段审计与管理日志的独立观察连接状态；启用身份链时还核对 snapshot/version/payload hash/RowVersion、generation 与两类队列。三个后阶段失败是真实 CHECK 约束拒绝，不是替换持久结果的 mock；caller事务内先有实际版本和优先消息，其他连接不可见，rollback后全部消失。已有先前工作通过caller自主commit后仍在；失败管理修改及日志不在。

原生夹具使用已存在服务器生命周期，专属loopback55435；每轮一个全新数据库串行使用不同租户。身份写入依赖正式rowversion/代际触发器及优先队列，故扩展夹具执行真实 Core/IdentityPriority 迁移；未把模型简表冒充这部分验收。8个 `permission-consumption-w18-02-*` 根均已核对stopped.txt、无postmaster.pid；保留成功/失败数据与日志。fixture_owner是提升权限的隔离测试身份，**不证明普通身份RLS、既有数据库迁移或生产采用**。未访问既有CP6DB/PG副本。

HTTP使用临时Kestrel loopback动态端口（现有锁定依赖无TestServer，未新增包），真实签发并验签JWT、TenantMiddleware、BizException/Csrf/MustChangePassword、授权与实际TenantController/DI服务/原生PG。无平台claim、impersonation、DB撤权均403；合法平台身份创建成功且审计actor/tenant/target正确；审计数据库拒绝返回500，业务计数不变。宿主只装入该控制器，不声称全Program启动、完整Web/Cookie登录或真实DEV验收；正常Bearer请求沿原CSRF合同。HTTP应用已Stop/Dispose，无既有服务替换。

复现最终选中验证（PowerShell，工作树根；每次PG根必须全新）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$artifactRoot='D:/CP6-local-test/wave18/w18-01/artifacts'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave18/permission-consumption-w18-02-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path $artifactRoot --filter FullyQualifiedName~TenantManagementAuditPostgreSqlTests -v:q
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --artifacts-path $artifactRoot --filter 'FullyQualifiedName~TenantAdminServiceTests|FullyQualifiedName~TenantControllerTests|FullyQualifiedName~SecurityAuditServiceTests|FullyQualifiedName~RequirePlatformAdminFilterTests' -v:q
```

这是最终复跑命令；本次实际分批执行和复用来源以上表为准，不把12项最终合集称为一次重跑。

## 集中审查及剩余范围

唯一总控已按用户要求完成相对最新已确认 main `f58080a0` 的一次任务级 SPEC/QUALITY 作者自审：全部产品/测试diff、接口/DI调用点、平台过滤器与租户盖章、事务所有权和原生失败链、取消资源释放、秘密不入日志、未变输入复用与写集。审查补足默认菜单非空分支后只局部复查/验证，最终无阻断；git diff --check及台账/生成视图检查通过。**作者自审不等于独立审查**。

完整WP02-04/10、公共生命周期共同门、普通身份数据库权限采用、持久幂等恢复、所有writer授权epoch/fence、真实业务与生产发布未完成。W18-03身份捕获/并发版本和W18-04跨切片串联继续执行；不因当前39项通过提升375任务/125业务状态。普通认证审计的成功/失败合同沿原服务，不将管理强审计要求扩散到登录。

候选与受保护main工作流/依赖内容沿已核实版本，push/PR前再次核对实际路径过滤；无本轮排队/运行中Actions，没有触发、取消或配置变更。共享台账/生成视图/四状态同任务提交；正常PR合并、远端包含性与范围文件核对后才关闭#277、释放窗口并从最新main接续W18-03。不推Tag、不强推、不绕过保护、不部署。
