import http from '../http'
import type { ResourceEnvelope, ResourceKind, ResourceOperation, ResourceReference, ResourceResult, ResourceScope, ResourceVersion } from './resourceSources'
import type { UomReference } from '../erp/uomProfiles'
import type { TimeReference } from '../erp/uomTimeBases'
export interface CapabilityReference { owner: 'Main.Mes.ResourceCapability.v1'; kind: ResourceKind; id: string; version: string; digest: string }
export interface CapabilityItem { key: string; valueKind: 'DECIMAL' | 'ENUM'; decimalValue: string | null; enumValue: string | null; unit: UomReference | null; dimension: string; validFrom: string; validTo: string | null; basis: string }
export interface CapabilityBody { source: ResourceReference; timeUnit: UomReference | null; timeBasis: TimeReference | null; outputUnit: UomReference | null; capabilities: CapabilityItem[]; basis: string }
export interface CapabilitySnapshot { tenantId: string; reference: CapabilityReference; isCurrent: boolean; body: CapabilityBody; actorId: string; recordedAt: string; originalJson: string; missing: string[]; qualification: 'DECLARED_NOT_QUALIFIED' }
export interface CapabilityEditor { resource: ResourceVersion; expectedVersion: string; current: CapabilitySnapshot | null }
export interface CapabilityCommand { operationId: string; expectedVersion: string; body: CapabilityBody; reason: string }
export function capabilityApi(kind: ResourceKind) {
  const root = `/mes/resource-capabilities/owner-v1/${kind}`
  return {
    scope: () => http.get<any, ResourceEnvelope<ResourceScope>>(`${root}/scope`),
    editor: (id: string) => http.get<any, ResourceEnvelope<CapabilityEditor>>(`${root}/${encodeURIComponent(id)}/editor`),
    read: (id: string, version?: string) => http.get<any, ResourceEnvelope<CapabilitySnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
    save: (input: CapabilityCommand) => http.post<any, ResourceEnvelope<ResourceResult>>(`${root}/commands`, input),
    operation: (id: string, close: boolean) => close ? http.post<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
      : http.get<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
  }
}
