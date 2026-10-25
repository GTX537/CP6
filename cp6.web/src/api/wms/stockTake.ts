import type { StocktakeCreationContext, StocktakeCreationScope, StocktakeCreationCommand, StocktakeCreationResult, StocktakeCreationOperation } from '@/types/wms/stocktakeCreation'
import http from '../http'
import type {
  StockTake, StockTakePlanRequest, StockTakeCountInput, StockTakeSearchQuery, WmsApi,
} from '@/types/wms/wms'

export const stockTakeApi = {
  creationContext() { return http.get<any, WmsApi<StocktakeCreationContext>>('/wms/stock-take/creation-context') },
  creationScope() { return http.get<any, WmsApi<StocktakeCreationScope>>('/wms/stock-take/operations/scope') },
  createOperation(request: StocktakeCreationCommand) { return http.post<any, WmsApi<StocktakeCreationResult>>('/wms/stock-take/operations', request) },
  readCreation(id: string) { return http.get<any, WmsApi<StocktakeCreationOperation>>('/wms/stock-take/operations/' + encodeURIComponent(id)) },
  closeCreation(id: string) { return http.post<any, WmsApi<StocktakeCreationOperation>>('/wms/stock-take/operations/' + encodeURIComponent(id) + '/close') },
  search(q: StockTakeSearchQuery = {}) {
    return http.get<any, WmsApi<StockTake[]>>('/wms/stock-take', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<StockTake>>(`/wms/stock-take/${encodeURIComponent(no)}`)
  },
  createPlan(req: StockTakePlanRequest) {
    return http.post<any, WmsApi<{ stockTakeNo: string }>>('/wms/stock-take/plan', req)
  },
  startCount(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/start-count`)
  },
  updateCounts(no: string, inputs: StockTakeCountInput[]) {
    return http.put<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/counts`, inputs)
  },
  submit(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/submit`)
  },
  approve(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/approve`)
  },
  cancel(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/stock-take/${encodeURIComponent(no)}/cancel`)
  },
}
