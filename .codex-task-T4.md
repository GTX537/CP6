# Task: CP6 Phase 10b - Bridge Hook Health Monitor

## Mission

Add an aggregation service, REST endpoint, and WMS frontend page for 24h bridge hook health metrics from `T_IntegrationEvent`.

## Backend Files

- `CP6.Entity/DTOs/BridgeHealthDto.cs`
- `CP6.Core/Services/IBridgeHealthService.cs`
- `CP6.Core/Services/BridgeHealthService.cs`
- `CP6.WebApi/Controllers/BridgeHealthController.cs`
- `CP6.Tests/BridgeHealthServiceTests.cs`

## Frontend Files

- `cp6.web/src/views/wms/BridgeHealthView.vue`
- `cp6.web/src/api/wms/bridgeHealth.ts`

## Required Behavior

- `GET /api/bridge-health/metrics` returns the last 24h hook totals, status counts, success rate, failed queue depth, and the latest 10 dead letters.
- `POST /api/bridge-health/compensate/{eventId}` marks a dead-letter event as `COMPENSATED`.
- Frontend route: `/wms/bridge-health`
- Frontend shows KPI cards, hook table, dead-letter table, manual compensation, and refreshes every 30 seconds.

## Verification

- `dotnet build CP6.Tests/CP6.Tests.csproj --nologo -v quiet`
- `dotnet test CP6.Tests/CP6.Tests.csproj --no-build --nologo -v quiet`
- `cd cp6.web && npm run type-check`
- gstack browser check for the new frontend route.
