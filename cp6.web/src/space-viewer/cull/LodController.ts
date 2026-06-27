export type LodTier = 'near' | 'medium' | 'far'

// Base thresholds (metres, world space camera-to-target distance)
const NEAR_MEDIUM = 80
const MEDIUM_FAR = 150
// Hysteresis band prevents oscillation when distance sits near a boundary
const HYSTERESIS = 8

export class LodController {
  private _tier: LodTier = 'near'

  get tier(): LodTier { return this._tier }

  /** Call each throttled tick. Returns the (possibly changed) tier. */
  update(distance: number): LodTier {
    switch (this._tier) {
      case 'near':
        if (distance > NEAR_MEDIUM + HYSTERESIS) this._tier = 'medium'
        break
      case 'medium':
        if (distance > MEDIUM_FAR + HYSTERESIS) this._tier = 'far'
        else if (distance < NEAR_MEDIUM - HYSTERESIS) this._tier = 'near'
        break
      case 'far':
        if (distance < MEDIUM_FAR - HYSTERESIS) this._tier = 'medium'
        break
    }
    return this._tier
  }
}
