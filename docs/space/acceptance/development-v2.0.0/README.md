# CP6 Space synthetic development CAD corpus v2.0.0

This package contains 20 deterministic ASCII DXF drawings created for CP6 development.
It contains no customer, supplier, site, personal, address, title-block, or equipment-serial data.

## Boundaries

- `purpose=DevelopmentSeed` and `countsTowardReleaseGate=false` are intentional.
- The files may be used for parser, mapping, issue, UI, regression, and demo development.
- Header coverage does not replace licensed vendor fidelity testing against native DWG files.
- The unresolved XRef in L5-DEV-004 is synthetic and intentional.
- Re-run generation with `generate-dev-corpus --output <directory>`.

## Matrix

| Sample | Family | DXF header | Scenario |
|---|---|---|---|
| L1-DEV-001 | L1-RegularRectangular | AC1009 | Small regular warehouse |
| L1-DEV-002 | L1-RegularRectangular | AC1015 | Wide warehouse with loading docks |
| L1-DEV-003 | L1-RegularRectangular | AC1027 | Compact dense storage |
| L1-DEV-004 | L1-RegularRectangular | AC1032 | Regular warehouse with cross aisle |
| L2-DEV-001 | L2-MultiFloor | AC1015 | Two floors sharing coordinates |
| L2-DEV-002 | L2-MultiFloor | AC1021 | Warehouse with storage mezzanine |
| L2-DEV-003 | L2-MultiFloor | AC1027 | Split-level logistics layout |
| L2-DEV-004 | L2-MultiFloor | AC1032 | Three-floor warehouse layout |
| L3-DEV-001 | L3-NonOrthogonal | AC1009 | Angled rack field |
| L3-DEV-002 | L3-NonOrthogonal | AC1021 | L-shaped warehouse |
| L3-DEV-003 | L3-NonOrthogonal | AC1027 | Trapezoid site layout |
| L3-DEV-004 | L3-NonOrthogonal | AC1032 | Diagonal aisle network |
| L4-DEV-001 | L4-Comprehensive | AC1015 | Automated warehouse |
| L4-DEV-002 | L4-Comprehensive | AC1021 | Cold storage zones |
| L4-DEV-003 | L4-Comprehensive | AC1027 | Mixed-use fulfillment center |
| L4-DEV-004 | L4-Comprehensive | AC1032 | High-bay warehouse |
| L5-DEV-001 | L5-NoisyNonStandard | AC1015 | Noisy and unknown layers |
| L5-DEV-002 | L5-NoisyNonStandard | AC1021 | Block attribute edge cases |
| L5-DEV-003 | L5-NoisyNonStandard | AC1027 | Curves, hatch and dimensions |
| L5-DEV-004 | L5-NoisyNonStandard | AC1032 | XRef and text noise |
