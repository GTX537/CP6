# W19-03：可信普通账号身份目录读取

交付DTO-only reader接口及内部可信连接适配器；实际普通A/B账号读目录，保留游标/404/409/503边界，无Owner回退。相关验证最终去重 **31项通过（14原生PG、17配置），0剩余失败/跳过**。一次集中SPEC/QUALITY作者自审无阻断；候选待正常PR合并与远端核对。当前不是全Main账号切换或真实业务验收。

## 基线及实现

基线 `c2b808a1e081b20d537a665cef387b90c6b4e164`（PR286），分支 `codex/wave19-trusted-directory-reader-20261018`。Handoff START `f201d63ac3f4423c91be83a9b702a255ce9503fa`，路径 `tasks/WP02-05/messages/20261019T004046Z-wave19-reader-start/MESSAGE.md`。同一总控复用既有目录，未新增工作树/代理。

- 新接口 `IIdentitySnapshotReader` 只返回目录DTO，原reader实现该接口；WebApi控制器依赖接口，原注册仍使用同一scoped legacy reader。没有自动开启普通账号环境。
- `TenantIdentityDirectoryReader` 仅接受可信管理器当前tenant，以及严格的 `identity-directory-read-v1` / `identity-directory-read-rls-v1`、空queue domains配置。host catalog/private credential resolver决定数据库/登录；方法tenant必须匹配可信绑定。请求无数据库/schema/login选择能力。
- 每次读取独占托管lease，以ReadCommitted事务运行。复用真实Core实体与PostgreSQL provider服务，TenantEfCommandGuard每条EF命令重验实际连接/事务和login映射。映射事务锁保留至commit；成功读取显式commit，失败/取消回滚释放。仅DTO越过生命周期边界。
- 旧Core模型对代际表省略schema。适配器私有只读DirectoryContext将现有表明确为public.CrmIdentityTenantGenerations，固定context类型分离模型缓存；不按tenant建立模型，不改原权威模型、历史迁移或数据库对象。
- PostgreSQL分页单语句联合Bootstrap就绪/bundle、generation和有界page，并显式排序；与snapshot读取一样可参与调用者事务，不创建/提交内层事务。ReadCommitted映射检查先完成，防止等待控制writer后仍使用旧RepeatableRead快照。单页是一个已提交视图，跨页代际变化仍409；不是不可变全目录协议。游标purpose/tenant/到期/分页上限不变。
- 只有五个已知IdentityReadException合同码通过内部结果封装保留；其他storage故障固定为C02_IDENTITY_STORE_UNAVAILABLE，控制器503，无SQL/detail/inner泄漏。取消原样传播，无Owner fallback或自动retry。身份认证、当前服务reader授权仍由宿主负责。

## 实际证据与保留失败

本机PostgreSQL18.6、原锁定.NET8依赖；真实Core/IdentityPriority迁移、Bootstrap、普通登录、RLS及实际TCP数据库。日志/JSON/TRX位于 `D:/CP6-local-test/wave19/w19-03/`，失败数据库保留。

| 运行前缀 | 实际结果 |
|---|---|
| reader-red-20261019T004117Z | 2失败/0跳过：adapter scaffold未实现；原分页在caller事务内尝试再开事务被拒绝 |
| reader-green-20261019T005447Z | 1通过/1失败：caller事务已支持；普通连接暴露旧代际表未限定public，42P01，已在私有只读模型修正 |
| reader-qualified-20261019T005639Z | 1项NotExecuted：筛选仅方法名，runner未设置显式PG opt-in。无PG根，不作为成功证据 |
| reader-boundaries-20261019T005816Z | 22通过；另9项因第二个fixture复用同一个owner-root而被安全拒绝，属于运行编排失败，不声称业务执行。22有效结果为17配置及5个受影响原PG用例 |
| reader-adapter-20261019T005902Z | 单独新根运行adapter集合：9通过、0失败/跳过。此前22项输入未变，直接复用；不重跑有效范围 |

最后两次有效证据合计31项去重。PG集合必须分别调用，每次新owner-root；复用的是构建输出、工作目录和成功证据，不复用旧数据库根。四个实际自有PG均stopped.txt且无postmaster.pid，无HTTP进程。

验证实际行为：

- 普通A→B→A读取；实际session_user分别directory_a/directory_b/directory_a，A复用同一backend、B为不同backend。分页两页、快照和外租户缺失结果正确，每次Borrowers归零。
- 无可信scope、scope结束后捕获的异步flow、请求tenant不匹配、model/migration/domain不符均拒绝；有效配置恢复后可读。
- 游标跨租户/非法cursor/page/aggregate保留400合同；真实角色投影变更后旧cursor409，重启分页成功。
- 撤销实际SELECT权限使page/snapshot均503，没有Owner回退；恢复权限后同manager可读。
- 实际Snapshot表锁阻塞两类查询，观察pg_stat_activity锁等待后取消；取消token保留，事务/lease释放，随后读取成功。统计观察者在持锁事务内清除统计快照，避免缓存造成假超时。
- 实际读取期间替换catalog：释放表锁后旧结果丢弃、旧pool排空，后续新catalog成功。控制事务删除login映射时reader等待advisory锁，commit后读取看到缺失并拒绝，恢复后成功。
- caller原ReadCommitted事务保持不被提交；其未提交not-ready状态可见，rollback后恢复。原受影响PG分页完整性/重放/代际一致性、空目录/就绪、游标到期及两类查询取消5项通过。
- 17项原配置检查通过；WebApi项目及必要依赖在该定向构建中编译，未全仓测试。

复现adapter集合（旧目录集合另选过滤条件并另建根）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave19/permission-consumption-w19-03-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~TenantIdentityDirectoryReaderPostgresTests' -v:q
```

## 集中审查和边界

唯一总控完成一次完整diff作者SPEC/QUALITY审查：租户来源/DTO生命周期、权限fail-closed、事务/映射锁等待、private模型缓存、SQL单语句/排序/LEFT JOIN空目录、固定错误码、默认DI兼容和实际资源收尾。没有独立审查声称，未以用例数量扩张目标。新实现问题、筛选跳过和fixture编排错误均原样保留；当前无剩余阻断。复用[三表普通账号隔离](../../WP02-02/identity-directory-isolation/DELIVERY.md)及[连接组件](../../WP02-02/tenant-connections/DELIVERY.md)未变的ACL/预算/通用生命周期验证，不重复运行。

W19-04继续实际HTTP认证/服务授权/可信context/普通目录读取串联。当前没有生产配置入口/全Main普通角色切换，不把合成可信来源的组件测试写成真实登录或DEV。完整WP02、冻结目录协议、CRM消费/健康对账、Owner最终撤权门、环境安装升级/恢复及真实业务生产均未完成。旧依赖/分析告警保留，无依赖升级/工作流/保护变更、Actions触发/取消、既有库操作或部署。共享台账/四状态同窗口同步，按正常PR及远端main核对完成后方关闭。
