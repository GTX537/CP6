# Explicit PostgreSQL messaging storage initialization

The `provision` command initializes **only** `erp-identity-messaging-v1` in an explicitly selected, empty, isolated PostgreSQL database. A separate `install-service-revocation-v1` command is described below. Neither mode authorizes deployment, switches a business connection, creates a database, or creates/rotates runtime credentials.

Build the project with the repository's .NET 8 SDK and locked dependencies. Run its executable as `CP6.TenantStorage provision --stdin`. Supply one JSON document through redirected standard input from an approved secret source; do not put credentials in command arguments, shell history, logs, or checked-in files. No default connection string or environment credential is inferred.

Input shape (placeholders are illustrative, never usable credentials):

```json
{
  "Request": {
    "OperationId": "11111111-1111-4111-8111-111111111111",
    "TenantId": "22222222-2222-4222-8222-222222222222",
    "RuntimeLogin": "precreated_tenant_login",
    "Host": "explicit-database-host.example",
    "Port": 5432,
    "Database": "explicit_isolated_target",
    "SystemIdentifier": "trusted-cluster-system-identifier",
    "DatabaseOid": 12345,
    "Profile": "erp-identity-messaging-v1"
  },
  "AdminUsername": "separate_migration_login",
  "AdminPassword": "supplied-only-through-private-process-input",
  "SslMode": "VerifyFull"
}
```

The trusted operator obtains the target identity independently using `pg_control_system().system_identifier` and the exact `pg_database.oid`/database name. The command verifies these before mutation. `VerifyFull` is the default TLS mode; explicit `Disable` exists for isolated local test clusters. The migration identity must be the target database owner and a separate PostgreSQL superuser for this bounded profile, which creates a NOLOGIN security helper and transfers function ownership. The precreated runtime login must have no membership, ownership, elevated role flags, role settings, or preexisting object grants; a plain CONNECT grant is permitted. Its password is neither required nor changed by this command.

The first operation atomically runs ERP baseline, ERP RLS overlay, Identity baseline, Identity RLS overlay, then records a protected directory, global operation result and tenant-local `initialization_history_v1`. The local history records this shared messaging profile's adoption, not tenant business migrations. Additional tenants validate the supported profile's structural categories and user-object dependency seal, exact EF history rows, directory identities, all previous tenant-local histories and result versions before appending a mapping/schema and minimum grants. Unknown or changed layouts fail closed. There is no repair, upgrade or general-purpose migration mode. The initial target must be new and uncustomized; an existing cluster-wide `cp6_tenant_lookup_v1` helper is rejected rather than reused for another database.

Reuse the same operation UUID and identical nonsecret request after cancellation, connection failure, or lost output. Before-commit interruption rolls back the entire operation. After-commit loss is recovered by verifying and returning the original saved result, including its original completion time. Changed inputs with an existing operation UUID are rejected. Credentials are excluded from canonical input and results; retrying with rotated control credentials is possible. Serialization uses a database-local transaction advisory lock followed by the existing directory lock, with 15-second lock and 60-second statement timeouts (CLI overall timeout: three minutes). A timeout is never proof that a commit did not occur.

Success emits only the original nonsecret result JSON. Failure emits a sanitized code without provider text or inner exceptions. An exit code or saved `Profile` result is not evidence that WebApi, real Owner writes, or all WP02-02 acceptance gates are complete. The protected catalog seal intentionally detects even additional non-system objects, so unrelated DDL requires a separately authorized profile/version change.

Provision in a quiescent control window. The directory-first control lock order can conflict with an already running table query that next calls the directory helper. PostgreSQL deadlock detection or bounded lock timeout can therefore reject an operation; roll back and retry the same operation/input. This tool does not promise uninterrupted runtime availability or coordinate arbitrary external administrator DDL.

## Bounded service revocation profile

`CP6.TenantStorage install-service-revocation-v1 --stdin` installs a **different fresh-only profile**. It does not extend or reinterpret `erp-identity-messaging-v1`, its recovery seal, or any EF history. The four business tables are the original `crm_identity.ServiceToken`, `crm_identity.Snapshot`, `public.CrmIdentityTenantGenerations`, and `crm_identity_priority.Cp6_OutboxMessage`, with their exact frozen baseline types, capacity constraints, keys/indexes, generation trigger, and Core/Identity RowVersion sequences/functions. It records only `cp6_service_revocation_v1.installation`; neither full Core nor full IdentityPriority migration history is stamped.

Input uses the same private redirected-stdin convention:

```json
{
  "Request": {
    "InstallationId": "11111111-1111-4111-8111-111111111111",
    "Server": "192.0.2.10",
    "Port": 5432,
    "Database": "explicit_issuance_database",
    "DatabaseOid": 12345,
    "SystemIdentifier": "trusted-numeric-cluster-identity",
    "IssuerLogin": "precreated_issuance_login",
    "Tenants": [{ "TenantId": "22222222-2222-4222-8222-222222222222", "Login": "precreated_tenant_login" }]
  },
  "AdminUsername": "separate_database_owner",
  "AdminPassword": "private-control-secret",
  "SslMode": "VerifyFull"
}
```

The control identity must be the database owner and a separate superuser for this bounded installer. Runtime and issuance logins must already exist as ordinary logins without membership, ownership or administrative flags. Runtime gets tenant RLS, SELECT plus UPDATE of `ServiceToken.RevokedAtUtc`, Snapshot/generation SELECT/INSERT/UPDATE, priority Outbox SELECT/INSERT, and only required helper/sequence privileges. It gets no DELETE/TRUNCATE, directory read/write, DDL, TEMP or unrelated Core grants. Issuance keeps the existing `RecordAsync` operation using a separate INSERT/SELECT-only ServiceToken login. The database identity functions have fixed SQL and search paths; their execution grants expose only the pinned identity/structural digest, not directory rows or administrative operations.

Use numeric IP endpoints. Host/port/database must explicitly match the application's actual issuance `DefaultConnection`; runtime additionally checks the physical database OID, cluster system identifier, installation UUID and structural seal before writes and at commit. Existing objects, another profile, a changed seal or a pre-existing cluster-wide `cp6_tenant_lookup_v1` are rejected. This mode does not migrate existing records, append tenants, repair objects, upgrade schemas, or support unrelated DDL. Repeat the same InstallationId and identical request after an uncertain result; the command verifies the saved installation and returns the same nonsecret result. An exception alone never establishes rollback.

Recovery also verifies every original directory OID/name/tenant row and the supported profile/physical identity fields. The separate issuance OID/name is recorded in the protected installation row; first installation and recovery verify ordinary login eligibility, absence of memberships/role settings/ownership, and the exact issuance grants (including no grant options, column-only extras, system-catalog grants or grants in another database). These explicit checks do not change the old profile's catalog-seal definition. The shared fresh-layout preflight rejects unknown user objects in `pg_catalog`, casts, operators and other unsupported dependencies before executing baseline SQL. Administrative changes require the quiet control window below; this installer does not fence arbitrary concurrent administrator changes or provide an online issuance-role revocation protocol.

The application explicitly enables `CrmIdentity:ServiceRevocationTenancy` and supplies `HostId`, `Revision`, UTC `ValidFromUtc`/`ValidUntilUtc`, optional `SourceLifetimeSeconds` (1–300, default 60), the exact `Server`/`Port`/`Database`/`DatabaseOid`/`SystemIdentifier`/`InstallationId`/`SslMode`, `Tenants` entries (`TenantId`, `Login`, private `Password`, `CredentialVersion`), and `Clients` entries (`ClientId`, `RegistrationVersion`, `AllowSelfRevoke`). Client IDs and tenant/region mappings must match the existing controlled CrmOidc/CrmIdentity registration. Neither Enabled-for-issuance nor the active-tenant lookup is used to deny self-revocation. Every request still needs current correct Basic credentials and explicit self-revocation permission. Omitted tenant settings preserve the legacy path; a configured enabled path never falls back on a mapping/layout/authentication failure. Unknown/malformed settings fail startup.

Configuration is frozen for the dedicated singleton host. Updates to secrets, routing, versions or operation permission require a controlled **stop ingress → drain all instances → update configuration/storage in a quiet window → restart** transition. Source evidence is short-lived and bound to one host instance; it cannot be reused after restart. There is no live reload or cross-instance instantaneous revocation guarantee. The protected directory lock covers storage mapping; the separately repeated Owner checks cover Basic credentials, actor, registration, permission and expiry before write/commit.

This limited profile supports the bounded issuance-record/revocation chain, not an entire Main application database. Normal user/department snapshots, bootstrap, dispatch, ERP, login, all Core tables and environment switching remain outside this installation. The actual HTTPS validation host isolates unrelated background services and supplies only the issuance tenant-active lookup as fixture support; authentication, context authority, token signing/recording and revocation storage are production implementations. See the [single delivery report](../../docs/development/evidence/WP02-02/service-revocation/DELIVERY.md).

## Bounded service token issuance and revocation profile

`CP6.TenantStorage install-service-token-v1 --stdin` explicitly installs **service-token-v1** / **service-token-tenant-rls-v1** in a fresh isolated database. Use the revocation input shape above **without `IssuerLogin`**; unknown fields (including that old field) are rejected. There is no separate cross-tenant issuance writer. The private-input, control-identity, physical-target, empty-layout and same-request recovery rules still apply. The installation uses `cp6_service_token_v1.installation`, without changing either older profile or stamping full EF migration history. This profile cannot overlay or upgrade an older installation.

The baseline selects the original five tables: the four revocation tables plus the real shared `public.Sys_Tenants`, including its complete frozen columns, types, capacity constraints, primary key and unique tenant-code index. The installer does not seed tenant data or enable tenants from storage mappings. The versioned directory control command below can supply eligibility rows for already registered service tenants; the installer does not implement tenant creation or maintenance.

Tenant logins retain ordinary role eligibility and no membership, ownership, role settings or DDL/TEMP rights. They gain tenant-constrained ServiceToken INSERT in addition to the original revocation grants. They receive no direct Sys_Tenants SELECT, UPDATE or directory enumeration. A fixed, no-argument SECURITY DEFINER helper uses authenticated `session_user` through the protected mapping, returns only that tenant's Enable/ExpireDate, and holds `FOR SHARE` on its row through the enclosing commit. The function is owned by the installation control owner, has an explicit search path and fully qualified object references; runtime gets only EXECUTE. The table remains shared and is not globally tenant-filtered. Original `ExpireDate` uses server-local wall time stored as `timestamp without time zone`; null means no deadline and equality is expired. The application reads its clock after obtaining the row lock and again before commit, rather than using a transaction-start timestamp.

The new seal includes the existing structural seal plus explicit tenant grants on system-catalog tables, columns, functions and schemas. Fresh installation rejects preexisting target-database object grants. Unrelated ordinary privileges in another database are not an authorization to access this installation and are outside this target's ACL checks; elevated role flags, membership or ownership are still rejected. The older catalog-seal SQL and recovery meanings remain unchanged.

Enable only `CrmIdentity:ServiceTokenTenancy`, using the same explicit fields listed for ServiceRevocationTenancy. Enabling both sections is rejected; profile selection comes from the section and cannot be overridden by a request or arbitrary ModelVersion. The DefaultConnection target must still match the explicit endpoint/database as a configuration consistency check, but this path never opens it for tenant eligibility, recording or self-revocation. Its database credentials have no issuance privileges from this installer. No failure falls back to that connection.

The dedicated host freezes the signing inputs and the service-client/organization/tenant/region registration. Actual Basic credentials create a short-lived Service actor with an issuance-specific purpose. Issuance additionally requires the frozen client Enabled, cp6.services scope and CrmEnabled organization; self-revocation retains its separate permission and continues for disabled clients. The ordinary tenant connection performs real directory lookup, ServiceToken recording and final configuration/source/tenant/token-expiry checks in one physical PostgreSQL transaction. Successful signing and confirmed commit precede the token response. Concurrent tenant-disable UPDATE is serialized by the row lock. Configuration changes continue to require stopping ingress and draining every instance before controlled update/restart; this is not a live revocation protocol.

The original issuance API has no idempotency key. A lost response after commit leaves a persisted token record; retry is a new issuance with a new jti, not recovery of the original token. Self-revocation keeps its existing persisted-result behavior. This five-table profile supports only the bounded service endpoints, not full Main startup, browser/user login, bootstrap/dispatch, or production acceptance. See [the issuance delivery report](../../docs/development/evidence/WP02-02/service-issuance/DELIVERY.md).

## Registered service tenant directory control v1

`CP6.TenantStorage service-token-tenant-directory-control-v1 --stdin` operates on exactly one already registered tenant in an existing `service-token-v1` installation. It accepts one JSON document (at most 16,384 characters), only on redirected private stdin. Do not put secrets in arguments, shell history, environment-wide configuration, source control or logs. Unknown/duplicate properties are rejected. The installation object must be the original approved installation request, including the complete original tenant/login mapping; there is no caller-supplied seal or fingerprint.

Example `inspect` input (substitute the approved physical identity, original mappings and private control credentials):

```json
{
  "Request": {
    "Installation": {
      "InstallationId": "11111111-1111-4111-8111-111111111111",
      "Server": "192.0.2.10",
      "Port": 5432,
      "Database": "explicit_issuance_database",
      "DatabaseOid": 12345,
      "SystemIdentifier": "trusted-numeric-cluster-identity",
      "Tenants": [{ "TenantId": "22222222-2222-4222-8222-222222222222", "Login": "precreated_tenant_login" }]
    },
    "TenantId": "22222222-2222-4222-8222-222222222222",
    "Operation": "inspect"
  },
  "AdminUsername": "separate_database_owner",
  "AdminPassword": "private-control-secret",
  "SslMode": "VerifyFull"
}
```

Keep `Installation`, `TenantId` and credentials unchanged when choosing an operation:

| Operation | Additional Request fields | Result |
| --- | --- | --- |
| `inspect` | None | `CURRENT` with this tenant's Id/code/name/eligibility, or `MISSING`. No enumeration or inferred enablement. |
| `initialize` | `TenantCode`, `TenantName`, `Desired: { "Enable": true, "ExpireDate": null }` | `INITIALIZED`, `ALREADY_EXISTS` with the unchanged current row, or `CODE_CONFLICT`. Never overwrites an existing row, even when the requested values differ. |
| `set-eligibility` | `Expected: { "Enable": true, "ExpireDate": null }`, `Desired: { "Enable": false, "ExpireDate": null }` | `UPDATED`, `CAS_CONFLICT` with the observed current row, or `MISSING`. Only Enable/ExpireDate and original Modifier/ModifyDate metadata change. |

Both `Enable` and `ExpireDate` must be explicitly present in every eligibility object; null is an explicit unlimited deadline, not an omitted CAS condition. CAS compares both old fields using PostgreSQL `IS NOT DISTINCT FROM`. Timestamp strings are finite **local wall times without an offset**, for example `"2030-10-17T12:34:56.123456"`. UTC `Z`, offsets, .NET infinity sentinels and sub-microsecond input are rejected rather than converted or rounded. Existing helper semantics remain unchanged: equality is expired, null is unlimited; TimeZoneId is not consulted and expiry does not automatically set Enable=false.

For `Expected` and `Desired` (including `initialize`), the CLI checks the decoded original timestamp text **before DateTime deserialization**: every fractional digit beyond the sixth must be zero. For example, `.1234560` and `.1234560000000000` remain exact equivalents of `.123456`, while `.12345601` is rejected before execution with `NOT_APPLIED` / `input-rejected`, even if DateTime would discard its nonzero tail. The existing JSON timestamp syntax limits and explicit-null rules still apply.

Code and name must be nonblank, valid Unicode without NUL, within the frozen 50/200 UTF-16-unit capacities. The command does not trim or change case. The original `bpchar` / `cp6_ci_as_provider_v1` unique index decides equality, including case and trailing-space equivalence. Initialization uses the registered TenantId as Id, explicit desired eligibility, TwoFactorMode=0, nullable optional fields, database-local CreateDate and authenticated control login as Creator. It does not create an admin, role, credential or mapping. Control metadata records the database login, not a natural-person business audit identity.

Every operation uses one unpooled physical control connection and transaction. The authenticated login must be the independent installation owner, a superuser and database owner, with session_user=current_user. Numeric endpoint, database/OID/cluster identity, protected installation ownership, original canonical inputs, compiled baseline hash, unchanged profile/seal and exact role OID/name/tenant mapping are checked before DML and again before commit. No runtime/default business connection is used. The old installer and its recovery contract are unchanged.

Shared installation/directory advisory locks and table SHARE locks pin protected rows while remaining compatible with runtime readers. A database-local transaction advisory lock per tenant serializes missing-row initialization; the original PK/code unique constraints also arbitrate external INSERT races. `FOR UPDATE` on the exact Sys_Tenants row naturally waits for the issuance helper's `FOR SHARE` and holds the row through commit. Other tenant readers can continue. Lock timeout is 15 seconds, statement timeout 60 seconds and CLI timeout three minutes. Use the existing quiet administration policy for configuration/DDL changes; these locks do not fence arbitrary concurrent superuser changes to roles or schemas.

Success/conflict JSON on stdout has `Contract`, `Outcome`, `Current`; failure JSON on stderr has `Contract`, `Outcome`, sanitized `Code`. Exit 0 means a completed observation (`CURRENT`/`MISSING`) or acknowledged write (`INITIALIZED`/`UPDATED`), 3 means an observed conflict (`ALREADY_EXISTS`/`CODE_CONFLICT`/`CAS_CONFLICT`), 1 means rejected input or acknowledged precommit rollback (`NOT_APPLIED`), 4 means `UNKNOWN`. Usage errors remain exit 2. `MISSING` during set-eligibility is an observation with no write; callers must check Outcome rather than only exit code.

**No request idempotency or original-result recovery is provided.** Commit errors, unacknowledged rollback, or output loss after execution are classified conservatively as `UNKNOWN`; a killed process or lost output may prevent any classification from reaching the caller and must also be treated as unknown. Do not automatically retry an uncertain write. Inspect reports current state only, cannot prove whether a particular request committed, and cannot recover its original result. CAS can still succeed after ABA (old state → different state → old state); repeat initialize returns the current row, not a saved result. A durable request/result slot requires a separate versioned design. The installer's same-input recovery instructions above do not apply to this control command.

This is limited eligibility-directory DML, not full tenant lifecycle, TenantAdminService, business audit, online configuration/credential updates, schema migration or full Main acceptance. See the [control delivery report](../../docs/development/evidence/WP02-02/tenant-eligibility-control/DELIVERY.md).
