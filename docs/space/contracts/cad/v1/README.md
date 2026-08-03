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

## Current delivery boundary

This contract and its tests are the first E02-S02 development slice. It reserves
both `Dxf` and `Dwg`, but it does not claim that a licensed production DWG adapter
exists. Synthetic DXF conversion and corpus evidence are developed against this
contract. The E02-S03 development slice also supplies coordinate analysis,
confirmation and preparation without claiming formal acceptance. Formal E02-S02
and E02-S03 acceptance still wait for E02-S01 vendor selection.

Files:

- `cad-ir.schema.json`: logical package schema.
- `coordinate-confirmation.schema.json`: explicit unit/origin/rotation/floor input.
- `examples/minimal-wall.json`: minimal valid IR package.
- `examples/development-coordinate-confirmation.json`: confirmation for synthetic sample 13.
