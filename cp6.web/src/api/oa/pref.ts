import http from '../http'

export const prefApi = {
  get:  ()                     => http.get('/oa/pref/get'),
  save: (prefsJson: string)    => http.post('/oa/pref/save', { prefsJson }),
  /** 服务端顶层键合并写（保他键；值 null=删键恢复默认） */
  saveMerge: (partialJson: string) => http.post('/oa/pref/save', { prefsJson: partialJson, merge: true }),
  /** 通知矩阵元数据（类型轴 + 通道支持标志） */
  notifyMatrix: () => http.get('/oa/pref/notify-matrix'),
}
