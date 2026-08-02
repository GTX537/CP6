import { describe, expect, it, vi } from 'vitest'
import { Group, Mesh, MeshBasicMaterial } from 'three'
import { DeviceLayer } from './DeviceLayer'
import type { SpaceDeviceCurrent } from '@/types/space/runtime'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}

const device = (overrides: Partial<SpaceDeviceCurrent> = {}): SpaceDeviceCurrent => ({
  mappingId: 'mapping-1',
  sourceId: 'WCS-01',
  sourceKind: 'Real',
  deviceExternalId: 'AGV-01',
  deviceKind: 'Agv',
  elementLogicalId: 'element-1',
  elementType: 'Device',
  mappingIsCurrent: true,
  mappedFloorLogicalId: 'floor-1',
  mappedXMillimeters: 10,
  mappedYMillimeters: 20,
  mappedZMillimeters: 0,
  operatingState: 'Running',
  floorLogicalId: 'floor-1',
  locationLogicalId: null,
  xMillimeters: 100,
  yMillimeters: 200,
  zMillimeters: 5,
  accuracyMillimeters: 25,
  positionOccurredAtUtc: '2026-08-02T17:59:00Z',
  positionReceivedAtUtc: '2026-08-02T17:59:01Z',
  positionEventId: 'position-event-1',
  positionSourceEventId: 'POSITION-1',
  operatingStateOccurredAtUtc: '2026-08-02T17:59:00Z',
  operatingStateReceivedAtUtc: '2026-08-02T17:59:01Z',
  operatingStateEventId: 'state-event-1',
  operatingStateSourceEventId: 'STATE-1',
  positionAgeMilliseconds: 60_000,
  operatingStateAgeMilliseconds: 60_000,
  hasPosition: true,
  positionIsStale: false,
  operatingStateIsStale: false,
  isSimulated: false,
  hasActiveAlarm: false,
  activeAlarmCount: 0,
  maximumActiveAlarmSeverity: null,
  activeAlarms: [],
  ...overrides,
})

describe('DeviceLayer', () => {
  it('prefers explicit runtime XYZ and preserves event traceability', () => {
    const viewer = fakeViewer()
    const layer = new DeviceLayer(viewer as never)
    layer.setDevices([device()], 'floor-1')

    expect(layer.count).toBe(1)
    expect(layer.runtimeCount).toBe(1)
    expect(layer.mappedAnchorCount).toBe(0)
    const item = viewer.root.children[0]!.children[0]!
    expect(item.position.x).toBe(100)
    expect(item.position.y).toBe(200)
    expect(item.userData['positionSource']).toBe('Runtime')
    expect(item.userData['positionEventId']).toBe('position-event-1')
  })

  it('uses the current Published mapping anchor only when runtime XYZ is absent', () => {
    const viewer = fakeViewer()
    const layer = new DeviceLayer(viewer as never)
    layer.setDevices([
      device({
        floorLogicalId: null,
        xMillimeters: null,
        yMillimeters: null,
        zMillimeters: null,
        hasPosition: false,
      }),
      device({
        deviceExternalId: 'AGV-02',
        mappingIsCurrent: false,
        floorLogicalId: null,
        xMillimeters: null,
        yMillimeters: null,
        zMillimeters: null,
      }),
    ], 'floor-1')

    expect(layer.count).toBe(1)
    expect(layer.mappedAnchorCount).toBe(1)
    const item = viewer.root.children[0]!.children[0]!
    expect(item.position.x).toBe(10)
    expect(item.userData['positionSource']).toBe('MappedElement')
  })

  it('renders simulated stale alarm state distinctly with evidence IDs', () => {
    const viewer = fakeViewer()
    const layer = new DeviceLayer(viewer as never)
    layer.setDevices([device({
      sourceKind: 'Simulated',
      isSimulated: true,
      positionIsStale: true,
      operatingState: 'Faulted',
      hasActiveAlarm: true,
      activeAlarmCount: 1,
      maximumActiveAlarmSeverity: 'Critical',
      activeAlarms: [{
        alarmExternalId: 'ALARM-1',
        alarmCode: 'MOTOR-OVERHEAT',
        alarmSeverity: 'Critical',
        alarmMessage: null,
        occurredAtUtc: '2026-08-02T17:58:00Z',
        receivedAtUtc: '2026-08-02T17:58:01Z',
        eventId: 'alarm-event-1',
        sourceEventId: 'ALARM-RAISED-1',
        ageMilliseconds: 120_000,
      }],
    })], 'floor-1')

    expect(layer.alarmCount).toBe(1)
    expect(layer.staleCount).toBe(1)
    const item = viewer.root.children[0]!.children[0]!
    const body = item.children.find(child => child.name === 'device-body') as Mesh
    const material = body.material as MeshBasicMaterial
    expect(material.wireframe).toBe(true)
    expect(material.opacity).toBeLessThan(0.5)
    expect(item.children.some(child => child.name === 'device-active-alarm')).toBe(true)
    expect(item.userData['alarmEventIds']).toEqual(['alarm-event-1'])
  })

  it('clear disposes GPU resources and removes the layer group', () => {
    const viewer = fakeViewer()
    const layer = new DeviceLayer(viewer as never)
    layer.setDevices([device()], 'floor-1')
    const body = viewer.root.children[0]!.children[0]!.children[0] as Mesh
    const geometryDispose = vi.spyOn(body.geometry, 'dispose')
    const materialDispose = vi.spyOn(body.material as MeshBasicMaterial, 'dispose')

    layer.clear()

    expect(geometryDispose).toHaveBeenCalledOnce()
    expect(materialDispose).toHaveBeenCalledOnce()
    expect(layer.count).toBe(0)
    expect(viewer.root.children).toHaveLength(0)
  })
})
