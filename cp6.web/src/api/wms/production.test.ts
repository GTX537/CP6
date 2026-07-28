import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../http', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
  },
}))

import http from '../http'
import { productionApi } from './production'

describe('WMS production administration API', () => {
  beforeEach(() => vi.clearAllMocks())

  it('updates a warehouse rollout flag with bounded retention data', () => {
    productionApi.updateFeatureFlag({
      warehouseCd: 'W 01',
      productionMoveEnabled: true,
      serialLpnEnabled: false,
      scanRetentionDays: 180,
      rowVersion: 'AQID',
    })

    expect(http.put).toHaveBeenCalledWith(
      '/v2/admin/wms-features/W%2001',
      {
        productionMoveEnabled: true,
        serialLpnEnabled: false,
        scanRetentionDays: 180,
      },
    )
  })

  it('adds a unique operation ID to serial and label commands', () => {
    productionApi.postSerial({
      txnType: 'MOVE',
      productCd: 'P-1',
      serialNos: ['S-1'],
      warehouseCd: 'W01',
      lotNo: '',
      fromLocationCd: 'A01',
      toLocationCd: 'B01',
    })
    productionApi.createLabelJob({ warehouseCd: 'W01', templateName: 'LPN' })

    expect(http.post).toHaveBeenNthCalledWith(
      1,
      '/v2/wms/serials',
      expect.objectContaining({ operationId: expect.any(String), txnType: 'MOVE' }),
    )
    expect(http.post).toHaveBeenNthCalledWith(
      2,
      '/v2/wms/label-jobs',
      expect.objectContaining({ operationId: expect.any(String), templateName: 'LPN' }),
    )
  })

  it('encodes LPN numbers in lifecycle routes and preserves row version', () => {
    productionApi.lpnCommand('PALLET/01', 'split', {
      rowVersion: 'row-version',
      targetLpnNo: 'PALLET-02',
      targetContainerType: 'PALLET',
      serialNos: ['S-1'],
      childLpns: [],
    })

    expect(http.post).toHaveBeenCalledWith(
      '/v2/wms/lpns/PALLET%2F01/split',
      expect.objectContaining({
        operationId: expect.any(String),
        rowVersion: 'row-version',
        targetLpnNo: 'PALLET-02',
      }),
    )
  })

  it('preserves a supplied operation ID when an uncertain command is retried', () => {
    productionApi.postSerial({
      operationId: 'fixed-operation-id',
      txnType: 'COUNT',
      productCd: 'P-1',
      serialNos: ['S-1'],
      warehouseCd: 'W01',
      lotNo: '',
      fromLocationCd: 'A01',
    })

    expect(http.post).toHaveBeenCalledWith(
      '/v2/wms/serials',
      expect.objectContaining({ operationId: 'fixed-operation-id' }),
    )
  })

  it('replaces a role warehouse and area scope as one atomic collection', () => {
    productionApi.replaceRoleScopes(20, [
      { warehouseCd: 'W01', areaCd: 'PICK-A' },
      { warehouseCd: 'W02' },
    ])

    expect(http.put).toHaveBeenCalledWith(
      '/v2/admin/wms-role-scopes/20',
      {
        scopes: [
          { warehouseCd: 'W01', areaCd: 'PICK-A' },
          { warehouseCd: 'W02' },
        ],
      },
    )
  })
})
