# BUG-257 — wave15 A 实施交付

2026-10-17；原 A 唯一实施 Owner；[Issue #257](https://github.com/GTX537/CP6/issues/257) 保持 Open。本报告所在提交为候选，固定完整 SHA 由本任务 Handoff DELIVERY 指向；尚待原 D 集中审查、COORD 集成与真实 DEV 验收。

## 基线与范围

- 产品分支 `codex/bug-257-dashboard-tenant-20261017`，工作树 `D:/CP6-worktrees/bug-257-dashboard-tenant-20261017`。开工及交付前 fetch 后 HEAD 基线、本地 main、origin/main 均为 `f73e985a027080be3c92443cb79b24d83f65509c`。
- 已读取 [A 执行包](../../../development/dispatch/20261017-wave15/A-BUG-257.md)、dispatch、roadmap 及最新 COORD `f1af49e2ad83296a826c4242626dd00ca366eae7`；单次真实 START 已发布在 A 资料分支 `6cdbf743561e308f52253f84e67b0636b529a274`。D 准备结论 `fd1ee29f2f584154f7013ee15adebc71e1744f6f` 已纳入核对，不冒充最终审查。
- 仅修改登记的六个文件：DashboardController、CacheService 的 dashboard key、两个既有 dashboard 关系测试、新 DashboardTenantIsolationTests、本报告。ReportParity 中 MES 行为未修改。

## 修正

`GET /api/dashboard` 在任何缓存读取前调用现有 `ICurrentPermissionContext.GetAsync()`，复用已合入的单一认证身份、合法且一致的 GUID/tenant、数据库上下文及启用持久用户校验。`CurrentPermissionUnavailableException` 转为安全 403；无认证由现有 `[Authorize]` 返回 401。捕获并复核同一个服务端租户值，避免缺失 claim 回退默认租户成为授权依据；合法默认租户仍可访问。未增加平台或 Owner 绕过，也未改变仪表盘既有角色准入规则。

所有九个 KPI 子查询（包含两个待审批来源）、最近订单、工单状态查询均绑定该租户参数。过滤在 TOP/LIMIT、排序和分组前发生。新键为 `dashboard:summary:tenant:v1:{tenantGuid:D}`，不读取旧 `dashboard:summary`；保持原一分钟滑动过期。DTO、decimal 数量、应用今日/月初下界和既有软删除统计语义保持不变。历史 SQL Server 查询同步保留租户条件并编译通过，本轮没有 SQL Server 运行验收。

## 实际本地验证

独立 PostgreSQL 18 实例 `127.0.0.1:55449`、随机自有 `CP6Compat_WP5_20261003_<owner8>` 数据库及 fixture ownership comment；沿用 WP5 原生迁移与空表门禁。HTTP 使用自有 Kestrel `18257`、测试签名 JWT、真实 JwtBearer/TenantMiddleware/CurrentPermissionContext/PermissionAggregator/CP6Context。数据库中的 A 为合法默认租户，B 与空 C 为随机独立租户，三个持久启用用户。仅测试数据，不是生产/客户验收。

2026-10-17 最终运行：**6 passed / 0 failed / 0 skipped**；同时完成受影响项目与必要依赖编译。调用私有 `test.ps1 -Run green-subject`，底层命令为：

```powershell
dotnet test CP6.Tests/CP6.Tests.csproj --no-restore `
  --artifacts-path D:/CP6-local-test/BUG-257/wave15-a/artifacts `
  --filter 'FullyQualifiedName~DashboardTenantIsolationTests|FullyQualifiedName~DashboardRelationalTests|FullyQualifiedName~ReportParityRelationalTests.Dashboard_' `
  --logger 'trx;LogFileName=green-subject.trx' `
  --results-directory D:/CP6-local-test/BUG-257/wave15-a/results `
  -p:NuGetAudit=false -v quiet
```

需先按既有 fixture 配置 `CP6_WP5_CORE_TEST_PROVIDER=PostgreSql`、私有 `CP6_WP5_CORE_TEST_CONNECTION` 和 `CP6_TEST_DATABASE_OWNER`；不在报告或 Handoff 放置密码/JWT。restore 使用现有本地包缓存、locked mode 和隔离 artifacts，未修改依赖/项目/锁文件。

| 验证 | 实际覆盖 |
|---|---|
| 新原生 HTTP 聚合/缓存测试 | A/B/C 全部 KPI、A 超过八条订单、仅本租户最近八条及状态分布；A/B/C 冷访问与 C/B/A/B/C/A 暖访问；预热后数据库变动不影响缓存快照，清缓存后只改变 A；decimal `123.45678901`、删除标记订单、日期下界、未来日期沿用原语义；写入选项为一分钟滑动过期 |
| 旧缓存污染测试 | 预置旧全局键的他租户汇总/订单/状态，空 C 仍全零/空列表 |
| 暖缓存认证负向 | 未认证 401；缺失/非法/空 tenant 或 GUID、用户不存在、跨租户用户、冲突 subject/tenant claim、带 `token_use=2fa_verify` 的已签名 JWT、伪平台管理员跨租户均 403；预热后持久用户停用立即 403；query/header 不选租户 |
| 服务端上下文不匹配 | 测试专用中间件将数据库上下文改为 B，签名 A 身份被真实权限组件拒绝，未写 dashboard 缓存 |
| 两个既有关系测试 | 空库 DTO 与本租户 KPI/top8/软删除/缓存刷新回归；只为这两个直接构造测试提供权限 mock，不将其声称为 HTTP 身份验证 |

原始日志与 TRX 留在私有 `D:/CP6-local-test/BUG-257/wave15-a/`：最终 `green-subject.log`、`results/green-subject.trx`。成功结果中的既有编译/analyzer warning 未扩展为无关修复。

失败记录完整保留：`red.log`/`red-behavior.log` 为测试编译问题；`red-native.log`/TRX 为夹具 ItemCd 超过现有 PostgreSQL 长度约束，缩短随机业务键后继续；`red-regression.log`/TRX 在修改产品前四项均失败，实际出现全局统计 11/11/6/4/4/4/6/6、旧缓存污染 99、错误 HTTP 200。该轮测试用户 ID 在 EF 生成前被读取为空，所以它证明原实现无租户过滤/旧键污染和缺少身份拒绝，不能当作合法持久主体正向证据。首轮 `green.log`/TRX 为 3 passed / 3 failed，暴露此夹具问题；显式生成持久用户 GUID 后最终六项全通过，真实正向与负向证据以 `green-subject` 为准。

## 审查、资源与剩余工作

A 已核对完整 diff、全部九个子查询和两个列表查询参数、认证先于缓存、异常拒绝、默认租户正向、旧键隔离、日期/精度/软删除及白名单；`git diff --check` 通过。D 的一次最终 SPEC/QUALITY 以固定候选为输入，未在此提前声明通过。

结束时通过 `SHOW data_directory` 和数据库 ownership comment 核对自有实例，确认六张业务表均空后停止 PG55449；18257/15257/55449 无监听，15257 从未使用。私有实例/日志保留；既有 PG5432、DEV19991/18080、原库、客户数据和截图均未操作。早期失败夹具可能留下的测试身份仅存在于已停止的本任务随机数据库中，不作为验收数据或共享环境。

候选与远端 main 工作流/相关依赖内容未变：普通入口仍为 `workflow_dispatch`，R2 candidate 仅 `v*.*.*` tag push，本次只推任务分支；没有本任务排队/执行中运行需要取消，没有触发配置变更或 Actions 调用。Handoff 仍 private、Actions disabled，默认与本资料分支无 workflows，按原安全检查结论推送并回读。

未执行全仓回归、MES/其他缓存验证、SQL Server、Redis 部署测试、完整生产宿主/浏览器或真实 DEV 业务验收；公共授权组件复用当前 main，未重写其合同。COORD 在 D 通过后负责正常集成、远端 main 核对、同一用户已确认 PostgreSQL DEV 的空租户/交替缓存冒烟、共享状态和 Issue 关闭。本窗口未合并 main、未创建发布候选、未部署，原 #38 有效交付不重复执行。
