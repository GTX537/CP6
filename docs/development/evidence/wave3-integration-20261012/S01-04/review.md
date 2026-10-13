# S01-04 integration admission review

Decision: **ACCEPT / NO_BLOCKERS** for candidate bfa00a4c055bfcf4f6b7b7fe696f7a819af720c8 against Main 8569b669fed56ddc7b53b7bb24666c7fb93d544a. This accepts the candidate and existing evidence for coordinated integration; it does not claim merged-main completion.

The worktree is clean. All 12 changed files stay inside the card's FinInventory directory, InventoryDecimalTests.cs and task evidence directory. Shared Wire, both projects, packages/locks, fixed input, other tests, StockFinBridge, database and coordinator state remain unchanged. Its write window is disjoint from S01-03.

Reuse the existing complete task-level review in docs/development/evidence/S01-04/REVIEW.md. Independently recomputed all 12 input/dependency Git-blob pins, four tested source hashes (raw and committed representations), four log/TRX hashes (raw and committed representations), and six GREEN artifact hashes, including restored project assets. All match the final candidate/evidence. All pinned shared inputs also match baseline; no candidate source change outside the recorded tested files was found.

Runtime-copy vectors equal the fixed input bytes, and the test output's actual CP6.Entity.dll equals the hashed Entity build output. Restored packages match existing Test SDK 17.14.1, xunit 2.9.3, runner 3.1.4 and EF Core 8.0.30. Environment evidence records SDK 10.0.302 and actual net8.0 runtime 8.0.29.

The original GREEN TRX contains 132 unique execution records, 132 passed, zero failed/skipped, and exactly matches the manifest's name/outcome records. All nine dispatched vector IDs are present, with 123 additional cases; ratio cases are included under the executed InventoryDecimalTests filter. Original RED records 129 NotImplementedException failures and three passes. RED stub/artifact snapshots were not retained; that limitation is disclosed, and GREEN hashes are not retroactively assigned to RED. This review did not rerun either phase.

Focused source inspection confirms unlimited integer magnitude subject only to host resources, original 18/12 fractional limits checked before normalization, independent NonNegativeQty12 lexical rules, and preservation of negative zero/trailing zeros in OriginalWire. BigInteger arithmetic and destination-field validation do not round/truncate values; decoded wire text remains distinct from original JSON bytes. InventoryRatio enforces positive denominator, signed arbitrary-precision numerator, gcd reduction and exact quotient/remainder with documented toward-zero semantics. It is explicitly internal arithmetic, not a Rational/basisRef or MAVG codec. Native Rational's positive numerator and MAVG's legal 00/-0 and state/null constraints remain documented for later adoption; the dispatch card expressly permits this internal-only implementation. No new correctness or legitimate-domain restriction was identified.

Reuse scope: the exact inventory scalar/arithmetic library and tested JSON/carrier/ratio boundaries above. Native full Rational/MAVG objects, actual costing/posting Owner, C#/TS/PostgreSQL joint adoption, persistence/migration and complete business scenarios remain unverified/NOT_RUN. Any later substantive source, dependency or input change needs focused impact validation; a coordinator-doc or commit-SHA change alone does not require repeating this suite. Publication/workflow guards and protected serial merge remain the coordinator's separate final operation.

No network, dotnet/npm/business test, database, Actions, repository edit or Git mutation was performed. The review used existing local artifacts and original execution records.

Evidence identity (SHA-256 of current raw file bytes):

| Evidence file | SHA-256 |
| --- | --- |
| REVIEW.md | 10d1f0b9949a5afcfed17b284500e1095fc2f25da251374633aefea2a9ddfe89 |
| verification.json | bf6647dbc9c88a21956501a33a2e17e76b18cb6f09b270f3ce66ffb22d077ffe |
| green/inventory-green.trx | e759c6804c5948dd1f7455724580280b237f9af426f0e4598407ea9387105621 |

Recorded 2026-10-13T02:48:42.762846+00:00.
