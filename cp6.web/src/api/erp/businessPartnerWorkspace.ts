import { isAxiosError } from 'axios'
import http from '../http'

export type ProfileValue = string | number | boolean | null
export type Registration = 'PRE_REGISTERED' | 'REGISTERED' | 'UNKNOWN_LEGACY'
export type PartnerStateAction = 'REGISTER' | 'FREEZE' | 'UNFREEZE' | 'RETIRE' | 'RESTORE'
export interface WorkspaceScope { legalEntityKey: string; name: string }
export interface PartnerRef { bpId: string; bpCd: string; legalEntityKey: string | null; revision: string; contentHash: string | null }
export interface PartnerState { registration: Registration; lifecycle: 'ACTIVE' | 'RETIRED'; hold: 'CLEAR' | 'FROZEN'; revision: string; legacyScopeState: 'ADOPTED' | 'UNPROVEN' }
export interface PartnerListItem { bp: PartnerRef; name: string; baseCd: string; state: PartnerState }
export interface FieldRule {
  field: string; type: 'BOOL' | 'INT' | 'DECIMAL' | 'STRING' | 'TIME'; nullable: boolean
  maxLength: number | null; sourceOwner: string | null; availability: string
  options: { code: string; label: string; enabled: boolean }[]
}
export interface WorkspaceOptions {
  legalEntityKey: string; capabilities: string[]; roles: string[]
  bases: { baseCd: string; name: string; canCreate: boolean; createModes: Registration[] }[]
  fieldRules: FieldRule[]; writeFields: string[]
}
export interface PartnerIssue { field: string; code: string; requiredRole: string | null }
export interface PartnerDetail extends PartnerListItem {
  abbreviation: string | null; commercialCurrency: string | null; roles: Record<string, boolean>
  profile: Record<string, ProfileValue>; redactedFields: string[]; issues: PartnerIssue[]
  relations: { slot: string; target: PartnerRef | null; availability: string }[]
  finance: { kind: string; availability: string }[]
  legacyFields: { field: string; value: ProfileValue; sourceRevision: string; state: string }[]
}
export interface EditContext { detail: PartnerDetail; options: WorkspaceOptions; editableFields: string[] }
export interface StatusContext { bp: PartnerRef; state: PartnerState; allowedActions: PartnerStateAction[] }
export interface CandidateRef { bpId: string; revision: string; candidateHash: string }
export interface PreviewRef { previewId: string; intentHash: string; expiresAt: string }
export interface WorkspacePreview {
  reference: PreviewRef; canApply: boolean; issues: PartnerIssue[]
  candidates: { visibility: string; items: { reference: CandidateRef; bpCd: string; displayName: string; matchReasons: string[] }[] }
}
export type MappingAction = 'LINK' | 'CREATE_AND_LINK' | 'REPLACE' | 'UNLINK' | 'CONFIRM' | 'CORRECT_CURRENT'
export interface CandidateCriteria { bpCd: string | null; bpName: string; stdCoCd: string | null; einType: string | null; ein: string | null }
export interface AccountProbe {
  availability: string
  observation: { observationId: string; accountCode: string; accountStatus: string; sourceAsOf: string } | null
}
export interface MappingHead { accountId: string; legalEntityKey: string; state: string; mappingRevision: string | null; bp: PartnerRef | null }
export interface MappingRef { accountId: string; legalEntityKey: string; state: string; mappingRevision: string; businessPartnerKey: string | null }
export interface MappingSourceRef { owner: string; id: string; version: string; digest: string; purpose: string }
export interface MappingContext {
  reference: { contextId: string; contextHash: string; expiresAt: string }
  observation: AccountProbe['observation']; head: MappingHead; allowedActions: MappingAction[]
  candidates: { visibility: string; setHash: string; items: {
    reference: CandidateRef; bpCd: string; displayName: string; availability: string; registration: string; hold: string
  }[] }
}
export interface MappingRead {
  head: MappingHead; history: MappingRef[]
  deliveries: { eventId: string; state: string; mapping: MappingRef }[]
  projection: { knowledge: string; observedAt: string | null }
}
export interface MappingCorrection { correctionContextId: string; resolution: string; ownerEvidence: MappingSourceRef[] }
export interface MappingJob { jobId: string; state: string; lastError: string | null }
export interface CommandReceipt {
  commandId: string; operation: string; outcome: string; bp: PartnerRef | null; mapping: MappingRef | null
  reconcile: { jobId: string; accountId: string; legalEntityKey: string } | null
  completedAt: string; receiptHash: string
}
export type CommandPath = 'create' | 'profile-apply' | 'register' | 'hold' | 'retire' | 'restore' | 'mapping-apply' | 'mapping-reconcile'
export interface WithTag<T> { body: T; etag: string | null }

function response<T>(method: 'GET' | 'POST', path: string, data?: unknown, headers?: Record<string, string>) {
  return http.request<unknown, WithTag<T>>({
    method,
    url: '/main/v1/bp' + path,
    data,
    headers,
    transformResponse: [(data: string, headers) => {
      const body: T = JSON.parse(data)
      return { body, etag: typeof headers['etag'] === 'string' ? headers['etag'] : null }
    }],
  })
}

function listInput(legal: string, name: string, page: number) {
  return {
    filter: { legalEntityKey: legal, name: name.trim() || null, rolesAny: [], registrations: [], lifecycles: [], holds: [],
      bpCd: null, baseCds: [], salesStaffCd: null, businessStaffCd: null, classFilters: [], createdFrom: null, createdTo: null },
    sort: 'BP_CD', direction: 'ASC', page, pageSize: 25,
  }
}

export const businessPartnerWorkspace = {
  scopes: () => response<WorkspaceScope[]>('GET', '/scopes'),
  options: (legal: string) => response<WorkspaceOptions>('GET', '/options?legalEntityKey=' + encodeURIComponent(legal)),
  list: (legal: string, name: string, page: number) =>
    response<{ items: PartnerListItem[]; total: number }>('POST', '/query', listInput(legal, name, page)),
  async exportCsv(legal: string, name: string, page: number) {
    try {
      return await http.post<Blob, Blob>('/main/v1/bp/export', listInput(legal, name, page), { responseType: 'blob' })
    } catch (error) {
      if (isAxiosError(error) && error.response?.data instanceof Blob && error.response.data.type.startsWith('application/json')) {
        const text = await error.response.data.text()
        try { error.response.data = { body: JSON.parse(text) } } catch { /* 由工作区显示通用请求错误。 */ }
      }
      throw error
    }
  },
  detail: (bp: string) => response<PartnerDetail>('GET', '/' + encodeURIComponent(bp)),
  editContext: (bp: string) => response<EditContext>('GET', '/' + encodeURIComponent(bp) + '/edit-context'),
  statusContext: (bp: string) => response<StatusContext>('GET', '/' + encodeURIComponent(bp) + '/status-context'),
  preview: (kind: 'create' | 'edit', data: unknown) =>
    response<WorkspacePreview>('POST', kind === 'create' ? '/create-previews' : '/profile-previews', data),
  execute: (path: CommandPath, key: string, tag: string | null, body: unknown) =>
    response<{ receipt: CommandReceipt; replayed: boolean }>('POST', '/' + path, body, { ...(tag ? { 'If-Match': tag } : {}), 'Idempotency-Key': key }),
  original: (key: string) => response<{ knowledge: string; receipt: CommandReceipt | null }>('GET', '/commands/' + encodeURIComponent(key)),
  probeAccount: (legal: string, account: string) => response<AccountProbe>('POST', '/account-probes', { legalEntityKey: legal, accountId: account }),
  mappingContext: (data: unknown) => response<MappingContext>('POST', '/mapping-contexts', data),
  mappingPreview: (data: unknown) => response<WorkspacePreview>('POST', '/mapping-previews', data),
  mappingBinding: (data: unknown) => response<{ bindingId: string | null; availability: string }>('POST', '/mapping-decision-bindings', data),
  mappingCorrection: (data: unknown) => response<MappingCorrection>('POST', '/mapping-correction-contexts', data),
  mapping: (legal: string, account: string) => response<MappingRead>('GET', '/mapping?' + new URLSearchParams({ legalEntityKey: legal, accountId: account })),
  mappingHistory: (legal: string, account: string, before: string) =>
    response<{ items: MappingRef[]; nextBeforeRevision: string | null }>('GET',
      '/mapping-history?' + new URLSearchParams({ legalEntityKey: legal, accountId: account, beforeRevision: before })),
  mappingJob: (id: string) => response<MappingJob>('GET', '/reconcile-jobs/' + encodeURIComponent(id)),
}
