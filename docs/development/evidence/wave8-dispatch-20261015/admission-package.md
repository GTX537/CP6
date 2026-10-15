# 第八轮 WP02-02 正式包路线准入

结论：可立即由协调开 **WP02-02 / PRODUCER_REGISTRATION_ONLY** 的 Platform 后续窗口，实施已有提案的版本/能力登记及签名发布链的源码准备。“继续推进”已覆盖这类可逆、必要的开发工作，无需现在重复索要一个尚无最终 SHA 的发包批准。窗口完成后再用最终代码、测试和准确费用请求一次正式运行授权。此窗口仍不解除 Main 业务采用 BLOCKED。

## 已核事实

- Main 本地 main 与 origin/main 均为 `b0a4f5601b008d0003598065bbf3241124442890`；Platform 二者均为 `66480fe24d51c706708be971e7e9d75a684a3bc4`，tree `4bb731924244ef7876805294b623fdbe8dd2155a`。Platform checkout 是无关旧分支，分析全部用 git 对象读取。按本次只读任务禁令未 fetch/联网；派工前由协调再次确认实际远端。
- 旧 Main `1129953156b7775152a74ad2f51cb95ef430ee56` + Platform `b28db763f70ab6f164cb7c87d6ce49a6d97267f1` 的 owner `0615c0909a5a4ee4976ca49cd1339694`：三入口 build 与私有 synthetic authority 31 断言通过。它们不证明最新 producer、正式包信任、Main Program 或 17 PGT。
- 正式 publication API/schema、能力 API/schema 与 P10 脚本相对提案读取的 `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` 无变更。当前 reader 仅 .0/.1/.2，四个 writer 与工作流只接受 .2；新 ctx 能力无法经当前正式路线表达，这是真实源码阻塞。
- 最新 producer 相对旧 b28db76 新增 Contracts/SloQualificationV1、Messaging transport，且改了 Cp6CloudEventValidator/Cp6ContractBundle、EF TransportOutboxPublisherV1；根依赖配置无该段变化。必须按闭包判断影响，不能把旧源兼容结论改签为新 SHA。
- 默认 runtime 仍 0.8.0-alpha.2，Release 默认 0.10.2-test.local.1，根 VERSION 为审计 0.10.2.0。正式 pack 显式传 PackageVersion；无需批量改七项目默认版本。
- consumer-lock schema 的 version 是非空字符串，无旧版本枚举，保持原字节。FormalPackageVerifier 已接受 canonical stable SemVer，无需改其版本检查。
- 0.10.3 只是建议：历史 2026-10-14 七包完整分页观测未见该版，本次未重新查询，不能声称槽位当前空闲或预留。

## 可直接派工的准确窗口

使用 WP02-02 的后续生产者准备窗口，保留已交付 WP02-27 的真实历史，不重新把旧实现记为完成。协调关闭旧 Main 准备 assignment 后，为同一任务登记 Platform 单 owner、独立分支/工作树、`Platform:package-trust` 和逐文件锁；建议分支 `codex/wp02-02-formal-registration-20261015`，工作树 `D:/CP6.Platform-worktrees/wp02-02-formal-registration-20261015`。需从届时已确认最新 main 建立，不在当前旧 checkout 改动。Main 主任务仍 BLOCKED/NOT_CHECKED；仅此准备子窗口 CONFIRMED。

精确允许修改清单见同名 JSON 的 required_write_paths，共 19 项：
- publication validator、capability validator、formal publication schema、capability-matrix schema；
- `p10-formal-packages.yml`；
- `Test-P10FormalPrerequisites`、`Pack-P10FormalPackages`、`New-P10FormalPackageSet`、`Publish-P10FormalPackageSet`、`New-P10FormalPublicationRecord`、`Test-P10FormalPackageSet` 六脚本；
- FormalPackagePublication、ReleaseSchemaParity、ReleaseSchemaAsset、P10FormalWorkflowContract、CapabilityValidation、CapabilityTrust 六测试文件；
- P10-RELEASE-GOVERNANCE 与任务专属 `docs/development/evidence/WP02-02/formal-registration/`。

JSON 另列精确可选文件，协调可一次性纳入：ReleaseSchemaTestData、专属 P10FormalRegistrationTests、`eng/wp02-02/formal-registration/`。根 VERSION 若作为审计登记更新为 0.10.3.0，应同时明确授权 CHANGELOG 的“登记就绪、未发布”条目；这是显式补充的审计文件范围，不是七项目版本迁移。未纳入者只读，不让执行者自行扩边界。

具体实现：
1. reader 精确扩至 .0/.1/.2/.3，writer 精确切 .3，拒绝 .4/latest/metadata/混版；保留历史 .0 recovery 与 .0/.1/.2 合法语义和原证据。
2. 新 .3 正式能力仅登记 `ctx-binding-v1/1.0.0`、`ctx-v1`、Background/Http/Message；旧三版仍能力空。新源码其他能力与 native S01-09 不随版本号自动取得正式声明。
3. 保七包统一 source/version/feed/hash；保 matrix 独立批准、原 publication/policy、实际归档验证与 Current 签名。未测 consumer row 不得 Allowed，当前正式矩阵/lock/hash 不得填假值。
4. 保工作流只手动触发、exact-main/event/checkout、受保护 Environment、one-build、签名/RFC3161、Current 撤销、marker、不可覆写、BytePreserving 查回、Windows/Linux 独立证据和私钥清理。
5. 交付真实实现、测试、差异与最终候选源码，不再交一份重复的笼统方案。准备完成仍标 NOT_PUBLISHED/NOT_FORMALLY_TRUSTED；正式发布后的消费者矩阵与实际宿主另办。

只读边界：trust policy/DER、全局 NuGet 与依赖配置、七 csproj 默认版本、ReleaseTool Program/verifier、旧 publication、recovery/signer 工作流、Main 真实 csproj/锁/Program/EF/迁移。总台账及四份项目记忆由协调交付窗口串行更新。无数据库窗口，后续新增验证只以 PostgreSQL 为目标，旧 DevOps/R2 SQL Server 历史不扩展为新增双库义务。

## 必需本地检查与可复用结果

- API/source-schema/实际 packed-schema 的旧三版与 .3 正反矩阵：混版、错 feed/source/hash、.4/latest/metadata、缺 schema；实际包资产通过独占目录唯一 TaskLocal prerelease 验证，不产假正式签名包。
- ctx 新版/旧版能力边界、local 冒 formal、缺批准/publication/policy、Unknown 兼容行、Current/Historical/Revoked/unknown signer、坏或缺 timestamp、篡改归档等相关 Release/Capabilities 回归。fixture 与真实网络密码学验证明确区分；在线不可用保留 UNKNOWN/NOT_RUN。
- 六脚本及正式 workflow 的版本边界、一构建、七槽冲突、未知发布结果查回、不可变 marker、权限/Environment/cleanup 合同测试；测试不得调用真实签名/发包入口。
- 一次相关 Release/Schema 测试与一次 Architecture 门；最终一次任务级完整 diff 审查，修正后只复查影响范围。显式记录源码/SDK/依赖、发现测试数、失败、产物/hash 和未验证范围。
- 对最终新 producer 做精确变化/闭包审计。旧 31 断言与三 build 只按其原 SHA 复用；因 Messaging/Contracts/EF 变化需要的三个真实 Main 构建入口/私有宿主检查应在任务私有临时 manifest 上更新并执行受影响范围，不改 Main 产品输入，不执行 Main Program/DB。不要每次提交重跑整套验证。
- 准备门不要求运行完整生产发布，也不以本地绿灯代替远程正式签名或 Current 查回。

## push / PR 的实际安全路径

Platform 当前有 8 个 workflow，其余均为 workflow_dispatch；唯一自动事件是 `p10-rfc3161-preflight.yml` 的 pull_request，paths 仅：
- `.github/workflows/p10-rfc3161-preflight.yml`
- `tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs`

本次允许改的是另一个 `P10FormalWorkflowContractTests.cs`，不会命中已观察到的窄过滤；上述两路径保持只读。每次 push/PR 仍必须重新检查候选与受保护 main 的全部 events/filters/依赖。本次未联网查队列，协调接手时查本任务归属运行；不得取消不明归属/部署任务。无需为本窗口修改或禁用 RFC3161 工作流。

## 实际需要的新授权

登记实现审查、相关本地门禁、正常合入、远端 main 包含性完成后，冻结最终完整 producer S（当前尚不存在），只读重核七槽、PUBLIC、Environment main-only/reviewer、Current signer 与 Main/CRM policy/cert pins。再提交一个具体请求：

`GTX537/CP6.Platform / .github/workflows/p10-formal-packages.yml / ref main / expected_commit=S / version=0.10.3`，一次 Windows build/author-sign/RFC3161/七包 GitHub NuGet 发布及原字节查回，后续 Linux 独立验证同份包、产真实 publication。必须获用户新的明确运行/费用/不可变发包授权并通过正常 `p10-formal-release` 审批，不能由“继续推进”推定这些授权。

估计合计 20–50 runner 分钟；当前硬上限 Windows 45 + Linux 20 = 65 分钟，审批等待另计。金额/额度本次未查询，应在请求时查明，不能报零费用。独立 RFC3161 preflight 的上限是两 runner 各15 + 汇总10 = 40，signer revalidation 20，recovery 20；这些额外运行/重试不包含在一次正式 run 的建议授权中。

当前槽位/权限/在线撤销无法确认时 UNKNOWN，不发包；marker 后失败先只读查同版实际结果，不盲目重跑/覆盖。最终正式制品需要精确七包 hash、原 feed bytes、真实 run/attempt/source/toolchain、Current 证据，才能讨论 Main 精确 PackageReference/真实锁与 WP02-28/17 PGT。生产 ProjectReference 例外未获批，不能作为捷径。包发布本身也不授权 R2/GHCR 候选、迁移、环境切换或生产部署；原生产签名、漏洞、审批、digest 门保持。

## 本次审计范围

只读文件与 Git 对象；无联网、无仓库修改/提交、无测试执行、无数据库、无 Actions。只将本报告写入指定 scratch。未声称新版本已登记、已预留、已发布，WP02-02 正式信任 BLOCKED、Main 与 17 PGT NOT_RUN、WP02-28 未开放。
