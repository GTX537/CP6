# OA P0 migration and rollback runbook

Date: 2026-07-23

This is an expand/backfill release. Do not drop the legacy definition columns or the legacy draft instances during P0.

## Preconditions

1. Restore a production-sized, masked backup into staging.
2. Record database backup identifier, application version, migration head, row counts, and the operator.
3. Stop deployment if the read-only post-expand preflight reports an unpinnable Running/Suspended instance, invalid legacy draft, invalid subflow reference, or duplicate active `(TenantId, BizType, BizId)`.
4. Never place a connection string in a checked-in command log. Supply it through the deployment secret store or `CP6_TEST_SQLSERVER`.

## Reusable isolated local drill

`scripts/invoke-oa-p0-staging-drill.ps1` is the fail-closed local historical
backup drill. It is evidence in addition to, not a replacement for, the
production-sized masked-staging rehearsal in the preconditions.

Run the pure safety/helper assertions first:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-OaP0StagingDrill.ps1
```

Inspect one explicit backup and clean the generated database automatically:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/invoke-oa-p0-staging-drill.ps1 `
  -Mode Inspect `
  -BackupPath <explicit-bak-path> `
  -EvidencePath <count-only-json-path>
```

Run restore, expand, preflight, double backfill, pin/feature rollback, model
drift, real SQL, and optionally all release gates:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/invoke-oa-p0-staging-drill.ps1 `
  -Mode Drill `
  -BackupPath <explicit-bak-path> `
  -EvidencePath artifacts/oa-p0-staging-drill.json `
  -RunFullVerification
```

The script:

- uses only the local `cp6-db` container;
- generates and prints a database name matching
  `^CP6OaP0Stage_[0-9]{14}_[0-9a-f]{8}$` before mutation;
- verifies `HEADERONLY`, `VERIFYONLY`, and `FILELISTONLY`, and maps every
  logical data/log file to a run-scoped destination without `REPLACE`;
- places credentials only in child-process environment state and redacts
  captured output;
- refuses unsafe preflight or malformed backfill evidence;
- asserts every second-run `Inserted` and both-run `Errors` value is zero;
- owns the exact database and copied-container-backup cleanup in `finally`;
- validates the run marker and exact name before drop, uses single-user only
  when sessions require it, and verifies absence afterward;
- records only backup metadata, aggregate counts, timings, gate results, and
  cleanup evidence. It never automates a schema downgrade.

If an interrupted process leaves its secret-free state file, cleanup mode
revalidates the database name, run identifier, copied path, and database
marker before acting:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts/invoke-oa-p0-staging-drill.ps1 `
  -Mode Cleanup `
  -StatePath <run-state-json>
```

## Expand, preflight, and backfill

Generate and review the forward script before execution:

```powershell
dotnet ef migrations script --idempotent --project CP6.Core --startup-project CP6.WebApi --output artifacts/oa-p0-forward.sql
dotnet ef migrations has-pending-model-changes --project CP6.Core --startup-project CP6.WebApi
```

Review requirements:

- The OA P0 migrations add tables, columns, indexes, and foreign keys only.
- `UX_Wf_FlowInstance_ActiveBusiness` filters exactly on non-null business keys and `Status IN (0, 4)`.
- `UX_Wf_FormData_SubmissionKey` filters out null submission keys.
- No unrelated `DROP TABLE`, `DROP COLUMN`, or destructive data rewrite is allowed.

After backup, execute the reviewed expand SQL. The command modes deliberately do
not call `Database.Migrate`; schema deployment remains a separate, reviewed
operation.

Run the read-only preflight against the expanded schema before any backfill:

```powershell
dotnet run --project CP6.WebApi -- --oa-p0-preflight
```

Save the count-only JSON output. It must include flow/form definitions, Running/Suspended/terminal/draft instances, orphan keys, unpinnable instances/form data, invalid subflows, invalid legacy drafts, and duplicate active business keys.

Then run the transactional backfill twice:

```powershell
dotnet run --project CP6.WebApi -- --oa-p0-backfill
dotnet run --project CP6.WebApi -- --oa-p0-backfill
```

The second backfill must report `inserted=0` for every category. Compare expected/inserted/skipped/error counts with preflight; payload JSON must never appear in the log.

## Functional drill

1. Complete one v1 instance that was already Running before the deployment.
2. Publish v2 and submit a new instance.
3. Verify the old instance remains pinned to v1 and the new instance pins v2.
4. Run the real SQL Server gate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-oa-p0.ps1 -Stage SqlServer
```

This gate covers filtered uniqueness and native rowversion categories. A skipped result means the environment is blocked, not accepted.

For a restored historical stage database, the real-SQL pin/feature rollback
test is `OaP0HistoricalSqlServerTests`. It creates only synthetic rows inside
the already isolated database. It publishes v1, starts an instance, publishes
v2, proves the old/new pins and approvers differ, disables the head entry path,
completes the pinned v1 instance, and proves legacy head reads plus expanded
version rows remain. It does not claim to execute a previous application
binary.

## Rollback

Prefer an application/feature rollback:

1. Disable new submission entry points.
2. Deploy the last compatible application build.
3. Keep all new version, form data, draft, pin, and approval records.
4. Do not downgrade the database after new P0 writes exist.

Only if the database has received no P0 writes may an operator generate a migration downgrade script to the migration immediately before `20260723153450_OaP0FoundationExpand`. Review it separately and take another backup before execution. Never automate a destructive downgrade in this runbook.

## Evidence record

Attach:

- preflight JSON and both backfill JSON summaries;
- reviewed forward SQL hash and reviewer;
- `has-pending-model-changes` result;
- SQL Server test summary;
- v1/v2 pin query results;
- backup/restore identifiers and rollback decision.

## 2026-07-23 local historical-backup record

Six canonical candidates were inspected sequentially. Every inspection
database and copied container backup was verified absent before the next
candidate. The safe comparison is retained in
`artifacts/oa-p0-backup-inventory-20260723.json`.

Selected source: `CP6DB-local-sync-source-20260721-062913.bak`, 4,149,248
bytes, backup finish `2026-07-21T10:37:53Z`, original database `CP6DB`,
migration head `20260720035903_SpaceAnalyticsControlTower`. It was the newest
candidate with WF definitions, OA runtime rows, and PUR rows: 20 WF tables /
49 rows and 15 PUR tables / 35 rows, including 6 flow definitions, 3 form
definitions, 4 instances, 3 purchase requests, and 4 purchase-request lines.
The backup had zero `Wf_FormData` rows.

The retained drill evidence is
`artifacts/oa-p0-staging-drill-20260723.json`. Database
`CP6OaP0Stage_20260724024204_320e6b97` restored in 1,121 ms and expanded to
`20260724000423_OaP0DraftAccess` in 10,065 ms. Preflight was safe:
6 flows, 3 forms, 1 Running, 0 Suspended, 3 terminal, 0 legacy drafts, and
zero orphan, unpinnable, invalid-subflow, invalid-draft, or duplicate-active
counts. First backfill inserted 6 flow versions, 3 form versions, 4 flow pins,
and 3 bindings with zero errors; the second inserted zero in every category.

The real-SQL pin/feature rollback drill passed and count-only evidence showed
two published versions, two pinned instances, two distinct pins, one completed
v1, one new v2, and one disabled-but-legacy-readable head. The real SQL gate
passed 2/2 without skips; `has-pending-model-changes` and all seven
`verify-oa-p0.ps1 -Stage All` commands exited zero.

This local backup is not production-sized and has no masking evidence. No
actual previous application binary was available, so the feature rollback
compatibility result must not be called a full previous-binary rollback
rehearsal. No destructive EF downgrade was run. The exact database and copied
backup were removed and verified absent; the canonical backup was unchanged.
