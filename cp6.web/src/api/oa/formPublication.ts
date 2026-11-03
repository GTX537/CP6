import http from '../http'
import type { FormAuthoringView } from './formAuthoring'
import type { DefinitionImpact } from './definitionImpact'
import type { DefinitionDiagnostic } from './definitionDiagnostics'
export interface PublicationPreview {
  id: string; digest: string; draftVersionId: string; expectedVersion: string; schemaDigest: string
  expiresAtUtc: string; diagnostics: string[]; canPublish: boolean; diagnosticDetails?: DefinitionDiagnostic[] | null
  affectedDraftCount?: number | null; impact?: DefinitionImpact | null
  dependencies: { kind: string; location: string; reference: string; version: string | null; evidenceState: string }[]
}
export interface PublishedForm {
  definitionId: string; versionId: string; version: number; key: string; fields: FormAuthoringView['fields']
  schemaDigest: string | null; publishedAtUtc: string; publisherRef: string; activationChanged: false
}
export type LifecycleOperation = 'FORM_PUBLISH' | 'FORM_CREATE_DRAFT'
export interface LifecycleIntent {
  unknown?: boolean
  key: string; definitionId: string; operation: LifecycleOperation
  command: { commandId: string; expectedVersion: string; body: Record<string, string> }
}
export interface LifecycleResult {
  commandId: string; outcome: string; resourceId: string | null; resourceVersion: string | null
  result: { publication: PublishedForm | null; draft: FormAuthoringView | null } | null
}
const root = (key: string) => `/oa/v2/definitions/FORM/${encodeURIComponent(key)}`
const data = <T>(value: any): T => value?.data ?? value
export const formPublicationApi = {
  preview: async (view: FormAuthoringView, signal?: AbortSignal) => data<PublicationPreview>(await http.post(root(view.key) + '/publish-preview', { draftVersionId: view.draftId, expectedVersion: view.version }, { signal })),
  versions: async (key: string, page: number, signal?: AbortSignal) => data<PublishedForm[]>(await http.get(root(key) + '/versions', { params: { page }, signal })),
  execute: async (intent: LifecycleIntent, signal?: AbortSignal) => data<LifecycleResult>(await http.post(root(intent.key) + (intent.operation === 'FORM_PUBLISH' ? '/publish' : '/drafts'), intent.command, { signal })),
  original: async (intent: LifecycleIntent, signal?: AbortSignal) => data<LifecycleResult>(await http.get(`/oa/v2/commands/${encodeURIComponent(intent.command.commandId)}`, { params: { operation: intent.operation, resource: intent.key }, signal })),
}
