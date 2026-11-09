# W355-01 Stock 普通登录与池容量实测

2026-11-09 08:11:56 UTC 在 DEV-2 开工，分支 `codex/w355-01`，登记基线 `310b99df8c41ca33e846a706c1025bab63f85404`。本窗测量 WP02-02 的固定共享 schema Stock 读取适配器。 [原验收 PGT-023](../../acceptance/postgresql-tenancy.json#L642)

## 实际路径

Windows 20 个逻辑处理器、15.8 GiB RAM，.NET SDK 8.0.424 和运行时 8.0.30，PostgreSQL 18.6。复用 W346 构建提交 `92c64e8bb26aaae3f77382f5b56ba7e636870dd0` 的 Core、WebApi、Tests、CLI 和锁定依赖 Npgsql 8.0.8、EF Core 8.0.30、CP6.Platform 0.10.3。从该提交到本窗基线，实际 Stock 读配置、模型、连接管理、读取安装分支和依赖未变。本窗只编译独立测量程序。 [测量程序](../../../../eng/wp02-02/stock-capacity/StockCapacity.cs)

在 55564 串行启动两个新集群，各运行实际 Core 和 IdentityPriority 迁移。核验原夹具所有权、数据目录与端口后，以 `max_connections=160` 重启本窗私有实例，实测保留 3 个超级用户连接和 `shared_buffers=128MB`。每租户植入一条精确到八位小数的模拟 Stock 行并生成独立普通 LOGIN。两个集群分别通过正式 CLI 首装和 inspect 恢复同一安装 ID，并核实实际 OID 到 TenantId 的映射和六个 RLS 策略。控制身份只做准备、真实 authority 事务和观察。实际数据读取经 `StockReadTenancyConfiguration.Add`、`TenantStockReadStore.ReadAsync` 与普通租户连接执行。模拟输入提供上游已授权的 actor/HTTP 声明，测量范围从 Stock 读取适配器开始。 [复用路径](../../../../CP6.Core/Persistence/Tenancy/TenantStockReadStore.cs)

## 结果

| 实测 | N=10 | N=100 |
| --- | ---: | ---: |
| 目录普通角色 | 10 | 100 |
| 目录关系及索引占用 | 56 KiB | 88 KiB |
| 首次读后立即热复用相同物理 PID | 10/10 | 100/100 |
| 冷登录总时长 p50 / p95 | 129.8 / 1422.4 ms | 128.0 / 151.6 ms |
| 即时热读总时长 p50 / p95 | 47.2 / 92.0 ms | 47.4 / 62.4 ms |
| 全量再扫总时长 p50 / p95 | 46.3 / 74.6 ms | 129.0 / 152.8 ms |
| 全量再扫复用原热读 PID | 10/10 | 0/100 |
| 单进程保留池 / 连接配额 | 10 / 20 | 32 / 64 |
| 单进程观测普通后端峰值 | 11 | 33 |
| 第三个同登录读到达查询回调的时长 | 242.1 ms | 255.1 ms |
| 合法读取成功数，含受控并发 | 33/33 | 368/368 |

两个档位各实测一个共享 Stock EF 模型，均有 233 个实体类型。两库的非系统 `pg_class` 对象各 2720，其中 Stock profile 两个专有 schema 合计 6 个。主测量进程的工作集分别由约 214 增至 260 MiB、由约 219 增至 269 MiB。原件同时保存进程私有内存与托管堆，口径为进程采样。冷读含各进程首次模型成本。总时长含本地 authority 事务，等待时长还包括实际连接借用与读前身份核验。

- 同一登录先用两条真实普通连接持有事务，第三个借用在至少 200 ms 的受控窗口内持续等待。释放后第三个读复用其中一个 PID。
- N=100 单进程实际持有 32 个不同租户池，`pg_stat_activity` 在压力点看到 32 个普通连接和同库 66 个客户端后端。第 33 个新租户读在 56.3 ms 返回 `CapacityExceeded`。释放原事务后原读成功。该预期拒绝与合法读错误率分别计。
- N=100 的第二个进程在测得 1,368,850,432 bytes 可用物理内存后启动。两个不同进程各实际持有 8 个读事务，合计 16 个不同的普通 PID 同时出现在 PostgreSQL。该联合阶段观测 40 个普通后端、同库 58 个客户端后端，两个进程的池分别保留 32/64 与 8/16，且各实测一个 Stock 模型。配置预算 `2×64` 属于上限，联合负载实测止于上述峰值。
- 129 项紧凑目录的 CLI 输入长 10,862 字符，实测 exit 1、`NOT_APPLIED`，正常宿主配置同样拒绝。1000 项输入长 82,284 字符，在 CLI 文档长度边界被拒绝，宿主也拒绝。当前 CLI 和宿主均最多接纳 128 项。1000 租户运行不可准入，本窗容量观测止于 100。

## 复现与收尾

以包含 build 与 n 的 W346-01 本地任务根运行下述脚本。`OutputRoot` 为新建的本任务专有证据目录，使用独占端口。每次调用创建一个新集群。

```powershell
$measure = 'eng/wp02-02/stock-capacity/Measure.ps1'
$common = @{
    ArtifactRoot = $w346Artifacts
    OutputRoot = $taskRoot
    DotnetHome = $dotnet8
    PostgreSqlBin = $pg18Bin
    Port = 55564
}
& $measure @common -TenantCount 10 -Processes 1
& $measure @common -TenantCount 100 -Processes 2
```

原件位于 `D:/CP6-local-test/CP6-72H-20261109/W355-01`。成功目录为 `n10-p1-197c0889` 和 `n100-p2-e1290d07`，各含 `observations.json`、编译和运行日志及 PG 自有停止记录。N=10 初次运行把写入扩展的回执字段误用于读取回执，保留该次失败 `n10-p1-8fbb3cf7`，按原 `InstallationId` 修正后使用新夹具通过。N=10 的成功结果先于 N=100 的双进程余量门调整。最终实测原件于 08:27:39 UTC 落盘，距开工约 15 分 42 秒。

两个成功实例与初次失败实例均已停止。普通连接排空为零，55564 无监听，本任务进程为零。W343 已有实际凭据轮换证据在其原范围内沿用。运行中凭据排空、1000 档运行、超过已观测的联合负载、schema-per-tenant 业务模型及生产容量留在原验收范围。Actions 触发、取消、工作流变更及部署均为零。 [W343 原证据](../W343-01/DELIVERY.md)

本地候选分支与干净 DEV-2 交 COORD 审查和远端交付，席位待正式释放。
