import http from '../http'

export type LanguageScope = 'global' | 'tenant'
export type LanguageField = 'ZhCN' | 'ZhTW' | 'En' | 'Ja' | 'Ko'
export type Translations = Partial<Record<LanguageField, string | null>>

export interface LanguageEntry {
  id: number
  langKey: string
  status: 'draft' | 'reviewed'
  everReviewed: boolean
  version: string
  translations: Translations
}

export interface LanguageEdition {
  kind: 'create' | 'update' | 'delete'
  id?: number
  expectedVersion?: string
  langKey?: string
  translations?: Translations
  reason: string
}

export interface PublicationPreview {
  revision: string
  currentVersion: string | null
  sourceVersion: string
  languages: string[]
  namespaces: string[]
  count: number
  missing: { code: string; count: number }[]
  versions: string[]
}

export interface PublicationCommand {
  operationRef: string
  kind: 'publish' | 'rollback'
  expectedRevision: string
  sourceVersion?: string
  version?: string
  reason: string
}

export interface PublicationReceipt {
  operationRef: string
  kind: PublicationCommand['kind']
  revision: string
  version: string
  appliedAtUtc: string
}

const entryPath = (scope: LanguageScope) => `/lang/entries-v1/${scope}`
const publication = '/lang/publication-v1'

export const langApi = {
  list(scope: LanguageScope, params: { page: number; pageSize: number; keyword?: string }) {
    return http.get<unknown, { rows: LanguageEntry[]; total: number; fields: { name: LanguageField; access: number }[] }>(
      entryPath(scope), { params })
  },
  save(scope: LanguageScope, command: LanguageEdition) {
    return http.post(`${entryPath(scope)}/commands`, command)
  },
  review(scope: LanguageScope, entries: Pick<LanguageEntry, 'id' | 'version'>[], reason: string) {
    return http.post(`${entryPath(scope)}/review`, { entries, reason })
  },
  export(scope: LanguageScope, fields: LanguageField[]) {
    const query = new URLSearchParams()
    fields.forEach(field => query.append('fields', field))
    return http.get(`${entryPath(scope)}/export`, { params: query })
  },
  import(scope: LanguageScope, commands: LanguageEdition[]) {
    return http.post(`${entryPath(scope)}/import`, commands)
  },
  preview() { return http.get<unknown, PublicationPreview>(publication) },
  apply(command: PublicationCommand) { return http.post<unknown, PublicationReceipt>(`${publication}/commands`, command) },
  recover(operation: string) { return http.get<unknown, PublicationReceipt>(`${publication}/operations/${operation}`) },
}
