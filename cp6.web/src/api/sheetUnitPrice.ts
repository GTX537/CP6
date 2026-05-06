import http from './http'
import type {
  ApiResult,
  SheetUnitPriceDto,
  SheetPriceImportResultDto,
  SheetPriceBatchUpdateDto,
  SheetPriceQueryDto,
} from '@/types/sheetUnitPrice'

export const sheetUnitPriceApi = {
  search(query: SheetPriceQueryDto) {
    return http.get<any, ApiResult<SheetUnitPriceDto[]>>('/sheet-unit-prices/list', {
      params: query,
    })
  },

  /** Excel 取込（multipart） */
  importExcel(file: File, baseDate: string, baseCd: string, importDiv: number) {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('baseDate', baseDate)
    fd.append('baseCd', baseCd)
    fd.append('importDiv', String(importDiv))
    return http.post<any, ApiResult<SheetPriceImportResultDto>>(
      '/sheet-unit-prices/import',
      fd,
      { headers: { 'Content-Type': 'multipart/form-data' } }
    )
  },

  batchUpdate(req: SheetPriceBatchUpdateDto) {
    return http.put<any, ApiResult<{ updated: number }>>(
      '/sheet-unit-prices/batch-update',
      req
    )
  },
}
