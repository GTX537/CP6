# C03 ERP integration implementation plan

> **For agentic workers:** Use `subagent-driven-development` for bounded independent work and `executing-plans` for dependent integration. Repository AGENTS.md requires one concentrated review for the complete task, with targeted risk checks during implementation.

**Goal:** Deliver the real CP6 ERP read APIs and transactional BP/order request handlers required by CRM executable specification §9 and §12, then exercise requests and results through isolated SQL Server and Dapr/Kafka.

**Architecture:** The existing ERP BusinessPartner, Quotation, ProductMaster and Order tables remain authoritative. A dedicated ERP integration journal joins the same SQL transaction as business changes and Platform Inbox/Outbox. CRM consumes the same pinned event schemas and uses service-authenticated reads; no CRM message carries legal master data or order lines.

**Tech stack:** .NET 8, EF Core 8 SQL Server, CP6.Platform.Messaging/EntityFramework 0.10.2, Dapr and Kafka.

## Baseline and boundaries

- Core branch starts at confirmed `origin/main` 53ad3d8813b59caadee9b2fd8030cb0d2db4e741; CRM companion branch starts at 70d5ecac5445556375521d5ff079beb548411639.
- C01/C02 are already integrated. Existing local main checkouts belong to other work; do not reset or modify them.
- Ordinary GitHub Actions remain manual only. Before every push/PR mutation inspect candidate and protected-branch triggers and queued/running jobs. Use local verification and normal protected PR merge.
- C03 development/UAT evidence does not satisfy production, CRM12, C04A migration or C04B cutover acceptance by itself. Preserve those prerequisite gates.
- Preserve accepted CRM action names and the frozen private product specification. Engineering additions belong in public executable contracts and task records.

## 1. Freeze request/result contracts

Files: `contracts/events/erp/`, `CP6.Core/Services/ErpIntegration/ErpEventContracts.cs`, `ErpEventValidator.cs`, `CP6.Tests/ErpIntegration/ErpEventContractTests.cs`; equivalent contract assets and adapter in the companion CRM repository.

- [x] Add failing contract tests for six types: CRM BP requested/order requested; ERP BP synchronized/BP failed/order created/order failed.
- [x] Pin Draft 2020-12 schemas and valid, missing-required, wrong-type, unknown-optional and PII-negative examples with the existing Platform bundle format.
- [x] Require canonical tenant/request/account IDs and positive requestVersion. Order messages also identify opportunity; requests bind businessPartnerKey, quotationKey, expectedAmount, currency and quotationVersion. Results repeat request identity and carry monotonic resultVersion, with stable errorCode/retryable or ERP keys and booked amount.
- [x] Bind source/topic/subject/aggregate/tenant/partition and envelope metadata. Aggregate is account ID for BP and opportunity ID for orders. Reject duplicate JSON properties and sensitive extensions recursively while accepting harmless unknown optional data.
- [x] Run `dotnet test CP6.Tests/CP6.Tests.csproj --filter FullyQualifiedName~ErpEventContractTests`. Record a failing test before implementation and the passing targeted result.

## 2. Add ERP authority and transaction model

Files: ERP entity/DTO/service files for BP and quotation; `CP6.Entity/DomainModels/Erp/ErpIntegrationModels.cs`, `CP6.Core/EFDbContext/CP6Context.cs`, forward migration, `CP6.Core/Services/ErpIntegration/`.

- [x] Expose a typed ERP-owned CRM account binding on registered BP master data. Validate customer requirements through BusinessPartnerService; absent legal/master data is a terminal failure, never synthesized from a CRM display name.
- [x] Add explicit quotation currency, accepted timestamp, validity timestamp and accepted content binding. Existing internal approval flags are not customer acceptance. ERP-owned mutation/acceptance operations must invalidate or rebind acceptance when price/lines/customer change.
- [x] Store integration request identity, canonical input hash, current attempt/result version, terminal outcome and ERP key; unique tenant/account/requestVersion for BP and tenant/opportunity/requestVersion for orders. A unique index on actual order tenant/opportunity enforces at most one created order across versions, including soft deletion.
- [x] Use real SQL constraints and tenant-scoped queries, including soft deletion and disabled tenant checks. Lock the opportunity and relevant quotation/BP/product state while constructing an order. Serialize document-number allocation against all ERP order writers.
- [x] Create a forward migration and isolated SQL test baseline. Exercise rollback after business writes and before Outbox commit, competing request versions, duplicate IDs with conflicting payloads and cross-tenant keys.

## 3. Implement ERP handlers and reads

Files: `CP6.Core/Services/ErpIntegration/ErpRequestHandler.cs`, `ErpQuotationOrderFactory.cs`, `ErpReadService.cs`, `CP6.WebApi/Controllers/Internal/ErpIntegrationController.cs`, runtime registration and Dapr subscription assets.

- [x] BP requests validate/associate the real ERP-owned BP registration and publish synchronized or terminal failure atomically with Inbox and journal state.
- [x] Order requests revalidate accepted/current/unexpired quotation, exact customer/account link, amount/currency, active customer and approved product mapping. Map each QuotationDetail.DetailNo to exactly one ProductMaster by quotation number and Branch1 in D4 format. Snapshot existing order product/process/material fields through existing ERP mappings.
- [x] Commit Inbox, journal, order and success Outbox in one SQL transaction. Bridge side effects must run only after durable commit with a retryable durable dispatch, not escape through best-effort hooks inside an uncommitted transaction.
- [x] Preserve Terminal outcomes until a new requestVersion. Technical errors roll back and retry the same version. Replays after success return the persisted outcome and never construct another order.
- [x] Implement `GET /internal/erp/v1/business-partners/{key}`, `/quotations/{key}`, `/orders/{key}`. Independently validate C01 CP6.Services token, exact tenant/correlation metadata, active service/tenant, recorded non-revoked token and an ERP-specific reader allowlist. Request ingress has a distinct authenticated subscription boundary.
- [x] Run targeted HTTP/authentication and SQL handler tests before claiming usable endpoints.

## 4. Wire the CRM consumer and local real transport proof

Files in CRM companion: `src/CP6.CRM.Infrastructure/Erp/`, unit/SQL tests, contract assets and local UAT runner. Final placement must follow existing CRM aggregate and messaging boundaries; add real prerequisite/process transitions without bypassing accepted quotation or Won guards.

- [x] Add the C01 service-token read client with expiry and 401/403 invalidation. Enforce response tenant/resource identity and fail closed on unavailable authority.
- [ ] Deferred to CRM06/07 after CRM05 supplies Account/Opportunity aggregates: add transactional BP/order result handling with Inbox dedupe, current requestVersion/resultVersion guards and stable terminal/retryable semantics. Completing BP writes the link and releases only the matching blocked order Outbox. Completing order writes the order link and Won in the same transaction where the CRM opportunity implementation is available.
- [x] Publish requests through real CRM Outbox and Dapr/Kafka to real ERP SQL handlers, then consume results through the same transport. No test process directly inserts ERP results or a Won state.
- [x] Capture baseline SQL hash, producer/handler source identity, contract hashes, request/Inbox/order/Outbox identifiers and canonical final SQL reconciliation. Include concurrency, broker interruption, duplicate and out-of-order delivery and terminal business errors.

## 5. Review and integrate the complete task

- [x] Check every §9/§12 and C03 row requirement against actual code, tests and transport evidence; record remaining CRM06/07/12 or migration prerequisites accurately rather than declaring them covered by a contract test.
- [x] Concentrated spec/quality/security/concurrency review of the complete diff; fix substantive findings and rerun only affected verification unless scope changes.
- [x] Update `docs/project-memory/PROJECT_STATE.md`, `05-Completed.md`, `06-Todo.md`, `CHANGELOG-AI.md` in each affected repository with evidence and remaining boundaries.
- [x] Save explicit task commits, verify workflows, push branches and merge normal PRs. Verify remote main includes all task commits and perform necessary post-merge smoke checks with documented reused results.

## Acceptance evidence matrix

| Requirement | Required evidence |
| --- | --- |
| Real ERP ownership | SQL BP/quotation/order rows and existing master-data validation |
| Service read isolation | HTTP tests for wrong audience/tenant/client, expired/revoked token and missing resource |
| Contract cofreeze | Same bundle bytes in producer/consumer; both reject malformed/PII messages before effects |
| One order per opportunity | SQL concurrency across duplicate IDs and multiple request versions |
| Transactional result | Failure injection proves no order without success Outbox and no Inbox completion without outcome |
| Monotonic result processing | Probe Inbox/version checkpoint ignores stale/duplicate results; actual CRM business state transitions remain CRM07 scope |
| Real transport UAT | Dapr/Kafka request and result trace plus canonical SQL reconciliation |
| Delivery | Normal merged PRs and verified remote main commits |
