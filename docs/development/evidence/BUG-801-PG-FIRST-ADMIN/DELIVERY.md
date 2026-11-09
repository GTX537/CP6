# BUG-801 · PostgreSQL 首管理员与基础导航初始化

2026-11-09 从 Main `d109257495d64d18fddd181b7af7fb29d1e7a3ef` 领取。DEV-1 独占 `codex/bug-801-pg-first-admin`。 [P0 #801](https://github.com/GTX537/CP6/issues/801)

## 根因与修复

空 PostgreSQL 运行真实 `Startup:Mode=DatabaseInit` 时，迁移先建立菜单。旧启动程序把首管理员建档放在菜单为空的分支内，导致用户表为空，随后 PUR 审批种子失败。原 RED 实库保留 31 个菜单、1 个默认 RoleId=1、0 个用户。原基础编号的唯一交集是 2，实际指向 `/erp/currency-policies` 和 `erp-currency-policy`。

PostgreSQL 首次引导在通用角色安全网前执行。事务取得现有认证写入根后，用全库用户及系统默认租户的当前事实判定资格。账户沿原首装口令，直接保存 BCrypt 哈希，启用状态和平台管理员身份一次写入，由原生管理员保护触发器约束。新默认角色、原基础导航及其关系与首管理员同事务提交。任一用户已存在即跳过首装，已有同名用户也不会按名字提权。已存在的有效默认角色可用于中断续跑，其既有导航决定新账户可见范围。

仅首建默认角色时增加基础绑定。迁移或操作方预置的菜单按真实路由、父级和权限键核身份，匹配时保留定义并建立新角色绑定。固定动作锚点错配会拒绝首装。仪表盘按实际路由和键复用，编号 2 被占用时取首个空闲且不占当前固定种子范围的正数 ID。菜单翻译使用 `nav.<实际 ID>`，由现有逐键种子保留已存译文。 [首管理员事务](../../../../CP6.WebApi/Seed/InitialAdministratorSeed.cs) · [实际启动入口](../../../../CP6.WebApi/Program.cs)

## 本地验证

Windows、.NET SDK 8.0.424、实际 PostgreSQL 18.6。每轮使用任务新隔离 PostgreSQL 根和新库。回归从真实 WebApi 子进程执行 `DatabaseInit`，在 Development 隔离配置中运行正常迁移及业务种子。使用明确模拟业务数据和临时私有凭据。

- RED `database-init-red.trx` 为 1 失败、0 跳过。旧版实际应用最新 Core 迁移后在 `PurApprovalFlowSeed` 抛出原错误。红库菜单编号与身份由原生 PostgreSQL 查询实证。
- 首轮 `database-init-green.trx` 为 2 通过、1 失败、0 跳过。空库已完成实际初始化，权限聚合缺少 `dashboard`，据实修正编号冲突。既有角色的中断续跑及其他租户已有用户的保护通过。
- 最终 `database-init-green2.trx` 执行 4 项，4 通过、0 失败、0 跳过，测试时长 8 分 38 秒，dotnet 退出 0。

实际回归验证首装、同库二跑、既有启用和禁用同名账户、跨租户已有账户、默认角色已存在的续跑、固定菜单冲突以及原生插入故障后的原子回滚和同库重试。检查真实聚合权限、仪表盘实际编号及翻译、两个 PUR 审批绑定和原生最后启用平台管理员保护。 [真实子进程回归](../../../../CP6.Tests/Persistence/PostgreSqlFirstAdminInitializationTests.cs)

本机原始证据位于 `D:/CP6-local-test/CP6-72H-20261109/BUG-801-PG-FIRST-ADMIN/` 的三个运行日志、TRX、各轮私有 PostgreSQL 根和子进程日志。原失败及预期拒绝均保留。最终实际子进程入口的 SHA-256 为 `92a7898d626dbb2875b079247def8f2572e48414657a09632e031c530faf5fa8`。三个专属 PostgreSQL 实例及 WebApi 子进程均已停止，端口 55443、55444 无监听。任务 Actions 触发和取消为零。生产候选及部署仍按既定 R2 门禁记录。交付时本地候选与 DEV-1 交 COORD 正常集成及释放。

## 复现

```powershell
$bug801RunRoot = 'D:/CP6-local-test/CP6-72H-20261109/BUG-801-PG-FIRST-ADMIN'
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:CP6_PERMISSION_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_PORT = '55443'
$env:CP6_PERMISSION_PG_ROOT = "$bug801RunRoot/permission-consumption-$([guid]::NewGuid().ToString('N'))"
& "$env:DOTNET_ROOT/dotnet.exe" test CP6.Tests/CP6.Tests.csproj -c Release --artifacts-path "$bug801RunRoot/build" --filter 'FullyQualifiedName~PostgreSqlFirstAdminInitializationTests' -m:1 -nodeReuse:false -p:UseSharedCompilation=false -p:RestoreLockedMode=true
```
