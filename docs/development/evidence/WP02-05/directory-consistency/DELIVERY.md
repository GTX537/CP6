# W19-01 / BUG-284：身份目录就绪与读取一致性

修复[P0 #284](https://github.com/GTX537/CP6/issues/284)：单条读取可能使用旧的已就绪状态返回新未就绪目录中的快照；bootstrap也可能因Context跟踪旧完成标记而跳过实际重建。两个原步骤均真实红绿，相关原生PostgreSQL验证去重 **11通过、0剩余失败/跳过**。当前候选待正常PR和远端main核对，Issue保持Open。

## 改动和适用边界

基线 `dd2b37e97022615d5644784a0fe0c67623cbaae0`，分支 `codex/bug-284-directory-readiness`。同一总控复用 `D:/CP6-worktrees/wave17-dispatch-20261018`；真实START为Handoff `96f653bc1fbb8456999665b58828d89ccb33e1e5`、`tasks/WP02-05/messages/20261018T235448Z-wave19-start/MESSAGE.md`。确认BUG后在原任务目录将分支改名关联#284，没有新建worktree。

- IdentitySnapshotReader仅PostgreSQL单条读取改为一条含就绪条件的LEFT JOIN，保留“未就绪”与“已就绪但没有聚合”的区别。无新事务/自动提交/重试，调用者READ COMMITTED事务仍可使用；一次语句里的状态与内容来自同一可见快照。[PostgreSQL隔离语义](https://www.postgresql.org/docs/18/transaction-iso.html)
- IdentityBootstrapService仅PostgreSQL锁定查询按AsNoTracking取实际存储marker，再刷新未修改跟踪实体的当前值、原值和RowVersion。已删除旧marker解除跟踪并可重建；调用者暂存的marker修改拒绝，不覆盖用户工作。保留原session资源锁、Serializable事务、失败后新scope整体重试归属和取消传递。
- 原分页RepeatableRead、代际变化409/要求重新开始、游标用途/租户/过期、现有响应和事件合同不变。单条读取遇重建切换，可以看到旧且自洽的已就绪视图，或新的未就绪结果；不能把旧就绪标记拼接新未就绪快照。该语义不是“响应完成瞬间的当前授权”，也不是Owner提交前撤权门。
- 没有模型/历史迁移/项目依赖/Platform包修改，没有新SQL Server实现或双库门禁；旧非PG分支保留，未运行SQL Server。两处产品文件加一份原生验证文件，无Program或Controller行为改动。

## 实际验证和失败保留

PostgreSQL18.6、.NET8；新自有集群执行实际Core/IdentityPriority迁移。日志、JSON与results/TRX保留在 `D:/CP6-local-test/wave19/w19-01/`。复用既有NuGet cache和已释放的串行构建输出，不新建源码副本。

| 运行前缀 | 实际结果 |
|---|---|
| `readiness-red-20261018T235541Z` | 0通过、2失败、0跳过：预期旧就绪v1却返回未就绪v2；预期重建后的bundle却仍是全零旧值。真实产品RED，无编译/夹具失败冒充 |
| `readiness-green-20261018T235843Z` | 修正后2/2通过。读取断言接受合同允许的旧一致视图或新未就绪，仍拒绝原v2混合结果；旧RED保留 |
| `directory-focused-20261019T000030Z` | 10/10通过：上述两路径加分页完整/重传/代际切换、tenant/cursor/就绪/空目录、两种查询的实际PG锁等待取消、调用者事务、marker暂存冲突/删除重建、真实Outbox失败整体回滚 |
| `bootstrap-waiters-20261019T000225Z` | 1/1通过：实际session锁等待取消释放连接，旧跟踪和新Context两个等待者恢复后按最新marker重建一次，v2/优先事件唯一 |

最终11是后两次的去重组合，不将2次红绿重复相加。产品自readiness-green后未变；最后只增加等待测试并移除未用using，其他10项未变成功直接复用，没有反复全仓验证。

分页用三个实际发布聚合，逐页完整集合/重复游标的版本与hash一致；其他租户快照不可见。第二连接在generation查询之后更新角色，当前页保留旧边界和旧内容，下页409，新首轮取得v2。未知tenant、未bootstrap/错误bundle拒绝，合法空目录与缺失聚合分别保持原语义。游标跨租户、篡改/超长/过期及合法输入边界有覆盖。

真实表锁使单快照和分页查询等待，取消后释放自有查询事务，下一次可用；单快照在调用者事务看到调用者未提交的未就绪状态且不提交/回滚该事务。Bootstrap拒绝已有事务。真实CHECK拒绝普通Outbox后，marker、snapshot、generation、消息共同回滚；去掉夹具约束后新Context整体重试、重复初始化不重复版本/发件。

四个 `permission-consumption-w19-01-*` 实例均有stopped.txt、无postmaster.pid，端口55435已释放；失败库及日志保留。fixture_owner是隔离提升权限身份，**不证明普通账号RLS或生产/真实DEV**；W19-02/03负责普通身份部分。本窗口无HTTP进程。

保留原有依赖/编译分析告警（NU1902/NU1903、CS1998、xUnit）。新增夹具两处EF1002仅涉及本地代码生成的GUID与固定DDL，已检查无请求/外部SQL输入；不抑制告警、不把它说成干净构建。DDL控制仅自有测试库，不进入产品代码。

可复现选择（新根，不要复用既有数据库）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave19/permission-consumption-w19-01-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~CrmIdentityDirectoryPostgreSqlTests' -v:q
```

## 一次集中SPEC/QUALITY作者自审

唯一总控完成完整任务diff、两处调用方（Internal Controller/BootstrapWorker）、查询tenant谓词/左连接空值、跟踪与RowVersion、资源锁和事务所有权、取消和失败恢复的集中作者审查；实际行为测试与计划对应，无剩余阻断。作者自审不称独立审查。共享状态、证据链接和roadmap/生成视图在同一交付提交核对。

复用W18身份快照writer/管理审计/权限和HTTP证据的未变范围：[W18-03](../identity-consistency/DELIVERY.md)、[W18-04](../../WAVE18/foundation-integration/DELIVERY.md)。未重跑这些不受影响的整套用例；新bootstrap用真实writer/Outbox验证本次受影响路径。

完整WP02-05的冻结目录协议、普通身份全Main采用、CRM消费和Owner最终撤权fence，真实DEV/业务/生产仍为后续范围；本窗口不提高完整任务/目标完成状态。无Actions触发或取消、workflow变化、生产部署或既有库操作；push/PR前复用未变workflow分析并检查本次路径。正常合并/远端核对后关闭#284，随后按计划继续W19-02。
