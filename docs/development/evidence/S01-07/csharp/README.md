C# contract verification passed **657/657**, with no skipped cases, on actual **.NET 8.0.29 / net8.0** using SDK 10.0.302. The test source was the uncommitted task tree based on `e20740e37e20bc0210e0b32c5670c2bce962002a`; [verification.json](verification.json) records exact tested code/dependency hashes and TRX execution timestamps. Subsequent source/commit identity alone does not turn this evidence into a new test execution.

[Final TRX](quality-contracts-format-final.trx) and [console log](native-format-final-gate.log) preserve actual discovery and results. All 20 `NCR-NULL-001..020` fixed IDs execute individually. Their source bytes are SHA-256 pinned and expected values are unchanged. Each applicable field is exercised inside its complete source-derived native fixture, including all three separate `originalQuery` payload branches. These frozen IDs retain their original `FIELD_SUBASSERTION_ONLY` scope; the additional full-object suite provides separate codec evidence.

The run includes 594 complete-object fixtures: 102 positive, 492 negative. Every one of the 92 emitted declarations has a positive case, per-field required rejection and closed-field rejection. Targeted tests add seven string-null rejections, five precise metadata corrections, duplicate literal/escaped names (including nested unions), nullable values, original Observation Qty precision, bounded JSON integer exponents, Unicode, strict serialization and five ECMAScript line terminators. [The generation manifest](../../../../../eng/contracts/s01-07/csharp/generation-manifest.json) lists 24 explicit roots and 102 complete schema nodes; [the field inventory](../../../../../eng/contracts/s01-07/csharp/native-field-inventory.json) records each module's fields and native identity predicates. [The contract README](../../../../../CP6.Entity/Contracts/Quality/README.md) summarizes supported and omitted surfaces.

Retained RED evidence is behavioral rather than a missing-reference or compiler failure:

| Evidence | Actual outcome | Missing behavior demonstrated |
| --- | --- | --- |
| [Initial declaration boundary](red-declaration-boundary.log) | 2 failed / 2 discovered | Default DTO binding accepted an unknown sourceKind and a foreign Uom owner field. |
| [Full declaration baseline](red-full-native.log) | 276 failed, 355 passed / 631 | Closed/scalar validation and native nested union reading/writing were absent. |
| [Writer identity](red-writer-identity.log) | 2 failed / 2 | Typed branch could silently encode as another valid branch; invalid UTF-16 was replaced. |
| [Regex dialect](red-regex-dialect.log) | 1 failed, 4 passed / 5 | .NET ECMAScript `$` accepted final LF despite the native JavaScript predicate rejecting it. |
| [RFC3339 range](red-rfc3339-format.log) | 6 failed, 6 passed / 12 | DateTime year limits and June/December-only second-60 positions narrowed source format inputs. |

[The actual Node probe](node-regex-dialect.json) used default Node v20.19.0 and preserved the original regex. The final C# adapter retains that original pattern in metadata and translates terminal `$` only at .NET evaluation to match ECMAScript without multiline. LF, CR, CRLF, U+2028 and U+2029 are all rejected. This is a language-dialect adaptation, not a source schema revision.

The 631-, 640- and 645-case successful logs are preserved as intermediate history. The final 657-case run supersedes them for final codec behavior; it does not rewrite their scopes or counts. Main quantity fixture text follows its canonical decimal pattern, while `1.00000000` precision preservation is tested on original SourceQA ObservationInput Qty, whose source allows it. Before the final gate the constructed fixture file was regenerated with explicit LF newlines for byte portability; its 594 cases and expected JSON values are unchanged.

Twelve time-format cases cover year `0000`, leap-year calendars, original fraction precision, numeric offsets crossing the four-digit year boundaries, and valid/invalid UTC month-end second-60 positions. These synthetic position tests do not claim an IERS-announced leap second. The final adapter follows [RFC3339's format/calendar rules](https://www.rfc-editor.org/rfc/rfc3339#section-5.7) using integer calendar arithmetic and preserves the original string.

Reproduction uses repository-local pinned inputs only:

```text
python eng/contracts/s01-07/csharp/generate.py --check
dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~QualityContractTests
```

No shared project, lock, Wire implementation, workflow, database, deployment or existing service was changed. No GitHub Actions run was triggered or cancelled, and no trigger configuration changed. No claim is made for unadopted source routes, actual Owner currentness/authorization, write wiring, storage, cross-object semantic rules, or production/business acceptance. Task-level review, commit, merge and remote-main confirmation remain with the parent conversation.
