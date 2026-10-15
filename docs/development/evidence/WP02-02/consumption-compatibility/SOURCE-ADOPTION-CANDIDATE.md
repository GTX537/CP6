# WP02-02 固定源码采用候选

本候选属于 **SOURCE_COMPATIBILITY_PREPARATION_ONLY**。固定源码的三个入口 restore/build、完整依赖图及私有 Kestrel 指定路径验证已取得通过证据。实际执行日期为 **2026-10-15 UTC**，最终 owner 为 `0615c0909a5a4ee4976ca49cd1339694`；先前两次真实失败保留，见 [诊断记录](DIAGNOSIS.md)。

运行策略结果为 **MATCHED_SPECIFIED_PATHS**，只覆盖下述构建和 synthetic authority 私有宿主路径。WP02-02 主任务继续 **BLOCKED / NOT_CHECKED**；正式包信任为 **BLOCKED**，Main consumption 与 17 PGT 为 **NOT_RUN**，WP02-28 未开放正式采用。总台账及四份项目状态文件由 COORD 串行维护。

依据：[第七轮执行包](../../../dispatch/20261014-wave7/WP02-02.md)、[准入报告](../../wave7-dispatch-20261014/admission-package.md)、[本轮计划](PLAN.md)。正式 consumer 的 feed-package 要求仍适用，见 [PLT-RELEASE-01](../../../../design-handbook/modules/PLT-RELEASE-01.md)。本次没有批准将生产 PackageReference 改为 ProjectReference。

## 1. 完整身份与证据边界

| 输入 | 固定身份 |
|---|---|
| Main 产品源码 | commit `1129953156b7775152a74ad2f51cb95ef430ee56`，tree `fcf79cd38055adddab4a69e4910808c691d442b2` |
| Platform 完整源码 | commit `b28db763f70ab6f164cb7c87d6ce49a6d97267f1`，tree `4ae98434d6e87f3fdd852a156fd057c03e336e43` |
| 派工文档基线 | Main `99fc0ce383d2d02c1404a8adbb3abe971b41d332`；不替换上行产品输入 |
| Main NuGet.config | 原字节 SHA-256 `23ab29f2a03952fcf859213fe42a1e2c2a8eee933e8f92dd74c18f68f2b7be45` |
| SDK / 本机已列出 runtime | SDK `8.0.424`；明确 .NET 入口的已安装 Microsoft.NETCore.App / Microsoft.AspNetCore.App 为 `8.0.30`；实际启动另见 runtime 证据 |
| 旧正式包身份 | `0.10.2`，producer `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，publication SHA-256 `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181`，只保留原历史范围 |
| 本次候选身份 | 固定 commit/tree + 精确临时 overlay + 实际依赖与输出指纹；**没有新正式版本或包归档身份** |

[input-manifest.json](../../../../../eng/wp02-02/consumption-compatibility/input-manifest.json)固定源码、树、SDK、NuGet 配置及四项替换。生成器从 Git 对象读取原字节，记录完整 Git 文件清单、实际导出范围及各导出文件 hash；不读取其他对话未提交源码，不复用其 bin/obj。

每次使用新 `owner32`。唯一临时根为本任务工作树中的 `artifacts/wp02-02/consumption-compatibility/<owner32>/`；其下 `snapshots/main`、`snapshots/platform`、`probe`、独立 NuGet/cache、bin/obj、日志与私有锁均属于同一 owner。跨 owner、链接/reparse point、路径越界或不同 producer 的项目/DLL 混用不能进入兼容通过结论。

所有原 `packages.lock.json` 保留，并另存 `originals/` 对照。新图使用 `packages.wp02-02.lock.json`；它不是未来正式 feed consumer lock。外部依赖 restore 使用固定 Main NuGet.config 的 `signatureValidationMode=require`、source mapping 和原 signer pins，不修改全局信任或把私有 NuGet 配置改为无包源。

## 2. 消费者与精确投影

两个真实 Main 构建入口为 `CP6.WebApi/CP6.WebApi.csproj` 与 `tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj`。前者包含 Core、Space、PostgreSQL 持久化相关闭包；后者只编译，禁止执行其数据库命令。固定输入求得的 Main 项目闭包是：

| 项目 | 本轮角色 |
|---|---|
| `CP6.WebApi` | 复制的主构建入口；不运行 Main Program |
| `CP6.Core` | Messaging / EntityFramework 直接消费者 |
| `CP6.Entity` | Core/应用所需实体编译依赖 |
| `CP6.Persistence.PostgreSql` | PostgreSQL 持久化编译依赖；无数据库连接 |
| `CP6.Space.Contracts`、`CP6.Space.Domain` | Space 合同与领域编译依赖 |
| `CP6.Space.Application`、`CP6.Space.Infrastructure` | Space 应用与基础设施编译依赖 |
| `tools/CP6.DatabaseCompatibility.Probe` | 第二构建入口，覆盖第四处直接 Platform 引用；仅 build |

临时 Main 副本内只替换以下四项，每项原始文本必须恰好存在一次：`<PackageReference Include="包名" Version="[0.10.2]" />`。替换目标为同一 owner 的固定 Platform 源码项目；替换前后 csproj hash、原文件及机械投影记录一并保留。业务 `.cs`、编译项和项目模块不为诊断删减。

| Main 相对项目 | 原包 | 固定 Platform 相对目标 |
|---|---|---|
| `CP6.Core/CP6.Core.csproj` | `CP6.Platform.Messaging` | `src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj` |
| `CP6.Core/CP6.Core.csproj` | `CP6.Platform.EntityFramework` | `src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj` |
| `CP6.WebApi/CP6.WebApi.csproj` | `CP6.Platform.AspNetCore` | `src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj` |
| `tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj` | `CP6.Platform.EntityFramework` | `src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj` |

独立 `probe/Probe.csproj` 直接引用同一 Platform 快照的 Abstractions、AspNetCore、Contracts、EntityFramework、Messaging 五个项目，并通过正常 NuGet restore 获取完整传递依赖。其局部 Directory.Build.props/targets 隔离 Main 父级构建策略，child Directory.Packages.props 使用固定 Platform 文件原字节。没有裸 DLL Reference 或从任意外部目录加载程序集的 fallback。

## 3. 依赖冲突责任与判定方式

原四裸 DLL 探针缺少 AspNetCore/Messaging 的完整依赖闭包，构建曾选择 framework DiagnosticSource `8.0.0.0`，而生产者依赖要求 `10.0.0.0`。该 [旧失败日志](../consumption-preparation/runs/probe-20261014-a3/build.log)保留。它不证明 Main 实际图同样存在冲突，也不授权降低 Main 的依赖版本。

固定 Main 的 WebApi/Core 原锁已有 `System.Diagnostics.DiagnosticSource 10.0.8`，NuGet contentHash 为 `rQHsK7Xr8Uz3449860ayVXp/CaLmrhHlMPxbpT/ibOPtp/dTTsr6+f/SxaGO2NzxHf+0siLE0UfdVN5z1I0EgQ==`。固定 Platform 中央依赖包括 OpenTelemetry `1.18.0`、Http.Resilience `10.9.0`、Dapr.Client `1.18.5`。NuGet 版本 `10.0.8` 与程序集版本 `10.0.0.0` 是不同字段，不能机械视作错版。

本候选要求三个 build 命令均显式带 `-warnaserror:MSB3277`。出现缺依赖、解析不确定或 MSB3277 时维持 FAIL/UNKNOWN，不使用忽略选项。其他实际警告保留，不把“未见 MSB3277”替代完整构建成功。

[inspect_graph.py](../../../../../eng/wp02-02/consumption-compatibility/inspect_graph.py)只读完成后的 assets/deps/私有锁与隔离 cache，输出每项目全部 library 和 target dependency maps、项目来源、原锁及私有锁差异、DiagnosticSource/OpenTelemetry 原/新记录。每个实际包分别记录 archive SHA-256、原始归档 SHA-512、`.nupkg.metadata` 来源和 NuGet contentHash。核验分两组：metadata/assets/lock/deps 的 NuGet contentHash 必须一致；实际完整归档 SHA-512 必须与缓存 `.nupkg.sha512` 一致。两组彼此不强制相等；O849 的实际 CloudNative.CloudEvents 2.9.0 已观察到两组值不同。任一组缺失或错配仍拒绝，不能把 cache `.sha512` 当作 metadata contentHash。元数据与本地 restore 证据不构成新的 Current 密码学验证或 feed 原字节查回证明。

运行前另行固定 build 输出；运行后逐个核实际 DLL 路径、hash、MVID、AssemblyName/version、观察到的类型与构建 assets/deps 的对应关系。缺依赖、错版本、错来源、重复程序集、未知兼容、跨快照/owner 必须拒绝。实际运行程序集集合应含全部五个 Platform 运行程序集及 DiagnosticSource、实际加载的 OpenTelemetry 依赖。

## 4. 可重现步骤

从已确认的最新 Main 建立独立任务分支/工作树，记录文档主线与本文件固定产品输入的区别。以下参数必须显式替换成实际绝对路径；`<fresh-owner32>` 必须是新的 32 位小写十六进制值，不能复用历史目录。

```text
python -B -X utf8 -m unittest discover -s eng/wp02-02/consumption-compatibility/tests -v
python -B -X utf8 eng/wp02-02/consumption-compatibility/run.py --phase verify-plan --manifest eng/wp02-02/consumption-compatibility/input-manifest.json
python -B -X utf8 eng/wp02-02/consumption-compatibility/run.py --phase prepare --owner <fresh-owner32> --output <absolute-owner-root> --main-repo <Main-Git-object-store> --platform-repo <Platform-Git-object-store>
python -B -X utf8 eng/wp02-02/consumption-compatibility/run.py --phase build --owner <owner32> --output <absolute-owner-root> --dotnet <absolute-SDK8-dotnet.exe>
```

使用任务包指定 Python 与 SDK8.0.424 入口；不默默切换 PATH SDK10。runner 写入 owner/global.json、独立 cache 与明确命令记录。其 restore/build 顺序覆盖复制 WebApi、复制 DatabaseCompatibility.Probe 和独立私有 Probe，restore 使用固定 Main 配置，build 使用 `--no-restore -c Release -warnaserror:MSB3277`。所有原生产文件和锁在前后核验中保持原字节。

Graph 检查只能在 build 阶段结束后运行；构建失败可以输出部分证据，但 compatibility 继续 UNKNOWN。运行身份捕获与核验由 [capture_build.py](../../../../../eng/wp02-02/consumption-compatibility/capture_build.py)、[runtime_policy.py](../../../../../eng/wp02-02/consumption-compatibility/runtime_policy.py)执行，使用各自 `--help` 所列参数；必须先完成独立 build 身份捕获，再启动 probe，再校验实际报告，不能用 runtime 自报 hash 反向充当构建基准。

```text
python -B -X utf8 eng/wp02-02/consumption-compatibility/capture_build.py --owner-root <absolute-owner-root>
python -B -X utf8 eng/wp02-02/consumption-compatibility/run.py --phase probe --owner <owner32> --output <absolute-owner-root> --dotnet <absolute-SDK8-dotnet.exe>
python -B -X utf8 eng/wp02-02/consumption-compatibility/inspect_graph.py --owner <owner32> --root <absolute-owner-root> --output <absolute-owner-root>/dependency-graph.json
python -B -X utf8 eng/wp02-02/consumption-compatibility/runtime_policy.py --owner-root <absolute-owner-root> --expected <absolute-owner-root>/pre-runtime-identities.json --runtime <absolute-owner-root>/runtime/runtime.json --output <absolute-owner-root>/runtime-policy.json
git -c core.longpaths=true diff --check
```

只有该 owner 的独立 probe restore/build 真正成功时才可执行 probe。若 Main 构建仍失败，即使独立 probe 成功，也只能记录 PRIVATE_PROBE_ONLY 范围，不提升总体兼容结论。DatabaseCompatibility.Probe 的 DLL 和 Main WebApi 的入口始终不执行。

私有宿主使用无默认配置的 WebApplication builder，显式绑定 `127.0.0.1:0`。同进程 HttpClient 请求成功路由与 401/403/503 拒绝路由；真实 TrustedRequestContext 中间件及 Background/Message executor 运行，但 verifier/authority 均标 **SYNTHETIC_AUTHORITY**。实际 WP02-25 SDK 输出必须包含 HTTP server/client trace、metric、log，flush、隐私字段约束和本地 sink 检查均须通过。最后停止/释放宿主、验证原端口拒绝连接并恢复拥有的 propagation 状态。失败返回非零并保留已有报告/日志。

## 5. 最终实际结果与未验证范围

最终 owner `0615c0909a5a4ee4976ca49cd1339694` 的原始证据保存在 [runs 目录](runs/0615c0909a5a4ee4976ca49cd1339694/)。

| 维度 | 结果 | 实际范围 |
|---|---|---|
| source | PASS | 固定 Main/Platform Git 原字节及四项 overlay；manifest SHA-256 `c38de8f5020ef05cddcc2a491a3bc051792a385047f55e8821b7c788e5b23848` |
| build | PASS | WebApi、DatabaseCompatibility.Probe、私有 Probe 的三次 restore 和三次 build 均 exit 0；每次 build 启用 MSB3277 错误门禁 |
| graph | PASS | 15 assets、15 deps、17 私有锁、189 个实际包，0 errors；原锁保留，完整 delta 已记录 |
| runtime | PASS / MATCHED_SPECIFIED_PATHS | 私有宿主 31 assertions，通过 HTTP 200/401/403/503、上下文与 SDK 路径；不运行 Main |
| identity | MATCHED | 运行前读取 12 个 DLL 候选 PE 身份；实际加载的 11 个目标程序集逐一匹配路径、hash、MVID、版本，含全部 5 个 Platform 运行程序集 |
| cleanup | PASS | Stop/Dispose/propagators 均成功；真实 ConnectionRefused，检查耗时 2044.4377 ms |
| source-adoption-plan | CANDIDATE_ONLY | 本方案可审查，未批准生产源码采用 |
| formal-package-trust | BLOCKED | 未生成、签名、发布或查回新正式包 |
| Main-consumption | NOT_RUN | 未执行 Main Program、数据库或原 17 PGT |

[完整图](runs/0615c0909a5a4ee4976ca49cd1339694/dependency-graph.json)只判定 restore/build 图，故自身 `compatibility` 保持 UNKNOWN；运行时指定路径结论由独立 [runtime-policy.json](runs/0615c0909a5a4ee4976ca49cd1339694/runtime-policy.json)提供。两者均不提供完整业务兼容或正式准入结论。

[DiagnosticSource 对照](runs/0615c0909a5a4ee4976ca49cd1339694/diagnostic-source-binding.json)核实三个入口 assets 均选择包 `10.0.8`；私有宿主实际加载的 DLL 与该包的 net8.0 DLL hash 相同：`5de9dc5c14e36e7798b69805b57b6d2c4136a6c2266c6db2c2351eb8eda792f6`，AssemblyVersion 为 `10.0.0.0`，MVID 为 `54d6b6a6-6acb-4134-8c85-9e2d495e61b0`。其他闭包项目图仍可能包含包 `10.0.0`，完整图保留该事实；不能仅靠 AssemblyVersion 混同包版本，也不能将私有宿主运行外推为 Main WebApi 已运行。

实际 SDK 本地 sink 记录 21 Log、7 Trace、7 Metric，flush 成功、droppedCount=0、传播和隐私检查通过。固定 Platform 的 `ObservabilityRuntimeV1.Completeness` 返回 Incomplete，本报告原样保留，不冒称遥测全集或 SLO 采用完成。五个 Platform 运行程序集的 AssemblyVersion 为 `0.8.0.0`，只结合本次固定源码及输出 hash/MVID 解释，不能改称旧或新正式包版本。

29 项工具测试按受影响范围分批通过，来源见 [交付报告](README.md)。另有 [8 类真实报告副本篡改反例](runs/0615c0909a5a4ee4976ca49cd1339694/negative-report-mutations.json)全部 REJECTED，覆盖缺依赖、错版本、错来源、重复程序集、Unknown、跨快照 owner、错 hash/MVID；它们是报告策略测试，未物理删除 DLL 或冒充加载器故障。已有输出的真实 CLI 拒绝验证退出 2、报告 hash 不变。

[生产输入前后对照](input-audit-after.json)确认 115 份项目/props/锁等工作文件原字节未变；原 17 PGT 仍为 NOT_RUN 且 execution_evidence 为空。两个早期失败 owner、原始构建错误、错误图报告及其后续修正报告均保留，不回写成成功。

WebApi 最终构建保留 5 warnings、0 errors，包括既有 System.Security.Cryptography.Xml `8.0.4` 的 NU1903 和代码警告；没有在本窗口升级依赖或抑制警告，本地诊断不作为生产漏洞门禁通过。前轮 44 checker / 8 parser / 10 synthetic 结果仅保留历史范围，不改称本 producer 的新验证。

## 6. 后续串行采用的准确变更与准入

本轮首先交付上述技术未知的真实结果与本候选，不重新把既有 `0.10.3` 提案登记成新增完成工作。[既有正式版本提案](../consumption-preparation/FORMAL-VERSION-PROPOSAL.md)继续承载未来版本/schema/workflow 的精确文件清单；`0.10.3` 仍不是预留、已登记或已发布版本。

若继续**正式 feed 包路线**，COORD 需另开 Platform:package-trust 独占窗口，完成既有登记变更并集成，随后冻结新的完整 producer SHA。新的 producer 不能自动继承本次固定 b28db76 的结果；按实际变化重算闭包和受影响验证。最终准确 repo/SHA/version/workflow/费用范围须获得新的正式执行授权，保持 Environment 审批、one-build、七包 author 签名/RFC3161、Current 撤销、不可变 feed 原字节查回及真实 publication。正式消费时在获准 Main 窗口更新上表三份真实 csproj 的四个准确 PackageReference 版本和相应真实锁，保持 feed 来源、签名和公共合同；不能提交本次 ProjectReference overlay 或私有锁充当正式消费。

若未来考虑**生产源码采用例外**，当前没有批准。须先明确与 PLT-RELEASE-01 feed 消费要求的正式例外及受影响消费者范围，并给出可审查的部署/回退与依赖信任依据。只有单独获准后，上表四处替换才可能成为真实产品变更候选；届时必须确定稳定仓库布局、唯一 producer 版本/源码身份、依赖锁、构建产物来源与长期更新规则。本临时 owner 路径不能进入生产 csproj，不能借 ProjectReference 隐式绕过正式信任要求。

无论哪条路线，真实宿主采用仍需独立 WP02-28/后继窗口：在实际 Host/DI 中提供可信认证与当前 authority，核 HTTP、Background、Message 的身份传递及失败关闭，覆盖授权/租户/Owner、PostgreSQL 存储与原 17 PGT。当前 synthetic verifier/authority 不得移植为生产认证适配器。租户 schema 仍须单独定义共享/租户边界、可信映射、权限、池/事务上下文与逐租户迁移；本候选不授权删除 TenantId、改变 Owner 或降低原子性。

## 7. 撤销、回退与交付条件

本次运行没有修改真实产品项目、锁、Program、数据库或环境，因此撤销诊断只需停止自有 probe，确认端口关闭，保留 owner 数据、日志与失败证据；不需要恢复生产数据库或重写共享历史。需要撤销已合并的私有工具时使用正常 revert 提交并保留任务证据，不 force-push 或删除远端历史。

未来正式采用必须单独固定此前受信任的精确包版本、feed/归档 hash、consumer 锁、应用产物身份与配置。回退应通过经过验证的应用产物与版本控制变更进行；任何 schema 已前向迁移的环境都必须先证明旧应用兼容，不能靠回滚数据库或可变 tag 宣称可恢复。本次无数据库动作，不能提供这项未来兼容证明。

交付前集中审查本任务完整 diff，核输入/投影/真实生产文件不变、三个根 restore/build 的实际范围、完整依赖图、真实 HTTP/SDK 输出、程序集身份拒绝与清理。必需项失败则保留具体错误及 UNKNOWN/NOT_RUN，不抬升兼容或任务状态。普通测试仅本地执行；本文件不授权 Actions、工作流/分支保护调整、正式签名/发包、生产部署或跨仓状态推进。最终提交/PR/合并按本仓分支规则和 COORD 串行窗口办理，远端 main 包含性核对由交付主对话记录。
