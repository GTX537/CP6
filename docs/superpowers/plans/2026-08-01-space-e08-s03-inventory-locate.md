# E08-S03 unified inventory locate plan

## Goal

Accept material, lot, and container criteria against the E08-S01 WMS runtime
boundary and make zero, multiple, and cross-floor results explicit in the 3D
Viewer. The legacy `/api/space/stock/locate` endpoint is not a data source for
this flow.

## Contract

- Add `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`.
- Accept one or more exact `materialNumber`, `lotNumber`, and
  `containerNumber` query values. Multiple supplied criteria use AND semantics;
  an empty request is invalid.
- Query every active location in the current Published version through
  `ISpaceWmsRuntimeSource`, in the existing bounded chunks and with current WMS
  adoption identities.
- Return the normalized criteria, E08-S02 source/freshness metadata, one stable
  hit per Space logical location, totals for locations/floors, quantities, and
  the matched material/lot/container facts.
- An available source plus zero hits is an authoritative empty result. An
  unavailable source remains distinguishable from empty.

## Adapter behavior

- Extend `SpaceWmsInventoryQuery` with optional locate criteria so filtering is
  performed at the WMS boundary rather than by downloading site inventory to
  the browser.
- The CP6 adapter filters positive stock rows for material/lot. Container
  queries use active pallet facts and may combine material and lot criteria.
- The standard simulator applies the same exact AND semantics.

## Viewer behavior

- Search modes are code, material, lot, and container.
- Stock search opens a result panel instead of navigating to the first hit.
- The panel explains the query, source time, hit/floor totals, groups results by
  floor, and exposes every hit as an explicit navigation choice.
- Selecting a hit reuses the existing code locator, including awaitable
  cross-floor loading, fly-to, and pulse highlighting.
- Empty and unavailable results have separate messages.

## Verification

- Runtime source/DTO/service contract tests.
- CP6 adapter and simulator filter tests.
- Runtime service tests for AND matching, normalization, stable grouping,
  multiple floors, empty, unavailable, and invalid criteria.
- Controller/OpenAPI/SDK drift tests.
- Frontend API serialization and Viewer component interaction tests.
- Focused and release backend suites, full frontend tests, type-check, and
  production build.
