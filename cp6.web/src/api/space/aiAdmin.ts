import http from '../http'

const root = '/space/design/v1'

export type SpaceAiDataPolicy = 'Disabled' | 'MetadataOnly' | 'StructuredFeatures'
export type SpaceAiProviderKind = 'Mock' | 'Local' | 'External'

export interface SpaceAiApprovedProvider {
  alias: string
  kind: SpaceAiProviderKind
}

export interface SpaceAiPolicy {
  version: number
  dataPolicy: SpaceAiDataPolicy
  allowedSiteIds: string[]
  allowedProviderAliases: string[]
  maxConcurrentRuns: number
  externalProviderEnabled: boolean
  dailyBudgetMinor?: number | null
  monthlyBudgetMinor?: number | null
  currency?: string | null
  approvedProviders: SpaceAiApprovedProvider[]
  updatedAtUtc?: string | null
  updatedBy?: string | null
}

export interface UpdateSpaceAiPolicyRequest {
  expectedVersion: number
  dataPolicy: SpaceAiDataPolicy
  allowedSiteIds: string[]
  allowedProviderAliases: string[]
  maxConcurrentRuns: number
  externalProviderEnabled: boolean
  dailyBudgetMinor?: number | null
  monthlyBudgetMinor?: number | null
  currency?: string | null
}

export interface UpdateSpaceAiPolicyResponse {
  policy: SpaceAiPolicy
  idempotentReplay: boolean
}

export interface SpaceAiUsageItem {
  id: string
  runId: string
  providerAlias: string
  providerModel: string
  inputUnits: number
  outputUnits: number
  estimatedCostMinor: number
  actualCostMinor?: number | null
  currency?: string | null
  latencyMs: number
  outcome: 'Unknown' | 'Succeeded' | 'Failed'
  recordedAtUtc: string
}

export interface SpaceAiBudgetBalance {
  limitMinor?: number | null
  consumedMinor: number
  remainingMinor?: number | null
  currency?: string | null
}

export interface SpaceAiUsageSummary {
  totalRuns: number
  inputUnits: number
  outputUnits: number
  estimatedCostMinor: number
  actualCostMinor: number
  hasUnpricedUsage: boolean
  dailyBudget: SpaceAiBudgetBalance
  monthlyBudget: SpaceAiBudgetBalance
}

export interface SpaceAiUsagePage {
  items: SpaceAiUsageItem[]
  total: number
  page: number
  pageSize: number
  summary: SpaceAiUsageSummary
}

export interface SpaceAiUsageQuery {
  fromUtc?: string
  toUtc?: string
  providerAlias?: string
  outcome?: string
  page?: number
  pageSize?: number
}

export const spaceAiAdminApi = {
  getPolicy() {
    return http.get<unknown, SpaceAiPolicy>(`${root}/ai-policy`)
  },
  updatePolicy(request: UpdateSpaceAiPolicyRequest, idempotencyKey: string) {
    return http.put<unknown, UpdateSpaceAiPolicyResponse>(
      `${root}/ai-policy`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
  getUsage(query: SpaceAiUsageQuery) {
    return http.get<unknown, SpaceAiUsagePage>(`${root}/ai-usage`, {
      params: query,
    })
  },
}
