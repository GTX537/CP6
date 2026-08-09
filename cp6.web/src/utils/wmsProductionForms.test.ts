import { describe, expect, it } from 'vitest'
import {
  buildCreateLpnCommand,
  buildLpnLifecycleCommand,
  buildSerialLifecycleCommand,
  buildSerialTrackingCommand,
  parseLpnContents,
  parseUniqueLines,
  ProductionFormValidationError,
  type LpnLifecycleForm,
  type SerialLifecycleForm,
} from './wmsProductionForms'

const serialForm = (overrides: Partial<SerialLifecycleForm> = {}): SerialLifecycleForm => ({
  txnType: 'MOVE',
  productCd: 'P-01',
  serialNosText: 'SN-01\nSN-02',
  warehouseCd: 'W01',
  lotNo: 'LOT-1',
  fromLocationCd: 'A-01',
  toLocationCd: 'B-01',
  lpnNo: '',
  deviceId: '',
  ...overrides,
})

const lpnForm = (overrides: Partial<LpnLifecycleForm> = {}): LpnLifecycleForm => ({
  lpnNo: '',
  containerType: '',
  warehouseCd: '',
  locationCd: '',
  deviceId: '',
  toLocationCd: '',
  childLpnsText: '',
  contentsText: '',
  serialNosText: '',
  targetLpnNo: '',
  targetContainerType: '',
  sourceLpnNo: '',
  ...overrides,
})

describe('WMS production command forms', () => {
  it('normalizes a valid serial MOVE command', () => {
    expect(buildSerialLifecycleCommand(serialForm())).toEqual({
      txnType: 'MOVE',
      productCd: 'P-01',
      serialNos: ['SN-01', 'SN-02'],
      warehouseCd: 'W01',
      lotNo: 'LOT-1',
      fromLocationCd: 'A-01',
      toLocationCd: 'B-01',
      lpnNo: undefined,
      deviceId: undefined,
    })
  })

  it.each([
    ['RECEIVE', 'toLocationCd', 'Target location is required for RECEIVE'],
    ['SHIP', 'fromLocationCd', 'Source location is required for SHIP'],
  ] as const)('requires lifecycle locations for %s', (txnType, field, message) => {
    expect(() => buildSerialLifecycleCommand(serialForm({
      txnType,
      [field]: '',
    }))).toThrow(message)
  })

  it('rejects duplicate serial values without case sensitivity', () => {
    expect(() => parseUniqueLines('Serial-A\n serial-a ', 'Serial numbers'))
      .toThrow('Serial numbers contains a duplicate value')
  })

  it('parses controlled-conversion rows and rejects duplicate serials', () => {
    expect(buildSerialTrackingCommand({
      productCd: 'P-01',
      trackingMode: 3,
      existingSerialsText: 'S1,W01,A01,L1\nS2,W01,A01,L1',
    }).existingSerials).toHaveLength(2)
    expect(() => buildSerialTrackingCommand({
      productCd: 'P-01',
      trackingMode: 2,
      existingSerialsText: 'S1,W01,A01,L1\ns1,W01,A01,L1',
    })).toThrow('duplicate value')
  })

  it('validates serialized and aggregate LPN content quantities', () => {
    expect(parseLpnContents('P1,L1,S1,1\nP2,,,2.5')).toEqual([
      { productCd: 'P1', lotNo: 'L1', serialNo: 'S1', qty: 1 },
      { productCd: 'P2', lotNo: '', serialNo: undefined, qty: 2.5 },
    ])
    expect(() => parseLpnContents('P1,L1,S1,2'))
      .toThrow('Serialized content on line 1 must have quantity 1')
  })

  it('builds typed create, pack, split, and merge LPN commands', () => {
    expect(buildCreateLpnCommand(lpnForm({
      lpnNo: 'PALLET-01',
      containerType: 'PALLET',
      warehouseCd: 'W01',
      locationCd: 'A01',
    }))).toEqual({
      lpnNo: 'PALLET-01',
      containerType: 'PALLET',
      warehouseCd: 'W01',
      locationCd: 'A01',
      deviceId: undefined,
    })
    expect(buildLpnLifecycleCommand('pack', 'rv1', lpnForm({
      childLpnsText: 'CASE-01',
      contentsText: 'P1,L1,S1,1',
    }))).toEqual({
      rowVersion: 'rv1',
      deviceId: undefined,
      childLpns: ['CASE-01'],
      contents: [{ productCd: 'P1', lotNo: 'L1', serialNo: 'S1', qty: 1 }],
    })
    expect(buildLpnLifecycleCommand('split', 'rv1', lpnForm({
      targetLpnNo: 'PALLET-02',
      targetContainerType: 'PALLET',
      serialNosText: 'S1',
    }))).toMatchObject({
      targetLpnNo: 'PALLET-02',
      targetContainerType: 'PALLET',
      serialNos: ['S1'],
    })
    expect(buildLpnLifecycleCommand('merge', 'rv1', lpnForm({
      sourceLpnNo: 'PALLET-02',
    }))).toMatchObject({ sourceLpnNo: 'PALLET-02' })
  })

  it('blocks no-op pack, unpack, and split commands', () => {
    for (const action of ['pack', 'unpack', 'split'] as const) {
      expect(() => buildLpnLifecycleCommand(action, 'rv1', lpnForm({
        targetLpnNo: 'TARGET',
        targetContainerType: 'PALLET',
      }))).toThrow(ProductionFormValidationError)
    }
  })
})
