import http from '../http'

const designRoot = '/space/design/v1'
const planningRoot = '/space/planning/v1'

export interface SpaceDesignModel {
  id: string
  siteId: string
  mode: string
  cutoverState: string
  activeDraftVersionId?: string | null
  currentPublishedVersionId?: string | null
  rowVersion: string
}

export interface SpacePlanningScenarioBranch {
  branchId: string
  siteId: string
  modelId: string
  basePublishedVersionId: string
  baseVersionNo: string
  scenarioVersionId: string
  scenarioVersionNo: string
  name: string
  branchStatus: string
  scenarioVersionStatus: string
  cloneJobId: string
  cloneJobStatus: string
  createdAtUtc: string
  createdBy: string
  definitionVersion: string
  productionIsolated: boolean
  limitations: string[]
}

export interface SpacePlanningScenarioBranchList {
  items: SpacePlanningScenarioBranch[]
  isTruncated: boolean
}

export interface CreateSpacePlanningScenarioBranchResponse {
  outcome: 'Created' | 'Duplicate'
  branch: SpacePlanningScenarioBranch
}

export const planningScenarioApi = {
  getModel(siteId: string) {
    return http.get<unknown, SpaceDesignModel>(
      `${designRoot}/sites/${encodeURIComponent(siteId)}/model`,
    )
  },
  list(siteId: string, limit = 50) {
    return http.get<unknown, SpacePlanningScenarioBranchList>(
      `${planningRoot}/sites/${encodeURIComponent(siteId)}/scenario-branches`,
      { params: { limit } },
    )
  },
  create(
    siteId: string,
    branchId: string,
    request: { basePublishedVersionId: string; name: string },
  ) {
    return http.put<unknown, CreateSpacePlanningScenarioBranchResponse>(
      `${planningRoot}/sites/${encodeURIComponent(siteId)}` +
        `/scenario-branches/${encodeURIComponent(branchId)}`,
      request,
    )
  },
}
