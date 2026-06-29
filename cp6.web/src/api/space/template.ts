import http from '../http'
import type { Envelope, TemplateVO } from '@/types/space/scene'

export const templateApi = {
  list() {
    return http.get<any, Envelope<TemplateVO[]>>('/space/template')
  },
  get(id: string) {
    return http.get<any, Envelope<TemplateVO>>(`/space/template/${id}`)
  },
  create(data: Partial<TemplateVO>) {
    return http.post<any, Envelope<TemplateVO>>('/space/template', data)
  },
  update(id: string, data: Partial<TemplateVO>) {
    return http.put<any, Envelope<TemplateVO>>(`/space/template/${id}`, data)
  },
  remove(id: string) {
    return http.delete<any, Envelope<void>>(`/space/template/${id}`)
  },
  clone(id: string) {
    return http.post<any, Envelope<TemplateVO>>(`/space/template/${id}/clone`)
  },
}
