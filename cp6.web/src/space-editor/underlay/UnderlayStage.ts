import Konva from 'konva'
import type { ISpaceSceneFloorDto } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  releaseDecodedUnderlay,
  type DecodedUnderlay,
} from './decodeUnderlay'
import { buildUnderlayRenderPlan } from './underlayPlan'
import type { UnderlayPixelPoint } from './underlayCalibration'
import type { ViewState } from '@/space-editor/coords'

export interface UnderlayLayerState {
  visible: boolean
  opacity: number
  locked: boolean
}

export class UnderlayStage {
  readonly stage: Konva.Stage
  readonly layer: Konva.Layer
  private bitmap: DecodedUnderlay | null = null
  private floor: ISpaceSceneFloorDto | null = null
  private underlayGroup: Konva.Group | null = null
  private calibrationPoints: UnderlayPixelPoint[] = []
  private calibrationEnabled = false
  private calibrationHandler:
    | ((point: UnderlayPixelPoint) => void)
    | null = null
  private state: UnderlayLayerState = {
    visible: true,
    opacity: 0.55,
    locked: true,
  }
  private viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'> = {
    panX: 0,
    panY: 0,
    zoom: 0.05,
  }

  constructor(container: HTMLDivElement) {
    this.stage = new Konva.Stage({
      container,
      width: container.clientWidth || 1000,
      height: container.clientHeight || 700,
    })
    this.layer = new Konva.Layer()
    this.stage.add(this.layer)
    this.stage.on('pointerdown.calibration', () => {
      if (!this.calibrationEnabled || !this.underlayGroup || !this.bitmap) return
      const pointer = this.stage.getPointerPosition()
      if (!pointer) return
      const local = this.underlayGroup
        .getAbsoluteTransform()
        .copy()
        .invert()
        .point(pointer)
      const point = {
        x: local.x * this.bitmap.width / this.underlayGroup.width(),
        y: local.y * this.bitmap.height / this.underlayGroup.height(),
      }
      if (
        point.x < 0 ||
        point.y < 0 ||
        point.x > this.bitmap.width ||
        point.y > this.bitmap.height
      ) {
        return
      }
      this.calibrationHandler?.({
        x: Math.round(point.x * 1000) / 1000,
        y: Math.round(point.y * 1000) / 1000,
      })
    })
  }

  setContent(
    bitmap: DecodedUnderlay | null,
    floor: ISpaceSceneFloorDto | null,
  ): void {
    if (this.bitmap?.image !== bitmap?.image) {
      releaseDecodedUnderlay(this.bitmap)
    }
    this.bitmap = bitmap
    this.floor = floor
    this.render()
  }

  setLayerState(next: Partial<UnderlayLayerState>): void {
    const opacity = next.opacity ?? this.state.opacity
    if (!Number.isFinite(opacity) || opacity < 0 || opacity > 1) {
      throw new Error('Underlay opacity must be between 0 and 1')
    }
    this.state = {
      ...this.state,
      ...next,
      opacity,
    }
    this.render()
  }

  setFloor(floor: ISpaceSceneFloorDto | null): void {
    this.floor = floor
    this.render()
  }

  setViewport(viewport: Pick<ViewState, 'panX' | 'panY' | 'zoom'>): void {
    if (
      !Number.isFinite(viewport.panX)
      || !Number.isFinite(viewport.panY)
      || !Number.isFinite(viewport.zoom)
      || viewport.zoom <= 0
      || viewport.zoom > 1
    ) {
      throw new Error('Underlay viewport is invalid')
    }
    this.viewport = { ...viewport }
    this.render()
  }

  setCalibrationSelection(
    enabled: boolean,
    points: UnderlayPixelPoint[],
    handler?: (point: UnderlayPixelPoint) => void,
  ): void {
    this.calibrationEnabled = enabled
    this.calibrationPoints = points.slice(0, 3)
    this.calibrationHandler = handler ?? null
    this.stage.container().style.cursor = enabled ? 'crosshair' : 'default'
    this.render()
  }

  getRasterSize(): { width: number; height: number } | null {
    return this.bitmap
      ? { width: this.bitmap.width, height: this.bitmap.height }
      : null
  }

  resize(width: number, height: number): void {
    if (width <= 0 || height <= 0) return
    this.stage.size({ width, height })
    this.render()
  }

  destroy(): void {
    releaseDecodedUnderlay(this.bitmap)
    this.bitmap = null
    this.calibrationHandler = null
    this.stage.destroy()
  }

  private render(): void {
    this.layer.destroyChildren()
    this.underlayGroup = null
    if (!this.bitmap || !this.floor) {
      this.layer.batchDraw()
      return
    }

    const plan = buildUnderlayRenderPlan(
      {
        pixelWidth: this.bitmap.width,
        pixelHeight: this.bitmap.height,
        millimetersPerPixel: this.floor.underlayScale,
        offsetX: this.floor.underlayOffsetX ?? 0,
        offsetY: this.floor.underlayOffsetY ?? 0,
        rotationZ: this.floor.underlayRotationZ ?? 0,
      },
      {
        width: this.stage.width(),
        height: this.stage.height(),
        ...this.viewport,
      },
    )
    const group = new Konva.Group({
      name: 'underlay-transform',
      x: plan.x,
      y: plan.y,
      width: plan.width,
      height: plan.height,
      rotation: plan.rotation,
      offsetY: plan.imageOffsetY,
      listening: false,
    })
    const image = new Konva.Image({
      name: 'underlay',
      image: this.bitmap.image,
      x: 0,
      y: 0,
      width: plan.width,
      height: plan.height,
      opacity: this.state.opacity,
      visible: this.state.visible,
      listening: false,
      draggable: false,
    })
    image.setAttr('calibrated', plan.calibrated)
    image.setAttr('locked', this.state.locked)
    group.add(image)
    for (const [index, point] of this.calibrationPoints.entries()) {
      const x = point.x * plan.width / this.bitmap.width
      const y = point.y * plan.height / this.bitmap.height
      group.add(new Konva.Circle({
        name: 'calibration-point',
        x,
        y,
        radius: 7,
        fill: index === 2 ? '#f59e0b' : '#2563eb',
        stroke: '#ffffff',
        strokeWidth: 2,
        listening: false,
      }))
      group.add(new Konva.Text({
        x: x + 10,
        y: y - 10,
        text: index === 2 ? 'V' : `P${index + 1}`,
        fill: '#111827',
        fontSize: 14,
        fontStyle: 'bold',
        listening: false,
      }))
    }
    this.underlayGroup = group
    this.layer.add(group)
    this.layer.batchDraw()
  }
}
