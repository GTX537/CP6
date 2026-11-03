import http from '../http'
export interface FormFileScope { kind: 'FORM' | 'DRAFT' | 'RETURNED' | 'HISTORY' | 'TASK'; key: string | null; id: string | null; formVersionId: string; expectedVersion?: string | null; expectedDataVersion?: string | null }
export interface FormFileReadContext { scope?: FormFileScope; instanceId?: string; queryDataId?: string }
export interface FormFileView { reference: string; fileName: string; contentRef: { id: string; digest: string; byteLength: number; mediaType: string }; stateVersion: string; inspection: { status: string; provider: string; reason: string }; held: boolean; availability: string }
export interface FormFileOperation { operationId: string; state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'; result?: FormFileView }
export interface FormFileUpload { operationId: string; fileId: string; scope: FormFileScope; field: string; fileName: string; byteLength: number; digest: string }
export interface FormFileCommand { operationId: string; scope: FormFileScope; field: string; reference: string; expectedStateVersion: string; action: string; reason: string }
const root = '/oa/v2/personal-forms/files', unwrap = <T>(v: any): T => v?.data ?? v
function readRequest(context: FormFileReadContext, field: string, reference: string, action: string) {
  if (context.queryDataId) return { url: `/oa/v2/form-data/${encodeURIComponent(context.queryDataId)}/files/${action}`, data: { field, reference } }
  if (context.instanceId) return { url: `/oa/v2/inbox/instances/${encodeURIComponent(context.instanceId)}/files/${action}`, data: { field, reference } }
  if (context.scope) return { url: `${root}/${action}`, data: { scope: context.scope, field, reference } }
  throw new Error('附件所属记录尚未读取')
}
function encodeHeader(value: object) {
  const bytes = new TextEncoder().encode(JSON.stringify(value)); let text = ''
  for (const byte of bytes) text += String.fromCharCode(byte)
  return btoa(text)
}
export const formFileApi = {
  upload: async (command: FormFileUpload, file: File, signal?: AbortSignal) => unwrap<FormFileOperation>(await http.post(`${root}/uploads`, file, { signal, timeout: 180000, headers: { 'Content-Type': 'application/octet-stream', 'X-File-Command': encodeHeader(command) } })),
  command: async (command: FormFileCommand, signal?: AbortSignal) => unwrap<FormFileOperation>(await http.post(`${root}/commands`, command, { signal, timeout: 180000 })),
  original: async (id: string, signal?: AbortSignal) => unwrap<FormFileOperation>(await http.get(`${root}/operations/${encodeURIComponent(id)}`, { signal })),
  close: async (id: string, signal?: AbortSignal) => unwrap<FormFileOperation>(await http.post(`${root}/operations/${encodeURIComponent(id)}/close`, undefined, { signal })),
  read: async (context: FormFileReadContext, field: string, reference: string, signal?: AbortSignal) => {
    const r = readRequest(context, field, reference, 'read'); return unwrap<FormFileView>(await http.post(r.url, r.data, { signal }))
  },
  download: async (context: FormFileReadContext, field: string, reference: string, signal?: AbortSignal): Promise<Blob> => {
    const r = readRequest(context, field, reference, 'download'); return await http.post(r.url, r.data, { signal, responseType: 'blob' }) as unknown as Blob
  },
}
