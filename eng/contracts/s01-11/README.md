# S01-11 portable assets and local verification

This directory adopts the bounded ERP-CREDIT native codec surface. It does not
connect native Owner writers, migrate legacy CreditNote storage, implement SQL
ports, or establish real Store/issuer/business acceptance.

`assets/native` retains the exact WMS schema `b324a9b6…` and FIN-AR schema
`8a7864b9…`. `assets/current` retains registry `564706ce…` and the composed
single-root OpenAPI `64e7bdda…`; `assets/frozen` retains registry `62effa1e…`.
Full hashes and original/selected source and patch selectors are in
`provenance.json`. Raw assets and fixtures are `-text` so checkout cannot change
their byte identities.

`surface.json` records original/composed generated-source range hashes, the 14
published factory roots, two scalar helpers, two dedicated replay roots, and
the complete three-file schema closure. The archived generator is design input,
not trusted runtime code. Its generated DTOs map to complete immutable native
JSON documents with exact original bytes. C# uses the delivered net8 StrictJson
and JsonSemanticEquality; TS uses own-property lookups, null-prototype objects,
exact number tokens and private snapshots. The archived SQL Server ports and
Owner dispatch code are omitted. No archive program is executed.

`fixtures/native-objects.json` contains constructed unit objects. `fixtures/cases.json`
distinguishes constructed contract inputs from selected design projections. The
shared fixed vectors remain in `../s01/vectors.json`; tests select VERSION-001..008
and BYTES-001..002 explicitly. The existing wave2 `Inputs/dispatch.json` is not
changed or used as a wave4 task manifest.

`fixtures/current-bodies` preserves five selected composed body files byte for
byte. `fixtures/current-chain.json` contains 343 leaf projections with original
source IDs and JSON pointers: one lookup binding, five body directory/rawRef8
entries, five Store7 entries, five CURRENT04 entries, two CURRENT05 entries,
324 CURRENT12 entries and one CURRENT13 entry. Unrelated historical SQL recipes
are excluded. Eighteen Money positions are validated by the native codecs in
both languages; the MULTI native outcome is explicitly tested as a nested
projection. This is not a whole-C06 decoder or whole-public-body acceptance.
Receipt symbols remain design data: actual issuer/native codec/publisher/Store
receipts remain absent, and new metadata writes remain zero.

Reproduction from this repository requires no archive path:

```text
python eng/contracts/s01-11/build_fixtures.py
python eng/contracts/s01-11/verify_design.py
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~ErpCreditCodecTests
npm --prefix cp6.web test -- src/contracts/erp-credit/creditCodec.test.ts
npm --prefix cp6.web run type-check
```

`adopt_design_fixtures.py` is an optional data-only reacquisition tool. It reads
the fixed admission's archive locators and verifies every original/composed hash
before selecting portable outputs. It also regenerates the explicit source/API
mapping and closed schema dependency inventory. Normal tests never call it.

`run_evidence.py` captures exact commands, environment, raw input/config/lock
hashes before and after, compiled artifact hashes, raw output and TRX/Vitest
counts into a new directory on every invocation. Modes are `dotnet-*`, `ts-*`,
`typecheck`, `design`, and `restore-ts`. Its source-baseline SHA is deliberately
separate from the uncommitted raw-source execution identity. RED scaffolds and
their failed assertions remain in the task evidence; successful runs do not
overwrite them. The design audit includes 60 explicit stale identity/proof
mutations, 29 OpenAPI routes, ten preserved extension blocks, and C01/C02
HTTP200/201 response-root equality.

These are contract and design-fixture subassertions. The 247 runtime business
cases remain NOT_RUN; 41 reference cases remain REFERENCE_ONLY; ITER-REL is
missing and WP05 remains PROPOSED. No source case is upgraded to business PASS.
