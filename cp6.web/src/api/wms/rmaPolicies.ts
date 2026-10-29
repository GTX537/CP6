import http from '../http'
import type { RmaRef } from './rmaFormalApplications'
export interface RmaPolicyDefinition { name: string; enabled: boolean; reasonOptions: { code: string; label: string }[]; allowedOutcomes: string[]; returnWindowDaysAfterDelivery: number | null; requireIndependentApprover: boolean }
export interface RmaPolicyVersion { reference: RmaRef; scope: { warehouseId: string; siteId: string }; policy: RmaPolicyDefinition; publishedAt: string; publishedBy: RmaRef; reason: string; previousRef: RmaRef | null }
export interface RmaPolicyLookup { state: 'NOT_CONFIGURED' | 'ENABLED' | 'DISABLED'; value: RmaPolicyVersion | null }
export interface RmaPolicyPublish { scopeOptionRef: RmaRef; expectedPolicyRef: RmaRef | null; policy: RmaPolicyDefinition; reason: string }
export interface RmaPolicyPending { version: 1; commandId: string; body: RmaPolicyPublish }
const root = '/wms/rma/v1/policies', headers = () => ({ 'X-Correlation-Id': crypto.randomUUID(), 'Cache-Control': 'no-store' })
export const rmaPolicyApi = {
  read: (scopeOptionRef: RmaRef, reference: RmaRef | null = null) => http.post<any, RmaPolicyLookup>(`${root}/read`, { scopeOptionRef, reference }, { headers: headers() }),
  publish: (pending: RmaPolicyPending) => http.post<any, RmaPolicyVersion>(`${root}/publish`, pending.body, { headers: { ...headers(), 'Idempotency-Key': pending.commandId } }),
  query: (pending: RmaPolicyPending) => http.post<any, { status: 'FOUND'; value: RmaPolicyVersion; canInferNoEffect: false } | { status: 'NOT_OBSERVED'; value: null; canInferNoEffect: false }>(`${root}/commands/query`, { scopeOptionRef: pending.body.scopeOptionRef, commandId: pending.commandId }, { headers: headers() }),
}
