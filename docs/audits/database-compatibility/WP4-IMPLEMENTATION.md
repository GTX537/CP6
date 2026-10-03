# DB-COMPAT-01 WP4：身份与核心业务双库适配

日期：2026-10-03。父任务[Issue #134](https://github.com/GTX537/CP6/issues/134)继续Open。工作包依据[已接受计划](../../superpowers/plans/2026-10-02-database-compatibility.md)，从已确认远端main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32` 建立分支 `codex/db-compat-wp4-20261003`，工作树 `D:\CP6\tmp\worktrees\db-compat-wp4-20261003`。当前实施开始，尚无本阶段业务通过结论。

WP3已由[PR #146](https://github.com/GTX537/CP6/pull/146)正常交付，候选`4ddc6c9d8d75f47711fce1c5e56fbb0ac8733e21`为远端main的ancestor，完整树`3755dbec61fd5f4579327302009a35dce65ff351`相同。合并后同一已验证API再次拒绝普通PG运行、非零退出、未观察到HTTP；[交付/冒烟原件](wp3-native/post-merge/manifest.json)保留七份原始记录，原WP3 manifest不改写。WP3真实门禁按原来源复用，不因SHA变化重跑。

根工作区main仍为`605246ca`：Git因原有未提交的`CP6.Space.IntegrationTests/packages.lock.json`拒绝fast-forward。其SHA256 `71D6B6B9EC4446E92D25842310633AA9E49E994EC6211A353B5140251B1870A5`保持，未stash、暂存或覆盖它及其他未跟踪内容。WP4直接使用最新远端main基线，不在根main开发。

## 执行顺序与当前切口

| 顺序 | 范围与实际入口 | 当前状态 |
| --- | --- | --- |
| 1 | OIDC grant/logout一次消费、真实RefreshTokenService轮换/family登出互斥；同时接通IdentitySnapshotWriter撤销与priority同事务链 | 两个测试子任务准备最小PG RED入口，生产适配方案定向核对中 |
| 2 | 身份bootstrap/变更捕获、service-token撤销、priority/普通Outbox投递；保持SaveChanges/savepoint失败回滚 | 等首段基础接入后连续实施 |
| 3 | ERP request/replay/报价转订单/bridge及收据、审计、结果Outbox原子性 | 后续实施；bridge外部hook独立提交不能机械整段重试 |
| 4 | WMS库存/台账、采购callback、OA/WF后台领取、财务等相关EF业务回归 | 后续实施；保持领域入口、权限与租户范围 |

首个OIDC切口复用24个独立连接兑换同code、仅一个成功的原断言；新公共fixture选择显式Provider，对本任务已创建的独占库实际迁移，SQL历史专项另保留。首个身份切口复用`business-save-produces-valid-versioned-snapshots`的实际SaveChanges/snapshot/Outbox主体，PG应先真实暴露旧SQL-only writer。先保留原始失败，再改生产，不以编译失败代替行为RED。

生产OIDC接口保留`ICrmOidcGrantStore`及旧`SqlCrmOidcGrantStore(string, identity?)`构造，新增显式`(DatabaseOptions, string, identity?)`入口；旧入口缺省SQL。数据库选择不能猜连接字符串。刷新与登出保持同一family锁顺序，PG Serializable的旧snapshot冲突必须由事务所有者完整回滚/新事务处理，不能在失败事务内重试语句。SQL原业务日期和UTC字段、绑定比较及错误语义按原行为保留。

## 本地环境与验收边界

已新建两库独占`CP6Compat_WP4_20261003_032b2139`，有本任务owner标记；连接/凭据只在根`tmp/db-compat.wp4-owned.json`，不提交。测试通过显式Provider、测试连接与owner环境变量使用这些库，配置缺失/不可达/迁移失败直接失败，不Skip、不接入现有业务库。root统一安排串行.NET构建；同一数据库上的不同suite不并发执行。

当前只完成数据库创建及测试入口准备；未执行WP4业务验收。后续保留真实服务器/源码/加载程序集、命令、失败与通过原件，适用输入不变的WP1–WP3结果按原范围复用。普通PG API/worker运行guard仍保留，WP4/5/6和父任务均未完成。未运行或取消Actions、修改工作流/保护、替换现有环境或部署生产。
