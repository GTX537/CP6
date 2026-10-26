import http from '../http'
export interface CurrencyEnvelope<T> { code: number; data: T }
export interface CurrencyScope { tenantId: string; actorId: string }
export interface CurrencyReference { owner: 'Main.Erp.CurrencyPolicy.v1'; id: string; version: string; digest: string }
export interface CurrencyPolicy { code: string; name: string; minorDigits: number | null; priceDigits: number | null; rounding: 'HALF_EVEN'; enabled: boolean }
export interface CurrencyVersion { tenantId: string; reference: CurrencyReference; isCurrent: boolean; policy: CurrencyPolicy; originalJson: string }
export interface CurrencyCreationContext extends CurrencyScope { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
export interface CurrencyCommand { operationId: string; action: 'CREATE' | 'REVISE'; policyId: string; expectedVersion: string | null; expectedAuthorityVersion: string | null; departmentId: string | null; policy: CurrencyPolicy }
export type CurrencyOperationKind = 'POLICY'
export interface CurrencyResult { operationId: string; kind: CurrencyOperationKind; objectId: string; version: string; digest: string }
export interface CurrencyOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: CurrencyResult | null }
const root = '/currency-policies/owner-v1'
export const currencyApi = {
  scope: () => http.get<any, CurrencyEnvelope<CurrencyScope>>(`${root}/scope`),
  creationContext: () => http.get<any, CurrencyEnvelope<CurrencyCreationContext>>(`${root}/creation-context`),
  list: (search = '', page = 1, size = 20) => http.get<any, CurrencyEnvelope<CurrencyVersion[]>>(root, { params: { search, page, size } }),
  read: (id: string, version?: string) => http.get<any, CurrencyEnvelope<CurrencyVersion>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: CurrencyCommand) => http.post<any, CurrencyEnvelope<CurrencyResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, CurrencyEnvelope<CurrencyOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {}) : http.get<any, CurrencyEnvelope<CurrencyOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
  quantize: (policyRef: CurrencyReference, kind: 'AMOUNT' | 'PRICE', value: string) => http.post<any, CurrencyEnvelope<{ policyRef: CurrencyReference; kind: string; value: string; roundedValue: string }>>(`${root}/quantizations`, { policyRef, kind, value }),
}
