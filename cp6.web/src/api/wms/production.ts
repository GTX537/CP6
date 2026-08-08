import http from '../http'
import type { PagedResult } from '@/types/wms/wms'

export interface TaskAnalytics {
  created: number
  completed: number
  partiallyCompleted: number
  exceptions: number
  overdue: number
  averageMinutes: number
}
export interface ClientDevice {
  deviceId: string
  deviceMode: string
  platform: string
  status: string
  warehouseCd?: string
  areaCd?: string
  appVersion?: string
  lastSeenAt?: string
  currentUser?: string
  currentTaskNo?: string
  rowVersion: string
}
export interface DeviceActivationTicket {
  activationToken: string
  expiresAt: string
  platform: string
  deviceMode: string
  warehouseCd?: string
  areaCd?: string
}
export interface BarcodeAlias {
  id: string
  barcode: string
  barcodeType: string
  targetKey: string
  productCd?: string
  lotNo?: string
  locationCd?: string
  packageUnitCd?: string
  conversionRate: number
  validFrom?: string
  validUntil?: string
  isEnabled: boolean
  rowVersion: string
}
export interface BarcodeImportResult {
  committed: boolean
  validCount: number
  invalidCount: number
  rows: Array<{ rowNumber: number, valid: boolean, errorCode?: string, item?: BarcodeAlias }>
}
export interface StockSerial {
  productCd: string
  serialNo: string
  warehouseCd: string
  locationCd: string
  lotNo: string
  lpnNo?: string
  status: string
  lastTxnNo?: string
  rowVersion: string
}
export interface LpnContent {
  productCd: string
  lotNo: string
  serialNo?: string
  qty: number
}
export interface LogisticsUnit {
  lpnNo: string
  containerType: string
  warehouseCd: string
  locationCd: string
  parentLpnNo?: string
  status: string
  contents: LpnContent[]
  childLpns: string[]
  rowVersion: string
}
export interface LabelJob {
  jobNo: string
  warehouseCd: string
  templateName: string
  format: string
  printerName?: string
  status: string
  requestedBy?: string
  requestedAt: string
  completedAt?: string
  attemptCount: number
  resultMessage?: string
  rowVersion: string
}
export interface LabelTemplate {
  id: string
  templateName: string
  format: string
  templateBody: string
  language?: string
  isEnabled: boolean
  rowVersion: string
}
export interface BarcodeProfile {
  id: string
  profileName: string
  format: string
  pattern: string
  mappingJson: string
  priority: number
  isEnabled: boolean
  rowVersion: string
}
export interface WmsFeatureFlag {
  warehouseCd: string
  productionMoveEnabled: boolean
  serialLpnEnabled: boolean
  scanRetentionDays: number
  rowVersion: string
}
export type WmsFeatureFlagChangeStatus =
  | 'PENDING'
  | 'APPLIED'
  | 'REJECTED'
  | 'STALE'
  | 'CANCELLED'
  | 'FAILED'
export interface WmsFeatureFlagChange {
  id: string
  operationId: string
  warehouseCd: string
  baseProductionMoveEnabled: boolean
  baseSerialLpnEnabled: boolean
  baseScanRetentionDays: number
  baseFeatureRowVersion: string
  targetProductionMoveEnabled: boolean
  targetSerialLpnEnabled: boolean
  targetScanRetentionDays: number
  reason: string
  changeTicket: string
  evidenceUri?: string
  status: WmsFeatureFlagChangeStatus
  requestedById: string
  requestedAtUtc: string
  flowInstanceId: string
  decidedById?: string
  decidedAtUtc?: string
  appliedAtUtc?: string
  failureCode?: string
}
export interface CreateWmsFeatureFlagChange extends IdempotentProductionCommand {
  warehouseCd: string
  productionMoveEnabled: boolean
  serialLpnEnabled: boolean
  scanRetentionDays: number
  rowVersion: string
  reason: string
  changeTicket: string
  evidenceUri?: string
}
export interface WmsRoleScope {
  roleId: number
  warehouseCd: string
  areaCd?: string
}
export interface IdempotentProductionCommand {
  operationId?: string
}
export type SerialLifecycleType =
  | 'RECEIVE'
  | 'PUTAWAY'
  | 'MOVE'
  | 'PICK'
  | 'SHIP'
  | 'COUNT'
  | 'RETURN'
export interface ExistingSerialInput {
  serialNo: string
  warehouseCd: string
  locationCd: string
  lotNo: string
}
export interface EnableSerialTrackingCommand extends IdempotentProductionCommand {
  productCd: string
  trackingMode: 2 | 3
  existingSerials: ExistingSerialInput[]
}
export interface SerialLifecycleCommand extends IdempotentProductionCommand {
  txnType: SerialLifecycleType
  productCd: string
  serialNos: string[]
  warehouseCd: string
  lotNo: string
  fromLocationCd?: string
  toLocationCd?: string
  lpnNo?: string
  deviceId?: string
}
export interface CreateLpnCommand extends IdempotentProductionCommand {
  lpnNo: string
  containerType: string
  warehouseCd: string
  locationCd: string
  deviceId?: string
}
export interface LpnCommandBase extends IdempotentProductionCommand {
  rowVersion: string
  deviceId?: string
}
export interface PackLpnCommand extends LpnCommandBase {
  childLpns: string[]
  contents: LpnContent[]
}
export interface UnpackLpnCommand extends LpnCommandBase {
  childLpns: string[]
  serialNos: string[]
}
export interface MoveLpnCommand extends LpnCommandBase {
  toLocationCd: string
}
export interface SplitLpnCommand extends LpnCommandBase {
  targetLpnNo: string
  targetContainerType: string
  serialNos: string[]
  childLpns: string[]
}
export interface MergeLpnCommand extends LpnCommandBase {
  sourceLpnNo: string
}
export type LpnLifecycleAction = 'pack' | 'unpack' | 'move' | 'split' | 'merge'
export type LpnLifecycleCommand =
  | PackLpnCommand
  | UnpackLpnCommand
  | MoveLpnCommand
  | SplitLpnCommand
  | MergeLpnCommand

const operationId = () => {
  if (typeof globalThis.crypto?.randomUUID === 'function')
    return globalThis.crypto.randomUUID()
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, character => {
    const random = Math.floor(Math.random() * 16)
    const value = character === 'x' ? random : (random & 0x3) | 0x8
    return value.toString(16)
  })
}

export const newProductionOperationId = operationId

function withOperation<T extends IdempotentProductionCommand>(request: T) {
  return {
    ...request,
    operationId: request.operationId || operationId(),
  }
}

export const productionApi = {
  analytics(params: Record<string, unknown> = {}) {
    return http.get<any, TaskAnalytics>('/v2/wms/task-analytics', { params })
  },
  devices(params: Record<string, unknown> = {}) {
    return http.get<any, PagedResult<ClientDevice>>('/v2/admin/client-devices', { params })
  },
  createActivation(request: {
    platform: 'Android' | 'Windows'
    deviceMode: 'Shared' | 'Personal'
    warehouseCd?: string
    areaCd?: string
    validMinutes?: number
  }) {
    return http.post<any, DeviceActivationTicket>('/v2/admin/client-devices', request)
  },
  updateDevice(deviceId: string, request: Partial<ClientDevice> & { rowVersion: string }) {
    return http.patch<any, ClientDevice>(`/v2/admin/client-devices/${encodeURIComponent(deviceId)}`, request)
  },
  barcodes(params: Record<string, unknown> = {}) {
    return http.get<any, PagedResult<BarcodeAlias>>('/v2/wms/barcodes', { params })
  },
  upsertBarcode(request: Partial<BarcodeAlias> & Pick<BarcodeAlias, 'barcode' | 'barcodeType' | 'targetKey'>) {
    return http.post<any, BarcodeAlias>('/v2/wms/barcodes', request)
  },
  importBarcodes(file: File, commit: boolean) {
    const data = new FormData()
    data.append('file', file)
    return http.post<any, BarcodeImportResult>(`/v2/wms/barcodes/import?commit=${commit}`, data)
  },
  barcodeProfiles() {
    return http.get<any, BarcodeProfile[]>('/v2/wms/barcode-profiles')
  },
  upsertBarcodeProfile(request: Partial<BarcodeProfile>) {
    return http.post<any, BarcodeProfile>('/v2/wms/barcode-profiles', request)
  },
  parseBarcode(rawBarcode: string) {
    return http.post('/v2/wms/barcode-profiles/parse', { rawBarcode })
  },
  serials(params: Record<string, unknown> = {}) {
    return http.get<any, PagedResult<StockSerial>>('/v2/wms/serials', { params })
  },
  postSerial(request: SerialLifecycleCommand) {
    return http.post('/v2/wms/serials', withOperation(request))
  },
  enableSerialTracking(request: EnableSerialTrackingCommand) {
    return http.post('/v2/wms/serials/enable-tracking', withOperation(request))
  },
  lpns(params: Record<string, unknown> = {}) {
    return http.get<any, PagedResult<LogisticsUnit>>('/v2/wms/lpns', { params })
  },
  createLpn(request: CreateLpnCommand) {
    return http.post<any, LogisticsUnit>('/v2/wms/lpns', withOperation(request))
  },
  lpnCommand(lpnNo: string, action: LpnLifecycleAction, request: LpnLifecycleCommand) {
    return http.post<any, LogisticsUnit>(
      `/v2/wms/lpns/${encodeURIComponent(lpnNo)}/${action}`,
      withOperation(request),
    )
  },
  labelJobs(params: Record<string, unknown> = {}) {
    return http.get<any, PagedResult<LabelJob>>('/v2/wms/label-jobs', { params })
  },
  createLabelJob(request: Record<string, unknown>) {
    return http.post<any, LabelJob>('/v2/wms/label-jobs', { operationId: operationId(), ...request })
  },
  labelTemplates() {
    return http.get<any, LabelTemplate[]>('/v2/wms/label-jobs/templates')
  },
  upsertLabelTemplate(request: Partial<LabelTemplate>) {
    return http.post<any, LabelTemplate>('/v2/wms/label-jobs/templates', request)
  },
  featureFlags() {
    return http.get<any, WmsFeatureFlag[]>('/v2/admin/wms-features')
  },
  featureChanges(params: { warehouseCd?: string, status?: WmsFeatureFlagChangeStatus } = {}) {
    return http.get<any, WmsFeatureFlagChange[]>('/v2/admin/wms-feature-changes', { params })
  },
  requestFeatureChange(request: CreateWmsFeatureFlagChange) {
    return http.post<any, {
      changeId: string
      approvalInstanceId: string
      status: WmsFeatureFlagChangeStatus
      change: WmsFeatureFlagChange
    }>('/v2/admin/wms-feature-changes', withOperation(request))
  },
  cancelFeatureChange(changeId: string) {
    return http.post(`/v2/admin/wms-feature-changes/${encodeURIComponent(changeId)}/cancel`)
  },
  roleScopes(roleId: number) {
    return http.get<any, WmsRoleScope[]>(`/v2/admin/wms-role-scopes/${roleId}`)
  },
  replaceRoleScopes(roleId: number, scopes: Array<Pick<WmsRoleScope, 'warehouseCd' | 'areaCd'>>) {
    return http.put<any, WmsRoleScope[]>(
      `/v2/admin/wms-role-scopes/${roleId}`,
      { scopes },
    )
  },
}
