# E00-S03 - Space data-source contract

Date: 2026-07-25
Branch: `codex/space-e00-inventory`
Baseline commit: `1524289fbac6f94b81b69a6fe1ce2f48fceb02dd`

## Outcome

Space runtime responses now carry mandatory provenance metadata. An empty
inventory, task, workload, or device result can no longer silently mean either
"the source returned no records" or "the source is not connected."

The shared states are:

| Kind | Meaning | Available | Simulated |
|---|---|---:|---:|
| `Real` | Read from the authoritative runtime source | yes | no |
| `Simulated` | Read from an explicitly selected simulator | yes | yes |
| `Unavailable` | No trustworthy source is configured | no | no |

Every source object also contains `dataSourceId` and an UTC
`observedAtUtc` timestamp. Enum values serialize as stable strings.

## Source ownership

| Adapter or payload | Kind | `dataSourceId` |
|---|---|---|
| WMS stock, pick task, workload | `Real` | `CP6_WMS` |
| Unconfigured WMS stock fallback | `Unavailable` | `WMS_UNCONFIGURED` |
| Placeholder WCS device query | `Unavailable` | `WCS_UNCONFIGURED` |
| Published Space scene and export | `Real` | `CP6_SPACE_RUNTIME` |
| Future simulator adapter | `Simulated` | adapter-defined stable ID |

All WMS query interfaces inherit `ISpaceDataSourceDescriptor`. A new adapter
therefore cannot compile without declaring its source kind and identity.

## API and DTO coverage

The source object is returned by:

- floor stock and material/lot/container inventory lookup;
- floor and site pick-task paths;
- workload heatmaps;
- device overlays;
- Viewer scene payloads;
- scene export payloads.

Existing authorization attributes and permission names are unchanged.
`SpaceStockController` and `SpaceAdvancedController` remain outside
`FieldMaskAttribute`; source metadata is a trust boundary and must not be
removed by FieldPolicy. A contract test locks this behavior.

## Unavailable behavior

`Unavailable` is fail-closed:

- inventory overlays discard untrusted rows and do not color locations;
- workload overlays discard untrusted rows and do not render a heatmap;
- polling propagates the new source marker to the Viewer UI;
- stale inventory or workload colors are reset to the base scene;
- pick paths and device layers do not render;
- location deactivation returns
  `SPACE_DATA_SOURCE_UNAVAILABLE` with HTTP 503 before using a stock quantity.

This prevents an unconfigured WMS adapter that returns `[]` or `0` from being
treated as authoritative empty inventory.

## Viewer marker

The stock legend and advanced-visualization panel display `REAL`,
`SIMULATED`, or `UNAVAILABLE` badges. `Real` and `Simulated` data remain usable;
`Unavailable` is visibly distinct and disables the affected visualization.
The timestamp shown to users comes from the same source metadata used to
render the snapshot.

## Rollback

Rollback is presentation-only:

1. revert the Viewer badges and unavailable-state rendering if necessary;
2. keep the backend source fields and stable enum values in API responses;
3. do not rewrite, clear, or synthesize inventory data;
4. do not change the WMS truth tables.

Removing provenance from the backend would recreate the empty-versus-
unavailable ambiguity and is not a safe rollback.

## Verification

Automated coverage includes:

- stable serialization and flags for all three source kinds;
- inventory and task API propagation for all three kinds;
- real, unconfigured, and future simulated adapter declarations;
- Viewer labels and availability rules for all three kinds;
- inventory and workload overlays rejecting unavailable snapshots;
- simulated inventory remaining usable and visibly marked;
- scene and export provenance;
- FieldPolicy exclusion for source-bearing endpoints;
- location deactivation failing closed when WMS is unavailable.

The repository-wide frontend type check still reports pre-existing
`CpListPage` generic-row errors outside the Space data-source change. No
E00-S03 file appears in that error set.

Validation results:

- E00-S03 backend contract and fail-closed tests: 12 passed;
- E00-S03 and affected frontend tests: 43 passed;
- full .NET suite: 2,244 passed, 5 existing SQL-only skips;
- frontend production build: passed, 2,657 modules transformed;
- full frontend assertions: 494 passed across 74 files;
- inventory scanner: 9 tests passed and frozen report check passed.

The full Vitest command still exits non-zero after its 494 passing assertions
because unchanged
`src/views/space/lifecycle/__tests__/SpaceCodeRuleView.spec.ts` produces 15
unhandled Element Plus `ElSelect` recursive-update rejections. Running that
single unchanged file reproduces all 15 errors; the E00-S03 six-file suite is
clean.
