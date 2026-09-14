# C04A real SQL Server guard tests

These tests run the real SQL Server provider and the linked repository Foundation migration. They create a fresh `CP6_C04A_Rehearsal_Test_<guid>` database for each test and destroy only that owned database. Fixture data is synthetic; this suite is not a production acceptance or a substitute for full restored-source evidence.

An explicit `C04A_TEST_SQL_CONNECTION` environment variable is required. It must point to `master` on the local SQL Server with database create/drop rights. Missing configuration fails the suite rather than skipping tests. Supply credentials through the environment, never as checked-in files or command-line text.

```powershell
dotnet restore eng/crm/source-fence-tests/CP6.Crm.SourceFence.Tests.csproj --locked-mode
dotnet test eng/crm/source-fence-tests/CP6.Crm.SourceFence.Tests.csproj -c Release --no-restore --logger 'trx;LogFileName=source-fence.trx' --results-directory <local-evidence-directory>
```

The test matrix covers all 20 source tables, unconditional DML rejection, reads and ERP writes, ownership-chained mixed transaction rollback, raw/bulk/ALTER/TRUNCATE permissions, actual EF OUTPUT compatibility on reopen, exact table inventory and empty rows including soft deletion, local/expected database identity, permission preservation, guard/metadata/constraint drift, durable replay and generation conflicts, irreversible forward-only sealing, concurrent operators, earlier writer drain, lock timeout, cancellation and injected late-install DDL failure.

The local `evidence` directory retains red, diagnostic and green test logs from development. Raw logs contain local machine paths and test database names; curated evidence belongs in the task's project records. Library/CLI errors themselves expose only stable codes.
# Actual source inspector coverage

`ActualSourceInspectorTests` uses owned `CP6_C04A_Inspection_Test_<guid>` databases as well as the original fixture. The inspection prefix is deliberately rejected by the original mutating `SourceFence` API. Fixture defaults and original rehearsal behavior remain unchanged. The lesser-login test creates and removes only its unique owned SQL login. New checks cover identity/refusal, scope drift, nonempty source reporting, metadata visibility, encrypted modules, locks/cancellation and actual Release CLI execution. These are synthetic fixture tests, not actual source freezing or production acceptance.
