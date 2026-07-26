# E01-S03 Job Ledger

Date: 2026-07-26
Branch: `codex/space-e01-job-ledger`
Base: E01-S02 `5463825c`

## Outcome

E01-S03 establishes the database-backed worker ledger used by later import,
clone, validation, scene, publish, and reconciliation slices.

- `Space_Job` is the task authority. Queue notifications may wake a Worker but
  cannot create or complete work independently of the ledger.
- `Space_JobAttempt` records every claim, takeover, failure, cancellation, and
  terminal outcome.
- `Space_JobStep` records versioned checkpoints and output hashes with unique
  `(TenantId, AttemptId, StepCode)` and step-number constraints.
- `Space_ModelIssue` stores localized issue arguments, severity, source/job
  context, blocking status, and auditable Warning acknowledgement.
- `Space_Artifact.JobId` adds Job-to-output provenance while continuing to use
  `Space_File` for object identity, hash, size, safety state, and retention.

## Lease and recovery semantics

- Claim uses SQL Server `rowversion` optimistic concurrency. Two Workers may
  inspect the same candidate, but only one transaction can persist the claim
  and its Attempt.
- Each claim creates a new `ActiveAttemptId`. This is the fencing token used by
  renew, progress, checkpoint, completion, failure, and cancellation writes.
- A takeover is allowed only after lease expiry. The prior Attempt becomes
  `Abandoned`; the old Worker is rejected with `SPACE_JOB_LEASE_LOST`.
- Default protocol remains a 20-second renewal cadence and 60-second lease;
  durations are supplied by the Worker host rather than hard-coded in storage.
- Progress never moves backwards and the total cannot change once established.
- Cancellation of a running Job is a request. The active Worker records
  `Cancelled` only at a safe checkpoint.
- An expired final Attempt becomes `DeadLetter`; it is not claimed past
  `MaxAttempts`.

## Retry and idempotency

- Business keys are SHA-256 values generated from server-owned canonical
  fields: Job type, subject type/id, input hash, processor version, and variant.
- The filtered unique index permits at most one Queued/Running Job for
  `(TenantId, JobType, BusinessKey)`.
- Concurrent duplicate enqueue returns the winning active Job after the unique
  constraint resolves the race.
- Transient and Bug failures use deterministic exponential backoff, capped at
  15 minutes, then DeadLetter at the attempt limit.
- Resource and Input failures stop for operator/user correction. Security
  failures are never retryable.
- Terminal Jobs are immutable. Manual retry creates a new Job with
  `RetryOfJobId`; Input retry requires a changed business key.
- A successful step is reusable after takeover only when Job, input hash,
  processor version, and step code match.

## Persistence

Migration: `20260726080918_SpaceE01S03JobLedger`

The migration:

- creates `Space_Job`, `Space_JobAttempt`, `Space_JobStep`, and
  `Space_ModelIssue`;
- adds nullable `JobId` and tenant-safe Job foreign key to `Space_Artifact`;
- adds tenant-composite foreign keys for Attempt, Step, Issue, retry lineage,
  diagnostic Artifact, and Job Artifact;
- adds active-Job, claim-order, attempt-number, step-code, issue, subject, and
  correlation indexes;
- adds database checks for attempt limits, progress, lease consistency,
  terminal timestamps, and Issue context;
- is additive and contains no legacy-table reference or destructive data
  operation.

An idempotent SQL script is stored beside the Space migrations.

## Verification

- Space unit tests: 35 passed, 0 failed.
- Space integration tests with SQL Server LocalDB: 25 passed, 0 skipped.
- Existing CP6 tests with SQL Server LocalDB: 2528 passed, 1 existing SQLite
  structural test skipped.
- Domain/application unit tests cover state transitions, deterministic business
  keys, duplicate enqueue, monotonic progress, takeover fencing, retry
  classification/backoff, cancellation, non-retryable failures, and Issue
  acknowledgement.
- SQL Server tests cover two-Worker claim competition, concurrent duplicate
  delivery, lease-expiry takeover, stale Worker rejection, cancellation at a
  safe checkpoint, matching checkpoint reuse, final-attempt DeadLetter,
  tenant-scoped active uniqueness, cross-tenant foreign-key rejection, and
  progress/Issue query projection.
- `dotnet ef migrations has-pending-model-changes` reports no pending changes.

## Deferred by scope

- HTTP Job/Issue endpoints and permission mapping: E01-S05.
- Concrete CAD, Excel, validation, scene, clone, and publish processors:
  their owning feature slices.
- File scanner/sandbox implementation and retention cleanup: E01-S06.
- Message-queue wakeups and a deployable Worker host: deployment integration
  after the first concrete processor is available.
