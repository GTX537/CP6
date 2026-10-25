import type { MachineDto } from './mes'

export interface MachineCreationScope { tenantId: string; actorId: string }
export interface MachineCreationContext extends MachineCreationScope {
  authorityVersion: string
  allowsUnbound: boolean
  departments: { id: string; code: string; name: string }[]
}
export interface MachineCreationCommand {
  operationId: string
  expectedAuthorityVersion: string
  departmentId: string | null
  machine: MachineDto
}
export interface MachineCreationResult { operationId: string; machineId: string; machineCd: string; departmentId: string | null }
export interface MachineCreationOperation { operationId: string; status: 'UNKNOWN' | 'COMMITTED' | 'CLOSED'; result?: MachineCreationResult | null }
