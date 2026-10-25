import type { WorkOrderDto } from './mes'
export interface WorkOrderCreationScope { tenantId: string; actorId: string }
export interface WorkOrderCreationContext extends WorkOrderCreationScope {
  authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[]
}
export interface WorkOrderCreationCommand {
  operationId: string; expectedAuthorityVersion: string; departmentId: string | null; workOrder: WorkOrderDto
}
export interface WorkOrderCreationResult { operationId: string; workOrderId: string; workOrderNo: string; departmentId: string | null }
export interface WorkOrderCreationOperation { operationId: string; status: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: WorkOrderCreationResult | null }
