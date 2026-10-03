# WP2 migration and installed model probe

This is an explicit local check for DB-COMPAT-01. It uses the actual four CP6 contexts, migration assemblies and pinned Platform package. It does not enable the PostgreSQL API runtime or replace later business, restore or production gates.

Build locally with the repository SDK and locked dependencies:

```powershell
dotnet restore tools/CP6.DatabaseCompatibility.MigrationProbe/CP6.DatabaseCompatibility.MigrationProbe.csproj --locked-mode
dotnet build tools/CP6.DatabaseCompatibility.MigrationProbe/CP6.DatabaseCompatibility.MigrationProbe.csproj --no-restore
dotnet tools/CP6.DatabaseCompatibility.MigrationProbe/bin/Debug/net8.0/CP6.DatabaseCompatibility.MigrationProbe.dll --provider PostgreSql --output tmp/wp2-pg.json --source-sha <confirmed-baseline-sha>
```

Set `CP6_TEST_DATABASE_OWNER` and the selected `CP6_TEST_POSTGRES` or `CP6_TEST_SQLSERVER` in the process environment through a local secret entry. Do not put credentials on the command line. The tool requires a literal loopback host, a dedicated `CP6Compat_WP2_YYYYMMDD_<eight-lowercase-hex>` database and matching task ownership metadata. It rejects an unmarked database before any migration or fixture write. PostgreSQL requires version 18 and the application connection pins `search_path=public`.

| Option | Actual scope |
| --- | --- |
| Default | Apply each canonical migration owner, check exact repeated histories, and compare all installed model tables and columns, native token generators, column storage/collation, ordered keys/FKs/indexes, filters and check metadata. SQL queue operations are owned by Core's immutable wrapper history; template IDs are not fabricated as applied. |
| `--catalog-only` | Read the installed catalogs without migration; it does not assert the histories are current. |
| `--write-gates` | Exercise actual EF/native/ExecuteUpdate token writes and stale rejection, tenant generation, language uniqueness, representative time/date/decimal boundaries, financial money/FK storage constraints, Space tenant-composite FK/Unicode capacity, the four Order FKs/cascades, six quotation audit column capacities and the real two-session finance posting race. Fixtures roll back or are precisely removed. |
| `--constraint-writes-only` | Run only the financial money/FK and Space tenant-composite FK/Unicode storage gates against the actual contexts and caller-owned connection. It does not assert migration currency, full catalog coverage or complete domain acceptance. |
| `--order-relations-only` | Persist a valid six-row graph through the actual Core EF context, require four independently targeted native orphan rejections with exact FK names and exercise four native delete cascades. Restore every table count, including audit/history rows, by rollback. It does not replace full order business acceptance. |
| `--quotation-audit-only` | Persist a valid Quotation/Calc/Detail graph through actual Core EF, then exercise each of its six Creator/Modifier columns with native Unicode parameters. Require exact 100 UTF-16-unit ASCII, supplementary and trailing-space values; reject 101-unit non-space overflow with SQL2628/8152 or PG23514 for the exact managed column check. Savepoints retain previous values, and the outer transaction restores all table counts, including audits/history. This checks storage and does not assert migration currency or full quotation business acceptance. |
| `--raw-order-only` | Independently compare the 65 frozen raw order defaults and four retained global unique indexes, evaluate their constants, and verify execution-time wall-clock advancement within a transaction. |
| `--catalog-negative-control` | In one transaction remove the model department index and replace it with a wrong predicate on its nullable column. Require the exact two catalog failures, roll back, and verify the original catalog is restored. No business-row DML. |
| `--sql-upgrade` | Require the exact populated Core 136 history and four known pending generation/index/FK/quotation-capacity migrations; capture existing counts and critical Snapshot/language/admin data, apply 140, verify token/data preservation, actual EF generation updates and rollback, and remove exact temporary fixtures. This mode does not also run the default migration loop. Older 136-to137 reports prove only the earlier generation component and are retained with their original scope. |
| `--history-negative-only` | Insert one unknown Core history identity in a transaction, require the same pre-migration history guard to reject it, then roll back and require the original exact complete history. No migration is applied. |
| `--seed-mode prepare --seed-state PATH` | After the actual first compiled application initialization, require the canonical budget translations, admin hash and corrected menu 708 key. Prepare custom translations/admin nickname and save all table counts and password digest in an ignored local file; refuse to overwrite an existing state. |
| `--seed-mode capture --seed-state PATH` | After a deliberate supported schema upgrade, require existing custom translation/admin fixtures and the corrected menu key, then record a new count baseline without business-row writes. This preserves the original first-initialization evidence rather than comparing an intentional migration with a repeat run. Refuse to overwrite an existing state. |
| `--seed-mode verify --seed-state PATH` | After the same compiled application initializes again, require all counts/history and prepared overrides/password to be unchanged. Include audit rows in the comparison. |

The JSON report records each executed assertion, server version, actual runtime assembly hashes, base SHA and time. Keep the applicable build log and source input hashes beside it; a base SHA by itself does not identify uncommitted implementation input. Reports never contain a connection string or password. Failed assertions are retained and produce exit code 1.

Check metadata covers names, enforcement/validation and referenced columns; arbitrary Boolean/function equivalence requires real invariant writes. Filter normalization accepts the frozen grammar and removes an `IS NOT NULL` term only when both the model and actual column are non-nullable. The negative control proves the same term on a nullable column remains a failure. PostgreSQL preserves the common microsecond temporal precision; SQL Server's extra 100 ns precision is outside that promise.

The primary modes `--seed-mode`, `--raw-order-only`, `--constraint-writes-only`, `--order-relations-only`, `--quotation-audit-only`, `--sql-upgrade`, `--catalog-only` and `--history-negative-only` are mutually exclusive. Conflicting modes fail before database access and receive a rejection scope in the report.

The quotation gate sends the full native parameter rather than allowing an EF column-length facet to truncate it. PostgreSQL raw reads retain trailing spaces and independently count four-byte UTF-8 characters as two UTF-16 units. A separate input of 100 ASCII characters followed by one extra space observes the existing SQL Server ANSI_WARNINGS behavior (the excess space is truncated) and PostgreSQL's strict managed capacity rejection. This native excess-trailing-space difference is outside the common provider equivalence promise; valid trailing spaces within the 100-unit capacity must be retained exactly.

The populated SQL upgrade fixture represents this supported isolated baseline and captured critical fields. It does not claim every existing business payload or a production upgrade was validated. The first PostgreSQL baseline has no previous supported PostgreSQL release to upgrade from.
