import http from '../http'
import type { EngineeringEnvelope } from '../plm/engineeringProducts'

export interface RmaScope { tenantId: string; actorId: string }
export interface RmaSourceChoice { id: string; number: string; customerCode: string | null }
export interface RmaSourceLine { id: string; lineNumber: number; productCode: string; shippedQuantity: string; unitCode: string; warehouseCode: string; locationCode: string | null; lotNumber: string | null; shipmentTransaction: string }
export interface RmaSource extends RmaSourceChoice { version: string; lines: RmaSourceLine[] }
export interface RmaCreate { operationId: string; applicationId: string; outboundId: string; sourceVersion: string; reason: string; requestedOutcome: string; lines: { outboundLineId: string; quantity: string }[]; remarks: string | null }
export interface RmaApplication { id: string; version: number; state: 'DRAFT' | 'SUBMITTED'; etag: string; outboundId: string; outboundNumber: string; customerCode: string; reason: string | null; remarks: string | null; requestedOutcome: string; requestedAt: string; submittedAt: string | null; lines: { source: RmaSourceLine; requestedQuantity: string }[] }
export interface RmaResult { operationId: string; applicationId: string; version: number; state: 'DRAFT' | 'SUBMITTED'; etag: string }
export interface RmaOperation { operationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result: RmaResult | null }
const root = '/wms/rma/native-applications-v1'
const data = <T>(response: EngineeringEnvelope<T>) => response.data
export const rmaApplicationsApi = {
  scope: () => http.get<any, EngineeringEnvelope<RmaScope>>(`${root}/scope`).then(data),
  sources: (keyword: string, page: number) => http.get<any, EngineeringEnvelope<RmaSourceChoice[]>>(`${root}/sources`, { params: { keyword, page } }).then(data),
  source: (id: string) => http.get<any, EngineeringEnvelope<RmaSource>>(`${root}/sources/${encodeURIComponent(id)}`).then(data),
  list: (page: number) => http.get<any, EngineeringEnvelope<RmaApplication[]>>(root, { params: { page } }).then(data),
  read: (id: string, version?: number) => http.get<any, EngineeringEnvelope<RmaApplication>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }).then(data),
  create: (command: RmaCreate) => http.post<any, EngineeringEnvelope<RmaResult>>(root, command).then(data),
  submit: (application: RmaApplication, operationId: string) => http.post<any, EngineeringEnvelope<RmaResult>>(`${root}/${encodeURIComponent(application.id)}/submit`, { operationId, expectedVersion: application.version }, { headers: { 'If-Match': application.etag } }).then(data),
  operation: (id: string, close: boolean) => (close
    ? http.post<any, EngineeringEnvelope<RmaOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<RmaOperation>>(`${root}/operations/${encodeURIComponent(id)}`)).then(data),
}
