# W23-01 / BUG308 平台管理员事务保护交付

## 结论

基线 `8deae54898ac7294624082c1b18421a3b558ea64`，分支 `codex/bug-308-platform-admin-transaction-20261019`。原生复现并修正[P0 #308](https://github.com/GTX537/CP6/issues/308)；**本地可合并 Yes**，一次作者集中SPEC/QUALITY无剩余阻断。65项去重有效通过：52原生PG/HTTP、13既有服务/控制器单测；所有失败记录保留。尚待正常合并、远端核对和Issue关闭。

## 问题和修正

原服务先计数再保存，两个撤销事务可同时认为另一个管理员仍在，最终全部失去平台权限。普通User停用/删除及直接批量DML没有共同约束。授撤标志先提交，后续best-effort安全审计失败被吞，仍返回成功。三个防自锁死RED和独立审计故障RED均真实复现。

新增PostgreSQL前向迁移 `20261019073000_PlatformAdminGuardV1`：专属schema内唯一root记录由事务UPDATE串行；其revision仅是平台管理员集合串行标记，**不是完整IAM authority/authentication epoch**。UPDATE Enable/IsPlatformAdmin、DELETE、TRUNCATE和新增启用管理员进入同一根；AFTER STATEMENT按完整旧集合/最终事实拒绝移除已存在的最后一名Enable且IsPlatformAdmin用户，原E-SEC-037保持。多行原子替换可保留合法新管理员。初始零管理员不凭空创建管理员；没有自动授予或数据搬迁。

原生DML也执行触发器；不依赖每个EF调用自愿拿advisory锁。更新锁根使陈旧RR/Serializable在并发后得到40001，不把旧快照当当前事实。READ COMMITTED等待后重新观察。原生语义依据[PostgreSQL触发器可见性](https://www.postgresql.org/docs/18/trigger-datachanges.html)、[事务隔离](https://www.postgresql.org/docs/18/transaction-iso.html)及[CREATE TRIGGER](https://www.postgresql.org/docs/18/sql-createtrigger.html)，本次有实际并发运行证据。

PlatformAdminService在同一事务先进入root，重新加载目标当前事实，检查集合，再将标志、必需安全审计和原字段审计一起保存。caller transaction使用独立savepoint；失败保留调用方之前的工作，并恢复操作前跟踪状态，避免随后误保存失败授予。服务拒绝已有未保存改动的context；不替调用方顺手提交其他实体。旧登录best-effort审计服务不改。审计仍显式落DefaultTenant，保留实际请求actor。User更新/删除只将准确的PG约束错误映射原E-SEC-037，不吞其他数据库错误。

schema/table/function未授PUBLIC能力，函数为invoker；缺root失败关闭、不自动重建。仅被授用户DML但无内部root权限的普通角色也被拒绝。完整普通写账号的最小授权采用仍未完成，不能据此称所有Main写入已切普通身份。超级用户/表Owner关闭trigger、修改DDL等管理旁路不在应用DML保证内。

## 验证和失败来源

证据根 `D:/CP6-local-test/wave23/platform-admin`，每次同名前缀JSON/log/TRX保留。所有成功均0跳过；没有把旧结果称为新执行。

| 前缀 | 实际结果/用途 |
|---|---|
| red-platform-admin-20261019T072908Z | 3项产品RED；第4项新审计夹具表名错误42P01，不是产品证据 |
| red-audit-fixture-fixed-20261019T073122Z | 1项真实RED，审计约束失败仍返回成功 |
| green-platform-admin-20261019T073341Z | 原步骤4通过 |
| transaction-boundaries-20261019T073709Z | 29项：26通过、3失败；2项误断言Npgsql EF包装异常类型，1项匿名POST先被CSRF拒绝403而非预期401 |
| boundary-contract-recheck-20261019T073848Z | 10通过；核原40001底层异常，匿名GET认证挑战，补跨租户审计/普通DML角色拒绝及受影响User更新单测 |
| affected-migration-writers-20261019T074002Z | 33项：32通过、1旧HTTP夹具直接撤唯一管理员被新约束拒绝；新迁移影响输入，故有理由追加此受影响集合 |
| migration-and-http-recheck-20261019T074146Z | 3通过；旧夹具补独立恢复管理员，实际HTTP正向授撤及三个拒绝路径，隔离已填数据baseline前向迁移/重复Migrate/保护生效 |

最终65去重有效结果由后四次适用记录合并，52原生、13既有内存/控制器。PG专项覆盖两种并发顺序、直接SQL/bulk、事务commit/rollback、RR/Serializable旧快照、调用方保存点、取消实际锁等待、审计故障、跟踪旧值、重复授予、多行原子换人、全局跨租户集合、缺root和TRUNCATE。锁等待检查自己的pg_stat_activity Lock状态，不用固定sleep推断成功。

实际HTTP宿主使用产品JWT/当前权限/平台过滤器、Tenant/Csrf/业务异常中间件与真实PlatformAdmin/User控制器。测试的无密码修改/删除不调用refresh/2FA，fixture对这两个未调用依赖用strict mock；不是完整Program/浏览器登录验收。实际数据库写入用现有测试Owner，单独普通DML角色测试只证明失败关闭。19项W22身份一致性/写者、2项既有基础HTTP、12项租户管理审计在新迁移输入下复查，未重跑全仓或其他跨仓制品。

## 审查、运行条件和剩余范围

一次作者集中SPEC/QUALITY覆盖完整产品/迁移/测试diff：全局管理员集合语义、root锁顺序/持有到commit、事务内必要审计、异步取消与savepoint、异常精确映射、受影响旧夹具、触发器权限和前向恢复。无独立审查代理。测试预期修正仅复查受影响用例，没有降低不变量或跳过失败。

迁移只在自有PG18新库及自有已填数据baseline副本执行；**既有DEV/生产尚未安装**。应用启用新PG授撤路径前须完成这条前向迁移，否则明确失败；没有Owner回退/自动修复/SQL Server新增实现。Down拒绝移除保护，恢复必须前向。相关平台集合变更会全局串行，长事务可能等待或死锁；吞吐未压测，不自动重放业务或降低隔离。提交/回滚结果不明保留UNKNOWN异常，不声称已撤销。

7个自有PG根均stopped、无postmaster.pid；HTTP/testhost结束。新worktree/代理/Actions0，原7项用户资料及失败根保留，D余约44.94GiB。仅复用原工作树与串行build/NuGet缓存；没有既有库操作、workflow/保护变更或部署。

本次只完成平台管理员防自锁死和事务审计。调用人认证/会话/撤权最终复核、完整IAM权威轴及全部writer、跨库Main/CRM、Owner业务最终门、既有环境rollout和真实业务仍未完成；247运行NOT_RUN不提升。User创建等其他写入白名单在本窗口未作安全完成声明，继续按实际入口检查。
