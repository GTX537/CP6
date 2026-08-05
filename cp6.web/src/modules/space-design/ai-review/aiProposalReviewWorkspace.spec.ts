import { describe, expect, it } from 'vitest'
import {
  aiReviewFreshness,
  filterAiReviewItems,
  parseAiProposalReviewWorkspace,
  previewAiReviewBatch,
  summarizeAiReviewItems,
  type AiProposalReviewWorkspace,
  type AiReviewItem,
} from './aiProposalReviewWorkspace'

const hash = 'a'.repeat(64)
const item: AiReviewItem = {
  reviewItemId: `ai-review-${'b'.repeat(32)}`,
  logicalId: '11111111-1111-4111-8111-111111111111',
  sourceKey: 'local-key-1',
  sourceRef: 'H:160',
  objectType: 'Rack',
  confidence: 0.97,
  confidenceBand: 'High',
  readiness: 'Ready',
  hasBlockingIssue: false,
  canBatchAccept: true,
  location: {
    floorLogicalId: '22222222-2222-4222-8222-222222222222',
    sourceRef: 'H:160',
    bounds: { minX: 0, minY: 0, maxX: 3000, maxY: 2000 },
    anchor: { x: 1500, y: 1000, z: 0 },
    suggestedPaddingMillimeters: 2000,
    canFocusCanvas: true,
  },
  fields: [{
    fieldPath: 'type',
    valueToken: 'Rack',
    winningSource: 'DeterministicRule',
    confidence: 0.97,
    evidence: [{
      source: 'DeterministicRule',
      valueToken: 'Rack',
      confidence: 0.97,
      evidenceCodes: ['LAYER_NAME'],
    }],
  }],
  relations: [],
  rackDerivation: {
    profileVersionId: '33333333-3333-4333-8333-333333333333',
    profileSha256: hash,
    winningSource: 'ExplicitSelected',
    rackWidthMillimeters: 3000,
    rackDepthMillimeters: 2000,
    rackHeightMillimeters: 4000,
    locationCount: 8,
    levels: [{
      logicalId: '44444444-4444-4444-8444-444444444444',
      levelNo: 1,
      locationCount: 8,
    }],
  },
  issues: [],
  difference: {
    kind: 'Added',
    geometryChanged: true,
    afterGeometrySha256: hash,
    afterGeometryBounds: { minX: 0, minY: 0, maxX: 3000, maxY: 2000 },
    fields: [{
      fieldPath: 'type',
      kind: 'Added',
      afterValueToken: 'Rack',
      winningSource: 'DeterministicRule',
      confidence: 0.97,
      evidence: [],
    }],
    beforeRackLevelCount: 0,
    afterRackLevelCount: 1,
    beforeLocationCount: 0,
    afterLocationCount: 8,
  },
}

function workspace(): AiProposalReviewWorkspace {
  const items = [structuredClone(item)]
  const runIssues: AiProposalReviewWorkspace['runIssues'] = []
  return {
    schemaVersion: 1,
    isReadOnlyWorkspace: true,
    decisionWritten: false,
    draftWritten: false,
    tenantId: '55555555-5555-4555-8555-555555555555',
    modelVersionId: '66666666-6666-6666-6666-666666666666',
    floorLogicalId: '22222222-2222-4222-8222-222222222222',
    proposalSetSha256: hash,
    baselineSnapshotSha256: hash,
    baselineContentRevision: 12,
    baselineContentHash: hash,
    runIssues,
    items,
    summary: summarizeAiReviewItems(items, runIssues),
    reviewEtag: hash,
    workspaceSha256: hash,
  }
}

describe('aiProposalReviewWorkspace', () => {
  it('parses, filters and preserves source evidence', () => {
    const parsed = parseAiProposalReviewWorkspace(JSON.stringify(workspace()))

    expect(filterAiReviewItems(parsed, {
      confidenceBand: 'High',
      readiness: 'Ready',
      differenceKind: 'Added',
      search: 'layer_name',
    })).toHaveLength(1)
    expect(filterAiReviewItems(parsed, {
      confidenceBand: 'High',
      search: 'H:160',
    })[0]?.fields[0]?.evidence[0]?.evidenceCodes).toContain('LAYER_NAME')
  })

  it('detects stale Draft baselines', () => {
    const parsed = parseAiProposalReviewWorkspace(workspace())

    expect(aiReviewFreshness(parsed, {
      modelVersionId: parsed.modelVersionId,
      floorLogicalId: parsed.floorLogicalId,
      contentRevision: 12,
      contentHash: hash,
    }).fresh).toBe(true)
    expect(aiReviewFreshness(parsed, {
      modelVersionId: parsed.modelVersionId,
      floorLogicalId: parsed.floorLogicalId,
      contentRevision: 13,
      contentHash: hash,
    })).toEqual({ fresh: false, reasons: ['revision'] })
  })

  it('previews selection without writing Decision or Draft', () => {
    const parsed = parseAiProposalReviewWorkspace(workspace())
    const preview = previewAiReviewBatch(parsed, [item.reviewItemId])

    expect(preview.acceptEligibleIds).toEqual([item.reviewItemId])
    expect(preview.rejectEligibleIds).toEqual([item.reviewItemId])
    expect(preview.requiresServerRevalidation).toBe(true)
    expect(preview.decisionWritten).toBe(false)
    expect(preview.draftWritten).toBe(false)
  })

  it('rejects tampered summaries and write markers', () => {
    const badSummary = workspace()
    badSummary.summary.totalCount = 2
    expect(() => parseAiProposalReviewWorkspace(badSummary)).toThrow(/summary.totalCount/)

    const written = workspace()
    written.decisionWritten = true
    expect(() => parseAiProposalReviewWorkspace(written)).toThrow(/identity/)
  })
})
