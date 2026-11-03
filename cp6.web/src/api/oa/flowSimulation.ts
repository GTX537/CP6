import http from '../http'
import type { DependencyReference } from './flowPublication'

export interface FlowSimulation {
  mode: 'EXPLANATION_ONLY'; definitionId: string; draftVersionId: string; expectedVersion: string; assumptions: string[]
  nodes: { nodeId: string; type: string; stages: { stageIndex: number; countersign: string; state: string; approverIds: string[] }[] }[]
  edges: { index: number; from: string; to: string }[]
  diagnostics: { path: string; code: string; evidenceState: string }[]
  dependencies: DependencyReference[]
}
export const flowSimulationApi = {
  simulate: async (key: string, input: { draftVersionId: string; expectedVersion: string; varsJson: string }, signal?: AbortSignal) => {
    const value: any = await http.post(`/oa/v2/definitions/FLOW/${encodeURIComponent(key)}/simulate`, input, { signal })
    return (value?.data ?? value) as FlowSimulation
  },
}
