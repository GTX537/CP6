// A3 固定资产前端类型（spec §10）

export interface AssetCategory {
  id?: string
  code: string
  name: string
  parentId?: string | null
  level: number
  defaultMethod: number
  defaultUsefulLifeMonths: number
  defaultSalvageRate: number
  assetAccountId: string
  accumDeprecAccountId: string
  deprecExpenseAccountId: string
  isActive: boolean
}

export interface AssetCard {
  id?: string
  assetNo?: string
  name: string
  specModel?: string
  categoryId: string
  originalValue: number
  salvageRate: number
  salvageValue: number
  method: number
  usefulLifeMonths: number
  totalWorkload?: number | null
  workloadUnit?: string
  acquisitionDate: string
  depreciationStartPeriod?: string
  accumulatedDepreciation: number
  depreciatedPeriods: number
  netBookValue?: number
  deprecExpenseAccountId?: string | null
  costCenterId?: string | null
  machineId?: string | null
  deptId?: string | null
  status: number
  location?: string
  custodian?: string
  isOpeningImport: boolean
  remarks?: string
}

export interface DepreciationEntryDto {
  assetCardId: string
  assetNo: string
  assetName: string
  method: number
  depreciationAmount: number
  openingAccumulated: number
  closingAccumulated: number
  deprecExpenseAccountId: string
  accumDeprecAccountId: string
  costCenterId?: string | null
  workloadThisPeriod?: number | null
}

export interface DepreciationScheduleRow {
  periodIndex: number
  yearMonth: string
  amount: number
  accumulated: number
  netValue: number
}

export interface DepreciationRun {
  id: string
  no: string
  fiscalPeriodId: string
  periodYearMonth: string
  status: number
  runMode: number
  totalAmount: number
  assetCount: number
  runAt: string
  runBy: string
}

export interface AssetDisposal {
  id?: string
  no?: string
  assetCardId: string
  disposalType: number
  disposalDate: string
  fiscalPeriodId: string
  originalValue?: number
  accumulatedDepreciation?: number
  netBookValue?: number
  proceeds: number
  taxAmount: number
  disposalExpense: number
  netGainLoss?: number
  clearingAccountId?: string
  gainLossAccountId?: string
  receiptBankAccountId?: string | null
  status?: number
  reason?: string
}
