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
