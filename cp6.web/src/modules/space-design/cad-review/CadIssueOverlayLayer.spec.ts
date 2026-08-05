import { describe, expect, it } from 'vitest'
import { buildCadIssueFocusPlan } from './CadIssueOverlayLayer'
import type { CadReviewItem } from './cadReviewWorkspace'

const floorLogicalId = '44444444-4444-4444-4444-444444444444'

function item(canFocusCanvas = true): CadReviewItem {
  return {
    reviewItemId: 'cad-review-1',
    trackingKey: 'cad-proposal:H:160:Rack:-',
    kind: 'LowConfidenceProposal',
    severity: 'Warning',
    status: 'Open',
    code: 'SPACE_CAD_LOW_CONFIDENCE',
    relatedCodes: [],
    suggestedActionCode: 'review-candidate',
    sourceRef: 'H:160',
    confidenceBand: 'Low',
    location: {
      kind: 'Entity',
      floorLogicalId,
      bounds: { minX: 3000, minY: 3500, maxX: 3000, maxY: 3500 },
      anchor: { x: 3000, y: 3500, z: 0 },
      suggestedPaddingMillimeters: 250,
      canFocusCanvas,
    },
    upstreamEvidenceSha256: 'a'.repeat(64),
  }
}

describe('buildCadIssueFocusPlan', () => {
  it('turns a zero-area CAD entity into a visible bounded marker', () => {
    const plan = buildCadIssueFocusPlan(item(), 700)

    expect(plan).toMatchObject({
      reviewItemId: 'cad-review-1',
      width: 25,
      height: 25,
      anchorX: 150,
      anchorY: 525,
      severity: 'Warning',
    })
    expect(plan?.label).toContain('H:160')
  })

  it('uses an anchor-only location and rejects unlocatable evidence', () => {
    const anchorOnly = item()
    delete anchorOnly.location.bounds
    expect(buildCadIssueFocusPlan(anchorOnly, 700)).toMatchObject({
      width: 18,
      height: 18,
      anchorX: 150,
      anchorY: 525,
    })
    expect(buildCadIssueFocusPlan(item(false), 700)).toBeNull()
  })

  it('honors the shared viewport used to center every canvas layer', () => {
    const plan = buildCadIssueFocusPlan(item(), 700, {
      panX: -7000,
      panY: -3500,
      zoom: 0.05,
    })

    expect(plan).toMatchObject({ anchorX: 500, anchorY: 350 })
  })
})
