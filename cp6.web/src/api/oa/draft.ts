import http from '../http'

export const draftApi = {
  list:   () => http.get('/oa/draft/list'),
  save:   (flowKey: string, varsJson: string) => http.post('/oa/draft/save', { flowKey, varsJson }),
  update: (id: string, varsJson: string) => http.post('/oa/draft/update', { id, varsJson }),
  submit: (id: string) => http.post('/oa/draft/submit', { id }),
  remove: (id: string) => http.post('/oa/draft/delete', { id }),
}
