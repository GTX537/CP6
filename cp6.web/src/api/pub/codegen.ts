import http from '../http'

export interface CodeGenTableInput {
  entityName: string
  module: string
  tableName: string
  resourceKey: string
  seqBizKey: string | null
  codeField: string | null
  routePath: string
  menuName?: string | null
}

export interface CodeGenColumnInput {
  name: string
  clrType: string
  label: string | null
  required: boolean
  inList: boolean
  inForm: boolean
  sort: number
}

export interface CodeGenPreview {
  schemaVersion: '1.0'
  templateRef: string
  templateVersion: string
  inputDigest: string
  applied: false
  outputs: { fileName: string; language: string; content: string }[]
}

export interface CodeGenSourceFile { targetPath: string; content: string }
export interface CodeGenMetadataDocument {
  table: CodeGenTableInput
  columns: CodeGenColumnInput[]
  templateRef: string
  templateVersion: string
}
export interface CodeGenMetadataSave { operationRef: string; expectedRevision: number; document: CodeGenMetadataDocument }
export interface CodeGenMetadataSnapshot {
  metadataId: string; tenantId: string; revision: number; document: CodeGenMetadataDocument
  savedBy: string; savedAtUtc: string
}
export interface CodeGenMetadataReceipt { operationRef: string; snapshot: CodeGenMetadataSnapshot }
export interface CodeGenComparison {
  schemaVersion: 'codegen-comparison-v1'
  templateRef: string
  templateVersion: string
  inputDigest: string
  comparisonDigest: string
  applied: false
  outputs: {
    fileName: string
    targetPath: string
    language: string
    status: 'NoBaseline' | 'Unchanged' | 'OverwriteAllowed' | 'OverwriteBlocked' | 'CustomBlockConflict'
    reason: string | null
    preservedCustomBlocks: number
    content: string | null
    difference: { startLine: number; removedLines: string[]; addedLines: string[] } | null
  }[]
}

export const codeGenApi = {
  async metadata(id: string): Promise<CodeGenMetadataSnapshot> {
    const res = await http.get<unknown, { data: CodeGenMetadataSnapshot }>(`/pub/codegen/metadata-v1/${encodeURIComponent(id)}`)
    return res.data
  },
  async saveMetadata(id: string, command: CodeGenMetadataSave): Promise<CodeGenMetadataReceipt> {
    const res = await http.post<unknown, { data: CodeGenMetadataReceipt }>(`/pub/codegen/metadata-v1/${encodeURIComponent(id)}/saves`, command)
    return res.data
  },
  async originalMetadata(id: string, operationRef: string): Promise<CodeGenMetadataReceipt> {
    const res = await http.post<unknown, { data: CodeGenMetadataReceipt }>(`/pub/codegen/metadata-v1/${encodeURIComponent(id)}/saves/read`, { operationRef })
    return res.data
  },
  async tables(): Promise<any[]> {
    const res: any = await http.get('/pub/codegen/tables')
    return res.data ?? []
  },
  save(table: any, columns: any[]) {
    return http.post('/pub/codegen/save', { table, columns })
  },
  async previewInline(table: any, columns: any[]): Promise<Record<string, string>> {
    const res: any = await http.post('/pub/codegen/preview', { table, columns })
    return res.data ?? {}
  },
  async previewV1(table: CodeGenTableInput, columns: CodeGenColumnInput[]): Promise<CodeGenPreview> {
    const res = await http.post<unknown, { code: number; data: CodeGenPreview }>('/pub/codegen/preview/v1', { table, columns })
    return res.data
  },
  async compare(table: CodeGenTableInput, columns: CodeGenColumnInput[], template: Pick<CodeGenPreview, 'templateRef' | 'templateVersion'>,
    existingFiles: CodeGenSourceFile[], overwritePaths: string[]): Promise<CodeGenComparison> {
    const res = await http.post<unknown, { code: number; data: CodeGenComparison }>('/pub/codegen/comparison/v1', {
      table, columns, templateRef: template.templateRef, templateVersion: template.templateVersion, existingFiles, overwritePaths,
    })
    return res.data
  },
}
