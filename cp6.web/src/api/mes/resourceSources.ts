import http from '../http'
export type ResourceKind = 'MACHINE' | 'WORK_CENTER'
export interface ResourceScope { tenantId: string; actorId: string }
export interface ResourceEnvelope<T> { code: number; data: T }
export interface ResourceReference { owner: 'Main.Mes.ResourceSource.v1'; kind: ResourceKind; id: string; version: string; digest: string }
export interface ResourceVersion {
  tenantId: string; reference: ResourceReference; axes: { master: string; capability: string; assignment: string; control: string }
  isCurrent: boolean; sourcePresent: boolean; sourceEnabled: boolean; qualification: 'NOT_ATTESTED'; originalJson: string
  source: { master: Record<string, string | null>; capability: Record<string, string | number | null>; assignment: Record<string, string | null>; control: { enabled: boolean; isDeleted: boolean; creator: string | null } }
}
export interface ResourceCommand { operationId: string; expected: ResourceReference; patches: { field: string; value: string | null }[] }
export interface ResourceResult { operationId: string; kind: ResourceKind; objectId: string; version: string; digest: string; changed: boolean }
export interface ResourceOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ResourceResult | null }
export function resourceApi(kind: ResourceKind) {
  const root = `/mes/resource-sources/owner-v1/${kind}`
  return {
    scope: () => http.get<any, ResourceEnvelope<ResourceScope>>(`${root}/scope`),
    list: (search: string, page = 1) => http.get<any, ResourceEnvelope<ResourceVersion[]>>(root, { params: { search, page, pageSize: 20 } }),
    read: (id: string, version?: string) => http.get<any, ResourceEnvelope<ResourceVersion>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
    save: (input: ResourceCommand) => http.post<any, ResourceEnvelope<ResourceResult>>(`${root}/commands`, input),
    operation: (id: string, close: boolean) => close
      ? http.post<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
      : http.get<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
  }
}
