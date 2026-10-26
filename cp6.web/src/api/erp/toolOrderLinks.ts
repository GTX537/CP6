import http from '../http'
import type { PlateSourceEnvelope, PlateSourceScope, PlateSourceVersion } from './plateMoldSources'
export type ToolLinkScope = PlateSourceScope
export type ToolLinkEnvelope<T> = PlateSourceEnvelope<T>
export interface ToolNativeOrder { orderId: string; lineId: string; number: string; lineNumber: number; arrangeNumber: string; orderRowVersion: string; lineRowVersion: string; deleted: boolean }
export interface ToolLinkReceipt { operationId: string; tenantId: string; tool: PlateSourceVersion['reference']; nativeToolRowVersion: string; order: ToolNativeOrder; effect: 'ORDER_CREATED' | 'NATIVE_LINK_RECORDED' | 'EXISTING_LINK'; sourceKind: 'NATIVE_ORDER_LINK_ONLY'; peState: 'NOT_EXECUTED' }
export interface ToolLinkView { original: ToolLinkReceipt | null; currentApplicable: boolean; state: 'CURRENT' | 'ORIGINAL_CHANGED' | 'UNRECORDED' }
export interface ToolLinkCommand { operationId: string; tool: PlateSourceVersion['reference']; expectedNativeRowVersion: string; orderDepartment: string | null }
export interface ToolLinkOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolLinkReceipt | null }
const root = '/plate-molds/order-link-owner-v1'
export const toolLinkApi = {
  scope: () => http.get<any, ToolLinkEnvelope<ToolLinkScope>>(`${root}/scope`),
  read: (id: string) => http.get<any, ToolLinkEnvelope<ToolLinkView>>(`${root}/${encodeURIComponent(id)}`),
  save: (command: ToolLinkCommand) => http.post<any, ToolLinkEnvelope<ToolLinkReceipt>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close
    ? http.post<any, ToolLinkEnvelope<ToolLinkOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, ToolLinkEnvelope<ToolLinkOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
