# Numeric admission: A03/S01-03 and B04/S01-04

Assessment baseline: d7a210256783e45291a5e724ea068ed4fc165ecc. Local HEAD/main/origin-main match; coordinator supplied fresh remote verification. No network, tests/builds, Git mutations or repo changes were performed. Only this report is written outside the repo.

## Decision

Both tasks can run concurrently after coordinator records separate assignments, CONFIRMED gates and write windows. They are still PLANNED/NOT_CHECKED/unassigned in the inspected roadmap; this assessment is not an entry grant. Both depend on merged S01-02 only, not each other, S01-05 or S01-10. All three exact numerical originals exist and hash-match; no numerical input blocker.

S01-02 is DONE via PR184: source 3890b4fdbda47c3762b9f51ea75b8c156540c69f, recorded remote merge fb99e35a38fbf14e86005aecbc8a9cf94c63cb17. Prior evidence records 77 passed/0 skipped, including 21 frozen vectors, on net8/.NET8.0.29 with SDK10.0.302; tested source binding 2c0ef2129012a8117b0b5b058ac3c470e12d73fd. Read docs/development/evidence/wave2-integration-20261012/S01-02/{original-validation.json,remote-main.json}; no tests rerun here.

## Exact parallel windows

| Worker | Exclusive writes |
|---|---|
| A03 | CP6.Entity/Contracts/FinCredit/CreditAmount.cs, CreditAmountJsonConverter.cs; eng/contracts/exact-values-tests/CreditAmountTests.cs; docs/development/evidence/S01-03/** |
| B04 | CP6.Entity/Contracts/FinInventory/InventoryDecimal.cs, InventoryRatio.cs; eng/contracts/exact-values-tests/InventoryDecimalTests.cs; docs/development/evidence/S01-04/** |

Necessary profile enums/helpers stay in these source files or task's domain directory; no new shared numerical helper. Put ratio tests inside InventoryDecimalTests so the original task-card filter executes them. Additional shared files require coordinator allocation.

Both read-only: CP6.Entity/Contracts/Wire/**; existing StrictJsonTests.cs; both csproj; Directory.Build.props; solutions/locks; eng/contracts/s01/**; schema/original/remediation assets; financial interfaces/services. No EF model/migration, DB fixture, TS, posting/credit decision changes. Coordinator alone edits roadmap/generated views/four status files in serialized delivery windows and merges tasks individually. Shared infrastructure changes must be merged once before consumers adopt them; never depend on the other unmerged branch/worktree. Root contains unrelated untracked assets; create independent worktrees from then-confirmed latest main.

## Current engineering surface

Leaf CP6.ExactValues.Tests.csproj uses net8.0 with SDK default source globbing, direct CP6.Entity reference only, Test SDK17.14.1/xunit2.9.3/runner3.1.4. Entity is net8.0 with EF Core8.0.30 and existing packages.lock.json. Root props enable lock-file restore for Entity, not the leaf. Framework BigInteger/System.Text.Json are available. No new package or project/solution edit is needed for these source/test additions.

Trap: leaf copies vectors.json as Inputs/vectors.json and wave2 dispatch as Inputs/dispatch.json. StrictJsonTests uses wave2 dispatch to select S01-02's 21 IDs. Do not repoint that copy to wave3 or edit StrictJsonTests. Each new class reads existing vectors and asserts its own exact profile IDs/count. Read/pin manifest at admission; source-relative manifest loading or task-local field bindings with provenance avoid csproj changes. If another copied asset is truly needed, coordinate one shared-project edit first.

StrictJson.Parse/Deserialize/GetOriginalUtf8 already provide the public boundary. New converters must enforce their field profiles and scalar token types. Decoded string roundtrip cannot prove escaped JSON-token bytes were retained; use retained original UTF8 for replay/digest assertions.

## Input pins

Package CP6-S01-CONTRACT-INPUTS/1.0.0. Native numerical sources are original-only, no composed variant or patch bundle; R1 storage representation is a separate pinned asset. Hashes below were recomputed against actual HEAD blobs/native bytes. Read archived schema/data, never execute archived programs/SQL.

| Repository-relative path | SHA256 Git blob bytes |
|---|---|
| eng/contracts/s01/profile-inputs.json | 2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e |
| eng/contracts/s01/vectors.json | b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01 |
| docs/superpowers/plans/2026-10-11-s01-task-breakdown.md | 2e4279a28110887ca3f1ff0418811b52f9010115545783d22b8c4123050738d8 |
| docs/design-handbook/remediation/01-version-and-authority.md | 495c8a2b821d3c5aed2b7d40e743351db6b3b8e4615f8d8f9bc25cd998fd1a7e |
| docs/design-handbook/remediation/06-wire-types-and-fixtures.md | 8dabf960916e6881f6d73c4dbee17b8cbea4b3d2d2a3a664650eee03ec1192d9 |
| docs/design-handbook/remediation/assets/core-types/numeric-storage-1.0.0.json | b67a8fea437a1db1990c3513a09cd9bc791704aac3b3a8f3f9b80642b5d4a607 |
| docs/design-handbook/remediation/assets/core-types/numeric-validation.json | 913057ab2df1788b1043931cd64d6dd430c50be049d3f1f1cdffc41660cfcc43 |
| docs/design-handbook/remediation/assets/core-types/DesignRevisionTypes.cs | a7ab417cabf201e8ec7b716360539b18fc0db751d180bb05b0b18f7dc3a3c3db |
| docs/design-handbook/remediation/assets/core-types/DesignTypesCheck.cs | 44e4ceb8b1f0d4b673f4745c2508e1b55fb37cad58b84a9ef160ff9424ec93b2 |
| eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj | 5178d2c0e8bc50523d5b24f93f6aa95f4083fb3810355ccc389d3b6a7c67bd0e |
| eng/contracts/exact-values-tests/StrictJsonTests.cs | d3aadd552e7937b6def544971386c5b4330dc2007aa1a97fd1d340e04975fc94 |
| CP6.Entity/Contracts/Wire/StrictJson.cs | 9a2a22e31c32f24235ad4b2ac8cb93f625ab1c75e9e358251b9dd54e001ab80d |
| CP6.Entity/Contracts/Wire/NullOnly.cs | 3fa80c80928c4ce04b904a98b1bb668445412732c635eb77b0d01e050fcada4b |
| CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs | 61afae5efd22fbff880d9a5c17f9c5ec465381533fd880e692f9ba648b469eee |
| CP6.Entity/CP6.Entity.csproj | bf8b9c3342cb7288a4671e9104030a970a5d12c5db64714f7f7b8214cea4084d |
| CP6.Entity/packages.lock.json | ff90d3bb5b2ea55f88c84d3fb0a4a33afd5eae54fb2e19b4898c358b290560f3 |
| Directory.Build.props | 0e9cc0316d9d617b284096544378e17d8890910d5154e0a6b9e664c7ac86015d |

Package JSON raw file hashes also match (LF). For hash_basis=git_blob_bytes use git -c core.longpaths=true cat-file blob <pin> or raw subprocess bytes, not text redirection/CRLF conversion.

| Native source / selector | SHA256 file bytes | Actual read path |
|---|---|---|
| numeric-0 / /source_integrity/0 | f768bfcd2f485e00587569978d2a0763a9c0bb70059d3eeb85e0e2591164d463 | D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/f7/f768bfcd2f485e00__FIN-CREDIT-WIRE.schema.json |
| numeric-1 / /source_integrity/1 | 3a61c4b68b8f5c01f65025a9fae5647188446569762e4315237f5f1d4fa0ba65 | D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/3a/3a61c4b68b8f5c01__CLOSED-WIRE-SCHEMA_R10.json |
| numeric-2 / /source_integrity/2 | 8d2b80a29721bd7381c0cb08dc3b377e5f7540ca088c2d7e9ae3be732183da1e | D:/CP6/docs/CP6_完整成果归档_20261009/完整成果/docs/originals/8d/8d2b80a29721bd73__DATA-DICTIONARY_R10.json |

Archive paths are admission evidence only; no machine/archive runtime dependencies in implementation/tests.

## All fixed profile vectors

All are lexical / CONTRACT_UNIT. Assert every exact ID, accepted/rejected, expected coefficient/scale; INV also originalWire. Missing/extra/unsupported selected cases must fail.

| Profile | Count | IDs |
|---|---:|---|
| fin-credit-signed | 14 | NUM-001, NUM-002, NUM-003, NUM-004, NUM-005, NUM-006, NUM-007, NUM-008, NUM-010, CREDIT-005, CREDIT-006, CREDIT-JS-001, CREDIT-JS-002, CREDIT-JS-003 |
| fin-credit-nonnegative | 6 | NUM-011, CREDIT-001, CREDIT-002, CREDIT-007, CREDIT-009, CREDIT-011 |
| fin-credit-positive | 5 | NUM-009, CREDIT-003, CREDIT-004, CREDIT-008, CREDIT-010 |
| fin-inv-decimal18 | 4 | NUM-012, NUM-015, INV-003, INV-004 |
| fin-inv-qty12 | 5 | NUM-013, NUM-014, NUM-016, INV-001, INV-002 |

CREDIT total25; INV total9. NUM017..023 belong to ATP. Fixed profile subsets contain no arithmetic, ratio, profile-misuse or JSON-token vectors; add task-private coverage, do not change common vectors/package statuses.

## A03 API/coverage requirements

- Bind https://cp6.example/design/fin-credit-wire-r4.schema.json $defs/signedDecimal, nonNegativeDecimal, positiveDecimal. Manifest has 4 signed/33 nonnegative/5 positive field pointers. Use exact bindings, never field-name guesses (fxEvidence.rate and planAtpObservationInput.requestedQuantity are positive; planAtpObservation.promisedQuantity nonnegative).
- Exact coefficient/scale, explicit profile parse/validation, exact compare/add/subtract or arbitrary-precision accumulator, explicit final-field checked emission. API spelling is worker design. No decimal/double/float intermediary. Signed parse cannot bypass positive/nonnegative emission validation; unknown profile rejects.
- Accept 38 integer digits, negative38 only signed, 30+8 fractional digits. Reject39 integer/31+8, scale9, -0, trailing fractional zeros, exponent, leading zeros, plus/whitespace/wrong tokens. Validate exact native pattern, not only magnitude.
- Test max38+1-1 succeeds but final max38+1 rejects; unequal-scale alignment/comparison, negative intermediate before positive result, canonical zero. No intermediate clamp, forced scale8, rounding/saturation/truncation.
- JSON strings roundtrip per profile; number/boolean/object/array/null reject for required scalar. Nullable/optional remains enclosing-field rule. Strict duplicate/escaped-key carrier tests use unchanged Wire. Preserve original byte evidence separately.
- CreditControlService.CheckCreditAsync(string, decimal), ICreditControlService and CreditCheckResult remain unchanged; current service computes decimal invoice round2. Switching these without all-writer/Owner adoption is out of scope.

## B04 API/coverage requirements

- Bind urn:cp6:fin-inv:r10:wire $defs/Decimal18/Qty12. Unlimited integer digit domain, fractional limits18/12. BigInteger coefficient/numeric scale/original lexical wire are separate. -0.000 normalizes numerically to (0,0) but OriginalWire stays -0.000.
- Explicit decimal/qty parsing, exact compare/intermediate arithmetic, string serialization and original-wire retrieval. Field outputs enforce their fractional limits without silent rounding. Test >90 integer digits to demonstrate90 is sample not cap; 18/12 accepts,19/13 rejects; negative/zero/tail zeros, exponent/plus/leading-zero/whitespace/wrong JSON tokens. Operations cannot rewrite original input.
- InventoryRatio: arbitrary signed numerator/positive denominator, zero/negative denominator rejected at boundary, internal gcd reduction/exact remainder. Test >90-digit parts, signs, zero normalization, exact/nonexact +/-7 over3 and numerator=quotient*denominator+remainder under documented division semantics. No new display-rounding or MAVG-posting policy.
- Critical native distinction: $defs/Rational requires positive numerator AND denominator plus basisRef. MAVG exactAverageNumerator/discardedRemainderNumerator instead use ^-?[0-9]+$ (legal00/-0); denominators ^[1-9][0-9]*$. Base fields nullable; ACTIVE requires values, EMPTY requires nulls. Internal canonical digits/gcd must not narrow original wire domains or rewrite raw evidence. If a MAVG string codec is exposed, preserve original parts and test these forms. If only internal arithmetic is offered, explicitly defer native Rational/MAVG object adoption.
- NonNegativeQty12 and state/field overlays retain their own constraints; scalar helpers do not certify complete objects.
- StockFinBridge remains read-only: current decimal abs(round(Qty*UnitPrice,2,AwayFromZero)) bridge dispatches voucher events, not the new FIN-INV cost ledger Owner. No posting/migration.

## Proposed worker commands and delivery

Commands below are original task-card commands, proposed only, not run here:

    dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~CreditAmountTests
    dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~InventoryDecimalTests

Run in each isolated worktree. Keep B04 ratio tests in InventoryDecimalTests. Preserve unique RED/GREEN logs/TRX/case IDs/counts, actual SDK/TFM/runtime, source/input/config/dependency/artifact hashes in task-only evidence. Compile failure/missing class/zero tests cannot count as executed passing coverage. Required final result: all task assertions run, zero failed/skipped. No solution/DB/TS tests needed. Unchanged StrictJson results may be reused with original provenance; new strict-carrier tests execute under each new filter. Broaden only when shared inputs actually change.

One full task diff review at delivery. Keep package NOT_RUN and source historical PASS unchanged. DR-OPS-IF-002 POS/NEG and DR-OPS-IF-030 POS/NEG gain contract subassertion evidence only; full business/Owner/storage/financial effects stay NOT_RUN. S01-10 consumes merged APIs later. Coordinator integrates shared state and serializes protected merges, checks drift and remote-main containment. Candidate/protected workflows must be checked before push/PR; no Actions runs/trigger edits, DB or deployment occurred in this assessment.

Remaining gates: coordinator registration/current baseline confirmation, exact file windows, no shared csproj/helper edits without separate allocation. No missing numerical source blocker.
