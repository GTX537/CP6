import http from '../http'
import type {
  MobileTask, MobileTaskQuery, PagedResult,
  CreateMoveTaskRequest, AssignTaskRequest,
  MobileTaskEvent, TaskCommand,
} from '@/types/wms/wms'

// ───────── モバイル作業指示（WM300） ─────────

export const mobileApi = {
  tasks(q: MobileTaskQuery = {}) {
    return http.get<any, PagedResult<MobileTask>>('/v2/wms/tasks', { params: q })
  },
  get(no: string) {
    return http.get<any, MobileTask>(`/v2/wms/tasks/${encodeURIComponent(no)}`)
  },
  events(no: string) {
    return http.get<any, MobileTaskEvent[]>(`/v2/wms/tasks/${encodeURIComponent(no)}/events`)
  },
  create(dto: CreateMoveTaskRequest) {
    return http.post<any, MobileTask>('/v2/wms/tasks', withOperation(dto))
  },
  assign(no: string, request: AssignTaskRequest) {
    return http.post<any, MobileTask>(`/v2/wms/tasks/${encodeURIComponent(no)}/assign`, withOperation(request))
  },
  pause(no: string, request: TaskCommand & { reason: string }) {
    return command(no, 'pause', request)
  },
  release(no: string, request: TaskCommand & { reason: string }) {
    return command(no, 'release', request)
  },
  takeover(no: string, request: TaskCommand & { assignedTo: string, reason: string }) {
    return command(no, 'takeover', request)
  },
  exception(no: string, request: TaskCommand & { reasonCode: string, description: string }) {
    return command(no, 'exception', request)
  },
  resolveException(no: string, request: TaskCommand & {
    action: 'RESUME' | 'REASSIGN' | 'ADJUST' | 'CANCEL'
    assignedTo?: string
    qty?: number
    toLocationCd?: string
    remarks?: string
  }) {
    return command(no, 'resolve-exception', request)
  },
  cancel(no: string, rowVersion: string, reason = '') {
    return http.post<any, MobileTask>(
      `/v2/wms/tasks/${encodeURIComponent(no)}/cancel`,
      withOperation({ rowVersion, reason }),
    )
  },
}

function command<T extends TaskCommand>(no: string, action: string, request: T) {
  return http.post<any, MobileTask>(
    `/v2/wms/tasks/${encodeURIComponent(no)}/${action}`,
    withOperation(request),
  )
}

function withOperation<T extends object>(request: T): T & { operationId: string } {
  const existing = (request as { operationId?: string }).operationId
  return {
    ...request,
    operationId: existing || newOperationId(),
  }
}

function newOperationId() {
  return globalThis.crypto?.randomUUID?.()
    ?? 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, char => {
      const random = Math.floor(Math.random() * 16)
      const value = char === 'x' ? random : (random & 0x3) | 0x8
      return value.toString(16)
    })
}
