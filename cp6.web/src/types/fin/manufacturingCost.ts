export interface FinanceChoice {
  token: string
  legalEntityLabel: string
  ledgerLabel: string
}

export interface FinanceContext {
  token: string
  scopeKey: string
  businessDate: string
  currency: string
}

export interface FinanceOptions {
  contexts: FinanceChoice[]
  contextToken: string | null
  scope: { environmentId: string; tenantId: string; legalEntityKey: string; ledgerKey: string; mode: string } | null
  businessDate: string | null
  currencies: string[]
  availability: 'AVAILABLE' | 'UNPROVEN' | 'NOT_AUTHORIZED'
}

export interface CostCenterLocator { rootId: string; versionId: string; opaqueToken: string }
export interface CostCenterChoice { locator: CostCenterLocator; code: string; name: string; operationId: string | null }
export interface RateBasisChoice {
  id: string
  workCenterId: string
  operationId: string
  policyVersionId: string
  zeroAllowed: boolean
  wgCd: string
  productCd: string
  processCd: string
  taskCd: string
}

export type RateState = 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'RELEASED'
export interface RateVersion {
  rateId: string
  state: RateState
  revision: number
  wgCd: string
  productCd: string
  processCd: string
  taskCd: string
  from: string
  toExclusive: string | null
  laborRate: string
  overheadRate: string
  madeByCurrentUser: boolean
}

export interface RateMaintenance {
  currency: string
  businessDate: string
  truncated: boolean
  versions: RateVersion[]
  choices: RateBasisChoice[]
  centers: CostCenterChoice[]
}

export interface RateDraft {
  workCenterId: string
  operationProfileId: string
  costCenter: CostCenterLocator
  policyVersionId: string
  basisId: string
  laborRate: string
  overheadRate: string
  from: string
  toExclusive: string | null
}

export interface RateSegment { rateId: string; from: string; toExclusive: string | null }
export interface RatePreview { token: string; rateId: string; before: RateSegment[]; after: RateSegment[] }

export type StandardBasisState = 'SUBMITTED' | 'APPROVED' | 'PUBLISHED' | 'REVOKED'
export interface BasisVersion {
  versionId: string
  sequence: string
  state: StandardBasisState
  revision: number
  selected: boolean
  from: string
  toExclusive: string | null
  madeByCurrentUser: boolean
}

export interface WorkOrderCostInputs {
  workOrderId: string
  workOrderNo: string
  productCd: string
  outputUnit: string | null
  completedQuantity: string
  businessDate: string
  currency: string
  policyVersionId: string | null
  sourceIssue: string | null
  basisInputsAvailable: boolean
  materials: { id: string; materialCd: string; materialName: string | null; unit: string | null }[]
  steps: { id: string; processCd: string; taskCd: string; wgCd: string }[]
  basisVersions: BasisVersion[]
  document: { id: string; version: string | null } | null
}

export interface BasisSubmission {
  commandId: string
  workOrderId: string
  policyVersionId: string
  previousVersionId: string | null
  targetQuantity: string
  outputUnit: string
  from: string
  toExclusive: string | null
  materials: { materialOccurrenceId: string; allowedQuantity: string; unit: string }[]
  times: { workStepId: string; laborHours: string; machineHours: string }[]
  zeroBasisReference: string | null
  reason: string
}

export interface StandardBody {
  targetQuantity: string
  outputUnit: string
  currency: string
  total: string
  zeroBasisReference: string | null
  lines: { inputId: string; element: 'Material' | 'Labor' | 'Overhead'; unit: string; quantity: string; unitPrice: string; amount: string; sourceId: string }[]
}

export interface StandardBasis { body: StandardBody; policyVersionId: string }
export interface StandardDocumentView {
  documentId: string
  version: string | null
  state: 'DRAFT' | 'READY'
  accountingDate?: string
  standardBasis?: { versionId: string; sequence: string }
  body?: StandardBody
}

interface CommandInput { commandId: string; reason: string }
export type StandardCommand =
  | { operation: 'CREATE'; input: CommandInput & { workOrderId: string } }
  | { operation: 'PREPARE'; documentId: string; input: CommandInput & { expectedVersion: string | null; standardBasisVersionId: string } }
  | { operation: 'FREEZE'; documentId: string; input: CommandInput & { expectedVersion: string | null; planId: string } }

export interface StandardReceipt {
  kind: 'COMMITTED'
  commandId: string
  receiptId: string
  operation: StandardCommand['operation']
  documentId: string
  version: string | null
  planId: string | null
  expiresAt: string | null
  replayed: boolean
}

export type StandardCommandResult = StandardReceipt | { kind: 'OUTCOME_UNKNOWN'; commandId: string }
export type StandardCommandQuery =
  | { kind: 'FOUND'; reply: StandardReceipt }
  | { kind: 'NOT_OBSERVED'; commandId: string; provesPermanentNoEffect: false }
