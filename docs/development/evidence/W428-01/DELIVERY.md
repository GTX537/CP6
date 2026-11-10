# W428-01 · IAM 用户成员关系复合引用

DEV-1 于 2026-11-09 20:46:41 UTC 从 Main #860 开工。原 WP02-02 / PGT-027 的共享用户引用范围。新增 PostgreSQL 前向 Core 迁移，在 `Sys_Users(TenantId,Id)` 建唯一约束，并由 `Sys_UserRole(TenantId,UserId)` 引用。外键在提交时检查，采用 `DEFERRABLE INITIALLY DEFERRED NO ACTION`。保留原全局用户主键、成员关系主键和 `(TenantId,UserId,RoleId)` 唯一键。现有模拟登录回归在独立事务暂存合法 A 角色并尝试把 B 用户写入 A 成员关系，核预期约束错误和两类行回滚，然后继续原登录、菜单、cookie、审计和恢复断言。[迁移](../../../../CP6.Persistence.PostgreSql/Migrations/Core/20261109204600_IamUserMembershipTenantReferenceV1.cs) · [现有宿主回归](../../../../CP6.Tests/Platform/ImpersonationPostgreSqlTests.cs) · [原验收卡](../../acceptance/postgresql-tenancy.json)

## 实际原生验证

- 从 C# 迁移提取原 Up SQL，SHA-256 `d45ce77bc103926fc901b43b0a5969c7b3ae1c1eeb709346519db8633e4c8cf0`。新建一份私有 PostgreSQL 18.6 最小相关表夹具，复用原 PG 用户、角色、成员关系列类型和原键。system identifier `7706280060673205256`，shared buffers 16 MiB，监听地址为空。使用 PostgreSQL 单后端逐次执行实际原 SQL。[固定输入](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-inputs/prepared.json) · [PostgreSQL 单后端入口](https://www.postgresql.org/docs/18/app-postgres.html)
- 第一次实际 initdb 退出 0，用时 5.011 秒。运行器按裸 PID 过早核对 Windows 子进程，窗口退出 1，SQL 尚未启动。核原八个进程已退出、`pg_ctl status=3` 和原控制、HBA、配置指纹后，使用 PID 加创建时间与有时限的子进程等待，沿同一停机目录接续。[首次真实结果](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-01/terminal.json) · [接续原件](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-resume.json)
- 第二次六段 SQL 于 21:07:07.775 UTC 退出 0，总耗时 4.650 秒。启动前物理可用 942 MiB，运行低点 922 MiB，提交可用低点 20,041 MiB。预置旧错属关系使真实 `ALTER TABLE` 返回 `23503 FK_Sys_UserRole_TenantUser`，事务结束后两项 DDL 均未留下，旧错属行与原数据一致。对齐的 A/B/A1 行在实际迁移后保留原键，原生目录确认新外键已验证且初始延迟。[实际结果](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-02/actual.json) · [迁移拒绝原件](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-02/02-migration-rejected.native.log)
- 先写成员关系后写用户能同事务提交，先删除用户后删除成员关系也能同事务提交。另一次事务内实际看见合法 A 角色及 A 到 B 的错属成员关系，`COMMIT` 返回同一外键的 `23503`，两类写入全回滚。原 B 和 A1 保持。前后原生停机状态为 3，八个阶段进程均已退出。[提交拒绝原件](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-02/05-commit-rejected.native.log) · [停止结果](/D:/CP6-local-test/CP6-72H-20261109/W428-01/native-sql-02/terminal.json)

## 接续范围

### 定向编译

同一任务分支恢复后，Git 源文件只归一 Windows 换行，实际提取的 Up SQL 摘要与原生验证相同。首轮采样 836–840 MiB，退出 75，编译器未启动。资源回升后，.NET SDK 8.0.424 Roslyn 使用封存 EF Core 8.0.30 引用单编迁移类，PID 28104 退出 0，stdout 和 stderr 均为空。采样与编译合计 1.596 秒，工作集峰值 117.20 MiB。产物 SHA-256 为 `c7bb63c645d33e16b7c0788c09e27af136f2782cf121844788b73dd02a7a5af5`。[编译结果](/D:/CP6-local-test/CP6-72H-20261109/W428-01/migration-build/csc-02/terminal.json) · [首轮采样](/D:/CP6-local-test/CP6-72H-20261109/W428-01/migration-build/csc-01/terminal.json)

### 正常迁移程序集与宿主入口

2026-11-09 22:03:53 UTC 在 DEV-1 恢复同一分支。Main #862 相对原 W428 基线的 Core、PostgreSQL、测试、WebApi、Space Infrastructure 及工作流输入无差异。W430 的同 schema 归属更新子场景已随 #862 采用。W428 继续覆盖正常迁移加载及原 IAM 宿主验证，正式 PGT-027 维持 `NOT_RUN`。[W430 实证](https://github.com/GTX537/CP6/blob/c8db0bc28c78928c2d2fc83dae954ba554a24f81/docs/development/evidence/W430-01/DELIVERY.md)

完整 `CP6.Persistence.PostgreSql.dll` 的响应文件及原件预检已通过。输入为 251 份候选跟踪源码、3 份生成文件和 375 个编译引用，识别出 228 个迁移 ID。引用选择复用 W364 的实际 SDK 解析缓存和封存项目引用，输出使用正常 Core profile 固定的程序集名。[完整程序集输入](/D:/CP6-local-test/CP6-72H-20261109/W428-01/provider-build/inputs.json) · [定向编译入口](/D:/CP6-local-test/CP6-72H-20261109/W428-01/provider-compile.py)

原 IAM 测试的私有 `CP6.Tests` friend 预检已通过。实际 W364 缓存为 174 个不同的锁定名称和 148 个经解析的项目或包引用，其余 26 个由原框架引用补足。前两次预检暴露 TestPlatform 同名记录，已核对三个重复名称的两份文件均字节相同，再按固定顺序选取。固定候选的原夹具、IAM 两个原方法和模拟登录 HTTP 原方法。驱动在任何写入前核实物理库、原 profile 选中的实际程序集和 SHA-256。[IAM 定向输入](/D:/CP6-local-test/CP6-72H-20261109/W428-01/iam-friend-build/inputs.json) · [阶段入口](/D:/CP6-local-test/CP6-72H-20261109/W428-01/iam-friend-build/RunW428.cs)

2026-11-10 00:46 UTC 在 DEV-1 再次恢复。同一编译输入到 Main #864 未改变。`csc-01` 实际编译 IAM friend，PID 52844 退出 0，用时 1.312 秒，stdout 和 stderr 均为空。产物 208,896 字节，SHA-256 `1df5df2ee39d06b562d8f99aa34ac8869ffcb70cc3f8758aed4314f3239cdf8d`。 [实际编译](/D:/CP6-local-test/CP6-72H-20261109/W428-01/iam-friend-build/csc-01/terminal.json)

完整 Provider `compile-01` 在 32.550 秒内取得 18 个样本，物理空闲 1,013–1,133 MiB，可用提交 20,982–21,055 MiB。1,150 MiB 启动检查退出 75，完整编译器、数据库及宿主均未启动。 [原始采样](/D:/CP6-local-test/CP6-72H-20261109/W428-01/provider-build/compile-01/compiler-gate.jsonl) · [退出记录](/D:/CP6-local-test/CP6-72H-20261109/W428-01/provider-build/compile-01/terminal.json)

接续同一分支。持续满足 1,150 MiB 物理内存与 19 GiB 可用提交时，运行 `provider-compile.py compile --attempt compile-02 --seconds 32`，并复用 IAM friend `csc-01`。两份产物就绪后使用 W383 的单一隔离副本，通过原 `TenantManagementPostgresFixture.Context` 正常升级并核对 224 条原 Core 历史及新增的一条记录，再分进程运行 IAM 与 HTTP 回归，独立核对实际数据库写集和停机。原 PostgreSQL 六段与单文件编译继续复用。 [W383 停机原件](/D:/CP6-local-test/CP6-72H-20261109/W383-VERIFY/stop-verification.json) · [正式宿主入口](../../../../CP6.Tests/Platform/TenantManagementPostgresFixture.cs#L52)
