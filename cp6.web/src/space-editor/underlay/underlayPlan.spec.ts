import { describe, expect, it } from 'vitest'
import { buildUnderlayRenderPlan } from './underlayPlan'

describe('buildUnderlayRenderPlan', () => {
  it('maps calibrated millimeters into the Z-up editor screen frame', () => {
    const plan = buildUnderlayRenderPlan(
      {
        pixelWidth: 1000,
        pixelHeight: 500,
        millimetersPerPixel: 10,
        offsetX: 2000,
        offsetY: 3000,
        rotationZ: 90,
      },
      {
        width: 1200,
        height: 800,
        zoom: 0.05,
        panX: 1000,
        panY: 1000,
      },
    )

    expect(plan).toEqual({
      x: 50,
      y: 450,
      width: 500,
      height: 250,
      rotation: -90,
      millimetersPerPixel: 10,
      calibrated: true,
    })
  })

  it('fits an uncalibrated image without inventing persisted calibration', () => {
    const plan = buildUnderlayRenderPlan(
      {
        pixelWidth: 2000,
        pixelHeight: 1000,
        millimetersPerPixel: null,
        offsetX: 0,
        offsetY: 0,
        rotationZ: 0,
      },
      {
        width: 1000,
        height: 600,
        zoom: 0.05,
        panX: 0,
        panY: 0,
      },
    )

    expect(plan.calibrated).toBe(false)
    expect(plan.width).toBe(800)
    expect(plan.height).toBe(400)
    expect(plan.millimetersPerPixel).toBe(8)
  })

  it('rejects invalid dimensions and calibration', () => {
    const viewport = {
      width: 1000,
      height: 600,
      zoom: 0.05,
      panX: 0,
      panY: 0,
    }

    expect(() =>
      buildUnderlayRenderPlan(
        {
          pixelWidth: 0,
          pixelHeight: 100,
          offsetX: 0,
          offsetY: 0,
          rotationZ: 0,
        },
        viewport,
      ),
    ).toThrow('pixelWidth')
    expect(() =>
      buildUnderlayRenderPlan(
        {
          pixelWidth: 100,
          pixelHeight: 100,
          millimetersPerPixel: -1,
          offsetX: 0,
          offsetY: 0,
          rotationZ: 0,
        },
        viewport,
      ),
    ).toThrow('millimetersPerPixel')
  })
})
