The NCR C# boundary adopts 24 explicit source roots and their complete 92-declaration closure. `NcrJson.Deserialize<T>(utf8)` validates strict JSON and the complete native schema before constructing a record. `NcrJson.Serialize(value)` validates both the concrete CLR branch and the declared wire route. Generated converter attributes also protect ordinary `System.Text.Json` use; use `NcrJson` for the UTF-8 grammar boundary. Caller serializer options do not change native field names, required null output or union representation.

The declarations are auditable adaptations of exact `composed-15` and `composed-16`; five metadata rows are copied from `composed-17`. Original names map to `CP6.Entity.Contracts.Quality.Main`, `.SourceQA`, `.OwnerWire` and `.Consumer`. `MainResult` adoption exposes only the full concrete `oneOf/14` payload. Other `MainResult` branches and the remaining source surface are not exposed by this codec. It does not implement handlers, Owner authorization/currentness, cross-object semantic checks, storage or quality business acceptance.

[Generation manifest](../../../eng/contracts/s01-07/csharp/generation-manifest.json) records every original declaration, source line, schema pointer, original/composed/native hashes, patch pointers and generated output hashes. [Field inventory](../../../eng/contracts/s01-07/csharp/native-field-inventory.json) lists every emitted type's exact fields, required/nullable/closed flags, resolved native predicates, union branches and version/discriminator identities. The source schema, including module-specific identities, remains authoritative; these are separate native types, without a universal reference adapter.

The following inventory summarizes Ref/Scope differences. Every field shown is required and nonnullable; all these objects and their nested objects are closed. Exact length, pattern, array and literal predicates appear in the linked field inventory.

| Source module / native shape | Fields | Version and identity rules |
| --- | --- | --- |
| SourceQA QapRef | `owner, id, version` | Owner 1–256 chars; ID 1–160; version retains the original nonnegative integer string pattern. |
| SourceQA QapEvidence | `owner, id, version, digest` | Flat evidence; original QAP identity plus lowercase 64-hex digest. |
| SourceQA QapUom | `id, version` | ID 1–256; original QAP version string. No Owner field. |
| SourceQA ExecRef | `owner, id, version` | Original EXEC string identity; distinct declaration from QAP. |
| SourceQA ExecEvidence | `ref, digest` | Nested EXEC reference; flattening it into QAP evidence is rejected. |
| SourceQA ExecScope | `scopeId, scopeVersion, ranges` | Scope ID is a source string, not forced to UUID. Each range requires `portionId, from, to`; at least one range. Original quantity strings retain permitted trailing zeros. |
| IPQC OutQualitySource.outputRef | `ref, digest` | Native Out evidence with nested `owner, id, version`. It is neither QAP flat evidence nor an event envelope. |
| CP4 OwnerWire.OwnerRef | `owner, id, version` | Owner 1–64, ID/version 1–128; version is an opaque bounded string, not forced into an integer. |
| CP4 OwnerWire.Evidence | `owner, id, version, digest` | Original flat Owner reference plus digest. |
| CP4 OwnerWire.NestedEvidence | `ref, digest` | Original nested Owner reference, retained as its own shape. |
| CP4 OwnerWire.OwnerBusinessRef | `owner, streamId, businessId, revision, digest` | Revision is the native positive Int64 lexical string. |
| CP4 OwnerWire.Scope | `scopeId, scopeVersion, ranges` | UUID scope; positive Int64 version string; 1–500 ranges; each range requires `portionId, from, to`. |
| MES RegisterExecutionBinding.scope | `scopeId, scopeVersion, ranges` | Separate original MES shape; UUID scope/portion identity, positive Int64 version string, native decimal ranges and 1–500 members. |
| Finance R10 Ref | `owner, id, version, digest` | Each identity string is 1–240 chars; no borrowed QAP numeric version restriction. |
| Finance R10 ScopeRef | `tenantKey, companyKey, ledgerKey, siteKey` | Four native string dimensions; no substitute `scopeId/ranges`. |
| WMS RMA Ref | `owner, id, version, digest` | Owner/version 1–64 chars, ID 1–200; retains native WMS version identity. |
| WMS RMA Uom | `code, version` | Code 1–32, version 1–64; no substituted QAP `id`. |
| CP4 ReferenceIdentity / Main imported ReferenceIdentity | `kind, value` | Exact `EVIDENCE`, `OWNER_REF`, `OWNER_BUSINESS_REF` branches; nested value must match its own native module schema. |

Seven corrected fields use the existing required `Wire.NullOnly`: three C09 `originalQuery` payloads, Main IPQC blocked/unknown `guardRef`, CP4 blocked/unknown `guardRef`, numeric ObservationInput `code`, and categorical ObservationInput `value`. Missing, zero, object and string `"null"` are rejected. Other legitimate nullable fields remain value-or-null. `MainResult.oneOf/14.priorHeadRef` stays required/nonnullable; query `sourceKind/keyword/cursor` and page `nextCursor` stay required/nullable. All five metadata rows retain `editable=false`.

The codec selects exactly one complete schema branch and writes its underlying object, never a CLR `Value` envelope. Native string values retain their content and precision. Original patterns remain unchanged in metadata. The runtime adapts an unescaped terminal `$` to .NET `\z` to match native ECMAScript without multiline: actual Node rejects trailing LF/CR/CRLF/U+2028/U+2029, whereas raw .NET ECMAScript incorrectly permits final LF. This is a regex dialect adaptation, not a source schema revision or transplant of the separate StableId patch. Date-time formats additionally validate the calendar and RFC3339 syntax; schema-specific UTC/microsecond patterns stay in effect. Byte recovery and digest operations must retain `StrictJsonDocument.GetOriginalUtf8()`; reserializing a typed value is not a replacement for original bytes.

Calendar checks retain the RFC3339 four-digit year range including `0000`, original fractional precision and numeric offsets. A second-60 value must occupy a UTC month-end position after offset arithmetic. This codec checks the format and possible position; it does not consult IERS announcements or claim that a synthetic test date contains an actual announced leap second. [RFC3339 sections 1, 5.6 and 5.7](https://www.rfc-editor.org/rfc/rfc3339#section-5.7) supply these format rules.

Reproduce from the repository root:

```text
python eng/contracts/s01-07/import_sources.py --check
python eng/contracts/s01-07/csharp/generate.py --check
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~QualityContractTests
```

`generate.py` fails if a selected source introduces an unsupported schema predicate or conflicting type route. `make_fixtures.py` constructs committed examples from pinned source schemas without calling the runtime validator. The fixtures are contract examples, not actual Owner or production acceptance data. Runtime code embeds its schema closure and has no archive-path or source-file dependency.
