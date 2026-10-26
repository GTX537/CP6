import http from '../http'

export interface UomEnvelope<T> { code: number; data: T }
export interface UomScope { tenantId: string; actorId: string }
export interface UomReference { owner: 'Main.Erp.UomProfile.v1'; id: string; version: string; digest: string }
export interface UomProfile {
  code: string; name: string; dimensionKey: string; quantityScale: number | null; packMultiple: string | null
  mode: 'BASE' | 'RATIO'; baseRef: UomReference | null; numerator: string; denominator: string; enabled: boolean
}
export interface UomVersion { tenantId: string; reference: UomReference; isCurrent: boolean; profile: UomProfile; originalJson: string }
export interface UomCreationContext extends UomScope {
  authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[]
}
export interface UomCommand {
  operationId: string; action: 'CREATE' | 'REVISE'; profileId: string; expectedVersion: string | null
  expectedAuthorityVersion: string | null; departmentId: string | null; profile: UomProfile
}
export type UomOperationKind = 'PROFILE' | 'PRODUCT_UNIT'
export interface UomResult { operationId: string; kind: UomOperationKind; objectId: string; version: string; digest: string }
export interface UomOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: UomResult | null }
export interface ProductUnitChoice { id: string; productCd: string; productVersion: string; unitCode: string | null }
export interface ProductUnitEditor {
  productId: string; productVersion: string; unitCode: string | null; productActive: boolean
  expectedBindingVersion: string; boundProfile: UomReference | null
}
export interface ProductUnitCommand {
  operationId: string; productId: string; expectedBindingVersion: string; productVersion: string; profileRef: UomReference
}
export interface ProductUnitVersion {
  tenantId: string; productId: string; version: string; digest: string; productVersion: string; productDigest: string
  unitCode: string; profileRef: UomReference; isLatestBinding: boolean; currentApplicable: boolean; reasons: string[]
  originalJson: string; formalAdoption: 'REQUIRED_FENCED'; missing: string[]
}
const unit = '/uom-profiles/owner-v1', product = '/products/identity-owner-v1'
export const uomApi = {
  scope: () => http.get<any, UomEnvelope<UomScope>>(`${unit}/scope`),
  creationContext: () => http.get<any, UomEnvelope<UomCreationContext>>(`${unit}/creation-context`),
  list: (search = '', page = 1, size = 20) => http.get<any, UomEnvelope<UomVersion[]>>(unit, { params: { search, page, size } }),
  read: (id: string, version?: string) => http.get<any, UomEnvelope<UomVersion>>(`${unit}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (command: UomCommand) => http.post<any, UomEnvelope<UomResult>>(`${unit}/commands`, command),
  operation: (id: string, close: boolean) => close
    ? http.post<any, UomEnvelope<UomOperation>>(`${unit}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, UomEnvelope<UomOperation>>(`${unit}/operations/${encodeURIComponent(id)}`),
  convert: (from: UomReference, to: UomReference, quantity: string) => http.post<any,
    UomEnvelope<{ from: UomReference; to: UomReference; quantity: string; convertedQuantity: string }>>(`${unit}/conversions`, { from, to, quantity }),
}
export const productUnitApi = {
  scope: () => http.get<any, UomEnvelope<UomScope>>(`${product}/unit-bindings/scope`),
  lookup: (search: string) => http.get<any, UomEnvelope<ProductUnitChoice[]>>(`${product}/lookup`, { params: { search } }),
  editor: (id: string) => http.get<any, UomEnvelope<ProductUnitEditor>>(`${product}/${encodeURIComponent(id)}/unit-profile/editor`),
  read: (id: string, version?: string) => http.get<any, UomEnvelope<ProductUnitVersion>>(`${product}/${encodeURIComponent(id)}/unit-profile`, { params: { version } }),
  save: (command: ProductUnitCommand) => http.post<any, UomEnvelope<UomResult>>(`${product}/unit-bindings/commands`, command),
  operation: (id: string, close: boolean) => close
    ? http.post<any, UomEnvelope<UomOperation>>(`${product}/unit-bindings/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, UomEnvelope<UomOperation>>(`${product}/unit-bindings/operations/${encodeURIComponent(id)}`),
}
