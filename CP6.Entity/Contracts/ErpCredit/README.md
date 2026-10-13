# ERP-CREDIT native contract boundary (S01-11)

`Cp6.ErpCredit.CurrentV1.Generated.CurrentCreditCodec` and
`Cp6.ErpCredit.Generated.FrozenR8CreditCodec` load exactly three byte-pinned files:
the unchanged WMS-RMA R3 and FIN-AR R2 schemas, plus the selected version's
TypeRegistry. Callers supply original bytes; constructors copy them before hashing
and parsing. No file paths, database, dependency registration or business services
are wired by this library.

Each published factory returns a complete `NativeCreditDocument`: exact profile,
codec namespace, native schema/root and registry identity; an independent
`JsonElement`; SHA-256; and defensive copies of the original UTF-8. Input object
member order is unrestricted. There is no replay serializer. TypeScript exposes
the same roots through `createCurrentCreditCodec` / `createFrozenR8CreditCodec`
and `publishedCodecs`, using immutable complete objects after lossless validation.

| Surface | Bounded roots |
| --- | --- |
| WMS native factories | CreditOwnerRequest, CreditOwnerQueryRequest, CreditOwnerQueryResult, CommercialOutcomeRef |
| FIN-AR native factories | CreditAuthorityReadRequest, CreditAuthorityReadResult, CreditSelectionsQuery, CreditSelectionPage, CreditPrepare, PostInput, SafeOperationDetail, CommandResult |
| Versioned registry factories | TerminalCommandReceipt, FinArObservation |
| Named scalar helpers | ParseWmsMoney / parseWmsMoney; ParseFinArD8 / parseFinArD8 |
| Frozen saved HTTP responses | C01 and C02's exact COMMITTED CommercialDraftView envelopes, via CaptureReplay / captureReplay only |

`IdentityFor` and `Decode` reject unlisted roots and mismatched identities. The
three extra C06/F09-dependent registry roots are not exposed. All 14 factories
validate full native objects, including required nulls, closed members, union
branches, conditional schemas and nested semantic rules. The private schema
interpreter implements the verified closure, not a general schema service.

WMS Money is exactly `{value,currency}` with a signed decimal string of at most
30 integer and 8 fraction digits. FIN-AR D8 has at most 20 integer and 8 fraction
digits. Native `-0` and `1.0` remain valid and byte-preserved. Exact integer tokens
are checked before conversion; the published schema closure bounds all numeric
integer fields to at most 86400. No `decimal`/JavaScript floating point conversion
is used for money or quantities. FIN-CREDIT canonical amount rules do not apply.

Current APPLIED requires a resultRef, REJECTED forbids one, and NO_CHANGE may
carry one. Frozen R8 retains its original non-APPLIED/no-ref rule. A valid FOUND
or FAILED_NO_EFFECT wire is still only a contract value. `AssessAdaptation` /
`assessAdaptation` always retains the original value and reports
`READ_ONLY_UNPROVEN`, `MANUAL_RECONCILIATION`, and UNKNOWN effect knowledge, with
no FOUND, dispatch or release permission. Full authenticated native BusinessResult,
current AR/GL/member/finite sources, producer no-effect/no-future-effect proof,
issuer registration, scope and Store receipt verification require later Owner
integration. No self-asserted boolean can authorize those actions here.

Frozen replay captures a scoped command key, exact original request JSON bytes
and hash, and the complete saved HTTP201 C01/C02 response bytes and hash. The
response is validated with the frozen registry. A replay with the same key and
same request bytes returns HTTP200 with precisely the stored COMMITTED body.
The in-memory guard does not authenticate command keys, authorize requests,
persist Store receipts, implement an HTTP handler, re-render/redact a body or
call a writer. Those remain responsibilities of the eventual trusted integration.

`FrozenOriginalBytes` / `captureOriginalBytes` is a separate byte-retention type
with `NativeSchemaValidated=false`. Shared BYTES vectors deliberately contain
`expectedRowVersion:"1"`, which fails the native PostInput base64 domain; they
prove only byte/digest retention. They cannot be converted into validated native
documents or treated as real Owner evidence.

The legacy CreditNote amount/storage and search API remain outside this adoption.
See [the task asset and test guide](../../../eng/contracts/s01-11/README.md) for
source mapping, exact assets, reproducible fixtures and local verification scope.
