# E00-S02 — Space feature switch and compatibility guard

Date: 2026-07-25
Branch: `codex/space-e00-inventory`
Baseline commit: `1524289fbac6f94b81b69a6fe1ce2f48fceb02dd`

## Outcome

E00-S02 adds a fail-closed compatibility seam without importing the candidate
`SpaceContext`, domain model, or migrations owned by E01:

- safe default for every unconfigured Tenant+Site: `Legacy / LegacyOpen`;
- configuration-backed Tenant+Site `Legacy / DesignV1` mode;
- cutover states and prerequisite evidence checks;
- centralized interception of all current Legacy `Space_*` writes;
- continued Legacy reads after DesignV1 activation;
- stable errors for disabled writes, invalid state, and tenant-scope denial.

## Configuration

The deployment-controlled configuration lives under `Space:Compatibility`.
The repository default disables the Design entry point and defines no Site
overrides:

```json
{
  "Space": {
    "Compatibility": {
      "DesignApiEnabled": false,
      "Sites": []
    }
  }
}
```

A DesignV1 Site must be configured atomically with:

- exact `TenantId` and `SiteId`;
- `Mode = DesignV1`;
- `CutoverState = DesignV1`;
- `BootstrapVerified`, `RuntimeHashVerified`, and `WmsIdentityVerified`;
- global `DesignApiEnabled = true`.

Startup validation rejects duplicate Tenant+Site keys, empty identities,
inconsistent mode/state pairs, incomplete evidence, and activation while the
Design entry point is disabled.

## Cutover policy

Normal forward path:

```text
LegacyOpen -> FreezeRequested -> Frozen -> Bootstrapping -> Verified -> DesignV1
```

`Bootstrapping -> Verified` requires bootstrap, runtime-hash, and WMS-identity
evidence. `Verified -> DesignV1` additionally requires the global Design API
entry point.

Before activation, a failed transition stays frozen and may return to
`LegacyOpen` only with `ReopenApproved` and no accepted Design writes. There is
no transition out of `DesignV1`; recovery after activation is a repair publish,
not automatic Legacy fallback.

## Legacy compatibility behavior

`LegacySpaceWriteGuardInterceptor` is attached to `CP6Context`. It discovers
changed Legacy Space entities and resolves them to Site through the current
hierarchy:

- direct: Site, Floor, Connector;
- via Floor: Zone, Rack, Marker, ConnectorStop;
- via Zone: Aisle;
- via Rack/Floor: Location;
- tenant-wide: Template and CodeRule.

Unknown or unresolved Legacy Space scopes fail closed as tenant-wide writes.
The guard covers controller, service, worker, and direct EF write paths that
call `SaveChanges`; it does not intercept queries, so old Published runtime
reads remain available.

There is no dual write.

## Permission and trust boundary

E00 exposes no runtime mutation endpoint for this deployment-level flag. This
avoids creating an unaudited in-memory switch before E01 provides the durable
control-plane model. Configuration changes are restricted operationally to the
approved `space:integration:manage` deployment path; external subjects receive
no switch surface.

When E01 adds the durable management endpoint, it must enforce
`space:integration:manage`, reject external subjects, and retain the same gate,
state policy, and stable errors.

## Stable errors

| Code | HTTP | Condition |
|---|---:|---|
| `SPACE_LEGACY_WRITE_DISABLED` | 409 | Legacy write targets a DesignV1 Site |
| `SPACE_VERSION_STATE_INVALID` | 409 | Write occurs during freeze/cutover or prerequisites are incomplete |
| `SPACE_TENANT_SCOPE_DENIED` | 403 | Requested Tenant differs from the current tenant context |

## Rollback

- Before activation: leave the Site frozen; after approval set
  `ReopenApproved`, transition to `LegacyOpen`, then restore Legacy mode.
- At activation: disable the global Design entry point to stop new Design
  writes while preserving reads and evidence.
- After any Design write is accepted: do not auto-reopen Legacy; use historical
  Published data and a forward repair publish.
- No Legacy table or migration is changed by E00-S02.

## Verification

Targeted tests cover:

- missing configuration defaults to Legacy;
- verified DesignV1 enables Design writes and blocks Legacy writes;
- frozen state rejects writes;
- cross-tenant access is denied;
- invalid transition and failed-cutover reopen rules;
- invalid configuration;
- direct and hierarchical Legacy write interception;
- tenant-wide write interception;
- Legacy read compatibility after DesignV1 activation.

## E01 handoff

E01 owns `Space_Model`, `Space_ModelVersion`, the independent `SpaceContext`,
the first formal migration, durable cutover state, and the authorized
management API. It should replace the configuration-backed compatibility
resolver behind `ISpaceCompatibilityGate`; the interceptor and error contracts
do not need to change.
