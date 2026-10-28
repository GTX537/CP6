import http from '../http'
import type { EngineeringReference } from './engineeringProducts'
import type { RevisionOption } from './engineeringRevisions'
import type { EngineeringCheckPolicy } from './engineeringSourceCheck'
export interface ConfigurationEnvelope<T> { code: number; data: T }
export interface ConfigurationScope { tenantId: string; actorId: string }
export interface ConfigurationCreation extends ConfigurationScope { authorityVersion: string; allowsUnbound: boolean; departments: { id: string; code: string; name: string }[] }
export interface OptionDefinition { name: string; type: RevisionOption['type']; required: boolean | null; allowAnyValue: boolean | null; allowedValues: RevisionOption[] }
export interface OptionConstraint { id: string; kind: 'MUTEX' | 'REQUIRES'; when: RevisionOption; then: RevisionOption; reason: string }
export interface ConfigurationBody { scope: 'ENGINEERING_OPTIONS_ONLY' | 'ENGINEERING_OPTIONS_AND_CHECKS_V1'; name: string; productRef: EngineeringReference; definitions: OptionDefinition[]; constraints: OptionConstraint[]; engineeringChecks?: EngineeringCheckPolicy | null }
export interface ConfigurationSnapshot { tenantId: string; reference: EngineeringReference; etag: string; isCurrent: boolean; isActive: boolean; activeRef: EngineeringReference | null; state: 'DRAFT' | 'UNDER_REVIEW' | 'APPROVED' | 'WITHDRAWN'; body: ConfigurationBody; action: string; reason: string; parentVersion: string | null; actorId: string; recordedAt: string; originalJson: string; missing: string[] }
export interface ConfigurationCommand { operationId: string; ruleId: string; action: 'CREATE' | 'REVISE' | 'SUBMIT' | 'APPROVE' | 'RETURN' | 'WITHDRAW'; expectedVersion?: string; expectedEtag?: string; expectedActiveRef?: EngineeringReference | null; expectedAuthorityVersion?: string; departmentId?: string | null; body?: ConfigurationBody; reason: string }
export type ConfigurationOperationKind = 'CONFIGURATION_RULE'
export interface ConfigurationResult { operationId: string; kind: ConfigurationOperationKind; objectId: string; version: string; digest: string }
export interface ConfigurationOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: ConfigurationResult | null }
export interface ConfigurationOptionCheck { scope: ConfigurationBody['scope']; ruleRef: EngineeringReference; optionsDigest: string; status: 'OPTIONS_VALIDATED' | 'UNKNOWN' | 'CONFLICT' | 'NO_MATCH'; findings: { code: string; name: string; constraintId?: string | null }[]; observedAt: string }
const root = '/configuration-rules/owner-v1'
export const configurationApi = {
  scope: () => http.get<any, ConfigurationEnvelope<ConfigurationScope>>(`${root}/scope`),
  creationContext: () => http.get<any, ConfigurationEnvelope<ConfigurationCreation>>(`${root}/creation-context`),
  list: (search = '', page = 1) => http.get<any, ConfigurationEnvelope<ConfigurationSnapshot[]>>(root, { params: { search, page } }),
  read: (id: string, version?: string) => http.get<any, ConfigurationEnvelope<ConfigurationSnapshot>>(`${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (body: ConfigurationCommand) => http.post<any, ConfigurationEnvelope<ConfigurationResult>>(`${root}/commands`, body),
  operation: (id: string, close: boolean) => close ? http.post<any, ConfigurationEnvelope<ConfigurationOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, ConfigurationEnvelope<ConfigurationOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
