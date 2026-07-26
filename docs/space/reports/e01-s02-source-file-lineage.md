# E01-S02 Source File Lineage

Date: 2026-07-26
Branch: `codex/space-e01-source-files`
Base: E01-S01 `90509522`

## Outcome

E01-S02 adds the source-file and artifact lineage baseline without exposing an
HTTP upload endpoint or starting the Job Ledger.

- `Space_File` owns storage identity, client/display metadata, detected type,
  SHA-256, quarantine/scan state, scan evidence, retention class, audit fields,
  and row-version concurrency.
- `Space_ModelSource` binds a clean source file (or an inline Editor/Template
  hash) to a model version and records parser, mapping, units, transform, state,
  and imported command-batch provenance.
- `Space_Artifact` binds a clean Artifact-retention file to a model version and
  optional source. E01-S03 can add Job provenance without duplicating object
  metadata.
- All new relationships include `TenantId`; cross-tenant references fail at
  both the application boundary and SQL Server foreign keys.
- The source-hash index and `ISpaceSourceCatalog` support tenant-scoped
  `SourceHash` lookup.

## Upload and safety boundary

`SpaceFileUploadService`:

1. accepts a readable stream and a server-side quarantine writer;
2. enforces the effective limit while reading (platform 200 MiB, tenant
   100 MiB, Excel 50 MiB by default);
3. computes SHA-256 incrementally with a 64 KiB buffer;
4. retains only a 4 KiB prefix for signature detection;
5. validates extension, declared MIME, and file signature for DWG, DXF, PDF,
   PNG, JPEG, and XLSX;
6. sanitizes client paths to a display-only final file name;
7. uses only a server-generated storage key;
8. reuses active tenant/hash/retention metadata and aborts the duplicate
   quarantine object.

Files remain `Quarantined` until a scanner moves them through `Scanning` to
`Clean`. Only clean files can become sources or artifacts. Full malware,
archive-bomb, encrypted-content, and active-content scanning remains E01-S06.

## Persistence

Migration:
`20260726072628_SpaceE01S02SourceFileLineage`

The migration is additive:

- adds the `(TenantId, Id)` alternate key to `Space_ModelVersion`;
- creates `Space_File`, `Space_ModelSource`, and `Space_Artifact`;
- creates tenant/hash, state, reference, and active-dedup indexes;
- uses `Restrict` for all file/version/source foreign keys;
- keeps the Space migration history in `__EFMigrationsHistory_Space`;
- contains no legacy `Space_Site` reference, column rewrite, or destructive
  data operation.

An idempotent SQL script is stored beside the Space migrations.

## Verification

- Space unit tests: 21 passed, 0 failed.
- Space integration tests with SQL Server LocalDB: 16 passed, 0 skipped.
- Existing CP6 tests with SQL Server LocalDB: 2528 passed, 1 existing SQLite
  structural test skipped.
- `dotnet ef migrations has-pending-model-changes`: no pending changes.
- SQL Server coverage includes migration application, tenant-scoped file
  deduplication, cross-tenant composite-FK rejection, one file referenced by
  multiple versions, source-hash lookup, and physical-delete restriction for a
  referenced file.

## Deferred by scope

- Web API upload/download endpoints and permissions: E01-S05.
- Job, Attempt, Step, lease, and retry ledger: E01-S03.
- Full file scanner/sandbox policy: E01-S06.
- Background physical-object retention cleanup: later worker/operations slice.
