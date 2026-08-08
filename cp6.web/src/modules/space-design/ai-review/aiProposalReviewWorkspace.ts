export type AiReviewConfidenceBand = 'High' | 'Medium' | 'Low'
export type AiReviewReadiness = 'Ready' | 'NeedsReview' | 'Blocked'
export type AiReviewDifferenceKind = 'Added' | 'Modified' | 'Unchanged'
export type AiReviewIssueSeverity = 'Info' | 'Warning' | 'Blocking'
export type AiReviewFusionSource =
  | 'TemplateDefault'
  | 'Ai'
  | 'DeterministicRule'
  | 'HumanLocked'

export interface AiReviewPoint {
  x: number
  y: number
  z: number
}

export interface AiReviewBounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

export interface AiReviewEvidence {
  source: AiReviewFusionSource
  valueToken: string
  confidence: number
  evidenceCodes: string[]
}

export interface AiReviewField {
  fieldPath: string
  valueToken: string
  winningSource: AiReviewFusionSource
  confidence: number
  evidence: AiReviewEvidence[]
}

export interface AiReviewIssue {
  code: string
  severity: AiReviewIssueSeverity
  sourceRef?: string
  sourceKey?: string
  fieldPath?: string
  detailToken?: string
}

export interface AiReviewFieldDifference {
  fieldPath: string
  kind: 'Added' | 'Removed' | 'Changed'
  beforeValueToken?: string
  afterValueToken?: string
  winningSource?: AiReviewFusionSource
  confidence?: number
  evidence: AiReviewEvidence[]
}

export interface AiReviewDifference {
  kind: AiReviewDifferenceKind
  geometryChanged: boolean
  beforeGeometrySha256?: string
  afterGeometrySha256: string
  beforeGeometryBounds?: AiReviewBounds
  afterGeometryBounds: AiReviewBounds
  fields: AiReviewFieldDifference[]
  beforeRackLevelCount: number
  afterRackLevelCount: number
  beforeLocationCount: number
  afterLocationCount: number
}

export interface AiReviewItem {
  reviewItemId: string
  logicalId: string
  sourceKey: string
  sourceRef: string
  objectType: string
  confidence: number
  confidenceBand: AiReviewConfidenceBand
  readiness: AiReviewReadiness
  hasBlockingIssue: boolean
  canBatchAccept: boolean
  location: {
    floorLogicalId: string
    sourceRef: string
    bounds: AiReviewBounds
    anchor: AiReviewPoint
    suggestedPaddingMillimeters: number
    canFocusCanvas: boolean
  }
  fields: AiReviewField[]
  relations: Array<{
    relationType: string
    targetLogicalId: string
    confidence: number
    evidenceCodes: string[]
  }>
  rackDerivation?: {
    profileVersionId: string
    profileSha256: string
    winningSource: string
    rackWidthMillimeters: number
    rackDepthMillimeters: number
    rackHeightMillimeters: number
    locationCount: number
    levels: Array<{ logicalId: string; levelNo: number; locationCount: number }>
  }
  issues: AiReviewIssue[]
  difference: AiReviewDifference
}

export interface AiReviewSummary {
  totalCount: number
  highConfidenceCount: number
  mediumConfidenceCount: number
  lowConfidenceCount: number
  readyCount: number
  needsReviewCount: number
  blockedCount: number
  batchAcceptEligibleCount: number
  addedCount: number
  modifiedCount: number
  unchangedCount: number
  locatableCount: number
  infoIssueCount: number
  warningIssueCount: number
  blockingIssueCount: number
  runIssueCount: number
  runBlockingIssueCount: number
}

export interface AiProposalReviewWorkspace {
  schemaVersion: number
  isReadOnlyWorkspace: boolean
  decisionWritten: boolean
  draftWritten: boolean
  tenantId: string
  modelVersionId: string
  floorLogicalId: string
  proposalSetSha256: string
  baselineSnapshotSha256: string
  baselineContentRevision: number
  baselineContentHash?: string
  runIssues: AiReviewIssue[]
  items: AiReviewItem[]
  summary: AiReviewSummary
  reviewEtag: string
  workspaceSha256: string
}

export interface AiReviewFilters {
  confidenceBand?: AiReviewConfidenceBand
  objectType?: string
  readiness?: AiReviewReadiness
  differenceKind?: AiReviewDifferenceKind
  issueSeverity?: AiReviewIssueSeverity
  winningSource?: AiReviewFusionSource
  search?: string
  onlyLocatable?: boolean
}

export interface AiReviewSceneIdentity {
  modelVersionId: string
  floorLogicalId: string
  contentRevision: number
  contentHash?: string | null
}

export interface AiReviewFreshness {
  fresh: boolean
  reasons: Array<'model' | 'floor' | 'revision' | 'contentHash'>
}

export interface AiReviewBatchPreview {
  selectedCount: number
  acceptEligibleIds: string[]
  acceptIneligibleIds: string[]
  rejectEligibleIds: string[]
  requiresServerRevalidation: true
  decisionWritten: false
  draftWritten: false
}

const bands = new Set<AiReviewConfidenceBand>(['High', 'Medium', 'Low'])
const readinesses = new Set<AiReviewReadiness>(['Ready', 'NeedsReview', 'Blocked'])
const differenceKinds = new Set<AiReviewDifferenceKind>(['Added', 'Modified', 'Unchanged'])
const severities = new Set<AiReviewIssueSeverity>(['Info', 'Warning', 'Blocking'])
const sources = new Set<AiReviewFusionSource>([
  'TemplateDefault', 'Ai', 'DeterministicRule', 'HumanLocked',
])
const sha256 = /^[0-9a-f]{64}$/
const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i
const emptyUuid = '00000000-0000-0000-0000-000000000000'

export function parseAiProposalReviewWorkspace(
  input: string | unknown,
): AiProposalReviewWorkspace {
  const value = typeof input === 'string' ? JSON.parse(input) : input
  if (!isRecord(value)
    || value.schemaVersion !== 1
    || value.isReadOnlyWorkspace !== true
    || value.decisionWritten !== false
    || value.draftWritten !== false
    || !isUuid(value.tenantId)
    || !isUuid(value.modelVersionId)
    || !isUuid(value.floorLogicalId)
    || !isSha(value.proposalSetSha256)
    || !isSha(value.baselineSnapshotSha256)
    || !isNonNegativeInteger(value.baselineContentRevision)
    || value.baselineContentHash !== undefined && !isSha(value.baselineContentHash)
    || !isSha(value.reviewEtag)
    || !isSha(value.workspaceSha256)
    || !Array.isArray(value.items) || value.items.length > 100_000
    || !Array.isArray(value.runIssues)
    || !isRecord(value.summary)) {
    throw new Error('AI proposal review workspace identity is invalid')
  }
  const items = value.items.map(assertItem)
  if (new Set(items.map(item => item.reviewItemId)).size !== items.length
    || new Set(items.map(item => item.logicalId)).size !== items.length) {
    throw new Error('AI proposal review item identities are not unique')
  }
  const runIssues = value.runIssues.map(assertIssue)
  const summary = summarizeAiReviewItems(items, runIssues)
  for (const [key, expected] of Object.entries(summary)) {
    if (value.summary[key] !== expected) {
      throw new Error(`AI proposal review summary.${key} is inconsistent`)
    }
  }
  return value as unknown as AiProposalReviewWorkspace
}

export function aiReviewFreshness(
  workspace: AiProposalReviewWorkspace,
  scene: AiReviewSceneIdentity,
): AiReviewFreshness {
  const reasons: AiReviewFreshness['reasons'] = []
  if (workspace.modelVersionId.toLowerCase() !== scene.modelVersionId.toLowerCase()) reasons.push('model')
  if (workspace.floorLogicalId.toLowerCase() !== scene.floorLogicalId.toLowerCase()) reasons.push('floor')
  if (workspace.baselineContentRevision !== scene.contentRevision) reasons.push('revision')
  if (workspace.baselineContentHash
    && workspace.baselineContentHash !== scene.contentHash) reasons.push('contentHash')
  return { fresh: reasons.length === 0, reasons }
}

export function filterAiReviewItems(
  workspace: AiProposalReviewWorkspace,
  filters: AiReviewFilters,
): AiReviewItem[] {
  const search = filters.search?.trim().toLocaleLowerCase()
  return workspace.items.filter(item =>
    (!filters.confidenceBand || item.confidenceBand === filters.confidenceBand)
    && (!filters.objectType || item.objectType === filters.objectType)
    && (!filters.readiness || item.readiness === filters.readiness)
    && (!filters.differenceKind || item.difference.kind === filters.differenceKind)
    && (!filters.issueSeverity || item.issues.some(issue => issue.severity === filters.issueSeverity))
    && (!filters.winningSource || item.fields.some(field => field.winningSource === filters.winningSource))
    && (!filters.onlyLocatable || item.location.canFocusCanvas)
    && (!search || searchableValues(item).some(value => value.toLocaleLowerCase().includes(search))),
  )
}

export function previewAiReviewBatch(
  workspace: AiProposalReviewWorkspace,
  selectedIds: readonly string[],
): AiReviewBatchPreview {
  const unique = new Set(selectedIds)
  if (unique.size !== selectedIds.length || unique.size > 1_000) {
    throw new Error('AI review batch selection must be unique and contain at most 1,000 items')
  }
  const selected = workspace.items.filter(item => unique.has(item.reviewItemId))
  if (selected.length !== unique.size) throw new Error('AI review batch selection contains an unknown item')
  return {
    selectedCount: selected.length,
    acceptEligibleIds: selected.filter(item => item.canBatchAccept).map(item => item.reviewItemId),
    acceptIneligibleIds: selected.filter(item => !item.canBatchAccept).map(item => item.reviewItemId),
    rejectEligibleIds: selected.map(item => item.reviewItemId),
    requiresServerRevalidation: true,
    decisionWritten: false,
    draftWritten: false,
  }
}

export function summarizeAiReviewItems(
  items: readonly AiReviewItem[],
  runIssues: readonly AiReviewIssue[],
): AiReviewSummary {
  const issues = items.flatMap(item => item.issues).concat(runIssues)
  return {
    totalCount: items.length,
    highConfidenceCount: items.filter(item => item.confidenceBand === 'High').length,
    mediumConfidenceCount: items.filter(item => item.confidenceBand === 'Medium').length,
    lowConfidenceCount: items.filter(item => item.confidenceBand === 'Low').length,
    readyCount: items.filter(item => item.readiness === 'Ready').length,
    needsReviewCount: items.filter(item => item.readiness === 'NeedsReview').length,
    blockedCount: items.filter(item => item.readiness === 'Blocked').length,
    batchAcceptEligibleCount: items.filter(item => item.canBatchAccept).length,
    addedCount: items.filter(item => item.difference.kind === 'Added').length,
    modifiedCount: items.filter(item => item.difference.kind === 'Modified').length,
    unchangedCount: items.filter(item => item.difference.kind === 'Unchanged').length,
    locatableCount: items.filter(item => item.location.canFocusCanvas).length,
    infoIssueCount: issues.filter(issue => issue.severity === 'Info').length,
    warningIssueCount: issues.filter(issue => issue.severity === 'Warning').length,
    blockingIssueCount: issues.filter(issue => issue.severity === 'Blocking').length,
    runIssueCount: runIssues.length,
    runBlockingIssueCount: runIssues.filter(issue => issue.severity === 'Blocking').length,
  }
}

function assertItem(value: unknown): AiReviewItem {
  if (!isRecord(value)
    || typeof value.reviewItemId !== 'string' || !value.reviewItemId.startsWith('ai-review-')
    || !isUuid(value.logicalId)
    || typeof value.sourceKey !== 'string' || !value.sourceKey
    || typeof value.sourceRef !== 'string' || !value.sourceRef
    || typeof value.objectType !== 'string' || !value.objectType
    || typeof value.confidence !== 'number' || value.confidence < 0 || value.confidence > 1
    || !bands.has(value.confidenceBand as AiReviewConfidenceBand)
    || !readinesses.has(value.readiness as AiReviewReadiness)
    || typeof value.hasBlockingIssue !== 'boolean'
    || typeof value.canBatchAccept !== 'boolean'
    || !Array.isArray(value.fields) || !Array.isArray(value.relations)
    || !Array.isArray(value.issues) || !isRecord(value.location)
    || !isRecord(value.difference)) {
    throw new Error('AI proposal review item is invalid')
  }
  const location = value.location
  assertBounds(location.bounds)
  if (!isUuid(location.floorLogicalId)
    || location.sourceRef !== value.sourceRef
    || !isPoint(location.anchor)
    || !isNonNegativeInteger(location.suggestedPaddingMillimeters)
    || location.canFocusCanvas !== true) {
    throw new Error('AI proposal review location is invalid')
  }
  value.fields.forEach(assertField)
  value.issues.forEach(assertIssue)
  assertDifference(value.difference)
  const blocking = value.issues.some(issue =>
    isRecord(issue) && issue.severity === 'Blocking')
  if (blocking !== value.hasBlockingIssue
    || (value.readiness === 'Blocked') !== blocking
    || value.canBatchAccept && (value.readiness !== 'Ready' || value.confidenceBand !== 'High')) {
    throw new Error('AI proposal review readiness is inconsistent')
  }
  return value as unknown as AiReviewItem
}

function assertField(value: unknown): void {
  if (!isRecord(value)
    || typeof value.fieldPath !== 'string' || !value.fieldPath
    || typeof value.valueToken !== 'string' || !value.valueToken
    || !sources.has(value.winningSource as AiReviewFusionSource)
    || typeof value.confidence !== 'number'
    || !Array.isArray(value.evidence)) throw new Error('AI proposal field evidence is invalid')
}

function assertIssue(value: unknown): AiReviewIssue {
  if (!isRecord(value)
    || typeof value.code !== 'string' || !value.code
    || !severities.has(value.severity as AiReviewIssueSeverity)) {
    throw new Error('AI proposal review issue is invalid')
  }
  return value as unknown as AiReviewIssue
}

function assertDifference(value: Record<string, unknown>): void {
  if (!differenceKinds.has(value.kind as AiReviewDifferenceKind)
    || typeof value.geometryChanged !== 'boolean'
    || !isSha(value.afterGeometrySha256)
    || value.beforeGeometrySha256 !== undefined && !isSha(value.beforeGeometrySha256)
    || !Array.isArray(value.fields)
    || !isNonNegativeInteger(value.beforeRackLevelCount)
    || !isNonNegativeInteger(value.afterRackLevelCount)
    || !isNonNegativeInteger(value.beforeLocationCount)
    || !isNonNegativeInteger(value.afterLocationCount)) {
    throw new Error('AI proposal review difference is invalid')
  }
  assertBounds(value.afterGeometryBounds)
  if (value.beforeGeometryBounds !== undefined) assertBounds(value.beforeGeometryBounds)
}

function assertBounds(value: unknown): asserts value is AiReviewBounds {
  if (!isRecord(value)
    || !isFiniteNumber(value.minX) || !isFiniteNumber(value.minY)
    || !isFiniteNumber(value.maxX) || !isFiniteNumber(value.maxY)
    || value.minX > value.maxX || value.minY > value.maxY) {
    throw new Error('AI proposal review bounds are invalid')
  }
}

function searchableValues(item: AiReviewItem): string[] {
  return [
    item.reviewItemId,
    item.logicalId,
    item.sourceKey,
    item.sourceRef,
    item.objectType,
    ...item.fields.flatMap(field => [field.fieldPath, field.valueToken]),
    ...item.fields.flatMap(field => field.evidence.flatMap(evidence => evidence.evidenceCodes)),
    ...item.relations.flatMap(relation => relation.evidenceCodes),
    ...item.issues.flatMap(issue => [issue.code, issue.detailToken ?? '']),
  ]
}

function isRecord(value: unknown): value is Record<string, any> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string'
    && uuid.test(value)
    && value.toLowerCase() !== emptyUuid
}

function isSha(value: unknown): value is string {
  return typeof value === 'string' && sha256.test(value)
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value)
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) >= 0
}

function isPoint(value: unknown): value is AiReviewPoint {
  return isRecord(value)
    && isFiniteNumber(value.x) && isFiniteNumber(value.y) && isFiniteNumber(value.z)
}
