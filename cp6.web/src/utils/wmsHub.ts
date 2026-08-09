import * as signalR from '@microsoft/signalr'

/**
 * WMS SignalR Hub クライアント（独立 connection）
 *
 * /hubs/wms に接続して以下イベントを購読：
 *   StockChanged / InboundReceived / OutboundShipped / StockTakeCompleted
 *
 * 既存の /hubs/notify（汎用）/ /hubs/mes と完全独立 — Hub ごとに connection 分離。
 */

export interface StockChangedPayload {
  txnNo: string
  txnType: 'IN' | 'OUT' | 'MOVE' | 'ADJ' | 'RSV' | 'UNRSV'
  txnAt: string
  warehouseCd: string
  locationCd: string
  productCd: string
  lotNo: string
  qty: number
  relatedNo?: string
  operatorCd?: string
}

export interface InboundReceivedPayload {
  receiptNo: string
  warehouseCd: string
  at: string
}

export interface OutboundShippedPayload {
  outboundNo: string
  packageNo?: string
  at: string
}

export interface StockTakeCompletedPayload {
  stockTakeNo: string
  diffLines: number
  at: string
}

export interface MobileTaskEventPayload {
  taskNo: string
  taskType: 'MOVE'
  status: number
  assignedTo?: string
  warehouseCd?: string
  productCd?: string
  rowVersion: string
  at: string
}

let wmsConn: signalR.HubConnection | null = null
const warehouseRefs = new Map<string, number>()
const stateListeners = new Set<(state: signalR.HubConnectionState) => void>()
let startInFlight: Promise<void> | null = null
let startRetryTimer: ReturnType<typeof setTimeout> | null = null

function emitState(state: signalR.HubConnectionState) {
  for (const listener of stateListeners) listener(state)
}

async function restoreWarehouseSubscriptions(c: signalR.HubConnection) {
  for (const warehouseCd of warehouseRefs.keys()) {
    try { await c.invoke('SubscribeWarehouse', warehouseCd) }
    catch (err) { console.warn('[WMS-Hub] Warehouse resubscribe failed:', warehouseCd, err) }
  }
}

function clearStartRetry() {
  if (startRetryTimer) clearTimeout(startRetryTimer)
  startRetryTimer = null
}

function scheduleStartRetry() {
  if (startRetryTimer || warehouseRefs.size === 0) return
  startRetryTimer = setTimeout(() => {
    startRetryTimer = null
    void startWmsConnection()
  }, 5000)
}

export function getWmsConnection(): signalR.HubConnection {
  if (!wmsConn) {
    wmsConn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/wms')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
    wmsConn.onreconnecting(() => emitState(signalR.HubConnectionState.Reconnecting))
    wmsConn.onreconnected(async () => {
      await restoreWarehouseSubscriptions(wmsConn!)
      emitState(signalR.HubConnectionState.Connected)
    })
    wmsConn.onclose(() => {
      emitState(signalR.HubConnectionState.Disconnected)
      scheduleStartRetry()
    })
  }
  return wmsConn
}

export async function startWmsConnection() {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Disconnected) {
    if (!startInFlight) startInFlight = (async () => {
      try {
        await c.start()
        clearStartRetry()
        await restoreWarehouseSubscriptions(c)
        emitState(c.state)
        console.log('[WMS-Hub] Connected')
      } catch (err) {
        console.warn('[WMS-Hub] Connection failed, will retry:', err)
        emitState(signalR.HubConnectionState.Disconnected)
        scheduleStartRetry()
      } finally {
        startInFlight = null
      }
    })()
    await startInFlight
  }
  return c
}

export function stopWmsConnection() {
  clearStartRetry()
  startInFlight = null
  if (wmsConn) {
    wmsConn.stop()
    wmsConn = null
  }
  warehouseRefs.clear()
  stateListeners.clear()
}

/** 倉庫別 購読登録（接続中のみ即時、それ以外は内部キュー無し ─ 再接続時に再購読要） */
export async function subscribeWarehouse(warehouseCd: string) {
  if (!warehouseCd) return
  const count = warehouseRefs.get(warehouseCd) ?? 0
  warehouseRefs.set(warehouseCd, count + 1)
  if (count > 0) return
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('SubscribeWarehouse', warehouseCd)
  }
}

export async function unsubscribeWarehouse(warehouseCd: string) {
  const count = warehouseRefs.get(warehouseCd) ?? 0
  if (count > 1) { warehouseRefs.set(warehouseCd, count - 1); return }
  warehouseRefs.delete(warehouseCd)
  if (warehouseRefs.size === 0) clearStartRetry()
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('UnsubscribeWarehouse', warehouseCd)
  }
}

export function onWmsConnectionState(
  listener: (state: signalR.HubConnectionState) => void,
): () => void {
  stateListeners.add(listener)
  listener(getWmsConnection().state)
  return () => stateListeners.delete(listener)
}

export async function subscribeProduct(productCd: string) {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('SubscribeProduct', productCd)
  }
}
