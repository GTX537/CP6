import http from '../http'

interface DictionaryType {
  id: number
  typeCode: string
  typeName: string
  orderNo: number
  enable: boolean
}

interface DictionaryItem {
  id: number
  value: string
  label: string
  orderNo: number
  enable: boolean
  selectable: boolean
}

interface DictionaryOptions {
  dictionaryVersion: string
  type: DictionaryType | null
  unavailableReason: string | null
  allItems: DictionaryItem[]
}

export interface DictionaryChange {
  operationRef: string
  kind: 'create-type' | 'update-type' | 'delete-type' | 'create-data' | 'update-data' | 'delete-data'
  typeCode: string
  expectedVersion: string
  id?: number
  value?: string
  label?: string
  enable?: boolean
  orderNo?: number
  reason: string
}

export interface DictionaryReceipt {
  operationRef: string
  dictionaryVersion: string
  typeCode: string
}

const owner = '/dict/owner-v1'

export const dictionaryApi = {
  types(params: { page: number; pageSize: number; keyword?: string }) {
    return http.get<unknown, { rows: DictionaryType[]; total: number }>('/dict/types', { params })
  },
  options(typeCode: string) {
    return http.get<unknown, DictionaryOptions>(`${owner}/options/${encodeURIComponent(typeCode)}`)
  },
  save(change: DictionaryChange) {
    return http.post<unknown, DictionaryReceipt>(`${owner}/commands`, change)
  },
  recover(operation: string) {
    return http.get<unknown, DictionaryReceipt>(`${owner}/operations/${operation}`)
  },
}

export function getDictOptions(typeCode: string) {
  return http.get(`/dict/options/${encodeURIComponent(typeCode)}`)
}
