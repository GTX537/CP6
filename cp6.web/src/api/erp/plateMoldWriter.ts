import http from '../http'
import type { PlateMoldDto } from '@/types/erp/plateMold'
import type { ToolNativeOrder, ToolLinkEnvelope } from './toolOrderLinks'
import type { PlateSourceVersion } from './plateMoldSources'
export type ToolWriteKind = 'CREATE' | 'REVISE' | 'UPDATE' | 'DELETE'
export interface ToolWriterScope { tenantId: string; actorId: string; newTechnicalControl: NonNullable<PlateMoldDto['expectedTechnicalControl']> }
export interface ToolWriteReceipt { operationId: string; kind: ToolWriteKind; toolId: string; number: string; revision: number;
  source: PlateSourceVersion['reference']; nativeRowVersion: string; technicalControl: NonNullable<PlateMoldDto['expectedTechnicalControl']>;
  order: ToolNativeOrder; commercialState: 'OWNER_APPLIED' | 'REQUESTED';
  intent?: { operationId: string; toolId: string; kind: 'CHANGE_REQUEST' | 'CANCEL_REQUEST'; state: 'REQUESTED'; requestDigest: string; originalLifecycle: string; order: ToolNativeOrder } | null;
  peState: 'NOT_EXECUTED'; orderSourceKind: 'NATIVE_ORDER_LINK_ONLY' }
export interface ToolWriteOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolWriteReceipt | null }
const root = '/plate-molds/write-owner-v1'
export const plateMoldWriterApi = {
  scope: () => http.get<any, ToolLinkEnvelope<ToolWriterScope>>(`${root}/scope`),
  operation: (id: string, close: boolean) => close
    ? http.post<any, ToolLinkEnvelope<ToolWriteOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, ToolLinkEnvelope<ToolWriteOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
