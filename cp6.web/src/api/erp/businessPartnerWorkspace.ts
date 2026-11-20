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
export interface CommandReceipt { commandId: string; operation: string; outcome: string; bp: PartnerRef; completedAt: string; receiptHash: string }
export type CommandPath = 'create' | 'profile-apply' | 'register' | 'hold' | 'retire' | 'restore'
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

export const businessPartnerWorkspace = {
  scopes: () => response<WorkspaceScope[]>('GET', '/scopes'),
  options: (legal: string) => response<WorkspaceOptions>('GET', '/options?legalEntityKey=' + encodeURIComponent(legal)),
  list: (legal: string, name: string, page: number) => response<{ items: PartnerListItem[]; total: number }>('POST', '/query', {
    filter: { legalEntityKey: legal, name: name.trim() || null, rolesAny: [], registrations: [], lifecycles: [], holds: [],
      bpCd: null, baseCds: [], salesStaffCd: null, businessStaffCd: null, classFilters: [], createdFrom: null, createdTo: null },
    sort: 'BP_CD', direction: 'ASC', page, pageSize: 25,
  }),
  detail: (bp: string) => response<PartnerDetail>('GET', '/' + encodeURIComponent(bp)),
  editContext: (bp: string) => response<EditContext>('GET', '/' + encodeURIComponent(bp) + '/edit-context'),
  statusContext: (bp: string) => response<StatusContext>('GET', '/' + encodeURIComponent(bp) + '/status-context'),
  preview: (kind: 'create' | 'edit', data: unknown) =>
    response<WorkspacePreview>('POST', kind === 'create' ? '/create-previews' : '/profile-previews', data),
  execute: (path: CommandPath, key: string, tag: string, body: unknown) =>
    response<{ receipt: CommandReceipt; replayed: boolean }>('POST', '/' + path, body, { 'If-Match': tag, 'Idempotency-Key': key }),
  original: (key: string) => response<{ knowledge: string; receipt: CommandReceipt | null }>('GET', '/commands/' + encodeURIComponent(key)),
}
