# W346-01 Stock 首装回执恢复

W346-01 于 2026-11-09 06:50:06 UTC 在固定 DEV-1 开工，分支 `codex/w346-01`，Main 基线 `c5642cd4f0b7400b0db5b388ca6ad92ca8ffed40`。本窗推进 WP02-02 的现有 Stock 存储首装恢复。 [开工记录](../../dispatch/20261109-wave346/README.md) · [原任务卡](../../task-plans/WP02.md#wp02-02)

## 交付

`CP6.TenantStorage inspect-stock-read-v1 --stdin` 复用原首装输入及 PostgreSQL 物理目标核验。在同一只读可重复读事务内核控制方拥有的安装记录、原版本及 UUID、完整的普通 LOGIN/OID/TenantId 映射和目录互斥函数与触发器，再核现有六个读取对象的 FORCE RLS、适用于这些登录的 SELECT 策略及列权限。读取策略限于原控制和租户策略，以及已批准写入扩展的指定表、专用 NOLOGIN 执行者和原谓词。已核验的原安装 ID 可用于原写入安装命令。 [入口](../../../../tools/CP6.TenantStorage/Program.cs) · [核验实现](../../../../CP6.Core/Persistence/Tenancy/StockReadStorageV1.Inspection.cs) · [使用说明](../../../../CP6.Core/Persistence/Tenancy/README.md#stock-owner-read-profile)

`VERIFIED` 返回原安装 ID 与已核物理库身份。`ABSENT` 表示该只读快照中无 Stock read schema 及原读取策略。schema 缺失但原策略仍在时拒绝。`REJECTED` 以固定错误码拒绝目标、映射或布局不符。V1 回执标识该主库的安装，不含调用者操作 ID。重复首装仍拒绝。

## 实际验证

Windows、.NET SDK 8.0.424、PostgreSQL 18.6，隔离端口 55556。使用任务自有目录和两个模拟租户，实际应用 Core 与 IdentityPriority 迁移，运行编译后的真实 CLI 子进程。首装 stdout 流弃置后，新进程查回与数据库持久记录相同的 UUID。用该 UUID 运行原写入栅栏及其余五个已交付写入安装命令，再用完整 A/B 列表核验。实际覆盖未安装、错误数据库 OID、不完整和互换映射、重复首装、额外适用的宽泛 SELECT 策略、停用 FORCE RLS、普通只读角色多出的专用列权限、目录查询函数和写触发器漂移及安装版本篡改。审查补充了回执 schema 改名后仍有原策略的场景，以及目录外普通 LOGIN 在宽泛策略下实际读到两租户库存的场景。最终两者均拒绝。 [真实 CLI 回归](../../../../CP6.Tests/Persistence/Tenancy/StockReadInspectionPostgreSqlTests.cs)

最终 CLI Release 构建退出 0，定向 PostgreSQL 回归 1 项通过、0 失败、0 跳过。TRX 于 07:19:06 UTC 完成，开工到最终回归为 29 分钟。证据根为 `D:/CP6-local-test/CP6-72H-20261109/W346-01`，含 `build-final-review.log`、`test-final-review.log`、`results/final-review.trx`。六个自有夹具均验证停止，55556 已无监听。初次私有还原因本地目录仍使用 HTTP 协议标记失败，修正私有配置后按原签名规则和锁文件还原。保留原还原日志、基线 CLI 退出 2 的 `results/red.trx`，以及停用目录写触发器仍错误成功的 `results/red-directory.trx`。审查两项反例在修正前的实际错误成功保留于 `results/red-review.trx`。基线构建已有警告在最终构建继续可见。

在已按锁文件还原至同一私有构建目录后，可从任务工作树运行以下验证。测试自动生成临时登录凭据。

```powershell
$runRoot = 'D:/CP6-local-test/CP6-72H-20261109/W346-01'
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:NUGET_PACKAGES = "$runRoot/n"
$env:CP6_PERMISSION_PG_ROOT = "$runRoot/permission-consumption-repro-" + [guid]::NewGuid().ToString('N')
$env:CP6_PERMISSION_PG_BIN = 'C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_PORT = '55556'
$env:CP6_TENANCY_CLI = "$runRoot/build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll"
& "$env:DOTNET_ROOT/dotnet.exe" build tools/CP6.TenantStorage/CP6.TenantStorage.csproj -c Release --no-restore --artifacts-path "$runRoot/build" -m:1 -nodeReuse:false -p:UseSharedCompilation=false
& "$env:DOTNET_ROOT/dotnet.exe" test CP6.Tests/CP6.Tests.csproj -c Release --no-restore --artifacts-path "$runRoot/build" -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~StockReadInspectionPostgreSqlTests'
```

本窗验收对象为正式 CLI 的已有 Stock profile 回执恢复。逐租户业务存储、逐租户迁移与备份恢复及原 PGT 正式验收仍按 WP02-02 各自范围执行。Actions 触发、取消、工作流变更及部署为零。

分支保存在本地候选提交，DEV-1 交总控审查、合并和正式释放。工作区保留总控原领取记录。本执行窗的自有进程已全部停止。
