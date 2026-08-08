import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import AdvancedPanel from '../AdvancedPanel.vue'
import type { SpaceRuntimeTaskItem, SpaceRuntimeTaskPathResponse } from '@/types/space/runtime'

vi.mock('vue-i18n', () => ({
  useI18n: () => ({ t: (value: string) => value }),
}))

const source = {
  kind: 'Real' as const,
  adapterId: 'cp6-wms-v1',
  dataSourceId: 'CP6_WMS',
  observedAtUtc: '2026-08-01T12:00:00Z',
  receivedAtUtc: '2026-08-01T12:00:01Z',
  delayMilliseconds: 1000,
  clockSkewMilliseconds: 0,
  isSimulated: false,
  isAvailable: true,
}

const stop = (sequenceNo: number, floorCode: string): SpaceRuntimeTaskItem => ({
  taskId: 'PICK-001', taskType: 'Pick', status: 'Released', sequenceNo,
  locationLogicalId: `L-${sequenceNo}`, wmsLogicalId: `W-${sequenceNo}`,
  spaceLocationCode: `${floorCode}-LOC-${sequenceNo}`,
  wmsLocationCode: `${floorCode}-LOC-${sequenceNo}`, codeMatches: true,
  floorLogicalId: floorCode, floorCode, floorName: floorCode, floorLevel: sequenceNo,
  zoneLogicalId: `Z-${floorCode}`, zoneCode: `ZONE-${floorCode}`,
  rackLogicalId: null, rackCode: null,
  anchorXMillimeters: sequenceNo * 1000, anchorYMillimeters: 500,
  anchorZMillimeters: 0, quantity: sequenceNo + 1, materialNumber: `SKU-${sequenceNo}`,
})

const actualStops = [stop(1, 'F1'), stop(2, 'F2')]
const taskPath: SpaceRuntimeTaskPathResponse = {
  siteId: 'S', publishedVersionId: 'V', warehouseCode: 'WH', source,
  taskId: 'PICK-001', stopCount: 2, locatedStopCount: 2,
  floorCount: 2, zoneCount: 2, floorTransitionCount: 1, zoneTransitionCount: 1,
  totalQuantity: 5, crossFloor: true, crossZone: true, actualStops,
  floors: [], aisles: [],
  workloads: [
    { floorLogicalId: 'F1', floorCode: 'F1', zoneLogicalId: 'Z-F1', zoneCode: 'ZONE-F1', stopCount: 1, totalQuantity: 2 },
    { floorLogicalId: 'F2', floorCode: 'F2', zoneLogicalId: 'Z-F2', zoneCode: 'ZONE-F2', stopCount: 1, totalQuantity: 3 },
  ],
}

describe('AdvancedPanel runtime task acceptance', () => {
  it('shows actual/optimized order, transitions, workload, and locates an actual stop', async () => {
    const wrapper = mount(AdvancedPanel, {
      props: {
        pathLoaded: true,
        pathLoading: false,
        pathInfo: 'path',
        compareInfo: 'comparison',
        taskPath,
        optimizedStops: [actualStops[1]!, actualStops[0]!],
        showOptimized: false,
        workloadOn: false,
        deviceOn: false,
        deviceLoading: false,
        deviceInfo: '',
        personnelOn: false,
        personnelLoading: false,
        trajectoryLoading: false,
        personnelInfo: '',
        taskSource: source,
        workloadSource: source,
        deviceSource: source,
      },
    })

    const text = wrapper.text()
    expect(text).toContain('实际顺序（WMS）')
    expect(text).toContain('优化顺序（仅演示，不回写 WMS）')
    const badges = wrapper.findAll('.ap-badge-warn')
    expect(badges[0]!.text()).toContain('跨层')
    expect(badges[0]!.text()).toContain('×1')
    expect(badges[1]!.text()).toContain('跨区')
    expect(badges[1]!.text()).toContain('×1')
    expect(text).toContain('F1/ZONE-F1: 1 点 / 2')

    await wrapper.find('.ap-stop').trigger('click')
    expect(wrapper.emitted('locate-task-stop')?.[0]?.[0]).toMatchObject({
      sequenceNo: 1,
      spaceLocationCode: 'F1-LOC-1',
    })
  })

  it('distinguishes an authoritative empty task result from an unavailable source', () => {
    const common = {
      pathLoaded: false, pathLoading: false, pathInfo: '', compareInfo: '',
      optimizedStops: [], showOptimized: false, workloadOn: false, deviceOn: false,
      deviceLoading: false, deviceInfo: '', personnelOn: false,
      personnelLoading: false, trajectoryLoading: false, personnelInfo: '',
      taskSource: source, workloadSource: source, deviceSource: source,
    }
    const empty = mount(AdvancedPanel, {
      props: {
        ...common,
        taskPath: {
          ...taskPath,
          stopCount: 0,
          locatedStopCount: 0,
          actualStops: [],
          floors: [],
          workloads: [],
        },
      },
    })
    expect(empty.text()).toContain('可用数据源中没有找到该任务')

    const unavailable = mount(AdvancedPanel, {
      props: {
        ...common,
        taskPath: {
          ...taskPath,
          source: { ...source, kind: 'Unavailable', isAvailable: false },
          stopCount: 0,
          locatedStopCount: 0,
          actualStops: [],
          floors: [],
          workloads: [],
        },
      },
    })
    expect(unavailable.text()).toContain('任务数据源不可用，不能判定任务是否存在')
    expect(unavailable.text()).not.toContain('可用数据源中没有找到该任务')
  })

  it('explains device position provenance and emits a bounded refresh', async () => {
    const wrapper = mount(AdvancedPanel, {
      props: {
        pathLoaded: false, pathLoading: false, pathInfo: '', compareInfo: '',
        taskPath: null, optimizedStops: [], showOptimized: false,
        workloadOn: false, deviceOn: true, deviceLoading: false,
        deviceInfo: '当前设备 2，来源 XYZ 1 / Published 锚点 1',
        personnelOn: false, personnelLoading: false, trajectoryLoading: false,
        personnelInfo: '', taskSource: source, workloadSource: source,
        deviceSource: source,
      },
    })

    expect(wrapper.text()).toContain('来源 XYZ 优先')
    expect(wrapper.text()).toContain('Published 元素锚点')
    const refresh = wrapper.findAll('button').find(button => button.text() === '刷新')
    expect(refresh).toBeDefined()
    await refresh!.trigger('click')
    expect(wrapper.emitted('refresh-device')).toHaveLength(1)
  })
})
