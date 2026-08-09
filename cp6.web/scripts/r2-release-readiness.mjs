import { createHash } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'
import { pathToFileURL } from 'node:url'
import YAML from 'yaml'

export const EXPECTED_INPUTS = Object.freeze([
  'windows-signing',
  'windows-timestamp',
  'windows-distribution',
  'android-signing',
  'android-distribution',
  'api-tls',
  'database',
  'redis',
  'messaging',
  'identity',
  'storage',
  'release-tag-automation',
  'observability',
  'pilot-scope',
  'role-owners',
  'devices',
  'master-data',
  'r2b-scope',
  'recovery',
  'change-approval',
])

const ALLOWED_PHASES = new Set(['Candidate', 'Compose', 'R2A', 'R2B'])
const ALLOWED_INPUT_STATES = new Set([
  'Pending',
  'Ready for approval',
  'Approved',
  'Rejected',
  'Expired',
])
const ALLOWED_RELEASE_STATES = new Set([
  'Draft',
  'ReadyForApproval',
  'Approved',
  'Rejected',
  'Expired',
  'Consumed',
])
const PLACEHOLDER = /^(?:PENDING|TBD|TODO|CHANGE_ME|PLACEHOLDER)$/i
const FORBIDDEN_VALUE_KEYS = /^(?:password|privateKey|connectionString|token|secretValue|certificateData)$/i

function invariant(condition, message) {
  if (!condition) {
    throw new Error(message)
  }
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function hasOwn(value, key) {
  return Object.prototype.hasOwnProperty.call(value, key)
}

function assertKnownKeys(value, allowed, description) {
  for (const key of Object.keys(value)) {
    invariant(allowed.includes(key), `${description}.${key} is not allowed by the release spec schema.`)
  }
}

function requireObject(value, description) {
  invariant(isObject(value), `${description} must be an object.`)
  return value
}

function requireText(value, description, { allowPlaceholder = false } = {}) {
  invariant(typeof value === 'string' && value.trim().length > 0, `${description} is required.`)
  if (!allowPlaceholder) {
    invariant(!PLACEHOLDER.test(value.trim()), `${description} must not be a placeholder.`)
  }
  return value.trim()
}

function requireIsoDate(value, description, { future = false, past = false, now = new Date() } = {}) {
  const text = requireText(value, description)
  const parsed = new Date(text)
  invariant(!Number.isNaN(parsed.valueOf()), `${description} must use ISO 8601.`)
  if (future) {
    invariant(parsed > now, `${description} must be in the future.`)
  }
  if (past) {
    invariant(parsed <= now, `${description} must not be in the future.`)
  }
  return parsed
}

function requireUri(value, description, scheme) {
  const text = requireText(value, description)
  let parsed
  try {
    parsed = new URL(text)
  } catch {
    throw new Error(`${description} must be an absolute ${scheme} URI.`)
  }
  invariant(parsed.protocol === `${scheme}:`, `${description} must use ${scheme}://.`)
  invariant(parsed.username === '' && parsed.password === '', `${description} must not embed credentials.`)
  invariant(parsed.hostname.length > 0, `${description} must include a host or bucket.`)
  return text
}

function scanForSecrets(value, path = '$') {
  if (Array.isArray(value)) {
    value.forEach((item, index) => scanForSecrets(item, `${path}[${index}]`))
    return
  }
  if (!isObject(value)) {
    return
  }
  for (const [key, child] of Object.entries(value)) {
    invariant(!FORBIDDEN_VALUE_KEYS.test(key), `${path}.${key} is a forbidden secret-bearing field.`)
    scanForSecrets(child, `${path}.${key}`)
  }
}

function sha256(buffer) {
  return createHash('sha256').update(buffer).digest('hex').toUpperCase()
}

function assertSecretReference(value, description) {
  const reference = requireText(value, description)
  invariant(
    reference === 'N/A' ||
      /^env:[A-Z][A-Z0-9_]+$/.test(reference) ||
      /^vault:\/\/[A-Za-z0-9._~!$&'()*+,;=:@%/-]+$/.test(reference),
    `${description} must be N/A, env:NAME, or vault://path.`,
  )
}

function validateStructure(spec, expectedVersion) {
  requireObject(spec, 'Release spec')
  assertKnownKeys(
    spec,
    ['schemaVersion', 'release', 'environments', 'runners', 'deployment', 'tagPolicy', 'inputs'],
    '$',
  )
  invariant(spec.schemaVersion === 1, 'Release spec schemaVersion must be 1.')

  const release = requireObject(spec.release, 'release')
  assertKnownKeys(
    release,
    ['version', 'tag', 'state', 'changeTicket', 'approvedAt', 'approvalExpiresAt'],
    'release',
  )
  const version = requireText(release.version, 'release.version', { allowPlaceholder: true })
  invariant(/^\d+\.\d+\.\d+$/.test(version), 'release.version must use major.minor.patch.')
  invariant(release.tag === `v${version}`, 'release.tag must equal v plus release.version.')
  if (expectedVersion) {
    invariant(version === expectedVersion, `release.version must equal ${expectedVersion}.`)
  }
  invariant(ALLOWED_RELEASE_STATES.has(release.state), 'release.state is invalid.')
  for (const field of ['changeTicket', 'approvedAt', 'approvalExpiresAt']) {
    invariant(hasOwn(release, field), `release.${field} must be present.`)
  }

  const environments = requireObject(spec.environments, 'environments')
  assertKnownKeys(environments, ['freeze', 'candidate', 'compose'], 'environments')
  for (const field of ['freeze', 'candidate', 'compose']) {
    requireText(environments[field], `environments.${field}`, { allowPlaceholder: true })
  }

  const runners = requireObject(spec.runners, 'runners')
  assertKnownKeys(runners, ['freeze', 'candidate', 'compose'], 'runners')
  for (const field of ['freeze', 'candidate', 'compose']) {
    invariant(Array.isArray(runners[field]) && runners[field].length >= 4, `runners.${field} is invalid.`)
    runners[field].forEach((label) => requireText(label, `runners.${field} label`, { allowPlaceholder: true }))
  }

  const deployment = requireObject(spec.deployment, 'deployment')
  assertKnownKeys(
    deployment,
    ['target', 'environment', 'baseUrl', 'evidenceRootUri', 'namespace', 'ingressHost', 'ingressClass', 'tlsSecret'],
    'deployment',
  )
  invariant(deployment.target === 'Compose', 'deployment.target must be Compose for v1.0.0.')
  for (const field of [
    'environment',
    'baseUrl',
    'evidenceRootUri',
    'namespace',
    'ingressHost',
    'ingressClass',
    'tlsSecret',
  ]) {
    invariant(hasOwn(deployment, field), `deployment.${field} must be present.`)
  }

  const tagPolicy = requireObject(spec.tagPolicy, 'tagPolicy')
  assertKnownKeys(
    tagPolicy,
    [
      'creator',
      'immutable',
      'nextVersionAfterFailure',
      'clientIdSecretReference',
      'privateKeySecretReference',
    ],
    'tagPolicy',
  )
  invariant(tagPolicy.creator === 'GitHubApp', 'tagPolicy.creator must be GitHubApp.')
  invariant(tagPolicy.immutable === true, 'tagPolicy.immutable must be true.')
  invariant(
    /^\d+\.\d+\.\d+$/.test(tagPolicy.nextVersionAfterFailure),
    'tagPolicy.nextVersionAfterFailure must use major.minor.patch.',
  )
  assertSecretReference(tagPolicy.clientIdSecretReference, 'tagPolicy.clientIdSecretReference')
  assertSecretReference(tagPolicy.privateKeySecretReference, 'tagPolicy.privateKeySecretReference')

  invariant(Array.isArray(spec.inputs), 'inputs must be an array.')
  invariant(spec.inputs.length === EXPECTED_INPUTS.length, `inputs must contain ${EXPECTED_INPUTS.length} entries.`)
  const ids = new Set()
  for (const input of spec.inputs) {
    requireObject(input, 'input')
    assertKnownKeys(
      input,
      [
        'id',
        'description',
        'requiredFor',
        'owner',
        'targetDate',
        'secretReference',
        'evidenceUri',
        'approver',
        'approvedAt',
        'expiresAt',
        'status',
      ],
      'input',
    )
    const id = requireText(input.id, 'input.id')
    invariant(EXPECTED_INPUTS.includes(id), `Unknown release input '${id}'.`)
    invariant(!ids.has(id), `Duplicate release input '${id}'.`)
    ids.add(id)
    requireText(input.description, `${id}.description`, { allowPlaceholder: true })
    invariant(Array.isArray(input.requiredFor) && input.requiredFor.length > 0, `${id}.requiredFor is required.`)
    input.requiredFor.forEach((phase) => invariant(ALLOWED_PHASES.has(phase), `${id} has invalid phase '${phase}'.`))
    for (const field of [
      'owner',
      'targetDate',
      'secretReference',
      'evidenceUri',
      'approver',
      'approvedAt',
      'expiresAt',
      'status',
    ]) {
      invariant(hasOwn(input, field), `${id}.${field} must be present.`)
    }
    invariant(ALLOWED_INPUT_STATES.has(input.status), `${id}.status is invalid.`)
    for (const dateField of ['targetDate', 'approvedAt', 'expiresAt']) {
      if (input[dateField] !== null && input[dateField] !== '') {
        requireIsoDate(input[dateField], `${id}.${dateField}`)
      }
    }
  }
  EXPECTED_INPUTS.forEach((id) => invariant(ids.has(id), `Missing release input '${id}'.`))
}

function validateFreeze(spec, now) {
  const release = spec.release
  invariant(release.state === 'Approved', 'release.state must be Approved.')
  requireText(release.changeTicket, 'release.changeTicket')
  const releaseApprovedAt = requireIsoDate(release.approvedAt, 'release.approvedAt', { past: true, now })
  requireIsoDate(release.approvalExpiresAt, 'release.approvalExpiresAt', { future: true, now })

  requireText(spec.environments.freeze, 'environments.freeze')
  requireText(spec.environments.candidate, 'environments.candidate')
  requireText(spec.environments.compose, 'environments.compose')
  requireText(spec.deployment.environment, 'deployment.environment')
  requireUri(spec.deployment.baseUrl, 'deployment.baseUrl', 'https')
  requireUri(spec.deployment.evidenceRootUri, 'deployment.evidenceRootUri', 's3')

  const required = spec.inputs.filter((input) =>
    input.requiredFor.some((phase) => phase === 'Candidate' || phase === 'Compose'),
  )
  invariant(required.length > 0, 'Candidate and Compose must have required inputs.')
  for (const input of required) {
    const prefix = input.id
    invariant(input.status === 'Approved', `${prefix}.status must be Approved.`)
    requireText(input.owner, `${prefix}.owner`)
    requireIsoDate(input.targetDate, `${prefix}.targetDate`, { now })
    assertSecretReference(input.secretReference, `${prefix}.secretReference`)
    requireUri(input.evidenceUri, `${prefix}.evidenceUri`, 's3')
    requireText(input.approver, `${prefix}.approver`)
    const inputApprovedAt = requireIsoDate(input.approvedAt, `${prefix}.approvedAt`, { past: true, now })
    invariant(inputApprovedAt <= releaseApprovedAt, `${prefix}.approvedAt must not be later than release.approvedAt.`)
    requireIsoDate(input.expiresAt, `${prefix}.expiresAt`, { future: true, now })
  }
}

export function validateSpec(raw, { mode = 'Structure', expectedVersion, now = new Date() } = {}) {
  invariant(Buffer.byteLength(raw, 'utf8') <= 1024 * 1024, 'Release spec must not exceed 1 MiB.')
  invariant(!/-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/.test(raw), 'Private key material is forbidden.')
  invariant(
    !/(?:Server|Data Source)\s*=[^;\r\n]+;[^\r\n]*(?:Password|Pwd)\s*=/i.test(raw),
    'Database connection strings are forbidden.',
  )
  const spec = YAML.parse(raw, { uniqueKeys: true })
  scanForSecrets(spec)
  validateStructure(spec, expectedVersion)
  if (mode === 'Freeze' || mode === 'VerifySnapshot') {
    validateFreeze(spec, now)
  }
  return spec
}

export function buildSnapshot({
  spec,
  raw,
  repositoryPath,
  gitSha,
  actor,
  runUri,
  generatedAt = new Date(),
}) {
  invariant(/^[A-Fa-f0-9]{40}$/.test(gitSha), 'gitSha must be a 40-character commit SHA.')
  requireUri(spec.deployment.evidenceRootUri, 'deployment.evidenceRootUri', 's3')
  return {
    schemaVersion: 1,
    status: 'Approved',
    releaseVersion: spec.release.version,
    tag: spec.release.tag,
    gitSha: gitSha.toLowerCase(),
    repositoryPath: repositoryPath.replaceAll('\\', '/'),
    specSha256: sha256(Buffer.from(raw, 'utf8')),
    changeTicket: spec.release.changeTicket,
    approvedAt: spec.release.approvedAt,
    approvalExpiresAt: spec.release.approvalExpiresAt,
    generatedAtUtc: generatedAt.toISOString(),
    generatedBy: actor,
    workflowRunUri: runUri,
    evidenceRootUri: spec.deployment.evidenceRootUri.replace(/\/+$/, ''),
    environments: spec.environments,
    runners: spec.runners,
    deployment: spec.deployment,
    inputs: spec.inputs.filter((input) =>
      input.requiredFor.some((phase) => phase === 'Candidate' || phase === 'Compose'),
    ),
  }
}

export function verifySnapshot({
  snapshot,
  rawSpec,
  spec,
  expectedVersion,
  expectedGitSha,
  expectedSnapshotSha256,
  rawSnapshot,
  now = new Date(),
}) {
  requireObject(snapshot, 'Freeze snapshot')
  invariant(snapshot.schemaVersion === 1, 'Freeze snapshot schemaVersion must be 1.')
  invariant(snapshot.status === 'Approved', 'Freeze snapshot status must be Approved.')
  invariant(snapshot.releaseVersion === expectedVersion, 'Freeze snapshot releaseVersion does not match.')
  invariant(snapshot.tag === `v${expectedVersion}`, 'Freeze snapshot tag does not match.')
  invariant(snapshot.gitSha === expectedGitSha.toLowerCase(), 'Freeze snapshot gitSha does not match.')
  invariant(snapshot.specSha256 === sha256(Buffer.from(rawSpec, 'utf8')), 'Freeze snapshot specSha256 does not match.')
  invariant(snapshot.changeTicket === spec.release.changeTicket, 'Freeze snapshot changeTicket does not match.')
  invariant(snapshot.approvalExpiresAt === spec.release.approvalExpiresAt, 'Freeze snapshot expiry does not match.')
  requireIsoDate(snapshot.approvalExpiresAt, 'Freeze snapshot approvalExpiresAt', { future: true, now })
  invariant(
    JSON.stringify(snapshot.deployment) === JSON.stringify(spec.deployment),
    'Freeze snapshot deployment does not match the release spec.',
  )
  if (expectedSnapshotSha256) {
    invariant(
      sha256(Buffer.from(rawSnapshot, 'utf8')) === expectedSnapshotSha256.toUpperCase(),
      'Freeze snapshot SHA-256 does not match.',
    )
  }
}

function parseArguments(argv) {
  const result = {}
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index]
    invariant(token.startsWith('--'), `Unexpected argument '${token}'.`)
    const key = token.slice(2)
    const value = argv[index + 1]
    invariant(value !== undefined && !value.startsWith('--'), `Argument --${key} requires a value.`)
    result[key] = value
    index += 1
  }
  return result
}

function main() {
  const args = parseArguments(process.argv.slice(2))
  const specPath = requireText(args.spec, '--spec')
  const mode = args.mode ?? 'Structure'
  invariant(['Structure', 'Freeze', 'VerifySnapshot'].includes(mode), '--mode is invalid.')
  const raw = readFileSync(specPath, 'utf8')
  const spec = validateSpec(raw, { mode, expectedVersion: args['expected-version'] })

  if (mode === 'Freeze') {
    const snapshot = buildSnapshot({
      spec,
      raw,
      repositoryPath: args['repository-path'] ?? specPath,
      gitSha: requireText(args['git-sha'], '--git-sha'),
      actor: requireText(args.actor, '--actor'),
      runUri: requireText(args['run-uri'], '--run-uri'),
    })
    const output = requireText(args['output-snapshot'], '--output-snapshot')
    writeFileSync(output, `${JSON.stringify(snapshot, null, 2)}\n`, 'utf8')
  } else if (mode === 'VerifySnapshot') {
    const snapshotPath = requireText(args.snapshot, '--snapshot')
    const rawSnapshot = readFileSync(snapshotPath, 'utf8')
    const snapshot = JSON.parse(rawSnapshot)
    verifySnapshot({
      snapshot,
      rawSpec: raw,
      spec,
      expectedVersion: requireText(args['expected-version'], '--expected-version'),
      expectedGitSha: requireText(args['expected-git-sha'], '--expected-git-sha'),
      expectedSnapshotSha256: args['expected-snapshot-sha256'],
      rawSnapshot,
    })
  }

  process.stdout.write(`R2 release readiness ${mode} gate passed for ${spec.release.version}.\n`)
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    main()
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`)
    process.exitCode = 1
  }
}
