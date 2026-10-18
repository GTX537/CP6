# W18-03 / BUG-279：身份快照锁后读取与版本一致性

修复 [P0 #279](https://github.com/GTX537/CP6/issues/279)：DbContext曾跟踪停用快照v1，另一连接恢复并提交v2后，原Context再次停用，旧实现会按v1误判内容未变，导致业务停用而存储快照仍启用、遗漏优先撤权事件。现在取得事务锁后读取实际存储行并更新未修改的跟踪快照与RowVersion，再做内容去重和版本分配。原步骤已红绿验证，最终相关证据去重 **63项通过、0剩余失败/跳过**；正常PR与远端核对尚待执行，Issue保持Open。

## 实施范围

基线 `d77d8c030ee2fd17b8e2f6c8e24d476112d5f0d9`（PR278），分支 `codex/bug-279-identity-consistency`。真实START经Handoff `a6cc53751a6062fe93cf1582a9c8e9d9e2f2235b` 的 `tasks/WP02-05/messages/20261018T161016Z-wave18-start/MESSAGE.md` 发布回读；同一总控、同一 `D:/CP6-worktrees/wave17-dispatch-20261018`，无子代理/新增工作树。

产品只修改IdentitySnapshotWriter.LoadLockedAsync：保留既有PostgreSQL事务资源锁、FOR UPDATE与取消传递；用AsNoTracking避免EF返回旧实例，再同步CurrentValues、OriginalValues和并发令牌。无跟踪行时Attach锁定结果；同事务已暂存的Added/Modified版本只在数据库原版本仍匹配时保留，冲突拒绝。未改变payload/事件/版本合同，不清空整个ChangeTracker，不提交或重试调用者事务。

原IdentityChangeCapture、IdentityPermissionSnapshot及CP6Context捕获/双阶段保存无需改动。原生验证确认它们已覆盖本窗口用户主/附角色、部门、角色/操作/范围/字段与共享菜单键的实际矩阵；不为制造产品修改重写。没有模型/迁移/包版本/依赖变化，没有新增SQL Server实现或双库验收。共享逻辑的旧SQL Server分支保留，本次没有运行SQL Server。

READ COMMITTED下取得锁后使用当前存储事实；调用者REPEATABLE READ若已建立过时快照，真实PostgreSQL 40001经EF包装返回，调用者负责整体rollback/重新发起，writer不将其旧值当新版本，不自动重放业务。失败工作单元结束后不能直接重试残留EF跟踪状态；不新增UNKNOWN幂等恢复。

## 实际验证

PostgreSQL18.6、.NET8、正式Core/IdentityPriority迁移；沿W18-02的TenantManagementPostgresFixture，复用既有NuGet与W18-01构建产物。日志、TRX及运行元数据在 `D:/CP6-local-test/wave18/w18-03/`。以下是实际分批运行，不把合集称为一次重跑。

| 原始运行前缀 | 实际结果和适用范围 |
|---|---|
| `stale-red-20261018T161305Z` | 新测试首次编译失败：期望long[]与实际int[]不符；未启动PG，不作为产品RED |
| `stale-red-fixed-fixture-20261018T161407Z` | 1失败：实际业务已停用而存储snapshot.enabled仍true；有效原步骤RED |
| `stale-green-20261018T161523Z` | 1通过：相同两连接原步骤，v3停用快照与优先队列版本1/3正确 |
| `capture-concurrency-20261018T161804Z` | 5通过、1失败：两连接首/后续版本、重复暂存去重、用户/权限矩阵通过；部门新夹具在Add前用空GUID构造Path，被真实合同拒绝 |
| `department-rollback-20261018T161918Z` | 5通过：修正部门夹具ID后的移动/负责人/停用/删除墓碑；重验受writer影响的W18-02快照/普通/优先Outbox拒绝与caller rollback四项 |
| `isolation-contract-20261018T162032Z` | 51通过、1失败：外租户用户/部门/附角色隔离及50项原捕获/合同通过；长事务实际40001正确，但新测试原先要求未包装异常类型 |
| `repeatable-followup-20261018T162114Z` | 1通过：按实际EF异常包装核对内部Postgres 40001及caller rollback，已提交v2不受损 |

最终9项新原生PG + 4项受影响旧原生回滚 + 50项原捕获/合同 = 63项去重成功。最后只修改测试夹具或断言，产品从stale-green起未再变化，成功未变输入直接复用；所有失败记录保留。原有NU1902/NU1903、CS1998/xUnit提示未抑制、未顺带升级依赖。

实际矩阵：

- 两个真实独立连接竞争同聚合缺行首版本及已有版本；第二写入在第一事务持锁期间不能结束，提交后按新版本去重。两类队列合并检查版本唯一递增、完整事件校验通过、最终data与存储快照完全对应；同事务重复暂存不丢消息或重复版本。
- 用户主角色、附角色从一个用户移到另一个用户、移除附角色、DeptId/ManagerId、停用均反映最终事实；停用走优先队列，display-only Email不增身份版本。部门路径/ParentId/LeaderId、停用、删除墓碑依次版本1..4。
- 角色操作、范围/自定义部门去重、字段限制及共享菜单键更改正确捕获；菜单键扩展到两个配置租户的实际角色，停用一方不污染另一方。外租户同号角色、同UserId错误tenant附角色、外租户用户/部门ID均不能泄漏事实，缺失投影是相应最小墓碑。
- 真实CHECK约束分别拒绝快照、普通和优先Outbox，业务/管理审计/字段审计/版本/RowVersion/代际/消息全部回滚；调用者事务中的优先消息其他连接不可见，caller rollback同时撤销。此四项使用W18-02原测试在修改后的writer上实际重验，不冒称旧运行。

所有6个 `permission-consumption-w18-03-*` 自有实例已停止、无postmaster.pid，端口55435释放，数据库和失败日志保留。首个编译失败未创建实例。fixture_owner是隔离提升权限测试身份，**不证明普通数据库身份RLS、已有数据库迁移、真实DEV或生产采用**。本窗口无HTTP实例；W18-04仍负责跨切片HTTP串联。

复现最终选择（PowerShell，工作树根；每次PG根全新）：

```powershell
$env:NUGET_PACKAGES='D:/CP6-local-test/WP02-02/adopt-a1/n'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-local-test/wave18/permission-consumption-w18-03-'+[guid]::NewGuid().ToString('N')
& C:/Users/tt/.dotnet/dotnet.exe test CP6.Tests/CP6.Tests.csproj --no-restore --artifacts-path D:/CP6-local-test/wave18/w18-01/artifacts --filter 'FullyQualifiedName~CrmIdentityConsistencyPostgreSqlTests|FullyQualifiedName~CrmIdentityChangeCaptureTests|FullyQualifiedName~CrmIdentityContractTests|FullyQualifiedName~TenantManagementAuditPostgreSqlTests.Identity_stage_failure|FullyQualifiedName~TenantManagementAuditPostgreSqlTests.Caller_rollback' -v:q
```

## 集中SPEC/QUALITY作者自审与剩余范围

当前总控完成一次任务级集中作者自审：相对最新已确认main的全部产品/测试差异、所有writer调用点（CP6Context/bootstrap/GDPR/service-token）、锁与跟踪版本、RowVersion并发、事务所有权、真实失败来源、跨租户谓词及合同/包未变。失败后仅对部门夹具与异常包装断言局部复查，不重启完整审查；无剩余阻断。作者自审不冒充独立审查。diff、台账、生成视图及四状态一致性检查作为提交前必要文档检查。

服务token既有资格/撤权协议不在本窗口重跑；公共锁路径保留，实际采用证据按原范围引用。完整WP02-05目录协议、CRM消费健康与对账、所有writer共同授权epoch/Owner最终撤权fence、完整WP02/F1/F2与业务/生产验收均未提升；Main组件结果不替代其他仓采用。

无本轮Actions触发/取消或配置变化；push/PR前核对候选与main工作流引用和路径过滤。正常PR合入并核对远端包含候选及范围文件后关闭#279、释放窗口，并从最新main在原目录开始W18-04。持续目标仍active。
