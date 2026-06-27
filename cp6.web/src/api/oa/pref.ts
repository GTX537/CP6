import http from '../http'

export const prefApi = {
  get:  ()                     => http.get('/oa/pref/get'),
  save: (prefsJson: string)    => http.post('/oa/pref/save', { prefsJson }),
}
