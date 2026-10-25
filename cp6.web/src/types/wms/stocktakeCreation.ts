import type { StockTakePlanRequest } from './wms'

export interface StocktakeCreationScope { tenantId: string; actorId: string }
export interface StocktakeCreationContext extends StocktakeCreationScope {
  authorityVersion: string
  allowsUnbound: boolean
  departments: { id: string; code: string; name: string }[]
}
export interface StocktakeCreationCommand {
  operationId: string
  expectedAuthorityVersion: string
  departmentId: string | null
  plan: StockTakePlanRequest
}
export interface StocktakeCreationResult { operationId: string; stockTakeId: string; stockTakeNo: string; departmentId: string | null }
export interface StocktakeCreationOperation { operationId: string; status: 'UNKNOWN' | 'COMMITTED' | 'CLOSED'; result?: StocktakeCreationResult | null }
