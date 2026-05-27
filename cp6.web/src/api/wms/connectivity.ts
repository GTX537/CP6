import http from '../http'
import type {
  WcsTask, WcsTaskSearchQuery,
  CarrierShipment, CarrierSearchQuery, CarrierEvent,
  IotSensor, IotSensorReading, IotAlert,
  WmsApi,
} from '@/types/wms'

// ───────── WCS タスク ─────────

export const wcsApi = {
  search(q: WcsTaskSearchQuery = {}) {
    return http.get<any, WmsApi<WcsTask[]>>('/wms/wcs-task', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<WcsTask>>(`/wms/wcs-task/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ taskNo: string }>>('/wms/wcs-task', dto)
  },
  dispatch(no: string, deviceCd: string) {
    return http.post<any, WmsApi<void>>(`/wms/wcs-task/${encodeURIComponent(no)}/dispatch`, { deviceCd })
  },
  start(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/wcs-task/${encodeURIComponent(no)}/start`)
  },
  complete(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/wcs-task/${encodeURIComponent(no)}/complete`)
  },
  fail(no: string, errorMessage: string) {
    return http.post<any, WmsApi<void>>(`/wms/wcs-task/${encodeURIComponent(no)}/fail`, { errorMessage })
  },
  delete(no: string) {
    return http.delete<any, WmsApi<void>>(`/wms/wcs-task/${encodeURIComponent(no)}`)
  },
}

// ───────── Carrier 配送業者 ─────────

export const carrierApi = {
  search(q: CarrierSearchQuery = {}) {
    return http.get<any, WmsApi<CarrierShipment[]>>('/wms/carrier', { params: q })
  },
  get(no: string) {
    return http.get<any, WmsApi<CarrierShipment>>(`/wms/carrier/${encodeURIComponent(no)}`)
  },
  create(dto: any) {
    return http.post<any, WmsApi<{ shipmentNo: string }>>('/wms/carrier', dto)
  },
  addEvent(no: string, evt: Partial<CarrierEvent>) {
    return http.post<any, WmsApi<void>>(`/wms/carrier/${encodeURIComponent(no)}/event`, evt)
  },
  pickUp(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/carrier/${encodeURIComponent(no)}/pickup`)
  },
  inTransit(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/carrier/${encodeURIComponent(no)}/in-transit`)
  },
  delivered(no: string) {
    return http.post<any, WmsApi<void>>(`/wms/carrier/${encodeURIComponent(no)}/delivered`)
  },
  fail(no: string, reason: string) {
    return http.post<any, WmsApi<void>>(`/wms/carrier/${encodeURIComponent(no)}/fail`, { reason })
  },
}

// ───────── IoT センサ ─────────

export const iotApi = {
  searchSensors(warehouseCd?: string, sensorType?: string) {
    return http.get<any, WmsApi<IotSensor[]>>('/wms/iot/sensors', { params: { warehouseCd, sensorType } })
  },
  getSensor(id: string) {
    return http.get<any, WmsApi<IotSensor>>(`/wms/iot/sensors/${encodeURIComponent(id)}`)
  },
  createSensor(dto: any) {
    return http.post<any, WmsApi<{ sensorId: string }>>('/wms/iot/sensors', dto)
  },
  updateSensor(id: string, dto: any) {
    return http.put<any, WmsApi<void>>(`/wms/iot/sensors/${encodeURIComponent(id)}`, dto)
  },
  deleteSensor(id: string) {
    return http.delete<any, WmsApi<void>>(`/wms/iot/sensors/${encodeURIComponent(id)}`)
  },
  getReadings(id: string, from?: string, to?: string, limit = 200) {
    return http.get<any, WmsApi<IotSensorReading[]>>(`/wms/iot/sensors/${encodeURIComponent(id)}/readings`, { params: { from, to, limit } })
  },
  postReading(id: string, value: number, readAt?: string) {
    return http.post<any, WmsApi<void>>(`/wms/iot/sensors/${encodeURIComponent(id)}/readings`, { value, readAt })
  },
  simulate(count = 1) {
    return http.post<any, WmsApi<{ generated: number }>>('/wms/iot/simulate', null, { params: { count } })
  },
  alerts() {
    return http.get<any, WmsApi<IotAlert[]>>('/wms/iot/alerts')
  },
}
