export const lotTraceVersion = 'LOT-TRACE-OWNER/1'
export interface LotTraceNode {
  txnNo: string; txnType: string; txnAt: string; warehouseCd: string; locationCd: string; qty: string
  relatedNo?: string | null; relatedType?: string | null; operatorCd?: string | null; remark?: string | null
}
export interface LotCustomer { outboundNo: string; webOrderNo?: string | null; customerCd?: string | null; customerName?: string | null; qty: string; shippedAt: string }
export interface LotSupplier { receiptNo: string; inboundNo?: string | null; supplierCd?: string | null; supplierName?: string | null; qty: string; receivedAt: string }
export interface LotSnapshot {
  version: typeof lotTraceVersion; coverage: 'AUTHORIZED_LOT_OBSERVATION'; productCd: string; lotNo: string; direction: 'FORWARD' | 'BACKWARD'
  operatorReadable: boolean; remarkReadable: boolean; customerNameReadable: boolean; supplierNameReadable: boolean
  stock: { recallVersion: string; physicalQty: string; availableQty: string; stockCount: number; recallState: 'NONE' | 'MIXED' | 'ALL'; expiryDate?: string | null } | null
  nodes: LotTraceNode[]; customers: LotCustomer[]; suppliers: LotSupplier[]
}
export interface LotQuery { productCd: string; lotNo: string; direction: 'FORWARD' | 'BACKWARD' }
export interface LotRecallCommand { productCd: string; lotNo: string; recallVersion: string; flag: boolean }
export interface LotRecallResult { version: typeof lotTraceVersion; productCd: string; lotNo: string; flag: boolean; affectedStocks: number }
