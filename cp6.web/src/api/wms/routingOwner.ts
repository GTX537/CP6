import http from '../http'
import type { WmsApi } from '@/types/wms/outboundRouting'
import type { RoutingPage, RoutingObservation, RoutingQuery } from '@/types/wms/routingOwner'
const base = '/wms/outbound-routing/owner/v1/rules'
export const routingOwnerApi = {
  search: (q: RoutingQuery) => http.get<any, WmsApi<RoutingPage>>(base, { params: q }),
  get: (id: string) => http.get<any, WmsApi<RoutingObservation>>(`${base}/${encodeURIComponent(id)}`),
}
