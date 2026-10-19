# W25-01 / BUG319 租户管理当前调用人最终门

> 已集成：https://github.com/GTX537/CP6/pull/320 / source `f9f54ee51c8d237d403a8e0ef2c16eb5ae2e12df` / main `1974bcb4ee9f179c6539dd032b09329037cd5002`，# 319 已关闭。最终组合核对复用本报告有效范围；下文候选措辞保留原交付时点。

## 结论

基线 `dcf7cb45029af329f13df72a5b0de7fd5d0fa6bb`，分支 `codex/wave25-01-tenant-final-gate-20261019`。[P0 #319](https://github.com/GTX537/CP6/issues/319)本地修正，**Ready to merge: Yes**；一次作者集中 SPEC/QUALITY 无阻断，非独立审查。79项相关验证通过（60原生 PostgreSQL/HTTP、19既有服务/控制器单测），0失败/跳过。正常合并并核对远端main后关闭Issue，继续W25-02/03，整个长目标未完成。

## 实际缺陷与改动

原实现的HTTP平台入口已通过后，另一连接撤销/停用/删除actor，恢复请求仍能创建、修改、暂停、恢复租户；PG服务无认证主体也可创建。原生5项RED均记录实际错误成功结果。

四种PostgreSQL写操作现在先进入既有PlatformAdminMutationV1根事务，PlatformAdminAuthorization按可信subject/tenant/平台claim重读并FOR SHARE锁定当前actor，随后执行目标读取、业务和必要审计，持锁直到同一事务结束。直接服务调用无认证主体拒绝；当前actor拒绝由实际控制器返回403/E-SEC-031（模拟仍E-SEC-034）。目标仍可跨tenant管理，编码冲突E-SEC-033、目标不存在E-SEC-032及时区E-WF-028保持原业务错误范围。

事务助手只增加泛型返回值支持，旧无返回值入口转调原行为；创建的临时密码只在服务自有事务确定commit后返回。调用方拥有事务时返回值仍是待提交结果，调用方负责最终commit后对外输出；本次不把保存点释放说成外层提交。既有调用方savepoint、取消、不明回滚和跟踪恢复机制复用，无自动重放。

必要审计使用核实的持久actor Id/Name（不是可缺失/旧Name claim），长name按现有审计100字符列限制截断；日志不含临时密码。已有EF目标跟踪在根内重新读取，避免以旧跟踪状态管理当前目标。非PostgreSQL历史路径保留，没有新增双库要求或迁移。

## 实际验证

原日志、TRX和结构化结果位于 `D:/CP6-local-test/wave25/foundation`，失败记录保留：

| 前缀 | 结果及适用范围 |
|---|---|
| tenant-red-20261019T090346Z | 5失败/0跳过：四个实际HTTP撤权竞争和无主体直接调用 |
| tenant-green-20261019T090558Z | 原步骤5通过/0跳过 |
| tenant-boundaries-20261019T090838Z | 79通过/0跳过，以下风险集中验证 |

新租户actor11原生测试覆盖四写入口、无主体、双方先取得门的commit/rollback、实际PG锁等待、旧目标跟踪、合法跨tenant管理和HTTP错误/真实审计。原审计12原生测试在明确persisted actor输入下继续覆盖创建/修改/暂停/恢复必要审计拒绝、晚期field audit、snapshot/normal/priority outbox失败、调用方事务保留既有工作/整组回滚、取消真实审计锁等待和临时密码实际哈希。

因共用事务助手改为泛型结果，W23管理员根19项、W24当前actor18项在新助手上实际执行，覆盖末名保护、双向竞争、旧RR/Serializable、保存点、取消及隔离已填数据前向迁移。它们是受影响接缝回归；本次产品没有新增迁移。TenantAdminService14和TenantController5既有单测验证业务兼容。79为同一次去重结果，不累加前两次。

HTTP使用实际JWT验证、平台过滤器、Tenant/Csrf/业务异常和TenantController，原生PG18自有实例；不是完整Program、浏览器或真实DEV业务验收。原构建的依赖漏洞/空引用警告保留，未宣称修复或新增扫描通过。后续只有报告/状态改动时复用以上产品验证，不因提交SHA改变重跑。

## 一次作者集中 SPEC/QUALITY

核完整产品/测试diff、全部ITenantAdminService消费者及共享助手：只涉及本任务；权限检查在业务读写之前；管理员根→当前actor行锁→业务/审计同事务；失败无结果输出且跟踪恢复；跨tenant目标和既有合法合同保持；直接服务测试提供真实fixture主体而无授权绕过；异常403与业务400区别正确。确认新测试失败起因是原业务错误成功，双向竞争使用pg_stat_activity实际锁等待，未用固定sleep推断。

没有新依赖/工作流、机器连接串、产品调试钩子或敏感证据。平台事务会串行化这些低频管理操作；长外层事务仍持锁，未做吞吐压测。当前actor检查仍是W24的平台资格/绑定门，完整认证版本/SSO/2FA/策略在后续范围；本交付不提升完整WP02-04或247运行NOT_RUN。

## 资源与后续

三次自有PG均停止且无postmaster.pid，HTTP/testhost已释放。复用原工作目录、构建和NuGet缓存，零新worktree/代理；原7项用户资料和全部失败证据保留。未运行Actions、操作既有数据库或部署。依次继续W25-02模拟边界与W25-03认证状态；全目标完成后统一组合核对和收尾。
