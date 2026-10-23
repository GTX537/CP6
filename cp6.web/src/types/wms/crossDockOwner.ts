export const crossDockVersion = 'CROSS-DOCK-OWNER/1'
export interface CrossDockQuery {
  xDockNo?: string; productCd?: string; inboundNo?: string; outboundNo?: string
  status?: number; dateFrom?: string; dateTo?: string; page: number; pageSize: number
}
export interface CrossDockObservation {
  version: string; knowledge: 'UNKNOWN'; sourceState: 'UNRESOLVED_SOURCE'; id: string; xDockNo: string
  productCd: string; productName?: string | null; qty: string; warehouseCd: string; tempLocationCd: string; lotNo: string; legacyStatus: number
  inboundNo?: string | null; outboundNo?: string | null; inTxnNo?: string | null; outTxnNo?: string | null
  fromDock?: string | null; toDock?: string | null; executedAt?: string | null; createDate: string
  supplierCdReadable: boolean; customerCdReadable: boolean; operatorCdReadable: boolean; remarksReadable: boolean
  supplierCd?: string | null; customerCd?: string | null; operatorCd?: string | null; remarks?: string | null
}
export interface CrossDockPage {
  version: string; coverage: 'AUTHORIZED_LEGACY_OBSERVATION'; writesAvailable: false; writeBlocker: string
  total: number; page: number; pageSize: number; items: CrossDockObservation[]
}
