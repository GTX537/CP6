# WP02-01 准入核对（精简）

日期：2026-10-13。**直接前置已交付，可在 COORD 登记唯一领取并冻结公共接口窗口后准入限定 Platform 实现。** 本报告不改变台账，不证明运行验收。

Main: e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47
Platform: dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351
工作树：D:/CP6.Platform-worktrees/wp02-01-20261012
分支：codex/wp02-01-trusted-context-20261012

S01-02 与 S02-00 在固定台账均 DONE；WP02-01 尚 PLANNED/NOT_CHECKED/assignment=null。无已识别直接前置缺交付阻塞。

## 已有能力与真正待补

- Immutable seven-field snapshot; no default tenant; nullable UserId/TokenId preserved
- Scoped accessor and HTTP finally cleanup; null/invalid snapshot is 403; no downstream invocation
- Existing context tests cover success/cleanup, missing context, empty tenant, forged X-Tenant
- Existing JWT validation covers issuer/audience/RS256/at+jwt/kid/required claims and lifetime; reuse
- Existing CloudEvent/bundle/topic/partition validation; not identity authorization
- No AsyncLocal found in fixed Platform src; do not invent an existing AsyncLocal leak

待补：
- Explicit per-operation scopes for message/worker with cleanup and A/B concurrency/cancellation coverage
- Independent versioned trusted-source binding evidence; tenant/issuer/actor/effectiveActor ownership must be explicit
- Current service identity differs from originalRequester audit reference; minimal scopes do not arise from null UserId or IsPublic
- Host adoption matrix separates package/reference/registration/enabled/runtime/business acceptance

兼容与串行点：
- Do not add mandatory issuer/region/epoch/actor to old Snapshot or frozen event
- Use separately versioned ContextResolutionEvidenceV1/ctx-binding-v1 candidate, signatures coordinated; effectiveActor exact type not yet frozen
- Keep legacy middleware 403 and JWT 401; authority-unavailable 503 only through explicit new host adapter
- Core scope in Abstractions can preserve current dependency graph; Messaging must not acquire AspNetCore dependency without coordinated window

## 建议正式写入白名单

以下均为 Platform 相对路径；目录仅允许本任务上下文文件，新增类型/文件逐一列入私有 PLAN。
- src/CP6.Platform.AspNetCore/RequestContextAccessor.cs
- src/CP6.Platform.AspNetCore/RequestContextExtensions.cs
- src/CP6.Platform.AspNetCore/RequestContextMiddleware.cs
- src/CP6.Platform.Contracts/Context/
- src/CP6.Platform.Abstractions/Context/
- src/CP6.Platform.AspNetCore/Context/
- src/CP6.Platform.Messaging/Context/
- tests/CP6.Platform.AspNetCoreTests/RequestContextMiddlewareTests.cs
- tests/CP6.Platform.UnitTests/RequestContextTests.cs
- tests/CP6.Platform.AspNetCoreTests/Context/
- tests/CP6.Platform.UnitTests/Context/
- docs/WP02-01-TRUSTED-CONTEXT.md
- docs/P02-REQUEST-CONTEXT.md
- docs/development/evidence/WP02-01/

只读边界：
- RequestContextSnapshot and exact seven-property IRequestContext
- IRequestContextResolver legacy signature; AddCp6RequestContext/UseCp6RequestContext compatibility
- JWT/profile/cache code; CloudEvent seven required attributes and original wire bytes
- EntityFramework models/migrations/transactions
- All project/dependency/version files and workflows
- Main/CRM files and Main shared state files

## 原验收与来源

原 catalog：docs/development/assessments/20261011/catalogs/foundation.json，/targets/13；完整断言见该固定来源，28 项全部 NOT_RUN。
- CTX01: CTX01-A1, CTX01-A2, CTX01-A3, CTX01-A4, CTX01-A5, CTX01-A6, CTX01-A7
- CTX02: CTX02-A1, CTX02-A2, CTX02-A3, CTX02-A4, CTX02-A5, CTX02-A6, CTX02-A7
- CTX03: CTX03-A1, CTX03-A2, CTX03-A3, CTX03-A4, CTX03-A5, CTX03-A6, CTX03-A7
- CTX04: CTX04-A1, CTX04-A2, CTX04-A3, CTX04-A4, CTX04-A5, CTX04-A6, CTX04-A7

原正文 SHA-256：265aecc406e50ca8117d1823d09fac61fa5694894baecdaff31df87013f8eb6f
原正文为本地归档，已核原字节 hash 与 catalog 一致，未跟踪于 Main 固定 Git 树。来源行分别为56、76、96、114。

Library subscenarios only. CTX02 routing/final fence, CTX03 current registration/revocation, CTX04 real host/Owner adoption require separate host evidence; none may be marked globally PASS by Platform tests.

S02-00 PGT-004：缺/空 tenant、未知 issuer/region、伪 schema/B tenant 在业务连接前拒绝；tenantless 健康不访问业务库；错误不泄露目录。来源：docs/development/acceptance/postgresql-tenancy.json；映射 CTX01-A2/A4/A6、CTX02-A2；NOT_RUN。

来源 pins：
- Main docs/development/assessments/20261011/catalogs/foundation.json SHA-256 6af601f92d8a3f3b347358313bd6d3c199581a0fd93650b3c6c7090249329417
- Main docs/design-handbook/modules/PLT-CTX-01.md SHA-256 b7587dd2f62a2810212b8fdc7558cbac65262bd7e8a96418f6e5c0885cda97d4
- Main docs/design-handbook/architecture/02-postgresql-tenancy.md SHA-256 dfcaea44365d92f7457db783908dd8eb6f1eaa7651c67016bdff68281a0148c3
- Main docs/development/acceptance/postgresql-tenancy.json SHA-256 b56363bd547034d45e5556bf112b41f71cbb4d7b9dc565de25876d297deefbd5
- Platform src/CP6.Platform.AspNetCore/RequestContextMiddleware.cs SHA-256 0a586bbf0fe0b983514b4e45e8124729f38aa065ebee3b92a8c446e2dc95e211

## 正反场景与验证

场景：
- A/B/A serial scopes and parallel A/B retain exact tenant and actor; each disposal clears Current
- Resolver/handler exception and cancellation leave no context for next operation
- Missing tenant, forged issuer/actor/effectiveActor and unregistered mapping fail without default fallback
- Current service registration governs worker/message; historical requester cannot impersonate effectiveActor
- Frozen seven-property interface/constructor/null values and old responses remain compatible

以下两个命令在上述工作树运行，覆盖测试项目中的新增测试，不依赖尚未冻结的类名 filter；本审计未执行：

    dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release
    dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release

保留源码/产物身份、TRX、失败及跳过记录；新增行为必须有正反自动化覆盖。两个项目测试是本地库验证，不是实际宿主/PG/Owner 验收。

## 宿主边界与未验证范围

At fixed Main pin CP6.Core/WebApi search finds no Add/UseCp6RequestContext. CrmIdentity/ErpIntegration conditionally register JWT; existing TenantMiddleware keeps default context when claim invalid/missing. Platform task cannot claim this Main fallback removed. Main storage/final gates remain WP02-02/03 and CRM adoption follow-on tasks. CRM latest source was not re-audited.

Consume cp6-pg-tenancy-v1 only; shared control/message schemas plus tenant business schemas, TenantId retained. Scope does not prove authorization/RLS/pool isolation/Owner transaction; no schema or migration writes.

Fixed main only: p10-rfc3161-preflight has pull_request limited to its own workflow and P10Rfc3161PreflightWorkflowContractTests.cs; whitelist excludes both. Other observed workflows workflow_dispatch. Recheck candidate and protected base before push/PR/merge. No modifications, trigger, run query or cancellation.

未运行：build/test, actual host acceptance, database/broker/PG isolation, migration/deployment, package publication/consumer adoption, GitHub Actions。没有取消项或触发配置变更；没有包发布、数据库或部署操作。
