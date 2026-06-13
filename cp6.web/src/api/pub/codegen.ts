import http from '../http'

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
}
