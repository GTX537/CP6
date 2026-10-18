# W18-04：公共基础PostgreSQL/HTTP串联验收

> **集成结果已核对：** 本窗口候选 `b85700a8cbb8e5f705cf748c009a6821d977140b` 经[PR281](https://github.com/GTX537/CP6/pull/281)合入远端main `f9a1496ce54c46cbbf6197cf8afd9675ae0cba63`，9个范围文件一致；Handoff `2e61d1de816e9d333f5f2d5feddb66419300d3a3` 已发布回读。四项有限窗口均已完成产品/适用验证并关闭；全轮21个自有PG及HTTP均停止。以下“待合并/收尾”是原候选报告时点。最终文档闭环正常合入及Handoff确认后结束持续目标；不新增产品测试。

三个已合入的公共基础切片已在同一实际HTTP/原生PostgreSQL链路验证：角色撤权后同token下一次读取拒绝，旧缓存不能恢复授权；角色/租户管理与身份快照、普通/优先Outbox及管理审计在失败时整体回滚。最终 **3项HTTP/PG场景通过、0剩余失败/跳过**（2项新串联、1项受影响原宿主回归）。集中SPEC/QUALITY作者自审无剩余阻断；本报告时点正常合并与全轮台账收尾尚待执行，持续目标active。

## 范围与前置

本窗口从远端main `58bdd59de191d3dd576c1fb5546a30a123abe74a` 开始，分支 `codex/wave18-foundation-integration-20261018`；START Handoff `b9292f5d9d4a38eb3bc51d41d8393d057591a835` 的 `tasks/COORD/messages/20261018T162700Z-wave18-integration-start/MESSAGE.md` 已发布回读。唯一总控在原 `D:/CP6-worktrees/wave17-dispatch-20261018` 串行执行，没有新对话/子代理/工作树。

| 已完成前置 | 候选与正常PR | 复用证据 |
|---|---|---|
| W18-01 / #275 | `d0805b6d28a65d199886b0f2d8c370d3588aa092` / PR276 | [授权一致读DELIVERY](../../WP02-03/consistent-permission-read/DELIVERY.md)：57项去重通过；一致语句快照、取消、聚合文本合同 |
| W18-02 / #277 | `cfa30476fa10445ea70c5995e9ce5fe4d527f708` / PR278 | [管理审计DELIVERY](../../WP02-04/tenant-management-audit/DELIVERY.md)：39项去重通过；四管理操作、强审计、caller事务、取消和平台三道闸 |
| W18-03 / #279 | `865a5b7b2f28c881efb8b377f1825acc3c3ed248` / PR280 | [身份一致性DELIVERY](../../WP02-05/identity-consistency/DELIVERY.md)：63项去重通过；旧跟踪值、版本、真实捕获、隔离和回滚 |

上述候选全部包含于本窗口基线，三个Issue均已关闭。不同窗口有重复场景，**不将57+39+63+3相加为全轮唯一用例数**；每份报告保留实际运行及复用来源。

本次只修改测试：TenantManagementPostgresFixture内HTTP宿主增加可选真实CrmIdentityRuntime/RoleController及权限服务，默认原配置保持；新增FoundationIntegrationPostgreSqlTests。无产品、模型、迁移、包或依赖变更。

## 实际链路与结果

使用真实Kestrel loopback动态端口、JWT签发及验签、TenantMiddleware、BizException/Csrf/MustChangePassword中间件、ASP.NET授权、RequirePermission/RequirePlatformAdmin过滤器、TenantController/RoleController、CurrentPermissionContext/PermissionAggregator、TenantAdminService、CP6Context捕获/快照writer和两个实际Outbox。读取执行W18-01的PostgreSQL单语句实现；角色/租户修改执行真实HTTP路由与本轮合入代码，不以假的controller/service结果替代。

- 普通A/B租户用户GET `/api/role` 各自只看本租户行。A编辑者通过PUT停用角色10，携带B的body TenantId也只改A；A原token下次GET403，B仍200，A用户绑定B的有效签名token同样403。已放入原key的旧unversioned allow不被消费；快照A v2为停用并进入优先队列，B保持v1启用。
- 真实CHECK约束拒绝A的普通Outbox后，PUT恢复角色返回500，角色、快照/RowVersion、代际、审计计数及两类队列与操作前完全一致；A依旧403。移除自有夹具约束后相同HTTP恢复成功，A下一次GET200、快照/事件为v3启用，B不受影响。
- 平台HTTP暂停目标租户后，业务Enable=false、tenant快照v2、优先事件、显式平台审计actor/tenant/target一致。真实审计CHECK拒绝恢复操作时HTTP500，业务/审计/快照/代际/队列保持原状；释放夹具约束后同请求成功，业务启用、快照v3和普通事件一致，管理审计恰有成功的暂停/恢复两条。
- 原HTTP平台三道闸与创建审计失败回归在默认宿主配置再次通过，确认新增可选配置未破坏原范围。没有重跑不受影响的三个完整矩阵。

生产路由之外的明确夹具边界：宿主仅加载Tenant/Role控制器；独立测试签名密钥、Bearer token、内存distributed cache、echo localizer及测试BCrypt成本，数据库为隔离提升权限fixture_owner。没有运行完整Program启动、浏览器/Cookie登录、Redis、后台消费者、普通身份RLS或真实DEV；不把角色next-read拒绝视作所有writer提交前撤权fence，也不额外封禁暂停租户的历史读取。授权/业务合同没有被测试范围暗改。

## 原始运行与资源

.NET8 / PostgreSQL18.6，正式Core/IdentityPriority迁移；复用原NuGet缓存及 `D:/CP6-local-test/wave18/w18-01/artifacts`。日志/元数据 `D:/CP6-local-test/wave18/w18-04/`，TRX在results。

| 前缀 | 实际结果 |
|---|---|
| `cross-slice-20261018T162840Z` | 2通过、1失败：角色串联和旧HTTP回归通过；新租户测试误写日志表单数，DDL报42P01，尚未进入审计拒绝阶段 |
| `management-followup-20261018T163003Z` | 1通过：改为EF模型元数据引用/转义实际表名，完成完整租户成功/失败串联；只复查此夹具修改，复用前两项未变结果 |

最终去重3项通过、无跳过。失败原件保留；现有NU1902/NU1903、CS1998/xUnit提示与新测试Assert.Single样式提示不影响结果，未抑制、未为纯样式扩展测试。两轮自有PG均stopped.txt存在、无postmaster.pid；所有Kestrel await using已Stop/Dispose。未访问既有CP6DB或PG副本，未替换现有服务。

复现命令（工作树根，每次PG根必须全新；本次实际分批范围以上表为准）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave18/permission-consumption-w18-04-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~FoundationIntegrationPostgreSqlTests|FullyQualifiedName~TenantManagementAuditPostgreSqlTests.Http_platform' -v:q
```

## 集中作者自审与收尾边界

当前总控完成一次完整任务SPEC/QUALITY作者自审：全部测试/宿主diff、实际Program中间件顺序对应范围、真实控制器与服务注册、当前源码/已合入依赖、错误注入因果链、跨租户/旧缓存/消息版本断言、资源释放、无敏感值/机器配置进入产品。表名失败仅修正夹具并局部复验，无产品阻断。作者自审不称独立审查。

共享台账、生成视图、四状态同本窗口提交，diff/链接/结构检查通过后正常PR合并；远端包含性和范围文件核对后再记录最后交付、关闭本轮窗口。目标只能在Handoff/状态最终收尾后完成。未触发或取消Actions、未变触发配置；每次push/PR复核工作流引用与路径过滤。不部署、不发正式包、不修改既有数据库。

本轮使用同一个物理工作树和共享依赖/构建输出；测试隔离根保留原始成功/失败证据，没有声称已回收旧工作树空间。完整WP02、F1/F2、普通数据库身份采用、CRM健康消费/目录和Owner最终提交门、真实业务/DEV/生产验收仍未完成，不由本轮四项有限交付提升。
