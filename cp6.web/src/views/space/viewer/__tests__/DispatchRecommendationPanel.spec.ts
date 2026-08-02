// @vitest-environment jsdom
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import DispatchRecommendationPanel from '../DispatchRecommendationPanel.vue'
import type { SpaceDispatchRecommendation } from '@/types/space/runtime'

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

function mountPanel(value: SpaceDispatchRecommendation | null) {
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
    },
    global: { plugins: [i18n] },
  })
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
