// 计划中台（Plan）模块前端类型 —— 对应后端 CP6.Entity.DomainModels.Plan / Services.Plan

export interface ApiResp<T> {
  code: number
  message: string
  data: T
}

/** 计划主数据：品目计划策略（spec §3.2） */
export interface ItemPolicy {
  id?: string
  itemCd: string
  safetyStock: number
  purchaseLeadDays: number
  lotRule: number // 1=按需 2=最小量 3=订货倍数 4=整卷取整
  moqQty?: number | null
  multipleQty?: number | null
  makeOrBuy: number // 1=自制 2=采购
  isDefault?: boolean
}

/** MRP 运算批次 */
export interface MrpRun {
  id: string
  runNo: string
  runAt: string
  status: number
}

/** 计划订单 */
export interface PlannedOrder {
  id: string
  mrpRunId: string
  type: number // 1=采购 2=生产
  itemCd: string
  qty: number
  requiredDate: string
  releaseDate: string
  status: number // 0=建议 1=已确认 2=已转单 9=已忽略
  convertedDocNo?: string | null
}

/** 净需求明细（毛-供给-净 钻取） */
export interface NetRequirement {
  id: string
  mrpRunId: string
  itemCd: string
  bucket: string
  gross: number
  onHand: number
  inTransit: number
  inWip: number
  firmPlanned: number
  safetyStock: number
  net: number
}

export interface MrpDemandInput {
  itemCd: string
  qty: number
  requiredDate?: string
  sourceRefNo?: string
}

export interface MrpRunRequest {
  demands?: MrpDemandInput[]
  fromOpenOrders?: boolean
  scopeJson?: string
}
