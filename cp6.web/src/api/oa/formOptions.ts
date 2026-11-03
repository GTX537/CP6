import http from '../http'
export interface FormOptionSource { owner: 'Main.Sys.DictionaryOptions.v1'; scope: 'SHARED'; typeId: number }
export interface FormOptionContext { kind: 'FORM' | 'DRAFT' | 'RETURNED' | 'TASK'; formVersionId: string; key: string | null; id: string | null }
export interface FormOptionPage { items: { value: string; label: string }[]; page: number; hasMore: boolean; scope: 'SHARED' }
const unwrap = <T>(v: any): T => v?.data ?? v
export const formOptionApi = {
  types: async (search: string, page: number) => unwrap<{ id: number; name: string }[]>(await http.get('/oa/v2/form-option-sources', { params: { search, page } })),
  query: async (context: FormOptionContext, fieldPath: string[], search: string, page: number) => unwrap<FormOptionPage>(await http.post('/oa/v2/form-options/query', { context, fieldPath, search, page })),
}
