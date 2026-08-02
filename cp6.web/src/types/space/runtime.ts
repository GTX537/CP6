import type { SpaceDataSource } from './dataSource'

export interface SpaceRuntimeSource extends SpaceDataSource {
  adapterId: string
  receivedAtUtc: string
  delayMilliseconds: number
  clockSkewMilliseconds: number
}

export interface SpaceRuntimeInventoryItem {
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  physicalQuantity: number
  allocatedQuantity: number
  materialNumber: string | null
  lotNumber: string | null
  containerNumber: string | null
  ownerId: string | null
}

export interface SpaceRuntimeInventoryResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  items: SpaceRuntimeInventoryItem[]
}

export interface SpaceRuntimeInventoryLocateQuery {
  materialNumber?: string
  lotNumber?: string
  containerNumber?: string
  ownerId?: string
}

export interface SpaceRuntimeInventoryLocateCriteria {
  materialNumber: string | null
  lotNumber: string | null
  containerNumber: string | null
  ownerId: string | null
}

export interface SpaceRuntimeInventoryLocateHit {
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  physicalQuantity: number
  allocatedQuantity: number
  materialNumbers: string[]
  lotNumbers: string[]
  containerNumbers: string[]
  ownerIds: string[]
}

export interface SpaceRuntimeInventoryLocateResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  criteria: SpaceRuntimeInventoryLocateCriteria
  locationCount: number
  floorCount: number
  items: SpaceRuntimeInventoryLocateHit[]
}

export type SpaceWarehouseAbcRank = 'A' | 'B' | 'C' | 'Unclassified'

export interface SpaceWarehouseModelKpi {
  floorCount: number
  areaAvailableFloorCount: number
  areaMissingFloorCount: number
  totalFloorAreaSquareMeters: number | null
  zoneCount: number
  rackCount: number
  rackFootprintSquareMeters: number
  rackFootprintRatePercent: number | null
  activeLocationCount: number
}

export interface SpaceWarehouseInventoryKpi {
  source: SpaceRuntimeSource
  inventoryLineCount: number | null
  occupiedLocationCount: number | null
  unoccupiedLocationCount: number | null
  occupiedLocationRatePercent: number | null
  occupiedLocationRateMethod: string
  capacityUtilizationPercent: number | null
  capacityUtilizationStatus: string
  capacityUtilizationReason: string
  distinctOwnerCount: number | null
  distinctMaterialCount: number | null
  distinctLotCount: number | null
  distinctContainerCount: number | null
}

export interface SpaceWarehouseTaskKpi {
  source: SpaceRuntimeSource
  activeTaskCount: number | null
  activeTaskStopCount: number | null
}

export interface SpaceWarehouseAnomalyKpi {
  activeDeviceAlarmCount: number
  criticalDeviceAlarmCount: number
  codeMismatchLocationCount: number | null
  overAllocatedInventoryLineCount: number | null
  areaMissingFloorCount: number
  unclassifiedAbcMaterialCount: number | null
}

export interface SpaceWarehouseAbcMaterial {
  materialNumber: string
  outboundMovementCount: number
  outboundQuantity: number
  previousCumulativeSharePercent: number | null
  cumulativeSharePercent: number | null
  rank: SpaceWarehouseAbcRank
  occupiedLocationCount: number
  floorCount: number
}

export interface SpaceWarehouseAbcLocationMaterial {
  materialNumber: string
  rank: SpaceWarehouseAbcRank
}

export interface SpaceWarehouseAbcLocation {
  locationLogicalId: string
  spaceLocationCode: string
  floorLogicalId: string
  floorCode: string
  rank: SpaceWarehouseAbcRank
  materials: SpaceWarehouseAbcLocationMaterial[]
}

export interface SpaceWarehouseAbc {
  source: SpaceRuntimeSource
  windowDays: number
  windowStartDate: string
  windowEndDateExclusive: string
  transactionTimeBasis: string
  rankingMethod: string
  aThresholdPercent: number
  bThresholdPercent: number
  spatialMappingAvailable: boolean
  materialCount: number | null
  aCount: number | null
  bCount: number | null
  cCount: number | null
  unclassifiedCount: number | null
  materials: SpaceWarehouseAbcMaterial[]
  locations: SpaceWarehouseAbcLocation[]
}

export interface SpaceWarehouseFloorKpi {
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  areaSquareMeters: number | null
  activeLocationCount: number
  occupiedLocationCount: number | null
  occupiedLocationRatePercent: number | null
  aLocationCount: number | null
  bLocationCount: number | null
  cLocationCount: number | null
  unclassifiedLocationCount: number | null
}

export interface SpaceWarehouseOverviewResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  capturedAtUtc: string
  isRuntimeComplete: boolean
  model: SpaceWarehouseModelKpi
  inventory: SpaceWarehouseInventoryKpi
  tasks: SpaceWarehouseTaskKpi
  anomalies: SpaceWarehouseAnomalyKpi
  abc: SpaceWarehouseAbc
  floors: SpaceWarehouseFloorKpi[]
}

export interface SpaceRuntimeTaskItem {
  taskId: string
  taskType: string
  status: string
  sequenceNo: number
  locationLogicalId: string
  wmsLogicalId: string
  spaceLocationCode: string
  wmsLocationCode: string
  codeMatches: boolean
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  zoneLogicalId: string | null
  zoneCode: string | null
  rackLogicalId: string | null
  rackCode: string | null
  anchorXMillimeters: number | null
  anchorYMillimeters: number | null
  anchorZMillimeters: number | null
  quantity: number | null
  materialNumber: string | null
}

export interface SpaceRuntimeTaskFloor {
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  elevationMillimeters: number
  heightMillimeters: number
  stopCount: number
  totalQuantity: number
}

export interface SpaceRuntimeTaskWorkload {
  floorLogicalId: string
  floorCode: string
  zoneLogicalId: string | null
  zoneCode: string | null
  stopCount: number
  totalQuantity: number
}

export interface SpaceRuntimeTaskAisle {
  floorLogicalId: string
  zoneLogicalId: string
  aisleLogicalId: string
  aisleCode: string
  centerlineJson: string
}

export interface SpaceRuntimeTaskPathResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  source: SpaceRuntimeSource
  taskId: string
  stopCount: number
  locatedStopCount: number
  floorCount: number
  zoneCount: number
  floorTransitionCount: number
  zoneTransitionCount: number
  totalQuantity: number
  crossFloor: boolean
  crossZone: boolean
  actualStops: SpaceRuntimeTaskItem[]
  floors: SpaceRuntimeTaskFloor[]
  workloads: SpaceRuntimeTaskWorkload[]
  aisles: SpaceRuntimeTaskAisle[]
}

export interface RuntimeLocationRef {
  locationLogicalId: string
  locationCode: string
}

export interface RuntimeStockItem {
  locationLogicalId: string
  locationCode: string
  binStatus: 0 | 1
  qty: number
  allocatedQty: number
  capacity: null
  topMaterial: string | null
  productKinds: number
}

export interface SpacePersonnelCurrentPage {
  siteId: string
  asOfUtc: string
  freshnessThresholdSeconds: number
  items: SpacePersonnelCurrent[]
  nextCursor: string | null
}

export interface SpacePersonnelCurrent {
  sourceId: string
  sourceKind: 'Real' | 'Simulated'
  personExternalId: string
  workState: 'Unknown' | 'Offline' | 'Idle' | 'Busy' | 'Break'
  floorLogicalId: string | null
  locationLogicalId: string | null
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
  accuracyMillimeters: number | null
  positionOccurredAtUtc: string | null
  positionReceivedAtUtc: string | null
  positionEventId: string | null
  positionSourceEventId: string | null
  workStateOccurredAtUtc: string | null
  workStateReceivedAtUtc: string | null
  workStateEventId: string | null
  workStateSourceEventId: string | null
  positionAgeMilliseconds: number | null
  workStateAgeMilliseconds: number | null
  hasPosition: boolean
  positionIsStale: boolean
  workStateIsStale: boolean
  isSimulated: boolean
}

export interface SpacePersonnelTrajectoryResponse {
  siteId: string
  sourceId: string
  sourceKind: 'Real' | 'Simulated'
  personExternalId: string
  fromUtc: string
  toUtc: string
  retentionCutoffUtc: string
  items: SpacePersonnelTrajectoryPoint[]
  nextCursor: string | null
}

export interface SpacePersonnelTrajectoryPoint {
  eventId: string
  sourceEventId: string
  floorLogicalId: string | null
  locationLogicalId: string | null
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
  accuracyMillimeters: number | null
  sourceSequence: number | null
  occurredAtUtc: string
  receivedAtUtc: string
  ingestDelayMilliseconds: number
}

export type SpaceDeviceSourceKind = 'Real' | 'Simulated'
export type SpaceDeviceKind =
  | 'Agv'
  | 'Conveyor'
  | 'StackerCrane'
  | 'Lift'
  | 'Sorter'
  | 'Workstation'
  | 'Sensor'
  | 'Other'
export type SpaceDeviceOperatingState =
  | 'Unknown'
  | 'Offline'
  | 'Idle'
  | 'Running'
  | 'Paused'
  | 'Faulted'
  | 'Maintenance'
export type SpaceDeviceAlarmSeverity = 'Info' | 'Warning' | 'Critical'

export interface SpaceDeviceCurrentPage {
  siteId: string
  publishedVersionId: string
  asOfUtc: string
  freshnessThresholdSeconds: number
  items: SpaceDeviceCurrent[]
  nextCursor: string | null
}

export interface SpaceDeviceCurrent {
  mappingId: string
  sourceId: string
  sourceKind: SpaceDeviceSourceKind
  deviceExternalId: string
  deviceKind: SpaceDeviceKind
  elementLogicalId: string
  elementType: string
  mappingIsCurrent: boolean
  mappedFloorLogicalId: string | null
  mappedXMillimeters: number | null
  mappedYMillimeters: number | null
  mappedZMillimeters: number | null
  operatingState: SpaceDeviceOperatingState
  floorLogicalId: string | null
  locationLogicalId: string | null
  xMillimeters: number | null
  yMillimeters: number | null
  zMillimeters: number | null
  accuracyMillimeters: number | null
  positionOccurredAtUtc: string | null
  positionReceivedAtUtc: string | null
  positionEventId: string | null
  positionSourceEventId: string | null
  operatingStateOccurredAtUtc: string | null
  operatingStateReceivedAtUtc: string | null
  operatingStateEventId: string | null
  operatingStateSourceEventId: string | null
  positionAgeMilliseconds: number | null
  operatingStateAgeMilliseconds: number | null
  hasPosition: boolean
  positionIsStale: boolean
  operatingStateIsStale: boolean
  isSimulated: boolean
  hasActiveAlarm: boolean
  activeAlarmCount: number
  maximumActiveAlarmSeverity: SpaceDeviceAlarmSeverity | null
  activeAlarms: SpaceDeviceActiveAlarm[]
}

export interface SpaceDeviceActiveAlarm {
  alarmExternalId: string
  alarmCode: string
  alarmSeverity: SpaceDeviceAlarmSeverity
  alarmMessage: string | null
  occurredAtUtc: string
  receivedAtUtc: string
  eventId: string
  sourceEventId: string
  ageMilliseconds: number | null
}

export interface SpaceOperationsDiagnosticThresholds {
  maximumObservationGapSeconds: number
  minimumBacktrackSegmentMillimeters: number
  backtrackAngleDegrees: number
  dwellThresholdSeconds: number
  congestionMinimumConcurrentPeople: number
  occupancyWatchPercent: number
  occupancyCriticalPercent: number
}

export interface SpaceOperationsPersonnelSourceItem {
  sourceId: string
  sourceKind: string
  eventCount: number
  personCount: number
  firstObservedAtUtc: string
  lastObservedAtUtc: string
  lastReceivedAtUtc: string
}

export interface SpaceOperationsPersonnelSource {
  evidenceEventCount: number
  eligibleRealEventCount: number
  excludedSimulatedEventCount: number
  excludedOutsidePublishedModelEventCount: number
  personCount: number
  sourceCount: number
  firstObservedAtUtc: string | null
  lastObservedAtUtc: string | null
  lastReceivedAtUtc: string | null
  sources: SpaceOperationsPersonnelSourceItem[]
}

export interface SpaceOperationsBacktrackFinding {
  floorLogicalId: string
  floorCode: string | null
  locationLogicalId: string | null
  spaceLocationCode: string | null
  xMillimeters: number
  yMillimeters: number
  occurredAtUtc: string
  turnAngleDegrees: number
  returnSegmentMeters: number
}

export interface SpaceOperationsPathDiagnosis {
  personCount: number
  observedTransitionCount: number
  knownDistanceSegmentCount: number
  unknownDistanceSegmentCount: number
  observedDistanceMeters: number
  backtrackCount: number
  backtrackDistanceMeters: number
  backtracksTruncated: boolean
  backtracks: SpaceOperationsBacktrackFinding[]
}

export interface SpaceOperationsDwellHotspot {
  locationLogicalId: string
  spaceLocationCode: string | null
  floorLogicalId: string
  floorCode: string | null
  episodeCount: number
  personCount: number
  totalDwellSeconds: number
  maximumDwellSeconds: number
}

export interface SpaceOperationsDwellDiagnosis {
  episodeCount: number
  personCount: number
  locationCount: number
  totalDwellSeconds: number
  hotspotsTruncated: boolean
  hotspots: SpaceOperationsDwellHotspot[]
}

export interface SpaceOperationsCongestionHotspot {
  locationLogicalId: string
  spaceLocationCode: string | null
  floorLogicalId: string
  floorCode: string | null
  peakConcurrentPeople: number
  concurrentSeconds: number
  observedPersonCount: number
}

export interface SpaceOperationsCongestionDiagnosis {
  locationCount: number
  peakConcurrentPeople: number
  concurrentSeconds: number
  hotspotsTruncated: boolean
  hotspots: SpaceOperationsCongestionHotspot[]
}

export interface SpaceOperationsFloorOccupancy {
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  locationCount: number
  occupiedLocationCount: number | null
  locationOccupancyPercent: number | null
  locationOccupancyPressure: string
}

export interface SpaceOperationsCapacityDiagnosis {
  source: SpaceRuntimeSource | null
  isAvailable: boolean
  occupancyBasis: string
  locationCount: number
  occupiedLocationCount: number | null
  locationOccupancyPercent: number | null
  locationOccupancyPressure: string
  capacityUtilizationPercent: number | null
  capacityUtilizationStatus: string
  capacityUtilizationReason: string
  floors: SpaceOperationsFloorOccupancy[]
}

export interface SpaceOperationsDiagnosticResponse {
  siteId: string
  publishedVersionId: string
  warehouseCode: string | null
  windowFromUtc: string
  windowToUtc: string
  calculatedAtUtc: string
  definitionVersion: string
  thresholds: SpaceOperationsDiagnosticThresholds
  personnelSource: SpaceOperationsPersonnelSource
  path: SpaceOperationsPathDiagnosis
  congestion: SpaceOperationsCongestionDiagnosis
  dwell: SpaceOperationsDwellDiagnosis
  capacity: SpaceOperationsCapacityDiagnosis
  limitations: string[]
}

export interface GenerateSpacePutawayRecommendationRequest {
  materialNumber: string
  ownerId?: string | null
  lotNumber?: string | null
  inboundQuantity: number
  floorLogicalId?: string | null
  zoneLogicalId?: string | null
  requiredWidthMillimeters?: number | null
  requiredHeightMillimeters?: number | null
  requiredDepthMillimeters?: number | null
  requiredMaxLoad?: number | null
  allowExactStockConsolidation: boolean
  maximumCandidates: number
}

export interface SpacePutawayRecommendationSources {
  inventory: SpaceRuntimeSource
  activeTasks: SpaceRuntimeSource
}

export interface SpacePutawayRecommendationExclusions {
  missingSpatialMetadata: number
  outsideRequestedScope: number
  activeTask: number
  invalidInventory: number
  locationCodeMismatch: number
  occupiedIncompatible: number
  dimensionTooSmall: number
  loadUnverifiable: number
  loadInsufficient: number
}

export interface SpacePutawayRecommendationExclusionSample {
  locationLogicalId: string
  spaceLocationCode: string | null
  floorLogicalId: string
  floorCode: string | null
  zoneLogicalId: string | null
  zoneCode: string | null
  reason: string
}

export interface SpacePutawayRecommendationCandidate {
  rank: number
  category: string
  locationLogicalId: string
  spaceLocationCode: string
  floorLogicalId: string
  floorCode: string
  floorName: string
  floorLevel: number
  zoneLogicalId: string | null
  zoneCode: string | null
  rackLogicalId: string | null
  rackCode: string | null
  columnNo: number
  levelNo: number
  depthNo: number
  widthMillimeters: number
  heightMillimeters: number
  depthMillimeters: number
  maxLoad: number | null
  currentPhysicalQuantity: number
  currentAllocatedQuantity: number
  sameFloorAsExistingStock: boolean
  sameZoneAsExistingStock: boolean
  distanceToMatchingStockMeters: number | null
  ruleHits: string[]
}

export interface SpacePutawayRecommendation {
  recommendationId: string
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  generatedAtUtc: string
  generatedBy: string
  definitionVersion: string
  outcome: string
  request: GenerateSpacePutawayRecommendationRequest
  sources: SpacePutawayRecommendationSources
  examinedLocationCount: number
  eligibleCandidateCount: number
  returnedCandidateCount: number
  isTruncated: boolean
  exclusions: SpacePutawayRecommendationExclusions
  exclusionSamplesTruncated: boolean
  exclusionSamples: SpacePutawayRecommendationExclusionSample[]
  candidates: SpacePutawayRecommendationCandidate[]
  limitations: string[]
}

export interface GenerateSpacePutawayRecommendationResponse {
  outcome: 'Generated' | 'Duplicate'
  recommendation: SpacePutawayRecommendation
}

export interface GenerateSpaceDispatchRecommendationRequest {
  taskType?: string | null
  taskFloorLogicalId?: string | null
  taskZoneLogicalId?: string | null
  allowCrossFloor: boolean
  maximumTravelDistanceMeters?: number | null
  includeSimulatedPersonnel: boolean
  maximumAssignments: number
}

export interface SpaceDispatchPersonnelSourceItem {
  sourceId: string
  sourceKind: string
  currentStateCount: number
  latestPositionOccurredAtUtc: string | null
  latestPositionReceivedAtUtc: string | null
  latestWorkStateOccurredAtUtc: string | null
  latestWorkStateReceivedAtUtc: string | null
}

export interface SpaceDispatchPersonnelSource {
  asOfUtc: string
  freshnessThresholdSeconds: number
  currentStateCount: number
  realStateCount: number
  simulatedStateCount: number
  sourcesTruncated: boolean
  sources: SpaceDispatchPersonnelSourceItem[]
}

export interface SpaceDispatchRecommendationSources {
  dispatchTasks: SpaceRuntimeSource
  personnel: SpaceDispatchPersonnelSource
}

export interface SpaceDispatchRecommendationExclusions {
  tasksOutsideRequestedScope: number
  tasksNotPending: number
  tasksAlreadyAssigned: number
  invalidTasks: number
  taskTargetOutsidePublishedModel: number
  taskLocationCodeMismatch: number
  eligibleTasksWithoutAssignment: number
  peoplePositionStale: number
  peopleWorkStateStale: number
  peopleNotIdle: number
  peopleSimulatedExcluded: number
  peopleWithoutResolvablePosition: number
  eligiblePeopleWithoutAssignment: number
  crossFloorPairsRejected: number
  distanceUnverifiablePairsRejected: number
  distanceExceededPairsRejected: number
}

export interface SpaceDispatchRecommendationExclusionSample {
  subject: string
  reason: string
  taskId: string | null
  personKey: string | null
  locationCode: string | null
  floorLogicalId: string | null
  floorCode: string | null
  zoneLogicalId: string | null
  zoneCode: string | null
}

export interface SpaceDispatchRecommendationAssignment {
  rank: number
  taskId: string
  taskType: string
  taskStatus: string
  taskPriority: number
  taskContractVersion: number
  taskExecutionVersion: number
  taskRowVersion: string
  targetLocationRole: string
  targetLocationLogicalId: string
  targetLocationCode: string
  targetFloorLogicalId: string
  targetFloorCode: string
  targetFloorName: string
  targetFloorLevel: number
  targetZoneLogicalId: string | null
  targetZoneCode: string | null
  targetRackLogicalId: string | null
  targetRackCode: string | null
  taskQuantity: number
  taskMaterialNumber: string | null
  personKey: string
  personSourceId: string
  personSourceKind: string
  personExternalId: string
  personLocationLogicalId: string | null
  personFloorLogicalId: string
  personZoneLogicalId: string | null
  personPositionOccurredAtUtc: string
  personPositionReceivedAtUtc: string
  personWorkStateOccurredAtUtc: string
  personWorkStateReceivedAtUtc: string
  sameFloor: boolean
  sameZone: boolean
  geometricDistanceMeters: number | null
  ruleHits: string[]
}

export interface SpaceDispatchRecommendation {
  recommendationId: string
  siteId: string
  publishedVersionId: string
  warehouseCode: string
  generatedAtUtc: string
  generatedBy: string
  definitionVersion: string
  outcome: string
  request: GenerateSpaceDispatchRecommendationRequest
  sources: SpaceDispatchRecommendationSources
  examinedTaskCount: number
  eligibleTaskCount: number
  examinedPersonCount: number
  eligiblePersonCount: number
  eligiblePairCount: number
  matchableAssignmentCount: number
  returnedAssignmentCount: number
  isTruncated: boolean
  exclusions: SpaceDispatchRecommendationExclusions
  exclusionSamplesTruncated: boolean
  exclusionSamples: SpaceDispatchRecommendationExclusionSample[]
  assignments: SpaceDispatchRecommendationAssignment[]
  limitations: string[]
}

export interface GenerateSpaceDispatchRecommendationResponse {
  outcome: 'Generated' | 'Duplicate'
  recommendation: SpaceDispatchRecommendation
}

export type SpaceDispatchApprovalStatus =
  | 'PendingApproval'
  | 'Applied'
  | 'Rejected'
  | 'Cancelled'
  | 'Stale'
  | 'FailedNoEffect'
  | 'Compensated'

export interface SubmitSpaceDispatchApprovalRequest {
  selectedRanks: number[]
  reason: string
}

export interface SpaceDispatchApprovalSelection {
  rank: number
  taskId: string
  taskType: string
  personSourceId: string
  personExternalId: string
  targetLocationCode: string
}

export interface SpaceDispatchTaskAdaptationReceipt {
  rank: number
  taskId: string
  personExternalId: string
  operationId: string
  outcome: string
}

export interface SpaceDispatchApprovalRequest {
  approvalRequestId: string
  siteId: string
  recommendationId: string
  publishedVersionId: string
  warehouseCode: string
  recommendationDefinitionVersion: string
  status: SpaceDispatchApprovalStatus
  reason: string
  requestedBy: string
  requestedAtUtc: string
  flowInstanceId: string
  decidedBy: string | null
  decidedAtUtc: string | null
  appliedAtUtc: string | null
  adapterId: string
  selectedCount: number
  selections: SpaceDispatchApprovalSelection[]
  receipts: SpaceDispatchTaskAdaptationReceipt[]
  failureCode: string | null
}

export interface SubmitSpaceDispatchApprovalResponse {
  outcome: 'Submitted' | 'Duplicate'
  approvalRequest: SpaceDispatchApprovalRequest
}

export interface SubmitSpaceDispatchExecutionActionRequest {
  reason: string
}

export type SpaceDispatchExecutionTaskState =
  | 'Assigned'
  | 'InProgress'
  | 'Paused'
  | 'Exception'
  | 'Completed'
  | 'PartiallyCompleted'
  | 'Cancelled'
  | 'Compensated'
  | 'Released'
  | 'Diverged'
  | 'Missing'

export type SpaceDispatchExecutionStatus =
  | 'PendingApproval'
  | 'Rejected'
  | 'Cancelled'
  | 'Stale'
  | 'AssignmentFailed'
  | 'Assigned'
  | 'Executing'
  | 'Completed'
  | 'Compensated'
  | 'AttentionRequired'

export interface SpaceDispatchExecutionTask {
  rank: number
  taskId: string
  personSourceId: string
  personExternalId: string
  assignmentOperationId: string
  wmsStatus: number
  state: SpaceDispatchExecutionTaskState
  executionVersion: number
  startedAtUtc: string | null
  doneAtUtc: string | null
  lastEventType: string | null
  lastEventAtUtc: string | null
}

export interface SpaceDispatchExecutionAction {
  actionId: string
  actionType: 'RetryAssignment' | 'CompensateAssignment'
  status: 'Applied' | 'FailedNoEffect' | 'RejectedNoEffect'
  reason: string
  requestedBy: string
  requestedAtUtc: string
  adapterId: string
  receipts: SpaceDispatchTaskAdaptationReceipt[]
  failureCode: string | null
}

export interface SpaceDispatchExecution {
  approvalRequestId: string
  siteId: string
  recommendationId: string
  approvalStatus: SpaceDispatchApprovalStatus
  status: SpaceDispatchExecutionStatus
  observedAtUtc: string
  totalCount: number
  assignedCount: number
  executingCount: number
  completedCount: number
  attentionCount: number
  canRetry: boolean
  retryAttemptCount: number
  retryAttemptsRemaining: number
  canCompensate: boolean
  compensationBlockCode: string | null
  compensatedAtUtc: string | null
  tasks: SpaceDispatchExecutionTask[]
  actions: SpaceDispatchExecutionAction[]
}

export interface SpaceDispatchExecutionActionResponse {
  outcome: 'Executed' | 'Duplicate'
  action: SpaceDispatchExecutionAction
  execution: SpaceDispatchExecution
}

export interface SpaceDispatchEvaluationEvidence {
  recommendationGeneratedAtUtc: string
  approvalRequestedAtUtc: string
  approvalDecidedAtUtc: string | null
  assignmentAppliedAtUtc: string | null
  executionObservedAtUtc: string
  recommendationDefinitionVersion: string
  evaluationDefinitionVersion: string
  adapterId: string
}

export interface SpaceDispatchEvaluationFunnel {
  recommendedCount: number
  selectedCount: number
  assignmentReceiptCount: number
  startedCount: number
  completedCount: number
  attentionCount: number
  compensatedCount: number
  selectionRatePercent: number
  assignmentSuccessRatePercent: number
  startRatePercent: number
  completionRatePercent: number
}

export interface SpaceDispatchEvaluationTiming {
  approvalLeadTimeSeconds: number | null
  assignmentLeadTimeSeconds: number | null
  assignmentToStartSampleCount: number
  averageAssignmentToStartSeconds: number | null
  executionSampleCount: number
  averageExecutionSeconds: number | null
  assignmentToCompletionSampleCount: number
  averageAssignmentToCompletionSeconds: number | null
}

export interface SpaceDispatchPlannedDistanceComparison {
  status: 'Available' | 'Unavailable'
  basis: 'SELECTED_COHORT_STABLE_ORDER_PUBLISHED_GEOMETRY'
  cohortCount: number
  stableOrderBaselineMeters: number | null
  optimizedMeters: number | null
  differenceMeters: number | null
  differencePercent: number | null
  outcome: 'Improved' | 'Neutral' | 'Regressed' | null
  unavailableReason: string | null
}

export interface SpaceDispatchBenefitBoundary {
  actualTravelDistanceAvailable: boolean
  actualTravelDistanceReason: string
  throughputUpliftAvailable: boolean
  throughputUpliftReason: string
  monetaryBenefitAvailable: boolean
  monetaryBenefitReason: string
}

export interface SpaceDispatchOutcomeEvaluation {
  approvalRequestId: string
  siteId: string
  recommendationId: string
  publishedVersionId: string
  warehouseCode: string
  approvalStatus: SpaceDispatchApprovalStatus
  executionStatus: SpaceDispatchExecutionStatus
  evaluatedAtUtc: string
  evidence: SpaceDispatchEvaluationEvidence
  funnel: SpaceDispatchEvaluationFunnel
  timing: SpaceDispatchEvaluationTiming
  plannedDistance: SpaceDispatchPlannedDistanceComparison
  benefitBoundary: SpaceDispatchBenefitBoundary
  limitations: string[]
}
