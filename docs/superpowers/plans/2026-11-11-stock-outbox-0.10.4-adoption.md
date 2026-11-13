# Stock Outbox 0.10.4 正式采用 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax. 本仓由COORD单对话串行；一次任务级集中SPEC/QUALITY审查，按实际调用影响选测试，不派生代理或逐小步重跑全套。

**Goal:** 让现有合法LF/CRLF guard下的原Main Stock Pending消息通过真实Worker完成派送，同时拒绝偏离已知正文的guard；远端采用后关闭[Main #844](https://github.com/GTX537/CP6/issues/844)。

**Architecture:** 采用同一批准源码构建、正式签名/时间戳、feed原字节回读及两系统验签的七包0.10.4；Main只更新实际四条直接PackageReference及三个锁文件，沿原Stock Worker/Owner/回执协议验证。生产迁移中的原guard不改写。完整v11恢复和PGT-021/022仍按其原义务独立验收。

**Tech Stack:** PostgreSQL 18、.NET 8/EF Core/Npgsql、现有Main Stock Outbox V2与RMA原生CLI、NuGet正式feed和受保护P10发布入口。

---

计划状态：**W526-01 已实施并验证**。用户另行授权的一次正式发布 [43378516039](https://github.com/GTX537/CP6.Platform/actions/runs/43378516039) 已成功；Main 实际四条直接引用及六份受影响锁文件升级，18项原生 Stock/Program 回归通过，同一 W477 原 Pending 消息实际完成派送。[W526报告](../../development/evidence/W526-01/DELIVERY.md)记录集成及 Issue 处置；以下 W490 输入和检查清单保留为原实施计划。本次授权不覆盖其他 Actions 或部署。

## 已核实输入

- Platform远端main：`f3fbe4be67d40641f7a8a969d74be65a95541819`。#80修完整LF/CRLF guard比较，#81注册精确0.10.4；W490 [#83](https://github.com/GTX537/CP6.Platform/pull/83)修本地Contract旧测试，完整相关脚本已实际通过，Issue #82关闭。
- 对已发布0.10.3源码`1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`，Main实际消费的Abstractions/AspNetCore/Contracts/EntityFramework/Messaging源码仅`OutboxV2/OutboxPostgreSqlV2.cs`有7增2删；其他四个运行库源码未变。Release的0.10.4注册是另一明确范围，不能推导新的ctx-binding能力资格。
- 七个GitHub Packages全部版本页在2026-11-12T00:02:09Z均无0.10.4。缺少正式feed包是实际前置；不存在“必须等tag”的要求，当前正式入口使用main+精确SHA。
- `p10-formal-release`仍要求reviewer及main分支策略；两项Secret名称存在，公共readiness变量为true。CRM/System信任策略摘要均为`da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f`，与提交策略字节一致；证书指纹均为`1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d`。Secret名称/公共变量存在不证明私钥匹配，原受保护运行必须再次实际预检。
- Main目前仍锁0.10.3。W405原真实Stock提交成功、消息Pending、Worker返回`CP6_OUTBOX_GUARD_MIGRATION_REQUIRED`，发送0次；原guard保留LF。原失败和W403单独Published证据各自保留，不互相冒替。
- Main当前完整编译的历史设计器OOM有实际记录。消费前须先采用[独立PostgreSQL本地构建任务](2026-11-11-postgresql-local-build.md)；不能把私有组件构建或旧Main DLL称为当前默认完整构建。

## 1. 正式发布：单独授权的外部写入

- [ ] 在执行时重新确认Platform远端main仍为上述SHA、七个feed版本均未占用、原保护和信任未改变。源码若变化，先更新具体发布请求，不能用旧批准运行新源码。
- [ ] 向用户提交**一次**具体请求：仓库`GTX537/CP6.Platform`，工作流`.github/workflows/p10-formal-packages.yml`，ref`main`，`expected_commit=f3fbe4be67d40641f7a8a969d74be65a95541819`，`version=0.10.4`。只包括Windows签名/RFC3161/七包发布回读和随后Linux独立验签，不包括其他Actions或部署。

预计为**10–30分钟执行预算**；最近成功运行总历时5分06秒，另一次25分30秒，仅作估计依据。源码超时为Windows45分钟+Linux20分钟，合计最多65 runner分钟；审批/排队另列，不把超时上限称实际计费。授权原因来自`D:/CP6/AGENTS.md`：“未经用户新的明确授权，不得主动触发或重跑 GitHub Actions”；手动入口、既往发布和本地成功都不是本次批准。

仅在用户明确同意且准入成立后执行：

```powershell
gh workflow run p10-formal-packages.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=f3fbe4be67d40641f7a8a969d74be65a95541819 -f version=0.10.4
```

- [ ] 保存本次精确run ID/attempt/source/event及Environment批准记录，跟踪该运行的`sign-publish`和`verify-linux`。等待时继续独立的Main构建准备；不得改保护、自批跳过必要检查或把Secret值取到聊天/本地。
- [ ] 任何发布/回读失败按原版本消耗标记处理，不自动重跑或覆盖0.10.4。特别是已经写入`p10-formal-version-consumed`之后的失败，保留原证据和feed现状，交独立前向版本决定。

成功出口为同一源码/版本的七包及最终`formal-package-publication.v1.json`，包含实际Windows和Linux验证。仅Windows成功、包名出现或源码PR合并均不够。

## 2. 接收不可变发布结果

- [ ] 获取本次run中`p10-s04-final-publication-f3fbe4be67d40641f7a8a969d74be65a95541819-1`和对应Windows read-back artifact；若attempt不是1，使用实际记录的attempt，不能泛取“latest”。先核artifact API的SHA-256与下载ZIP原字节一致，再解压；保留原ZIP和原五份最终记录，不改写原证据。
- [ ] 用同一Platform源码的`tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj`及其现行CLI验证实际publication、提交的trust store/certificates和当前UTC评估时间。W490相关脚本结果只证明本地合成边界，不替代此次正式结果；Main历史0.10.1 ReleaseTool不升级，也不拿它验证0.10.4。
- [ ] 按现有`Test-P10FormalPackageSet.ps1`核七份真正feed回读包：版本、源码、签名者、RFC3161、逐包hash和一致发布记录。不能在本地重打包/重签后冒feed原字节；正式哈希/签名要求保留。

## 3. Main消费窗口及文件边界

建议窗口`W493-01`；开工时重新查ID/分支/PR和直接前置。W492已完成并释放后，官方status/start从最新已确认Main建立`codex/w493-01`并复用DEV-1；DEV-2保留CRM缓存。时间估算为一个2–4小时实现/验证窗口，按真实出口重排，发布审批耗时不算该窗口已实施。

只更新以下**四条直接引用、三个项目**的`[0.10.3]`到`[0.10.4]`：

| 现存项目 | 直接PackageReference | 锁文件 |
|---|---|---|
| `CP6.Core/CP6.Core.csproj` | Messaging、EntityFramework | `CP6.Core/packages.lock.json` |
| `CP6.WebApi/CP6.WebApi.csproj` | AspNetCore | `CP6.WebApi/packages.lock.json` |
| `tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj` | EntityFramework | 同目录`packages.lock.json` |

其他运行库为传递依赖；按真实新包图更新，不能虚构为五条直接引用。保留`eng/p10/NuGet.formal.config`的require-signature、唯一CP6源映射及现行签名者。普通依赖复用有效缓存；新CP6包必须来自实际正式feed，记录其原nupkg与publication hash的对应。凭据用已有受控注入，不写入命令、Git文件或日志。

```powershell
$dotnet='C:/Users/tt/.dotnet/dotnet.exe'
$stockArtifacts='D:/CP6-test-artifacts/W493-01/build'
& $dotnet restore CP6.WebApi/CP6.WebApi.csproj --configfile eng/p10/NuGet.formal.config --force-evaluate --use-lock-file -p:Cp6PostgreSqlDevelopment=true --artifacts-path $stockArtifacts
& $dotnet restore tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj --configfile eng/p10/NuGet.formal.config --force-evaluate --use-lock-file -p:Cp6PostgreSqlDevelopment=true --artifacts-path $stockArtifacts
```

逐条核真实lock差异和hash，随后locked restore/构建依赖。五个运行库不能混入旧0.10.3或TaskLocal包；若新的包元数据与原合同不兼容，保持Pending并记录失败，不改证据或放宽版本门。

## 4. Main Worker实际红绿及边界

- [ ] **复用原红证据。** W405的原始Main/0.10.3/LF失败只按其原输入采用，完整保留；不将新的合成失败改称W405。新的Main测试先在原0.10.3图复现，再在正式0.10.4图验证；若构建输入未改变且原失败能精确适用，可以直接引用原红而不重复制造。
- [ ] **新增`CP6.Tests/Erp/StockOutboxGuardPostgreSqlTests.cs`**，沿现有`partial OrderPriceCorrectionPostgreSqlTests`复用`Seed`、`GrantStocktakeDashboard`、`SeedInventory`、`StockApply`、`InventoryMover`、`StockHost`、`StockPublisher`、`StockQueue`，不构造第二个业务实现。
- [ ] 同一串行隔离夹具完成三种真实目录状态：完整LF、完整正确CRLF、正文偏离已知完整模板。以`pg_get_functiondef`/`prosrc`保留原定义，仅在测试库替换函数正文行尾或明确负变异；原owner/返回类型/语言/search_path/security/volatility及所有trigger属性保持。每例finally恢复原定义，异常也保留日志；不在原封存源改guard。
- [ ] 正向调用现有真实Worker并回读原消息/可信完成：

```csharp
var publisher = new StockPublisher(OutboxTransportDecisionV2.Confirmed);
var result = await new StockOutboxWorkerV1(h.Db, StockHost(), publisher)
    .RunAsync(h.Tenant, default);
Assert.Equal("RESOLVED", result.State);
Assert.Equal(1, result.Completed);
Assert.Single(publisher.Messages);
await using var queue = StockQueue(h);
Assert.Equal(Cp6OutboxStatus.Published,
    (await queue.Set<Cp6OutboxMessage>().SingleAsync(x => x.TenantId == h.Tenant)).Status);
Assert.NotNull((await queue.Set<StockOutboxRunV1>()
    .SingleAsync(x => x.RequestId == result.RequestId)).CompletedAtUtc);
```

这段使用的是已核实的现存类型/方法，只是测试正文的一部分；仍需在上一步完成真实Owner提交及LF/CRLF夹具建立。负向正文必须返回`CP6_OUTBOX_GUARD_MIGRATION_REQUIRED`、发送0次、原Pending和业务图不变。不能通过禁用触发器、删除属性检查或仅改`p.prosrc`比较为宽松trim/case-insensitive来达成。

- [ ] 从W405已停机的任务专属源副本读回原A/B Pending，用本次实际Main程序集+正式包的驱动完成原键派送；记录原OperationId/MessageId/tenant/root、发送内容、票据和完成回执。源及B历史不覆盖，缺原回执/UNKNOWN仍沿原协议，不能生成第二笔业务替代旧Pending。
- [ ] **相关回归仅一次集中执行。** 新三种guard、现有Stock worker正常/UNKNOWN/三阶段commit-ack丢失/原件不可变约束和`Stock_outbox_actual_Program_worker_delivers_empty_hint_to_authenticated_websocket`。真实Program要加载本次WebApi及0.10.4依赖，按W492入口构建，不借旧DLL。记录实际加载路径、包hash/源码；PG/HTTP/内存发送端明确分开。

## 5. 恢复链及正式状态

原Issue #844完整条件通过、一次集中审查、正常远端Main采用后才关闭Issue。该窗口不自动关闭WP02-02或改PGT计数。

同一v11后继优先复用[W481](../../development/evidence/W481-01/DELIVERY.md)的真实组合与只读对账，不重建相同证据。已核实的入口为`eng/wp02-02/rma-tenant-archive/verify_combined_candidate.py`、`verify_current_v11.py`、`verify_stock_stage.py`和`verify_stock_reconcile.py`；最后一项现有模式是`v10|published`，不能当成现成v11完整恢复测试。其target/用户名/租户/manifest/离线副本必须从实际新候选记录读取，不能指向原源或猜参数。

PGT-021还要求活跃B合法差量、A完整原件/共享依赖、普通角色与批准切换；PGT-022仍需真实当前撤权、全局键/旧lease/UNKNOWN和合法route epoch变化。W481直接改epoch被55000正确拒绝，不是合法轮换通过；0.10.4只修guard兼容，不能以发布包代替Owner合法轮换设计。PGT-024另保混合版本/回退范围。

最后按准确源HEAD审查，检查候选与受保护main工作流/过滤及依赖，普通验证只本地。正常PR合并、核远端采用；停止自有进程，按官方finish清理准确任务分支并释放席位。唯一交付报告`docs/development/evidence/W493-01/DELIVERY.md`列原失败、真实绿色、复用来源、模拟范围、未验证内容，以及分支处置/工作区/席位结果。无生产部署。
