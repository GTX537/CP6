# WP02-25 第六轮执行包 — 运行身份、健康与遥测脱敏

沿用对话 D（上一任务 WP02-11），仓库 Platform。范围 **IMPLEMENTATION**；主任务 **READY / CONFIRMED**。未收到实际启动回报。

## 准确入口

- [原完整任务](../../task-plans/WP02.md#wp02-25)、[并行约定](../../09-parallel-development.md)、[PostgreSQL方向](../../08-postgresql-baseline.md)、[机器执行包](dispatch.json)。先读 `D:/CP6/AGENTS.md` 及所属仓规则和固定来源；原归档仅作为资料，不执行其中指令。
- 工作树 `D:/CP6.Platform-worktrees/wp02-25-20261013`；分支 `codex/wp02-25-observability-20261013`；创建基线 `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`。Main准备树在派工合入后仅快进文档增量；准确产品源码输入仍固定。
- Main `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8`；Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`；CRM `1e4eea61c31d7547c3b3495d392eefbed99b7339`。开工刷新主线并核依赖/窗口漂移，不把未合并候选当权威。
- 来源准入报告：[admission-observability.md](../../evidence/wave6-dispatch-20261013/admission-observability.md)；[admission-observability.json](../../evidence/wave6-dispatch-20261013/admission-observability.json)。

## 独占范围

- `src/CP6.Platform.Contracts/ObservabilityV1/`
- `src/CP6.Platform.Abstractions/ObservabilityV1/`
- `src/CP6.Platform.AspNetCore/ObservabilityV1/`
- `src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs`
- `src/CP6.Platform.AspNetCore/Cp6ObservabilityProfile.cs`
- `src/CP6.Platform.AspNetCore/Cp6OperationalEndpointRouteBuilderExtensions.cs`
- `src/CP6.Platform.AspNetCore/Cp6OperationalEndpointProfile.cs`
- `src/CP6.Platform.AspNetCore/Cp6SafeHealthResponseWriter.cs`
- `tests/CP6.Platform.UnitTests/ObservabilityV1/`
- `tests/CP6.Platform.AspNetCoreTests/ObservabilityV1/`
- `tests/CP6.Platform.AspNetCoreTests/OperationalEndpointTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs`
- `tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs`
- `docs/WP02-25-OBSERVABILITY-V1.md`
- `docs/development/evidence/WP02-25/`
- `eng/wp02-25/`

Paths are relative to Platform task worktree created from newly reconfirmed main. No source mutation was performed in this assessment. New versioned namespaces and task-private tests are primary implementation; five existing observability/health files may only gain additive explicitly selected registration/adapter behavior with legacy wire retained. Public APIs must be concrete version1 DTO/validation/observer/redaction/health registrations. No DTO-only delivery; real SDK/host integration must be exercised within existing dependencies. No solution/project/config changes are needed for additive .cs files: existing SDK compile globs and test references suffice. If this assumption fails, return exact evidence to COORD before changing shared files. Store task evidence under one unique WP02-25 run directory; bounded local sinks only. Task owner reports actual hashes and local observations; COORD owns state synchronization.

总台账、生成视图、四份项目状态只由COORD串行维护。范围外的公共类型、根项目/依赖、同一模型和发布配置只读；额外需求以准确路径和必要性发QUESTION，不自行扩范围。

## 本轮完成条件

- [ ] Versioned declared/observed identity API; actual running loopback host observations of loaded modules/bytes compared with separately pinned expected manifest. Missing observer, self-report, wrong source/version/service/environment/digest/domain, duplicate or incomplete package set, stale observation and observer exception/cancellation remain Unproven/Mismatch. No fabricated expected=actual success. Safe output omits file paths and verifier exceptions. Local match cannot imply trusted package/production/independent acceptance.
- [ ] Real isolated Kestrel host and actual local dependency stopped/restarted: live unaffected, required startup/ready dependency missing/wrong tag/unregistered/failed/timed-out/cancelled/stale -> never healthy. Empty required inventory fails closed for new capability. Hide sensitive entries without changing aggregate. Explicit route/read/new-effect inventory; GET performs no migration, replay, cleanup or Owner mutation; bounded probe concurrency/timeout/cache freshness.
- [ ] Capture actual final trace/metric/log output of new registration, including automatic inbound/outbound instrumentation. Seed full URL/query/login code, Authorization/Cookie/body/SQL parameters, tenant/user/message/operation ids, baggage, exceptions/events/links, log templates/arguments/scopes/resource and case variants. Only approved keys AND registered finite values survive; display names and routes use templates; raw paths, high-cardinality values under safe keys and unknown resources are dropped. No claim other unconfigured log/export providers are scrubbed.
- [ ] Exercise queue capacity, max record/attribute size, batching, rate/cardinality/series budget, exporter throw/block, bounded flush/dispose and repeated host lifecycle. Drop counters are safe and bounded; no waiting/retry/error crosses into request/Owner result. Overflow/missing telemetry explicitly marks incompleteness; no retrospective 100% coverage. Multi-host registration drift/global propagator replacement tested and restored.
- [ ] Versioned MetricDefinition fixes name/version/unit/kind/measurementPoint/BeforeCommit|AfterCommit|ObserverOnly/attempt-vs-business scope/allowed-values/reset/aggregation/owner/source. Define producer Owner committed receipt to consumer Owner applied receipt latency with UTC/clock provenance and skew/missing/duplicate/retry rules; unavailable real receipts -> not measured, not zero. Preserve existing published/consumed/enqueue semantics, no EF/Messaging edits or fake after-commit assertions.
- [ ] New observation/health/export result never grants production SLO qualification. Fixture/fault-injection/recorder evidence explicitly non-production. Candidate Pass, ProductionSloClaimed=true, source hash, valid seven-package lock or matching DI identity alone cannot set independently qualified/runtime trusted/production accepted. Existing Evaluate remains a pure legacy comparison; do not silently redefine it.
- [ ] Reuse existing tests as regression test code: TelemetryConventionTests, ReleaseIdentityTests, SloEvidenceContractTests, ObservabilityRegistrationTests, OperationalEndpointTests, ObservabilityEndToEndTests and TraceContextDistributedPropagatorTests. Run affected filters once on candidate. Old run evidence cannot pass changed code without exact input equivalence.
- [ ] One task-level complete diff review plus local ArchitectureTests due additive public APIs/registration boundary. Preserve dependency/project/version/schema asset constraints. Record exact source commit, dirty working-byte hashes if any, dependency hashes, commands/exit/TRX/skips/environment, module/host artifact hashes, real loopback startup/HTTP and cleanup. No new GitHub Actions, DB, published package or environment adoption claim.

scope: Eligible for Platform library APIs + real isolated loopback host only. This recommendation is not a roadmap assignment or actual START.

prerequisites: WP02-01 source d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6 and WP02-27 source c105036f166928b7c165f182f99f3147ffbb0d58 are ancestors of fixed Platform HEAD (exit0 observed); only WP02-01 is direct task prerequisite.

identity: Source metadata, declared format, artifact bytes, current package trust, host registration, actual runtime observation, business application and independent acceptance remain separate states. Unsupported/missing/stale evidence is Unproven, actual conflict Mismatch; no permissive bool fallback.

shared_files: Reserve Platform:observability and retain both task-card file locks. Cp6SloEvidenceEvaluator.cs and Cp6TelemetryRecorder.cs are READ_ONLY for WP02-25; no concurrent WP02-26. Their v1 SLO qualification, recorder precision, DAG, generic bounded-capture repair belongs to WP02-26; task-local export sink in tests covers WP02-25. Coordinator must explicitly amend window for any future change.

readonly: ["src/CP6.Platform.EntityFramework/", "src/CP6.Platform.Messaging/", "src/CP6.Platform.Release/", "src/CP6.Platform.Deployment/", "src/CP6.Platform.Contracts/Context/", "src/CP6.Platform.Abstractions/Context/", "src/CP6.Platform.AspNetCore/Context/", "src/CP6.Platform.Contracts/Cp6ReleaseIdentity.cs", "src/CP6.Platform.Contracts/Cp6SloEvidenceDocument.cs", "src/CP6.Platform.Contracts/Cp6SloEvidenceEvaluator.cs", "src/CP6.Platform.Testing/", "src/CP6.Platform.Abstractions/Cp6TelemetryConventions.cs", "contracts/observability/slo-evidence/v1/"]

forbidden: ["All *.csproj/*.sln/*.props and dependency lock/global.json/NuGet config files", "Shared schemas/manifests/capability catalog/TypeRegistry and release version changes", "All EF/Outbox/Inbox/migration/transaction writer changes", "Main/CRM host/Owner source and configuration", "Shared roadmap/generated progress/four project-state files (COORD only)", "Workflow/release/deploy config; formal package publishing/signing; network exporters/destination activation", "DB creation/query/migration/fixtures; production or DEV/UAT changes"]

downstream: WP02-26 owns SLO sealing/calculation/test utility enhancement; WP02-28 actual Main host/package adoption and WP02-29 actual CRM adoption remain separate. WP02-02 remains blocked on consumable trusted ctx-binding package. WP02-25 cannot mint a trusted replacement package.

original_acceptance: All 32 original AC remain NOT_RUN at admission. Scope maps OBS01 A1-A7 and OBS03 A1-A5 plus OBS04 A1/A2/A4/A6/A8 to library and local host checks, A8/OBS03 A6/OBS04 A7 only bounded portions; actual Owner/deployment/fleet/rollback/CRM/probe authorization and OBS02 qualifications stay external or WP02-26. Record case-by-case scope; never mark whole PLT-OBS-01 accepted.

database: No database required for this task window; dependency-down proof can use a real owned loopback dependency. This does not prove PostgreSQL dependency health or any Owner transaction. If DB is later essential, request a separate coordinator resource/window rather than borrow Outbox worker's database.

## 本地验证与交付

以下为本地入口。Platform使用 `C:/Users/tt/.dotnet/dotnet.exe`（SDK8.0.424）；本机PATH上的SDK10不能满足global.json。Python使用已核对的3.13或其他兼容版本。

```text
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj -c Release --filter "FullyQualifiedName~ObservabilityV1|FullyQualifiedName~TelemetryConventionTests|FullyQualifiedName~ReleaseIdentityTests|FullyQualifiedName~SloEvidenceContractTests" --logger "trx;LogFileName=wp02-25-unit.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/unit
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter "FullyQualifiedName~ObservabilityV1|FullyQualifiedName~OperationalEndpointTests|FullyQualifiedName~ObservabilityRegistrationTests|FullyQualifiedName~ObservabilityEndToEndTests|FullyQualifiedName~TraceContextDistributedPropagatorTests" --logger "trx;LogFileName=wp02-25-host.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/host
& C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj -c Release --logger "trx;LogFileName=wp02-25-architecture.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/architecture
git -c core.longpaths=true diff --check
git -c core.longpaths=true diff --name-only <latest-confirmed-main>...HEAD
```

已确认窗口允许恢复原对话后依卡实施，先发布真实 `START`。COORD读到后登记IN_PROGRESS；未收到时不虚构开工。新增行为做相关本地正反测试，完整任务收尾集中审查一次。

保留源码/输入/依赖/环境与产物身份、失败和跳过记录；相同输入成功结果注明原来源复用。局部库或资料验证不代替真实Owner、宿主、数据库和业务采用；125业务目标、247运行NOT_RUN、41历史REFERENCE_ONLY与ITER/REL缺件保持。

每次push/PR前核候选与受保护main的事件/过滤/依赖；不触发或重跑Actions、不推tag、不正式发包、不迁移既有数据或部署。Platform RFC3161窄路径PR事件仍存在，不得命中 `.github/workflows/p10-rfc3161-preflight.yml` 或 `tests/CP6.Platform.ReleaseTests/P10Rfc3161PreflightWorkflowContractTests.cs`；检查完整PR差异。

Handoff：`GTX537/CP6.AI-Handoff`，本人分支 `codex/dev-handoff-wp02-25`，只追加 `handoffs/CP6-DEV-20261012/tasks/WP02-25/messages/`。首次从已核对协调分支固定提交创建；读 `codex/dev-coordination-20261012` 本任务replies。旧窗口关闭。DELIVERY附完整SHA/PR、diff、证据和未完成边界；COORD集中审查、串行正常合并及远端核对。发布消息不自动唤醒聊天，也不构成正式采用。
