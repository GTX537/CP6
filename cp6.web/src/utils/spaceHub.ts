import * as signalR from '@microsoft/signalr'

/**
 * Space SignalR Hub クライアント（独立 connection）
 *
 * /hubs/space に接続して以下イベントを購読：
 *   LocationPublished … 库位発布/停用が完了（{ batchNo, count, status }）
 *
 * 既存の /hubs/notify・/hubs/mes・/hubs/wms と完全独立 — Hub ごとに connection 分離。
 * 照 wmsHub.ts：cookie 隐式認証（accessTokenFactory 無し）＋ withAutomaticReconnect ＋ 状態守卫。
 * グループ無し（Space イベントは低頻・全播）。
 */

export interface LocationPublishedPayload {
  batchNo: string
  count: number
  status: string
}

let spaceConn: signalR.HubConnection | null = null

export function getSpaceConnection(): signalR.HubConnection {
  if (!spaceConn) {
    spaceConn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/space')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return spaceConn
}

export async function startSpaceConnection() {
  const c = getSpaceConnection()
  if (c.state === signalR.HubConnectionState.Disconnected) {
    try {
      await c.start()
      console.log('[Space-Hub] Connected')
    } catch (err) {
      console.warn('[Space-Hub] Connection failed, will retry:', err)
    }
  }
  return c
}

export function stopSpaceConnection() {
  if (spaceConn) {
    spaceConn.stop()
    spaceConn = null
  }
}

/** LocationPublished 購読登録（singleton connection 上に .on） */
export function onLocationPublished(cb: (payload: LocationPublishedPayload) => void) {
  getSpaceConnection().on('LocationPublished', cb)
}

/** LocationPublished 購読解除（onUnmounted 時のクリーンアップ） */
export function offLocationPublished(cb: (payload: LocationPublishedPayload) => void) {
  getSpaceConnection().off('LocationPublished', cb)
}
