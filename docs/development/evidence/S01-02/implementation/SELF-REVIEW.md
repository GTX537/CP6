# S01-02 implementation and local verification

Base HEAD: `4ab979e2f391c5a5645da0d1586781e824a9f8cd`. Source was uncommitted during these runs; the per-run metadata records exact SHA-256 identities of all five source/project files and built assemblies. A later commit identity must not be substituted for the actual tested file identities.

## Implemented boundary

- `CP6.Entity.Contracts.Wire.NullOnly` has its own converter. Only a JSON null token is accepted and writes produce null. Presence belongs to the containing property's `required`/`JsonRequired` metadata, demonstrated by separate required and optional test carriers.
- `StrictJson.Parse(ReadOnlySpan<byte>)` first copies input, validates UTF-8 and strict JSON grammar, and scans every object's decoded names ordinally before returning an owning disposable document. `StrictJson.Deserialize<T>` finishes that scan before invoking any target converter. Comments, trailing commas/documents and duplicate names cannot be enabled through serializer options.
- `StrictJsonDocument.GetOriginalUtf8()` returns an independent copy of the unchanged input, including surrounding whitespace, escape spelling and number spelling. The API does not serialize the document to generate signature/digest/replay bytes.
- `JsonSemanticEquality.Key` and `AreEqual` provide schema const/enum/uniqueItems equality. Objects sort decoded names ordinally; arrays retain order; type tags and length frames avoid delimiter collisions. They independently validate externally supplied JsonElements, so duplicate properties do not acquire semantic keys.
- Number coefficients remain exact normalized signed decimal digit strings; exponents use `BigInteger`. Trailing coefficient zeroes adjust the exponent without expanding powers of ten. No floating-point or `decimal` conversions occur. Keys are opaque in-process strings, not a versioned serialization or signing protocol.

## Test sequence and evidence

Command: `dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests` with console/TRX logging arguments shown in each metadata file.

| Run | Result | Exit | Evidence |
| --- | --- | --- | --- |
| red | 77 discovered; 76 runtime failures, 1 runtime assertion passed | 1 | `red.log`, `red.trx`, `red-metadata.json` |
| red-final | 77 discovered; 76 runtime failures, 1 runtime assertion passed | 1 | `red-final.log`, `red-final.trx`, `red-final-metadata.json` |
| green | 77 passed; 0 failed; 0 skipped | 0 | `green.log`, `green.trx`, `green-metadata.json` |

RED used compiled API stubs throwing `NotImplementedException`, retained in `red-api-stubs.txt`; no missing-type compile failure is counted as RED. Before implementation, the harness was corrected from exact `JsonException` matching to accepting its derived types (the framework reader throws a subtype), then rerun as `red-final`. The initial attempt to run the evidence helper had an incorrect relative repository path and failed during file hashing, before invoking dotnet; correcting it did not alter production behavior or any test expectation.

The 21 fixed cases are read directly from the unchanged package, selected using the S01-02 dispatch IDs and asserted to match the complete set. No operation is silently skipped and no expected result is generated from implementation behavior. TRX records original IDs:

- JSON-001 through JSON-007.
- NULL-001 through NULL-007.
- EQUAL-001 through EQUAL-007.

The remaining 56 cases cover malformed JSON, escaped/supplementary Unicode names, escaped lone surrogates, case/normalization distinctions, invalid UTF-8, duplicate rejection before converters, serializer-option isolation, NullOnly round trip and optional presence, exact original-byte ownership, 2048 nested containers, huge positive/negative exponents, nearby integers above the binary64 exact range, type/boundary distinctions and actual runtime identity.

Actual SDK: 10.0.302. Target framework and test process: net8.0 / .NET 8.0.29, confirmed both by VSTest adapter output and an executed runtime assertion. The SDK version is not the target framework. Build/test output contains no warnings or errors in the green run. The leaf project references only CP6.Entity and the specified Test SDK/xUnit packages; no existing project, solution, dependency lock or workflow configuration was edited.

## Assumptions, resource limits and remaining scope

- There is no newly imposed domain size/depth limit. Reader/document depth is explicitly `Int32.MaxValue` rather than System.Text.Json's default 64. Traversal uses heap stacks. Contiguous byte arrays, strings, JsonDocument metadata, BigInteger storage and available process memory still impose finite runtime capacity; this is not a claim that all theoretical JSON fits in memory.
- Semantic keys allocate content and container strings. Deep nesting causes repeated copying, potentially quadratic total work in input size/depth; this implementation prioritizes exact schema semantics, not a throughput or untrusted-ingress quota guarantee. Resource exhaustion and deployment input budgets were not benchmarked or changed.
- Raw input must be valid UTF-8. Escaped lone UTF-16 surrogates remain distinguishable exact code units in the strict scan and semantic keys. No Unicode normalization or replacement is used there. System.Text.Json's target string converter/JsonElement string accessors can reject such escaped values; parse acceptance is not a promise that every typed target supports every JSON value.
- Typed deserialization retains caller type metadata and custom-converter semantics, including caller-specified nonzero MaxDepth. Default MaxDepth 0 is raised to Int32.MaxValue. Required property guarantees still depend on a correctly declared target contract; this is not a general schema validator.
- A document owns the lifetime of its JsonElements; dispose after consumers finish, or clone an element when independent lifetime is required. Retained bytes and semantic keys serve different protocols.
- Verified only the new leaf contract library. Owner workflows, pages, PostgreSQL/storage, migrations, runtime deployment and production acceptance were not exercised. No GitHub Actions were triggered, no cancellation or workflow trigger change was needed, and no database/deployment action occurred.

Self-review found no unresolved functional or contract defect in this task's implementation. The task controller handles the one task-level independent review and candidate publication; COORD owns shared status documents, the serial merge window and remote-main completion confirmation under the dispatch instructions.
