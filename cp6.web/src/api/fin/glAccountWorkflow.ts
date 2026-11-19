import http from '@/api/http'

const owner = '/fin/account/owner-v1'
const headers = (context: string) => ({ 'X-Finance-Context': context })

export interface GlChoice {
  contextToken: string
  scope: { environmentId: string; tenantId: string; legalEntityKey: string; ledgerKey: string; mode: string }
  scheme: string
  baseCurrency: string
  businessDate: string
  scopeRevision: number
}

export type GlControl = 'SUSPENDED' | 'ENABLED' | 'RETIRED'
export type GlContent = {
  name: string
  type: number
  normalSide: number
  parentRootId: string | null
  postingClass: 'GROUP' | 'POSTABLE' | ''
  isControl: boolean
  subLedgerType: string | null
  requirePartner: boolean
  roleKey: string | null
  currency: { mode: 'BASE_ONLY' | 'SINGLE' | 'MULTI' | ''; codes: string[] } | null
  from: string
  toExclusive: string | null
}

type GlTimelineReference = { rootId: string; revision: number; releaseId: string; digest: string }

export interface GlHeads {
  controlRevision: number
  timelineRevision: number | null
  proposalId: string
  proposalRevision: number
}

interface GlVersion {
  id: string
  number: number
  content: GlContent
}

interface GlProposal {
  id: string
  state: string
  revision: number
  versionId: string
  activate: boolean
  decisionId: string | null
}

interface GlDecision {
  id: string
  kind: string
  reviewer: string
  reason: string
  basisKey: string
}

interface GlSegment {
  from: string
  toExclusive: string | null
  versionId: string
  releaseId: string
}

type GlChoiceRow = { id: string; code: string; name: string }

export interface GlSnapshot {
  scopeRevision: number
  businessDate: string
  baseCurrency: string
  roles: string[]
  currencies: string[]
  rows: { id: string; code: string | null; name: string | null; controlState: GlControl | null; proposalState: string | null; currentVersionId: string | null }[]
  parentChoices: GlChoiceRow[]
  canCreate: boolean
  canImport: boolean
  canReadDetails: boolean
  canReadHistory: boolean
}

export interface GlHistory {
  rootId: string
  code: string
  version: GlVersion
  proposal: GlProposal
  decision: GlDecision | null
}

export interface GlDetail extends GlHistory {
  scopeRevision: number
  heads: GlHeads
  controlState: GlControl
  timeline: { revision: number; releaseId: string; versionId: string; decisionId: string; segments: GlSegment[] } | null
  currentEffective: { segment: GlSegment; version: GlVersion; level: number } | null
  timelineReference: GlTimelineReference | null
  restoreDecision: GlDecision | null
  allowedActions: GlKind[]
  canReadHistory: boolean
}

export type GlKind = 'create' | 'revise' | 'successor' | 'submit' | 'withdraw' | 'abandon' | 'approve' | 'reject' |
  'publish' | 'suspend' | 'approve-restore' | 'restore' | 'retire' | 'import-template'

export interface GlCommand {
  operationRef: string
  kind: GlKind
  expectedScopeRevision: number
  rootId: string | null
  expectedHeads: GlHeads | null
  code: string | null
  content: GlContent | null
  activate: boolean | null
  previewId: string | null
  decisionId: string | null
  timelineSubject: GlTimelineReference | null
  reason: string
}

export interface GlReceipt {
  operationRef: string
  kind: string
  rootId: string | null
  rootIds: string[] | null
  scopeRevision: number
  versionId: string | null
  proposalId: string | null
  decisionId: string | null
  releaseId: string | null
  timelineReference: GlTimelineReference | null
  controlState: GlControl | null
  errorCode?: string
}

export interface GlPublishPreview {
  previewId: string
  scopeRevision: number
  heads: GlHeads
  before: GlSegment[]
  activate: boolean
  canApply: boolean
  issues: string[]
  segments: (Omit<GlSegment, 'releaseId'> & { releaseId: string | null })[]
  superseded: GlSegment[]
}

export interface GlTemplatePreview {
  previewId: string
  scopeRevision: number
  drafts: { rootId: string; code: string; content: GlContent }[]
}

export interface GlLegacyRow {
  id: string
  code: string
  name: string
  standardScheme: string
  isActive: boolean
  level: number
  currencyCd: string | null
}

export type GlHistoryRevision = { proposalId: string; revision: number; state: string }

export const glWorkflow = {
  options: () => http.get<unknown, { choices: GlChoice[] }>(owner + '/options'),
  roots: (context: string) => http.get<unknown, GlSnapshot>(owner + '/roots', { headers: headers(context) }),
  parents: (context: string, date: string) => http.get<unknown, GlChoiceRow[]>(owner + '/parent-options',
    { params: { date }, headers: headers(context) }),
  detail: (context: string, root: string) => http.get<unknown, GlDetail>(owner + '/roots/' + root + '/workflow',
    { headers: headers(context) }),
  revisions: (context: string, root: string) => http.get<unknown, GlHistoryRevision[]>(owner + '/roots/' + root + '/revisions',
    { headers: headers(context) }),
  history: (context: string, root: string, revision: GlHistoryRevision) => http.get<unknown, GlHistory>(
    owner + '/roots/' + root + '/history', { params: { proposalId: revision.proposalId, revision: revision.revision }, headers: headers(context) }),
  original: (context: string, key: string) => http.get<unknown, GlReceipt>(owner + '/commands/' + key, { headers: headers(context) }),
  execute: (context: string, command: GlCommand) => http.post<unknown, GlReceipt>(owner + '/commands', command,
    { headers: headers(context) }),
  publishPreview: (context: string, root: string, head: GlDetail) => http.post<unknown, GlPublishPreview>(
    owner + '/roots/' + root + '/publish-preview', { expectedScopeRevision: head.scopeRevision, expectedHeads: head.heads,
      activate: head.proposal.activate, replaceFrom: head.version.content.from, replaceToExclusive: head.version.content.toExclusive },
    { headers: headers(context) }),
  templatePreview: (context: string, expectedScopeRevision: number, from: string, toExclusive: string | null, legacyIds: string[] | null) =>
    http.post<unknown, GlTemplatePreview>(owner + '/templates/preview',
      { expectedScopeRevision, standardTemplate: legacyIds === null, from, toExclusive, legacyIds }, { headers: headers(context) }),
  legacy: (context: string) => http.get<unknown, { rows: GlLegacyRow[] }>(owner + '/templates/legacy', { headers: headers(context) }),
}
