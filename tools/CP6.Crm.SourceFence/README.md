# C04A local source fence

This standalone .NET 8 library and CLI rehearses source-write fencing on **local, isolated copies**. It does not modify application startup, EF mappings, production migrations, routes, target writes, ERP/C01/C02 behavior, or release workflows. It does not close C04A.

The separate [`inspect-actual` command](ACTUAL-INSPECTION.md) can read an explicitly identified actual local source. It has no mutation API and does not relax the rehearsal-only checks below. A matching inspection scope is not approval to freeze, migrate, switch routes or reopen.

Only a database whose exact name starts with `CP6_C04A_Rehearsal_` is accepted. The caller must also supply its expected `sys.databases.service_broker_guid`; the connected catalog and actual SQL Server machine must match. System databases, `CP6DB`, remote SQL Server, attached files, failover partners and read-intent connections are rejected. The broker GUID survives normal restore: it checks expected identity together with the name, and is not a unique restore incarnation token. Creation/restore of the isolated copy is a separate operator action.

The source profile is the exact 20 `dbo.Crm_*` tables from `20260811030108_CrmFoundation.cs`. Every row is counted, including soft-deleted rows. Freeze and preflight reject any row. Status reports reopened/uninitialized row counts; status on a fenced source rejects unexpected rows. Table inventory is checked; a full source column/index/relationship comparison is **not** implemented by this library.

## Explicit invocation

Restore with `dotnet restore tools/CP6.Crm.SourceFence/CP6.Crm.SourceFence.csproj --locked-mode`, then build with `dotnet build tools/CP6.Crm.SourceFence/CP6.Crm.SourceFence.csproj -c Release --no-restore`.

Set environment variables in the calling process without putting credentials on the command line:

| Variable | Meaning |
| --- | --- |
| `C04A_SQL_CONNECTION` | Connection string selecting the existing isolated copy |
| `C04A_EXPECTED_DATABASE` | Exact rehearsal database name |
| `C04A_EXPECTED_DATABASE_GUID` | Expected `service_broker_guid` |
| `C04A_RUN_ID` | Nonzero GUID for mutation commands |
| `C04A_EXPECTED_GENERATION` | Exact current generation before mutation |
| `C04A_LOCK_TIMEOUT_MS` | Optional, 100–60000; default 5000 |
| `C04A_COMMAND_TIMEOUT_SECONDS` | Optional, 1–300; default 30 |

Then invoke the built assembly explicitly:

```powershell
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll preflight
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll freeze
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll status
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll reopen
dotnet tools/CP6.Crm.SourceFence/bin/Release/net8.0/CP6.Crm.SourceFence.dll seal-forward-only
```

Each line is a separate operator command, not a script to execute all transitions. Successful commands emit one JSON object and return 0. Rejected commands emit only `{ "error": "C04A_..." }` to stderr and return 2. Connection strings, SQL exception text and source field values are excluded. The same operations are available as `SourceFence` instance async methods using `SourceFenceOptions` and an optional cancellation token.

## Durable transitions

| Current state | Command | Result |
| --- | --- | --- |
| Uninitialized, generation 0 | Freeze with unused run ID, expected 0 | Frozen, generation 1 |
| Frozen | Reopen with same run, exact generation | Reopened, next generation |
| Reopened | Freeze with a new, never-used run ID | Frozen, next generation |
| Frozen | SealForwardOnly with same run, exact generation | ForwardOnly, next generation |
| ForwardOnly | Reopen / new Freeze | Rejected |

Sealing is an irreversible **operator decision before target-write enablement**. It is not evidence that a target write occurred. This slice has no target-write integration. Repeating an identical successful command is safe only while its exact next generation remains current and no transition has intervened; it appends no second audit row. Stale generations, cross-run commands, reused run IDs and resets are rejected.

The first successful freeze creates additive `crm_source_control.Control` and `crm_source_control.Audit` tables, an audit update/delete rejection trigger, public schema mutation denies, and a database extended-property version marker. Subsequent reads and commands validate metadata shape, required constraints, audit continuity and current state. Status/preflight never initialize DDL. Ordinary callers cannot mutate metadata directly; database owners and sysadmins remain privileged.

## What the fence verifies

Every transition takes a transaction-owned exclusive operator application lock, then retains `TABLOCKX,HOLDLOCK` locks on all 20 source tables in fixed ordinal order. The counts drain prior writers and run under those locks. Guard installation/removal, permission changes, lifecycle state and the audit record commit atomically. Timeout, cancellation, a nonempty source or DDL failure rolls back the operation. Read-only status uses a shared operator lock and retained shared table locks for consistent inspection.

Frozen and ForwardOnly states require one exact, enabled, unconditional `AFTER INSERT, UPDATE, DELETE` trigger per table, without replication exemptions. Each throws SQL error 51041, `C04A_SOURCE_FROZEN`, even for zero-row DML. These triggers also reject DML through ownership-chained procedures. Public object-level `DENY INSERT, UPDATE, DELETE, ALTER` blocks ordinary raw/bulk writes and ALTER/TRUNCATE bypasses. Existing public mutation permissions at object or column scope are rejected before installation rather than overwritten. Reopen removes only the owned four denies and triggers; unrelated permissions remain intact. Drift causes refusal, with no repair or force option.

Enabled triggers can cause legacy EF SQL Server `OUTPUT` statements to fail with SQL error 334 before the trigger runs. That still rejects the write. Reopen removes source triggers and restores the unchanged EF mapping's normal `OUTPUT` behavior. Bulk writes omitting `FireTriggers` can fail with SQL error 5304 due to denied ALTER permission; this is a permission rejection, not a trigger execution claim.

**`completeWriteFenceVerified` always remains false.** DENY cannot constrain database owners/sysadmins; privileged bulk/DDL/TRUNCATE, ownership changes and metadata destruction remain possible. The real application/worker identities and live writer inventory still require evidence and isolation. `fullSourceColumnSchemaVerified`, `targetWriteIntegrationVerified` and `forwardOnlyIsActualTargetWriteProof` also remain false. Passing local guard tests or a restored-copy rehearsal does not establish production acceptance.

## Verification

The SQL tests under `eng/crm/source-fence-tests` create uniquely named owned local databases using the linked real Foundation migration, plus synthetic test data. They require a master administrator connection in `C04A_TEST_SQL_CONNECTION`; no SQL-unavailable skip is allowed. All probes stay inside their own fixture databases and cleanup validates the exact unique fixture prefix. A separately restored full source copy remains an additional acceptance step.
