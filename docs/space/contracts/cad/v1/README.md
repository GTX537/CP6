# CP6 Space CAD IR v1

Status: development contract; production adapter selection remains gated by E02-S01.

CAD IR is the vendor-neutral boundary between isolated CAD converter workers and
Space semantic parsing. ODA, APS, or another approved adapter must emit this same
contract; SDK-specific objects may not cross the converter boundary.

## Invariants

- Schema version is `1` and coordinates normalize to `FloorLocal-ZUp`.
- The exact source SHA-256, source format, CAD version, source unit, millimeter
  scale, converter ID, and converter version are mandatory provenance.
- Unknown units remain `Unknown` with a null scale. A converter may not guess.
- Every entity has a stable `sourceRef`, raw entity type, layer ID, normalized
  geometry, affine transform, bounds, closed/supported flags, and controlled attributes.
- Declared layers retain their name, object count, color, line type and visibility,
  including layers with zero entities; entities on undeclared layers remain visible
  through a synthesized layer record and an explicit issue.
- Unsupported entities remain in the IR with `isSupported=false`; silent dropping
  is a contract violation.
- Entity source references and layer IDs are unique within their scopes.
- The converter receives a read-only stream and writes to `ISpaceCadIrSink`; it
  receives no WebApi dependency, local path, Draft repository, or vendor type.
- Large production artifacts use streaming records. The JSON Schema documents the
  logical package and is also used for small fixtures and contract examples.
- Coordinate preparation is a separate deterministic stage. It exposes the detected
  unit and extent first, requires an explicit confirmation bound to the source hash,
  then applies source origin, target floor origin and counterclockwise Z rotation.
- Prepared coordinates are rounded to integer millimeters in `LOCAL_MM_Z_UP`. A
  missing confirmation, implausible 1 m–5 km floor span, invalid floor assignment or
  geometry outside the assigned boundary remains Blocking.
- Inventory generation consumes only a ready coordinate preparation. Its source,
  coordinate transform, target floor, layer/type counts, block definitions,
  references, attribute summaries/values and bounds are bound by a deterministic
  SHA-256. Layer, block and block-reference queries are capped at 200 records per page.
- Mapping profiles are immutable, hash-sealed snapshots. System profiles have no
  tenant owner and are read-only to tenants; a tenant copy records its system base
  and subsequent edits create a new version. A tenant profile cannot cross tenants.
- Mapping rules target either layers or blocks and use exact, glob or bounded
  non-backtracking regular-expression matching. Block rules may add controlled
  attribute conditions. Equal priority/specificity matches are Blocking conflicts;
  explicit layer overrides win. Required sources fail closed when absent or empty.
- Mapping previews retain unmapped and empty sources, bind the profile, inventory,
  structural reuse key and overrides by SHA-256, and do not write semantic objects.
- A ready preparation seals the exact mapping replay inputs as canonical JSON: tenant,
  source and immutable profile identities, inventory/structure/preview hashes, and
  canonical layer overrides. New parse jobs carry that server-owned snapshot and a
  worker must validate it before invoking a provider; a provider must replay and
  validate the resulting mapping preview before emitting semantic artifacts.
- Semantic parsing consumes the exact prepared IR, inventory, sealed profile and
  mapping preview chain. It emits deterministic temporary `previewObjectId` values,
  integer-millimeter geometry, source/rule/default provenance and confidence bands;
  the artifact is explicitly read-only and never creates a permanent LogicalId.
- Block rules are evaluated for each block reference and override a layer rule only
  for references that satisfy their attribute condition. A missing real block
  footprint retains the block instance transform as a sub-0.70 candidate with a
  warning; the parser does not invent rack dimensions.
- The semantic diagnostic index binds every proposal to a hash-sealed evidence row
  containing SourceRef, target, applied rule and confidence band. Mapping and semantic
  issues receive stable IDs, recovery codes and Entity/Layer/Block/Document locations;
  a location says explicitly when no canvas bounds exist instead of inventing them.
- Evidence and diagnostic queries are deterministic and capped at 200 rows. This
  read-only index enables later Excel matching and editor problem navigation but does
  not implement correction commands, field locks or Draft persistence.
- Excel/CAD matching consumes canonical E03-S03 rows, the exact semantic/diagnostic
  chain and a hash-sealed editor rack snapshot. Rack rows associate through an exact
  CAD/editor SourceRef or a controlled rack-code key; multiple candidates, CAD/editor
  source disagreement and two Excel rows claiming one target remain explicit conflicts.
- Match output classifies every rack row as New, Update, Unchanged, Unmatched,
  Conflict or Error, keeps unmatched rows independently queryable, and records key,
  CAD confidence, changed fields, canvas location and per-row SHA-256 evidence. It is
  a read-only preview and does not apply Excel values to Draft.
- The CAD review workspace combines the exact diagnostic index, a tenant/model/floor
  bound editor snapshot and an optional Excel/CAD match preview. It lists diagnostics,
  low/rejected proposals and exceptional Excel rows with stable tracking keys and
  spatial locations. A successor linked to the previous workspace marks disappeared
  tracking keys Resolved and reopens keys that return; it never mutates upstream facts.
- Review queries are deterministic and capped at 200 items. Open/resolved transitions,
  locatable counts and every input/workspace SHA are validated before the editor may
  display or focus an item. The development UI imports this artifact locally; loading
  it is not a production API, correction command or Draft write.

## Current delivery boundary

This contract and its tests are the first E02-S02 development slice. It reserves
both `Dxf` and `Dwg`, but it does not claim that a licensed production DWG adapter
exists. Synthetic DXF conversion and corpus evidence are developed against this
contract. The E02-S03 development slice also supplies coordinate analysis,
confirmation and preparation without claiming formal acceptance. The E02-S04
development slice adds a deterministic, queryable layer/block inventory without
production persistence or authorization claims. The E02-S05 development slice adds
mapping profile sealing, tenant-safe copy/version semantics and deterministic preview
resolution without database writes. The E02-S06 development slice adds the pure,
read-only semantic proposal parser and confidence selection boundary without Draft
writes. The E02-S07 development slice adds the provenance and spatial diagnostic
index needed by later matching and editor UI. Formal E02-S02 through E02-S07
acceptance still waits for E02-S01 vendor selection and the production
conversion/persistence chain. The E03-S04 development slice consumes these artifacts
for deterministic Excel rack matching, but formal E03-S04 still waits for that same
production source/preflight persistence chain; confirmation and idempotent Draft writes
remain E03-S05.
The E04-S05 development slice consumes the diagnostic and optional match artifacts as
a read-only review workspace and canvas overlay. Formal E04-S05 still waits for the
production CAD artifact/issue API, authorization/audit policy and real editor workflow;
local development-artifact import does not satisfy those gates.

Files:

- `cad-ir.schema.json`: logical package schema.
- `coordinate-confirmation.schema.json`: explicit unit/origin/rotation/floor input.
- `inventory.schema.json`: query-source layer, block and block-reference inventory.
- `mapping-profile.schema.json`: draft/sealed system or tenant mapping profile.
- `mapping-preview.schema.json`: deterministic layer/block mapping preview.
- `semantic-preview.schema.json`: deterministic read-only semantic proposal preview.
- `semantic-diagnostics.schema.json`: proposal evidence and spatial issue index.
- `excel-cad-match-preview.schema.json`: read-only Excel rack association preview.
- `cad-review-workspace.schema.json`: read-only problem/unmatched list and focus workspace.
- `examples/minimal-wall.json`: minimal valid IR package.
- `examples/development-coordinate-confirmation.json`: confirmation for synthetic sample 13.
- `examples/development-mapping-profile-draft.json`: system profile draft for the synthetic corpus.
