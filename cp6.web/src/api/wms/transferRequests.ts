import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
import type { TransferDeployment } from './transferEnvironment'
export interface TransferLineInput { clientLineKey: string; productCatalogueRef: EngineeringReference; inputUomRef: EngineeringReference; inputQuantity: string; sourceWarehouseId: string; sourceLocationId: string; destinationWarehouseId: string; destinationLocationId: string }
export interface TransferRequestLine { id: string; input: TransferLineInput; product: { label: string }; baseQuantity: string }
export interface TransferRequest { id: string; draftKey: string; creatorId: string; authorId: string; departmentId: string | null; version: string; revision: string; etag: string; state: 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'REJECTED' | 'WITHDRAWN'; lines: TransferRequestLine[]; contentDigest: string; allowedActions: string[]; executionState: 'NOT_DISPATCHED' | 'PARTIALLY_MOVED' | 'COMPLETED'; stages?: TransferStage[] | null }
export interface TransferStageSelection { requestLineId: string; stockId: string; requestFrom: string; quantity: string }
export interface TransferStage { id: string; state: 'PREPARED' | 'POSTED' | 'CLOSED_NO_EFFECT'; device: string; scanOccurrenceKey: string; performerId: string; digest: string; lines: { selection: TransferStageSelection; lot: string }[]; movements: { outMovementId: string; inMovementId: string; quantity: string }[] }
export interface TransferStockChoice { requestLineId: string; stockId: string; lot: string; available: string; unitCode: string }
export interface TransferStagePrepare extends TransferAction { device: string; scanOccurrenceKey: string; lines: TransferStageSelection[] }
export interface TransferStageDecision { operationId: string; expectedRevision: string; etag: string; stageDigest: string; physicalNotOccurred: boolean; reason: string }
export interface TransferRequestContext { tenantId: string; actorId: string; deployment: TransferDeployment; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[]; locations: { warehouseId: string; locationId: string; label: string }[]; products: { catalogueRef: EngineeringReference; label: string }[]; units: { reference: EngineeringReference; profile: { code: string; name: string } }[] }
export interface TransferRequestResult { operationId: string; request: TransferRequest; changed: boolean }
export interface TransferRequestOperation { operationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result: TransferRequestResult | null }
export interface TransferCreate { operationId: string; draftKey: string; departmentId: string | null; lines: TransferLineInput[]; reason: string }
export interface TransferEdit { operationId: string; expectedRevision: string; etag: string; lines: TransferLineInput[]; reason: string }
export interface TransferAction { operationId: string; expectedRevision: string; etag: string; contentDigest: string; reason: string }
const root = '/wms/transfer/native-requests-v1'
export const transferRequestsApi = {
  context: () => http.get<any, EngineeringEnvelope<TransferRequestContext>>(`${root}/context`),
  list: (page = 1) => http.get<any, EngineeringEnvelope<TransferRequest[]>>(root, { params: { page } }),
  read: (id: string) => http.get<any, EngineeringEnvelope<TransferRequest>>(`${root}/${encodeURIComponent(id)}`),
  stockChoices: (id: string) => http.get<any, EngineeringEnvelope<TransferStockChoice[]>>(`${root}/${encodeURIComponent(id)}/stock-choices`),
  prepareStage: (id: string, body: TransferStagePrepare) => http.post<any, EngineeringEnvelope<TransferRequestResult>>(`${root}/${encodeURIComponent(id)}/stages`, body),
  decideStage: (id: string, stage: string, execute: boolean, body: TransferStageDecision) => http.post<any, EngineeringEnvelope<TransferRequestResult>>(`${root}/${encodeURIComponent(id)}/stages/${encodeURIComponent(stage)}/${execute ? 'execute' : 'close'}`, body),
  create: (body: TransferCreate) => http.post<any, EngineeringEnvelope<TransferRequestResult>>(root, body),
  edit: (id: string, body: TransferEdit) => http.post<any, EngineeringEnvelope<TransferRequestResult>>(`${root}/${encodeURIComponent(id)}/edit`, body),
  act: (id: string, action: string, body: TransferAction) => http.post<any, EngineeringEnvelope<TransferRequestResult>>(`${root}/${encodeURIComponent(id)}/decisions/${encodeURIComponent(action)}`, body),
  operation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<TransferRequestOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<TransferRequestOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
