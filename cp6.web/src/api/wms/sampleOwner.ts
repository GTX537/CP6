import http from '../http'
import type { WmsApi } from '@/types/wms/outboundRouting'
import type { SamplePage, SampleObservation, SampleQuery } from '@/types/wms/sampleOwner'
const base = '/wms/sample-stock/owner/v1/records'
export const sampleOwnerApi = {
  search: (q: SampleQuery) => http.get<any, WmsApi<SamplePage>>(base, { params: q }),
  get: (id: string) => http.get<any, WmsApi<SampleObservation>>(`${base}/${encodeURIComponent(id)}`),
}
