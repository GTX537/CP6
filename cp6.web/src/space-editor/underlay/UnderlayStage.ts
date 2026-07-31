import Konva from 'konva'
import type { ISpaceSceneFloorDto } from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  releaseDecodedUnderlay,
  type DecodedUnderlay,
} from './decodeUnderlay'
import { buildUnderlayRenderPlan } from './underlayPlan'

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
  private state: UnderlayLayerState = {
    visible: true,
    opacity: 0.55,
    locked: true,
  }

  constructor(container: HTMLDivElement) {
    this.stage = new Konva.Stage({
      container,
      width: container.clientWidth || 1000,
      height: container.clientHeight || 700,
    })
    this.layer = new Konva.Layer()
    this.stage.add(this.layer)
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

  resize(width: number, height: number): void {
    if (width <= 0 || height <= 0) return
    this.stage.size({ width, height })
    this.render()
  }

  destroy(): void {
    releaseDecodedUnderlay(this.bitmap)
    this.bitmap = null
    this.stage.destroy()
  }

  private render(): void {
    this.layer.destroyChildren()
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
        zoom: 0.05,
        panX: 0,
        panY: 0,
      },
    )
    const image = new Konva.Image({
      name: 'underlay',
      image: this.bitmap.image,
      x: plan.x,
      y: plan.y,
      width: plan.width,
      height: plan.height,
      rotation: plan.rotation,
      opacity: this.state.opacity,
      visible: this.state.visible,
      listening: !this.state.locked,
      draggable: false,
    })
    image.setAttr('calibrated', plan.calibrated)
    image.setAttr('locked', this.state.locked)
    this.layer.add(image)
    this.layer.batchDraw()
  }
}
