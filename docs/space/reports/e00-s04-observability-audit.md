# E00-S04 - Space observability and audit baseline

## Outcome

E00-S04 is verified end to end in the isolated
`codex/space-e00-inventory` worktree. The HTTP publish boundary, audit
ledger, failed SPACE outbox event, real retry dispatcher, and retry
finalizer form one queryable observability chain. The new
`SpaceObservabilityChainTests` test moved from fixture-contract RED to
GREEN without a production-code fix.

No staging, commit, migration execution, root-worktree write, or
`space-volume1` write was performed. E01/`SpaceContext` has zero matching
changed or untracked paths.

## Execution context contract

`SpaceExecutionContextMiddleware` requires one authenticated internal
identity, an exact tenant claim matching `ITenantContext`, a non-empty actor
identifier, and a W3C trace. It accepts a valid inbound
`X-Correlation-ID`, creates one only when the header is absent, and rejects
invalid or ambiguous identity data.

The chain test proves that all four audit rows and the final integration
event have exactly one `TenantId` and one inbound `CorrelationId`. The
original failed event, user success audit, retry-start audit, and retry
success audit retain one non-empty `JobId` and `PublishAttemptId`. The
system retry uses a new non-empty `RunId` and trace; it does not create a
replacement correlation to hide a broken chain. The HTTP `Started` row
correctly precedes allocation of publish/job identifiers.

## Fail-closed boundaries

The global Space mutation audit filter appends `Started` before invoking a
controller action. If that append is unavailable, the action and its
adapter are not called and the response is
`SPACE_AUDIT_UNAVAILABLE`. A successful action whose mandatory outcome
cannot be appended is surfaced as `SPACE_OPERATION_OUTCOME_UNKNOWN`.
Tenant, actor, trace, correlation, and external-subject validation remain
mandatory.

Worker retries likewise require the retry `Started` audit before dispatch.
The retry lease, attempt update, terminal event state, and outcome audit are
fenced; the finalizer will not claim success after losing ownership.

## Audit ledger and redaction

`Space_AuditEvent` records actor, action, resource, outcome, tenant,
correlation, trace, job, run, publish attempt, and attempt number. Evidence
uses the bounded `SpaceAuditEvidence` allowlist and is truncated to a safe
sentinel above the storage limit. Stable reason codes, SHA-256 hashes, and
tenant consistency are validated before persistence.

The SPACE event API and `SpaceEventsView` expose safe identifiers and a
safe error code, not raw `LastError` or `PayloadJson`. The final scans found
no `LastError`/`PayloadJson` reference in Space controllers or that view,
and no `ex.ToString()` in `SpaceBridgeHook`. The one remaining
`ex.ToString()` in `IntegrationEventRetryWorker` is in the explicitly
unchanged non-SPACE branch; SPACE errors use the sanitizer and stable
storage codes.

## Publish and retry propagation

The new chain test uses a shared InMemory database and the real middleware,
audit writer, `LocationPublishService`, `SpaceBridgeHook`,
`IntegrationEventDispatcher`, `IntegrationEventRetryWorker`, and
`SpaceRetryFinalizer`. Only a fail-once WMS consumer and unrelated external
constructor boundaries are faked.

The first WMS rejection persists a failed event at attempt 1. After making
that event due, one real worker pass dispatches it with a retry fence,
records system `Started` and `Succeeded` audits at attempt 2, and leaves the
same event in `SUCCESS` with no error, next retry, or lease. The consumer is
called exactly twice, and the second call carries the original event ID in
its fence.

The GUID scan found no correlation creation in
`IntegrationEventDispatcher`. `LocationPublishService` GUID creation is
limited to adopted entity IDs and explicitly named publish-attempt IDs.

## Query permission

Audit query, timeline, and compatibility event-read routes require
`space-audit:read`. Authorized audit reads append safe read evidence with
the permission code, authorization result, and item count. Permission
denials use stable `SPACE_PERMISSION_DENIED` audit evidence. The permission
reflection tests include both audit query actions.

`AuditQueryEnabled=false` returns
`SPACE_AUDIT_QUERY_DISABLED` without weakening mutation auditing or
execution-context checks.

## Metrics and configuration

The configuration section is `SpaceObservability` with these keys:

- `AuditQueryEnabled`
- `MetricsEnabled`
- `LegacyIntegrationEventTimeZoneId`

When enabled, the collector publishes the registry-safe gauges
`cp6_space_audit_event_total` and
`cp6_space_audit_event_by_outcome`. Disabling metrics avoids resolving the
collector and does not alter the existing metrics endpoint or bridge
metrics. The legacy timezone key is used only for historical SPACE
integration rows that lack `OccurredAtUtc`; missing or invalid required
configuration fails closed.

## Migration

The cumulative E00 migration set is:

1. `20260725144609_SpaceE00S04ObservabilityAudit`
2. `20260725174242_SpaceIntegrationEventRetryLeaseFence`
3. `20260725181400_SpaceRetryCompletionAndDeadLetterOutbox`
4. `20260725203000_SpaceIntegrationEventOccurredAtUtc`

Before deployment, if any historical SPACE row has
`OccurredAtUtc IS NULL`, set
`SpaceObservability__LegacyIntegrationEventTimeZoneId` to the timezone used
by those legacy event timestamps. Missing or invalid required configuration
fails closed rather than guessing the historical timezone.

An idempotent SQL script bounded from the preceding
`20260714075419_WfsSubFlow` migration through the fourth E00 migration was
generated to an explicit worktree temp file. Review found 4 migration
history inserts, 4 transactions and commits, and only the four E00
migration IDs. It contains no `DROP`, `DELETE`, `TRUNCATE`, or `MERGE`.
Its two data updates are intentional, scoped backfills: mark existing SPACE
dead letters as already notified, and copy legacy SPACE `CreateDate` into
`OccurredAtUtc` only for rows with a non-empty `JobId` different from the
event ID. It does not mutate `PayloadJson` or `LastError`.

The resolved temp path was validated as the exact expected child of this
worktree before deleting only that file. EF reports no pending model
changes. SQL Server LocalDB verification left zero `CP6Test_*` or
`CP6SpaceUtcTest_*` databases.

## Rollback

Rollback must obey these iron laws:

- `AuditQueryEnabled` and `MetricsEnabled` may be disabled.
- Tenant, actor, and external-subject validation must not be disabled.
- High-risk audit writes must not be stopped.
- The `Space_AuditEvent` table or its rows must not be deleted.
- Do not execute an E00-S04 EF `Down` migration or database downgrade: it
  would drop the audit ledger or observability columns.

Only code/config rollback or a forward-fix is permitted. Code/config
rollback may remove optional query/metric exposure, but it must retain
execution identity, mandatory mutation auditing, retry identity, and the
append-only audit ledger. The audit ledger must never be deleted.

## Verification

- New observability chain: 1 passed, 0 failed, 0 skipped. Initial RED was
  `E-SPACE-307` from lower-case fixture JSON; a second fixture expectation
  assumed attempt 0 although the existing outbox contract starts at attempt
  1. Correcting those test fixtures produced GREEN without production
  changes.
- E00-S04 backend target: 244 passed, 0 failed, 0 skipped.
- Full backend, default provider: 2,522 passed, 0 failed, 7 skipped,
  2,529 total.
- Full backend, SQL Server LocalDB enabled: 2,528 passed, 0 failed,
  1 skipped, 2,529 total. The remaining skip is the existing structural
  `BudgetSqliteTests` case.
- Backend solution build: 0 warnings and 0 errors.
- Task 9 plus data-source frontend target: 7 passed across 2 files.
- Full frontend: 494 passed across 74 files, exit 0, with no unhandled
  rejection.
- Separate historical `SpaceCodeRuleView`/`ElSelect` reproduction:
  5 passed, exit 0, and no unhandled rejection in the current runtime.
- Frontend build-only: passed with 2,649 modules transformed; only the
  existing large-chunk advisory was emitted.
- Frontend type check: passed.
- Inventory scanner: 9 passed; after the authorized two-report refresh,
  `--check` passed with 59 baseline endpoints and 79 candidate files.
- `git diff --check`: exit 0. Existing line-ending conversion notices were
  emitted.
- Status scan: no `bin`, `obj`, `dist`, generated `.js`, `.pyc`, or
  `__pycache__` path appears in changed or untracked status.

The inventory reports were stale because external state moved. Stored
baseline and current baseline are both
`codex/space-e00-inventory` at `1524289` with 452 Space files. Stored
candidate state was `feat/gr-vp-t6` with 368 changes, 59 endpoints,
11 tables, 1 test, 31 permissions, and contract statuses
Implemented 2 / Partial 0 / NotStarted 11. Current read-only sibling state
is `codex/space-volume1` with 79 changes, 17 endpoints, 25 tables, 7 tests,
13 permissions, and statuses Implemented 2 / Partial 8 / NotStarted 3.
The root worktree remains `feat/gr-vp-t6` with 368 status entries. Because
the sibling changed externally, the "two dirty worktree counts are
unchanged from startup" condition cannot be certified literally; all task
writes were confined to the isolated E00 worktree.

## Pre-existing unrelated failures

`npm run i18n:check` remains red with exactly 758 code-referenced keys
missing from the database snapshot. It is outside E00-S04.

The historical 15 Element Plus `ElSelect` recursive-update rejections
documented by E00-S03 did not reproduce: both the complete 494-test run and
the isolated 5-test file exited cleanly. No E00-S04 unhandled rejection was
observed.

When the new test first forced a test-project rebuild, existing warnings
were visible in `SpaceRetryLeaseMigrationTests` (2 nullable warnings),
`PendingCookieTests` (3 nullable warnings), `BudgetVsActualTests`
(xUnit2012), and `InboxServiceTests` (xUnit2031). The final solution build
was clean. Default-provider skips were the 6 SQL Server-gated Space tests
plus the existing `BudgetSqliteTests` skip; enabling LocalDB ran all 6 SQL
Server cases successfully.
