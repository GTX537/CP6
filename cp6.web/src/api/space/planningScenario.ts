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

export interface CreateSpacePlanningHistoricalTaskRequest {
  taskToken: string
  workerToken?: string | null
  taskType: string
  outcome: string
  originalCreatedAtUtc: string
  originalCompletedAtUtc: string
  fromLocationLogicalId?: string | null
  toLocationLogicalId: string
  quantity: number
}

export interface CreateSpacePlanningHistoricalDatasetRequest {
  name: string
  historicalFromUtc: string
  historicalToUtc: string
  replayStartUtc: string
  replaySpeedFactor: number
  sourceDatasetHash: string
  confirmDeidentified: boolean
  tasks: CreateSpacePlanningHistoricalTaskRequest[]
}

export interface SpacePlanningHistoricalDatasetSummary {
  datasetId: string
  branchId: string
  scenarioVersionId: string
  name: string
  taskCount: number
  historicalFromUtc: string
  historicalToUtc: string
  replayStartUtc: string
  replayEndUtc: string
  replaySpeedFactor: number
  createdAtUtc: string
}

export interface SpacePlanningHistoricalDatasetList {
  items: SpacePlanningHistoricalDatasetSummary[]
  isTruncated: boolean
}

export interface SpacePlanningHistoricalTask
  extends CreateSpacePlanningHistoricalTaskRequest {
  sequenceNo: number
  replayCreatedAtUtc: string
  replayCompletedAtUtc: string
}

export interface SpacePlanningReplayClock {
  historicalFromUtc: string
  historicalToUtc: string
  replayStartUtc: string
  replayEndUtc: string
  replaySpeedFactor: number
  historicalDurationSeconds: number
  replayDurationSeconds: number
}

export interface SpacePlanningHistoricalDataset {
  datasetId: string
  branchId: string
  siteId: string
  scenarioVersionId: string
  name: string
  taskCount: number
  sourceDatasetHash: string
  definitionVersion: string
  deidentificationVersion: string
  deidentified: boolean
  productionWriteAllowed: boolean
  replayClock: SpacePlanningReplayClock
  tasks: SpacePlanningHistoricalTask[]
  createdAtUtc: string
  createdBy: string
  limitations: string[]
}

export interface CreateSpacePlanningHistoricalDatasetResponse {
  outcome: 'Created' | 'Duplicate'
  dataset: SpacePlanningHistoricalDataset
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

function historicalDatasetRoot(siteId: string, branchId: string) {
  return `${planningRoot}/sites/${encodeURIComponent(siteId)}` +
    `/scenario-branches/${encodeURIComponent(branchId)}/historical-datasets`
}

export const planningDatasetApi = {
  list(siteId: string, branchId: string, limit = 50) {
    return http.get<unknown, SpacePlanningHistoricalDatasetList>(
      historicalDatasetRoot(siteId, branchId),
      { params: { limit } },
    )
  },
  get(siteId: string, branchId: string, datasetId: string) {
    return http.get<unknown, SpacePlanningHistoricalDataset>(
      `${historicalDatasetRoot(siteId, branchId)}/${encodeURIComponent(datasetId)}`,
    )
  },
  create(
    siteId: string,
    branchId: string,
    datasetId: string,
    request: CreateSpacePlanningHistoricalDatasetRequest,
  ) {
    return http.put<unknown, CreateSpacePlanningHistoricalDatasetResponse>(
      `${historicalDatasetRoot(siteId, branchId)}/${encodeURIComponent(datasetId)}`,
      request,
    )
  },
}
