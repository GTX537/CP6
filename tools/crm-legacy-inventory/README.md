# C04B legacy reference inventory

Python 3.8+ and Git are sufficient. Run from the Core repository:

```powershell
$revision = (git rev-parse HEAD).Trim()
python -B tools/crm-legacy-inventory/legacy_inventory.py --root . --revision $revision --output inventory.json --require-no-runtime-references
python -B -m unittest discover -s tools/crm-legacy-inventory -p test_legacy_inventory.py -v
```

The scanner reads only the full immutable commit's Git objects, never local configuration or dirty/untracked files. It matches the fixed 20 legacy tables, entities, DbSets, enums and state machine. C01/C02 identity and C03 ERP associations such as `CrmAccountId` remain valid. Reports contain paths, line numbers, identifiers and hashes, without source lines or values. Output files must be new.

Exit codes: `0` valid inventory with no required blocking references, `2` runtime/entity/current-model-snapshot/unclassified references remain when the gate is requested, `1` invalid or unreadable input. Historical migrations, test fixtures and operational source-protection tools are retained and reviewed separately. Unknown code locations, invalid UTF-8, oversized blobs, symlinks and submodules fail closed.

This is a conservative lexical inventory, not a compiler graph or proof of database write absence. Dynamic SQL, reflection, external dependencies and omitted extensions require independent review. The tool does not assess overall development acceptance or production adoption. The C04B decision and assembled model, SQL, build and local HTTP evidence determine development acceptance; production acceptance is separate.

Historical legacy test sources now live in `eng/crm/legacy-model-fixture`, compiled only by `CP6.Tests`. `LegacyCrmFixtureContext` preserves their historical model. Current production-model and populated SQL retirement tests live in `eng/crm/legacy-exit-tests`:

```powershell
dotnet test eng/crm/legacy-exit-tests/CP6.Crm.LegacyExit.Tests.csproj -c Release --filter FullyQualifiedName~LegacyExitModelTests
# Set C04B_TEST_SQL_CONNECTION privately to a local SQL Server master connection.
# Set C04B_SQL_EVIDENCE_PATH to a new report path. SQL acceptance never skips.
dotnet test eng/crm/legacy-exit-tests/CP6.Crm.LegacyExit.Tests.csproj -c Release --filter FullyQualifiedName~LegacyExitSqlTests
```

SQL acceptance creates its own GUID-named database, constructs the current model and historical foundation tables, seeds synthetic rows, positions synthetic migration history immediately before the retirement marker, and applies that marker. It tests this incremental migration, not the full historical migration chain. It verifies row hashes and column metadata hashes (not a full index/FK fingerprint), current identity/ERP storage and tenant-scoped reads, then removes only its owned database after identity checks. The production migration has no table/data operations; physical table removal is a separate future decision.
