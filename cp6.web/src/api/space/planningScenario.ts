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

export interface SpacePlanningSimulationLocationCapacityRequest {
  locationLogicalId: string
  quantityCapacity: number
  concurrentTaskCapacity: number
}

export interface CreateSpacePlanningSimulationRunRequest {
  name: string
  datasetId: string
  defaultQuantityCapacity: number
  defaultConcurrentTaskCapacity: number
  throughputWindowMinutes: number
  distanceCostPerMeter: number
  laborCostPerHour: number
  congestionCostPerTaskHour: number
  currencyCode: string
  locationCapacities: SpacePlanningSimulationLocationCapacityRequest[]
}

export interface SpacePlanningSimulationRunSummary {
  runId: string
  datasetId: string
  scenarioContentRevision: number
  name: string
  status: string
  currencyCode: string
  taskCount: number
  distanceCoveragePercent: number
  totalDistanceMeters: number
  overloadedLocationCount: number
  averageCompletedTasksPerHour: number
  totalCost: number
  createdAtUtc: string
}

export interface SpacePlanningSimulationRunList {
  items: SpacePlanningSimulationRunSummary[]
  isTruncated: boolean
}

export interface SpacePlanningSimulationLocationResult {
  locationLogicalId: string
  taskCount: number
  completedTaskCount: number
  totalQuantity: number
  distanceEligibleTaskCount: number
  totalDistanceMeters: number
  quantityCapacity: number
  concurrentTaskCapacity: number
  peakConcurrentTasks: number
  peakConcurrentQuantity: number
  capacityUtilizationPercent: number
  congestionSeconds: number
  congestionTaskSeconds: number
  isOverloaded: boolean
}

export interface SpacePlanningSimulationRun {
  runId: string
  siteId: string
  branchId: string
  scenarioVersionId: string
  scenarioContentRevision: number
  datasetId: string
  name: string
  status: string
  definitionVersion: string
  datasetRequestHash: string
  resultHash: string
  productionWriteAllowed: boolean
  highPrecisionPhysicalSimulation: boolean
  parameters: {
    defaultQuantityCapacity: number
    defaultConcurrentTaskCapacity: number
    throughputWindowMinutes: number
    distanceCostPerMeter: number
    laborCostPerHour: number
    congestionCostPerTaskHour: number
    currencyCode: string
    locationCapacityOverrideCount: number
  }
  distance: {
    geometryBasis: string
    taskCount: number
    eligibleTaskCount: number
    unknownTaskCount: number
    coveragePercent: number
    totalDistanceMeters: number
    averageEligibleTaskDistanceMeters?: number | null
  }
  congestion: {
    monitoredLocationCount: number
    overloadedLocationCount: number
    peakConcurrentTasks: number
    congestionSeconds: number
    congestionTaskSeconds: number
    congestionTaskHours: number
  }
  capacity: {
    monitoredLocationCount: number
    overloadedLocationCount: number
    peakUtilizationPercent: number
    quantityBasis: string
  }
  throughput: {
    completedTaskCount: number
    completedQuantity: number
    historicalWindowHours: number
    measurementWindowMinutes: number
    averageCompletedTasksPerHour: number
    peakCompletedTasksPerHour: number
    averageCompletedQuantityPerHour: number
    peakCompletedQuantityPerHour: number
  }
  cost: {
    currencyCode: string
    laborHours: number
    distanceCost: number
    laborCost: number
    congestionCost: number
    totalCost: number
    laborBasis: string
  }
  locationResults: SpacePlanningSimulationLocationResult[]
  locationResultsTruncated: boolean
  createdAtUtc: string
  createdBy: string
  limitations: string[]
}

export interface CreateSpacePlanningSimulationRunResponse {
  outcome: 'Created' | 'Duplicate'
  run: SpacePlanningSimulationRun
}

export interface CreateSpacePlanningComparisonRequest {
  name: string
  baselineRunId: string
  runIds: string[]
  minimumDistanceCoveragePercent: number
  maximumPeakCapacityUtilizationPercent: number
  maximumCongestionTaskHours: number
  maximumTotalCost?: number | null
}

export interface SpacePlanningComparisonRisk {
  code: string
  severity: 'Information' | 'Warning' | 'Critical'
}

export interface SpacePlanningComparisonEntry {
  sequenceNo: number
  runId: string
  branchId: string
  scenarioVersionId: string
  scenarioContentRevision: number
  runName: string
  runResultHash: string
  isBaseline: boolean
  metrics: {
    distanceCoveragePercent: number
    totalDistanceMeters: number
    congestionTaskSeconds: number
    congestionTaskHours: number
    overloadedLocationCount: number
    peakCapacityUtilizationPercent: number
    averageCompletedTasksPerHour: number
    peakCompletedTasksPerHour: number
    totalCost: number
  }
  deltaFromBaseline: {
    distanceMeters: number
    congestionTaskSeconds: number
    overloadedLocationCount: number
    peakCapacityUtilizationPercentagePoints: number
    averageCompletedTasksPerHour: number
    totalCost: number
  }
  risks: SpacePlanningComparisonRisk[]
}

export interface SpacePlanningComparison {
  comparisonId: string
  siteId: string
  modelId: string
  basePublishedVersionId: string
  baselineRunId: string
  name: string
  status: string
  definitionVersion: string
  requestHash: string
  comparisonHash: string
  sourceDatasetHash: string
  currencyCode: string
  historicalFromUtc: string
  historicalToUtc: string
  thresholds: {
    minimumDistanceCoveragePercent: number
    maximumPeakCapacityUtilizationPercent: number
    maximumCongestionTaskHours: number
    maximumTotalCost?: number | null
  }
  entries: SpacePlanningComparisonEntry[]
  automatedRanking: boolean
  productionWriteAllowed: boolean
  createdAtUtc: string
  createdBy: string
  limitations: string[]
}

export interface SpacePlanningComparisonSummary {
  comparisonId: string
  baselineRunId: string
  name: string
  currencyCode: string
  runCount: number
  riskCount: number
  createdAtUtc: string
}

export interface SpacePlanningComparisonList {
  items: SpacePlanningComparisonSummary[]
  isTruncated: boolean
}

export interface CreateSpacePlanningComparisonResponse {
  outcome: 'Created' | 'Duplicate'
  comparison: SpacePlanningComparison
}

export type SpacePlanningDecisionOutcome =
  | 'Selected'
  | 'Deferred'
  | 'RejectedAll'

export interface CreateSpacePlanningDecisionRequest {
  outcome: SpacePlanningDecisionOutcome
  selectedRunId?: string | null
  rationale: string
  supersedesDecisionId?: string | null
}

export interface SpacePlanningDecision {
  decisionId: string
  siteId: string
  comparisonId: string
  selectedRunId?: string | null
  supersedesDecisionId?: string | null
  outcome: SpacePlanningDecisionOutcome
  rationale: string
  comparisonHash: string
  definitionVersion: string
  humanDecision: boolean
  automatedRecommendation: boolean
  productionWriteAllowed: boolean
  createdAtUtc: string
  createdBy: string
}

export interface SpacePlanningDecisionList {
  items: SpacePlanningDecision[]
  isTruncated: boolean
}

export interface CreateSpacePlanningDecisionResponse {
  outcome: 'Created' | 'Duplicate'
  decision: SpacePlanningDecision
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

function simulationRoot(siteId: string, branchId: string) {
  return `${planningRoot}/sites/${encodeURIComponent(siteId)}` +
    `/scenario-branches/${encodeURIComponent(branchId)}/simulation-runs`
}

export const planningSimulationApi = {
  list(siteId: string, branchId: string, limit = 50) {
    return http.get<unknown, SpacePlanningSimulationRunList>(
      simulationRoot(siteId, branchId),
      { params: { limit } },
    )
  },
  get(siteId: string, branchId: string, runId: string) {
    return http.get<unknown, SpacePlanningSimulationRun>(
      `${simulationRoot(siteId, branchId)}/${encodeURIComponent(runId)}`,
    )
  },
  create(
    siteId: string,
    branchId: string,
    runId: string,
    request: CreateSpacePlanningSimulationRunRequest,
  ) {
    return http.put<unknown, CreateSpacePlanningSimulationRunResponse>(
      `${simulationRoot(siteId, branchId)}/${encodeURIComponent(runId)}`,
      request,
    )
  },
}

function comparisonRoot(siteId: string) {
  return `${planningRoot}/sites/${encodeURIComponent(siteId)}/comparisons`
}

export const planningComparisonApi = {
  list(siteId: string, limit = 50) {
    return http.get<unknown, SpacePlanningComparisonList>(
      comparisonRoot(siteId),
      { params: { limit } },
    )
  },
  get(siteId: string, comparisonId: string) {
    return http.get<unknown, SpacePlanningComparison>(
      `${comparisonRoot(siteId)}/${encodeURIComponent(comparisonId)}`,
    )
  },
  create(
    siteId: string,
    comparisonId: string,
    request: CreateSpacePlanningComparisonRequest,
  ) {
    return http.put<unknown, CreateSpacePlanningComparisonResponse>(
      `${comparisonRoot(siteId)}/${encodeURIComponent(comparisonId)}`,
      request,
    )
  },
  listDecisions(siteId: string, comparisonId: string, limit = 50) {
    return http.get<unknown, SpacePlanningDecisionList>(
      `${comparisonRoot(siteId)}/${encodeURIComponent(comparisonId)}/decisions`,
      { params: { limit } },
    )
  },
  getDecision(siteId: string, comparisonId: string, decisionId: string) {
    return http.get<unknown, SpacePlanningDecision>(
      `${comparisonRoot(siteId)}/${encodeURIComponent(comparisonId)}` +
        `/decisions/${encodeURIComponent(decisionId)}`,
    )
  },
  createDecision(
    siteId: string,
    comparisonId: string,
    decisionId: string,
    request: CreateSpacePlanningDecisionRequest,
  ) {
    return http.put<unknown, CreateSpacePlanningDecisionResponse>(
      `${comparisonRoot(siteId)}/${encodeURIComponent(comparisonId)}` +
        `/decisions/${encodeURIComponent(decisionId)}`,
      request,
    )
  },
}
