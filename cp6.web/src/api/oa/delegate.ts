import http from '../http'

export const delegateApi = {
  myGrants: ()          => http.get('/oa/delegate/my-grants'),
  list:     ()          => http.get('/oa/delegate/list'),
  add:      (body: unknown) => http.post('/oa/delegate/add', body),
  remove:   (id: string)    => http.post('/oa/delegate/remove', { id }),
}
