# C02 producer SQL and HTTP verification

This fixture runs the actual Core save pipeline, forward migrations, SQL grant stores, service-token endpoints and internal snapshot controllers. Its HTTP case uses real loopback TCP/TLS, an in-memory generated certificate pinned only by fixture clients, live Discovery/JWKS, and published Platform `0.10.2` JWT middleware. No application authentication handler is replaced.

It is component verification. CRM authorization projections, Dapr/Kafka replay and the real 30-second revocation requirement are separate C02 acceptance work.

## Run

Provide `CP6_C02_TEST_SQL` privately with administrative access to an isolated local SQL Server. The fixture accepts only loopback/local machine SQL hosts. It creates a unique `CP6C02Test_<guid>` database, applies the historical migrations through the C01 baseline, preserves a seeded baseline user while applying the C02 migrations, and drops only its owned database in `finally`.

```powershell
dotnet restore eng/crm/identity-events-fixture/CP6.IdentityEvents.Fixture.csproj --locked-mode
dotnet run --project eng/crm/identity-events-fixture/CP6.IdentityEvents.Fixture.csproj --no-restore -- <public-output> <private-diagnostics>
```

The public directory contains summary/JUnit with assembly and contract hashes, explicit case counts, zero skipped cases and a nonzero exit on failure or incomplete execution. Private exceptions are never uploaded by CI. An optional third argument selects one exact case plus required migration setup; that report explicitly identifies its smaller scope and cannot count as the complete 23-case batch. Use distinct output directories for every attempt.

The batch covers unchanged content, concurrent versions, ordinary/priority rollback, envelope/snapshot/Outbox failure, `SaveChanges(false)`, native and direct grant/family revocation, service client ownership, role removal, department descendants, GDPR tombstones, bootstrap concurrency, cursor boundaries, actual HTTPS authorization and caller-owned transaction savepoints. In the latter case the caller catches a failed command and commits its outer transaction; the rejected command must leave no business-only write behind. MARS connections are rejected before such a write.

## Producer activation

The feature is off unless `CrmIdentity:Enabled=true`. Configure a CRM-enabled tenant and a registered service reader in the existing `CrmOidc` options before enabling it. Reader clients retain their existing strict Basic authentication and exact `cp6.services` scope; their tenant cannot be overridden by a request.

```json
{
  "CrmIdentity": {
    "Enabled": true,
    "Tenants": { "11111111-1111-4111-8111-111111111111": "us" },
    "ProjectionReaderClientIds": ["crm-projection-reader"],
    "DaprHttpEndpoint": "http://127.0.0.1:3500",
    "DaprGrpcEndpoint": "http://127.0.0.1:50001"
  }
}
```

The example tenant is illustrative, not an accepted deployment input. Use the existing secret store for SQL, client secrets and signing keys. `DefaultConnection` must have `MultipleActiveResultSets=False`; Core checks this on activation because EF SQL savepoints are disabled with MARS. Password expiry comes from the existing `Security:Password:ExpiryDays`, and the issuer comes from validated `CrmOidc:Issuer`.

Only `CP6Context` owns application migrations. Its C02 migration creates both `crm_identity` and `crm_identity_priority`; never run a second migration owner for the priority context. New migrations are forward only. Bootstrap creates each configured tenant's baseline atomically. Internal readers return 503 until that tenant's bundle-bound bootstrap is complete. Process readiness alone does not mean projection readiness.

`GET /internal/crm/identity/versions` accepts only `cursor` and `pageSize` (1–200). Cursors expire after 10 minutes and are protected, tenant-bound and boundary-bound. A change between pages returns 409 and requires restarting the baseline. `GET /internal/crm/identity/snapshots/{aggregateId}` returns the full safe data and its hash. Both endpoints require a currently registered reader, an active tenant and an actual persisted, unrevoked `CP6.Services` token. Unindexed old service tokens do not gain reader access.

`POST /connect/service-revocations` uses strict Basic authentication and exactly one form value, `jti`, in canonical GUID `D` format. The `(issuer, client, tenant, jti)` lookup returns the same empty 200 for an unknown or already revoked token. A persistence failure returns 503. No bearer token or client secret is written into events or token records.

The independent ordinary and priority dispatcher loops use batch budgets 32 and 16. Disabled facts and token revocations enter the priority queue in the caller's same SQL transaction. Both publish through Dapr component `cp6-kafka-pubsub`, topic `cp6.platform.events.v1`, partition `{tenantid}:{aggregateid}`. Publishing has a 10-second deadline, five-minute claim leases and at most 10 attempts with 1-second to 5-minute backoff. Dead letters and repeated bootstrap/dispatch failures produce safe logs; they require recovery before downstream authorization is treated as healthy. Local cross-repository acceptance verified priority progress while ordinary business events remained unpublished after an actual Core restart.

Publish metadata must include `rawPayload=true`: the payload is already a validated structured CloudEvent and its original data bytes define the snapshot hash. The consumer subscription uses `rawPayload=false` so Dapr delivers that existing structured event. These are distinct publish/subscribe settings. The Kafka component must exclude publisher-supplied reserved metadata using `excludeHeaderMetaRegex=(?i)^(__(key|topic|partition|offset|timestamp)|pubsubname)$`, with `escapeHeaders=false`.

`scripts/test-c02-identity-events.ps1 -OutputDirectory <fresh-public-attempt> -PrivateDiagnosticDirectory <private-attempt>` runs the narrow byte/metadata probe against the already locally built fixture. It reuses cached Dapr 1.18.2/Kafka 4.3.1 images, creates a unique Compose project and removes only those owned resources afterwards. The actual production publisher and a mixed-case reserved-header spoof must preserve exact bytes/data hashes and valid broker routing. A separate wrong broker key must fail delivery validation. The producer accepts both `partitionKey` and `__key` as aliases for setting the actual Kafka key; supplying both is ambiguous and is never a valid header-spoof positive test. This generated-event probe uses no SQL and is not the full Core/CRM denial-latency acceptance.

The CRM entry `scripts/test-c02-real-transport.ps1` uses this fixture's `live-initialize`, `live-dispatch` and `live-cleanup` modes with actual Core/CRM API processes, SQL, native password/PKCE, service tokens and Dapr/Kafka. The [consumer evidence locator](evidence/2026-09-10/crm-real-transport-locator.json) binds 13 distinct passed scenarios and three categories of 100 real denial samples. Original failed attempts and reviewed reuse are retained in the CRM evidence directory. This local result does not complete protected-main delivery or production acceptance.

## Package and contract identity

Core/WebApi reference exactly `[0.10.2]`. `NuGet.config` requires signatures and maps `CP6.Platform.*` only to the authoritative GitHub feed. CI supplies a job-scoped `GITHUB_TOKEN` with package read access. Docker restore uses a required BuildKit `nuget_token` secret, never a credential build argument or saved NuGet credential. Package access must be explicitly granted to the Core repository in GitHub Packages; a developer's local restore does not prove the Actions token's access.

Core owns `contracts/events/platform`. The five event families each include the full Draft 2020-12 envelope Schema and the five-example matrix. The content-addressed index is checked with:

```powershell
./eng/crm/update-identity-contract-index.ps1 -Check
```

The implementation's index SHA-256 is `24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63`. Consumers copy these exact bytes and pin the producer source commit; a copied working-tree bundle is not a delivered source identity. Data snapshots contain identifiers and authorization facts only. Permission snapshots are per `role:<int>` and include the entire grant set, including an empty set after removal. Browser revocations use the CRM grant's actual GUID `D` jti, and service revocations use the recorded issued jti and original expiry.
