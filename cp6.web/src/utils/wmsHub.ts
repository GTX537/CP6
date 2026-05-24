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

let wmsConn: signalR.HubConnection | null = null

export function getWmsConnection(): signalR.HubConnection {
  if (!wmsConn) {
    wmsConn = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/wms')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return wmsConn
}

export async function startWmsConnection() {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Disconnected) {
    try {
      await c.start()
      console.log('[WMS-Hub] Connected')
    } catch (err) {
      console.warn('[WMS-Hub] Connection failed, will retry:', err)
    }
  }
  return c
}

export function stopWmsConnection() {
  if (wmsConn) {
    wmsConn.stop()
    wmsConn = null
  }
}

/** 倉庫別 購読登録（接続中のみ即時、それ以外は内部キュー無し ─ 再接続時に再購読要） */
export async function subscribeWarehouse(warehouseCd: string) {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('SubscribeWarehouse', warehouseCd)
  }
}

export async function subscribeProduct(productCd: string) {
  const c = getWmsConnection()
  if (c.state === signalR.HubConnectionState.Connected) {
    await c.invoke('SubscribeProduct', productCd)
  }
}
