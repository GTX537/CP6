import type { WmsApi, WmsPaged } from '@/types/wms/wms'

/** Do not turn a missing/truncated response into a successful empty authorized list. */
export function readWmsPage<T>(response: WmsApi<WmsPaged<T>>, page: number, size: number) {
  const data = response?.data
  if (response?.code !== 0 || !data || !Array.isArray(data.items)
    || !Number.isSafeInteger(data.total) || data.total < 0 || data.total > 2147483647
    || data.page !== page || data.pageSize !== size
    || data.items.length !== Math.min(size, Math.max(0, data.total - (page - 1) * size))) {
    throw new Error('Unavailable page')
  }
  return { rows: data.items, total: data.total }
}
