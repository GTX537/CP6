# Explicit PostgreSQL messaging storage initialization

This internal operator command initializes **only** `erp-identity-messaging-v1` in an explicitly selected, empty, isolated PostgreSQL database. It does not authorize deployment, switch a business connection, create a database, create/rotate runtime credentials, or install Core business tables.

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
