# Issue #211 — original ITER identity mapping correction

The six affected entities now preserve their original non-null Id bytes through a required `original_id_key`. A registered exact identity can identify only one row; existing ProjectNo, IterationNo, RoundNo, input-version, reference-tuple and command UQs remain independent. Source scope is explicit:

| Entity | Original ITER line | Identity scope |
|---|---:|---|
| Project | 1580 | Bare Id global PK |
| Iteration | 1602 | Bare Id global PK |
| IterationInput | 1632 | Id + TenantId + IterationId |
| CandidateRef / CandidateObjectRef | 1664 | Id + TenantId |
| ReviewRound | 1692 | Id + TenantId + IterationId |
| ExternalRequestReceipt | 1738 | Id + TenantId + IterationId |

Only the two explicit global PKs have empty scope bytes; tenant and parent cannot split their equality or lock keys. The four grouped source identities retain exactly their cited scope components. Equal raw Id in a distinct source tuple remains possible; bare-Id global uniqueness for those four is unproven and is not imposed. Parent scope resolves original Iteration Id bytes through the same-tenant FK. The fixed original Owner/schema/domain and complete original tuple determine the interned identity. Incompatible tenant assignment for the same complete tuple conflicts without another UUID. Digest collisions compare every tuple byte. No normalization, trimming, UUID coercion or caller-selected schema/domain is introduced.

The focused regression interprets declarations using synthetic rows. The first six-test RED/GREEN cycle restored identities but assumed all six were global; focused source review rejected that unsupported restriction. That intermediate source, mapping, metadata and first GREEN are preserved under `superseded-all-global/`, with an index explicitly marking them superseded. A new scope RED produced 18 assertion failures across eight tests; the corrected profiles passed **8/8 tests**. The current separate read-only verifier passed **1,000 structural/source/hash checks**, confirming that only the six entities and the registry's named source-identity profiles changed; 24 entities remain unchanged. Tests cover required binding, business-key independence, exact tuple duplicates, two global PKs, four tenant/parent scopes, byte distinctions, digest collisions and raw/key consistency. Analogous identity-bearing keys were inspected without redefining unrelated version models.

The original 54 gate tests, gate implementation and manager inputs/logs are unchanged and reused at their recorded identities. The original 12,251-check run describes its original mapping snapshot and is not relabelled as a fresh check of this correction. `superseded-index.json` pins 16 original files captured before replacement, including original manifest/validation/delivery/review records. Original immutable test logs and all RED failures remain in place. The prior SPEC/QUALITY conclusions apply only to their original commits; COORD performs the independent focused correction review.

Only `build_candidate.mapping()` was invoked, after checking the original product/source pins. No graph, decisions, basis, gate contract, adoption, roadmap, shared project state, workflow, product, DB or migration was regenerated. This is a static preparation correction: no database registry or locking runtime was implemented or exercised. Runtime, provider, authority and remote-ancestry evidence remain unverified. Production/adoption/completion flags remain false and WP01-01 remains PLANNED / NOT_CHECKED.
