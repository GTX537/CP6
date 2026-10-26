import http from '../http'
import type { EngineeringEnvelope, EngineeringReference, EngineeringScope } from './engineeringProducts'
import type { PlateSourceVersion } from '../erp/plateMoldSources'
import type { TechnicalControl } from '../erp/plateMoldWriter'
export interface ToolRequirements { productRef: EngineeringReference; productApplicability: string; processRequirements: string; configurationApplicability: string; technicalRequirements: string; documentRequirements: string }
export interface ToolSpecification { reference: EngineeringReference; source: PlateSourceVersion['reference']; nativeRowVersion: string; requirements: ToolRequirements; reason: string; actorId: string; recordedAt: string; originalJson: string; state: 'DRAFT'; approval: 'NOT_REQUESTED'; freeze: 'NOT_REQUESTED'; physicalQualification: 'UNKNOWN' }
export interface ToolSpecificationView { source: PlateSourceVersion; currentControl: TechnicalControl; specification: ToolSpecification | null; isCurrent: boolean; sourceMatches: boolean; productMatches: boolean }
export interface ToolSpecificationCommand { operationId: string; action: 'CREATE' | 'REVISE'; source: PlateSourceVersion['reference']; expectedNativeRowVersion: string; expectedTechnicalControl: TechnicalControl; requirements: ToolRequirements; reason: string }
export interface ToolSpecificationResult { operationId: string; kind: 'TOOL_SPECIFICATION_DRAFT'; objectId: string; version: string; digest: string }
export interface ToolSpecificationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ToolSpecificationResult | null }
const root = '/tool-specifications/owner-v1'
export const toolSpecificationApi = {
  scope: () => http.get<any, EngineeringEnvelope<EngineeringScope>>(`${root}/scope`),
  list: (search = '', page = 1) => http.get<any, EngineeringEnvelope<ToolSpecificationView[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, EngineeringEnvelope<ToolSpecificationView>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: ToolSpecificationCommand) => http.post<any, EngineeringEnvelope<ToolSpecificationResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => { const path = `${root}/operations/${encodeURIComponent(id)}`; return close ? http.post<any, EngineeringEnvelope<ToolSpecificationOperation>>(`${path}/close`, {}) : http.get<any, EngineeringEnvelope<ToolSpecificationOperation>>(path) },
}
