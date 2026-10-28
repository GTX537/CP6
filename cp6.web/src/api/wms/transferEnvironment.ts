import http from '../http'
import type { EngineeringEnvelope, EngineeringReference } from '../plm/engineeringProducts'
export interface TransferDeployment { environmentId: string; mode: 'PRODUCTION' | 'SIMULATION'; sandboxScopeId: string | null }
export interface TransferRegistration { reference: EngineeringReference; original: { tenantId: string; actorId: string; deployment: TransferDeployment; enabled: boolean; profile: string; owners: string[]; reason: string; recordedAt: string }; originalJson: string }
export interface TransferEnvironmentContext { tenantId: string; actorId: string; deployment: TransferDeployment; version: string; registration: TransferRegistration | null; availableOwners: string[]; qualification: string }
export interface TransferEnvironmentCommand { operationId: string; expectedVersion: string; enabled: boolean; reason: string }
export interface TransferEnvironmentResult { operationId: string; reference: EngineeringReference; enabled: boolean; changed: boolean }
export interface TransferEnvironmentOperation { operationId: string; state: 'COMMITTED' | 'UNKNOWN' | 'CLOSED'; result?: TransferEnvironmentResult | null }
const root = '/wms/transfer/environment-v1'
export const transferEnvironmentApi = {
  context: () => http.get<any, EngineeringEnvelope<TransferEnvironmentContext>>(root),
  history: (version: string) => http.get<any, EngineeringEnvelope<TransferRegistration>>(`${root}/versions/${encodeURIComponent(version)}`),
  save: (command: TransferEnvironmentCommand) => http.post<any, EngineeringEnvelope<TransferEnvironmentResult>>(`${root}/commands`, command),
  operation: (id: string, close: boolean) => close
    ? http.post<any, EngineeringEnvelope<TransferEnvironmentOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, EngineeringEnvelope<TransferEnvironmentOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
