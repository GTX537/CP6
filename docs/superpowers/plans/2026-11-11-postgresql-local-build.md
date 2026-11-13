# Main PostgreSQL 本地构建入口 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax. 按本仓精简规则单对话执行，一项任务一个分支、一次集中审查；不自动派生代理或逐小步骤重复交付。

**Goal:** 将W482已经实际使用的PostgreSQL组件构建范围变成可复现、明确选择的本地入口，使当前Main源码及真实WebApi/PG测试可在受控内存中构建。默认构建和SQL Server历史资产保持其原有范围。

**Architecture:** 只增加显式MSBuild属性`Cp6PostgreSqlDevelopment=true`。该属性在Core排除历史`Migrations/**/*.cs`，在Tests排除直接依赖这些历史类的四个测试文件；实际PostgreSQL迁移继续由`CP6.Persistence.PostgreSql`提供。一个本地PowerShell入口设置任务私有产物/PG目录并执行相关构建和真实Owner/HTTP冒烟。不得依赖私有W482 targets或修改生产数据库选择逻辑。

**Tech Stack:** .NET 8；现有EF Core/Npgsql/PostgreSQL 18；PowerShell；现有Main原生PG测试夹具。

---

计划状态：**W526-01 已实施并验证**。已加入显式属性与本地入口；Tests（包含当前 WebApi）及兼容性 Probe 编译通过，18项相关原生测试通过。1 GiB heap 在分析器阶段失败后改用2 GiB，未关闭分析器；默认编译输入及全部253个 PostgreSQL 编译项保持。实际结果见[W526报告](../../development/evidence/W526-01/DELIVERY.md)。以下保留原计划；它不证明默认全量构建或生产候选通过。[原构建范围](../../development/evidence/W482-01/DELIVERY.md)、[W490结算](../../development/evidence/W490-01/DELIVERY.md)。

## 任务与准入

建议窗口`W492-01`，仍归原WP02/受影响构建前置，不新增产品任务。实际执行前先查该ID/分支/开放PR；已有同任务时从原任务恢复，不能新建重复工作。

1. 从最新已确认Main创建`codex/w492-01`，官方`status/start`复用DEV-1。Main主目录有用户未跟踪资料，始终保留。
2. 修改范围只有以下两个现存项目、一个新增本地入口及必要说明/唯一报告；台账及四状态在同一窗口同步。
3. 本任务不升级Platform包，不修#844、不改迁移SQL/数据库模型、默认Provider、租户/权限或运行时行为。

```powershell
& D:/CP6/tools/worktrees/manage.ps1 status
git -c core.longpaths=true -C D:/CP6 fetch --no-tags origin main
$buildBaseline=(git -c core.longpaths=true -C D:/CP6 rev-parse origin/main).Trim()
& D:/CP6/tools/worktrees/manage.ps1 start --repository CP6 --task W492-01 --owner COORD --slot DEV-1 --branch codex/w492-01 --baseline $buildBaseline
```

预期：最新基线、独占DEV-1且只有一个规范分支。配额满时排队；不得为本地构建另复制源码或创建额外树。

## 实施与验证

- [ ] **复用真实失败，核精确输入。** W482完整Core编译在历史迁移设计器处OOM；私有`main-component.targets`随后排除Core历史迁移和以下四个测试，相关真实Main HTTPS/CRM链已通过。先核这些输入与当前源码是否变化，不重复制造同一OOM，也不将组件结果称默认全量成功。
- [ ] **在`CP6.Core/CP6.Core.csproj`加入显式条件。**

```xml
<ItemGroup Condition="'$(Cp6PostgreSqlDevelopment)' == 'true'">
  <Compile Remove="Migrations/**/*.cs" />
</ItemGroup>
```

只在显式属性为true时生效。`CP6.Core/Persistence/DatabaseMigrationProfile.cs`的PostgreSQL分支当前返回`CP6.Persistence.PostgreSql`；该项目、其全部PG迁移及正常项目引用必须继续编译。保留现有SQL Server依赖、迁移原文件、快照及默认构建输入，不把本任务扩成旧实现退役。

- [ ] **在`CP6.Tests/CP6.Tests.csproj`加入同一条件。**

```xml
<ItemGroup Condition="'$(Cp6PostgreSqlDevelopment)' == 'true'">
  <Compile Remove="Persistence/OrderModelForeignKeyRepairTests.cs;Persistence/OrderModelIndexRepairTests.cs;Persistence/QuotationAuditColumnCapacityRepairTests.cs;Space/SpaceRetryLeaseMigrationTests.cs" />
</ItemGroup>
```

这四项只在此PG开发构建中不编译，报告明确列为范围外；不得记PASS、删原测试、扩大排除到整个Persistence或Space目录。其余当前测试仍编译。

- [ ] **新增`eng/development/Test-PostgreSqlDevelopment.ps1`。** 入口参数明确要求`PostgreSqlBin`、私有`PostgreSqlRoot`、私有`ArtifactsRoot`和`Port`；`DotNetPath`可传入，默认`dotnet`。路径必须绝对且不能指向主工作目录/既有业务数据；沿用原夹具的目录/Owner/端口检查，不自建第二套数据库资格协议。原生程序非零立即失败，日志/TRX保留到指定私有目录。

入口把`DOTNET_PROCESSOR_COUNT=2`、`DOTNET_GCHeapHardLimit=0x40000000`、`DOTNET_gcServer=0`和`MSBUILDDISABLENODEREUSE=1`限在本次进程；构建使用`--disable-build-servers -m:1 /p:UseSharedCompilation=false /p:BuildInParallel=false`。所有构建/测试参数带`-p:Cp6PostgreSqlDevelopment=true`及同一`--artifacts-path`，禁止复用其他属性的默认bin/obj。无需新NuGet依赖或锁文件变更。

入口按顺序执行以下已有项目与行为；下面命令也可独立重现：

```powershell
$env:DOTNET_ROOT='C:/Users/tt/.dotnet'
$env:CP6_PERMISSION_PG_BIN='C:/Program Files/PostgreSQL/18/bin'
$env:CP6_PERMISSION_PG_ROOT='D:/CP6-test-artifacts/W492-01/pg'
$env:CP6_PERMISSION_PG_PORT='55571'
$env:DOTNET_PROCESSOR_COUNT='2'
$env:DOTNET_GCHeapHardLimit='0x40000000'
$env:DOTNET_gcServer='0'
$env:MSBUILDDISABLENODEREUSE='1'
$profileArtifacts='D:/CP6-test-artifacts/W492-01/build'
$dotnet='C:/Users/tt/.dotnet/dotnet.exe'
& $dotnet build CP6.Tests/CP6.Tests.csproj --artifacts-path $profileArtifacts -p:Cp6PostgreSqlDevelopment=true --disable-build-servers -m:1 /p:UseSharedCompilation=false /p:BuildInParallel=false
& $dotnet build tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj --artifacts-path $profileArtifacts -p:Cp6PostgreSqlDevelopment=true --disable-build-servers -m:1 /p:UseSharedCompilation=false /p:BuildInParallel=false
& $dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --artifacts-path $profileArtifacts -p:Cp6PostgreSqlDevelopment=true --filter 'FullyQualifiedName~Stock_outbox_committed_original_has_durable_notification_even_when_commit_ack_is_lost|FullyQualifiedName~Actual_Program_rejects_old_access_after_password_change' --logger 'trx;LogFileName=postgresql-profile.trx' --results-directory D:/CP6-test-artifacts/W492-01/results
```

执行入口必须在开工时检查55571未占用；若已占用则选择并记录新隔离端口，不停他人进程。`CP6.Tests`引用WebApi/Core/PG依赖；真实`AuthenticationApiProcess`从本次WebApi程序集启动，不能指回旧发布目录。上述过滤不调用仍受#844阻塞的发布Worker。预期两种Stock提交确认场景和一个实际Program权限冒烟全部成功、0跳过；任何必要编译或行为失败先分析再修。

- [ ] **核默认范围与产物身份。** 用MSBuild的`-getItem:Compile`读取默认与显式PG属性的Compile列表：默认仍含原历史迁移和四个测试，PG属性恰好排除上述范围且保留全部PG迁移/当前Owner；不新增镜像实现的单元测试。记录本次源码SHA、属性、SDK、PG和实际加载程序集路径。无需为这项构建任务运行SQL Server或无关全仓业务回归。
- [ ] **集中审查及交付。** 完整diff集中核默认兼容、PG迁移程序集、条件范围、日志和异常传播；四状态/台账仅同步本任务真实范围。正常PR/合并后确认远端采用，复用相同产物的成功结果，不因SHA改变重编。

## 出口与后继

唯一报告`docs/development/evidence/W492-01/DELIVERY.md`列实际构建属性、成功/失败与四项历史测试的明确未运行范围。停止自有PG/HTTP/编译服务，按精确HEAD执行官方`finish`，报告分支处置、工作区状态及席位释放。

该入口只有在相关构建、真实PG Owner/Program冒烟及集中审查成功、远端Main采用后才能成为[Stock正式包消费计划](2026-11-11-stock-outbox-0.10.4-adoption.md)的构建前置。它不能证明默认全量构建、正式生产候选或PGT-021/022通过。
