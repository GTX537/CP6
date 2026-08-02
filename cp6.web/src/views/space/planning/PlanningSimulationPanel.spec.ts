// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus, { ElMessage } from 'element-plus'
import { planningSimulationApi } from '@/api/space/planningScenario'
import PlanningSimulationPanel from './PlanningSimulationPanel.vue'
import type {
  CreateSpacePlanningSimulationRunResponse,
  SpacePlanningHistoricalDatasetSummary,
  SpacePlanningSimulationRun,
} from '@/api/space/planningScenario'

vi.mock('@/api/space/planningScenario', () => ({
  planningSimulationApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
  },
}))

const dataset: SpacePlanningHistoricalDatasetSummary = {
  datasetId: 'dataset-1',
  branchId: 'branch-1',
  scenarioVersionId: 'scenario-1',
  name: 'June replay',
  taskCount: 42,
  historicalFromUtc: '2026-06-01T00:00:00Z',
  historicalToUtc: '2026-06-02T00:00:00Z',
  replayStartUtc: '2026-07-29T12:00:00Z',
  replayEndUtc: '2026-07-29T15:00:00Z',
  replaySpeedFactor: 8,
  createdAtUtc: '2026-07-29T12:00:00Z',
}

const run: SpacePlanningSimulationRun = {
  runId: 'run-1',
  siteId: 'site-1',
  branchId: 'branch-1',
  scenarioVersionId: 'scenario-1',
  scenarioContentRevision: 7,
  datasetId: dataset.datasetId,
  name: 'Peak baseline',
  status: 'Completed',
  definitionVersion: 'space-planning-simulation-v1',
  datasetRequestHash: 'a'.repeat(64),
  resultHash: 'b'.repeat(64),
  productionWriteAllowed: false,
  highPrecisionPhysicalSimulation: false,
  parameters: {
    defaultQuantityCapacity: 100,
    defaultConcurrentTaskCapacity: 1,
    throughputWindowMinutes: 60,
    distanceCostPerMeter: 1,
    laborCostPerHour: 20,
    congestionCostPerTaskHour: 5,
    currencyCode: 'CNY',
    locationCapacityOverrideCount: 1,
  },
  distance: {
    geometryBasis: 'rack-cell-straight-line-v1',
    taskCount: 42,
    eligibleTaskCount: 40,
    unknownTaskCount: 2,
    coveragePercent: 95.2381,
    totalDistanceMeters: 1250,
    averageEligibleTaskDistanceMeters: 31.25,
  },
  congestion: {
    monitoredLocationCount: 2,
    overloadedLocationCount: 1,
    peakConcurrentTasks: 3,
    congestionSeconds: 600,
    congestionTaskSeconds: 900,
    congestionTaskHours: 0.25,
  },
  capacity: {
    monitoredLocationCount: 2,
    overloadedLocationCount: 1,
    peakUtilizationPercent: 125,
    quantityBasis: 'CALLER_DEFINED_TASK_QUANTITY_UNITS',
  },
  throughput: {
    completedTaskCount: 40,
    completedQuantity: 80,
    historicalWindowHours: 24,
    measurementWindowMinutes: 60,
    averageCompletedTasksPerHour: 1.666667,
    peakCompletedTasksPerHour: 5,
    averageCompletedQuantityPerHour: 3.333333,
    peakCompletedQuantityPerHour: 10,
  },
  cost: {
    currencyCode: 'CNY',
    laborHours: 20,
    distanceCost: 1250,
    laborCost: 400,
    congestionCost: 1.25,
    totalCost: 1651.25,
    laborBasis: 'UNIONED_TOKENIZED_WORKER_INTERVALS_PLUS_UNASSIGNED_TASKS',
  },
  locationResults: [{
    locationLogicalId: '11111111-1111-1111-1111-111111111111',
    taskCount: 42,
    completedTaskCount: 40,
    totalQuantity: 80,
    distanceEligibleTaskCount: 40,
    totalDistanceMeters: 1250,
    quantityCapacity: 64,
    concurrentTaskCapacity: 1,
    peakConcurrentTasks: 3,
    peakConcurrentQuantity: 80,
    capacityUtilizationPercent: 125,
    congestionSeconds: 600,
    congestionTaskSeconds: 900,
    isOverloaded: true,
  }],
  locationResultsTruncated: false,
  createdAtUtc: '2026-07-29T13:00:00Z',
  createdBy: 'actor-1',
  limitations: ['SIMULATION_RESULTS_CANNOT_WRITE_OR_PUBLISH_TO_PRODUCTION'],
}

describe('PlanningSimulationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(planningSimulationApi.list).mockResolvedValue({
      items: [{
        runId: run.runId,
        datasetId: run.datasetId,
        scenarioContentRevision: run.scenarioContentRevision,
        name: run.name,
        status: run.status,
        currencyCode: run.cost.currencyCode,
        taskCount: run.distance.taskCount,
        distanceCoveragePercent: run.distance.coveragePercent,
        totalDistanceMeters: run.distance.totalDistanceMeters,
        overloadedLocationCount: run.capacity.overloadedLocationCount,
        averageCompletedTasksPerHour:
          run.throughput.averageCompletedTasksPerHour,
        totalCost: run.cost.totalCost,
        createdAtUtc: run.createdAtUtc,
      }],
      isTruncated: false,
    })
    vi.mocked(planningSimulationApi.get).mockResolvedValue(run)
    vi.mocked(planningSimulationApi.create).mockResolvedValue({
      outcome: 'Created',
      run,
    } satisfies CreateSpacePlanningSimulationRunResponse)
    vi.spyOn(ElMessage, 'success').mockImplementation(() => undefined as never)
  })

  it('shows all five metric families with explicit production boundaries', async () => {
    const wrapper = mountPanel()
    await flushPromises()

    expect(planningSimulationApi.list).toHaveBeenCalledWith(
      'site-1',
      'branch-1',
    )
    expect(wrapper.text()).toContain('Peak baseline')
    expect(wrapper.text()).toContain('永不写入或发布到生产')

    await wrapper.find('[data-test="view-simulation"]').trigger('click')
    await flushPromises()

    const evidence = wrapper.find('[data-test="simulation-evidence"]')
    expect(evidence.text()).toContain('无生产回写')
    expect(evidence.text()).toContain('距离')
    expect(evidence.text()).toContain('拥堵')
    expect(evidence.text()).toContain('容量')
    expect(evidence.text()).toContain('平均吞吐')
    expect(evidence.text()).toContain('总成本')
    expect(evidence.text()).toContain('直线货架格口距离')
  })

  it('submits explicit rates and destination capacity overrides', async () => {
    const wrapper = mountPanel()
    await flushPromises()
    await wrapper.find('[data-test="simulation-name"]')
      .setValue('July option')
    await wrapper.find('[data-test="simulation-capacities"]')
      .setValue(JSON.stringify([{
        locationLogicalId: '11111111-1111-1111-1111-111111111111',
        quantityCapacity: 64,
        concurrentTaskCapacity: 2,
      }]))
    await wrapper.find('[data-test="create-simulation"]').trigger('click')
    await flushPromises()

    expect(planningSimulationApi.create).toHaveBeenCalledWith(
      'site-1',
      'branch-1',
      expect.stringMatching(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      ),
      expect.objectContaining({
        name: 'July option',
        datasetId: dataset.datasetId,
        defaultQuantityCapacity: 100,
        defaultConcurrentTaskCapacity: 1,
        throughputWindowMinutes: 60,
        currencyCode: 'CNY',
        locationCapacities: [expect.objectContaining({
          quantityCapacity: 64,
          concurrentTaskCapacity: 2,
        })],
      }),
    )
  })
})

function mountPanel() {
  return mount(PlanningSimulationPanel, {
    props: {
      siteId: 'site-1',
      branchId: 'branch-1',
      datasets: [dataset],
    },
    global: {
      plugins: [
        ElementPlus,
        createI18n({
          legacy: false,
          locale: 'zh-CN',
          messages: { 'zh-CN': {} },
        }),
      ],
      directives: { permission: {} },
    },
  })
}
