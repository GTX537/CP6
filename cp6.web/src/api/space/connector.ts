import http from '../http'
import type { Envelope } from '@/types/space/scene'
import type { ConnectorVO, ConnectorCreate, ConnectorStopVO } from '@/types/space/connector'

export const connectorApi = {
  listBySite(siteId: string) {
    return http.get<unknown, Envelope<ConnectorVO[]>>(`/space/site/${siteId}/connector`)
  },
  create(d: ConnectorCreate) {
    return http.post<unknown, Envelope<{ id: string }>>(`/space/connector`, d)
  },
  upsertStop(id: string, s: ConnectorStopVO) {
    return http.put<unknown, Envelope<null>>(`/space/connector/${id}/stop`, s)
  },
  deleteStop(id: string, floorId: string) {
    return http.delete<unknown, Envelope<null>>(`/space/connector/${id}/stop/${floorId}`)
  },
  remove(id: string) {
    return http.delete<unknown, Envelope<null>>(`/space/connector/${id}`)
  },
}
