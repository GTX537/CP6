# 第七轮 B 准入建议：WP02-02 私有源码兼容候选

建议开放 **Main 任务私有的完整依赖闭包编译与真实本地运行准备**。工作可以在正式发布批准之前完成，关闭“当前生产者源码与 Main 真实依赖图是否能编译并运行指定路径”的技术未知；**不会解除 WP02-02 的 BLOCKED / NOT_CHECKED，也不会满足 WP02-28**。本报告仅只读评估，没有改仓库、执行构建/测试、访问业务数据库、运行 Actions 或写远端。完整输入指纹及十四个历史包身份见同名 JSON。

## 固定输入

| 输入 | 准确身份 |
|---|---|
| Main HEAD/main/origin/main 本地引用 | `1129953156b7775152a74ad2f51cb95ef430ee56`，tree `fcf79cd38055adddab4a69e4910808c691d442b2` |
| Platform HEAD/main/origin/main 本地引用 | `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`，tree `4ae98434d6e87f3fdd852a156fd057c03e336e43`；读取干净 `D:/CP6.Platform-worktrees/wave6-integration-20261014` |
| 第六轮 Main 准备交付 | source `f395d92ab8e15a5a06d00deeb2f47a9ec583baaf`；[PR206](https://github.com/GTX537/CP6/pull/206)，merge `b526e6b2194c232fae224cad7e72aef1c9b85c9e` |
| 当前 producer 已含 WP02-25 | source `80f43346e4e94e6d7b402d7b557c685b475ba743`；[PR70](https://github.com/GTX537/CP6.Platform/pull/70)，merge `5e882a5e1006ee2eb27fc0fa1170755f552be089` |
| 当前 producer 已含 WP02-08 | source `2e659e382e95988ee06799cd2feb0d580dcd8323`；[PR71](https://github.com/GTX537/CP6.Platform/pull/71)，merge 即 `b28db763...` |
| 原正式包 | `0.10.2`，producer `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`；publication SHA256 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181` |
| 原本地包 | `0.10.3-wp02.27.g7c59256e13c217fc0e8a66b8f3f46905222fdcbc.20261013134250`；consumer-lock SHA256 `1fabaecefb24050d801a9fd894dcd908b8428f7bd486fe4d0a634ad97ffe0b01` |

本评估未 fetch；远端包含性来源是已合入的逐任务证据与当前本地 origin/main。COORD 派工时重新核对远端，从届时已确认 Main 创建独立任务分支。源码兼容输入另行准确固定，派工文档变化不能悄悄替换 producer 身份。

## MSB3277 的实际含义

[原 Probe.csproj](D:/CP6/eng/wp02-02/consumption/probe/Probe.csproj)只有四个裸 DLL Reference；task-private NuGet.Config 没有包源，因此不会恢复 AspNetCore/Messaging 的依赖闭包。[a3 构建日志](D:/CP6/docs/development/evidence/WP02-02/consumption-preparation/runs/probe-20261014-a3/build.log)显示 framework DiagnosticSource 8.0.0.0 被选为 primary，包 DLL 所需 10.0.0.0 未能统一。

Main 的 [WebApi lock](D:/CP6/CP6.WebApi/packages.lock.json:1239) 与 [Core lock](D:/CP6/CP6.Core/packages.lock.json:752) **已解析 DiagnosticSource 10.0.8**，contentHash 均为 `rQHsK7Xr8Uz3449860ayVXp/CaLmrhHlMPxbpT/ibOPtp/dTTsr6+f/SxaGO2NzxHf+0siLE0UfdVN5z1I0EgQ==`。Platform 当前中央依赖包括 OpenTelemetry 1.18.0、Http.Resilience 10.9.0、Dapr.Client 1.18.5；Main lock 中 OpenTelemetry.Api 要求 DiagnosticSource 10.0.0，其他依赖要求 10.0.8。

有证据的结论是“原探针遗漏依赖闭包”，**不能据此判定 Main 同样冲突或现在降级 producer**。下一任务必须真实 restore/build 完整图，并运行 DiagnosticSource/OpenTelemetry 路径。原 `TreatWarningsAsErrors` 没拦住 MSBuild warning，需使用 `-warnaserror:MSB3277`，不能只依赖 C# 编译器属性。

## 可交付实现

建议名称：**WP02-02 Main 私有全依赖闭包编译与 Platform 实际本地运行候选**。归属已有 WP02-02 的新准备窗口，不新增 DONE 数。

1. 新建任务私有 runner、manifest/schema、MSBuild source projection、测试及 loopback host。只从固定 Git 对象导出 Main/Platform，记录 SHA/tree、机械投影补丁、SDK/runtime、config、原锁与实际输出哈希；不读未提交源码或复用共享 bin/obj。
2. 真实恢复/编译 Main `CP6.WebApi/CP6.WebApi.csproj` 与 `tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj` 的**隔离导出副本**。前者覆盖 Core/Space/PostgreSql 闭包，后者覆盖第四个直接 Platform 引用。不执行 Main Program、数据库 Probe 或 worker。
3. 仅在副本把四个 `[0.10.2]` 引用换为固定 producer 快照 ProjectReference：Core 的 Messaging/EntityFramework、WebApi 的 AspNetCore、DatabaseCompatibility.Probe 的 EntityFramework。可用透明 overlay 或可审查机械补丁；原生产 csproj/lock 不变，业务 `.cs` 原字节不变，不删模块或编译项来过检查。
4. 外部包 restore 使用 Main `NuGet.config` **原字节**（SHA256 `23ab29f2a03952fcf859213fe42a1e2c2a8eee933e8f92dd74c18f68f2b7be45`），保 signatureValidationMode=require、source mapping 和既有 signer pins。隔离 cache 和新 graph 的私有生成锁，保留原锁并审查逐项漂移；这些锁不能冒充未来正式 feed 锁。诊断 graph 的所有 Platform 引用必须来自同一 producer 快照，残留旧包或外部 project path 直接失败。
5. 新 Kestrel probe 在 `127.0.0.1:0` 自有端口启动，运行实际 `AddCp6TrustedRequestContextV1`/中间件与 Background/Message executor；verifier/authority 明确为合成。调用当前 WP02-25 `AddCp6ObservabilityV1`、`LocalTelemetrySinkV1` 与实际 SDK，通过真实 HTTP 产生 trace/metric/log，flush 后断言输出、卸载及清理。任务 host 只含自定义无业务 IO 路由，不能启动原 Main 宿主跳过数据库前置。
6. 记录实际加载的五个 Platform 运行程序集以及 DiagnosticSource/OpenTelemetry 等依赖的路径、AssemblyName/version、MVID、SHA256，绑定实际 build/assets/deps；拒绝旧 DLL 混入、missing type/method/load exception。不用自报 manifest 代替观察，不把 DLL hash 当作正式包归档 hash。

相较旧十个合成场景，新增可验收成果是：**两个真实 Main 项目根的 MSBuild 求值、全依赖恢复和编译；当前 b28db76 producer；真实 Kestrel/SDK 路径；完整运行依赖身份及无残留证明**。原 44 checker / 8 parser / 10 synthetic 结果按原范围复用，不能改称新 producer 已通过。

## 写入范围与命令

只准提交 Main：`eng/wp02-02/consumption-compatibility/` 与 `docs/development/evidence/WP02-02/consumption-compatibility/`。前者包含独立 csproj/props/targets、runner/overlay/schema/fixture/tests；后者保存输入、日志、graph/lock delta、产物指纹及报告。

临时写入只在该任务 worktree 的 `artifacts/wp02-02/consumption-compatibility/<new-attempt>/`，含 `snapshots/main`、`snapshots/platform`、cache、bin/obj、私有锁。验证绝对路径归属，拒绝复用旧输出或越界引用。Main 根现有不相关 untracked 文件不能复制、清理、暂存。所有 Platform checkout 只读。

下列为**待实现入口契约**，本评估未执行，也不声称文件已有：

```text
python eng/wp02-02/consumption-compatibility/run.py --main-repo <Main-object-store> --main-sha 1129953156b7775152a74ad2f51cb95ef430ee56 --platform-repo <Platform-object-store> --platform-sha b28db763f70ab6f164cb7c87d6ce49a6d97267f1 --dotnet C:/Users/tt/.dotnet/dotnet.exe --output <new-task-owned-attempt>
python -m unittest discover -s eng/wp02-02/consumption-compatibility/tests -v
```

runner 仅对副本执行并记录展开参数：

```text
dotnet restore <snapshot-main>/CP6.WebApi/CP6.WebApi.csproj --configfile <exact-Main-NuGet.config> --packages <attempt>/nuget-cache
dotnet build <snapshot-main>/CP6.WebApi/CP6.WebApi.csproj -c Release --no-restore -warnaserror:MSB3277
dotnet restore <snapshot-main>/tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj --configfile <exact-Main-NuGet.config> --packages <attempt>/nuget-cache
dotnet build <snapshot-main>/tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj -c Release --no-restore -warnaserror:MSB3277
dotnet restore <private-probe>/Probe.csproj --configfile <exact-Main-NuGet.config> --packages <attempt>/nuget-cache
dotnet build <private-probe>/Probe.csproj -c Release --no-restore -warnaserror:MSB3277
dotnet <private-probe-output>/Probe.dll --manifest <fixed-source-manifest> --output <attempt>/runtime
```

SDK 入口为 `C:/Users/tt/.dotnet/dotnet.exe`，既有证据 SDK8.0.424/runtime8.0.30；执行必须实际记录版本，不默默使用 PATH SDK10。新 source项目不是正式包，不要求伪造它的包签名；外部 NuGet 包签名要求不得削弱。网络/信任失败如实记录，不自动改信任、cache或Actions。

命令与日志仅记录所需显式参数，不导出完整环境变量、凭据、用户NuGet配置、连接串或机器Secret。实际程序集路径用于本地身份核对，提交报告可用经核对的任务根相对路径；不要提交机器专属生产配置。提交前扫描日志及binlog敏感内容；若需发布脱敏副本，保留其脱敏说明及原/副本各自哈希，不把改写副本称作原始日志。

必需门：source/tree/patch/config mismatch、越界引用、旧 Platform 包/DLL混入、缺依赖/MSB3277、实际加载身份不符、HTTP/SDK输出缺失、清理失败的正反回归；相关runner测试、两个项目编译、一次真实probe；收尾一次完整diff审查。必需验证失败不得报告 compatibility PASS。若发现真实产品API或依赖缺口，报告精确文件与诊断，由COORD另开窗口，不能顺手改共享代码。

## WP02-12 / WP02-26 重叠

| 任务 | 实际读取重叠 | 本方案边界 |
|---|---|---|
| WP02-12 | Messaging、Abstractions、Contracts、Messaging.csproj、Directory.Packages.props | B只编译固定b28db76导出副本；不改native schema、Kafka/Dapr、项目依赖。12后续合入是另一producer，不能继承结果；需修共享依赖时交COORD串行。 |
| WP02-26 | Contracts、AspNetCore、Testing/telemetry、中央依赖 | B自有probe及公开LocalTelemetrySink，不改SloEvidenceEvaluator/TelemetryRecorder、schema或shared tests。Testing当前IsPackable=false，不加第八包。26影响输入后，最终freeze重算受影响闭包；无关输入按实际哈希复用。 |

无需占用 Platform:package-trust/message-transport/test-evidence 或写 Main:host-package-adoption 的产品入口；登记 Main 任务私有工具/证据窗口。读取不授予共享编辑权；后续转入真实 csproj/lock/Program/DI 是独立采用窗口。

## 正式权威与状态

当前 Platform formal validator/Capabilities 白名单仍只有0.10.0/1/2，旧正式能力列表必须为空；formal workflow Windows/Linux仍 exact0.10.2。Capabilities的TaskLocal只接受`0.10.3-wp02.27.g<sha>.<attempt>`；不借这个名字伪造新候选，也不改共享schema。默认VersionPrefix0.8.0/alpha.2不等于显式formal版本。0.10.3只有提案，没有登记、发布或预留。

本轮首选 **source-build诊断候选**，无需新版本、打包、签名或workflow写入。其报告不产生正式Cp6CapabilityApproval，不填未测数据库为Allowed。未来`.3`登记文件清单已由[既有正式版本提案](D:/CP6/docs/development/evidence/WP02-02/consumption-preparation/FORMAL-VERSION-PROPOSAL.md)给出，下个独占窗口可实施，无需本轮再抄提案。

[PLT-RELEASE-01:54](D:/CP6/docs/design-handbook/modules/PLT-RELEASE-01.md:54)要求正式consumer使用feed package、不能ProjectReference/source fallback；[第73行](D:/CP6/docs/design-handbook/modules/PLT-RELEASE-01.md:73)保留Required-REL-01..07。本建议仅允许临时验证副本使用源码，不改变合同，也不批准生产源码采用例外。旧卡允许隔离构建producer；新完整graph/四处投影/私有锁/loopback权限应在本轮卡明确授予，不沿用旧卡自行扩范围。

后续仍需：独立Platform版本登记窗口并集成；冻结最终完整producer；具体repo/SHA/version/workflow/费用范围的新正式run授权；保护Environment批准、one-build、RFC3161/Current撤销、七包不可变原字节feed查回；真实签名包restore与16锁生成；WP02-28真实宿主/兼容回退与WP02-02原17PGT。不能把source实验全绿当正式run制品或包信任。

B可登记 **SOURCE_COMPATIBILITY_PREPARATION_ONLY**。WP02-02主任务仍 **BLOCKED / NOT_CHECKED**，17PGT全NOT_RUN；WP02-28不开放正式采用；不增加DONE数。此评估没有新构建/运行/Current信任证据，没有查询在线排队；Actions执行/取消、触发修改、远端写入、数据库操作均为0。
