import assert from 'node:assert/strict'
import { test } from 'node:test'
import YAML from 'yaml'
import {
  buildSnapshot,
  EXPECTED_INPUTS,
  validateSpec,
  verifySnapshot,
} from './r2-release-readiness.mjs'

const NOW = new Date('2026-07-28T12:00:00Z')
const GIT_SHA = '0123456789abcdef0123456789abcdef01234567'

function createReadySpec() {
  const phaseById = {
    'pilot-scope': ['R2A'],
    'role-owners': ['R2A'],
    devices: ['R2A'],
    'master-data': ['R2A'],
    'r2b-scope': ['R2B'],
    observability: ['Compose'],
    recovery: ['Compose'],
    'change-approval': ['Compose'],
  }
  return {
    schemaVersion: 1,
    release: {
      version: '1.0.0',
      tag: 'v1.0.0',
      state: 'Approved',
      changeTicket: 'CHG-1000',
      approvedAt: '2026-07-28T10:00:00Z',
      approvalExpiresAt: '2099-01-01T00:00:00Z',
    },
    environments: {
      freeze: 'r2-release-freeze',
      candidate: 'r2-candidate',
      compose: 'r2-pilot-compose',
    },
    runners: {
      freeze: ['self-hosted', 'Windows', 'X64', 'cp6-release'],
      candidate: ['self-hosted', 'Windows', 'X64', 'cp6-release'],
      compose: ['self-hosted', 'Windows', 'X64', 'cp6-deploy'],
    },
    deployment: {
      target: 'Compose',
      environment: 'r2-pilot-compose',
      baseUrl: 'https://pilot.cp6.example',
      evidenceRootUri: 's3://cp6-evidence/releases/v1.0.0',
      namespace: 'cp6-production',
      ingressHost: 'N/A',
      ingressClass: 'nginx',
      tlsSecret: 'cp6-tls',
    },
    tagPolicy: {
      creator: 'GitHubApp',
      immutable: true,
      nextVersionAfterFailure: '1.0.1',
      clientIdSecretReference: 'env:CP6_RELEASE_TAG_APP_CLIENT_ID',
      privateKeySecretReference: 'env:CP6_RELEASE_TAG_APP_PRIVATE_KEY',
    },
    inputs: EXPECTED_INPUTS.map((id) => {
      const requiredFor =
        phaseById[id] ??
        (['api-tls', 'database', 'redis', 'messaging', 'identity', 'storage'].includes(id)
          ? ['Candidate', 'Compose']
          : ['Candidate'])
      const dueNow = requiredFor.includes('Candidate') || requiredFor.includes('Compose')
      return {
        id,
        description: `${id} input`,
        requiredFor,
        owner: dueNow ? 'owner@example.test' : 'PENDING',
        targetDate: dueNow ? '2026-07-31' : null,
        secretReference: dueNow ? 'env:CP6_TEST_REFERENCE' : 'N/A',
        evidenceUri: dueNow ? `s3://cp6-evidence/approvals/${id}.json` : 'PENDING',
        approver: dueNow ? 'approver@example.test' : 'Pending Role',
        approvedAt: dueNow ? '2026-07-28T09:00:00Z' : null,
        expiresAt: dueNow ? '2099-01-01T00:00:00Z' : null,
        status: dueNow ? 'Approved' : 'Pending',
      }
    }),
  }
}

test('Structure accepts the committed draft shape', () => {
  const spec = createReadySpec()
  spec.release.state = 'Draft'
  spec.release.changeTicket = 'PENDING'
  spec.release.approvedAt = null
  spec.release.approvalExpiresAt = null
  spec.inputs.forEach((input) => {
    input.status = 'Pending'
    input.owner = 'PENDING'
    input.targetDate = null
    input.evidenceUri = 'PENDING'
    input.approvedAt = null
    input.expiresAt = null
  })
  assert.doesNotThrow(() =>
    validateSpec(YAML.stringify(spec), { mode: 'Structure', expectedVersion: '1.0.0', now: NOW }),
  )
})

test('Freeze accepts only approved Candidate and Compose inputs', () => {
  const raw = YAML.stringify(createReadySpec())
  assert.doesNotThrow(() =>
    validateSpec(raw, { mode: 'Freeze', expectedVersion: '1.0.0', now: NOW }),
  )
})

test('Freeze rejects a pending required input', () => {
  const spec = createReadySpec()
  spec.inputs.find((input) => input.id === 'windows-signing').status = 'Pending'
  assert.throws(
    () => validateSpec(YAML.stringify(spec), { mode: 'Freeze', now: NOW }),
    /windows-signing\.status must be Approved/,
  )
})

test('Freeze rejects expired approvals and approval ordering conflicts', () => {
  const expired = createReadySpec()
  expired.release.approvalExpiresAt = '2026-07-28T11:00:00Z'
  assert.throws(
    () => validateSpec(YAML.stringify(expired), { mode: 'Freeze', now: NOW }),
    /approvalExpiresAt must be in the future/,
  )

  const approvalConflict = createReadySpec()
  approvalConflict.inputs.find((input) => input.id === 'windows-signing').approvedAt =
    '2026-07-28T11:00:00Z'
  assert.throws(
    () => validateSpec(YAML.stringify(approvalConflict), { mode: 'Freeze', now: NOW }),
    /approvedAt must not be later than release\.approvedAt/,
  )
})

test('Structure rejects secret-bearing fields and duplicate inputs', () => {
  const withSecret = createReadySpec()
  withSecret.password = 'not-allowed'
  assert.throws(() => validateSpec(YAML.stringify(withSecret)), /forbidden secret-bearing field/)

  const duplicate = createReadySpec()
  duplicate.inputs[1].id = duplicate.inputs[0].id
  assert.throws(() => validateSpec(YAML.stringify(duplicate)), /Duplicate release input/)
})

test('Structure rejects a version mismatch', () => {
  assert.throws(
    () =>
      validateSpec(YAML.stringify(createReadySpec()), {
        mode: 'Structure',
        expectedVersion: '1.0.1',
      }),
    /release\.version must equal 1\.0\.1/,
  )
})

test('Snapshot binds version, source SHA, spec hash and deployment', () => {
  const raw = YAML.stringify(createReadySpec())
  const spec = validateSpec(raw, { mode: 'Freeze', expectedVersion: '1.0.0', now: NOW })
  const snapshot = buildSnapshot({
    spec,
    raw,
    repositoryPath: 'docs/client/r2/releases/v1.0.0/candidate.yaml',
    gitSha: GIT_SHA,
    actor: 'release-owner',
    runUri: 'https://github.example/actions/runs/1',
    generatedAt: NOW,
  })
  const rawSnapshot = `${JSON.stringify(snapshot, null, 2)}\n`
  assert.doesNotThrow(() =>
    verifySnapshot({
      snapshot,
      rawSpec: raw,
      spec,
      expectedVersion: '1.0.0',
      expectedGitSha: GIT_SHA,
      rawSnapshot,
      now: NOW,
    }),
  )
  assert.throws(
    () =>
      verifySnapshot({
        snapshot,
        rawSpec: raw,
        spec,
        expectedVersion: '1.0.0',
        expectedGitSha: 'f'.repeat(40),
        rawSnapshot,
        now: NOW,
      }),
    /gitSha does not match/,
  )
})
