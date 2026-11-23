import http from '../http'

export interface ForecastPin { kind: 'FORM' | 'FLOW'; location: string; versionId: string }
export type ForecastInput =
  | { draftVersionId: string; expectedVersion: string; varsJson: string; draftDependencies: ForecastPin[] }
  | { publishedVersionId: string; varsJson: string }
export interface FlowForecast {
  mode: 'EXPLANATION_ONLY'
  isExecutionPermit: false
  definitionId: string
  source: 'DRAFT_SIMULATION' | 'PUBLISHED_PIN'
  versionId: string
  draftRevision: string | null
  starterBasis: 'CURRENT_ACTOR_AS_STARTER'
  knowledgeCutUtc: string
  coverage: 'COMPLETE_FOR_SUPPORTED_GRAPH' | 'PARTIAL'
  visitedNodeCount: number
  totalNodeCount: number
  traversedEdgeCount: number
  totalEdgeCount: number
  nodes: { nodeId: string; type: string; stages: { stageIndex: number; state: 'RESOLVED_AT_CUT' | 'ARRIVAL_TIME' | 'REDACTED'; people: { id: string; displayName: string }[] }[] }[]
  edges: { index: number; from: string; to: string; condition: 'TRUE' | 'FALSE' | 'UNKNOWN'; traversed: boolean }[]
  unvisited: { path: string; reason: string }[]
  diagnostics: { path: string; code: string; evidenceState: string }[]
  dependencies: { kind: string; location: string; versionId: string | null; evidenceState: string }[]
}

export const flowSimulationApi = {
  forecast: async (key: string, input: ForecastInput, signal: AbortSignal, download = false) => {
    const path = '/oa/v2/definitions/FLOW/' + encodeURIComponent(key) + '/forecast' + (download ? '/export' : '')
    const value: any = await http.post(path, input, { signal })
    return (value?.data ?? value) as FlowForecast
  },
}
