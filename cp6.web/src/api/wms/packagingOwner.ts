import http from '@/api/http'
import type { WmsApi } from '@/types/wms/wms'
import type { PackagingPage, PackagingQuery, PackagingRecord } from '@/types/wms/packagingOwner'
const base = '/wms/shipping/owner/v1/packages'
export const packagingOwnerApi = {
  search: (q: PackagingQuery) => http.get<any, WmsApi<PackagingPage>>(base, { params: q }),
  get: (number: string) => http.get<any, WmsApi<PackagingRecord>>(`${base}/${encodeURIComponent(number)}`),
}
