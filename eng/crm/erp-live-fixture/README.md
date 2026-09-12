# C03 ERP transport fixture

This CLI prepares the ERP side of a real C03 request/result acceptance run. It uses the production Core migrations,
BusinessPartnerService, QuotationService, ErpCommerceAuthority, OrderService, FxRateService, Inbox handler, Outbox dispatcher
and replay service. It neither creates CRM Accounts/Opportunities nor inserts ERP command results or Won state.

All database commands require an explicit `CP6_C03_TEST_SQL` connection to a local SQL Server with database creation rights.
There is no SQLite, in-memory or unavailable-SQL fallback. Initialization creates one new `CP6C03Live_<32 lowercase hex>`
database and a matching private directory outside Git repositories. Both the private ownership manifest and an independent
database extended-property marker must match before subsequent operations. Existing application databases are never attached.

Build this project only after coordinating with the main C03 build owner:

```powershell
dotnet build eng/crm/erp-live-fixture/CP6.ErpLive.Fixture.csproj
```

Invoke the resulting `CP6.ErpLive.Fixture.dll` with one of these commands:

```text
initialize <input.json> <privateParent>
dispatch <ownedDirectory/appsettings.Local.json>
snapshot <ownedDirectory/live-fixture.json> <public-output.json>
storage-fault <ownedDirectory/live-fixture.json> <tenantId> <on|off>
replay <ownedDirectory/live-fixture.json> <tenantId> <messageId> <operationId>
cleanup <ownedDirectory-or-privateParent>
```

`input.json` is private and contains the following exact camel-case fields:

```json
{
  "coreOrigin": "https://127.0.0.1:55101",
  "crmOrigin": "https://127.0.0.1:55102",
  "daprHttpEndpoint": "http://127.0.0.1:55103",
  "daprGrpcEndpoint": "http://127.0.0.1:55104",
  "daprApiToken": "<independent sidecar DAPR_API_TOKEN, at least 32 characters>",
  "daprAppToken": "<independent callback APP_API_TOKEN, at least 32 characters>",
  "certificatePath": "<absolute path to the fixture HTTPS certificate>",
  "certificatePassword": "<certificate password>"
}
```

The API token authorizes calls to the Dapr sidecar. The app token authorizes Dapr callbacks into Core; the values must differ.
The caller owns ports, TLS trust, authenticated Dapr/Kafka infrastructure and the `cp6-core` app registration. This CLI does
not start containers, bind ports, stop an existing preview or launch Core. Core can be started with the private content root
and `ASPNETCORE_ENVIRONMENT=Local`; the caller supplies its exact HTTPS/loopback callback URLs. The generated configuration
enables C01/C02/C03 and disables automatic database initialization and hosted services. Run `dispatch` separately.

Initialization creates two real tenants (`c03-primary`, `c03-isolated`), one ERP staff identity in each tenant, real base/staff
masters and ERP permissions, fully validated preregistered customer BPs with their commerce profiles, and real quotations
with explicit customer acceptance through the ERP authority service. Approved ProductMaster rows are fixture master-data
seeds; this does not test the product-approval workflow. The first customer uses JPY; the second uses USD with a real 150 JPY/USD
rate entry. The BPs remain preregistered until actual CRM requests travel through Kafka/Dapr and the production handler.

The current fixture explicitly supplies matching `PCS` quotation, quantity and price units. The snapshots include all three
unit fields and the actual order units, so a transport run can verify that accepted quantities/prices retained their meaning.
Earlier fixture snapshots without these fields are unchanged historical evidence, not verification of this correction.

`live-fixture.json` and `appsettings.Local.json` contain credentials and must stay private. The private document has:

- Top level: `schemaId`, `database`, `connection`, `coreContentRoot`, `coreOrigin`, `crmOrigin`, `coreAppId`, `browserSecret`,
  `tenants`, `authority`.
- Each `tenants[]`: `id`, `slug`, `region`, `accountId`, `departmentId`, `readerClientId`, `readerSecret`, `users`,
  `businessPartner`, `quotation`, `product`.
- Each `users[]`: `id`, `label` (`erp-staff`), `userName`, `password`, `roleId`, `departmentId`, `baseCode`, `salesStaffCode`,
  `businessStaffCode`. The reader client is registered to exactly that tenant for `cp6.services` service grants.
- `businessPartner`: `id`, `key`, `rowVersion`, `status`, `accountId`, `frozen`, `currency`.
- `quotation`: `id`, `key`, `rowVersion`, `businessPartnerKey`, `totalAmount`, `currency`, `validUntilUtc`, `acceptedAtUtc`,
  `acceptedContentSha256`, `detailNo`, `quantity`, `unitPrice`.
- `product`: `id`, `key`, `rowVersion`, `quotationKey`, `branch`, `status`, `workflowApproved`.

Row versions are base64 representations of actual eight-byte SQL rowversion values. `authority` records actual applied
migrations, Core/API assembly and contract hashes, and the hash of the initial canonical SQL snapshot plus migrations.
It is local fixture provenance, not a production image digest or environment acceptance.

`dispatch` starts the actual C03 result Outbox worker and durable command retry worker, plus the C02 identity Outbox worker.
The real OrderService/FxRateService are used. Throw-on-use external hook guards only assert that the transaction does not call
WMS/MES/PowerEgg. They never synthesize a successful hook result. The real post-commit `OrderBridgeDispatch` rows remain pending
and are included in the snapshot. This fixture does not claim WMS/MES downstream acceptance. Ctrl+C cancels these workers.

`snapshot` reads a consistent SQL snapshot for the two owned tenants. Its top-level fields are `schemaId`, `observedAtUtc`,
`authority`, `tenantIds`, `scope`, `canonical`. `canonical` contains `tenants`, `businessPartners`, `quotations`, `quotationLines`,
`products`, `inbox`, `requests`, `aggregates`, `outbox`, `orders`, `orderLines`, `orderBridges`, `replayAudits`, `identitySnapshots`,
`serviceTokens` and `storageFaults`. Explicit SQL column allowlists exclude credentials, token values, raw event payloads,
legal/contact names and addresses. Service-token evidence is limited to tenant-level counts of total, revoked and active rows.
This output can be used for public evidence. Raw private manifests and private error logs cannot.

`storage-fault` adds/removes only `dbo.T_Order.C03Fixture_OrderStorageFault`, a fixture-owned tenant-filtered CHECK constraint.
It is artificial storage fault injection, with SQL error 547 for new writes to the specified tenant. `WITH NOCHECK` preserves
existing rows; updates of matching rows would also fail while enabled, so the runner must avoid them. Other-tenant inserts
remain valid. A trigger is intentionally avoided because SQL Server would reject EF's normal INSERT/OUTPUT for all tenants.
The JSON stdout fields are `schemaId`, `tenantId`, `enabled`, `mechanism`, `constraintName`, `expectedSqlError`,
`artificialFixtureFault`, `preservesExistingRows`. A fault owned by another tenant or manifest is never replaced or removed.

The fixture configures three Inbox attempts and 1–8 second retry delays so the real API/worker can reach a durable dead letter.
After turning the storage fault off, `replay` reads the real receipt hash/rowversion and invokes ErpInboxReplayService with
the fixture ERP user's real ID, an explicit operation ID, and `dependency-recovered`. It cannot replace the message payload.
The worker processes the original message and the result still traverses Kafka. JSON stdout fields are `schemaId`, `tenantId`,
`operationId`, `messageId`, `replayedAtUtc`, `payloadSha256`, `inputRowVersion`, `driver`. Repeating the same operation preserves
the original audit input for the production replay service to enforce. This CLI drives the domain service; HTTP operator
authentication and authorization are verified separately and are not claimed by this transport run.

`cleanup` only drops matching, independently marked owned databases and verifies their absence. It retains private evidence
files and never enumerates or removes other CRM databases. Errors print stable safe codes; detailed diagnostics are written
only beneath the supplied private directory. No command prints secrets.
