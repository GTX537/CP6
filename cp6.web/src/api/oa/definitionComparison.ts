import http from '../http'
import type { DependencyReference } from './flowPublication'
export interface ComparedVersion {
  versionId: string; version: number; nameSnapshot: string | null; publishedAtUtc: string; publisherRef: string
  schemaDigest: string | null; evidenceState: string; dependencyRefs: DependencyReference[]; status: 'Published'
}
export interface DefinitionComparison {
  kind: 'FLOW' | 'FORM'; key: string; left: ComparedVersion; right: ComparedVersion
  scope: 'ALL_FIELDS' | 'VISIBLE_FIELDS_ONLY'; omittedFields: string[]; dependenciesVisible: boolean
  page: number; pageSize: number; totalChanges: number; hasMore: boolean
  changes: { path: string; category: string; change: string; beforeJson: string | null; afterJson: string | null }[]
}
export const definitionComparisonApi = {
  compare: async (kind: 'FLOW' | 'FORM', key: string, leftVersionId: string, rightVersionId: string, page: number, signal?: AbortSignal) => {
    const response = await http.get(`/oa/v2/definitions/${kind}/${encodeURIComponent(key)}/compare`, { params: { leftVersionId, rightVersionId, page }, signal })
    return (response?.data ?? response) as DefinitionComparison
  },
}
