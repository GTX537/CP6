# WP02-25 运行身份、健康与遥测脱敏：准入评估

**建议准入 Platform 限定实现窗口**；须由 COORD 登记领取后 START。Main `9a0155dec1c3366df8a6631f3d8fdb086aa32fc8`，Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`。本地 HEAD/main/origin/main 一致，Platform 干净；没有在线刷新远端。前置 WP02-01 `d5ab56de99a3c9a3db0b58d7ae6e2540d05c72c6` 与 WP02-27 `c105036f166928b7c165f182f99f3147ffbb0d58` 均为 Platform HEAD 祖先（exit 0）。仅 WP02-01 是直接前置。

当前 release health 只比声明与 DI 注册，自动 instrumentation 未统一脱敏，ready 缺 required inventory。WP02-27 只交付本地校验器，没有包含本轮能力的新可信包；不影响本地源码/隔离宿主能力开发，不解除 WP02-02 阻塞。

## 写入窗口

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

优先新增 ObservabilityV1 类型与实际注册/导出适配。五个现有观测/health 文件仅作兼容性显式接线，旧 DTO/wire/技术 counter 语义保留。新 .cs 使用现有编译 globs；全部项目、版本、依赖、shared schema/manifest、工作流、发布配置只读。EF/Outbox/Inbox/Messaging、Context、Release/Deployment 和 Main/CRM 只读。测试使用独占 loopback 依赖，不借数据库。

保留 Platform:observability 与任务卡两个文件资源锁。`Cp6SloEvidenceEvaluator.cs`、`Cp6TelemetryRecorder.cs` 本轮只读，WP02-26 不并发启动。通用 recorder/DAG/精度/SLO资格化由 WP02-26 接续；本轮在测试目录实现有界的最终导出 capture。任何扩写先由 COORD 重新登记。总台账与四份项目状态由 COORD 处理。

## 必须交付

1. ObservabilityV1 runtime binding: declared Cp6ReleaseIdentity + environment/region + typed observed artifact kind/digest + packageSetRef/configurationDigest/schemaSetRef/deploymentRef + process-instance/epoch/start/observation time + verifier/evidence reference + Match/Mismatch/Unproven. Bounded canonical identifiers, supported versions/enums, immutable defensive copies, UTC and freshness limits; no network locator fetch. Default comparison Unproven.

2. Host observer must obtain evidence from the actual running process/loaded assemblies and bound byte sources, not request headers or expected declaration. Trusted expected identity is independently pinned. Metadata/source SHA and module MVID alone never prove bytes or package trust. No generic Match from two caller-filled structs; distinguish observed-by-this-adapter from independently-qualified.

3. HealthCheckBindingV1: component/purpose/routes/dependency/timeout/freshness/failure classification/probe policy/safe name. New registration validates required inventory, path collisions and actual check tags. Liveness constant; startup and readiness scoped to real inventory; unknown/mismatch identity prevents corresponding new capability readiness but preserves separately authorized reads. Exposes no write-permission API.

4. TelemetryPolicyV1: closed key/value registry, approved resource values, method/template-route/status-class/duration/safe enum only; finite record/attribute/series/cardinality/rate/queue/batch/flush bounds and drop status. Sanitize before queue/aggregation and before configured sink, including automatic spans, events, links, baggage and logs. Missing approved destination/retention/permission leaves external export disabled.

5. MetricDefinitionV1: preserve technical counter names and measurement points; separately define producer committed-receipt to consumer applied-receipt latency, exact unit/clock provenance/population/duplicate/retry/reset semantics and Owner references. Adapter reports observation only; no fabricated business success or invented production threshold.

6. Versioned safe observation envelope permanently distinguishes local fixture/non-production from qualification. Keep old ReleaseIdentity and SLO1.0.0 JSON unchanged; no global reinterpretation of /health/release200, Candidate, Evaluate Pass or ProductionSloClaimed. If shape changes, expose explicit versioned contract/profile rather than mutate legacy schema.

7. Concrete bounded adapter and new public registration should compile using existing Contracts/Abstractions/AspNetCore dependencies. No Release->AspNetCore dependency or reverse package-trust shortcut; host adopts Release capability lock via opaque verified references and still must supply actual runtime observation.

真实身份观察须读取实际宿主加载模块与绑定字节，不能把预期 manifest 复制为 observed。模块 MVID/source metadata、加载后文件 hash 单独都不足以证明完整加载字节；不能证明的 artifact 类型保持 Unproven。缺证/异常/过期为 Unproven，实际不符为 Mismatch；current package trust、业务权限、独立接受各自保留独立状态。

## 必需检查

- **runtime-identity**：Versioned declared/observed identity API; actual running loopback host observations of loaded modules/bytes compared with separately pinned expected manifest. Missing observer, self-report, wrong source/version/service/environment/digest/domain, duplicate or incomplete package set, stale observation and observer exception/cancellation remain Unproven/Mismatch. No fabricated expected=actual success. Safe output omits file paths and verifier exceptions. Local match cannot imply trusted package/production/independent acceptance.
- **health-inventory**：Real isolated Kestrel host and actual local dependency stopped/restarted: live unaffected, required startup/ready dependency missing/wrong tag/unregistered/failed/timed-out/cancelled/stale -> never healthy. Empty required inventory fails closed for new capability. Hide sensitive entries without changing aggregate. Explicit route/read/new-effect inventory; GET performs no migration, replay, cleanup or Owner mutation; bounded probe concurrency/timeout/cache freshness.
- **privacy-export-boundary**：Capture actual final trace/metric/log output of new registration, including automatic inbound/outbound instrumentation. Seed full URL/query/login code, Authorization/Cookie/body/SQL parameters, tenant/user/message/operation ids, baggage, exceptions/events/links, log templates/arguments/scopes/resource and case variants. Only approved keys AND registered finite values survive; display names and routes use templates; raw paths, high-cardinality values under safe keys and unknown resources are dropped. No claim other unconfigured log/export providers are scrubbed.
- **bounded-export**：Exercise queue capacity, max record/attribute size, batching, rate/cardinality/series budget, exporter throw/block, bounded flush/dispose and repeated host lifecycle. Drop counters are safe and bounded; no waiting/retry/error crosses into request/Owner result. Overflow/missing telemetry explicitly marks incompleteness; no retrospective 100% coverage. Multi-host registration drift/global propagator replacement tested and restored.
- **owner-measurement-definition**：Versioned MetricDefinition fixes name/version/unit/kind/measurementPoint/BeforeCommit|AfterCommit|ObserverOnly/attempt-vs-business scope/allowed-values/reset/aggregation/owner/source. Define producer Owner committed receipt to consumer Owner applied receipt latency with UTC/clock provenance and skew/missing/duplicate/retry rules; unavailable real receipts -> not measured, not zero. Preserve existing published/consumed/enqueue semantics, no EF/Messaging edits or fake after-commit assertions.
- **production-claim-separation**：New observation/health/export result never grants production SLO qualification. Fixture/fault-injection/recorder evidence explicitly non-production. Candidate Pass, ProductionSloClaimed=true, source hash, valid seven-package lock or matching DI identity alone cannot set independently qualified/runtime trusted/production accepted. Existing Evaluate remains a pure legacy comparison; do not silently redefine it.
- **existing-behavior**：Reuse existing tests as regression test code: TelemetryConventionTests, ReleaseIdentityTests, SloEvidenceContractTests, ObservabilityRegistrationTests, OperationalEndpointTests, ObservabilityEndToEndTests and TraceContextDistributedPropagatorTests. Run affected filters once on candidate. Old run evidence cannot pass changed code without exact input equivalence.
- **architecture-and-evidence**：One task-level complete diff review plus local ArchitectureTests due additive public APIs/registration boundary. Preserve dependency/project/version/schema asset constraints. Record exact source commit, dirty working-byte hashes if any, dependency hashes, commands/exit/TRX/skips/environment, module/host artifact hashes, real loopback startup/HTTP and cleanup. No new GitHub Actions, DB, published package or environment adoption claim.

以下是建议执行命令，本次未运行：

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj -c Release --filter "FullyQualifiedName~ObservabilityV1|FullyQualifiedName~TelemetryConventionTests|FullyQualifiedName~ReleaseIdentityTests|FullyQualifiedName~SloEvidenceContractTests" --logger "trx;LogFileName=wp02-25-unit.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/unit
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj -c Release --filter "FullyQualifiedName~ObservabilityV1|FullyQualifiedName~OperationalEndpointTests|FullyQualifiedName~ObservabilityRegistrationTests|FullyQualifiedName~ObservabilityEndToEndTests|FullyQualifiedName~TraceContextDistributedPropagatorTests" --logger "trx;LogFileName=wp02-25-host.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/host
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj -c Release --logger "trx;LogFileName=wp02-25-architecture.trx" --results-directory docs/development/evidence/WP02-25/<unique-run>/architecture
```

现有测试可复用为回归代码；旧 WP02-01 HTTP243/Unit259 只是其原 SHA 的历史执行。未来仅在输入/依赖/环境/范围一致时复用旧结果，不移动旧总数冒充新身份/脱敏通过。保留实际 SHA、工作字节/依赖/产物 hash、原失败、TRX、HTTP 启动与清理；一次任务级 diff 审查和适用门禁，不逐步重跑。

## 原始验收边界

原始 PLT-OBS-01 全文 111 行已读，实际 SHA256 与 catalog 一致：`20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99`。原文是 Main 未跟踪归档，不伪称 Git blob。四组 AC 位于原文 41/65/83/103 行，catalog 为 `foundation.json#/targets/17`。

32 条原断言、逐项责任均在 JSON，当前全部 NOT_RUN。OBS01 A1–A7、OBS03 A1–A5、OBS04 A1/A2/A4/A6/A8 可覆盖限定库/本地宿主部分；实际 Owner commit/fleet/切流/回退、CRM 原 ready、生产 probe 权限仍外部。OBS02 只保旧 shape 回归和禁止测试样本升级生产的边界；真实 SLO 来源/计算/封存/覆盖归 WP02-26 与证据 Owner。局部 fixture 通过不升级原业务 AC、PLT-OBS-01、Main/CRM 实际采用或生产信任。

## 固定来源

- [Main docs/development/task-plans/WP02.md:1638](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/task-plans/WP02.md#L1638)
- [Main docs/development/roadmap.json:159715](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/roadmap.json#L159715)
- [Main docs/development/roadmap.json:160676](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/roadmap.json#L160676)
- [Main docs/development/roadmap.json:160743](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/roadmap.json#L160743)
- [Main docs/development/09-parallel-development.md:32](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/09-parallel-development.md#L32)
- [Main docs/development/08-postgresql-baseline.md:1](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/08-postgresql-baseline.md#L1)
- [Main docs/design-handbook/modules/PLT-OBS-01.md:13](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/design-handbook/modules/PLT-OBS-01.md#L13)
- [Main docs/development/assessments/20261011/implementation-assessment.json:16713](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/assessments/20261011/implementation-assessment.json#L16713)
- [Main docs/development/assessments/20261011/catalogs/foundation.json:6797](https://github.com/GTX537/CP6/blob/9a0155dec1c3366df8a6631f3d8fdb086aa32fc8/docs/development/assessments/20261011/catalogs/foundation.json#L6797)

关键 Platform 源码（均固定上述 e5c63a28）：`Cp6SafeHealthResponseWriter.cs:52`、`Cp6ObservabilityServiceCollectionExtensions.cs:17`、`Cp6OperationalEndpointRouteBuilderExtensions.cs:17`、`Cp6TelemetryConventions.cs:35`、`Cp6SloEvidenceEvaluator.cs:8`、`Cp6TelemetryRecorder.cs:14/50/109`；完整 37 项固定来源 URL 在 JSON。

仅完成只读准入评估。没有源码变更、构建/测试、网络、Actions、数据库、正式发布或部署操作；仅写本报告两文件。

