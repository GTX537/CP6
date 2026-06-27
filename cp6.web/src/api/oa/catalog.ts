import http from '../http'

export const catalogApi = {
  tree:     ()                          => http.get('/oa/catalog/tree'),
  favorite: (formKey: string, on: boolean) => http.post('/oa/catalog/favorite', { formKey, on }),
}
