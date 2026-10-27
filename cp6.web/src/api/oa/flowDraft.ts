import http from '../http'
export interface DraftSource { versionId: string; version: number; name: string; schemaDigest: string }
export interface DraftContext { definitionId: string; headVersion: string; draftVersionId: string | null; sources: DraftSource[] }
export interface DraftCommand { operationId: string; sourceVersionId: string; expectedHeadVersion: string }
export interface DraftCreated { owner: string; operationId: string; sourceVersionId: string; sourceDigest: string; draft: { definitionId: string; versionId: string; version: number } }
const path = (key: string) => `/oa/flow-defs/${encodeURIComponent(key)}`
export const flowDraftApi = {
  context: async (key: string): Promise<DraftContext> => (await http.get(`${path(key)}/draft-context`)).data,
  create: async (key: string, body: DraftCommand): Promise<DraftCreated> => (await http.post(`${path(key)}/drafts`, body)).data,
  operation: async (key: string, operation: string): Promise<{ state: string; result: DraftCreated | null }> =>
    (await http.get(`${path(key)}/draft-operations/${encodeURIComponent(operation)}`)).data,
}
