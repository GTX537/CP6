import type {
  CreateLpnCommand,
  EnableSerialTrackingCommand,
  ExistingSerialInput,
  LpnContent,
  LpnLifecycleAction,
  LpnLifecycleCommand,
  SerialLifecycleCommand,
  SerialLifecycleType,
} from '@/api/wms/production'

export class ProductionFormValidationError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'ProductionFormValidationError'
  }
}

export interface SerialLifecycleForm {
  txnType: SerialLifecycleType
  productCd: string
  serialNosText: string
  warehouseCd: string
  lotNo: string
  fromLocationCd: string
  toLocationCd: string
  lpnNo: string
  deviceId: string
}

export interface SerialTrackingForm {
  productCd: string
  trackingMode: 2 | 3
  existingSerialsText: string
}

export interface LpnLifecycleForm {
  lpnNo: string
  containerType: string
  warehouseCd: string
  locationCd: string
  deviceId: string
  toLocationCd: string
  childLpnsText: string
  contentsText: string
  serialNosText: string
  targetLpnNo: string
  targetContainerType: string
  sourceLpnNo: string
}

const sourceRequired = new Set<SerialLifecycleType>([
  'PUTAWAY', 'MOVE', 'PICK', 'SHIP', 'COUNT',
])
const targetRequired = new Set<SerialLifecycleType>([
  'RECEIVE', 'PUTAWAY', 'MOVE', 'RETURN',
])

function required(value: string, label: string, maxLength = 128) {
  const normalized = value.trim()
  if (!normalized) throw new ProductionFormValidationError(`${label} is required`)
  if (normalized.length > maxLength)
    throw new ProductionFormValidationError(`${label} must be ${maxLength} characters or fewer`)
  return normalized
}

function optional(value: string, maxLength = 128) {
  const normalized = value.trim()
  if (normalized.length > maxLength)
    throw new ProductionFormValidationError(`Optional values must be ${maxLength} characters or fewer`)
  return normalized || undefined
}

export function parseUniqueLines(value: string, label: string) {
  const values = value
    .split(/\r?\n/)
    .map(item => item.trim())
    .filter(Boolean)
  const seen = new Set<string>()
  for (const item of values) {
    const key = item.toLocaleUpperCase()
    if (seen.has(key))
      throw new ProductionFormValidationError(`${label} contains a duplicate value: ${item}`)
    seen.add(key)
  }
  return values
}

function csvColumns(line: string, expected: number, lineNumber: number, label: string) {
  const values = line.split(',').map(value => value.trim())
  if (values.length !== expected)
    throw new ProductionFormValidationError(
      `${label} line ${lineNumber} must contain ${expected} comma-separated values`,
    )
  return values
}

export function parseExistingSerials(value: string): ExistingSerialInput[] {
  const rows = value.split(/\r?\n/).map(line => line.trim()).filter(Boolean)
  const seen = new Set<string>()
  return rows.map((line, index) => {
    const [serialNoValue, warehouseValue, locationValue, lotValue] =
      csvColumns(line, 4, index + 1, 'Existing serials')
    const serialNo = required(serialNoValue ?? '', `Serial on line ${index + 1}`)
    const key = serialNo.toLocaleUpperCase()
    if (seen.has(key))
      throw new ProductionFormValidationError(`Existing serials contains a duplicate value: ${serialNo}`)
    seen.add(key)
    return {
      serialNo,
      warehouseCd: required(warehouseValue ?? '', `Warehouse on line ${index + 1}`),
      locationCd: required(locationValue ?? '', `Location on line ${index + 1}`),
      lotNo: lotValue?.trim() ?? '',
    }
  })
}

export function parseLpnContents(value: string): LpnContent[] {
  const rows = value.split(/\r?\n/).map(line => line.trim()).filter(Boolean)
  const serialKeys = new Set<string>()
  return rows.map((line, index) => {
    const [productValue, lotValue, serialValue, qtyValue] =
      csvColumns(line, 4, index + 1, 'Contents')
    const productCd = required(productValue ?? '', `Product on line ${index + 1}`)
    const serialNo = serialValue?.trim() || undefined
    const quantityText = qtyValue?.trim() ?? ''
    const qty = quantityText ? Number(quantityText) : serialNo ? 1 : Number.NaN
    if (!Number.isFinite(qty) || qty <= 0)
      throw new ProductionFormValidationError(`Quantity on line ${index + 1} must be positive`)
    if (serialNo && qty !== 1)
      throw new ProductionFormValidationError(`Serialized content on line ${index + 1} must have quantity 1`)
    if (serialNo) {
      const key = `${productCd}\u0000${serialNo}`.toLocaleUpperCase()
      if (serialKeys.has(key))
        throw new ProductionFormValidationError(
          `Contents contains duplicate product/serial: ${productCd} / ${serialNo}`,
        )
      serialKeys.add(key)
    }
    return {
      productCd,
      lotNo: lotValue?.trim() ?? '',
      serialNo,
      qty,
    }
  })
}

export function buildSerialLifecycleCommand(form: SerialLifecycleForm): SerialLifecycleCommand {
  const productCd = required(form.productCd, 'Product')
  const warehouseCd = required(form.warehouseCd, 'Warehouse')
  const serialNos = parseUniqueLines(form.serialNosText, 'Serial numbers')
  if (serialNos.length === 0)
    throw new ProductionFormValidationError('At least one serial number is required')
  const fromLocationCd = optional(form.fromLocationCd)
  const toLocationCd = optional(form.toLocationCd)
  if (sourceRequired.has(form.txnType) && !fromLocationCd)
    throw new ProductionFormValidationError(`Source location is required for ${form.txnType}`)
  if (targetRequired.has(form.txnType) && !toLocationCd)
    throw new ProductionFormValidationError(`Target location is required for ${form.txnType}`)
  if (form.txnType === 'MOVE' && fromLocationCd === toLocationCd)
    throw new ProductionFormValidationError('Source and target locations must be different')
  return {
    txnType: form.txnType,
    productCd,
    serialNos,
    warehouseCd,
    lotNo: form.lotNo.trim(),
    fromLocationCd,
    toLocationCd,
    lpnNo: optional(form.lpnNo, 64),
    deviceId: optional(form.deviceId),
  }
}

export function buildSerialTrackingCommand(form: SerialTrackingForm): EnableSerialTrackingCommand {
  if (form.trackingMode !== 2 && form.trackingMode !== 3)
    throw new ProductionFormValidationError('Tracking mode must include serial tracking')
  return {
    productCd: required(form.productCd, 'Product'),
    trackingMode: form.trackingMode,
    existingSerials: parseExistingSerials(form.existingSerialsText),
  }
}

export function buildCreateLpnCommand(form: LpnLifecycleForm): CreateLpnCommand {
  return {
    lpnNo: required(form.lpnNo, 'LPN', 64),
    containerType: required(form.containerType, 'Container type'),
    warehouseCd: required(form.warehouseCd, 'Warehouse'),
    locationCd: required(form.locationCd, 'Location'),
    deviceId: optional(form.deviceId),
  }
}

export function buildLpnLifecycleCommand(
  action: LpnLifecycleAction,
  rowVersion: string,
  form: LpnLifecycleForm,
): LpnLifecycleCommand {
  const base = {
    rowVersion: required(rowVersion, 'Current row version', 2048),
    deviceId: optional(form.deviceId),
  }
  if (action === 'move') {
    return {
      ...base,
      toLocationCd: required(form.toLocationCd, 'Target location'),
    }
  }
  if (action === 'pack') {
    const childLpns = parseUniqueLines(form.childLpnsText, 'Child LPNs')
    const contents = parseLpnContents(form.contentsText)
    if (childLpns.length === 0 && contents.length === 0)
      throw new ProductionFormValidationError('Pack requires a child LPN or a content line')
    return { ...base, childLpns, contents }
  }
  if (action === 'unpack') {
    const childLpns = parseUniqueLines(form.childLpnsText, 'Child LPNs')
    const serialNos = parseUniqueLines(form.serialNosText, 'Serial numbers')
    if (childLpns.length === 0 && serialNos.length === 0)
      throw new ProductionFormValidationError('Unpack requires a child LPN or serial number')
    return { ...base, childLpns, serialNos }
  }
  if (action === 'split') {
    const childLpns = parseUniqueLines(form.childLpnsText, 'Child LPNs')
    const serialNos = parseUniqueLines(form.serialNosText, 'Serial numbers')
    if (childLpns.length === 0 && serialNos.length === 0)
      throw new ProductionFormValidationError('Split requires a child LPN or serial number')
    return {
      ...base,
      targetLpnNo: required(form.targetLpnNo, 'Target LPN', 64),
      targetContainerType: required(form.targetContainerType, 'Target container type'),
      serialNos,
      childLpns,
    }
  }
  return {
    ...base,
    sourceLpnNo: required(form.sourceLpnNo, 'Source LPN', 64),
  }
}
