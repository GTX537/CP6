import { describe, expect, it } from 'vitest'
import { calculateUnderlayCalibration } from './underlayCalibration'

describe('calculateUnderlayCalibration', () => {
  it('calculates scale and origin in the millimeter Y-up frame', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 1000, y: 3000 },
      },
    })

    expect(result).toEqual({
      millimetersPerPixel: 10,
      offsetX: 1000,
      offsetY: 2000,
      rotationZ: 0,
      validationErrorMillimeters: 0,
    })
  })

  it('calculates a positive 90 degree world rotation', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 1000, y: 3000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 0, y: 2000 },
      },
    })

    expect(result.rotationZ).toBe(90)
    expect(result.validationErrorMillimeters).toBe(0)
  })

  it('reports third point error before the server applies its threshold', () => {
    const result = calculateUnderlayCalibration({
      pixelWidth: 1000,
      pixelHeight: 500,
      point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
      point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
      validationPoint: {
        pixel: { x: 0, y: 400 },
        world: { x: 1100, y: 3000 },
      },
    })

    expect(result.validationErrorMillimeters).toBe(100)
  })

  it('rejects a validation point on the control line', () => {
    expect(() =>
      calculateUnderlayCalibration({
        pixelWidth: 1000,
        pixelHeight: 500,
        point1: { pixel: { x: 0, y: 500 }, world: { x: 1000, y: 2000 } },
        point2: { pixel: { x: 100, y: 500 }, world: { x: 2000, y: 2000 } },
        validationPoint: {
          pixel: { x: 50, y: 500 },
          world: { x: 1500, y: 2000 },
        },
      }),
    ).toThrow('Validation point')
  })
})
