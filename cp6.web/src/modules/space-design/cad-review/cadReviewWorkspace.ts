export type CadReviewItemKind =
  | 'MappingDiagnostic'
  | 'SemanticDiagnostic'
  | 'LowConfidenceProposal'
  | 'RejectedProposal'
  | 'ExcelUnmatched'
  | 'ExcelConflict'
  | 'ExcelError'

export type CadReviewSeverity = 'Info' | 'Warning' | 'Blocking'
export type CadReviewItemStatus = 'Open' | 'Resolved'
export type CadConfidenceBand = 'High' | 'Review' | 'Low' | 'Rejected'
export type CadReviewLocationKind = 'Document' | 'Layer' | 'Block' | 'Entity'

export interface CadReviewPoint {
  x: number
  y: number
  z: number
}

export interface CadReviewBounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

export interface CadReviewLocation {
  kind: CadReviewLocationKind
  floorLogicalId: string
  layerId?: string
  blockName?: string
  sourceRef?: string
  previewObjectId?: string
  bounds?: CadReviewBounds
  anchor?: CadReviewPoint
  suggestedPaddingMillimeters: number
  canFocusCanvas: boolean
}

export interface CadReviewItem {
  reviewItemId: string
  trackingKey: string
  kind: CadReviewItemKind
  severity: CadReviewSeverity
  status: CadReviewItemStatus
  code: string
  relatedCodes: string[]
  detailToken?: string
  suggestedActionCode: string
  sourceRef?: string
  previewObjectId?: string
  targetLogicalId?: string
  rackCode?: string
  confidenceBand?: CadConfidenceBand
  location: CadReviewLocation
  upstreamEvidenceSha256: string
  resolvedFromWorkspaceSha256?: string
}

export interface CadReviewWorkspaceSummary {
  totalCount: number
  openCount: number
  resolvedCount: number
  openInfoCount: number
  openWarningCount: number
  openBlockingCount: number
  locatableCount: number
  unlocatableCount: number
  cadDiagnosticCount: number
  proposalReviewCount: number
  excelReviewCount: number
}

export type CadChangeKind =
  | 'Add'
  | 'Modify'
  | 'Delete'
  | 'Conflict'
  | 'LowConfidence'
  | 'Unrecognized'

export interface CadReviewChange {
  changeId: string
  kind: CadChangeKind
  logicalId: string
  sourceRef: string
  previewObjectId?: string
  objectType: string
  confidence?: number
  isSelected: boolean
  canApply: boolean
  blockingReasonCode?: string
  beforeBounds?: CadReviewBounds
  afterBounds?: CadReviewBounds
}

export interface CadReviewChangeSummary {
  totalCount: number
  addCount: number
  modifyCount: number
  deleteCount: number
  conflictCount: number
  lowConfidenceCount: number
  unrecognizedCount: number
  selectedCount: number
  applyEligibleCount: number
}

export interface CadReviewWorkspace {
  schemaVersion: 1
  isReadOnlyWorkspace: true
  tenantId: string
  modelVersionId: string
  floorLogicalId: string
  floorCode: string
  diagnosticIndexSha256: string
  matchPreviewSha256?: string
  editorContentRevision: number
  editorContentHash?: string
  editorSnapshotSha256: string
  previousWorkspaceSha256?: string
  items: CadReviewItem[]
  summary: CadReviewWorkspaceSummary
  workspaceSha256: string
  sourceId?: string
  cadParseJobId?: string
  semanticPreviewSha256?: string
  changes?: CadReviewChange[]
  changeSummary?: CadReviewChangeSummary
  changesetSha256?: string
}

export interface CadReviewFilters {
  status?: CadReviewItemStatus
  severity?: CadReviewSeverity
  kind?: CadReviewItemKind
  search?: string
  onlyLocatable?: boolean
}

export interface CadReviewSceneIdentity {
  modelVersionId: string
  floorLogicalId: string
  contentRevision: number
  contentHash?: string | null
}

export interface CadReviewFreshness {
  fresh: boolean
  reasons: Array<'model' | 'floor' | 'revision' | 'contentHash'>
}

export interface CadReviewSceneObject {
  revision?: {
    logicalId?: string
    sourceRef?: string | null
  } | null
}

export interface CadReviewCanvasObjectRef {
  logicalId: string
  ownerKind: 'Element' | 'Rack'
}

const sha256Pattern = /^[0-9a-f]{64}$/
const guidPattern = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i
const emptyGuid = '00000000-0000-0000-0000-000000000000'
const itemKinds = new Set<CadReviewItemKind>([
  'MappingDiagnostic',
  'SemanticDiagnostic',
  'LowConfidenceProposal',
  'RejectedProposal',
  'ExcelUnmatched',
  'ExcelConflict',
  'ExcelError',
])
const severities = new Set<CadReviewSeverity>(['Info', 'Warning', 'Blocking'])
const statuses = new Set<CadReviewItemStatus>(['Open', 'Resolved'])
const confidenceBands = new Set<CadConfidenceBand>([
  'High',
  'Review',
  'Low',
  'Rejected',
])
const locationKinds = new Set<CadReviewLocationKind>([
  'Document',
  'Layer',
  'Block',
  'Entity',
])

export function parseCadReviewWorkspace(input: string | unknown): CadReviewWorkspace {
  const value = typeof input === 'string' ? JSON.parse(input) : input
  assertRecord(value, 'workspace')
  if (value.schemaVersion !== 1 || value.isReadOnlyWorkspace !== true) {
    throw new Error('CAD review workspace schema or read-only marker is invalid')
  }
  requireGuid(value.tenantId, 'tenantId')
  requireGuid(value.modelVersionId, 'modelVersionId')
  requireGuid(value.floorLogicalId, 'floorLogicalId')
  requireText(value.floorCode, 'floorCode', 128)
  requireSha(value.diagnosticIndexSha256, 'diagnosticIndexSha256')
  optionalSha(value.matchPreviewSha256, 'matchPreviewSha256')
  requireInteger(value.editorContentRevision, 'editorContentRevision', 0)
  optionalSha(value.editorContentHash, 'editorContentHash')
  requireSha(value.editorSnapshotSha256, 'editorSnapshotSha256')
  optionalSha(value.previousWorkspaceSha256, 'previousWorkspaceSha256')
  requireSha(value.workspaceSha256, 'workspaceSha256')
  optionalGuid(value.sourceId, 'sourceId')
  optionalGuid(value.cadParseJobId, 'cadParseJobId')
  optionalSha(value.semanticPreviewSha256, 'semanticPreviewSha256')
  optionalSha(value.changesetSha256, 'changesetSha256')
  if (!Array.isArray(value.items) || value.items.length > 100_000) {
    throw new Error('CAD review workspace items are invalid or too large')
  }

  const ids = new Set<string>()
  const trackingKeys = new Set<string>()
  const items = value.items.map((candidate, index) => {
    const item = parseItem(candidate, String(value.floorLogicalId), index)
    if (ids.has(item.reviewItemId) || trackingKeys.has(item.trackingKey)) {
      throw new Error('CAD review workspace contains duplicate item identity')
    }
    ids.add(item.reviewItemId)
    trackingKeys.add(item.trackingKey)
    return item
  })
  const expectedSummary = summarizeCadReviewItems(items)
  assertSummary(value.summary, expectedSummary)
  parseChanges(value)
  return value as unknown as CadReviewWorkspace
}

function parseChanges(value: Record<string, unknown>): void {
  if (value.changes === undefined) return
  if (!Array.isArray(value.changes) || value.changes.length > 100_000) {
    throw new Error('CAD review changeset is invalid or too large')
  }
  requireGuid(value.sourceId, 'sourceId')
  requireGuid(value.cadParseJobId, 'cadParseJobId')
  requireSha(value.semanticPreviewSha256, 'semanticPreviewSha256')
  requireSha(value.changesetSha256, 'changesetSha256')
  const ids = new Set<string>()
  for (const [index, candidate] of value.changes.entries()) {
    assertRecord(candidate, `changes[${index}]`)
    requireText(candidate.changeId, `changes[${index}].changeId`, 128)
    if (ids.has(String(candidate.changeId))) throw new Error('Duplicate CAD change identity')
    ids.add(String(candidate.changeId))
    if (!['Add', 'Modify', 'Delete', 'Conflict', 'LowConfidence', 'Unrecognized']
      .includes(String(candidate.kind))) throw new Error(`changes[${index}].kind is invalid`)
    requireGuid(candidate.logicalId, `changes[${index}].logicalId`)
    requireText(candidate.sourceRef, `changes[${index}].sourceRef`, 200)
    requireText(candidate.objectType, `changes[${index}].objectType`, 128)
    if (typeof candidate.isSelected !== 'boolean' || typeof candidate.canApply !== 'boolean') {
      throw new Error(`changes[${index}] selection state is invalid`)
    }
    if (candidate.confidence !== undefined &&
      (typeof candidate.confidence !== 'number' || candidate.confidence < 0 || candidate.confidence > 1)) {
      throw new Error(`changes[${index}].confidence is invalid`)
    }
    optionalText(candidate.previewObjectId, `changes[${index}].previewObjectId`, 128)
    optionalText(candidate.blockingReasonCode, `changes[${index}].blockingReasonCode`, 128)
    if (candidate.beforeBounds !== undefined) parseBounds(candidate.beforeBounds, index)
    if (candidate.afterBounds !== undefined) parseBounds(candidate.afterBounds, index)
  }
}

export function filterCadReviewItems(
  workspace: CadReviewWorkspace,
  filters: CadReviewFilters,
): CadReviewItem[] {
  const search = filters.search?.trim().toLocaleLowerCase()
  return workspace.items.filter((item) =>
    (!filters.status || item.status === filters.status)
    && (!filters.severity || item.severity === filters.severity)
    && (!filters.kind || item.kind === filters.kind)
    && (!filters.onlyLocatable || item.location.canFocusCanvas)
    && (!search || searchableValues(item).some((value) =>
      value.toLocaleLowerCase().includes(search)))
  )
}

export function cadReviewFreshness(
  workspace: CadReviewWorkspace,
  scene: CadReviewSceneIdentity,
): CadReviewFreshness {
  const reasons: CadReviewFreshness['reasons'] = []
  if (workspace.modelVersionId.toLowerCase() !== scene.modelVersionId.toLowerCase()) {
    reasons.push('model')
  }
  if (workspace.floorLogicalId.toLowerCase() !== scene.floorLogicalId.toLowerCase()) {
    reasons.push('floor')
  }
  if (workspace.editorContentRevision !== scene.contentRevision) {
    reasons.push('revision')
  }
  if (
    workspace.editorContentHash
    && workspace.editorContentHash !== scene.contentHash
  ) {
    reasons.push('contentHash')
  }
  return { fresh: reasons.length === 0, reasons }
}

export function resolveCadReviewCanvasObject(
  item: CadReviewItem,
  racks: readonly CadReviewSceneObject[],
  elements: readonly CadReviewSceneObject[],
): CadReviewCanvasObjectRef | null {
  const logicalId = item.targetLogicalId?.toLowerCase()
  const sourceRef = item.sourceRef
  const matches = (candidate: CadReviewSceneObject) =>
    Boolean(
      logicalId
      && candidate.revision?.logicalId?.toLowerCase() === logicalId,
    ) || Boolean(sourceRef && candidate.revision?.sourceRef === sourceRef)
  const rack = racks.find(matches)
  if (rack?.revision?.logicalId) {
    return { logicalId: rack.revision.logicalId, ownerKind: 'Rack' }
  }
  const element = elements.find(matches)
  return element?.revision?.logicalId
    ? { logicalId: element.revision.logicalId, ownerKind: 'Element' }
    : null
}

export function summarizeCadReviewItems(
  items: readonly CadReviewItem[],
): CadReviewWorkspaceSummary {
  const open = items.filter((item) => item.status === 'Open')
  return {
    totalCount: items.length,
    openCount: open.length,
    resolvedCount: items.length - open.length,
    openInfoCount: open.filter((item) => item.severity === 'Info').length,
    openWarningCount: open.filter((item) => item.severity === 'Warning').length,
    openBlockingCount: open.filter((item) => item.severity === 'Blocking').length,
    locatableCount: items.filter((item) => item.location.canFocusCanvas).length,
    unlocatableCount: items.filter((item) => !item.location.canFocusCanvas).length,
    cadDiagnosticCount: items.filter((item) =>
      item.kind === 'MappingDiagnostic' || item.kind === 'SemanticDiagnostic').length,
    proposalReviewCount: items.filter((item) =>
      item.kind === 'LowConfidenceProposal' || item.kind === 'RejectedProposal').length,
    excelReviewCount: items.filter((item) => item.kind.startsWith('Excel')).length,
  }
}

function parseItem(
  value: unknown,
  floorLogicalId: string,
  index: number,
): CadReviewItem {
  assertRecord(value, `items[${index}]`)
  requireText(value.reviewItemId, `items[${index}].reviewItemId`, 128)
  requireText(value.trackingKey, `items[${index}].trackingKey`, 512)
  if (!itemKinds.has(value.kind as CadReviewItemKind)) {
    throw new Error(`items[${index}].kind is invalid`)
  }
  if (!severities.has(value.severity as CadReviewSeverity)) {
    throw new Error(`items[${index}].severity is invalid`)
  }
  if (!statuses.has(value.status as CadReviewItemStatus)) {
    throw new Error(`items[${index}].status is invalid`)
  }
  requireText(value.code, `items[${index}].code`, 128)
  requireText(value.suggestedActionCode, `items[${index}].suggestedActionCode`, 128)
  optionalText(value.detailToken, `items[${index}].detailToken`, 512)
  optionalText(value.sourceRef, `items[${index}].sourceRef`, 200)
  optionalText(value.previewObjectId, `items[${index}].previewObjectId`, 128)
  if (value.targetLogicalId !== undefined) {
    requireGuid(value.targetLogicalId, `items[${index}].targetLogicalId`)
  }
  optionalText(value.rackCode, `items[${index}].rackCode`, 128)
  if (
    value.confidenceBand !== undefined
    && !confidenceBands.has(value.confidenceBand as CadConfidenceBand)
  ) {
    throw new Error(`items[${index}].confidenceBand is invalid`)
  }
  if (!Array.isArray(value.relatedCodes)) {
    throw new Error(`items[${index}].relatedCodes is invalid`)
  }
  for (const [relatedIndex, code] of value.relatedCodes.entries()) {
    requireText(code, `items[${index}].relatedCodes[${relatedIndex}]`, 128)
  }
  requireSha(value.upstreamEvidenceSha256, `items[${index}].upstreamEvidenceSha256`)
  const resolved = value.status === 'Resolved'
  if (resolved) {
    requireSha(
      value.resolvedFromWorkspaceSha256,
      `items[${index}].resolvedFromWorkspaceSha256`,
    )
  } else if (value.resolvedFromWorkspaceSha256 !== undefined) {
    throw new Error(`items[${index}] open item cannot carry resolution evidence`)
  }
  parseLocation(value.location, floorLogicalId, index)
  return value as unknown as CadReviewItem
}

function parseLocation(value: unknown, floorLogicalId: string, index: number): void {
  assertRecord(value, `items[${index}].location`)
  if (!locationKinds.has(value.kind as CadReviewLocationKind)) {
    throw new Error(`items[${index}].location.kind is invalid`)
  }
  requireGuid(value.floorLogicalId, `items[${index}].location.floorLogicalId`)
  if (String(value.floorLogicalId).toLowerCase() !== floorLogicalId.toLowerCase()) {
    throw new Error(`items[${index}].location is on another floor`)
  }
  optionalText(value.layerId, `items[${index}].location.layerId`, 128)
  optionalText(value.blockName, `items[${index}].location.blockName`, 128)
  optionalText(value.sourceRef, `items[${index}].location.sourceRef`, 200)
  optionalText(value.previewObjectId, `items[${index}].location.previewObjectId`, 128)
  requireInteger(
    value.suggestedPaddingMillimeters,
    `items[${index}].location.suggestedPaddingMillimeters`,
    0,
  )
  if (typeof value.canFocusCanvas !== 'boolean') {
    throw new Error(`items[${index}].location.canFocusCanvas is invalid`)
  }
  if (value.bounds !== undefined) parseBounds(value.bounds, index)
  if (value.anchor !== undefined) parsePoint(value.anchor, index)
  if (value.canFocusCanvas && value.bounds === undefined && value.anchor === undefined) {
    throw new Error(`items[${index}] locatable item has no geometry`)
  }
}

function parseBounds(value: unknown, index: number): void {
  assertRecord(value, `items[${index}].location.bounds`)
  for (const key of ['minX', 'minY', 'maxX', 'maxY'] as const) {
    requireInteger(value[key], `items[${index}].location.bounds.${key}`)
  }
  if (Number(value.minX) > Number(value.maxX) || Number(value.minY) > Number(value.maxY)) {
    throw new Error(`items[${index}].location.bounds is inverted`)
  }
}

function parsePoint(value: unknown, index: number): void {
  assertRecord(value, `items[${index}].location.anchor`)
  for (const key of ['x', 'y', 'z'] as const) {
    requireInteger(value[key], `items[${index}].location.anchor.${key}`)
  }
}

function assertSummary(value: unknown, expected: CadReviewWorkspaceSummary): void {
  assertRecord(value, 'summary')
  for (const [key, count] of Object.entries(expected)) {
    if (value[key] !== count) {
      throw new Error(`CAD review workspace summary.${key} is inconsistent`)
    }
  }
}

function searchableValues(item: CadReviewItem): string[] {
  return [
    item.code,
    item.sourceRef,
    item.previewObjectId,
    item.rackCode,
    item.detailToken,
    ...item.relatedCodes,
  ].filter((value): value is string => Boolean(value))
}

function assertRecord(
  value: unknown,
  name: string,
): asserts value is Record<string, unknown> {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error(`${name} must be an object`)
  }
}

function requireText(value: unknown, name: string, maximumLength: number): void {
  if (
    typeof value !== 'string'
    || value.length === 0
    || value.length > maximumLength
    || value.trim() !== value
  ) {
    throw new Error(`${name} is invalid`)
  }
}

function optionalText(value: unknown, name: string, maximumLength: number): void {
  if (value !== undefined) requireText(value, name, maximumLength)
}

function requireSha(value: unknown, name: string): void {
  if (typeof value !== 'string' || !sha256Pattern.test(value)) {
    throw new Error(`${name} is not a lowercase SHA-256`)
  }
}

function optionalSha(value: unknown, name: string): void {
  if (value !== undefined) requireSha(value, name)
}

function requireGuid(value: unknown, name: string): void {
  if (
    typeof value !== 'string'
    || !guidPattern.test(value)
    || value.toLowerCase() === emptyGuid
  ) {
    throw new Error(`${name} is not a GUID`)
  }
}

function optionalGuid(value: unknown, name: string): void {
  if (value !== undefined) requireGuid(value, name)
}

function requireInteger(value: unknown, name: string, minimum?: number): void {
  if (
    typeof value !== 'number'
    || !Number.isSafeInteger(value)
    || (minimum !== undefined && value < minimum)
  ) {
    throw new Error(`${name} is not a bounded integer`)
  }
}
