import http from '../http'
import type {
  PaperRoll, PaperRollSearchQuery, SlitRequest,
  InkLot, InkLotSearchQuery, InkColorMatchHistory, MixInkRequest,
  Pallet, PalletSearchQuery,
  VmiCustomerSummary, VmiStockDetail, VmiBilling,
  WmsApi,
} from '@/types/wms'

export const paperRollApi = {
  search(q: PaperRollSearchQuery = {}) {
    return http.get<any, WmsApi<PaperRoll[]>>('/wms/paper-roll', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<PaperRoll>>(`/wms/paper-roll/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ rollNo: string }>>('/wms/paper-roll', dto)
  },
  consume(no: string, lengthM: number) {
    return http.post<any, WmsApi<void>>(`/wms/paper-roll/${encodeURIComponent(no)}/consume`, { lengthM })
  },
  match(paperGrade: string, widthMm: number, grainDirection: string, requiredLengthM: number) {
    return http.get<any, WmsApi<PaperRoll[]>>('/wms/paper-roll/match', { params: { paperGrade, widthMm, grainDirection, requiredLengthM } })
  },
  slit(req: SlitRequest) {
    return http.post<any, WmsApi<{ createdRolls: string[] }>>('/wms/paper-roll/slit', req)
  },
  dispose(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/paper-roll/${encodeURIComponent(no)}/dispose`)
  },
}

export const inkApi = {
  searchLots(q: InkLotSearchQuery = {}) {
    return http.get<any, WmsApi<InkLot[]>>('/wms/ink/lots', { params: q })
  },
  getLot(no: string) {
    return http.get<any, WmsApi<InkLot>>(`/wms/ink/lots/${encodeURIComponent(no)}`)
  },
  createLot(dto: any) {
    return http.post<any, WmsApi<{ inkLotNo: string }>>('/wms/ink/lots', dto)
  },
  openLot(no: string, newExpiryDate?: string) {
    return http.post<any, WmsApi<void>>(`/wms/ink/lots/${encodeURIComponent(no)}/open`, { newExpiryDate })
  },
  mix(req: MixInkRequest) {
    return http.post<any, WmsApi<{ newLotNo: string }>>('/wms/ink/lots/mix', req)
  },
  searchMatches(customerCd?: string, colorCode?: string) {
    return http.get<any, WmsApi<InkColorMatchHistory[]>>('/wms/ink/matches', { params: { customerCd, colorCode } })
  },
  recordMatch(dto: any) {
    return http.post<any, WmsApi<{ matchNo: string }>>('/wms/ink/matches', dto)
  },
}

export const palletApi = {
  search(q: PalletSearchQuery = {}) {
    return http.get<any, WmsApi<Pallet[]>>('/wms/pallet', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<Pallet>>(`/wms/pallet/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ palletNo: string }>>('/wms/pallet', dto)
  },
  update(no: string, dto: any) {
    return http.put<any, WmsApi<void>>(`/wms/pallet/${encodeURIComponent(no)}`, dto)
  },
  completeBuilding(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/pallet/${encodeURIComponent(no)}/complete-building`)
  },
  moveToShipping(no: string, toLocationCd: string) {
    return http.post<any, WmsApi<void>>(`/wms/pallet/${encodeURIComponent(no)}/move-to-shipping`, { toLocationCd })
  },
  markShipped(no: string, outboundNo: string) {
    return http.post<any, WmsApi<void>>(`/wms/pallet/${encodeURIComponent(no)}/mark-shipped`, { outboundNo })
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/pallet/${encodeURIComponent(no)}`)
  },
}

export const vmiApi = {
  customers(customerCd?: string) {
    return http.get<any, WmsApi<VmiCustomerSummary[]>>('/wms/vmi/customers', { params: { customerCd } })
  },
  details(customerCd: string) {
    return http.get<any, WmsApi<VmiStockDetail[]>>(`/wms/vmi/customers/${encodeURIComponent(customerCd)}/details`)
  },
  billings(customerCd?: string, yearMonth?: string, confirmed?: boolean) {
    return http.get<any, WmsApi<VmiBilling[]>>('/wms/vmi/billings', { params: { customerCd, yearMonth, confirmed } })
  },
  calculate(yearMonth: string, dailyStorageRate: number) {
    return http.post<any, WmsApi<{ upserted: number }>>('/wms/vmi/billings/calculate', { yearMonth, dailyStorageRate })
  },
  confirm(billingNo: string) {
    return http.post<any, WmsApi<void>>(`/wms/vmi/billings/${encodeURIComponent(billingNo)}/confirm`)
  },
}
