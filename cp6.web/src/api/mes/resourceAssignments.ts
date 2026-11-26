import http from '../http'
import { resourceApi, type ResourceEnvelope, type ResourceOperation, type ResourceReference, type ResourceResult } from './resourceSources'

export interface AssignmentInterval { workCenter: ResourceReference; validFrom: string; validTo: string | null }
export interface AssignmentPlan { machine: ResourceReference; intervals: AssignmentInterval[] }
export interface AssignmentVersion {
  tenantId: string
  reference: { owner: 'Main.Mes.ResourceAssignment.v1'; id: string; version: string; digest: string }
  isCurrent: boolean
  plan: AssignmentPlan
  originalJson: string
  currentWorkCenter: ResourceReference | null
}
export interface AssignmentCommand { operationId: string; expectedVersion: string; plan: AssignmentPlan; reason: string }

const root = '/mes/resource-assignments/owner-v1'
export const assignmentApi = {
  scope: resourceApi('MACHINE').scope,
  read: (id: string, version?: string) => http.get<any, ResourceEnvelope<AssignmentVersion | null>>(
    `${root}/${encodeURIComponent(id)}`, { params: { version } }),
  save: (input: AssignmentCommand) => http.post<any, ResourceEnvelope<ResourceResult>>(`${root}/commands`, input),
  operation: (id: string, close: boolean) => close
    ? http.post<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}/close`, {})
    : http.get<any, ResourceEnvelope<ResourceOperation>>(`${root}/operations/${encodeURIComponent(id)}`),
}
