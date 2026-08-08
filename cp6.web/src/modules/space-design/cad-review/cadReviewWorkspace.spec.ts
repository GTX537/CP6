import { describe, expect, it } from 'vitest'
import {
  cadReviewFreshness,
  filterCadReviewItems,
  parseCadReviewWorkspace,
  resolveCadReviewCanvasObject,
} from './cadReviewWorkspace'

const sha = (character: string) => character.repeat(64)
const floorLogicalId = '44444444-4444-4444-4444-444444444444'
const modelVersionId = 'aaaaaaaa-1111-2222-3333-444444444444'

function workspace() {
  return {
    schemaVersion: 1,
    isReadOnlyWorkspace: true,
    tenantId: '55555555-5555-5555-5555-555555555555',
    modelVersionId,
    floorLogicalId,
    floorCode: 'F01',
    diagnosticIndexSha256: sha('a'),
    matchPreviewSha256: sha('b'),
    editorContentRevision: 7,
    editorContentHash: sha('c'),
    editorSnapshotSha256: sha('d'),
    items: [
      {
        reviewItemId: 'cad-review-open',
        trackingKey: 'cad-proposal:H:160:Rack:-',
        kind: 'LowConfidenceProposal',
        severity: 'Warning',
        status: 'Open',
        code: 'SPACE_CAD_LOW_CONFIDENCE',
        relatedCodes: [],
        suggestedActionCode: 'review-candidate',
        sourceRef: 'H:160',
        previewObjectId: 'cad-preview-1',
        confidenceBand: 'Low',
        location: {
          kind: 'Entity',
          floorLogicalId,
          layerId: 'RACK',
          sourceRef: 'H:160',
          previewObjectId: 'cad-preview-1',
          bounds: { minX: 3000, minY: 3500, maxX: 3000, maxY: 3500 },
          anchor: { x: 3000, y: 3500, z: 0 },
          suggestedPaddingMillimeters: 250,
          canFocusCanvas: true,
        },
        upstreamEvidenceSha256: sha('e'),
      },
      {
        reviewItemId: 'cad-review-resolved',
        trackingKey: 'excel-match:row-1:Unmatched',
        kind: 'ExcelUnmatched',
        severity: 'Warning',
        status: 'Resolved',
        code: 'SPACE_EXCEL_CAD_UNMATCHED',
        relatedCodes: [],
        suggestedActionCode: 'map-source-or-rack-code',
        rackCode: 'R-404',
        location: {
          kind: 'Document',
          floorLogicalId,
          suggestedPaddingMillimeters: 0,
          canFocusCanvas: false,
        },
        upstreamEvidenceSha256: sha('f'),
        resolvedFromWorkspaceSha256: sha('1'),
      },
    ],
    summary: {
      totalCount: 2,
      openCount: 1,
      resolvedCount: 1,
      openInfoCount: 0,
      openWarningCount: 1,
      openBlockingCount: 0,
      locatableCount: 1,
      unlocatableCount: 1,
      cadDiagnosticCount: 0,
      proposalReviewCount: 1,
      excelReviewCount: 1,
    },
    workspaceSha256: sha('2'),
  }
}

describe('cadReviewWorkspace', () => {
  it('parses a bounded workspace and filters by status, location and search', () => {
    const parsed = parseCadReviewWorkspace(JSON.stringify(workspace()))

    expect(filterCadReviewItems(parsed, {
      status: 'Open',
      onlyLocatable: true,
      search: 'h:160',
    })).toEqual([parsed.items[0]])
    expect(filterCadReviewItems(parsed, {
      status: 'Resolved',
      kind: 'ExcelUnmatched',
      search: 'r-404',
    })).toEqual([parsed.items[1]])
  })

  it('rejects inconsistent summaries and cross-floor locations', () => {
    const badSummary = workspace()
    badSummary.summary.openCount = 2
    expect(() => parseCadReviewWorkspace(badSummary)).toThrow(/summary.openCount/)

    const badFloor = workspace()
    badFloor.items[0]!.location.floorLogicalId =
      '77777777-7777-7777-7777-777777777777'
    expect(() => parseCadReviewWorkspace(badFloor)).toThrow(/another floor/)

    const emptyTenant = workspace()
    emptyTenant.tenantId = '00000000-0000-0000-0000-000000000000'
    expect(() => parseCadReviewWorkspace(emptyTenant)).toThrow(/tenantId/)
  })

  it('fails freshness when model, floor, revision or content hash drift', () => {
    const parsed = parseCadReviewWorkspace(workspace())
    expect(cadReviewFreshness(parsed, {
      modelVersionId,
      floorLogicalId,
      contentRevision: 7,
      contentHash: sha('c'),
    })).toEqual({ fresh: true, reasons: [] })

    expect(cadReviewFreshness(parsed, {
      modelVersionId: 'bbbbbbbb-1111-2222-3333-444444444444',
      floorLogicalId: '77777777-7777-7777-7777-777777777777',
      contentRevision: 8,
      contentHash: sha('9'),
    })).toEqual({
      fresh: false,
      reasons: ['model', 'floor', 'revision', 'contentHash'],
    })
  })

  it('resolves an applied canvas object by LogicalId first or exact SourceRef', () => {
    const parsed = parseCadReviewWorkspace(workspace())
    const item = parsed.items[0]!
    const rack = {
      revision: {
        logicalId: '88888888-8888-8888-8888-888888888888',
        sourceRef: 'H:160',
      },
    }
    expect(resolveCadReviewCanvasObject(item, [rack], [])).toEqual({
      logicalId: rack.revision.logicalId,
      ownerKind: 'Rack',
    })

    const logicalItem = {
      ...item,
      sourceRef: undefined,
      targetLogicalId: '77777777-7777-7777-7777-777777777777',
    }
    expect(resolveCadReviewCanvasObject(logicalItem, [], [{
      revision: {
        logicalId: '77777777-7777-7777-7777-777777777777',
        sourceRef: null,
      },
    }])).toEqual({
      logicalId: '77777777-7777-7777-7777-777777777777',
      ownerKind: 'Element',
    })
  })
})
