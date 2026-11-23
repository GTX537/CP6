import http from '../http'

export type Kind = 'TRIGGER' | 'CONNECTOR'
export type Control = 'PUBLISH' | 'ENABLE' | 'ACTIVATE' | 'DISABLE' | 'RETIRE'
export interface Profile {
  id: string; label: string; kind: string; sourceKind?: string; enabled: boolean; adoption: string
  implementation?: string; implementationVersion?: string; operation?: string; hasAuth: boolean; credentialGeneration?: string
  timeZone?: string; timeRules?: string; misfirePolicy?: string; sourceModule?: string; hookName?: string
  registration?: string; schema?: string; destinationPolicy?: string; inputSchema?: string; outputSchema?: string; maxCallDurationSec?: number
}
export interface Selection {
  name: string; sourceKind: string; profileId: string; flowKey?: string; flowVersionId?: string; legacyRootId?: string
}
export interface Approval { subjectRef: string; kind: string; impactRef?: string; decisionRef?: string; decision: string; maker: string }
export interface Version {
  rootId: string; sourceKind: string; name: string; revision: string; latest: string; active?: string; versionId: string; number: number
  displayName: string; state: string; published: boolean; profile: Profile; approvals: Approval[]; versions: {id: string; number: number}[]; canDecide?: boolean
  content: { selection: Selection; config?: { cron?: string; vars?: Record<string, unknown>; varsMap?: Record<string, string> } }
}
export interface Choices {
  profiles: Profile[]; definitions: { flowKey: string; versionId: string; version: string }[]; actions: string[]
  legacySources?: { id: string; name: string; sourceKind: string; flowKey?: string }[]
}
export interface Impact {
  previewId: string; rootId: string; versionId: string; action: Control; coverage: string; expires: string
  manifest: { invocations: number; slots: number; jobs: number; instances: number; inFlight: number }
  page: number; total: number; entries: {locator: string; kind: string; state: string}[]
}
export interface DispatchHistory {
  page: number; hasNext: boolean
  entries: { jobId: string; state: string; transportState: string; originalQueryState?: string; adoption: string }[]
}
export interface Subject { subjectRef: string; kind: string; rootId: string; versionId: string; revision: string; target: Version; impact?: Impact; canDecide: boolean }
export interface Intent { path: string; command: { commandId: string; expectedVersion: string; body: unknown } }
export interface Receipt {
  commandId: string; outcome: string; rootId: string; versionId: string
  result: { revision?: string; subjectRef?: string; decisionRef?: string; state?: string; effect?: string; notRevokedInFlight?: number } & Partial<Impact>
}
const base = '/oa/conn/v1/'
export const family = (kind: Kind) => kind === 'TRIGGER' ? 'triggers' : 'connectors'
async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await http.get(base + path, { signal })
  return response.data
}
async function post<T>(path: string, body: unknown, signal?: AbortSignal): Promise<T> {
  const response = await http.post(base + path, body, { signal })
  return response.data
}
export const connectorControl = {
  choices: (kind: Kind, signal?: AbortSignal) => get<Choices>('options?kind=' + kind, signal),
  list: (kind: Kind, page: number, signal?: AbortSignal) => get<Version[]>(family(kind) + '?page=' + page, signal),
  context: (kind: Kind, root: string, signal?: AbortSignal, version?: string) =>
    get<Version>(family(kind) + '/' + encodeURIComponent(root) + (version ? '/versions/' + encodeURIComponent(version) : '/context'), signal),
  subjects: (kind: Kind, page: number, signal?: AbortSignal) => get<Subject[]>('review-subjects?kind=' + kind + '&page=' + page, signal),
  creation: (kind: Kind, selection: Selection, signal?: AbortSignal) =>
    post<{expectedVersion: string}>('creation-contexts?kind=' + kind, selection, signal),
  execute: (intent: Intent, signal?: AbortSignal) => post<Receipt>(intent.path, intent.command, signal),
  preview: (id: string, signal?: AbortSignal, page = 1) => get<Impact>('control-impact-previews/' + encodeURIComponent(id) + '?page=' + page, signal),
  dispatches: (root: string, version: string, page: number, signal?: AbortSignal) =>
    get<DispatchHistory>('connectors/' + encodeURIComponent(root) + '/versions/' + encodeURIComponent(version) + '/dispatches?page=' + page, signal),
  original: (id: string, signal?: AbortSignal) => get<Receipt>('commands/' + encodeURIComponent(id), signal),
}
