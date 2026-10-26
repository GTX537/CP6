import http from '../http'
import type { UomEnvelope, UomScope, UomReference, UomVersion } from './uomProfiles'
import type { ResourceReference } from '../mes/resourceSources'
export type TimeBasis = 'SECOND' | 'MINUTE' | 'HOUR'
export type TimeOperationKind = 'TIME_BASIS'
export type TimeEnvelope<T> = UomEnvelope<T>
export type TimeScope = UomScope
export interface TimeReference { owner: 'Main.Erp.UomTimeBasis.v1'; id: string; version: string; digest: string }
export interface TimeDefinition { baseRef: UomReference; basis: TimeBasis; enabled: boolean; reason: string }
export interface TimeVersion { tenantId: string; reference: TimeReference; isCurrent: boolean; definition: TimeDefinition; originalJson: string; currentApplicable: boolean; reasons: string[] }
export interface TimeEditor { currentBase: UomVersion; expectedVersion: string; definition: TimeVersion | null }
export interface TimeCommand { operationId: string; expectedVersion: string; definition: TimeDefinition }
export interface TimeResult { operationId: string; kind: TimeOperationKind; objectId: string; version: string; digest: string }
export interface TimeOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: TimeResult | null }
export interface ResourceTimeResult { sourceRef: ResourceReference; field: string; nativeBasis: TimeBasis; nativeQuantity: string | null; targetRef: UomReference; timeBasisRef: TimeReference | null; state: 'KNOWN' | 'UNKNOWN'; convertedQuantity: string | null; reasons: string[]; observation: 'CURRENT_TRANSACTION_ONLY'; qualification: 'NOT_ATTESTED' }
const root = '/uom-profiles/owner-v1/time-bases'
export const timeApi = {
  scope: () => http.get<any, TimeEnvelope<TimeScope>>(`${root}/scope`),
  editor: (id: string) => http.get<any, TimeEnvelope<TimeEditor>>(`${root}/${encodeURIComponent(id)}/editor`),
  read: (id: string, version?: string) => http.get<any, TimeEnvelope<TimeVersion>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (input: TimeCommand) => http.post<any, TimeEnvelope<TimeResult>>(`${root}/commands`, input),
  operation: (id: string, close: boolean) => close
    ? http.post<any, TimeEnvelope<TimeOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, TimeEnvelope<TimeOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
  resource: (sourceRef: ResourceReference, field: string, targetRef: UomReference) => http.post<any, TimeEnvelope<ResourceTimeResult>>(
    `/mes/resource-sources/owner-v1/${sourceRef.kind}/${encodeURIComponent(sourceRef.id)}/time-values`, { sourceRef, field, targetRef }),
}
