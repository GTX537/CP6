// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import DispatchRecommendationPanel from '../DispatchRecommendationPanel.vue'
import type {
  SpaceDispatchApprovalRequest,
  SpaceDispatchExecution,
  SpaceDispatchOutcomeEvaluation,
  SpaceDispatchRecommendation,
} from '@/types/space/runtime'

describe('DispatchRecommendationPanel', () => {
  it('emits explicit floor, distance, source, and result caps', async () => {
    const wrapper = mountPanel(null)
    const inputs = wrapper.findAll('.dispatch-form input')
    await inputs[0]!.setValue(' Pick ')
    await inputs[1]!.setValue('25')
    await inputs[2]!.setValue('10')
    await inputs[3]!.setValue(true)
    await inputs[4]!.setValue(true)
    await inputs[5]!.setValue(true)
    await wrapper.find('.dispatch-form').trigger('submit')

    expect(wrapper.emitted('generate')?.[0]?.[0]).toEqual({
      taskType: 'Pick',
      taskFloorLogicalId: 'floor-1',
      taskZoneLogicalId: null,
      allowCrossFloor: true,
      maximumTravelDistanceMeters: 25,
      includeSimulatedPersonnel: true,
      maximumAssignments: 10,
    })
  })

  it('renders task concurrency and independent personnel-time evidence', async () => {
    const wrapper = mountPanel(recommendation())

    expect(wrapper.text()).toContain('2/2')
    expect(wrapper.text()).toContain('TASK-1 → PERSON-A')
    expect(wrapper.text()).toContain('C2 · E3 · AQIDBA==')
    expect(wrapper.text()).toContain('TASK_CONCURRENCY_EVIDENCE_CAPTURED')
    expect(wrapper.text()).toContain('人员位置陈旧 1')
    expect(wrapper.text()).toContain('不会审批、分配、认领、启动或修改任务')

    await wrapper.find('.assignment-list button').trigger('click')
    expect(wrapper.emitted('locate')?.[0]).toEqual(['F1-L01'])
  })

  it('requires an explicit real-person selection and reason before approval submission', async () => {
    const wrapper = mountPanel(recommendation())
    const checkboxes = wrapper.findAll('.assignment-select input')
    expect(checkboxes).toHaveLength(2)
    await checkboxes[1]!.setValue(true)
    await checkboxes[0]!.setValue(true)
    await wrapper.find('.approval-form textarea').setValue(' Release reviewed batch ')
    await wrapper.find('.approval-form').trigger('submit')

    expect(wrapper.emitted('submit-approval')?.[0]?.[0]).toEqual({
      selectedRanks: [1, 2],
      reason: 'Release reviewed batch',
    })
  })

  it('renders durable approval evidence and exposes refresh and pending cancel only', async () => {
    const wrapper = mountPanel(recommendation(), approval())

    expect(wrapper.find('.approval-status').attributes('data-status')).toBe('PendingApproval')
    expect(wrapper.text()).toContain('待审批')
    expect(wrapper.text()).toContain('cp6-mobile-task-assignment-v1')
    await wrapper.findAll('.approval-status .approval-actions button')[0]!.trigger('click')
    await wrapper.findAll('.approval-status .approval-actions button')[1]!.trigger('click')
    expect(wrapper.emitted('refresh-approval')).toHaveLength(1)
    expect(wrapper.emitted('cancel-approval')).toHaveLength(1)

    await wrapper.setProps({ approval: { ...approval(), status: 'Applied' } })
    expect(wrapper.findAll('.approval-status .approval-actions button')).toHaveLength(1)
  })

  it('renders live execution evidence and emits explicit retry and compensation reasons', async () => {
    const appliedApproval = { ...approval(), status: 'Applied' as const }
    const wrapper = mountPanel(recommendation(), appliedApproval, execution())

    expect(wrapper.text()).toContain('执行中')
    expect(wrapper.text()).toContain('TASK-1 → PERSON-A')
    expect(wrapper.text()).toContain('WMS 20 · E4')
    expect(wrapper.text()).toContain('剩余重试次数 2')
    expect(wrapper.text()).toContain('补偿只撤销尚未开始的整批任务分派')

    await wrapper.find('.execution-action-form textarea').setValue(' Retry reviewed batch ')
    const buttons = wrapper.findAll('.execution-action-form .approval-actions button')
    expect(buttons).toHaveLength(3)
    await buttons[0]!.trigger('click')
    await buttons[1]!.trigger('click')
    await buttons[2]!.trigger('click')

    expect(wrapper.emitted('refresh-execution')).toHaveLength(1)
    expect(wrapper.emitted('retry-execution')?.[0]?.[0]).toEqual({
      reason: 'Retry reviewed batch',
    })
    expect(wrapper.emitted('compensate-execution')?.[0]?.[0]).toEqual({
      reason: 'Retry reviewed batch',
    })
  })

  it('renders evidence-bounded outcome evaluation and emits refresh', async () => {
    const appliedApproval = { ...approval(), status: 'Applied' as const }
    const wrapper = mountPanel(
      recommendation(),
      appliedApproval,
      execution(),
      evaluation(),
    )

    expect(wrapper.text()).toContain('调度效果评估')
    expect(wrapper.text()).toContain('2.000 m')
    expect(wrapper.text()).toContain('1.000 m')
    expect(wrapper.text()).toContain('+50.0%')
    expect(wrapper.text()).toContain('TASK_LINKED_ROUTE_TRAJECTORY_NOT_AVAILABLE')
    expect(wrapper.text()).toContain('COMPARABLE_HISTORICAL_CONTROL_WINDOW_NOT_AVAILABLE')
    expect(wrapper.text()).toContain('LABOR_DEVICE_COST_AND_ATTRIBUTION_BASELINE_NOT_AVAILABLE')
    expect(wrapper.text()).toContain('稳定顺序反事实')

    await wrapper.find('.evaluation-section .approval-actions button').trigger('click')
    expect(wrapper.emitted('refresh-evaluation')).toHaveLength(1)
  })

  it('keeps no-assignment and refresh failure states visible', async () => {
    const value = recommendation()
    value.outcome = 'NoAssignment'
    value.matchableAssignmentCount = 0
    value.returnedAssignmentCount = 0
    value.assignments = []
    const wrapper = mountPanel(value)
    expect(wrapper.text()).toContain('当前约束下没有可解释的调度建议')

    await wrapper.setProps({ error: 'Personnel source unavailable' })
    expect(wrapper.text()).toContain('Personnel source unavailable')
  })
})

function mountPanel(
  value: SpaceDispatchRecommendation | null,
  approvalValue: SpaceDispatchApprovalRequest | null = null,
  executionValue: SpaceDispatchExecution | null = null,
  evaluationValue: SpaceDispatchOutcomeEvaluation | null = null,
) {
  const i18n = createI18n({
    legacy: false,
    locale: 'zh-CN',
    missingWarn: false,
    fallbackWarn: false,
    messages: {
      'zh-CN': {
        PERSON_POSITION_STALE: '人员位置陈旧',
      },
    },
  })
  return mount(DispatchRecommendationPanel, {
    props: {
      currentFloorId: 'floor-1',
      result: value,
      loading: false,
      error: '',
      approval: approvalValue,
      approvalLoading: false,
      approvalError: '',
      execution: executionValue,
      executionLoading: false,
      executionError: '',
      evaluation: evaluationValue,
      evaluationLoading: false,
      evaluationError: '',
    },
    global: { plugins: [i18n] },
  })
}

function evaluation(): SpaceDispatchOutcomeEvaluation {
  return {
    approvalRequestId: 'approval-1',
    siteId: 'site-1',
    recommendationId: 'recommendation-1',
    publishedVersionId: 'version-1',
    warehouseCode: 'WH-01',
    approvalStatus: 'Applied',
    executionStatus: 'Executing',
    evaluatedAtUtc: '2026-08-02T18:03:00Z',
    evidence: {
      recommendationGeneratedAtUtc: '2026-08-02T18:00:00Z',
      approvalRequestedAtUtc: '2026-08-02T18:01:00Z',
      approvalDecidedAtUtc: '2026-08-02T18:01:30Z',
      assignmentAppliedAtUtc: '2026-08-02T18:02:00Z',
      executionObservedAtUtc: '2026-08-02T18:03:00Z',
      recommendationDefinitionVersion: 'space-dispatch-v1',
      evaluationDefinitionVersion: 'space-dispatch-outcome-evaluation-v1',
      adapterId: 'cp6-mobile-task-assignment-v1',
    },
    funnel: {
      recommendedCount: 2,
      selectedCount: 1,
      assignmentReceiptCount: 1,
      startedCount: 1,
      completedCount: 0,
      attentionCount: 0,
      compensatedCount: 0,
      selectionRatePercent: 50,
      assignmentSuccessRatePercent: 100,
      startRatePercent: 100,
      completionRatePercent: 0,
    },
    timing: {
      approvalLeadTimeSeconds: 30,
      assignmentLeadTimeSeconds: 60,
      assignmentToStartSampleCount: 1,
      averageAssignmentToStartSeconds: 10,
      executionSampleCount: 0,
      averageExecutionSeconds: null,
      assignmentToCompletionSampleCount: 0,
      averageAssignmentToCompletionSeconds: null,
    },
    plannedDistance: {
      status: 'Available',
      basis: 'SELECTED_COHORT_STABLE_ORDER_PUBLISHED_GEOMETRY',
      cohortCount: 2,
      stableOrderBaselineMeters: 2,
      optimizedMeters: 1,
      differenceMeters: 1,
      differencePercent: 50,
      outcome: 'Improved',
      unavailableReason: null,
    },
    benefitBoundary: {
      actualTravelDistanceAvailable: false,
      actualTravelDistanceReason: 'TASK_LINKED_ROUTE_TRAJECTORY_NOT_AVAILABLE',
      throughputUpliftAvailable: false,
      throughputUpliftReason: 'COMPARABLE_HISTORICAL_CONTROL_WINDOW_NOT_AVAILABLE',
      monetaryBenefitAvailable: false,
      monetaryBenefitReason: 'LABOR_DEVICE_COST_AND_ATTRIBUTION_BASELINE_NOT_AVAILABLE',
    },
    limitations: ['PLANNED_DISTANCE_IS_PUBLISHED_GEOMETRY_NOT_ACTUAL_ROUTE'],
  }
}

function execution(): SpaceDispatchExecution {
  return {
    approvalRequestId: 'approval-1',
    siteId: 'site-1',
    recommendationId: 'recommendation-1',
    approvalStatus: 'Applied',
    status: 'Executing',
    observedAtUtc: '2026-08-02T18:03:00Z',
    totalCount: 1,
    assignedCount: 0,
    executingCount: 1,
    completedCount: 0,
    attentionCount: 0,
    canRetry: true,
    retryAttemptCount: 1,
    retryAttemptsRemaining: 2,
    canCompensate: true,
    compensationBlockCode: null,
    compensatedAtUtc: null,
    tasks: [{
      rank: 1,
      taskId: 'TASK-1',
      personSourceId: 'PDA-01',
      personExternalId: 'PERSON-A',
      assignmentOperationId: 'operation-1',
      wmsStatus: 20,
      state: 'InProgress',
      executionVersion: 4,
      startedAtUtc: '2026-08-02T18:02:00Z',
      doneAtUtc: null,
      lastEventType: 'TASK_STARTED',
      lastEventAtUtc: '2026-08-02T18:02:00Z',
    }],
    actions: [{
      actionId: 'action-1',
      actionType: 'RetryAssignment',
      status: 'Applied',
      reason: 'Retry reviewed batch',
      requestedBy: 'requester-1',
      requestedAtUtc: '2026-08-02T18:01:30Z',
      adapterId: 'cp6-mobile-task-assignment-v1',
      receipts: [],
      failureCode: null,
    }],
  }
}

function approval(): SpaceDispatchApprovalRequest {
  return {
    approvalRequestId: 'approval-1',
    siteId: 'site-1',
    recommendationId: 'recommendation-1',
    publishedVersionId: 'version-1',
    warehouseCode: 'WH-01',
    recommendationDefinitionVersion: 'space-dispatch-v1',
    status: 'PendingApproval',
    reason: 'Release reviewed batch',
    requestedBy: 'requester-1',
    requestedAtUtc: '2026-08-02T18:01:00Z',
    flowInstanceId: 'flow-1',
    decidedBy: null,
    decidedAtUtc: null,
    appliedAtUtc: null,
    adapterId: 'cp6-mobile-task-assignment-v1',
    selectedCount: 1,
    selections: [{
      rank: 1,
      taskId: 'TASK-1',
      taskType: 'Pick',
      personSourceId: 'PDA-01',
      personExternalId: 'PERSON-A',
      targetLocationCode: 'F1-L01',
    }],
    receipts: [],
    failureCode: null,
  }
}

function recommendation(): SpaceDispatchRecommendation {
  return {
    recommendationId: 'recommendation-1',
    siteId: 'site-1',
    publishedVersionId: 'version-1',
    warehouseCode: 'WH-01',
    generatedAtUtc: '2026-08-02T18:00:00Z',
    generatedBy: 'actor-1',
    definitionVersion: 'space-dispatch-v1',
    outcome: 'AssignmentsGenerated',
    request: {
      taskType: 'PICK',
      allowCrossFloor: false,
      includeSimulatedPersonnel: false,
      maximumAssignments: 20,
    },
    sources: {
      dispatchTasks: {
        kind: 'Real',
        adapterId: 'cp6-wms-v1',
        dataSourceId: 'CP6_WMS',
        observedAtUtc: '2026-08-02T17:59:58Z',
        receivedAtUtc: '2026-08-02T18:00:00Z',
        delayMilliseconds: 2000,
        clockSkewMilliseconds: 0,
        isSimulated: false,
        isAvailable: true,
      },
      personnel: {
        asOfUtc: '2026-08-02T18:00:00Z',
        freshnessThresholdSeconds: 300,
        currentStateCount: 3,
        realStateCount: 3,
        simulatedStateCount: 0,
        sourcesTruncated: false,
        sources: [{
          sourceId: 'PDA-01',
          sourceKind: 'Real',
          currentStateCount: 3,
          latestPositionOccurredAtUtc: '2026-08-02T17:59:50Z',
          latestPositionReceivedAtUtc: '2026-08-02T17:59:51Z',
          latestWorkStateOccurredAtUtc: '2026-08-02T17:59:52Z',
          latestWorkStateReceivedAtUtc: '2026-08-02T17:59:53Z',
        }],
      },
    },
    examinedTaskCount: 2,
    eligibleTaskCount: 2,
    examinedPersonCount: 3,
    eligiblePersonCount: 2,
    eligiblePairCount: 4,
    matchableAssignmentCount: 2,
    returnedAssignmentCount: 2,
    isTruncated: false,
    exclusions: {
      tasksOutsideRequestedScope: 0,
      tasksNotPending: 0,
      tasksAlreadyAssigned: 0,
      invalidTasks: 0,
      taskTargetOutsidePublishedModel: 0,
      taskLocationCodeMismatch: 0,
      eligibleTasksWithoutAssignment: 0,
      peoplePositionStale: 1,
      peopleWorkStateStale: 0,
      peopleNotIdle: 0,
      peopleSimulatedExcluded: 0,
      peopleWithoutResolvablePosition: 0,
      eligiblePeopleWithoutAssignment: 0,
      crossFloorPairsRejected: 0,
      distanceUnverifiablePairsRejected: 0,
      distanceExceededPairsRejected: 0,
    },
    exclusionSamplesTruncated: false,
    exclusionSamples: [{
      subject: 'Person',
      reason: 'PERSON_POSITION_STALE',
      taskId: null,
      personKey: 'stale-person-key',
      locationCode: null,
      floorLogicalId: 'floor-1',
      floorCode: 'F1',
      zoneLogicalId: 'zone-1',
      zoneCode: 'Z1',
    }],
    assignments: [assignment(1, 'TASK-1', 'PERSON-A', 'F1-L01'),
      assignment(2, 'TASK-2', 'PERSON-B', 'F1-L02')],
    limitations: [
      'RECOMMENDATION_DOES_NOT_APPROVE_ASSIGN_CLAIM_START_OR_WRITE_TASKS',
    ],
  }
}

function assignment(rank: number, taskId: string, person: string, location: string) {
  return {
    rank,
    taskId,
    taskType: 'Pick',
    taskStatus: 'Pending',
    taskPriority: rank,
    taskContractVersion: 2,
    taskExecutionVersion: 3,
    taskRowVersion: 'AQIDBA==',
    targetLocationRole: 'Source',
    targetLocationLogicalId: `location-${rank}`,
    targetLocationCode: location,
    targetFloorLogicalId: 'floor-1',
    targetFloorCode: 'F1',
    targetFloorName: 'Floor 1',
    targetFloorLevel: 1,
    targetZoneLogicalId: 'zone-1',
    targetZoneCode: 'Z1',
    targetRackLogicalId: 'rack-1',
    targetRackCode: 'R1',
    taskQuantity: 1,
    taskMaterialNumber: 'SKU-1',
    personKey: `person-key-${rank}`,
    personSourceId: 'PDA-01',
    personSourceKind: 'Real',
    personExternalId: person,
    personLocationLogicalId: `person-location-${rank}`,
    personFloorLogicalId: 'floor-1',
    personZoneLogicalId: 'zone-1',
    personPositionOccurredAtUtc: '2026-08-02T17:59:50Z',
    personPositionReceivedAtUtc: '2026-08-02T17:59:51Z',
    personWorkStateOccurredAtUtc: '2026-08-02T17:59:52Z',
    personWorkStateReceivedAtUtc: '2026-08-02T17:59:53Z',
    sameFloor: true,
    sameZone: true,
    geometricDistanceMeters: rank,
    ruleHits: ['TASK_CONCURRENCY_EVIDENCE_CAPTURED', 'PERSON_STATE_IS_IDLE'],
  }
}
