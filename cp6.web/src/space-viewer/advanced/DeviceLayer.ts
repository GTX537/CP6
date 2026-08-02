import {
  BoxGeometry,
  Group,
  Mesh,
  MeshBasicMaterial,
  TorusGeometry,
} from 'three'
import type { Material, Object3D } from 'three'
import type { ViewerHandle } from '../api/ViewerHandle'
import type {
  SpaceDeviceAlarmSeverity,
  SpaceDeviceCurrent,
  SpaceDeviceOperatingState,
} from '@/types/space/runtime'

const BODY_DIMENSIONS: Record<string, readonly [number, number, number]> = {
  Agv: [900, 650, 420],
  Conveyor: [1_600, 700, 320],
}
const DEFAULT_DIMENSIONS: readonly [number, number, number] = [850, 650, 500]
const STATE_COLORS: Record<SpaceDeviceOperatingState, number> = {
  Unknown: 0x78909c,
  Offline: 0x455a64,
  Idle: 0x42a5f5,
  Running: 0x43a047,
  Paused: 0xf9a825,
  Faulted: 0xe53935,
  Maintenance: 0x8e24aa,
}
const ALARM_COLORS: Record<SpaceDeviceAlarmSeverity, number> = {
  Info: 0x29b6f6,
  Warning: 0xfb8c00,
  Critical: 0xff1744,
}

type PositionSource = 'Runtime' | 'MappedElement'

/** Current device overlay. Runtime XYZ wins; the Published element anchor is an explicit fallback. */
export class DeviceLayer {
  private readonly _viewer: ViewerHandle
  private readonly _group = new Group()
  private _count = 0
  private _runtimeCount = 0
  private _mappedAnchorCount = 0
  private _alarmCount = 0
  private _staleCount = 0

  constructor(viewer: ViewerHandle) {
    this._viewer = viewer
    this._group.name = 'space-device-runtime-layer'
  }

  get count(): number { return this._count }
  get runtimeCount(): number { return this._runtimeCount }
  get mappedAnchorCount(): number { return this._mappedAnchorCount }
  get alarmCount(): number { return this._alarmCount }
  get staleCount(): number { return this._staleCount }

  setDevices(devices: readonly SpaceDeviceCurrent[], activeFloorId: string): void {
    this.clear(false)
    for (const device of devices) {
      const placement = this.resolvePlacement(device, activeFloorId)
      if (!placement) continue
      const [width, depth, height] = BODY_DIMENSIONS[device.deviceKind] ?? DEFAULT_DIMENSIONS
      const severity = device.maximumActiveAlarmSeverity
      const bodyColor = severity ? ALARM_COLORS[severity] : STATE_COLORS[device.operatingState]
      const opacity = device.positionIsStale || device.operatingStateIsStale ? 0.42 : 0.82
      const container = new Group()
      container.name = `space-device:${device.sourceId}:${device.deviceExternalId}`
      container.position.set(placement.x, placement.y, placement.z + height / 2)
      container.userData = {
        kind: 'space-device-current',
        mappingId: device.mappingId,
        sourceId: device.sourceId,
        sourceKind: device.sourceKind,
        deviceExternalId: device.deviceExternalId,
        deviceKind: device.deviceKind,
        elementLogicalId: device.elementLogicalId,
        positionSource: placement.source,
        positionEventId: device.positionEventId,
        positionSourceEventId: device.positionSourceEventId,
        operatingStateEventId: device.operatingStateEventId,
        operatingStateSourceEventId: device.operatingStateSourceEventId,
        alarmEventIds: device.activeAlarms.map(alarm => alarm.eventId),
        operatingState: device.operatingState,
        positionIsStale: device.positionIsStale,
        operatingStateIsStale: device.operatingStateIsStale,
        isSimulated: device.isSimulated,
        hasActiveAlarm: device.hasActiveAlarm,
      }

      const body = new Mesh(
        new BoxGeometry(width, depth, height),
        new MeshBasicMaterial({
          color: bodyColor,
          transparent: opacity < 1,
          opacity,
          wireframe: device.isSimulated,
        }),
      )
      body.name = 'device-body'
      container.add(body)

      if (severity) {
        const ring = new Mesh(
          new TorusGeometry(Math.max(width, depth) * 0.68, 55, 8, 32),
          new MeshBasicMaterial({
            color: ALARM_COLORS[severity],
            transparent: true,
            opacity: 0.92,
          }),
        )
        ring.name = 'device-active-alarm'
        ring.position.z = height * 0.65
        container.add(ring)
        this._alarmCount++
      }

      this._group.add(container)
      this._count++
      if (placement.source === 'Runtime') this._runtimeCount++
      else this._mappedAnchorCount++
      if (device.positionIsStale || device.operatingStateIsStale) this._staleCount++
    }
    if (this._count > 0) this._viewer.getSceneRoot().add(this._group)
    this._viewer.requestRender()
  }

  clear(requestRender = true): void {
    for (const child of [...this._group.children]) {
      this.disposeObject(child)
      this._group.remove(child)
    }
    if (this._group.parent) this._group.parent.remove(this._group)
    this._count = 0
    this._runtimeCount = 0
    this._mappedAnchorCount = 0
    this._alarmCount = 0
    this._staleCount = 0
    if (requestRender) this._viewer.requestRender()
  }

  private resolvePlacement(
    device: SpaceDeviceCurrent,
    activeFloorId: string,
  ): { x: number; y: number; z: number; source: PositionSource } | null {
    if (
      device.floorLogicalId === activeFloorId &&
      device.xMillimeters != null &&
      device.yMillimeters != null &&
      device.zMillimeters != null
    ) {
      return {
        x: device.xMillimeters,
        y: device.yMillimeters,
        z: device.zMillimeters,
        source: 'Runtime',
      }
    }
    if (
      device.mappingIsCurrent &&
      device.mappedFloorLogicalId === activeFloorId &&
      device.mappedXMillimeters != null &&
      device.mappedYMillimeters != null &&
      device.mappedZMillimeters != null
    ) {
      return {
        x: device.mappedXMillimeters,
        y: device.mappedYMillimeters,
        z: device.mappedZMillimeters,
        source: 'MappedElement',
      }
    }
    return null
  }

  private disposeObject(value: Object3D): void {
    value.traverse(child => {
      const mesh = child as Mesh
      mesh.geometry?.dispose()
      const material = mesh.material as Material | Material[] | undefined
      if (Array.isArray(material)) material.forEach(item => item.dispose())
      else material?.dispose()
    })
  }
}
