import http from '../http'

export interface CodeGenTableInput {
  entityName: string
  module: string
  tableName: string
  resourceKey: string
  seqBizKey: string
  codeField: string
  routePath: string
}

export interface CodeGenColumnInput {
  name: string
  clrType: string
  label: string
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

export const codeGenApi = {
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
}
